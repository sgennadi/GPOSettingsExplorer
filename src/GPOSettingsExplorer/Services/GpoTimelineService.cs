using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GPOSettingsExplorer.Models;

namespace GPOSettingsExplorer.Services;

/// <summary>
/// Explicit, current-user-DPAPI-encrypted local snapshots. Stored values are
/// represented by SHA-256, never written as plaintext. No remote upload and
/// no automatic GPO rollback.
/// </summary>
public sealed record GpoTimelineEntry(string Key, string Fingerprint, int Count);
public sealed record GpoTimelineSnapshot(
    Guid GpoId, string GpoName, string Domain, string Source,
    DateTimeOffset CapturedUtc, bool Partial, string Coverage,
    IReadOnlyList<GpoTimelineEntry> Entries);

public sealed record GpoTimelineDifference(string Kind, string Setting);

public static class GpoTimelineService
{
    private const int MaxSnapshotsPerGpo = 40;
    private const int MaxSnapshotFileBytes = 16 * 1024 * 1024;

    public static GpoTimelineSnapshot Capture(RealSettingsScanResult source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var entries = source.Rows
            .GroupBy(Identity, StringComparer.Ordinal)
            .Select(group => new GpoTimelineEntry(group.Key,
                Hash(string.Join("\0", group
                    .Select(row => Hash(row.State + "\0" + row.ValueType +
                                        "\0" + row.Value))
                    .OrderBy(value => value, StringComparer.Ordinal))),
                group.Count()))
            .OrderBy(row => row.Key, StringComparer.Ordinal)
            .ToArray();
        return new GpoTimelineSnapshot(source.GpoId, source.GpoName,
            source.Domain, source.DomainController, DateTimeOffset.UtcNow,
            source.IsPartial, source.Coverage, entries);
    }

    public static IReadOnlyList<GpoTimelineDifference> Compare(
        GpoTimelineSnapshot before, GpoTimelineSnapshot after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        var older = before.Entries.ToDictionary(e => e.Key, StringComparer.Ordinal);
        var newer = after.Entries.ToDictionary(e => e.Key, StringComparer.Ordinal);
        var result = new List<GpoTimelineDifference>();
        foreach (var key in older.Keys.Union(newer.Keys, StringComparer.Ordinal)
                     .OrderBy(key => key, StringComparer.Ordinal))
        {
            if (!older.TryGetValue(key, out var a))
                result.Add(new("Present only in newer source", key));
            else if (!newer.TryGetValue(key, out var b))
                result.Add(new("Present only in older source", key));
            else if (a.Fingerprint != b.Fingerprint || a.Count != b.Count)
                result.Add(new("Stored value fingerprint changed", key));
        }
        return result;
    }

    public static string Save(GpoTimelineSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var directory = DirectoryFor(snapshot.Domain, snapshot.GpoId);
        Directory.CreateDirectory(directory);
        var json = JsonSerializer.Serialize(snapshot);
        var encrypted = DpapiCredentialProtector.Protect(
            json, CredentialPersistenceScope.CurrentUser);
        var path = Path.Combine(directory,
            snapshot.CapturedUtc.UtcDateTime.ToString("yyyyMMdd-HHmmss-fffffff") +
            "-" + Guid.NewGuid().ToString("N") + ".dpapi");
        var temporary = path + ".tmp";
        try
        {
            File.WriteAllText(temporary, encrypted, Encoding.ASCII);
            File.Move(temporary, path);
            foreach (var old in Directory.EnumerateFiles(directory, "*.dpapi")
                         .OrderByDescending(Path.GetFileName, StringComparer.Ordinal)
                         .Skip(MaxSnapshotsPerGpo))
                File.Delete(old);
            return path;
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    public static IReadOnlyList<string> List(string domain, Guid gpoId) =>
        Directory.Exists(DirectoryFor(domain, gpoId))
            ? Directory.EnumerateFiles(DirectoryFor(domain, gpoId), "*.dpapi")
                .OrderByDescending(Path.GetFileName, StringComparer.Ordinal)
                .Take(MaxSnapshotsPerGpo).ToArray()
            : Array.Empty<string>();

    public static GpoTimelineSnapshot Load(string path)
    {
        var stat = new FileInfo(path);
        if (!stat.Exists || stat.Length > MaxSnapshotFileBytes)
            throw new InvalidDataException("Timeline snapshot unavailable or oversized.");
        var secret = File.ReadAllText(path, Encoding.ASCII);
        var json = DpapiCredentialProtector.Unprotect(
            secret, CredentialPersistenceScope.CurrentUser);
        var result = JsonSerializer.Deserialize<GpoTimelineSnapshot>(json);
        return result is not null && result.GpoId != Guid.Empty &&
               result.Entries is not null && result.Entries.Count <= 250_000
            ? result
            : throw new InvalidDataException("Invalid or mismatched timeline format.");
    }

    private static string DirectoryFor(string domain, Guid id)
    {
        // No arbitrary domain text is used as a filesystem directory.
        var domainKey = Hash(domain.ToLowerInvariant())[..24];
        return Path.Combine(Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "GPOSettingsExplorer", "Timeline", domainKey, id.ToString("N"));
    }

    private static string Identity(RealSettingRecord row) =>
        string.Join(" | ", row.Scope, row.Category, row.SettingName,
            row.RegistryKey, row.RegistryValue);

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
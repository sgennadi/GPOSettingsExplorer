using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GPOSettingsExplorer.Models;

namespace GPOSettingsExplorer.Services;

/// <summary>
/// Review-only GitOps fingerprints using a per-user HMAC key protected by
/// Windows DPAPI. Unlike unkeyed SHA-256, an exported low-entropy value
/// fingerprint cannot be dictionary-guessed without the local HMAC key.
/// Never exposes a raw GPO name, domain name, path, setting key or value.
/// Exports made by different users are intentionally not comparable.
/// </summary>
public sealed record GpoGitOpsFingerprint(
    string PolicyKeyHmacSha256, string StoredValueHmacSha256, int DuplicateCount);

public sealed record GpoGitOpsManifest(
    string Schema, DateTimeOffset CapturedUtc,
    string KeyId, string GpoIdentityHmacSha256, string DomainIdentityHmacSha256,
    bool PartialCoverage, int SourceRecords,
    IReadOnlyList<GpoGitOpsFingerprint> Entries, string Caveat);

public static class GpoGitOpsExportService
{
    private static readonly object KeyLock = new();
    private const int KeyLength = 32;

    public static GpoGitOpsManifest Capture(RealSettingsScanResult source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var key = GetCurrentUserHmacKey();
        try
        {
            var groups = source.Rows
                .GroupBy(Identity, StringComparer.Ordinal)
                .OrderBy(group => group.Key, StringComparer.Ordinal);

            var records = new List<GpoGitOpsFingerprint>();
            foreach (var group in groups)
            {
                var values = group
                    .Select(row => Hmac(key, "row\0" + row.State + "\0" +
                                           row.ValueType + "\0" + row.Value))
                    .OrderBy(value => value, StringComparer.Ordinal);
                records.Add(new GpoGitOpsFingerprint(
                    Hmac(key, "identity\0" + group.Key),
                    Hmac(key, "values\0" + string.Join("\0", values)),
                    group.Count()));
            }

            return new GpoGitOpsManifest(
                "gposes-gitops-fingerprint-v2", DateTimeOffset.UtcNow,
                Convert.ToHexString(SHA256.HashData(key))[..16],
                Hmac(key, "gpo\0" + source.GpoId.ToString("N")),
                Hmac(key, "domain\0" + source.Domain.ToLowerInvariant()),
                source.IsPartial, source.Rows.Count, records,
                "HMAC fingerprints require this user's local DPAPI key for subsequent " +
                "comparable exports. Different users/computers are intentionally " +
                "not comparable. This is a review artifact, not an effective GPO " +
                "report or deployable GPO/Intune policy. Never run automatic writes.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    public static string ToJson(GpoGitOpsManifest report) =>
        JsonSerializer.Serialize(report, new JsonSerializerOptions
        {
            WriteIndented = true
        });

    private static string Identity(RealSettingRecord row) =>
        string.Join("\0", row.Scope, row.Category, row.SettingName,
            row.RegistryKey, row.RegistryValue);

    private static string Hmac(byte[] key, string text) =>
        Convert.ToHexString(HMACSHA256.HashData(key,
            Encoding.UTF8.GetBytes(text)));

    private static byte[] GetCurrentUserHmacKey()
    {
        lock (KeyLock)
        {
            var path = Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData),
                "GPOSettingsExplorer", "Secrets", "gitops-hmac-v1.dpapi");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            if (!File.Exists(path))
            {
                var generated = RandomNumberGenerator.GetBytes(KeyLength);
                var staging = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    var protectedValue = DpapiCredentialProtector.Protect(
                        Convert.ToBase64String(generated),
                        CredentialPersistenceScope.CurrentUser);
                    File.WriteAllText(staging, protectedValue, Encoding.ASCII);
                    // Never replace an existing key if multiple app instances
                    // are exporting at the same time.
                    try { File.Move(staging, path, overwrite: false); }
                    catch (IOException) when (File.Exists(path)) { }
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(generated);
                    try { if (File.Exists(staging)) File.Delete(staging); }
                    catch (IOException) { }
                }
            }
            var source = new FileInfo(path);
            if (!source.Exists || source.Length > 8192)
                throw new InvalidDataException("GitOps HMAC key file is missing or oversized.");
            var decoded = DpapiCredentialProtector.Unprotect(
                File.ReadAllText(path, Encoding.ASCII),
                CredentialPersistenceScope.CurrentUser);
            var key = Convert.FromBase64String(decoded);
            if (key.Length != KeyLength)
                throw new InvalidDataException("Invalid GitOps HMAC key length.");
            return key;
        }
    }
}
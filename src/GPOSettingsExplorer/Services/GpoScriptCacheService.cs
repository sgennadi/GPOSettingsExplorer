using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GPOSettingsExplorer.Models;

namespace GPOSettingsExplorer.Services;

public static class GpoScriptCacheService
{
    private sealed record CacheSnapshot(
        Guid GpoId,
        string DomainName,
        string DomainController,
        long ModificationUtcTicks,
        IReadOnlyList<GpoScriptInfo> Scripts);

    private static readonly JsonSerializerOptions JsonOptions =
        new()
        {
            WriteIndented =
                false
        };

    public static bool TryLoad(
        GpoInfo gpo,
        out IReadOnlyList<GpoScriptInfo> scripts)
    {
        scripts =
            Array.Empty<GpoScriptInfo>();

        try
        {
            var path =
                CachePath(
                    gpo);

            if (!File.Exists(
                    path))
            {
                return false;
            }

            var snapshot =
                JsonSerializer.Deserialize<CacheSnapshot>(
                    File.ReadAllText(
                        path),
                    JsonOptions);

            if (snapshot is null ||
                snapshot.GpoId !=
                gpo.Id ||
                !snapshot.DomainName.Equals(
                    gpo.DomainName,
                    StringComparison.OrdinalIgnoreCase) ||
                !snapshot.DomainController.Equals(
                    DomainConnectionState.GetServerFor(
                        gpo.DomainName),
                    StringComparison.OrdinalIgnoreCase) ||
                snapshot.ModificationUtcTicks !=
                ModificationTicks(
                    gpo))
            {
                return false;
            }

            scripts =
                snapshot.Scripts
                    .ToArray();

            return true;
        }
        catch
        {
            return false;
        }
    }

    public static void Save(
        GpoInfo gpo,
        IReadOnlyList<GpoScriptInfo> scripts)
    {
        try
        {
            Directory.CreateDirectory(
                CacheDirectory);

            var snapshot =
                new CacheSnapshot(
                    gpo.Id,
                    gpo.DomainName,
                    DomainConnectionState.GetServerFor(
                        gpo.DomainName),
                    ModificationTicks(
                        gpo),
                    scripts);

            var path =
                CachePath(
                    gpo);

            var temp =
                path +
                "." +
                Guid.NewGuid()
                    .ToString(
                        "N") +
                ".tmp";

            File.WriteAllText(
                temp,
                JsonSerializer.Serialize(
                    snapshot,
                    JsonOptions),
                new UTF8Encoding(
                    encoderShouldEmitUTF8Identifier:
                        false));

            File.Move(
                temp,
                path,
                overwrite:
                    true);
        }
        catch
        {
        }
    }

    public static void Invalidate(
        GpoInfo gpo)
    {
        try
        {
            File.Delete(
                CachePath(
                    gpo));
        }
        catch
        {
        }
    }

    private static long ModificationTicks(
        GpoInfo gpo) =>
        gpo.ModificationTime?
            .ToUniversalTime()
            .Ticks
        ?? 0;

    private static string CacheDirectory =>
        Path.Combine(
            StoragePaths.Cache,
            "GpoScripts");

    private static string CachePath(
        GpoInfo gpo)
    {
        var key =
            $"{gpo.DomainName}|{DomainConnectionState.GetServerFor(gpo.DomainName)}|{gpo.Id:B}";

        var hash =
            Convert.ToHexString(
                SHA256.HashData(
                    Encoding.UTF8.GetBytes(
                        key.ToUpperInvariant())));

        return Path.Combine(
            CacheDirectory,
            hash +
            ".json");
    }
}

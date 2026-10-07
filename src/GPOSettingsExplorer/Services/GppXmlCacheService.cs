using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;

namespace GPOSettingsExplorer.Services;

public static class GppXmlCacheService
{
    private sealed record CacheEntry(
        string Path,
        long Length,
        long LastWriteUtcTicks,
        string Xml);

    private static readonly JsonSerializerOptions JsonOptions =
        new()
        {
            WriteIndented =
                false
        };

    private static readonly object SyncRoot =
        new();

    public static XDocument Load(
        string path,
        LoadOptions options =
            LoadOptions.None)
    {
        var xml =
            ReadText(
                path);

        return XDocument.Parse(
            xml,
            options);
    }

    public static string ReadText(
        string path)
    {
        var info =
            new FileInfo(
                path);

        if (!info.Exists)
        {
            throw new FileNotFoundException(
                "The Group Policy Preferences XML file does not exist.",
                path);
        }

        var cachePath =
            CachePath(
                path);

        lock (SyncRoot)
        {
            try
            {
                if (File.Exists(
                        cachePath))
                {
                    var cached =
                        JsonSerializer.Deserialize<CacheEntry>(
                            File.ReadAllText(
                                cachePath),
                            JsonOptions);

                    if (cached is not null &&
                        cached.Path.Equals(
                            path,
                            StringComparison.OrdinalIgnoreCase) &&
                        cached.Length ==
                        info.Length &&
                        cached.LastWriteUtcTicks ==
                        info.LastWriteTimeUtc.Ticks)
                    {
                        return cached.Xml;
                    }
                }
            }
            catch
            {
            }

            var xml =
                File.ReadAllText(
                    path);

            try
            {
                Directory.CreateDirectory(
                    CacheDirectory);

                var entry =
                    new CacheEntry(
                        path,
                        info.Length,
                        info.LastWriteTimeUtc.Ticks,
                        xml);

                var temp =
                    cachePath +
                    "." +
                    Guid.NewGuid()
                        .ToString(
                            "N") +
                    ".tmp";

                File.WriteAllText(
                    temp,
                    JsonSerializer.Serialize(
                        entry,
                        JsonOptions),
                    new UTF8Encoding(
                        encoderShouldEmitUTF8Identifier:
                            false));

                File.Move(
                    temp,
                    cachePath,
                    overwrite:
                        true);
            }
            catch
            {
            }

            return xml;
        }
    }

    public static void Invalidate(
        string path)
    {
        lock (SyncRoot)
        {
            try
            {
                File.Delete(
                    CachePath(
                        path));
            }
            catch
            {
            }
        }
    }

    private static string CacheDirectory =>
        Path.Combine(
            StoragePaths.Cache,
            "GppXml");

    private static string CachePath(
        string path)
    {
        var bytes =
            SHA256.HashData(
                Encoding.UTF8.GetBytes(
                    path.ToUpperInvariant()));

        var name =
            Convert.ToHexString(
                bytes);

        return Path.Combine(
            CacheDirectory,
            name +
            ".json");
    }
}

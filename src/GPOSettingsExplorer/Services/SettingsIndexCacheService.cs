using System.Text.Json;
using GPOSettingsExplorer.Models;

namespace GPOSettingsExplorer.Services;

public sealed class SettingsIndexCacheService
{
    public const int CurrentSchemaVersion = 5;

    private static readonly JsonSerializerOptions JsonOptions =
        new()
        {
            WriteIndented = false,
            PropertyNameCaseInsensitive = true
        };

    public SettingsIndexCacheSnapshot Load(
        string domainName)
    {
        var path =
            GetPath(
                domainName);

        if (!File.Exists(path))
        {
            return SettingsIndexCacheSnapshot.Empty(
                domainName);
        }

        try
        {
            var json =
                File.ReadAllText(
                    path);

            var snapshot =
                JsonSerializer.Deserialize<SettingsIndexCacheSnapshot>(
                    json,
                    JsonOptions);

            if (snapshot is null ||
                snapshot.SchemaVersion != CurrentSchemaVersion ||
                !snapshot.DomainName.Equals(
                    domainName,
                    StringComparison.OrdinalIgnoreCase))
            {
                return SettingsIndexCacheSnapshot.Empty(
                    domainName);
            }

            return snapshot;
        }
        catch
        {
            TryQuarantineCorruptCache(
                path);

            return SettingsIndexCacheSnapshot.Empty(
                domainName);
        }
    }

    public void Save(
        string domainName,
        IEnumerable<GpoInfo> gpos,
        IEnumerable<PolicySettingInfo> settings)
    {
        var gpoArray =
            gpos.ToArray();

        var settingsByGpo =
            settings
                .GroupBy(item =>
                    item.GpoId)
                .ToDictionary(
                    group =>
                        group.Key,
                    group =>
                        group.ToList());

        var snapshot =
            new SettingsIndexCacheSnapshot
            {
                SchemaVersion =
                    CurrentSchemaVersion,
                DomainName =
                    domainName,
                GeneratedUtc =
                    DateTime.UtcNow,
                Entries =
                    gpoArray
                        .Select(gpo =>
                            new SettingsIndexCacheEntry
                            {
                                GpoId =
                                    gpo.Id,
                                GpoModificationUtc =
                                    ToUtc(
                                        gpo.ModificationTime),
                                Settings =
                                    settingsByGpo.TryGetValue(
                                        gpo.Id,
                                        out var cachedSettings)
                                        ? cachedSettings
                                        : new List<PolicySettingInfo>()
                            })
                        .ToList()
            };

        SaveSnapshot(
            snapshot);
    }

    public void SaveSnapshot(
        SettingsIndexCacheSnapshot snapshot)
    {
        var path =
            GetPath(
                snapshot.DomainName);

        var directory =
            Path.GetDirectoryName(
                path)
            ?? StoragePaths.Cache;

        Directory.CreateDirectory(
            directory);

        var temp =
            path +
            "." +
            Guid.NewGuid().ToString("N") +
            ".tmp";

        var json =
            JsonSerializer.Serialize(
                snapshot,
                JsonOptions);

        File.WriteAllText(
            temp,
            json);

        File.Move(
            temp,
            path,
            overwrite: true);
    }

    public IReadOnlyList<PolicySettingInfo> GetCurrentSettings(
        SettingsIndexCacheSnapshot snapshot,
        IEnumerable<GpoInfo> currentGpos)
    {
        var currentIds =
            currentGpos
                .Select(gpo =>
                    gpo.Id)
                .ToHashSet();

        return snapshot.Entries
            .Where(entry =>
                currentIds.Contains(
                    entry.GpoId))
            .SelectMany(entry =>
                entry.Settings)
            .ToArray();
    }

    public IReadOnlyList<GpoInfo> GetChangedGpos(
        SettingsIndexCacheSnapshot snapshot,
        IEnumerable<GpoInfo> currentGpos,
        bool forceRebuild)
    {
        if (forceRebuild)
        {
            return currentGpos.ToArray();
        }

        var cached =
            snapshot.Entries.ToDictionary(
                entry =>
                    entry.GpoId);

        return currentGpos
            .Where(gpo =>
            {
                if (!cached.TryGetValue(
                        gpo.Id,
                        out var entry))
                {
                    return true;
                }

                return !Nullable.Equals(
                    entry.GpoModificationUtc,
                    ToUtc(
                        gpo.ModificationTime));
            })
            .ToArray();
    }

    public SettingsIndexCacheSnapshot Merge(
        string domainName,
        SettingsIndexCacheSnapshot previous,
        IEnumerable<GpoInfo> currentGpos,
        IEnumerable<GpoInfo> rebuiltGpos,
        IEnumerable<PolicySettingInfo> rebuiltSettings)
    {
        var current =
            currentGpos.ToArray();

        var currentIds =
            current
                .Select(gpo =>
                    gpo.Id)
                .ToHashSet();

        var rebuiltIds =
            rebuiltGpos
                .Select(gpo =>
                    gpo.Id)
                .ToHashSet();

        var rebuiltByGpo =
            rebuiltSettings
                .GroupBy(item =>
                    item.GpoId)
                .ToDictionary(
                    group =>
                        group.Key,
                    group =>
                        group.ToList());

        var previousByGpo =
            previous.Entries
                .Where(entry =>
                    currentIds.Contains(
                        entry.GpoId))
                .ToDictionary(
                    entry =>
                        entry.GpoId);

        var entries =
            new List<SettingsIndexCacheEntry>(
                current.Length);

        foreach (var gpo in current)
        {
            if (rebuiltIds.Contains(
                    gpo.Id))
            {
                entries.Add(
                    new SettingsIndexCacheEntry
                    {
                        GpoId =
                            gpo.Id,
                        GpoModificationUtc =
                            ToUtc(
                                gpo.ModificationTime),
                        Settings =
                            rebuiltByGpo.TryGetValue(
                                gpo.Id,
                                out var settings)
                                ? settings
                                : new List<PolicySettingInfo>()
                    });

                continue;
            }

            if (previousByGpo.TryGetValue(
                    gpo.Id,
                    out var existing))
            {
                entries.Add(
                    existing);
            }
        }

        return new SettingsIndexCacheSnapshot
        {
            SchemaVersion =
                CurrentSchemaVersion,
            DomainName =
                domainName,
            GeneratedUtc =
                DateTime.UtcNow,
            Entries =
                entries
        };
    }

    public string GetPath(
        string domainName)
    {
        var safeDomain =
            string.Concat(
                domainName.Select(character =>
                    char.IsLetterOrDigit(character) ||
                    character is '.' or '-' or '_'
                        ? character
                        : '_'));

        return Path.Combine(
            StoragePaths.Cache,
            $"settings-index-{safeDomain}.json");
    }

    private static DateTime? ToUtc(
        DateTime? value)
    {
        if (value is null)
        {
            return null;
        }

        return value.Value.Kind switch
        {
            DateTimeKind.Utc =>
                value.Value,
            DateTimeKind.Local =>
                value.Value.ToUniversalTime(),
            _ =>
                DateTime.SpecifyKind(
                    value.Value,
                    DateTimeKind.Local)
                .ToUniversalTime()
        };
    }

    private static void TryQuarantineCorruptCache(
        string path)
    {
        try
        {
            var quarantine =
                path +
                ".corrupt-" +
                DateTime.UtcNow.ToString(
                    "yyyyMMddHHmmss");

            File.Move(
                path,
                quarantine,
                overwrite: true);
        }
        catch
        {
        }
    }
}

public sealed class SettingsIndexCacheSnapshot
{
    public int SchemaVersion { get; init; }
    public string DomainName { get; init; } = string.Empty;
    public DateTime GeneratedUtc { get; init; }
    public List<SettingsIndexCacheEntry> Entries { get; init; } = new();

    public static SettingsIndexCacheSnapshot Empty(
        string domainName)
    {
        return new SettingsIndexCacheSnapshot
        {
            SchemaVersion = SettingsIndexCacheService.CurrentSchemaVersion,
            DomainName = domainName,
            GeneratedUtc = DateTime.MinValue
        };
    }
}

public sealed class SettingsIndexCacheEntry
{
    public Guid GpoId { get; init; }
    public DateTime? GpoModificationUtc { get; init; }
    public List<PolicySettingInfo> Settings { get; init; } = new();
}

using System.Text.Json;
using GPOSettingsExplorer.Models;

namespace GPOSettingsExplorer.Services;

public sealed class WmiBackupService
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true
    };

    public string Backup(WmiFilterInfo filter, string reason)
    {
        var safeName = SanitizeFileName(
            string.IsNullOrWhiteSpace(filter.Name) ? "WMI-Filter" : filter.Name);

        var directory = Path.Combine(
            StoragePaths.WmiBackups,
            $"{safeName}_{DateTime.Now:yyyyMMdd-HHmmss}");

        Directory.CreateDirectory(directory);

        var snapshot = new
        {
            Timestamp = DateTimeOffset.Now,
            Reason = reason,
            Filter = new
            {
                filter.Id,
                filter.Domain,
                filter.Name,
                filter.Description,
                filter.Author,
                filter.SourceOrganization,
                filter.CreationDate,
                filter.ChangeDate,
                Rules = filter.Rules.Select(rule => new
                {
                    rule.QueryLanguage,
                    rule.TargetNamespace,
                    rule.Query
                }).ToArray()
            }
        };

        var path = Path.Combine(directory, "wmi-filter.json");
        File.WriteAllText(path, JsonSerializer.Serialize(snapshot, Options));
        return path;
    }

    private static string SanitizeFileName(string value)
    {
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            value = value.Replace(invalid, '_');
        }

        return value;
    }
}

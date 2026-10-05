using System.Text.Json;
using GPOSettingsExplorer.Models;

namespace GPOSettingsExplorer.Services;

public sealed class AuditLogReaderService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public IReadOnlyList<AuditEntryInfo> Load(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
            throw new ArgumentException("Audit directory cannot be empty.", nameof(directory));

        var fullPath = Path.GetFullPath(
            Environment.ExpandEnvironmentVariables(directory.Trim()));

        if (!Directory.Exists(fullPath))
            return Array.Empty<AuditEntryInfo>();

        var result = new List<AuditEntryInfo>();

        foreach (var file in Directory.EnumerateFiles(
                     fullPath,
                     "audit-*.jsonl",
                     SearchOption.TopDirectoryOnly)
                 .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            foreach (var line in File.ReadLines(file))
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                try
                {
                    var dto = JsonSerializer.Deserialize<AuditEntryInfo>(line, JsonOptions);
                    if (dto is null)
                        continue;

                    result.Add(new AuditEntryInfo
                    {
                        Timestamp = dto.Timestamp,
                        User = dto.User,
                        Computer = dto.Computer,
                        Action = dto.Action,
                        ObjectType = dto.ObjectType,
                        ObjectName = dto.ObjectName,
                        Details = dto.Details,
                        Before = dto.Before,
                        After = dto.After,
                        SourceFile = file
                    });
                }
                catch (JsonException)
                {
                    // A single malformed historical line must not block the entire audit viewer.
                }
            }
        }

        return result
            .OrderByDescending(entry => entry.Timestamp)
            .ToArray();
    }
}

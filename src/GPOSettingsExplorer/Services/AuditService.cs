using System.Text.Json;

namespace GPOSettingsExplorer.Services;

public sealed class AuditService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false
    };

    public void Write(
        string action,
        string objectType,
        string objectName,
        string details,
        string? before = null,
        string? after = null)
    {
        var entry = new AuditEntry
        {
            Timestamp = DateTimeOffset.Now,
            User = $"{Environment.UserDomainName}\\{Environment.UserName}",
            Computer = Environment.MachineName,
            Action = action,
            ObjectType = objectType,
            ObjectName = objectName,
            Details = details,
            Before = before ?? string.Empty,
            After = after ?? string.Empty
        };

        var file = Path.Combine(StoragePaths.Audit, $"audit-{DateTime.Now:yyyy-MM}.jsonl");
        var line = JsonSerializer.Serialize(entry, JsonOptions);
        File.AppendAllText(file, line + Environment.NewLine);
    }

    private sealed class AuditEntry
    {
        public DateTimeOffset Timestamp { get; init; }
        public string User { get; init; } = string.Empty;
        public string Computer { get; init; } = string.Empty;
        public string Action { get; init; } = string.Empty;
        public string ObjectType { get; init; } = string.Empty;
        public string ObjectName { get; init; } = string.Empty;
        public string Details { get; init; } = string.Empty;
        public string Before { get; init; } = string.Empty;
        public string After { get; init; } = string.Empty;
    }
}

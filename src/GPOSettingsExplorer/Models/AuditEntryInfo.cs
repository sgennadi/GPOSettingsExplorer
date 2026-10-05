namespace GPOSettingsExplorer.Models;

public sealed class AuditEntryInfo
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
    public string SourceFile { get; init; } = string.Empty;

    public string SearchText =>
        $"{Timestamp:O} {User} {Computer} {Action} {ObjectType} {ObjectName} {Details} {Before} {After}";
}

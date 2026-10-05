namespace GPOSettingsExplorer.Models;

public sealed class GpoBackupInfo
{
    public Guid BackupId { get; init; }
    public Guid GpoId { get; init; }
    public string DisplayName { get; init; } = string.Empty;
    public string DomainName { get; init; } = string.Empty;
    public string Comment { get; init; } = string.Empty;
    public DateTime? Timestamp { get; init; }
    public string BackupDirectory { get; init; } = string.Empty;

    public string BackupIdText => BackupId.ToString("B").ToUpperInvariant();
    public string GpoIdText => GpoId.ToString("B").ToUpperInvariant();
}

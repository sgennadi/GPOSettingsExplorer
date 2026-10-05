namespace GPOSettingsExplorer.Models;

public sealed class GpoInfo
{
    public Guid Id { get; init; }
    public string DisplayName { get; init; } = string.Empty;
    public string DomainName { get; init; } = string.Empty;
    public DateTime? CreationTime { get; init; }
    public DateTime? ModificationTime { get; init; }
    public bool ComputerEnabled { get; init; }
    public bool UserEnabled { get; init; }
    public string WmiFilterName { get; init; } = string.Empty;
    public string WmiFilterPath { get; init; } = string.Empty;

    public string ScopeState =>
        ComputerEnabled && UserEnabled ? "Computer + User" :
        ComputerEnabled ? "Computer" :
        UserEnabled ? "User" : "Disabled";

    public string IdText => Id.ToString("B").ToUpperInvariant();
}

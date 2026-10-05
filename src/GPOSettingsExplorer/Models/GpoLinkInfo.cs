namespace GPOSettingsExplorer.Models;

public sealed class GpoLinkTarget
{
    public string Name { get; init; } = string.Empty;
    public string DistinguishedName { get; init; } = string.Empty;
    public string TargetType { get; init; } = string.Empty;
    public bool BlockInheritance { get; init; }

    public string DisplayName => $"{TargetType}: {Name}";
}

public sealed class GpoLinkInfo
{
    public Guid GpoId { get; init; }
    public string GpoName { get; init; } = string.Empty;
    public string TargetName { get; init; } = string.Empty;
    public string TargetDn { get; init; } = string.Empty;
    public string TargetType { get; init; } = string.Empty;
    public int Order { get; init; }
    public bool Enabled { get; init; }
    public bool Enforced { get; init; }
    public bool BlockInheritance { get; init; }

    public string State => Enabled ? "Enabled" : "Disabled";
}

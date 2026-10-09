namespace GPOSettingsExplorer.Models;

public sealed class GpoComparisonRow
{
    public string Identity { get; init; } = string.Empty;
    public string Scope { get; init; } = string.Empty;
    public string SettingName { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public string RegistryKey { get; init; } = string.Empty;
    public string RegistryValue { get; init; } = string.Empty;
    public string LeftState { get; init; } = string.Empty;
    public string LeftValue { get; init; } = string.Empty;
    public string RightState { get; init; } = string.Empty;
    public string RightValue { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;

    public bool IsDifferent => !Status.Equals("Same", StringComparison.OrdinalIgnoreCase);

    public string SearchText =>
        $"{Status} {Scope} {SettingName} {Category} {RegistryKey} {RegistryValue} " +
        $"{LeftState} {LeftValue} {RightState} {RightValue}";
}

public sealed class GpoConflictInfo
{
    public string Identity { get; init; } = string.Empty;
    public string Scope { get; init; } = string.Empty;
    public string SettingName { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public string RegistryKey { get; init; } = string.Empty;
    public string RegistryValue { get; init; } = string.Empty;
    public string Gpos { get; init; } = string.Empty;
    public string Variants { get; init; } = string.Empty;
    public IReadOnlyList<Guid> GpoIds { get; init; } = Array.Empty<Guid>();
    public string Kind { get; init; } = "Different values";
    public string OverlapStatus { get; init; } = "Unknown";
    public string LinkEvidence { get; init; } = string.Empty;
    public string Recommendation { get; init; } = string.Empty;
    public string PriorityNote { get; init; } = string.Empty;
    public bool IsPotentialOverlap { get; init; }
    public IReadOnlyList<PolicySettingInfo> Participants { get; init; } =
        Array.Empty<PolicySettingInfo>();

    public string SearchText =>
        Kind + " " + OverlapStatus + " " + LinkEvidence + " " +
        Recommendation + " " +
        $"{Scope} {SettingName} {Category} {RegistryKey} {RegistryValue} {Gpos} {Variants}";
}

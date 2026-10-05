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

    public string SearchText =>
        $"{Scope} {SettingName} {Category} {RegistryKey} {RegistryValue} {Gpos} {Variants}";
}

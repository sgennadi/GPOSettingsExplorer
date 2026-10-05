namespace GPOSettingsExplorer.Models;

public sealed class PolicySettingInfo
{
    public Guid GpoId { get; init; }
    public string GpoName { get; init; } = string.Empty;
    public string Scope { get; init; } = string.Empty;
    public string Extension { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public string SettingName { get; init; } = string.Empty;
    public string State { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;
    public string RegistryKey { get; init; } = string.Empty;
    public string RegistryValue { get; init; } = string.Empty;

    public string SearchText =>
        $"{GpoName} {Scope} {Extension} {Category} {SettingName} {State} {Value} {RegistryKey} {RegistryValue}";
}

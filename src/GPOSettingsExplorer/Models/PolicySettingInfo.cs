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

    // GPMC Group Policy Preferences Registry item metadata. Never treated
    // as Registry.pol or proof of any target machine's live registry.
    public string GppRegistryHive { get; init; } = string.Empty;
    public string GppRegistryKeyPath { get; init; } = string.Empty;
    public string GppRegistryValueName { get; init; } = string.Empty;
    public string GppRegistryAction { get; init; } = string.Empty;
    public string GppRegistryUid { get; init; } = string.Empty;
    public int GppRegistryOrder { get; init; }

    public string SearchText =>
        $"{GpoName} {Scope} {Extension} {Category} {SettingName} {State} {Value} {RegistryKey} {RegistryValue} {GppRegistryHive} {GppRegistryKeyPath} {GppRegistryValueName} {GppRegistryAction} {GppRegistryUid} {GppRegistryOrder}";
}

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
    public string GppRegistryValueType { get; init; } = string.Empty;
    public bool GppRegistryHasFilters { get; init; }
    public string GppRegistryUid { get; init; } = string.Empty;
    public int GppRegistryOrder { get; init; }

    // Source-labeled, display-only columns for the legacy GPMC index grid.
    // Keep GPP Registry.xml identities completely separate from Registry.pol.
    // GPMC metadata of other extensions must not fabricate a registry key.
    public string IndexedSourceRegistryKey =>
        Extension.Equals("GppRegistry", StringComparison.OrdinalIgnoreCase) &&
        !string.IsNullOrWhiteSpace(GppRegistryHive) &&
        !string.IsNullOrWhiteSpace(GppRegistryKeyPath)
            ? "GPP source: " + GppRegistryHive + "\\" + GppRegistryKeyPath
            : Extension.Equals("RegistrySettings", StringComparison.OrdinalIgnoreCase) ||
              Extension.Equals("Administrative Templates", StringComparison.OrdinalIgnoreCase)
                ? RegistryKey : string.Empty;

    public string IndexedSourceRegistryValueName =>
        Extension.Equals("GppRegistry", StringComparison.OrdinalIgnoreCase)
            ? GppRegistryValueName
            : Extension.Equals("RegistrySettings", StringComparison.OrdinalIgnoreCase) ||
              Extension.Equals("Administrative Templates", StringComparison.OrdinalIgnoreCase)
                ? RegistryValue : string.Empty;

    public string SearchText =>
        $"{GpoName} {Scope} {Extension} {Category} {SettingName} {State} {Value} {RegistryKey} {RegistryValue} {GppRegistryHive} {GppRegistryKeyPath} {GppRegistryValueName} {GppRegistryAction} {GppRegistryValueType} {GppRegistryUid} {GppRegistryOrder}";
}

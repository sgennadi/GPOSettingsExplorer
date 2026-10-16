using GPOSettingsExplorer.Models;

namespace GPOSettingsExplorer.Services;

/// <summary>
/// Fail-closed GPMC value-source classification shared by the All Settings
/// index and individual value viewer. Extension XML attributes called Name,
/// Key, Path or ValueName do NOT, by themselves, establish a Registry.pol target.
/// This also protects users who have an older cached GPMC settings index.
/// </summary>
public static class GpoSettingSourceProvenance
{
    public static bool IsVerifiedRegistryTarget(PolicySettingInfo setting)
    {
        if (string.IsNullOrWhiteSpace(setting.RegistryKey))
            return false;

        if (setting.Extension.Equals("RegistrySettings", StringComparison.OrdinalIgnoreCase) ||
            setting.Extension.Equals("Administrative Templates", StringComparison.OrdinalIgnoreCase))
            return true;

        // Security Options KeyName may be LSA/SecEdit metadata rather than
        // a Registry.pol policy. Never claim it is a verified registry
        // target from the generic GPMC model.
        return false;
    }

    public static bool IsGppRegistrySource(PolicySettingInfo setting) =>
        setting.Extension.Equals("GppRegistry", StringComparison.OrdinalIgnoreCase) &&
        !string.IsNullOrWhiteSpace(setting.GppRegistryHive) &&
        !string.IsNullOrWhiteSpace(setting.GppRegistryKeyPath);

    // View-only, source-labeled target. DO NOT feed it to Registry.pol
    // correlation or native/local regedit; a GPP Delete action describes
    // desired policy behavior, not a present value.
    public static string DisplayRegistryTarget(PolicySettingInfo setting)
    {
        if (IsGppRegistrySource(setting))
            return "GPP source: " + setting.GppRegistryHive + "\\" +
                setting.GppRegistryKeyPath +
                (string.IsNullOrWhiteSpace(setting.GppRegistryValueName)
                    ? "" : " / " + setting.GppRegistryValueName);
        return RegistryTarget(setting);
    }

    public static string RegistrySummary(PolicySettingInfo setting)
    {
        if (IsGppRegistrySource(setting))
            return DisplayRegistryTarget(setting) +
                " | Action: " + setting.GppRegistryAction +
                " | Stored preference (not Registry.pol; target client state unknown)";
        if (!IsVerifiedRegistryTarget(setting))
            return "Not applicable / not verified for this GPMC extension";

        return string.IsNullOrWhiteSpace(setting.RegistryValue)
            ? setting.RegistryKey
            : setting.RegistryKey + " \\ " + setting.RegistryValue;
    }

    public static string RegistryTarget(PolicySettingInfo setting)
    {
        if (!IsVerifiedRegistryTarget(setting))
            return string.Empty;
        return string.IsNullOrWhiteSpace(setting.RegistryValue)
            ? setting.RegistryKey
            : setting.RegistryKey + " / " + setting.RegistryValue;
    }

    public static string SourceType(PolicySettingInfo setting) =>
        setting.Extension switch
        {
            "RegistrySettings" or "Administrative Templates" => "Registry.pol / Administrative Templates",
            "GppRegistry" => "GPMC Group Policy Preferences Registry (not Registry.pol)",
            "SecuritySettings" => "GPMC Security Settings (not GPP Registry)",
            "AuditSettings" => "GPMC Advanced Audit",
            "NrptSettings" => "GPMC Name Resolution Policy",
            "SoftwareInstallationSettings" => "GPMC Software Installation",
            _ => "GPMC extension: " +
                 (string.IsNullOrWhiteSpace(setting.Extension)
                     ? "(unknown)" : setting.Extension)
        };
}

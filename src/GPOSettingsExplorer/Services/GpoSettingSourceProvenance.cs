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

        if (setting.Extension.Equals("RegistrySettings", StringComparison.OrdinalIgnoreCase))
            return true;

        // Security Options may encode an actual registry-backed policy target
        // via an explicit SecurityOptions/KeyName source. Other security
        // extension rows (Account, Registry ACL, Rights) are NOT registry.pol.
        return setting.Extension.Equals("SecuritySettings", StringComparison.OrdinalIgnoreCase) &&
               setting.Category.Equals(
                   "Security Settings > Local Policies > Security Options",
                   StringComparison.OrdinalIgnoreCase);
    }

    public static string RegistrySummary(PolicySettingInfo setting)
    {
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
            "RegistrySettings" => "Registry.pol / Administrative Templates",
            "SecuritySettings" => "GPMC Security Settings (not GPP Registry)",
            "AuditSettings" => "GPMC Advanced Audit",
            "NrptSettings" => "GPMC Name Resolution Policy",
            "SoftwareInstallationSettings" => "GPMC Software Installation",
            _ => "GPMC extension: " +
                 (string.IsNullOrWhiteSpace(setting.Extension)
                     ? "(unknown)" : setting.Extension)
        };
}

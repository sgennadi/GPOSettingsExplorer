using GPOSettingsExplorer.Models;

namespace GPOSettingsExplorer.Services;

/// <summary>
/// Maps configured non-ADMX GPO extension summaries to *existing* MMC tree
/// nodes. These are section-only targets: never mistake them for an exact
/// editable policy row.
/// </summary>
public static class GpoPolicySectionRoutes
{
    public static IReadOnlyList<string> Resolve(PolicySettingInfo setting)
    {
        var scope = setting.Scope.Equals("User", StringComparison.OrdinalIgnoreCase)
            ? "User Configuration"
            : "Computer Configuration";

        if (setting.Extension.Equals("NrptSettings", StringComparison.OrdinalIgnoreCase))
        {
            // Name Resolution Policy is not below Security Settings.
            return new[] { scope, "Policies", "Windows Settings", "Name Resolution Policy" };
        }

        if (setting.Extension.Equals("SoftwareInstallationSettings", StringComparison.OrdinalIgnoreCase))
        {
            // GPO XML may expose nested Trustee Permissions/Auditing from an
            // MSI package's security descriptor. These are package metadata,
            // not separate Security Options or Administrative Templates.
            return new[] { scope, "Policies", "Software Settings", "Software installation" };
        }

        if (setting.Extension.Equals("SecuritySettings", StringComparison.OrdinalIgnoreCase) &&
            scope == "Computer Configuration")
        {
            // These GPMC report rows describe security template sections,
            // not individual settings with a verified native MMC edit dialog.
            var category = SecurityXmlEntryClassifier.InferLegacyCategory(setting);
            var section = category switch
            {
                SecurityXmlEntryClassifier.RegistrySection => "Registry",
                SecurityXmlEntryClassifier.RestrictedGroupsSection => "Restricted Groups",
                _ => null
            };

            if (section is not null)
                return new[]
                {
                    scope, "Policies", "Windows Settings", "Security Settings", section
                };
        }

        if (!setting.Extension.Equals("PublicKeySettings", StringComparison.OrdinalIgnoreCase))
            return Array.Empty<string>();

        var result = new List<string>
        {
            scope, "Policies", "Windows Settings", "Security Settings", "Public Key Policies"
        };

        var name = setting.SettingName;
        if (name.Contains("Root Certificate", StringComparison.OrdinalIgnoreCase))
        {
            result.Add("Trusted Root Certification Authorities");
        }
        else if (name.Contains("EFS", StringComparison.OrdinalIgnoreCase) ||
                 name.Contains("Encrypting File", StringComparison.OrdinalIgnoreCase))
        {
            result.Add("Encrypting File System");
        }
        else if (name.Contains("Certificate Path Validation", StringComparison.OrdinalIgnoreCase))
        {
            result.Add("Certificate Path Validation Settings");
        }
        // For other PublicKeySettings summaries, stop at Public Key Policies.
        // Picking an unrelated certificate store would be unsafe.

        return result;
    }
}

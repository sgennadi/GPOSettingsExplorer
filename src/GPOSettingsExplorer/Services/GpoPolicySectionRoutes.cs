using GPOSettingsExplorer.Models;

namespace GPOSettingsExplorer.Services;

/// <summary>
/// Conservative GPMC-to-MMC SECTION route registry. A section-only route is
/// never proof that a particular value can be located or edited. An unhandled
/// GPMC extension remains unmapped; do NOT invent "Security Settings".
/// </summary>
public static class GpoPolicySectionRoutes
{
    private static readonly IReadOnlyDictionary<string, string[]> SecurityCategories =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["Security Settings > Account Policies > Password Policy"] =
                new[] { "Account Policies", "Password Policy" },
            ["Security Settings > Account Policies > Account Lockout Policy"] =
                new[] { "Account Policies", "Account Lockout Policy" },
            ["Security Settings > Account Policies > Kerberos Policy"] =
                new[] { "Account Policies", "Kerberos Policy" },
            ["Security Settings > Local Policies > Audit Policy"] =
                new[] { "Local Policies", "Audit Policy" },
            ["Security Settings > Local Policies > User Rights Assignment"] =
                new[] { "Local Policies", "User Rights Assignment" },
            ["Security Settings > Local Policies > Security Options"] =
                new[] { "Local Policies", "Security Options" },
            ["Security Settings > Event Log"] = new[] { "Event Log" },
            ["Security Settings > Restricted Groups"] = new[] { "Restricted Groups" },
            ["Security Settings > System Services"] = new[] { "System Services" },
            ["Security Settings > Registry"] = new[] { "Registry" },
            ["Security Settings > File System"] = new[] { "File System" }
        };

    public static IReadOnlyList<string> Resolve(PolicySettingInfo setting)
    {
        ArgumentNullException.ThrowIfNull(setting);
        var computer = setting.Scope.Equals("Computer", StringComparison.OrdinalIgnoreCase);
        var user = setting.Scope.Equals("User", StringComparison.OrdinalIgnoreCase);
        if (!computer && !user)
            return Array.Empty<string>();

        var scope = computer ? "Computer Configuration" : "User Configuration";
        var extension = setting.Extension.Trim();

        if (extension.Equals("RegistrySettings", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals("Administrative Templates", StringComparison.OrdinalIgnoreCase))
            return new[] { scope, "Policies", "Administrative Templates" };

        // Microsoft documents Software Restriction Policies under the
        // corresponding User/Computer Windows Settings > Security Settings.
        // This is a section route, not proof of a uniquely editable rule.
        if (SoftwareRestrictionMmcRouteService.IsSoftwareRestriction(setting))
            return SoftwareRestrictionMmcRouteService.EditorSection(setting);

        // Group Policy Preferences is not Registry.pol / Administrative Templates.
        // A known folder is a hint only until its native row is observed.
        var preferences = GpoPreferencesMmcSections.Resolve(setting);
        if (preferences.Count > 0)
            return preferences;

        if (extension.Equals("SoftwareInstallationSettings", StringComparison.OrdinalIgnoreCase))
            return new[] { scope, "Policies", "Software Settings", "Software installation" };

        if (extension.Equals("NrptSettings", StringComparison.OrdinalIgnoreCase) && computer)
            return new[] { scope, "Policies", "Windows Settings", "Name Resolution Policy" };

        if (extension.Equals("AuditSettings", StringComparison.OrdinalIgnoreCase) && computer)
            return new[] { scope, "Policies", "Windows Settings", "Security Settings",
                "Advanced Audit Policy Configuration", "Audit Policies" };

        if (extension.Equals("SecuritySettings", StringComparison.OrdinalIgnoreCase) && computer)
        {
            if (KerberosPolicyMetadataService.IsKnownKerberosSetting(setting))
                return KerberosPolicyMetadataService.EditorSection;

            if (GpoKnownAccountPolicyService.TryDescribe(setting, out var account))
                return account.TreePath;

            var category = SecurityXmlEntryClassifier.InferLegacyCategory(setting) ??
                setting.Category;
            // A category string is evidence only when it is the exact known
            // security-tree hierarchy. Arbitrary "SecuritySettings" and other
            // unknown names must never be routed to a plausible parent node.
            if (!SecurityCategories.TryGetValue(category.Trim(), out var relative))
                return Array.Empty<string>();

            return new[] { scope, "Policies", "Windows Settings", "Security Settings" }
                .Concat(relative).ToArray();
        }

        if (!extension.Equals("PublicKeySettings", StringComparison.OrdinalIgnoreCase))
            return Array.Empty<string>();

        var result = new List<string>
        {
            scope, "Policies", "Windows Settings", "Security Settings", "Public Key Policies"
        };

        // Only explicit section names get a deeper MMC route. A fuzzy
        // "Root Certificate" substring could refer to the wrong store.
        var name = setting.SettingName.Trim();
        if (name.Equals("Root Certificate Settings", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("Trusted Root Certification Authorities", StringComparison.OrdinalIgnoreCase))
        {
            result.Add("Trusted Root Certification Authorities");
        }
        else if (name.Equals("EFS", StringComparison.OrdinalIgnoreCase) ||
                 name.Equals("Encrypting File System", StringComparison.OrdinalIgnoreCase))
        {
            result.Add("Encrypting File System");
        }
        else if (name.Equals("Certificate Path Validation Settings",
                     StringComparison.OrdinalIgnoreCase))
        {
            result.Add("Certificate Path Validation Settings");
        }
        return result;
    }
}

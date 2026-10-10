using System.Xml.Linq;
using GPOSettingsExplorer.Models;

namespace GPOSettingsExplorer.Services;

/// <summary>
/// Classifies descriptive GPMC SecuritySettings XML entries without treating
/// leaf elements such as Group Member or Registry ACL as independent ADMX
/// policies. Categories are GPMC evidence, not verified exact edit targets.
/// </summary>
public static class SecurityXmlEntryClassifier
{
    public const string RegistrySection = "Security Settings > Registry";
    public const string RestrictedGroupsSection = "Security Settings > Restricted Groups";

    public static string? InferCategory(XElement element)
    {
        // Known Account security-extension nodes are Kerberos policy settings,
        // not Registry or an unrelated Security Options category.
        if (KerberosPolicyMetadataService.TryDescribeGpmcNode(element, out _))
            return KerberosPolicyMetadataService.Category;

        var label = element.Name.LocalName;
        var ancestors = element.Ancestors()
            .Select(node => node.Name.LocalName).ToArray();

        if (IsRegistryNode(label) && !ancestors.Any(a =>
                a.Equals("SecurityOptions", StringComparison.OrdinalIgnoreCase)))
            return RegistrySection;

        if (IsGroupMemberNode(label) &&
            ancestors.Any(IsGroupContainer))
            return RestrictedGroupsSection;

        if (IsGroupContainer(label) &&
            !label.Equals("LocalGroup", StringComparison.OrdinalIgnoreCase))
            return RestrictedGroupsSection;

        return null;
    }

    public static bool IsTechnicalDetail(PolicySettingInfo setting)
    {
        if (!setting.Extension.Equals("SecuritySettings",
                StringComparison.OrdinalIgnoreCase))
            return false;

        var name = setting.SettingName.Trim();
        return name.StartsWith("Member:", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("Registry", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("Security Descriptor", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("Permission", StringComparison.OrdinalIgnoreCase);
    }

    public static string DisplaySummary(PolicySettingInfo setting)
    {
        if (!IsTechnicalDetail(setting))
            return setting.Value;

        if (setting.SettingName.StartsWith("Member:", StringComparison.OrdinalIgnoreCase))
            return "Restricted-group membership XML detail - inspect source for SID and member name.";

        if (setting.SettingName.Equals("Registry", StringComparison.OrdinalIgnoreCase))
            return "Registry security descriptor / ACL XML detail - inspect source for full permissions.";

        return "GPMC XML metadata / security descriptor - inspect original value.";
    }

    /// <summary>
    /// Applies only to legacy indexed records which did not store XML ancestry.
    /// Never classify arbitrary group member labels without a provenance clue.
    /// The resulting route remains section-only and read-only.
    /// </summary>
    public static string? InferLegacyCategory(PolicySettingInfo setting)
    {
        if (!setting.Extension.Equals("SecuritySettings",
                StringComparison.OrdinalIgnoreCase))
            return null;

        if (setting.Category.Equals(RegistrySection,
                StringComparison.OrdinalIgnoreCase) ||
            setting.Category.Equals(RestrictedGroupsSection,
                StringComparison.OrdinalIgnoreCase))
            return setting.Category;

        if (!setting.Category.Equals("SecuritySettings",
                StringComparison.OrdinalIgnoreCase))
            return null;

        if (setting.SettingName.Equals("Registry", StringComparison.OrdinalIgnoreCase))
            return RegistrySection;

        if (setting.SettingName.StartsWith("Member:", StringComparison.OrdinalIgnoreCase) &&
            setting.Value.Contains("SID=", StringComparison.OrdinalIgnoreCase) &&
            setting.Value.Contains("Name=", StringComparison.OrdinalIgnoreCase))
            return RestrictedGroupsSection;

        return null;
    }

    private static bool IsRegistryNode(string name) =>
        name.Equals("Registry", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("RegistrySecurity", StringComparison.OrdinalIgnoreCase);

    private static bool IsGroupMemberNode(string name) =>
        name.Equals("Member", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("MemberOf", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("GroupMember", StringComparison.OrdinalIgnoreCase);

    private static bool IsGroupContainer(string name) =>
        name.Equals("Group", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("RestrictedGroup", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("RestrictedGroups", StringComparison.OrdinalIgnoreCase);
}

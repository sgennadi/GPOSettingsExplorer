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

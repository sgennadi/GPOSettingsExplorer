using System.Xml.Linq;
using GPOSettingsExplorer.Models;

namespace GPOSettingsExplorer.Services;

/// <summary>
/// Verified GPMC SecuritySettings Account policy metadata for Kerberos.
/// This is security-template/LSA policy, NOT a Registry.pol value or GPP item.
/// Only exact known names on Account XML nodes with an explicit Kerberos type
/// may be normalized; unfamiliar entries remain manual/unknown.
/// </summary>
public static class GpoKerberosPolicyMetadata
{
    public const string Category = "Security Settings > Account Policies > Kerberos Policy";

    private static readonly IReadOnlyDictionary<string, string> Names =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["MaxServiceAge"] = "Maximum lifetime for service ticket",
            ["MaxTicketAge"] = "Maximum lifetime for user ticket",
            ["MaxRenewAge"] = "Maximum lifetime for user ticket renewal",
            ["MaxClockSkew"] = "Maximum tolerance for computer clock synchronization",
            ["TicketValidateClient"] = "Enforce user logon restrictions"
        };

    public static bool TryResolveXml(
        XElement element, string extension, string scope, out string settingName)
    {
        settingName = string.Empty;
        if (!extension.Equals("SecuritySettings", StringComparison.OrdinalIgnoreCase) ||
            !scope.Equals("Computer", StringComparison.OrdinalIgnoreCase) ||
            !element.Name.LocalName.Equals("Account", StringComparison.OrdinalIgnoreCase))
            return false;

        var type = ElementValue(element, "Type");
        var key = ElementValue(element, "Name");
        if (!type.Equals("Kerberos", StringComparison.OrdinalIgnoreCase) ||
            !Names.TryGetValue(key, out var label))
            return false;

        settingName = label;
        return true;
    }

    /// <summary>
    /// Supports older indexed SecuritySettings records with the exact
    /// "Account: MaxTicketAge" name and Type=Kerberos evidence.
    /// Never infer Kerberos from names alone for unrelated extensions.
    /// </summary>
    public static bool TryResolveSetting(PolicySettingInfo setting,
        out string displayName)
    {
        displayName = string.Empty;
        if (!setting.Extension.Equals("SecuritySettings", StringComparison.OrdinalIgnoreCase) ||
            !setting.Scope.Equals("Computer", StringComparison.OrdinalIgnoreCase))
            return false;

        if (setting.Category.Equals(Category, StringComparison.OrdinalIgnoreCase))
        {
            var existing = Names.Values.FirstOrDefault(n =>
                n.Equals(setting.SettingName, StringComparison.OrdinalIgnoreCase));
            if (existing is not null)
            {
                displayName = existing;
                return true;
            }
        }

        const string prefix = "Account: ";
        if (!setting.SettingName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
            !setting.Value.Split(';', StringSplitOptions.TrimEntries)
                .Any(p => p.Equals("Type=Kerberos", StringComparison.OrdinalIgnoreCase)))
            return false;

        var key = setting.SettingName[prefix.Length..].Trim();
        if (!Names.TryGetValue(key, out var label))
            return false;
        displayName = label;
        return true;
    }

    public static IReadOnlyList<string> MmcTreePath =>
        new[] { "Computer Configuration", "Policies", "Windows Settings",
            "Security Settings", "Account Policies", "Kerberos Policy" };

    private static string ElementValue(XElement source, string localName) =>
        source.Elements().FirstOrDefault(e =>
            e.Name.LocalName.Equals(localName, StringComparison.OrdinalIgnoreCase))?
            .Value.Trim() ?? string.Empty;
}

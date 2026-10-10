using System.Xml.Linq;
using GPOSettingsExplorer.Models;

namespace GPOSettingsExplorer.Services;

/// <summary>
/// Known GPMC SecuritySettings Account items that correspond to Kerberos Policy.
/// They are stored in the security policy extension, NOT Registry.pol or GPP
/// Registry. Unknown account metadata is never routed to an invented MMC item.
/// </summary>
public sealed record KerberosPolicyDescriptor(string InternalName, string DisplayName);

public static class KerberosPolicyMetadataService
{
    public const string Category =
        "Security Settings > Account Policies > Kerberos Policy";

    private static readonly Dictionary<string, KerberosPolicyDescriptor> Known =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["MaxServiceAge"] = new("MaxServiceAge",
                "Maximum lifetime for service ticket"),
            ["MaxTicketAge"] = new("MaxTicketAge",
                "Maximum lifetime for user ticket"),
            ["MaxRenewAge"] = new("MaxRenewAge",
                "Maximum lifetime for user ticket renewal"),
            ["MaxClockSkew"] = new("MaxClockSkew",
                "Maximum tolerance for computer clock synchronization"),
            ["TicketValidateClient"] = new("TicketValidateClient",
                "Enforce user logon restrictions")
        };

    public static bool TryGet(string? name, out KerberosPolicyDescriptor descriptor)
    {
        var normalized = (name ?? "").Trim();
        if (normalized.StartsWith("Account:", StringComparison.OrdinalIgnoreCase))
            normalized = normalized["Account:".Length..].Trim();
        if (Known.TryGetValue(normalized, out var exact))
        {
            descriptor = exact;
            return true;
        }
        foreach (var policy in Known.Values)
        {
            if (policy.DisplayName.Equals(normalized, StringComparison.OrdinalIgnoreCase))
            {
                descriptor = policy;
                return true;
            }
        }
        descriptor = null!;
        return false;
    }

    public static bool TryDescribeGpmcNode(
        XElement element, out KerberosPolicyDescriptor descriptor)
    {
        descriptor = null!;
        if (!element.Name.LocalName.Equals("Account", StringComparison.OrdinalIgnoreCase))
            return false;
        var name = element.Attributes().FirstOrDefault(a =>
            a.Name.LocalName.Equals("name", StringComparison.OrdinalIgnoreCase))?.Value;
        return TryGet(name, out descriptor);
    }

    public static bool IsKnownKerberosSetting(PolicySettingInfo setting) =>
        setting.Scope.Equals("Computer", StringComparison.OrdinalIgnoreCase) &&
        setting.Extension.Equals("SecuritySettings", StringComparison.OrdinalIgnoreCase) &&
        (setting.Category.Contains("Kerberos Policy", StringComparison.OrdinalIgnoreCase) ||
         setting.Category.Equals("SecuritySettings", StringComparison.OrdinalIgnoreCase)) &&
        TryGet(setting.SettingName, out _);

    public static IReadOnlyList<string> EditorSection => new[]
    {
        "Computer Configuration", "Policies", "Windows Settings",
        "Security Settings", "Account Policies", "Kerberos Policy"
    };
}

using GPOSettingsExplorer.Models;

namespace GPOSettingsExplorer.Services;

/// <summary>
/// Conservative mapping for GPMC security Account nodes that explicitly
/// declare the native source Type=Password or Type=Account Lockout.
/// These are Security CSE source settings, NEVER Registry.pol/ADMX guesses.
/// Unknown or untranslated policy names remain unsupported.
/// </summary>
public static class GpoKnownAccountPolicyService
{
    public sealed record Descriptor(
        string Type, string SourceName, string NativeName,
        IReadOnlyList<string> TreePath);

    private static readonly IReadOnlyDictionary<string, string> Password =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["MinimumPasswordAge"] = "Minimum password age",
            ["MaximumPasswordAge"] = "Maximum password age",
            ["MinimumPasswordLength"] = "Minimum password length",
            ["PasswordHistorySize"] = "Enforce password history",
            ["PasswordComplexity"] = "Password must meet complexity requirements",
            ["ClearTextPassword"] = "Store passwords using reversible encryption"
        };

    private static readonly IReadOnlyDictionary<string, string> Lockout =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["LockoutBadCount"] = "Account lockout threshold",
            ["LockoutDuration"] = "Account lockout duration",
            ["ResetLockoutCount"] = "Reset account lockout counter after"
        };

    public static bool TryDescribe(PolicySettingInfo setting, out Descriptor descriptor)
    {
        descriptor = null!;
        if (!setting.Extension.Equals("SecuritySettings", StringComparison.OrdinalIgnoreCase) ||
            !setting.Scope.Equals("Computer", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(setting.SettingName))
            return false;

        var name = setting.SettingName.Trim();
        if (name.StartsWith("Account:", StringComparison.OrdinalIgnoreCase))
            name = name["Account:".Length..].Trim();

        // Never infer policy type from Name alone. GPMC legacy reports have
        // Type=Password / Type=Account Lockout; these values are mandatory.
        var typeFields = setting.Value.Split(';',
            StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Where(field => field.StartsWith("Type=", StringComparison.OrdinalIgnoreCase))
            .Select(field => field["Type=".Length..].Trim())
            .ToArray();

        if (typeFields.Length != 1)
            return false;

        IReadOnlyDictionary<string, string>? names;
        string category;
        if (typeFields[0].Equals("Password", StringComparison.OrdinalIgnoreCase))
        {
            names = Password;
            category = "Password Policy";
        }
        else if (typeFields[0].Equals("Account Lockout",
                     StringComparison.OrdinalIgnoreCase))
        {
            names = Lockout;
            category = "Account Lockout Policy";
        }
        else
            return false;

        if (!names.TryGetValue(name, out var native))
        {
            var reverse = names.Where(pair =>
                pair.Value.Equals(name, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (reverse.Length != 1)
                return false;
            native = reverse[0].Value;
        }

        if (!setting.Category.Equals("SecuritySettings", StringComparison.OrdinalIgnoreCase) &&
            !setting.Category.Equals(
                "Security Settings > Account Policies > " + category,
                StringComparison.OrdinalIgnoreCase))
            return false;

        descriptor = new Descriptor(typeFields[0], name, native, new[]
        {
            "Computer Configuration", "Policies", "Windows Settings",
            "Security Settings", "Account Policies", category
        });
        return true;
    }
}

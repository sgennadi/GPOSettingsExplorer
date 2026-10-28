using GPOSettingsExplorer.Models;

namespace GPOSettingsExplorer.Services;

/// <summary>
/// Software Restriction Policies use their own GPMC Security extension.
/// Opening a GPO's Additional Rules folder never proves one stored rule's
/// value or client RSoP. Path Rule names repeat: only an exact, unique
/// path observed in the native MMC list can be used as an edit target.
/// </summary>
public static class SoftwareRestrictionMmcRouteService
{
    public static bool IsSoftwareRestriction(PolicySettingInfo setting) =>
        setting.Extension.Equals("SoftwareRestrictionSettings",
            StringComparison.OrdinalIgnoreCase) &&
        (setting.Scope.Equals("User", StringComparison.OrdinalIgnoreCase) ||
         setting.Scope.Equals("Computer", StringComparison.OrdinalIgnoreCase));

    public static bool IsPathRule(PolicySettingInfo setting) =>
        IsSoftwareRestriction(setting) &&
        setting.SettingName.Equals("Path Rule", StringComparison.OrdinalIgnoreCase);

    public static IReadOnlyList<string> EditorSection(PolicySettingInfo setting)
    {
        if (!IsSoftwareRestriction(setting))
            return Array.Empty<string>();

        var root = setting.Scope.Equals("Computer", StringComparison.OrdinalIgnoreCase)
            ? "Computer Configuration"
            : "User Configuration";
        var result = new List<string>
        {
            root, "Policies", "Windows Settings", "Security Settings",
            "Software Restriction Policies"
        };
        // This is Microsoft's specific folder for path/hash/certificate rules.
        // Only the recognized GPMC Path Rule leaf is routed to Additional Rules.
        if (IsPathRule(setting))
            result.Add("Additional Rules");
        return result;
    }

    /// <summary>
    /// Accept only an explicit final Path field in the GPMC report value.
    /// Do not match the repeated title 'Path Rule', the security level,
    /// a GUID guessed from another record, a substring or an expanded
    /// environment-variable path.
    /// </summary>
    public static bool TryGetPathRuleTarget(PolicySettingInfo setting, out string path)
    {
        path = string.Empty;
        if (!IsPathRule(setting) || string.IsNullOrWhiteSpace(setting.Value))
            return false;

        var segments = setting.Value.Split(';');
        if (segments.Length == 0 || segments.Length > 32)
            return false;

        var matches = segments
            .Select((segment, index) => new { Segment = segment.Trim(), Index = index })
            .Where(part =>
            {
                var equals = part.Segment.IndexOf('=');
                return equals > 0 &&
                    part.Segment[..equals].Trim().Equals(
                        "Path", StringComparison.OrdinalIgnoreCase);
            })
            .ToArray();

        // A semicolon in a path or a second Path field must fail closed;
        // a final, unambiguous Path= record is the only supported format.
        if (matches.Length != 1 || matches[0].Index != segments.Length - 1)
            return false;
        var pair = matches[0].Segment;
        var separator = pair.IndexOf('=');
        var candidate = pair[(separator + 1)..].Trim();
        if (candidate.Length is < 2 or > 4096 ||
            candidate.Any(char.IsControl) ||
            candidate.Contains('"') ||
            candidate.EndsWith('\\'))
            return false;

        path = candidate;
        return true;
    }

    public static bool IsExactAdditionalRulesSection(
        string scope, string sectionPath)
    {
        var root = scope.Equals("Computer", StringComparison.OrdinalIgnoreCase)
            ? "Computer Configuration"
            : scope.Equals("User", StringComparison.OrdinalIgnoreCase)
                ? "User Configuration" : string.Empty;
        if (root.Length == 0)
            return false;

        var expected = new[]
        {
            root, "Policies", "Windows Settings", "Security Settings",
            "Software Restriction Policies", "Additional Rules"
        };
        var actual = sectionPath.Split('>',
            StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return actual.SequenceEqual(expected, StringComparer.OrdinalIgnoreCase);
    }

    public static bool ExactPath(string? actual, string? expected) =>
        !string.IsNullOrWhiteSpace(actual) &&
        !string.IsNullOrWhiteSpace(expected) &&
        actual.Trim().Equals(expected.Trim(), StringComparison.OrdinalIgnoreCase);

    public static bool MatchesExactPathRule(
        PolicySettingInfo setting, string section, string nativeRowName) =>
        TryGetPathRuleTarget(setting, out var path) &&
        IsExactAdditionalRulesSection(setting.Scope, section) &&
        ExactPath(nativeRowName, path);
}

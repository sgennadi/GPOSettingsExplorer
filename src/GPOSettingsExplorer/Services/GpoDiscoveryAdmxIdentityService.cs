using System;
using System.Linq;

namespace GPOSettingsExplorer.Services;

/// <summary>
/// Strictly associates one native Administrative Templates folder with the
/// complete localized ADMX category hierarchy. A suffix match such as
/// "Other > Windows Update" vs "Windows Update" is NOT an exact route.
/// </summary>
public static class GpoDiscoveryAdmxIdentityService
{
    public static bool MatchesExactFolder(string scope, string mmcSection,
        string admxCategory)
    {
        if (string.IsNullOrWhiteSpace(admxCategory) ||
            string.IsNullOrWhiteSpace(mmcSection))
            return false;

        var root = scope.Equals("Computer", StringComparison.OrdinalIgnoreCase)
            ? "Computer Configuration"
            : scope.Equals("User", StringComparison.OrdinalIgnoreCase)
                ? "User Configuration" : string.Empty;
        if (root.Length == 0)
            return false;

        var parts = mmcSection.Split('>',
            StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var category = admxCategory.Split('>',
            StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != category.Length + 3 ||
            category.Length == 0 ||
            !parts[0].Equals(root, StringComparison.OrdinalIgnoreCase) ||
            !parts[1].Equals("Policies", StringComparison.OrdinalIgnoreCase) ||
            !parts[2].Equals("Administrative Templates", StringComparison.OrdinalIgnoreCase))
            return false;

        return parts.Skip(3).SequenceEqual(category, StringComparer.OrdinalIgnoreCase);
    }
}

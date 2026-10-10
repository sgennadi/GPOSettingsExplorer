using GPOSettingsExplorer.Models;

namespace GPOSettingsExplorer.Services;

/// <summary>
/// Compare indexed configured settings without claiming missing index entries
/// are Not Configured. Duplicate identities inside a single GPO must be
/// reported as ambiguous rather than arbitrarily choosing the first value.
/// </summary>
public static class GpoSettingsComparisonService
{
    public static IReadOnlyList<GpoComparisonRow> Compare(
        IEnumerable<PolicySettingInfo> left,
        IEnumerable<PolicySettingInfo> right)
    {
        var leftGroups = GroupSafe(left);
        var rightGroups = GroupSafe(right);
        var keys = leftGroups.Keys.Union(rightGroups.Keys,
                StringComparer.OrdinalIgnoreCase)
            .OrderBy(k => k, StringComparer.OrdinalIgnoreCase);

        var result = new List<GpoComparisonRow>();
        foreach (var id in keys)
        {
            leftGroups.TryGetValue(id, out var leftEntries);
            rightGroups.TryGetValue(id, out var rightEntries);
            var lhs = leftEntries?.FirstOrDefault();
            var rhs = rightEntries?.FirstOrDefault();
            var sample = lhs ?? rhs;
            if (sample is null) continue;

            var lAmbiguous = IsAmbiguous(leftEntries);
            var rAmbiguous = IsAmbiguous(rightEntries);
            var status = lAmbiguous || rAmbiguous
                ? "Ambiguous index"
                : lhs is null ? "Right only (index)"
                : rhs is null ? "Left only (index)"
                : Same(lhs, rhs) ? "Same" : "Different";

            result.Add(new GpoComparisonRow
            {
                Identity = id,
                Scope = sample.Scope,
                SettingName = sample.SettingName,
                Category = sample.Category,
                RegistryKey = sample.RegistryKey,
                RegistryValue = sample.RegistryValue,
                LeftState = FormatState(leftEntries),
                LeftValue = FormatValue(leftEntries),
                RightState = FormatState(rightEntries),
                RightValue = FormatValue(rightEntries),
                Status = status
            });
        }

        return result;
    }

    private static Dictionary<string, PolicySettingInfo[]> GroupSafe(
        IEnumerable<PolicySettingInfo> source) =>
        source.Where(s => !SecurityXmlEntryClassifier.IsTechnicalDetail(s))
            .GroupBy(GpoConflictAnalysisService.Identity,
                StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToArray(),
                StringComparer.OrdinalIgnoreCase);

    private static bool IsAmbiguous(PolicySettingInfo[]? source) =>
        source is { Length: > 1 } &&
        source.Select(s => s.State.Trim() + "|" + s.Value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase).Skip(1).Any();

    private static string FormatState(PolicySettingInfo[]? source) =>
        source is null || source.Length == 0 ? "Not in loaded index" :
        IsAmbiguous(source) ? "Ambiguous indexed values" : source[0].State;

    private static string FormatValue(PolicySettingInfo[]? source) =>
        source is null || source.Length == 0 ? string.Empty :
        IsAmbiguous(source)
            ? string.Join(" | ", source.Select(s => s.Value.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase).Take(6))
            : source[0].Value;

    private static bool Same(PolicySettingInfo a, PolicySettingInfo b) =>
        a.State.Trim().Equals(b.State.Trim(), StringComparison.OrdinalIgnoreCase) &&
        a.Value.Trim().Equals(b.Value.Trim(), StringComparison.OrdinalIgnoreCase);
}

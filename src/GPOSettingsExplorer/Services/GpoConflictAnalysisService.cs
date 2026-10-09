using GPOSettingsExplorer.Models;

namespace GPOSettingsExplorer.Services;

/// <summary>
/// Static comparison of *configured* settings and GPO links. This is not
/// RSoP: user/computer membership, filtering, WMI and CSE processing are not
/// knowable from a GPO index and must never be presented as a proven conflict.
/// </summary>
public static class GpoConflictAnalysisService
{
    public static IReadOnlyList<GpoConflictInfo> Analyze(
        IEnumerable<PolicySettingInfo> settings,
        IReadOnlyList<GpoLinkInfo> links,
        IReadOnlyList<GpoInfo> gpos)
    {
        var names = gpos.ToDictionary(g => g.Id, g => g);
        return settings
            .GroupBy(Identity, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var participants = group
                    .GroupBy(s => s.GpoId)
                    .Select(g => g.First())
                    .OrderBy(s => s.GpoName, StringComparer.CurrentCultureIgnoreCase)
                    .ToArray();
                if (participants.Length < 2)
                    return null;

                var variants = participants
                    .Select(s => (s.State.Trim() + "|" + s.Value.Trim()).ToLowerInvariant())
                    .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                var duplicate = variants.Length == 1;
                var involved = participants.Select(s => s.GpoId).ToHashSet();
                var activeLinks = links.Where(l => involved.Contains(l.GpoId) && l.Enabled)
                    .OrderBy(l => l.TargetDn, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(l => l.Order).ToArray();
                var overlap = AnalyzeOverlap(participants, activeLinks, names);
                var sample = participants[0];
                var gpoStates = participants.Select(s =>
                {
                    var active = !names.TryGetValue(s.GpoId, out var gpo) ||
                        (s.Scope.Equals("User", StringComparison.OrdinalIgnoreCase)
                            ? gpo.UserEnabled : gpo.ComputerEnabled);
                    return s.GpoName + (active ? "" : " [SCOPE DISABLED]") +
                           ": " + s.State +
                           (string.IsNullOrWhiteSpace(s.Value) ? "" : " = " + s.Value);
                });
                var plan = duplicate
                    ? "CONSOLIDATE CANDIDATE: Compare target OUs, WMI/security filtering and scope. " +
                      "If identical applicability is verified, keep the setting in the designated GPO, " +
                      "back up both policies, then remove the duplicate setting from the other via its native editor. " +
                      "Do not remove a link or an entire policy just to deduplicate one setting."
                    : "VALUE MISMATCH: Verify effective scope using gpresult/RSoP on a representative object. " +
                      "Compare the configured values and GPMC Link Order; choose the intended value, then " +
                      "edit the appropriate GPO or link in the native editor after separate backups. " +
                      "Avoid assuming that the highest priority is known from link order alone.";

                return new GpoConflictInfo
                {
                    Identity = group.Key,
                    Scope = sample.Scope,
                    SettingName = sample.SettingName,
                    Category = sample.Category,
                    RegistryKey = sample.RegistryKey,
                    RegistryValue = sample.RegistryValue,
                    Gpos = string.Join(" | ", participants.Select(x => x.GpoName)),
                    Variants = string.Join(" | ", gpoStates),
                    GpoIds = involved.OrderBy(x => x).ToArray(),
                    Kind = duplicate ? "Duplicate" : "Different values",
                    OverlapStatus = overlap.Status,
                    LinkEvidence = overlap.Evidence,
                    Recommendation = plan,
                    Participants = participants,
                    IsPotentialOverlap = overlap.Potential,
                    PriorityNote = "Link Order 1 wins only inside a given container; Enforced, " +
                        "inheritance, loopback, WMI/security filters and CSE rules also matter."
                };
            })
            .Where(item => item is not null)
            .Cast<GpoConflictInfo>()
            .OrderBy(item => item.Kind, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Scope, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.SettingName, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    public static string Identity(PolicySettingInfo setting)
    {
        if (!string.IsNullOrWhiteSpace(setting.RegistryKey) ||
            !string.IsNullOrWhiteSpace(setting.RegistryValue))
            return $"{setting.Scope}|REG|{setting.RegistryKey}|{setting.RegistryValue}";

        return $"{setting.Scope}|NAME|{setting.Category}|{setting.SettingName}";
    }

    private static (string Status, string Evidence, bool Potential) AnalyzeOverlap(
        PolicySettingInfo[] participants,
        GpoLinkInfo[] links,
        IReadOnlyDictionary<Guid, GpoInfo> gpos)
    {
        if (participants.All(s => gpos.TryGetValue(s.GpoId, out var gpo) &&
              !(s.Scope.Equals("User", StringComparison.OrdinalIgnoreCase)
                  ? gpo.UserEnabled : gpo.ComputerEnabled)))
            return ("Scopes disabled", "These GPO scopes are currently disabled.", false);

        if (links.Length == 0)
            return ("Unknown / no links loaded",
                "Load GPO Links before drawing applicability conclusions. " +
                "Disabled links, filters and direct container mappings may affect scope.", false);

        var pairs = from first in links
                    from second in links
                    where first.GpoId != second.GpoId
                    select new { First = first, Second = second };

        foreach (var pair in pairs)
        {
            var a = pair.First;
            var b = pair.Second;
            if (a.TargetDn.Equals(b.TargetDn, StringComparison.OrdinalIgnoreCase))
            {
                return ("Shared target (potential)",
                    $"{a.GpoName} (order {a.Order}) and {b.GpoName} (order {b.Order}) " +
                    $"both link to {a.TargetDn}. Enforced={a.Enforced}/{b.Enforced}. " +
                    "Actual application still depends on filters and target objects.", true);
            }

            if (a.TargetDn.EndsWith("," + b.TargetDn, StringComparison.OrdinalIgnoreCase) ||
                b.TargetDn.EndsWith("," + a.TargetDn, StringComparison.OrdinalIgnoreCase))
            {
                return ("Ancestor/descendant (potential)",
                    $"{a.TargetDn} and {b.TargetDn} are nested link containers. " +
                    "Inheritance or Enforced may apply; verify block-inheritance on the child " +
                    "and filtering before changing values.", true);
            }

            if (a.TargetType.Equals("Site", StringComparison.OrdinalIgnoreCase) ||
                b.TargetType.Equals("Site", StringComparison.OrdinalIgnoreCase))
                return ("Site + domain/OU (uncertain)",
                    "A site link and a domain/OU link can both affect a computer. " +
                    "This snapshot cannot verify site membership or WMI/security filters.", true);
        }

        return ("No shared link path shown",
            "These enabled links do not share an ancestor/descendant container in the loaded " +
            "link snapshot. That is not proof of non-overlap for security/site/loopback cases.", false);
    }
}

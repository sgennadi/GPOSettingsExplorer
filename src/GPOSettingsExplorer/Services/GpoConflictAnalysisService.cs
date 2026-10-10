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
            // GPMC reports complex XML as numerous leaf descriptions. They
            // are not independent policy assignments or conflict candidates.
            .Where(s => !SecurityXmlEntryClassifier.IsTechnicalDetail(s))
            .GroupBy(Identity, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var byGpo = group
                    .GroupBy(s => s.GpoId)
                    .OrderBy(g => g.Key)
                    .ToArray();
                if (byGpo.Length < 2)
                    return null;

                // Different indexed values with the same identity in one
                // GPO cannot be resolved by taking the first entry.
                var ambiguous = byGpo.Any(g => g.Select(VariantIdentity)
                    .Distinct(StringComparer.OrdinalIgnoreCase).Skip(1).Any());
                var participants = byGpo
                    .SelectMany(g => ambiguous ? g : g.Take(1))
                    .OrderBy(s => s.GpoName, StringComparer.CurrentCultureIgnoreCase)
                    .ToArray();
                var variants = participants
                    .Select(VariantIdentity)
                    .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                var duplicate = !ambiguous && variants.Length == 1;
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
                var plan = ambiguous
                    ? "AMBIGUOUS INDEX: one GPO has more than one distinct value for the same policy identity. " +
                      "Verify the original GPMC XML, policy scope and CSE before drawing any duplication or effective-conflict conclusion. " +
                      "Do not merge, unlink or delete based on this result."
                    : duplicate
                    ? "HOLD - NO MERGE RECOMMENDATION: equal configured values are not proof of equal application. " +
                      "Run 'Verify RSoP / WMI / Security' to check a representative computer, then " +
                      "review inheritance, sites, loopback and all affected OU targets. " +
                      "Before an explicitly approved manual cleanup back up both policies separately. " +
                      "Never remove a link or GPO merely to deduplicate one setting."
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
                    Kind = ambiguous ? "Ambiguous index" : duplicate ? "Duplicate" : "Different values",
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
        if (!string.IsNullOrWhiteSpace(setting.RegistryKey) &&
            !string.IsNullOrWhiteSpace(setting.RegistryValue))
            return $"{setting.Scope.Trim()}|REG|{setting.RegistryKey.Trim()}|{setting.RegistryValue.Trim()}";

        // An incomplete registry target is not a verified policy identity:
        // include its CSE and category to avoid unrelated same-label hits.
        return $"{setting.Scope.Trim()}|NAME|{setting.Extension.Trim()}|" +
               $"{setting.Category.Trim()}|{setting.SettingName.Trim()}|" +
               $"{setting.RegistryKey.Trim()}|{setting.RegistryValue.Trim()}";
    }

    private static string VariantIdentity(PolicySettingInfo s) =>
        s.State.Trim() + "|" + s.Value.Trim();

    private static (string Status, string Evidence, bool Potential) AnalyzeOverlap(
        PolicySettingInfo[] participants,
        GpoLinkInfo[] links,
        IReadOnlyDictionary<Guid, GpoInfo> gpos)
    {
        var activeParticipants = participants.Where(s =>
            !gpos.TryGetValue(s.GpoId, out var gpo) ||
            (s.Scope.Equals("User", StringComparison.OrdinalIgnoreCase)
                ? gpo.UserEnabled : gpo.ComputerEnabled)).ToArray();

        if (activeParticipants.Length < 2)
            return ("Not simultaneously active",
                "At least one required Computer/User Configuration scope is currently " +
                "disabled, so this snapshot does not establish a simultaneous active conflict. " +
                "Retain this finding for future enablement review.", false);

        // Exclude links whose GPO policy scope is disabled before comparing
        // affected target containers.
        var activeGpoIds = activeParticipants.Select(s => s.GpoId).ToHashSet();
        links = links.Where(l => activeGpoIds.Contains(l.GpoId)).ToArray();

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

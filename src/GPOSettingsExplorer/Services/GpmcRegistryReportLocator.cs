using GPOSettingsExplorer.Models;

namespace GPOSettingsExplorer.Services;

/// <summary>
/// Resolves one typed GPMC Registry Preference to one item in a fresh GPMC
/// XML Settings report. Never searches by display name alone: duplicate
/// names and the same key used by Delete/Update items are common.
/// Source-only lookup; no native MMC, live registry, SYSVOL write or RSoP.
/// </summary>
public static class GpmcRegistryReportLocator
{
    public const string Exact = "EXACT_GPMC_REGISTRY_SOURCE";
    public const string WrongGpo = "WRONG_GPO";
    public const string MissingScope = "SCOPE_NOT_FOUND";
    public const string MissingUid = "REGISTRY_UID_NOT_FOUND";
    public const string AmbiguousUid = "REGISTRY_UID_AMBIGUOUS";
    public const string StaleMetadata = "GPMC_REGISTRY_METADATA_CHANGED";

    public sealed record Reference(
        Guid GpoId, string Scope, Guid Uid, int Order,
        string RegistryTarget, string Action);

    public sealed record Resolution(string Status, string Explanation,
        GpoReportNode? Node = null)
    {
        public bool IsExact => Status == Exact && Node is not null;
    }

    /// <summary>
    /// A plain registry policy or a technical GPMC XML leaf has no GPP UID
    /// and must never be promoted to an exact GPP Registry source target.
    /// </summary>
    public static bool TryCreate(PolicySettingInfo? setting, out Reference reference)
    {
        reference = null!;
        if (setting is null ||
            setting.GpoId == Guid.Empty ||
            setting.Scope is not ("Computer" or "User") ||
            !GpoSettingSourceProvenance.IsGppRegistrySource(setting) ||
            !Guid.TryParse(setting.GppRegistryUid, out var uid) ||
            uid == Guid.Empty ||
            setting.GppRegistryOrder is < 1 or > 20000 ||
            setting.GppRegistryAction is not ("Create" or "Update" or "Replace" or "Delete"))
            return false;

        var target = setting.GppRegistryHive + "\\" + setting.GppRegistryKeyPath +
            (string.IsNullOrWhiteSpace(setting.GppRegistryValueName)
                ? "" : " / " + setting.GppRegistryValueName);

        reference = new Reference(setting.GpoId, setting.Scope, uid,
            setting.GppRegistryOrder, target, setting.GppRegistryAction);
        return true;
    }

    /// <summary>
    /// Resolution is bound to the same GPO, Computer/User scope, UID, item
    /// order, exact registry path and preference action. A stale cached
    /// index cannot silently select a different item with the same label.
    /// </summary>
    public static Resolution Locate(GpoSettingsReport report, Reference selected)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(selected);

        if (selected.GpoId == Guid.Empty || report.GpoId != selected.GpoId)
            return new(WrongGpo,
                "Selected indexed setting and the fresh Settings report refer to different GPO identities.");

        var titlePrefix = selected.Scope switch
        {
            "Computer" => "Computer Configuration (",
            "User" => "User Configuration (",
            _ => string.Empty
        };
        if (titlePrefix.Length == 0)
            return new(MissingScope, "Computer/User scope is invalid.");

        var scopes = report.Sections.Where(root =>
            root.Label.StartsWith(titlePrefix, StringComparison.Ordinal)).ToArray();
        if (scopes.Length != 1)
            return new(MissingScope,
                "The GPMC report does not contain one unique selected Computer/User scope.");

        var candidates = Walk(scopes[0])
            .Where(node => Guid.TryParse(node.GppRegistryUid, out var uid) &&
                uid == selected.Uid &&
                node.GppRegistryScope.Equals(selected.Scope, StringComparison.Ordinal))
            .ToArray();

        if (candidates.Length == 0)
            return new(MissingUid,
                "This UID was not found in the fresh GPMC report for the selected GPO and scope.");
        if (candidates.Length != 1)
            return new(AmbiguousUid,
                "Multiple GPMC report items have the same Registry preference UID in the same scope.");

        var matched = candidates[0];
        if (matched.GppRegistryOrder != selected.Order ||
            !matched.RegistryTarget.Equals(selected.RegistryTarget,
                StringComparison.OrdinalIgnoreCase) ||
            !matched.GppRegistryAction.Equals(selected.Action,
                StringComparison.Ordinal))
            return new(StaleMetadata,
                "The Registry UID exists but its order, action or full key/value target " +
                "no longer matches the cached GPMC settings index. Refresh the index.");

        return new(Exact,
            "One same-GPO, same-scope, UID/order/action/key Registry preference was found " +
            "in the new GPMC Settings report. Stored source identity only; not a live " +
            "registry or effective client RSoP verification.",
            matched);
    }

    private static IEnumerable<GpoReportNode> Walk(GpoReportNode item)
    {
        yield return item;
        foreach (var child in item.Children)
            foreach (var nested in Walk(child))
                yield return nested;
    }
}

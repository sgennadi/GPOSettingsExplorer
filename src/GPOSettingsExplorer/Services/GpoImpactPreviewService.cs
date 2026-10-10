using GPOSettingsExplorer.Models;

namespace GPOSettingsExplorer.Services;

/// <summary>
/// Conservative, read-only link footprint. It never estimates the number of
/// eligible users/computers from OU links or labels a hypothetical change effective.
/// </summary>
public sealed record GpoImpactLink(
    string Target, string TargetType, string TargetDn,
    int Order, string LinkState, string Inheritance,
    string Evidence);

public sealed record GpoImpactPreview(
    Guid GpoId, string GpoName, string Domain, string PinnedDc,
    DateTimeOffset CapturedAt,
    IReadOnlyList<GpoImpactLink> DirectLinks,
    bool LinkInventoryComplete,
    string GpoSections, string WmiEvidence, string Notes)
{
    public int EnabledLinks => DirectLinks.Count(link => link.LinkState == "Enabled");
    public int DisabledLinks => DirectLinks.Count(link => link.LinkState == "Disabled");
    public string Summary =>
        $"{DirectLinks.Count} directly assigned link(s), {EnabledLinks} enabled, " +
        $"{DisabledLinks} disabled; " +
        (LinkInventoryComplete ? "AD link inventory loaded" : "INCOMPLETE LINK INVENTORY");
    public string ToText()
    {
        var header = $"GPO IMPACT PREVIEW - READ ONLY\nCaptured: {CapturedAt:O}\n" +
            $"GPO: {GpoName} ({GpoId:B})\nDomain: {Domain}\nPinned DC: {PinnedDc}\n" +
            $"Sections: {GpoSections}\nWMI: {WmiEvidence}\n{Summary}\n\n{Notes}\n";
        return header + "\nDirect links (not inferred recipients):\n" +
            string.Join("\n", DirectLinks.Select(link =>
                $"[{link.LinkState}] {link.TargetType}: {link.Target} | {link.TargetDn} | " +
                $"order={link.Order} | {link.Inheritance} | {link.Evidence}"));
    }
}

public static class GpoImpactPreviewService
{
    public static GpoImpactPreview Build(
        GpoInfo gpo, IEnumerable<GpoLinkInfo> links, string domain,
        string dc, bool inventoryComplete)
    {
        ArgumentNullException.ThrowIfNull(gpo);
        ArgumentNullException.ThrowIfNull(links);
        var direct = links.Where(link => link.GpoId == gpo.Id)
            .Select(link => new GpoImpactLink(
                link.TargetName, link.TargetType, link.TargetDn,
                link.Order,
                link.Enabled ? "Enabled" : "Disabled",
                link.BlockInheritance ? "Block inheritance set on target" :
                    "No block on directly linked target",
                (link.Enforced ? "Enforced link" : "Not enforced") +
                "; target GPO processing also depends on Security Filtering, WMI, " +
                "site membership, inherited links, CSE and target processing"))
            .OrderBy(link => link.TargetType, StringComparer.OrdinalIgnoreCase)
            .ThenBy(link => link.TargetDn, StringComparer.OrdinalIgnoreCase)
            .ThenBy(link => link.Order)
            .ToArray();
        var sections = gpo.ComputerEnabled && gpo.UserEnabled ? "Computer + User enabled" :
            gpo.ComputerEnabled ? "Computer enabled; User disabled" :
            gpo.UserEnabled ? "User enabled; Computer disabled" :
            "Computer and User sections disabled";
        var wmi = string.IsNullOrWhiteSpace(gpo.WmiFilterPath)
            ? "No WMI filter assigned in current GPO metadata"
            : string.IsNullOrWhiteSpace(gpo.WmiFilterName)
                ? "WMI filter assigned (not evaluated): " + gpo.WmiFilterPath
                : gpo.WmiFilterName + " (not evaluated)";
        var notes =
            "This is an inventory of DIRECT GPO links, NOT effective application. " +
            "Domain/site/OU links may affect descendants not displayed here. " +
            "Security Filtering/denies, user and computer group membership, " +
            "WMI results, loopback, enforced links, block inheritance, site membership, " +
            "CSE processing, replication and the last client-side processing are unknown. " +
            "A disabled link cannot apply from that link, but the same GPO may " +
            "still be linked elsewhere. Before editing or consolidation, verify " +
            "representative clients with logged RSoP using the sample checker.";
        return new GpoImpactPreview(gpo.Id, gpo.DisplayName, domain, dc,
            DateTimeOffset.Now, direct, inventoryComplete, sections, wmi, notes);
    }
}

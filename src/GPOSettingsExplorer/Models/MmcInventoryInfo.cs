using GPOSettingsExplorer.Models;

namespace GPOSettingsExplorer.Models;

/// <summary>A row observed in the selected GPO editor's right pane.
/// This is not a domain-wide GPO setting or a proof of an effective RSoP result.</summary>
public sealed record MmcInventoryEntry
{
    public Guid GpoId { get; init; }
    public string GpoName { get; init; } = string.Empty;
    public string Scope { get; init; } = string.Empty;
    public string SectionPath { get; init; } = string.Empty;
    public IReadOnlyList<string> TreeSegments { get; init; } = Array.Empty<string>();
    public string SettingName { get; init; } = string.Empty;
    public string MmcState { get; init; } = "Not reported";
    public string MmcValue { get; init; } = string.Empty;
    public string Source { get; init; } = string.Empty;
    public string GpmcMatch { get; init; } = "Not checked";
    public string AdmxMatch { get; init; } = "Not checked";
    public string Navigation { get; init; } = "Verify in MMC";
    public string SearchText => string.Join(" ", new[]
    {
        GpoName, Scope, SectionPath, SettingName, MmcState, MmcValue,
        Source, GpmcMatch, AdmxMatch, Navigation
    });
}

public sealed record MmcInventorySection(
    string Path, string Status, int RowCount, string Details = "");

/// <summary>The scan result is always explicitly bounded. Incomplete scans
/// are never described as an exhaustive MMC policy catalog.</summary>
public sealed record MmcInventoryScanResult(
    IReadOnlyList<MmcInventoryEntry> Rows,
    IReadOnlyList<MmcInventorySection> Sections,
    int NodesVisited,
    bool Interrupted,
    string CompletionReason,
    DateTimeOffset CollectedAt)
{
    public bool IsComplete =>
        !Interrupted && Sections.All(s =>
            s.Status is "Rows read" or "No list" or "Empty list");

    public int Failures => Sections.Count(s =>
        s.Status is "Read error" or "Selection failed" or "Truncated");

    public string Coverage =>
        $"{Rows.Count:N0} observed rows / {NodesVisited:N0} tree nodes / " +
        $"{Sections.Count:N0} sections inspected / {Failures:N0} incomplete; " +
        (IsComplete ? "scan finished without detected read errors" :
            "PARTIAL - " + CompletionReason);
}

/// <summary>Individual native MMC ListView cells (not the concatenated
/// diagnostic string used by the existing Security Options navigation).</summary>
public sealed record MmcNativeListRow(
    int Index, string Name, string Value, string Additional);

public sealed record MmcNativeListSnapshot(
    IReadOnlyList<MmcNativeListRow> Rows,
    bool HasList,
    bool Complete,
    string Error)
{
    public static MmcNativeListSnapshot NoList() =>
        new(Array.Empty<MmcNativeListRow>(), false, true, "");
}

public static class MmcInventoryReconciliation
{
    public static MmcInventoryEntry Reconcile(
        MmcInventoryEntry row,
        IReadOnlyList<PolicySettingInfo> configured,
        IReadOnlyList<AdmxPolicyDefinition>? admx)
    {
        // Same GPO + scope + name + category, never name alone. A policy
        // with the same label in different folders must remain ambiguous.
        var matching = configured.Where(s =>
            s.GpoId == row.GpoId &&
            s.Scope.Equals(row.Scope, StringComparison.OrdinalIgnoreCase) &&
            s.SettingName.Equals(row.SettingName, StringComparison.CurrentCultureIgnoreCase) &&
            SectionMatches(row.SectionPath, s.Category)).ToArray();

        var gpmcStatus = matching.Length switch
        {
            0 => "Not found in indexed GPMC XML (not proof of Not Configured)",
            1 => $"Indexed: {matching[0].State}" +
                 (string.IsNullOrWhiteSpace(matching[0].Value)
                    ? "" : " = " + matching[0].Value),
            _ => $"Ambiguous: {matching.Length} indexed rows"
        };

        var catalog = admx is null
            ? "ADMX catalog not loaded"
            : MatchAdmx(row, admx);

        return row with { GpmcMatch = gpmcStatus, AdmxMatch = catalog };
    }

    private static string MatchAdmx(
        MmcInventoryEntry row, IReadOnlyList<AdmxPolicyDefinition> catalog)
    {
        var matching = catalog.Where(p =>
            (p.Scope.Equals(row.Scope, StringComparison.OrdinalIgnoreCase) ||
             p.Scope.Equals("Both", StringComparison.OrdinalIgnoreCase)) &&
            p.DisplayName.Equals(row.SettingName, StringComparison.CurrentCultureIgnoreCase) &&
            SectionMatches(row.SectionPath, p.Category)).ToArray();

        return matching.Length switch
        {
            0 => "No exact ADMX name/category match",
            1 => "ADMX exact name/category candidate",
            _ => $"Ambiguous: {matching.Length} matching ADMX definitions"
        };
    }

    public static bool SectionMatches(string mmcPath, string category)
    {
        if (string.IsNullOrWhiteSpace(category))
            return false;

        static string Normalize(string text) => string.Join(" > ",
            text.Split('>', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => s.Length > 0));

        var path = Normalize(mmcPath);
        var tail = Normalize(category);
        return path.Equals(tail, StringComparison.CurrentCultureIgnoreCase) ||
               path.EndsWith(" > " + tail, StringComparison.CurrentCultureIgnoreCase);
    }
}

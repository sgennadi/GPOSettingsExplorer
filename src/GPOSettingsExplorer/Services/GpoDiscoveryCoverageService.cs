using System.Text;

namespace GPOSettingsExplorer.Services;

/// <summary>
/// Aggregated, redacted coverage diagnostics for one GPO's captured evidence.
/// No GPO names, setting names, registry paths, values, DCs, hashes or raw
/// exception text are accepted or included in the safe diagnostic output.
/// Counts are observations, not compliance percentages or effective RSoP.
/// </summary>
public static class GpoDiscoveryCoverageService
{
    public sealed record CoverageFinding(
        string Area, string Status, int Count, string Meaning, string NextCheck);

    public sealed record CoverageReport(
        int IndexedRows, int ObservedMmcRows, int LocatedSourceRows,
        int LiteralComparisonRows, int ReviewedUnitRows,
        IReadOnlyList<CoverageFinding> Findings)
    {
        public string ToSafeText()
        {
            var output = new StringBuilder();
            output.AppendLine("GPO SETTINGS EXPLORER - REDACTED DISCOVERY COVERAGE");
            output.AppendLine("No identities, policy values, registry paths or file hashes included.");
            output.AppendLine("This is an indexed-snapshot summary, NOT effective RSoP or compliance.");
            output.AppendLine($"Indexed GPMC rows: {IndexedRows}");
            output.AppendLine($"Exact MMC rows: {ObservedMmcRows}");
            output.AppendLine($"Located Registry.pol source rows: {LocatedSourceRows}");
            output.AppendLine($"Literal comparisons: {LiteralComparisonRows}");
            output.AppendLine($"Operator-reviewed unit comparisons: {ReviewedUnitRows}");
            foreach (var row in Findings)
                output.AppendLine($"{row.Area}\t{row.Status}\t{row.Count}\t{row.Meaning}\t{row.NextCheck}");
            return output.ToString();
        }
    }

    private sealed record Definition(string Meaning, string NextCheck);

    private static readonly IReadOnlyDictionary<string, Definition> Navigation = 
        new Dictionary<string, Definition>(StringComparer.Ordinal)
        {
            ["MMC_ROW_OBSERVED"] = new("One uniquely identified MMC native row was observed.",
                "Compare with a matching, fully parsed source snapshot; not effective RSoP."),
            ["SECTION_CANDIDATE"] = new("Only a candidate MMC policy section is known.",
                "Scan MMC on the pinned DC and inspect exact native row and ADMX category."),
            ["AMBIGUOUS"] = new("Duplicate source identity or multiple MMC row candidates.",
                "Resolve duplicate policy names, categories and GPO row identities manually."),
            ["UNMAPPED"] = new("No supported route from the indexed GPMC extension.",
                "Inspect native GPMC and the extension-specific source parser.")
        };

    private static readonly IReadOnlyDictionary<string, Definition> Source =
        new Dictionary<string, Definition>(StringComparer.Ordinal)
        {
            ["REGISTRY_SOURCE_LOCATED"] = new("One unique Registry.pol source record located.",
                "Validate file completeness, pinned DC, stored type and hash."),
            ["REGISTRY_METADATA"] = new("Registry metadata exists, but a complete source was not matched.",
                "Capture/read the same GPO's Registry.pol from the pinned DC."),
            ["NON_REGISTRY_OR_UNVERIFIED"] = new("Source is not proven to be Registry.pol.",
                "Use a dedicated Security/SecEdit, Kerberos, GPP or CSE reader."),
            ["AMBIGUOUS_REGISTRY_SOURCE"] = new("Multiple source records claim the same target.",
                "Check duplicate Registry.pol entries before comparing values.")
        };

    private static readonly IReadOnlyDictionary<string, Definition> Value =
        new Dictionary<string, Definition>(StringComparer.Ordinal)
        {
            ["NOT_VERIFIED"] = new("Value has not been compared.", "Collect exact MMC and typed source evidence."),
            ["SOURCE_RECORD_LOCATED_VALUE_NOT_COMPARED"] = new("Stored source located; MMC value not safely comparable.",
                "Check native display format, unit semantics, state and source freshness."),
            ["MMC_OBSERVED_SOURCE_NOT_COMPARED"] = new("Native MMC row observed; source not compared.",
                "Capture a unique same-GPO/scope stored source and inspect it."),
            ["MATCH_LITERAL_REG_SZ"] = new("Exact literal REG_SZ text agrees in captured snapshots.",
                "Literal agreement does not establish effective client application."),
            ["MISMATCH_LITERAL_REG_SZ"] = new("Literal REG_SZ text differs in captured snapshots.",
                "Verify source freshness, exact setting identity and MMC representation."),
            ["MATCH_LITERAL_INTEGER"] = new("Canonical literal integer agrees; units are NOT established.",
                "Do not treat this as semantic unit equivalence or effective RSoP."),
            ["MISMATCH_LITERAL_INTEGER"] = new("Canonical literal integer differs; units are NOT established.",
                "Determine documented source and display units before concluding semantic mismatch."),
            ["MATCH_EXPLICIT_MINUTES_HOURS"] = new("Reviewed minutes-to-hours conversion agrees.",
                "Recheck Registry.pol and ADMX/ADML fingerprints before relying on exported evidence."),
            ["MISMATCH_EXPLICIT_MINUTES_HOURS"] = new("Reviewed minutes-to-hours conversion differs.",
                "Recheck Registry.pol, review ticket and native MMC row; no automatic repair.")
        };

    private static readonly IReadOnlyDictionary<string, Definition> Unit =
        new Dictionary<string, Definition>(StringComparer.Ordinal)
        {
            ["NOT_EVALUATED"] = new("No explicit unit mapping was reviewed.",
                "Do not guess time units from numeric ADMX ranges or policy names."),
            ["VERIFIED_EXPLICIT"] = new("Session-only operator-reviewed unit mapping.",
                "Re-read same pinned source and ADMX/ADML before exporting."),
            ["AMBIGUOUS_RULES"] = new("Conflicting or duplicated mapping rules.",
                "Resolve distinct identities; never silently select the first rule."),
            ["UNVERIFIED_RULE"] = new("Missing approval or stale review fingerprints.",
                "Repeat independent unit review with a current source snapshot."),
            ["UNSUPPORTED_UNIT"] = new("Conversion pair is not implemented.",
                "Keep value unverified until an explicitly typed reader is available."),
            ["SOURCE_NOT_VERIFIED"] = new("Typed and hashed DWORD source not verified.",
                "Check source completeness and exact Registry.pol target.")
        };

    private static readonly IReadOnlyDictionary<string, Definition> Review =
        new Dictionary<string, Definition>(StringComparer.Ordinal)
        {
            ["READY_FOR_MANUAL_REVIEW"] = new("Unique candidate for manual unit review only.",
                "Check official unit semantics independently before approving."),
            ["VERIFIED_EXPLICIT"] = new("Session approval and pinned-source recheck recorded.",
                "Approval expires at window close; export requires fresh recheck."),
            ["MMC_NOT_EXACT"] = new("MMC section/row is not uniquely verified.",
                "Capture exact native MMC row with complete section hierarchy."),
            ["SOURCE_NOT_EXACT"] = new("Typed, hashed and unique Registry.pol source unavailable.",
                "Read a complete Registry.pol from the pinned controller."),
            ["MMC_NOT_CONFIGURED"] = new("MMC reports Not Configured.",
                "Do not compare to a configured source value."),
            ["UNSUPPORTED_FORMAT"] = new("Unrecognized native MMC value/units.",
                "Check language and format; do not infer from a number."),
            ["ADMX_NOT_LOADED"] = new("ADMX/ADML definitions are not available.",
                "Load the matching policy definition store."),
            ["AMBIGUOUS_ADMX"] = new("More than one ADMX policy matches the native row.",
                "Resolve exact policy/category and registry target."),
            ["ADMX_NOT_MAPPED"] = new("No exact policy/category match in ADMX.",
                "Verify complete localized MMC category chain."),
            ["AMBIGUOUS_ELEMENT"] = new("ADMX has multiple candidate elements.",
                "Policy-level display cannot identify the unit for one element."),
            ["DECIMAL_ELEMENT_NOT_MAPPED"] = new("No one-to-one decimal registry element found.",
                "Check ADMX element key, value name and source type."),
            ["STALE_OR_UNVERIFIED_RULE"] = new("Review/source fingerprints changed or a recheck failed.",
                "Re-scan pinned source and repeat explicit review.")
        };

    public static CoverageReport Build(
        IEnumerable<GpoDiscoveryEvidenceService.Evidence> evidence,
        IEnumerable<string>? reviewStatuses = null)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        var rows = evidence.ToArray();
        var reviews = reviewStatuses?.ToArray() ?? Array.Empty<string>();
        if (reviewStatuses is not null && reviews.Length != rows.Length)
            throw new ArgumentException(
                "Review status count must equal the evidence row count.",
                nameof(reviewStatuses));

        var findings = new List<CoverageFinding>();
        Append("MMC navigation", Navigation, rows.Select(row => row.NavigationStatus), findings);
        Append("Source provenance", Source, rows.Select(row => row.SourceStatus), findings);
        Append("Value verification", Value, rows.Select(row => row.ValueVerificationStatus), findings);
        Append("Unit mapping", Unit, rows.Select(row => row.UnitMappingStatus), findings);
        if (reviewStatuses is not null)
            Append("Review eligibility", Review, reviews, findings);

        return new CoverageReport(rows.Length,
            rows.Count(row => row.NavigationStatus == "MMC_ROW_OBSERVED"),
            rows.Count(row => row.SourceStatus == "REGISTRY_SOURCE_LOCATED"),
            rows.Count(row => row.ValueVerificationStatus is
                "MATCH_LITERAL_REG_SZ" or "MISMATCH_LITERAL_REG_SZ" or
                "MATCH_LITERAL_INTEGER" or "MISMATCH_LITERAL_INTEGER"),
            rows.Count(row => row.ValueVerificationStatus is
                "MATCH_EXPLICIT_MINUTES_HOURS" or "MISMATCH_EXPLICIT_MINUTES_HOURS"),
            findings);
    }

    private static void Append(string area,
        IReadOnlyDictionary<string, Definition> allowed,
        IEnumerable<string> statuses, List<CoverageFinding> output)
    {
        const string unknown = "OTHER_OR_UNRECOGNIZED";
        foreach (var group in statuses
            // Do not copy arbitrary source values or path-looking "statuses"
            // into UI or the safe report. Only fixed, known codes survive.
            .Select(status => allowed.ContainsKey(status ?? "") ? status : unknown)
            .GroupBy(status => status!, StringComparer.Ordinal)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.Ordinal))
        {
            var meaning = allowed.TryGetValue(group.Key, out var definition)
                ? definition
                : new Definition("Unsupported/unrecognized status; not verified.",
                    "Inspect the local evidence details without sharing confidential values.");
            output.Add(new CoverageFinding(area, group.Key,
                group.Count(), meaning.Meaning, meaning.NextCheck));
        }
    }
}

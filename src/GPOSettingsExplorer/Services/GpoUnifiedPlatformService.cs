using System.Text;
using System.Text.Json;
using GPOSettingsExplorer.Models;

namespace GPOSettingsExplorer.Services;

/// <summary>
/// Unified, offline-capable control plane: merges independent read-only evidence
/// into an intentionally identifier-free report. No AD/SYSVOL/Graph calls, policy
/// edits, background network requests, or inference about effective client RSoP.
/// Missing/inconsistent evidence is UNKNOWN, never PASS.
/// </summary>
public sealed record GpoUnifiedPlatformReport(
    DateTimeOffset GeneratedUtc,
    DateTimeOffset SourceCapturedUtc,
    int StoredRows,
    int ExactRows,
    int ComputerRows,
    int UserRows,
    int UnclassifiedRows,
    int ReadFiles,
    int AbsentOptionalFiles,
    int UnavailableFiles,
    bool SourcePartial,
    bool SecurityReviewed,
    bool SecurityComplete,
    int CriticalSecurityFindings,
    int ReviewSecurityFindings,
    int UnknownSecurityFindings,
    bool HealthReviewed,
    int HealthErrors,
    int HealthWarnings,
    int HealthUnknowns,
    bool LinksReviewed,
    bool LinkInventoryComplete,
    int DirectLinks,
    int EnabledLinks,
    bool IntuneMappingLoaded,
    int EligibleRegistryRows,
    int MappedIntuneCandidates)
{
    public bool NeedsAttention =>
        SourcePartial ||
        ExactRows != StoredRows ||
        !SecurityReviewed || !SecurityComplete ||
        CriticalSecurityFindings > 0 || ReviewSecurityFindings > 0 ||
        UnknownSecurityFindings > 0 ||
        !HealthReviewed || HealthErrors > 0 || HealthUnknowns > 0 ||
        HealthWarnings > 0 ||
        !LinksReviewed || !LinkInventoryComplete;

    public string ToText()
    {
        var lines = new List<string>
        {
            "GPO UNIFIED PLATFORM - 2.0 (READ ONLY / AGGREGATED)",
            "Generated UTC: " + GeneratedUtc.ToUniversalTime().ToString("O"),
            "Source captured UTC: " + SourceCapturedUtc.ToUniversalTime().ToString("O"),
            "Overview: " + (NeedsAttention
                ? "ATTENTION / INCOMPLETE / UNVERIFIED"
                : "OBSERVED CHECKS ONLY - EFFECTIVE CLIENT POLICY UNKNOWN"),
            "",
            "SOURCE SETTINGS",
            "  Stored rows: " + StoredRows,
            "  Exactly represented values: " + ExactRows,
            "  Computer/User/other scope: " +
                ComputerRows + "/" + UserRows + "/" + UnclassifiedRows,
            "  Source files read / optional absent / unavailable: " +
                ReadFiles + " / " + AbsentOptionalFiles + " / " + UnavailableFiles,
            "  Source coverage: " +
                (SourcePartial || ExactRows != StoredRows
                    ? "PARTIAL / UNCERTAIN"
                    : "Observed bounded sources; additional CSEs not assessed"),
            "",
            "SECURITY SOURCE TRIAGE",
            "  Scan: " + (SecurityReviewed
                ? (SecurityComplete ? "Bounded scan completed" : "INCOMPLETE")
                : "NOT CHECKED"),
            "  Critical/review/unknown findings: " +
                CriticalSecurityFindings + "/" + ReviewSecurityFindings + "/" +
                UnknownSecurityFindings,
            "  Findings are heuristics, not proof of exploitation or safety.",
            "",
            "AD/SYSVOL HEALTH (ONE PINNED DC)",
            "  Check: " + (HealthReviewed ? "OBSERVED" : "NOT CHECKED"),
            "  Errors/warnings/unknown: " +
                HealthErrors + "/" + HealthWarnings + "/" + HealthUnknowns,
            "  Matching one DC does not demonstrate DFSR convergence.",
            "",
            "DIRECT GPO LINK INVENTORY",
            "  Inventory: " + (!LinksReviewed
                ? "NOT CHECKED"
                : LinkInventoryComplete ? "OBSERVED" : "INCOMPLETE"),
            "  Direct links / enabled: " + DirectLinks + " / " + EnabledLinks,
            "  Does not infer inheritance, security token, WMI or effective clients.",
            "",
            "INTUNE MAPPING READINESS",
            "  Reviewed CSP map: " +
                (IntuneMappingLoaded ? "LOADED" : "NOT LOADED"),
            "  Eligible exact registry rows / mapped candidates: " +
                EligibleRegistryRows + " / " + MappedIntuneCandidates,
            "  Candidates are not deployable policies or equivalent values.",
            "",
            "LIMITATIONS",
            "  No domain-wide coverage or compliance score is calculated.",
            "  Missing scans, incomplete sources, mismatched identities and",
            "  effective per-client RSoP, token/group/WMI/loopback/CSE execution",
            "  remain unknown. Captures are non-atomic and may become stale.",
            "  Local AI, if explicitly invoked, receives ONLY these counts.",
            "  No automatic editing, deployment, credential handling or upload."
        };
        return string.Join("\n", lines);
    }
}

public static class GpoUnifiedPlatformService
{
    /// <summary>
    /// JSON contains ONLY the fixed-schema aggregate report. Unlike general
    /// Advanced Analysis exports, it is designed for controlled review without
    /// plaintext GPO/AD identities. Operator chooses the destination.
    /// </summary>
    public static string ToSafeJson(GpoUnifiedPlatformReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        return JsonSerializer.Serialize(new
        {
            Schema = "gposes-unified-safe-summary-v1",
            Limitations = "Anonymous bounded counts only; no effective-client RSoP, " +
                          "domain-wide compliance or automatic policy application.",
            Report = report
        }, new JsonSerializerOptions { WriteIndented = true });
    }

    public static void ExportSafeJson(string path, GpoUnifiedPlatformReport report)
    {
        var json = ToSafeJson(report);
        if (Encoding.UTF8.GetByteCount(json) > 65536)
            throw new InvalidDataException("Aggregated report unexpectedly exceeded 64 KiB.");

        var destination = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(destination) ??
            throw new IOException("Invalid report destination.");
        Directory.CreateDirectory(directory);
        var staging = Path.Combine(directory,
            "." + Path.GetFileName(destination) + "." +
            Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var output = new FileStream(staging, FileMode.CreateNew,
                       FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(output, new UTF8Encoding(false)))
            {
                writer.Write(json);
                writer.Flush();
                output.Flush(flushToDisk: true);
            }
            File.Move(staging, destination, overwrite: true);
        }
        finally
        {
            try { if (File.Exists(staging)) File.Delete(staging); }
            catch (IOException) { }
        }
    }


    public static GpoUnifiedPlatformReport Build(
        RealSettingsScanResult source,
        GpoSecurityScan? security = null,
        GpoConsistencyReport? health = null,
        GpoImpactPreview? impact = null,
        GpoIntuneMappingDocument? mapping = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.GpoId == Guid.Empty || source.Rows is null || source.Files is null ||
            source.Rows.Any(x => x.GpoId != source.GpoId))
            throw new InvalidDataException("Unified source has inconsistent GPO identities.");
        if (source.Rows.Count > GpoGitOpsReviewService.MaxEntries)
            throw new InvalidDataException("Unified source exceeds the approved row cap.");

        if (security is not null && security.GpoId != source.GpoId)
            throw new InvalidOperationException(
                "Security evidence belongs to another GPO. Recapture before combining.");
        if (health is not null &&
            (health.GpoId != source.GpoId ||
             !health.Domain.Equals(source.Domain, StringComparison.OrdinalIgnoreCase) ||
             !health.DomainController.Equals(source.DomainController,
                 StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException(
                "AD/SYSVOL health evidence belongs to another GPO, domain or DC.");
        if (impact is not null &&
            (impact.GpoId != source.GpoId ||
             !impact.Domain.Equals(source.Domain, StringComparison.OrdinalIgnoreCase) ||
             !impact.PinnedDc.Equals(source.DomainController,
                 StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException(
                "Link evidence belongs to another GPO, domain or pinned DC.");

        // Count only known, trusted categories. Unknown/novel scanner output
        // remains unknown and can never be quietly considered low severity.
        var critical = security?.Findings.Count(f => f.Severity == "Critical") ?? 0;
        var review = security?.Findings.Count(f => f.Severity == "Review") ?? 0;
        var unknown = security?.Findings.Count(f =>
            f.Severity is not ("Critical" or "Review")) ?? 0;

        var assessment = GpoIntuneMigrationService.Assess(source, mapping);
        return new GpoUnifiedPlatformReport(
            DateTimeOffset.UtcNow,
            source.CapturedAt,
            source.Rows.Count,
            source.Rows.Count(GpoSourceValueCompleteness.IsExactProjection),
            source.Rows.Count(r => r.Scope == "Computer"),
            source.Rows.Count(r => r.Scope == "User"),
            source.Rows.Count(r => r.Scope is not ("Computer" or "User")),
            source.Files.Count(f => f.Status == "Read"),
            source.Files.Count(f => f.Status == "Absent"),
            source.Files.Count(f => f.Status is not ("Read" or "Absent")),
            source.IsPartial,
            security is not null,
            security?.Complete == true,
            critical,
            review,
            unknown,
            health is not null,
            health?.Findings.Count(f => f.Status == "Error") ?? 0,
            health?.Findings.Count(f => f.Status == "Warning") ?? 0,
            health?.Findings.Count(f => f.Status is not ("Pass" or "Error" or "Warning" or "Info")) ?? 0,
            impact is not null,
            impact?.LinkInventoryComplete == true,
            impact?.DirectLinks.Count ?? 0,
            impact?.EnabledLinks ?? 0,
            mapping is not null,
            assessment.EligibleRegistryRows,
            assessment.MappingMatches);
    }
}

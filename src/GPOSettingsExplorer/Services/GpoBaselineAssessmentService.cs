using System.Text.Json;
using GPOSettingsExplorer.Models;

namespace GPOSettingsExplorer.Services;

/// <summary>
/// Assesses exact source-value observations against an operator-supplied,
/// versioned normalized baseline. This is not an embedded official Microsoft
/// baseline; missing/unreadable policies must never appear compliant.
/// </summary>
public sealed record GpoBaselineRule(
    string Id, string Scope, string Category, string Setting,
    string Expected, string RegistryKey = "", string RegistryValue = "");
public sealed record GpoBaselineDocument(
    string Schema, string Source, string Version,
    IReadOnlyList<GpoBaselineRule> Rules);
public sealed record GpoBaselineFinding(
    string RuleId, string Status, string Details);
public sealed record GpoBaselineAssessment(
    string Name, string Version, bool SourcePartial,
    IReadOnlyList<GpoBaselineFinding> Findings)
{
    public string ToText() =>
        "GPO CONFIGURATION BASELINE ASSESSMENT (READ ONLY)\n" +
        "Baseline: " + Name + " (" + Version + ")\n" +
        "Source coverage: " + (SourcePartial ? "INCOMPLETE" : "bounded files scanned") +
        "\nObserved matches do not prove effective RSoP or compliance for all target clients.\n\n" +
        string.Join("\n", Findings.Select(f =>
            "[" + f.Status + "] " + f.RuleId + ": " + f.Details));
}

public static class GpoBaselineAssessmentService
{
    public const int MaxDocumentBytes = 512 * 1024;

    public static GpoBaselineDocument Load(string path)
    {
        var bytes = OfflineGpoSourceService.ReadBounded(path, MaxDocumentBytes);
        var doc = JsonSerializer.Deserialize<GpoBaselineDocument>(
            bytes, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (doc?.Schema != "gposes-baseline-v1" ||
            string.IsNullOrWhiteSpace(doc.Source) || doc.Source.Length > 500 ||
            doc.Rules is null || doc.Rules.Count is < 1 or > 5000 ||
            doc.Rules.Any(r => r is null ||
                string.IsNullOrWhiteSpace(r.Id) || r.Id.Length > 256 ||
                r.Scope is not ("Computer" or "User") ||
                string.IsNullOrWhiteSpace(r.Setting) || r.Setting.Length > 1024 ||
                r.Category.Length > 1024 || r.Expected.Length > 16_384))
            throw new InvalidDataException(
                "Invalid or unsupported baseline. Use gposes-baseline-v1 with exact rules.");
        if (doc.Rules.Select(r => r.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count()
            != doc.Rules.Count)
            throw new InvalidDataException("Duplicate baseline rule IDs are ambiguous.");
        return doc;
    }

    public static GpoBaselineAssessment Assess(
        RealSettingsScanResult source, GpoBaselineDocument baseline)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(baseline);
        var findings = new List<GpoBaselineFinding>();
        foreach (var rule in baseline.Rules)
        {
            var matches = source.Rows.Where(row =>
                row.Scope.Equals(rule.Scope, StringComparison.OrdinalIgnoreCase) &&
                row.Category.Equals(rule.Category, StringComparison.OrdinalIgnoreCase) &&
                row.SettingName.Equals(rule.Setting, StringComparison.OrdinalIgnoreCase) &&
                (string.IsNullOrEmpty(rule.RegistryKey) ||
                 row.RegistryKey.Equals(rule.RegistryKey, StringComparison.OrdinalIgnoreCase)) &&
                (string.IsNullOrEmpty(rule.RegistryValue) ||
                 row.RegistryValue.Equals(rule.RegistryValue, StringComparison.OrdinalIgnoreCase))
            ).ToArray();
            var result = matches.Length switch
            {
                0 => new GpoBaselineFinding(rule.Id, "Unknown",
                    "Not observed in selected stored source files; do not infer Not Configured."),
                > 1 => new GpoBaselineFinding(rule.Id, "Ambiguous",
                    "More than one source entry matches; no compliant conclusion."),
                _ when matches[0].Value.Equals(rule.Expected, StringComparison.Ordinal) =>
                    new GpoBaselineFinding(rule.Id, "Observed match",
                        "Exact expected stored value observed; effective policy unverified."),
                _ => new GpoBaselineFinding(rule.Id, "Mismatch",
                    "Observed stored value differs from the expected baseline; " +
                    "the actual value is omitted from this report.")
            };
            findings.Add(result);
        }
        return new GpoBaselineAssessment(
            baseline.Source, baseline.Version, source.IsPartial, findings);
    }
}
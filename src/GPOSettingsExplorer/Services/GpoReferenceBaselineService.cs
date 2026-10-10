using GPOSettingsExplorer.Models;

namespace GPOSettingsExplorer.Services;

/// <summary>
/// Compares exactly observed stored settings from a selected GPO with a
/// separately loaded, administrator-reviewed GPMC baseline backup. This does
/// NOT imply that a GPO baseline equals Microsoft's current official baselines,
/// target security posture, or client effective policy.
/// </summary>
public sealed record GpoReferenceFinding(
    string Setting, string Status, string Details);

public sealed record GpoReferenceBaselineReport(
    string Target, string Baseline, DateTimeOffset CapturedUtc,
    bool TargetPartial, bool BaselinePartial,
    IReadOnlyList<GpoReferenceFinding> Findings)
{
    public int Matching => Findings.Count(f => f.Status == "Observed match");
    public int Different => Findings.Count(f => f.Status == "Observed difference");
    public int Unknown => Findings.Count(f => f.Status == "Unknown" ||
                                            f.Status == "Ambiguous");
    public string ToText()
    {
        var lines = new List<string>
        {
            "GPO REFERENCE BASELINE (READ ONLY)",
            "Captured UTC: " + CapturedUtc.ToString("O"),
            "Target source: " + Target,
            "Reference backup: " + Baseline,
            "Coverage: target " + (TargetPartial ? "PARTIAL" : "bounded sources read") +
                ", reference " + (BaselinePartial ? "PARTIAL" : "bounded sources read"),
            "Observed same " + Matching + ", observed different " + Different +
                ", unknown/ambiguous " + Unknown,
            "This is NOT a Microsoft-certified benchmark, domain-wide compliance " +
                "score, or effective-client RSoP.",
            "A reference GPMC backup must come from a vetted baseline; raw values " +
                "are not included in this report.",
            ""
        };
        lines.AddRange(Findings.Take(1500).Select(f =>
            "[" + f.Status + "] " + f.Setting + ": " + f.Details));
        if (Findings.Count > 1500)
            lines.Add("[Truncated] Additional findings omitted: " +
                      (Findings.Count - 1500));
        return string.Join("\n", lines);
    }
}

public static class GpoReferenceBaselineService
{
    public static GpoReferenceBaselineReport Compare(
        RealSettingsScanResult target, RealSettingsScanResult baseline)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(baseline);
        if (baseline.Rows.Count == 0)
            throw new InvalidDataException(
                "Reference GPMC backup contains no supported stored settings; " +
                "cannot assess a baseline.");

        static string Key(RealSettingRecord row) =>
            string.Join("\u001F", row.Scope.ToUpperInvariant(),
                row.Category.ToUpperInvariant(), row.SettingName.ToUpperInvariant(),
                row.RegistryKey.ToUpperInvariant(), row.RegistryValue.ToUpperInvariant());

        var targetGroups = target.Rows.GroupBy(Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(),
                StringComparer.Ordinal);
        var findings = new List<GpoReferenceFinding>();
        foreach (var group in baseline.Rows.GroupBy(Key, StringComparer.Ordinal)
                     .OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var first = group.First();
            var identity = string.Join(" | ", first.Scope, first.Category,
                first.SettingName, first.RegistryKey, first.RegistryValue);
            var referenceRows = group.ToArray();
            var currentRows = targetGroups.GetValueOrDefault(group.Key) ??
                Array.Empty<RealSettingRecord>();

            if (referenceRows.Length != 1 || currentRows.Length > 1)
            {
                findings.Add(new(identity, "Ambiguous",
                    "Duplicate configured source identities; no comparison conclusion."));
                continue;
            }
            if (currentRows.Length == 0)
            {
                findings.Add(new(identity, "Unknown",
                    "Target policy did not expose this supported reference row. " +
                    "Do not infer Not Configured or effective noncompliance."));
                continue;
            }

            var a = referenceRows[0];
            var b = currentRows[0];
            if (!GpoSourceValueCompleteness.IsExactProjection(a) ||
                !GpoSourceValueCompleteness.IsExactProjection(b))
            {
                findings.Add(new(identity, "Unknown",
                    "A displayed source value is redacted, truncated or only partly " +
                    "projected; equality cannot be verified from the UI data."));
                continue;
            }
            var matches = a.Value.Equals(b.Value, StringComparison.Ordinal) &&
                          a.ValueType.Equals(b.ValueType, StringComparison.Ordinal) &&
                          a.State.Equals(b.State, StringComparison.Ordinal);
            findings.Add(new(identity,
                matches ? "Observed match" : "Observed difference",
                matches
                    ? "Exactly matching stored value, type and state. " +
                      "Effective target processing is unverified."
                    : "Stored value/type/state differs from the reviewed reference; " +
                      "value details intentionally omitted from the report."));
        }
        return new GpoReferenceBaselineReport(
            target.GpoName + " (" + target.GpoId.ToString("B") + ")",
            baseline.GpoName + " (" + baseline.GpoId.ToString("B") + ")",
            DateTimeOffset.UtcNow, target.IsPartial, baseline.IsPartial, findings);
    }
}

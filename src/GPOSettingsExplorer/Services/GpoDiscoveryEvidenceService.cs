using System.Security.Cryptography;
using System.Globalization;
using System.Text;
using GPOSettingsExplorer.Models;

namespace GPOSettingsExplorer.Services;

/// <summary>
/// Evidence-first discovery of setting identity and navigation readiness.
/// No MMC invocation, network writes, or inference of effective RSoP.
/// </summary>
public static partial class GpoDiscoveryEvidenceService
{
    public sealed record Evidence(
        Guid GpoId, string Scope, string Extension, string SettingName,
        string SourceType, string SourceStatus, string RegistryTarget,
        string MmcSection, string NavigationStatus, string IdentityHash,
        string Notes)
    {
        public string MmcObservedState { get; init; } = string.Empty;
        public string MmcObservedValue { get; init; } = string.Empty;
        public string MmcObservedSection { get; init; } = string.Empty;
        public string MmcRowNavigation { get; init; } = string.Empty;
        public string ValueVerificationStatus { get; init; } = "NOT_VERIFIED";
        public string SourceStoredValue { get; init; } = string.Empty;
        public string SourceStoredType { get; init; } = string.Empty;
        public string SourceFileHash { get; init; } = string.Empty;
        public string SourceUnit { get; init; } = string.Empty;
        public string UnitMappingStatus { get; init; } = "NOT_EVALUATED";
        public VerifiedUnitMapping? UnitMappingEvidence { get; init; }
    }

    public static IReadOnlyList<Evidence> Build(
        IEnumerable<PolicySettingInfo> settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var candidates = settings.Select(setting =>
        {
            var route = GpoPolicySectionRoutes.Resolve(setting);
            var registry = GpoSettingSourceProvenance.RegistryTarget(setting);
            var source = GpoSettingSourceProvenance.SourceType(setting);
            var status = route.Count == 0 ? "UNMAPPED" : "SECTION_CANDIDATE";
            var notes = route.Count == 0
                ? "No verified MMC route. Open the GPO editor manually."
                : "Derived section only; exact row and actual MMC presence not verified.";
            var identity = string.Join("\u001f",
                setting.GpoId.ToString("D"), setting.Scope, setting.Extension,
                setting.Category, setting.SettingName, registry);
            var hash = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
            return new Evidence(setting.GpoId, setting.Scope, setting.Extension,
                setting.SettingName, source,
                string.IsNullOrWhiteSpace(registry) ? "NON_REGISTRY_OR_UNVERIFIED" : "REGISTRY_METADATA",
                registry, string.Join(" > ", route), status, hash, notes);
        }).ToArray();

        // Two indexed GPMC records with an identical identity are not two
        // independent proofs. Never give each the same MMC/source match.
        var duplicated = candidates
            .GroupBy(row => row.IdentityHash, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.Ordinal);
        return candidates.Select(row => duplicated.Contains(row.IdentityHash)
            ? row with
            {
                NavigationStatus = "AMBIGUOUS",
                Notes = row.Notes + " Duplicate GPMC setting identity; exact row/value verification blocked."
            }
            : row).ToArray();
    }

    /// <summary>
    /// Promote a candidate to an observed section/row only if a unique native
    /// MMC inventory entry agrees on GPO, scope, literal name and section.
    /// An MMC observation does not validate the underlying configured value.
    /// </summary>
    public static IReadOnlyList<Evidence> CorrelateMmc(
        IEnumerable<Evidence> evidence, IEnumerable<MmcInventoryEntry> observed,
        IReadOnlyList<AdmxPolicyDefinition>? admxCatalog = null)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentNullException.ThrowIfNull(observed);
        var rows = observed.ToArray();
        return evidence.Select(item =>
        {
            if (item.NavigationStatus != "SECTION_CANDIDATE")
                return item;

            var matches = rows.Where(row =>
                row.GpoId == item.GpoId &&
                row.Scope.Equals(item.Scope, StringComparison.OrdinalIgnoreCase) &&
                row.SettingName.Equals(item.SettingName, StringComparison.OrdinalIgnoreCase) &&
                (row.TreeSegments.SequenceEqual(
                     item.MmcSection.Split(" > ", StringSplitOptions.RemoveEmptyEntries),
                     StringComparer.OrdinalIgnoreCase) ||
                 IsExactAdmxObservation(item, row, admxCatalog))).ToArray();

            return matches.Length switch
            {
                1 => item with
                {
                    NavigationStatus = "MMC_ROW_OBSERVED",
                    MmcObservedState = matches[0].MmcState,
                    MmcObservedValue = matches[0].MmcValue,
                    MmcObservedSection = matches[0].SectionPath,
                    MmcRowNavigation = matches[0].Navigation,
                    ValueVerificationStatus = "MMC_OBSERVED_SOURCE_NOT_COMPARED",
                    Notes = "One matching native MMC row observed. Value and effective RSoP are not verified."
                },
                > 1 => item with
                {
                    NavigationStatus = "AMBIGUOUS",
                    Notes = "Multiple matching MMC rows; manual review required."
                },
                _ => item
            };
        }).ToArray();
    }

    // An Administrative Templates route is only a parent section. Promote a
    // nested MMC row only when one exact ADMX policy AND one exact decimal
    // registry element identify the row's folder and stored target. Avoid
    // the tempting but unsafe same-display-name-only fallback.
    private static bool IsExactAdmxObservation(
        Evidence item, MmcInventoryEntry row,
        IReadOnlyList<AdmxPolicyDefinition>? catalog)
    {
        if (catalog is null ||
            item.SourceStatus != "REGISTRY_METADATA" ||
            string.IsNullOrWhiteSpace(item.RegistryTarget) ||
            row.Navigation != "Exact MMC row candidate")
            return false;

        var prefix = item.MmcSection.Split(" > ",
            StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (prefix.Length != 3 ||
            !prefix[2].Equals("Administrative Templates", StringComparison.OrdinalIgnoreCase) ||
            row.TreeSegments.Count <= prefix.Length ||
            !row.TreeSegments.Take(prefix.Length).SequenceEqual(
                prefix, StringComparer.OrdinalIgnoreCase))
            return false;

        var definitions = catalog.Where(policy =>
            (policy.Scope.Equals(item.Scope, StringComparison.OrdinalIgnoreCase) ||
             policy.Scope.Equals("Both", StringComparison.OrdinalIgnoreCase)) &&
            policy.DisplayName.Equals(item.SettingName, StringComparison.Ordinal) &&
            MmcInventoryReconciliation.SectionMatches(row.SectionPath, policy.Category) &&
            // A native MMC row displays a policy-level value, not the
            // individual field name. Multiple ADMX elements make the
            // field-to-display-unit association ambiguous.
            policy.Elements.Count == 1 &&
            policy.Elements[0].Type == AdmxElementType.Decimal &&
            !string.IsNullOrWhiteSpace(policy.Elements[0].ValueName) &&
            string.Equals(
                policy.Elements[0].Key + " / " + policy.Elements[0].ValueName,
                item.RegistryTarget, StringComparison.OrdinalIgnoreCase)).ToArray();

        return definitions.Length == 1;
    }

    /// <summary>
    /// A conservative source cross-check: only a unique same-GPO, same-scope,
    /// exact Registry.pol target is compared. Other CSE source formats and
    /// MMC display units require dedicated parsers and remain unverified.
    /// </summary>
    public static IReadOnlyList<Evidence> CorrelateRegistrySource(
        IEnumerable<Evidence> evidence, IEnumerable<RealSettingRecord> sources)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentNullException.ThrowIfNull(sources);
        var records = sources.ToArray();
        return evidence.Select(item =>
        {
            if (item.SourceStatus != "REGISTRY_METADATA" ||
                string.IsNullOrWhiteSpace(item.RegistryTarget))
                return item;

            var matching = records.Where(record =>
                record.GpoId == item.GpoId &&
                record.Scope.Equals(item.Scope, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(
                    string.IsNullOrWhiteSpace(record.RegistryValue)
                        ? record.RegistryKey
                        : record.RegistryKey + " / " + record.RegistryValue,
                    item.RegistryTarget, StringComparison.OrdinalIgnoreCase) &&
                record.SourceFile.EndsWith("Registry.pol", StringComparison.OrdinalIgnoreCase))
                .ToArray();

            return matching.Length switch
            {
                1 => item with
                {
                    SourceStatus = "REGISTRY_SOURCE_LOCATED",
                    ValueVerificationStatus = "SOURCE_RECORD_LOCATED_VALUE_NOT_COMPARED",
                    SourceStoredValue = matching[0].Value,
                    SourceStoredType = matching[0].ValueType,
                    SourceFileHash = matching[0].SourceSha256,
                    Notes = item.Notes + " Unique Registry.pol target located; GPMC/MMC value and units not compared."
                },
                > 1 => item with
                {
                    SourceStatus = "AMBIGUOUS_REGISTRY_SOURCE",
                    ValueVerificationStatus = "NOT_VERIFIED",
                    Notes = item.Notes + " Duplicate Registry.pol source targets; manual review required."
                },
                _ => item
            };
        }).ToArray();
    }

    /// <summary>
    /// Compare only a narrowly supported, unambiguous literal registry value.
    /// MMC display strings are not a typed API: never infer units, enabled state,
    /// security-template values, or effective RSoP from textual resemblance.
    /// </summary>
    public static IReadOnlyList<Evidence> VerifyLiteralRegistryValues(IEnumerable<Evidence> evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        return evidence.Select(item =>
        {
            if (item.NavigationStatus != "MMC_ROW_OBSERVED" ||
                item.SourceStatus != "REGISTRY_SOURCE_LOCATED" ||
                string.IsNullOrWhiteSpace(item.MmcObservedValue) ||
                string.IsNullOrWhiteSpace(item.SourceStoredType) ||
                !IsSha256(item.SourceFileHash))
                return item;

            // REG_SZ is a literal string; numeric types require explicit MMC units.
            // Refuse comparisons where the source is a registry operation directive.
            if (item.RegistryTarget.Contains(" / **", StringComparison.Ordinal))
                return item;
            // An explicit mapping must be independently reviewed and remain
            // bound to this exact policy and Registry.pol snapshot.
            // A SourceUnit string alone is never authorization to convert.
            if (item.UnitMappingStatus != "NOT_EVALUATED" &&
                item.UnitMappingStatus != "VERIFIED_EXPLICIT")
                return item;
            if (item.UnitMappingStatus == "VERIFIED_EXPLICIT" &&
                item.UnitMappingEvidence is { } proof &&
                IsVerifiedUnitProof(item, proof) &&
                item.SourceUnit.Equals("minutes", StringComparison.Ordinal) &&
                IsCanonicalDecimal(item.SourceStoredValue) &&
                uint.TryParse(item.SourceStoredValue, NumberStyles.None,
                    CultureInfo.InvariantCulture, out var sourceMinutes) &&
                TryParseHoursDisplay(item.MmcObservedValue, out var displayHours))
            {
                return item with
                {
                    ValueVerificationStatus = (ulong)sourceMinutes == (ulong)displayHours * 60UL
                        ? "MATCH_EXPLICIT_MINUTES_HOURS" : "MISMATCH_EXPLICIT_MINUTES_HOURS",
                    Notes = item.Notes + " Reviewed and fingerprint-bound minutes-to-hours mapping; effective RSoP not verified."
                };
            }
            // Failed proof, unsupported display format, stale hashes or an
            // unreviewed SourceUnit must never fall through to literal integers.
            if (item.UnitMappingStatus == "VERIFIED_EXPLICIT" ||
                !string.IsNullOrEmpty(item.SourceUnit))
                return item;

            if (item.SourceStoredType.Equals("REG_DWORD", StringComparison.OrdinalIgnoreCase) ||
                item.SourceStoredType.Equals("REG_QWORD", StringComparison.OrdinalIgnoreCase))
            {
                if (!IsCanonicalDecimal(item.MmcObservedValue) ||
                    !IsCanonicalDecimal(item.SourceStoredValue) ||
                    !ulong.TryParse(item.MmcObservedValue, NumberStyles.None,
                        CultureInfo.InvariantCulture, out var displayed) ||
                    !ulong.TryParse(item.SourceStoredValue, NumberStyles.None,
                        CultureInfo.InvariantCulture, out var stored) ||
                    (item.SourceStoredType.Equals("REG_DWORD", StringComparison.OrdinalIgnoreCase) &&
                     (displayed > uint.MaxValue || stored > uint.MaxValue)))
                    return item;
                return item with
                {
                    ValueVerificationStatus = displayed == stored
                        ? "MATCH_LITERAL_INTEGER" : "MISMATCH_LITERAL_INTEGER",
                    Notes = item.Notes + " Canonical unsigned decimal comparison only; MMC units and effective RSoP not verified."
                };
            }
            if (!item.SourceStoredType.Equals("REG_SZ", StringComparison.OrdinalIgnoreCase))
                return item;

            // No trimming or case folding: a literal value must match exactly.
            return item with
            {
                ValueVerificationStatus = string.Equals(
                    item.MmcObservedValue, item.SourceStoredValue,
                    StringComparison.Ordinal) ? "MATCH_LITERAL_REG_SZ" : "MISMATCH_LITERAL_REG_SZ",
                Notes = item.Notes + " Literal REG_SZ comparison only; effective RSoP not verified."
            };
        }).ToArray();
    }

    private static bool TryParseHoursDisplay(string text, out uint hours)
    {
        hours = 0;
        const string suffix = " hours";
        if (!text.EndsWith(suffix, StringComparison.Ordinal))
            return false;
        var number = text[..^suffix.Length];
        return IsCanonicalDecimal(number) &&
            uint.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out hours);
    }

    private static bool IsCanonicalDecimal(string value) =>
        value.Length > 0 && value.All(c => c >= '0' && c <= '9') &&
        (value.Length == 1 || value[0] != '0');

    public static string ExportTsv(IEnumerable<Evidence> evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        static string Safe(string? value)
        {
            var clean = (value ?? "").Replace('\t', ' ').Replace('\r', ' ')
                .Replace('\n', ' ');
            // Prevent spreadsheet formula injection when importing TSV.
            return clean.Length > 0 && "=+-@".Contains(clean[0])
                ? "'" + clean : clean;
        }

        var output = new StringBuilder();
        output.AppendLine("GpoId\tScope\tExtension\tSettingName\tSourceType\tSourceStatus\tRegistryTarget\tMmcSection\tNavigationStatus\tIdentityHash\tNotes\tMmcObservedState\tMmcObservedValue\tMmcObservedSection\tMmcRowNavigation\tValueVerificationStatus\tSourceStoredValue\tSourceStoredType\tSourceFileHash\tSourceUnit\tUnitMappingStatus\tUnitMappingProvenance\tUnitMappingDefinitionSha256\tUnitMappingReviewReference");
        foreach (var row in evidence)
            output.AppendLine(string.Join("\t", new[]
            {
                row.GpoId.ToString("D"), row.Scope, row.Extension, row.SettingName,
                row.SourceType, row.SourceStatus, row.RegistryTarget, row.MmcSection,
                row.NavigationStatus, row.IdentityHash, row.Notes,
                row.MmcObservedState, row.MmcObservedValue, row.MmcObservedSection,
                row.MmcRowNavigation, row.ValueVerificationStatus,
                row.SourceStoredValue, row.SourceStoredType, row.SourceFileHash, row.SourceUnit,
                row.UnitMappingStatus, row.UnitMappingEvidence?.Provenance ?? string.Empty,
                row.UnitMappingEvidence?.DefinitionEvidenceSha256 ?? string.Empty,
                row.UnitMappingEvidence?.ReviewReference ?? string.Empty
            }.Select(Safe)));
        return output.ToString();
    }
}

using System.Security.Cryptography;
using System.Text;
using GPOSettingsExplorer.Models;

namespace GPOSettingsExplorer.Services;

/// <summary>
/// Evidence-first discovery of setting identity and navigation readiness.
/// No MMC invocation, network writes, or inference of effective RSoP.
/// </summary>
public static class GpoDiscoveryEvidenceService
{
    public sealed record Evidence(
        Guid GpoId, string Scope, string Extension, string SettingName,
        string SourceType, string SourceStatus, string RegistryTarget,
        string MmcSection, string NavigationStatus, string IdentityHash,
        string Notes)
    {
        public string MmcObservedState { get; init; } = string.Empty;
        public string MmcObservedValue { get; init; } = string.Empty;
        public string ValueVerificationStatus { get; init; } = "NOT_VERIFIED";
    }

    public static IReadOnlyList<Evidence> Build(
        IEnumerable<PolicySettingInfo> settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return settings.Select(setting =>
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
    }

    /// <summary>
    /// Promote a candidate to an observed section/row only if a unique native
    /// MMC inventory entry agrees on GPO, scope, literal name and section.
    /// An MMC observation does not validate the underlying configured value.
    /// </summary>
    public static IReadOnlyList<Evidence> CorrelateMmc(
        IEnumerable<Evidence> evidence, IEnumerable<MmcInventoryEntry> observed)
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
                row.TreeSegments.SequenceEqual(
                    item.MmcSection.Split(" > ", StringSplitOptions.RemoveEmptyEntries),
                    StringComparer.OrdinalIgnoreCase)).ToArray();

            return matches.Length switch
            {
                1 => item with
                {
                    NavigationStatus = "MMC_ROW_OBSERVED",
                    MmcObservedState = matches[0].MmcState,
                    MmcObservedValue = matches[0].MmcValue,
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
        output.AppendLine("GpoId\tScope\tExtension\tSettingName\tSourceType\tSourceStatus\tRegistryTarget\tMmcSection\tNavigationStatus\tIdentityHash\tNotes\tMmcObservedState\tMmcObservedValue\tValueVerificationStatus");
        foreach (var row in evidence)
            output.AppendLine(string.Join("\t", new[]
            {
                row.GpoId.ToString("D"), row.Scope, row.Extension, row.SettingName,
                row.SourceType, row.SourceStatus, row.RegistryTarget, row.MmcSection,
                row.NavigationStatus, row.IdentityHash, row.Notes,
                row.MmcObservedState, row.MmcObservedValue, row.ValueVerificationStatus
            }.Select(Safe)));
        return output.ToString();
    }
}

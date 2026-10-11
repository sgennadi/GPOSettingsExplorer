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
        string Notes);

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
        output.AppendLine("GpoId\tScope\tExtension\tSettingName\tSourceType\tSourceStatus\tRegistryTarget\tMmcSection\tNavigationStatus\tIdentityHash\tNotes");
        foreach (var row in evidence)
            output.AppendLine(string.Join("\t", new[]
            {
                row.GpoId.ToString("D"), row.Scope, row.Extension, row.SettingName,
                row.SourceType, row.SourceStatus, row.RegistryTarget, row.MmcSection,
                row.NavigationStatus, row.IdentityHash, row.Notes
            }.Select(Safe)));
        return output.ToString();
    }
}

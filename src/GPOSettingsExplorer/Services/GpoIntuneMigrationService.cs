using System.Text.Json;
using GPOSettingsExplorer.Models;

namespace GPOSettingsExplorer.Services;

/// <summary>
/// Offline mapping assessment only. A CSP mapping is reported ONLY when it
/// appears in an explicitly operator-reviewed source; registry-path similarity
/// never proves Intune equivalence, assignment or policy migration success.
/// </summary>
public sealed record GpoIntuneMapping(
    string Scope, string RegistryKey, string RegistryValue, string OmaUri,
    string? Notes = null);
public sealed record GpoIntuneMappingDocument(
    string Schema, string Source, IReadOnlyList<GpoIntuneMapping> Mappings);
public sealed record GpoIntuneCandidate(
    string Setting, string Status, string Target, string Evidence);
public sealed record GpoIntuneMigrationReport(
    string MappingSource, int TotalSourceRows, int MappingMatches,
    IReadOnlyList<GpoIntuneCandidate> Candidates)
{
    public string ToText() =>
        "GPO TO INTUNE READINESS (OFFLINE / READ ONLY)\n" +
        "Mapping evidence: " + MappingSource + "\n" +
        "Stored records: " + TotalSourceRows + "; mapped candidates: " +
        MappingMatches + "\n" +
        "A proposed OMA URI is NOT proof of compatible value encoding, assignments," +
        " policy applicability or migration success.\n\n" +
        string.Join("\n", Candidates.Select(f =>
            "[" + f.Status + "] " + f.Setting + " -> " +
            f.Target + " | " + f.Evidence));
}
public static class GpoIntuneMigrationService
{
    public const int MaxMappingBytes = 512 * 1024;

    public static GpoIntuneMappingDocument LoadMapping(string path)
    {
        var raw = OfflineGpoSourceService.ReadBounded(path, MaxMappingBytes);
        var value = JsonSerializer.Deserialize<GpoIntuneMappingDocument>(
            raw, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (value?.Schema != "gposes-csp-map-v1" ||
            string.IsNullOrWhiteSpace(value.Source) || value.Source.Length > 500 ||
            value.Mappings is null || value.Mappings.Count is < 1 or > 10000 ||
            value.Mappings.Any(m => m is null ||
                m.Scope is not ("Computer" or "User") ||
                string.IsNullOrWhiteSpace(m.RegistryKey) || m.RegistryKey.Length > 1024 ||
                string.IsNullOrWhiteSpace(m.RegistryValue) || m.RegistryValue.Length > 256 ||
                string.IsNullOrWhiteSpace(m.OmaUri) || m.OmaUri.Length > 2048 ||
                !m.OmaUri.StartsWith("./Device/Vendor/MSFT/Policy/Config/",
                    StringComparison.OrdinalIgnoreCase) &&
                !m.OmaUri.StartsWith("./User/Vendor/MSFT/Policy/Config/",
                    StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("Invalid gposes-csp-map-v1 mappings.");
        return value;
    }

    public static GpoIntuneMigrationReport Assess(
        RealSettingsScanResult source, GpoIntuneMappingDocument? mapping)
    {
        var candidates = new List<GpoIntuneCandidate>();
        var count = 0;
        foreach (var row in source.Rows.Where(r => r.RegistryKey.Length > 0))
        {
            var matches = mapping?.Mappings.Where(m =>
                m.Scope.Equals(row.Scope, StringComparison.OrdinalIgnoreCase) &&
                m.RegistryKey.Equals(row.RegistryKey, StringComparison.OrdinalIgnoreCase) &&
                m.RegistryValue.Equals(row.RegistryValue, StringComparison.OrdinalIgnoreCase))
                .ToArray() ?? Array.Empty<GpoIntuneMapping>();
            var identity = row.Scope + " | " + row.RegistryKey +
                " | " + row.RegistryValue;
            if (matches.Length == 0)
                candidates.Add(new(identity, "Unmapped", "",
                    "No explicitly verified registry-to-CSP mapping supplied."));
            else if (matches.Length == 1)
            {
                count++;
                candidates.Add(new(identity, "Candidate", matches[0].OmaUri,
                    "Mapping supplied by " + mapping!.Source +
                    "; Intune value transformation and policy scope unverified."));
            }
            else
                candidates.Add(new(identity, "Ambiguous", "",
                    "Multiple CSP mappings; manual review required."));
        }
        return new GpoIntuneMigrationReport(
            mapping?.Source ?? "none (all entries intentionally unmapped)",
            source.Rows.Count, count, candidates);
    }
}
using System.Text.Json;
using GPOSettingsExplorer.Models;

namespace GPOSettingsExplorer.Services;

/// <summary>
/// Explicit, operator-reviewed Registry.pol -> Policy CSP candidates only.
/// This is NOT Microsoft Group Policy analytics, a Settings Catalog import
/// converter, a value-encoding translator or a migration/write engine.
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
    IReadOnlyList<GpoIntuneCandidate> Candidates,
    bool SourcePartial = false, int EligibleRegistryRows = 0)
{
    public string ToText()
    {
        var result = new List<string>
        {
            "GPO TO INTUNE READINESS (STORED SOURCE / READ ONLY)",
            "Reviewed mapping: " + MappingSource,
            "Stored source records: " + TotalSourceRows,
            "Eligible exact Registry.pol records: " + EligibleRegistryRows,
            "Explicit CSP mapping candidates: " + MappingMatches,
            "Source coverage: " + (SourcePartial
                ? "INCOMPLETE - unsupported/unreadable files may hide more settings"
                : "Available bounded sources inspected (not all Group Policy CSE types)"),
            "A CSP candidate is not evidence of equivalent value encoding, Settings " +
            "Catalog availability, assignment, Intune applicability or migration success.",
            "Unmapped/unsupported rows are never silently counted as compatible.",
            ""
        };
        result.AddRange(Candidates.Take(2500).Select(f =>
            "[" + f.Status + "] " + f.Setting + " -> " +
            f.Target + " | " + f.Evidence));
        if (Candidates.Count > 2500)
            result.Add("[Truncated] More source findings: " +
                (Candidates.Count - 2500));
        return string.Join("\n", result);
    }
}

public static class GpoIntuneMigrationService
{
    public const int MaxMappingBytes = 512 * 1024;
    private const string DeviceRoot =
        "./Device/Vendor/MSFT/Policy/Config/";
    private const string UserRoot =
        "./User/Vendor/MSFT/Policy/Config/";

    public static GpoIntuneMappingDocument LoadMapping(string path)
    {
        var raw = OfflineGpoSourceService.ReadBounded(path, MaxMappingBytes);
        var value = JsonSerializer.Deserialize<GpoIntuneMappingDocument>(
            raw, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (value?.Schema != "gposes-csp-map-v1" ||
            string.IsNullOrWhiteSpace(value.Source) || value.Source.Length > 500 ||
            value.Mappings is null || value.Mappings.Count is < 1 or > 10000 ||
            value.Mappings.Any(m => !IsSafeMapping(m)))
            throw new InvalidDataException(
                "Invalid gposes-csp-map-v1: mappings must contain exact scope, " +
                "registry identity and a canonical matching Device/User Policy CSP URI.");
        return value;
    }

    public static bool IsSafeMapping(GpoIntuneMapping? map)
    {
        if (map is null || map.Scope is not ("Computer" or "User") ||
            string.IsNullOrWhiteSpace(map.RegistryKey) ||
            map.RegistryKey.Length > 1024 ||
            string.IsNullOrWhiteSpace(map.RegistryValue) ||
            map.RegistryValue.Length > 256 ||
            string.IsNullOrWhiteSpace(map.OmaUri) ||
            map.OmaUri.Length > 2048 ||
            (map.Notes?.Length ?? 0) > 2000)
            return false;
        var prefix = map.Scope == "Computer" ? DeviceRoot : UserRoot;
        if (!map.OmaUri.StartsWith(prefix, StringComparison.Ordinal) ||
            map.OmaUri.Length == prefix.Length ||
            map.OmaUri.Any(char.IsWhiteSpace) ||
            map.OmaUri.Any(char.IsControl) ||
            map.OmaUri.Contains('%') || map.OmaUri.Contains('?') ||
            map.OmaUri.Contains('#') || map.OmaUri.Contains('\\'))
            return false;
        var suffix = map.OmaUri[prefix.Length..];
        return suffix.Split('/').All(part =>
            !string.IsNullOrWhiteSpace(part) &&
            part is not ("." or "..") &&
            part.All(c => char.IsLetterOrDigit(c) ||
                          c is '-' or '_' or '.'));
    }

    public static GpoIntuneMigrationReport Assess(
        RealSettingsScanResult source, GpoIntuneMappingDocument? mapping)
    {
        ArgumentNullException.ThrowIfNull(source);
        var candidates = new List<GpoIntuneCandidate>();
        var mapped = 0;
        var eligible = 0;
        foreach (var row in source.Rows.Where(r => r.RegistryKey.Length > 0))
        {
            var identity = row.Scope + " | " + row.RegistryKey +
                " | " + row.RegistryValue;
            if (row.State != "Stored registry value" ||
                !row.ValueType.StartsWith("REG_", StringComparison.OrdinalIgnoreCase))
            {
                candidates.Add(new(identity, "Unsupported source", "",
                    "Not an exact, ordinary Registry.pol value; CSP equivalence unknown."));
                continue;
            }
            if (!GpoSourceValueCompleteness.IsExactProjection(row))
            {
                candidates.Add(new(identity, "Unverifiable", "",
                    "Value was truncated/redacted/partially projected; " +
                    "not safe to assess CSP compatibility."));
                continue;
            }

            eligible++;
            var matches = mapping?.Mappings.Where(m =>
                IsSafeMapping(m) &&
                m.Scope.Equals(row.Scope, StringComparison.Ordinal) &&
                m.RegistryKey.Equals(row.RegistryKey,
                    StringComparison.OrdinalIgnoreCase) &&
                m.RegistryValue.Equals(row.RegistryValue,
                    StringComparison.OrdinalIgnoreCase))
                .ToArray() ?? Array.Empty<GpoIntuneMapping>();

            if (matches.Length == 0)
                candidates.Add(new(identity, "Unmapped", "",
                    "No explicitly approved CSP mapping for this registry identity."));
            else if (matches.Length == 1)
            {
                mapped++;
                candidates.Add(new(identity, "Candidate", matches[0].OmaUri,
                    "Manually reviewed mapping from " + mapping!.Source +
                    ". Target value encoding and Intune assignments not validated."));
            }
            else
                candidates.Add(new(identity, "Ambiguous", "",
                    "Multiple reviewed CSP mappings for the same key; " +
                    "require a human to resolve duplicates."));
        }
        return new GpoIntuneMigrationReport(
            mapping?.Source ?? "none (mapping required for any candidate)",
            source.Rows.Count, mapped, candidates,
            source.IsPartial, eligible);
    }
}

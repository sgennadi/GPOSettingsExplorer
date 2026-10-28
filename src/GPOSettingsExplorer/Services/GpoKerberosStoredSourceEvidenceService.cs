using System.Globalization;
using GPOSettingsExplorer.Models;

namespace GPOSettingsExplorer.Services;

/// <summary>
/// Read-only equality check between one typed GPMC Kerberos Account record
/// and one complete, SHA-256 bound GptTmpl.inf [Kerberos Policy] record.
/// This compares two STORED snapshots; it does not attest native MMC display,
/// a live DC state, replication consistency or effective client RSoP.
/// </summary>
public static class GpoKerberosStoredSourceEvidenceService
{
    public const string NotApplicable = "NOT_KERBEROS";
    public const string NotSupported = "SOURCE_READER_NOT_IMPLEMENTED";
    public const string NoSnapshot = "SECURITY_SOURCE_NOT_SCANNED";
    public const string WrongSnapshot = "GPO_OR_DC_CHANGED";
    public const string GpmcAmbiguous = "GPMC_IDENTITY_AMBIGUOUS";
    public const string GpmcUntyped = "GPMC_VALUE_NOT_EXACT";
    public const string FileAbsent = "GPTTMPL_NOT_CAPTURED";
    public const string FileAmbiguous = "GPTTMPL_FILE_AMBIGUOUS";
    public const string FileIncomplete = "GPTTMPL_NOT_COMPLETE";
    public const string SourceMissing = "KERBEROS_SOURCE_ROW_MISSING";
    public const string SourceAmbiguous = "KERBEROS_SOURCE_ROW_AMBIGUOUS";
    public const string SourceUntrusted = "KERBEROS_SOURCE_NOT_EXACT";
    public const string Match = "STORED_SNAPSHOTS_MATCH";
    public const string Mismatch = "STORED_SNAPSHOTS_DIFFER";

    public sealed record Result(
        string Status, string Explanation,
        string GpmcLiteral = "", string InfLiteral = "",
        string SourceSha256 = "")
    {
        public bool IsCompared => Status is Match or Mismatch;
    }

    public static Result Compare(
        PolicySettingInfo setting,
        IReadOnlyList<PolicySettingInfo> indexedGpoSettings,
        RealSettingsScanResult? snapshot,
        string expectedDomain, string pinnedDc)
    {
        ArgumentNullException.ThrowIfNull(setting);
        ArgumentNullException.ThrowIfNull(indexedGpoSettings);

        if (SoftwareRestrictionMmcRouteService.IsSoftwareRestriction(setting))
            return Block(NotSupported, "SRP Path Rules require a separate typed source reader. " +
                "An MMC path match is NOT a stored rule/security-level match.");
        if (!KerberosPolicyMetadataService.IsKnownKerberosSetting(setting) ||
            !(setting.Category.Equals(KerberosPolicyMetadataService.Category,
                  StringComparison.OrdinalIgnoreCase) ||
              setting.Category.Equals("SecuritySettings", StringComparison.OrdinalIgnoreCase)) ||
            !KerberosPolicyMetadataService.TryGet(setting.SettingName, out var policy))
            return Block(NotApplicable,
                "No supported typed Kerberos Account source comparison for this setting.");

        // The GPMC report is a projection, not a raw security-policy record:
        // never compare if distinct GPMC entries share a policy identity.
        if (setting.GpoId == Guid.Empty ||
            indexedGpoSettings.Count(other =>
                other.GpoId == setting.GpoId &&
                other.Scope.Equals(setting.Scope, StringComparison.OrdinalIgnoreCase) &&
                other.Extension.Equals(setting.Extension, StringComparison.OrdinalIgnoreCase) &&
                other.Category.Equals(setting.Category, StringComparison.OrdinalIgnoreCase) &&
                other.SettingName.Equals(setting.SettingName, StringComparison.OrdinalIgnoreCase)) != 1)
            return Block(GpmcAmbiguous,
                "GPMC contains zero/multiple records for this same Kerberos identity.");

        if (!TryGetGpmcValue(setting.Value, policy.InternalName,
                out var expected, out var expectedType))
            return Block(GpmcUntyped,
                "GPMC must contain exactly one Name, Type=Kerberos, and one canonical " +
                "SettingNumber or SettingBoolean. Missing/duplicate/ambiguous values are not compared.");

        if (snapshot is null)
            return Block(NoSnapshot,
                "Read the selected GPO's security source on demand. A GPMC index alone cannot verify GptTmpl.inf.");
        if (string.IsNullOrWhiteSpace(expectedDomain) ||
            string.IsNullOrWhiteSpace(pinnedDc) ||
            snapshot.GpoId != setting.GpoId ||
            !snapshot.Domain.Equals(expectedDomain, StringComparison.OrdinalIgnoreCase) ||
            !snapshot.DomainController.Equals(pinnedDc, StringComparison.OrdinalIgnoreCase))
            return Block(WrongSnapshot,
                "The captured security file belongs to another GPO/domain/pinned DC.");

        var expectedPath = Path.Combine(
            $@"\\{pinnedDc}\SYSVOL\{expectedDomain}",
            "Policies", setting.GpoId.ToString("B"), "Machine", "Microsoft",
            "Windows NT", "SecEdit", "GptTmpl.inf");
        static string Normalize(string path) =>
            path.Replace('/', '\\').TrimEnd('\\');

        // One specific path under the single pinned DC and GPO is required;
        // a suffix-only test could accept another GPO's or DC's template.
        var matches = snapshot.Files.Where(file =>
            Normalize(file.SourceFile).Equals(
                Normalize(expectedPath), StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matches.Length == 0)
            return Block(FileAbsent,
                "Exact GptTmpl.inf file not found in this captured source snapshot. " +
                "Absence is NOT proof that the policy is Not Configured.");
        if (matches.Length != 1)
            return Block(FileAmbiguous, "More than one file record has the exact SecEdit path.");

        var file = matches[0];
        if (file.Status != "Read" || !IsSha256(file.Sha256))
            return Block(FileIncomplete,
                "GptTmpl.inf is absent, partial, invalid, unreadable or lacks a valid SHA-256.");

        var records = snapshot.Rows.Where(row =>
            row.GpoId == setting.GpoId &&
            row.Scope.Equals("Computer", StringComparison.OrdinalIgnoreCase) &&
            Normalize(row.SourceFile).Equals(
                Normalize(expectedPath), StringComparison.OrdinalIgnoreCase) &&
            row.Category.Equals("Security template > Kerberos Policy",
                StringComparison.OrdinalIgnoreCase) &&
            row.SettingName.Equals(policy.InternalName, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (records.Length == 0)
            return Block(SourceMissing,
                "No exact Kerberos key was found in this complete SecEdit source. " +
                "Do not infer Not Configured from a single source file.");
        if (records.Length != 1)
            return Block(SourceAmbiguous,
                "Duplicate matching Kerberos entries in the security template; no first-match fallback.");

        var row = records[0];
        if (!row.SourceSha256.Equals(file.Sha256, StringComparison.OrdinalIgnoreCase) ||
            row.ValueType != "INF string" || row.State != "Stored template value" ||
            (expectedType == "Boolean" && row.Value is not ("0" or "1")) ||
            !TryCanonicalValue(row.Value, expectedType, out var actual))
            return Block(SourceUntrusted,
                "Source hash, type, state or canonical Kerberos number/boolean is not exact.");

        return string.Equals(expected, actual, StringComparison.Ordinal)
            ? new Result(Match,
                "One typed GPMC Account value agrees with one complete SHA-256-bound " +
                "GptTmpl.inf Kerberos key in a captured snapshot. NOT MMC display, " +
                "live DC freshness, replication state or client RSoP.",
                expected, actual, file.Sha256)
            : new Result(Mismatch,
                "Typed GPMC Account value differs from the saved GptTmpl.inf " +
                "Kerberos key in the captured snapshot. Check GPMC/source/DC " +
                "freshness; no automatic correction or client RSoP conclusion.",
                expected, actual, file.Sha256);
    }

    private static Result Block(string status, string explanation) =>
        new(status, explanation);

    private static bool TryGetGpmcValue(
        string summary, string expectedName,
        out string canonical, out string valueType)
    {
        canonical = "";
        valueType = expectedName.Equals("TicketValidateClient",
            StringComparison.OrdinalIgnoreCase) ? "Boolean" : "Number";

        if (string.IsNullOrWhiteSpace(summary) || summary.Length > 8192)
            return false;

        // The GPMC summary is semicolon-separated XML metadata. Reject malformed
        // pairs and duplicate keys rather than silently choosing the last field.
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var segment in summary.Split(';'))
        {
            var equals = segment.IndexOf('=');
            if (equals < 1)
                return false;
            var key = segment[..equals].Trim();
            var value = segment[(equals + 1)..].Trim();
            if (key.Length == 0 || value.Length == 0 || !fields.TryAdd(key, value))
                return false;
        }

        if (!fields.TryGetValue("Name", out var name) ||
            !name.Equals(expectedName, StringComparison.OrdinalIgnoreCase) ||
            !fields.TryGetValue("Type", out var policyType) ||
            policyType != "Kerberos")
            return false;

        var field = valueType == "Boolean" ? "SettingBoolean" : "SettingNumber";
        var conflicting = valueType == "Boolean" ? "SettingNumber" : "SettingBoolean";
        if (!fields.TryGetValue(field, out var raw) || fields.ContainsKey(conflicting))
            return false;

        return TryCanonicalValue(raw, valueType, out canonical);
    }

    private static bool TryCanonicalValue(string raw, string valueType, out string canonical)
    {
        canonical = "";
        if (valueType == "Boolean")
        {
            // The INF contains 0/1; GPMC reports true/false. Do not treat
            // arbitrary text or 2 as equivalent to true.
            if (raw.Equals("true", StringComparison.OrdinalIgnoreCase) || raw == "1")
                canonical = "1";
            else if (raw.Equals("false", StringComparison.OrdinalIgnoreCase) || raw == "0")
                canonical = "0";
            return canonical.Length != 0;
        }

        if (raw.Length is < 1 or > 10 ||
            !raw.All(c => c >= '0' && c <= '9') ||
            (raw.Length > 1 && raw[0] == '0') ||
            !uint.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var integer))
            return false;

        canonical = integer.ToString(CultureInfo.InvariantCulture);
        return true;
    }

    private static bool IsSha256(string? sha) =>
        sha is { Length: 64 } && sha.All(Uri.IsHexDigit);
}

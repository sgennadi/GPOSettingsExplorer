using GPOSettingsExplorer.Models;

namespace GPOSettingsExplorer.Services;

/// <summary>
/// Exact saved-source correlation of a System Advanced Audit GPMC entry with
/// the selected GPO's SHA-256-bound audit.csv on the session-pinned DC.
/// Never claims client auditpol, native MMC, precedence, replication or RSoP.
/// </summary>
public static class GpoAdvancedAuditStoredSourceEvidenceService
{
    public const string NotApplicable = "NOT_SYSTEM_ADVANCED_AUDIT";
    public const string NoSnapshot = "AUDIT_SOURCE_NOT_SCANNED";
    public const string WrongSnapshot = "AUDIT_GPO_OR_DC_CHANGED";
    public const string GpmcInvalid = "AUDIT_GPMC_IDENTITY_NOT_EXACT";
    public const string GpmcAmbiguous = "AUDIT_GPMC_SUBCATEGORY_AMBIGUOUS";
    public const string FileMissing = "AUDIT_CSV_NOT_CAPTURED";
    public const string FileAmbiguous = "AUDIT_CSV_FILE_AMBIGUOUS";
    public const string FileIncomplete = "AUDIT_CSV_NOT_COMPLETE";
    public const string SourceMissing = "AUDIT_SUBCATEGORY_NOT_FOUND";
    public const string SourceAmbiguous = "AUDIT_SOURCE_SUBCATEGORY_AMBIGUOUS";
    public const string SourceInvalid = "AUDIT_SOURCE_NOT_EXACT";
    public const string Match = "AUDIT_STORED_SNAPSHOTS_MATCH";
    public const string Mismatch = "AUDIT_STORED_SNAPSHOTS_DIFFER";

    public sealed record Result(string Status, string Explanation, string SourceSha256 = "")
    {
        public bool IsCompared => Status is Match or Mismatch;
    }

    public static bool IsSupported(PolicySettingInfo setting) =>
        setting.Extension.Equals("AuditSettings", StringComparison.OrdinalIgnoreCase) &&
        setting.Scope.Equals("Computer", StringComparison.OrdinalIgnoreCase) &&
        setting.Category.Equals(
            "Security Settings > Advanced Audit Policy Configuration > Audit Policies",
            StringComparison.OrdinalIgnoreCase) &&
        !string.IsNullOrWhiteSpace(setting.SettingName) &&
        !setting.SettingName.Equals("Audit Setting", StringComparison.OrdinalIgnoreCase);

    public static Result Compare(
        PolicySettingInfo setting, IReadOnlyList<PolicySettingInfo> indexed,
        RealSettingsScanResult? snapshot, string expectedDomain, string pinnedDc)
    {
        ArgumentNullException.ThrowIfNull(setting);
        ArgumentNullException.ThrowIfNull(indexed);
        if (!IsSupported(setting))
            return Block(NotApplicable,
                "Only explicitly typed Computer Advanced Audit System subcategories are supported.");

        if (setting.GpoId == Guid.Empty ||
            !TryTypedGpmc(setting.Value, out var subcategory, out var expected))
            return Block(GpmcInvalid,
                "Unique GPMC PolicyTarget=System, nonempty SubcategoryGuid and " +
                "canonical SettingValue=0..4 are required. Per-user audit is unsupported.");

        // Match unique GUID identity, not translated display names. Never
        // validate a modified/foreign setting against an indexed report.
        var candidates = indexed.Where(other =>
            other.GpoId == setting.GpoId && IsSupported(other) &&
            TryTypedGpmc(other.Value, out var guid, out _) &&
            guid == subcategory).ToArray();
        if (candidates.Length != 1 ||
            !candidates[0].SettingName.Equals(setting.SettingName, StringComparison.Ordinal) ||
            !candidates[0].Value.Equals(setting.Value, StringComparison.Ordinal))
            return Block(GpmcAmbiguous,
                "Exactly one unchanged, indexed same-GPO System audit subcategory " +
                "GUID and exact GPMC record are required.");

        if (snapshot is null)
            return Block(NoSnapshot,
                "Read only the selected GPO's Machine Audit/audit.csv from the pinned DC.");

        if (string.IsNullOrWhiteSpace(expectedDomain) || string.IsNullOrWhiteSpace(pinnedDc) ||
            snapshot.GpoId != setting.GpoId ||
            !snapshot.Domain.Equals(expectedDomain, StringComparison.OrdinalIgnoreCase) ||
            !snapshot.DomainController.Equals(pinnedDc, StringComparison.OrdinalIgnoreCase))
            return Block(WrongSnapshot,
                "GPO, domain or pinned DC differs from the audit source.");

        var expectedPath = Path.Combine(
            $@"\\{pinnedDc}\SYSVOL\{expectedDomain}", "Policies",
            setting.GpoId.ToString("B"), "Machine", "Microsoft",
            "Windows NT", "Audit", "audit.csv");
        static string Normalize(string? value) =>
            (value ?? "").Replace('/', '\\').TrimEnd('\\');

        var files = snapshot.Files.Where(file =>
            Normalize(file.SourceFile).Equals(
                Normalize(expectedPath), StringComparison.OrdinalIgnoreCase)).ToArray();
        if (files.Length == 0)
            return Block(FileMissing,
                "Exact pinned Audit/audit.csv not captured; absence does not mean Not Configured.");
        if (files.Length != 1)
            return Block(FileAmbiguous, "Duplicate audit.csv file-evidence identities.");

        var file = files[0];
        if (file.Status != "Read" || !IsSha256(file.Sha256))
            return Block(FileIncomplete,
                "Audit CSV is partial, unreadable, malformed or lacks a SHA-256 fingerprint.");

        // A per-user SID target with the same GUID is NOT a System policy row.
        var rows = snapshot.Rows.Where(row =>
            row.GpoId == setting.GpoId &&
            row.Scope.Equals("Computer", StringComparison.OrdinalIgnoreCase) &&
            Normalize(row.SourceFile).Equals(
                Normalize(expectedPath), StringComparison.OrdinalIgnoreCase) &&
            row.Category.Equals("Advanced Audit Policy > System",
                StringComparison.OrdinalIgnoreCase) &&
            row.RegistryKey.Equals("Subcategory GUID", StringComparison.OrdinalIgnoreCase) &&
            Guid.TryParse(row.RegistryValue, out var guid) && guid == subcategory)
            .ToArray();
        if (rows.Length == 0)
            return Block(SourceMissing,
                "No matching System audit.csv GUID; a per-user SID or translated name is not a substitute.");
        if (rows.Length != 1)
            return Block(SourceAmbiguous, "Duplicate System subcategory GUID in audit.csv.");

        var record = rows[0];
        if (record.State != "Stored advanced audit CSV entry" ||
            record.ValueType != "CSV string" ||
            !record.SourceSha256.Equals(file.Sha256, StringComparison.OrdinalIgnoreCase) ||
            !TryCanonical(record.Value, out var actual))
            return Block(SourceInvalid,
                "CSV source value, type, state or SHA-256 does not match the complete file.");

        return new Result(expected == actual ? Match : Mismatch,
            expected == actual
                ? "Typed System GPMC and the complete pinned audit.csv agree on " +
                  "subcategory GUID and literal numeric value (0..4). SAVED source " +
                  "evidence only; client auditpol, native MMC, inheritance, " +
                  "replication and effective RSoP remain NOT verified."
                : "The GPMC System audit code differs from same-GUID pinned " +
                  "audit.csv. Refresh both source snapshots before interpreting " +
                  "the difference. No GPO changes were made.",
            file.Sha256);
    }

    private static bool TryTypedGpmc(string summary, out Guid subcategory, out string value)
    {
        subcategory = Guid.Empty;
        value = "";
        if (string.IsNullOrWhiteSpace(summary) || summary.Length > 8192)
            return false;
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var tokens = summary.Split(';', StringSplitOptions.TrimEntries);
        if (tokens.Length is < 3 or > 64)
            return false;
        foreach (var token in tokens)
        {
            var separator = token.IndexOf('=');
            if (separator < 1)
                return false;
            var key = token[..separator].Trim();
            var raw = token[(separator + 1)..].Trim();
            // Other report details are ignored, but trusted identity fields
            // must each occur once, never twice.
            if (key.Equals("PolicyTarget", StringComparison.OrdinalIgnoreCase) ||
                key.Equals("SubcategoryGuid", StringComparison.OrdinalIgnoreCase) ||
                key.Equals("SettingValue", StringComparison.OrdinalIgnoreCase))
            {
                if (!fields.TryAdd(key, raw))
                    return false;
            }
        }

        return fields.Count == 3 &&
            fields["PolicyTarget"].Equals("System", StringComparison.Ordinal) &&
            Guid.TryParse(fields["SubcategoryGuid"], out subcategory) &&
            subcategory != Guid.Empty &&
            TryCanonical(fields["SettingValue"], out value);
    }

    private static bool TryCanonical(string input, out string result)
    {
        result = "";
        // Microsoft MS-GPAC System audit codes: 0 unchanged, 1 success,
        // 2 failure, 3 success+failure, 4 none. No names/units inferred.
        if (input.Length == 1 && input[0] >= '0' && input[0] <= '4')
            result = input;
        return result.Length != 0;
    }

    private static bool IsSha256(string? value) =>
        value is { Length: 64 } && value.All(Uri.IsHexDigit);

    private static Result Block(string status, string explanation) => new(status, explanation);
}

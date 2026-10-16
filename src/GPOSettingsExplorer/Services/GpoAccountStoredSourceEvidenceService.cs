using System.Globalization;
using GPOSettingsExplorer.Models;

namespace GPOSettingsExplorer.Services;

/// <summary>
/// Read-only comparison of one explicitly typed GPMC Password Policy/Account
/// Lockout record with [System Access] in the same pinned GPO's GptTmpl.inf.
/// A match only compares STORED source snapshots, never local account policy,
/// domain effective policy, a native MMC value, applied RSoP or replication.
/// </summary>
public static class GpoAccountStoredSourceEvidenceService
{
    public const string NotApplicable = "NOT_KNOWN_ACCOUNT_POLICY";
    public const string NoSnapshot = "SYSTEM_ACCESS_NOT_SCANNED";
    public const string WrongSnapshot = "GPO_OR_DC_CHANGED";
    public const string GpmcAmbiguous = "ACCOUNT_GPMC_IDENTITY_AMBIGUOUS";
    public const string GpmcUntyped = "ACCOUNT_GPMC_VALUE_NOT_EXACT";
    public const string FileAbsent = "ACCOUNT_GPTTMPL_NOT_CAPTURED";
    public const string FileAmbiguous = "ACCOUNT_GPTTMPL_FILE_AMBIGUOUS";
    public const string FileIncomplete = "ACCOUNT_GPTTMPL_NOT_COMPLETE";
    public const string SourceMissing = "SYSTEM_ACCESS_SOURCE_ROW_MISSING";
    public const string SourceAmbiguous = "SYSTEM_ACCESS_SOURCE_ROW_AMBIGUOUS";
    public const string SourceUntrusted = "SYSTEM_ACCESS_SOURCE_NOT_EXACT";
    public const string Match = "ACCOUNT_STORED_SNAPSHOTS_MATCH";
    public const string Mismatch = "ACCOUNT_STORED_SNAPSHOTS_DIFFER";

    public sealed record Result(string Status, string Explanation, string SourceSha256 = "")
    {
        public bool IsCompared => Status is Match or Mismatch;
    }

    public static Result Compare(
        PolicySettingInfo setting, IReadOnlyList<PolicySettingInfo> indexed,
        RealSettingsScanResult? snapshot, string expectedDomain, string pinnedDc)
    {
        ArgumentNullException.ThrowIfNull(setting);
        ArgumentNullException.ThrowIfNull(indexed);
        if (!GpoKnownAccountPolicyService.TryDescribe(setting, out var descriptor))
            return Block(NotApplicable,
                "Only explicitly typed Computer Password Policy and Account Lockout records are supported.");

        // Disallow similarly named Account policies belonging to different GPOs,
        // duplicated GPMC entries or ambiguous aliases.
        if (setting.GpoId == Guid.Empty ||
            indexed.Count(other => other.GpoId == setting.GpoId &&
                other.Scope.Equals("Computer", StringComparison.OrdinalIgnoreCase) &&
                GpoKnownAccountPolicyService.TryDescribe(other, out var candidate) &&
                candidate.Type.Equals(descriptor.Type, StringComparison.OrdinalIgnoreCase) &&
                candidate.NativeName.Equals(descriptor.NativeName, StringComparison.OrdinalIgnoreCase)) != 1)
            return Block(GpmcAmbiguous,
                "Exactly one same-GPO, same-scope, typed Account setting is required.");

        var isBoolean = descriptor.SourceName.Equals("PasswordComplexity",
                            StringComparison.OrdinalIgnoreCase) ||
                        descriptor.SourceName.Equals("ClearTextPassword",
                            StringComparison.OrdinalIgnoreCase);
        if (!TryGpmcValue(setting.Value, descriptor.SourceName,
                descriptor.Type, isBoolean, out var expected))
            return Block(GpmcUntyped,
                "GPMC Account metadata must contain unique Name, Type and one canonical " +
                "SettingNumber/SettingBoolean. A missing or unsupported unit is not inferred.");

        if (snapshot is null)
            return Block(NoSnapshot,
                "Read only the selected GPO's SecEdit GptTmpl.inf on the pinned DC. " +
                "An MMC section alone cannot verify a stored source.");
        if (string.IsNullOrWhiteSpace(expectedDomain) ||
            string.IsNullOrWhiteSpace(pinnedDc) ||
            setting.Scope != "Computer" ||
            snapshot.GpoId != setting.GpoId ||
            !snapshot.Domain.Equals(expectedDomain, StringComparison.OrdinalIgnoreCase) ||
            !snapshot.DomainController.Equals(pinnedDc, StringComparison.OrdinalIgnoreCase))
            return Block(WrongSnapshot,
                "GPO, scope, domain or pinned controller differs from the selected source.");

        var path = Path.Combine(
            $@"\\{pinnedDc}\SYSVOL\{expectedDomain}", "Policies",
            setting.GpoId.ToString("B"), "Machine", "Microsoft",
            "Windows NT", "SecEdit", "GptTmpl.inf");
        static string Normalize(string? p) => (p ?? "").Replace('/', '\\').TrimEnd('\\');
        var files = snapshot.Files.Where(file => Normalize(file.SourceFile).Equals(
            Normalize(path), StringComparison.OrdinalIgnoreCase)).ToArray();
        if (files.Length == 0)
            return Block(FileAbsent,
                "GptTmpl.inf is not included in the source snapshot. Absence is NOT proof of Not Configured.");
        if (files.Length != 1)
            return Block(FileAmbiguous, "Duplicate source evidence for exact GptTmpl.inf path.");

        var file = files[0];
        if (file.Status != "Read" || !IsSha256(file.Sha256))
            return Block(FileIncomplete,
                "The source file is partial, invalid, unreadable or lacks a SHA-256 fingerprint.");

        // Distinguish Password Policy & Account Lockout from Kerberos (different
        // INF sections). Never match a generic security-name occurrence.
        var rows = snapshot.Rows.Where(row =>
            row.GpoId == setting.GpoId &&
            row.Scope.Equals("Computer", StringComparison.OrdinalIgnoreCase) &&
            Normalize(row.SourceFile).Equals(Normalize(path), StringComparison.OrdinalIgnoreCase) &&
            row.Category.Equals("Security template > System Access", StringComparison.OrdinalIgnoreCase) &&
            row.SettingName.Equals(descriptor.SourceName, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (rows.Length == 0)
            return Block(SourceMissing,
                "Exact [System Access] key is missing; this does not prove policy absence.");
        if (rows.Length != 1)
            return Block(SourceAmbiguous, "Duplicate [System Access] entries; no first-match fallback.");

        var record = rows[0];
        if (record.State != "Stored template value" ||
            record.ValueType != "INF string" ||
            !record.SourceSha256.Equals(file.Sha256, StringComparison.OrdinalIgnoreCase) ||
            !TryCanonical(record.Value, isBoolean, out var actual))
            return Block(SourceUntrusted,
                "Source type, state, SHA-256 or canonical number/boolean does not match the required format.");

        var explanation = "Unique typed GPMC Account and pinned-DC SHA-256-bound " +
            "GptTmpl.inf [System Access] values were compared. This is a STORED " +
            "snapshot only; effective domain/account precedence, time units beyond " +
            "literal source semantics, live MMC and client RSoP remain NOT verified.";
        return new Result(expected == actual ? Match : Mismatch,
            expected == actual ? explanation
                : "GPMC typed Account value differs from pinned GptTmpl.inf [System Access]. " +
                  "Recheck source freshness before interpreting this stored snapshot difference. " +
                  "No GPO write or effective RSoP assertion was performed.", file.Sha256);
    }

    private static bool TryGpmcValue(
        string summary, string sourceName, string sourceType, bool boolean,
        out string value)
    {
        value = "";
        if (string.IsNullOrWhiteSpace(summary) || summary.Length > 8192)
            return false;
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var segment in summary.Split(';'))
        {
            var separator = segment.IndexOf('=');
            if (separator <= 0)
                return false;
            var key = segment[..separator].Trim();
            var entry = segment[(separator + 1)..].Trim();
            if (key.Length == 0 || entry.Length == 0 || !fields.TryAdd(key, entry))
                return false;
        }

        if (!fields.TryGetValue("Name", out var name) ||
            !name.Equals(sourceName, StringComparison.OrdinalIgnoreCase) ||
            !fields.TryGetValue("Type", out var type) ||
            !type.Equals(sourceType, StringComparison.OrdinalIgnoreCase))
            return false;
        var current = boolean ? "SettingBoolean" : "SettingNumber";
        var conflicting = boolean ? "SettingNumber" : "SettingBoolean";
        return fields.TryGetValue(current, out var raw) &&
            !fields.ContainsKey(conflicting) &&
            TryCanonical(raw, boolean, out value);
    }

    private static bool TryCanonical(string input, bool boolean, out string value)
    {
        value = "";
        if (boolean)
        {
            if (input.Equals("true", StringComparison.OrdinalIgnoreCase) || input == "1")
                value = "1";
            else if (input.Equals("false", StringComparison.OrdinalIgnoreCase) || input == "0")
                value = "0";
            return value.Length > 0;
        }

        if (input.Length is < 1 or > 10 ||
            input.Any(c => c is < '0' or > '9') ||
            (input.Length > 1 && input[0] == '0') ||
            !uint.TryParse(input, NumberStyles.None, CultureInfo.InvariantCulture, out var number))
            return false;
        value = number.ToString(CultureInfo.InvariantCulture);
        return true;
    }

    private static bool IsSha256(string? value) =>
        value is { Length: 64 } && value.All(Uri.IsHexDigit);

    private static Result Block(string status, string explanation) => new(status, explanation);
}

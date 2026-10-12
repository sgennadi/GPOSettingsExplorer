using GPOSettingsExplorer.Models;

namespace GPOSettingsExplorer.Services;

/// <summary>
/// Narrow, read-only source comparison for legacy SRP Path Rules.
/// Matches a uniquely indexed GPMC Path+SecurityLevel with exactly one
/// typed, SHA-256-bound ItemData record in a pinned GPO Registry.pol.
/// This verifies two STORED snapshots, not GPO applicability, current
/// native MMC value, rule precedence, extra flags or effective client RSoP.
/// </summary>
public static class GpoSoftwareRestrictionStoredSourceEvidenceService
{
    public const string NotApplicable = "NOT_SRP_PATH_RULE";
    public const string GpmcInvalid = "SRP_GPMC_PATH_OR_LEVEL_NOT_EXACT";
    public const string GpmcAmbiguous = "SRP_GPMC_PATH_AMBIGUOUS";
    public const string NoSnapshot = "SRP_REGISTRY_SOURCE_NOT_SCANNED";
    public const string WrongSnapshot = "SRP_GPO_OR_DC_CHANGED";
    public const string FileAbsent = "SRP_REGISTRY_FILE_NOT_CAPTURED";
    public const string FileAmbiguous = "SRP_REGISTRY_FILE_AMBIGUOUS";
    public const string FileIncomplete = "SRP_REGISTRY_FILE_INCOMPLETE";
    public const string SourceMissing = "SRP_ITEMDATA_NOT_FOUND";
    public const string SourceAmbiguous = "SRP_ITEMDATA_AMBIGUOUS";
    public const string SourceNotExact = "SRP_ITEMDATA_NOT_EXACT";
    public const string Match = "STORED_SRP_PATH_LEVEL_MATCH";
    public const string Mismatch = "STORED_SRP_PATH_LEVEL_DIFFER";

    public sealed record Result(
        string Status, string Explanation, string Path = "",
        string GpmcSecurityLevel = "", string SourceSecurityLevel = "",
        string SourceSha256 = "")
    {
        public bool IsCompared => Status is Match or Mismatch;
    }

    public static Result Compare(
        PolicySettingInfo setting, IReadOnlyList<PolicySettingInfo> configured,
        RealSettingsScanResult? source, string expectedDomain, string pinnedDc)
    {
        ArgumentNullException.ThrowIfNull(setting);
        ArgumentNullException.ThrowIfNull(configured);
        if (!SoftwareRestrictionMmcRouteService.IsPathRule(setting))
            return Block(NotApplicable, "This is not a supported legacy Software Restriction Path Rule.");

        if (!SoftwareRestrictionMmcRouteService.TryGetPathRuleTarget(
                setting, out var gpmcPath) ||
            !TryGetGpmcLevel(setting.Value, out var gpmcLevel))
            return Block(GpmcInvalid,
                "Require one literal Path and one SecurityLevel=Disallowed/Unrestricted " +
                "from the typed GPMC Path Rule. No guessed or partial fields.");

        if (setting.GpoId == Guid.Empty || configured.Count(other =>
            other.GpoId == setting.GpoId &&
            other.Scope.Equals(setting.Scope, StringComparison.OrdinalIgnoreCase) &&
            SoftwareRestrictionMmcRouteService.IsPathRule(other) &&
            SoftwareRestrictionMmcRouteService.TryGetPathRuleTarget(
                other, out var otherPath) &&
            // Two rules with the same path, even at different levels, cannot
            // be mapped to one native policy row or one stored ItemData.
            string.Equals(otherPath, gpmcPath,
                StringComparison.OrdinalIgnoreCase)) != 1)
            return Block(GpmcAmbiguous,
                "Zero or multiple GPMC Path Rules share this exact GPO/scope/path.");

        if (source is null)
            return Block(NoSnapshot,
                "Read the selected GPO's Registry.pol from the pinned DC on demand. " +
                "GPMC alone does not verify the stored rule.");
        if (string.IsNullOrWhiteSpace(expectedDomain) ||
            string.IsNullOrWhiteSpace(pinnedDc) ||
            source.GpoId != setting.GpoId ||
            !source.Domain.Equals(expectedDomain, StringComparison.OrdinalIgnoreCase) ||
            !source.DomainController.Equals(pinnedDc, StringComparison.OrdinalIgnoreCase))
            return Block(WrongSnapshot,
                "The captured Registry.pol belongs to another GPO, domain or pinned DC.");

        var scopeDirectory = setting.Scope.Equals("User", StringComparison.OrdinalIgnoreCase)
            ? "User" : "Machine";
        var expectedPath = Path.Combine(
            $@"\\{pinnedDc}\SYSVOL\{expectedDomain}",
            "Policies", setting.GpoId.ToString("B"), scopeDirectory, "Registry.pol");

        static string Norm(string? value) =>
            (value ?? string.Empty).Replace('/', '\\').TrimEnd('\\');

        var files = source.Files.Where(file =>
            Norm(file.SourceFile).Equals(Norm(expectedPath),
                StringComparison.OrdinalIgnoreCase)).ToArray();
        if (files.Length == 0)
            return Block(FileAbsent,
                "The exact scope-specific Registry.pol was not captured. " +
                "This does not mean the SRP rule is Not Configured.");
        if (files.Length != 1)
            return Block(FileAmbiguous,
                "More than one file record identifies this same GPO/scope Registry.pol.");

        var fileEvidence = files[0];
        if (fileEvidence.Status != "Read" || !IsSha256(fileEvidence.Sha256))
            return Block(FileIncomplete,
                "The scope-specific Registry.pol is incomplete, partial, unreadable " +
                "or lacks a valid source-file SHA-256.");

        // Identity is by actual saved ItemData PATH, not the GPMC generic
        // "Path Rule" label or a substring. Search both security levels
        // before checking the expected level so wrong-level matches surface
        // as evidence of a DIFFERENCE, not as an absent source rule.
        var pathRows = source.Rows.Where(row =>
            row.GpoId == setting.GpoId &&
            row.Scope.Equals(setting.Scope, StringComparison.OrdinalIgnoreCase) &&
            Norm(row.SourceFile).Equals(Norm(expectedPath),
                StringComparison.OrdinalIgnoreCase) &&
            row.RegistryValue.Equals("ItemData", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(row.Value, gpmcPath,
                StringComparison.OrdinalIgnoreCase)).ToArray();

        if (pathRows.Length == 0)
            return Block(SourceMissing,
                "No matching literal SRP ItemData path was found in the complete Registry.pol. " +
                "Do not assume the rule is absent from all GPOs or clients.");
        if (pathRows.Length != 1)
            return Block(SourceAmbiguous,
                "Multiple Registry.pol ItemData records contain this exact SRP path. " +
                "Never choose the first GUID or level.");

        var record = pathRows[0];
        if (record.State != "Stored registry value" ||
            record.ValueType is not ("REG_SZ" or "REG_EXPAND_SZ") ||
            !record.SourceSha256.Equals(fileEvidence.Sha256,
                StringComparison.OrdinalIgnoreCase) ||
            !TryParseSrpPathRuleKey(record.RegistryKey, out var level) ||
            record.Value.Length is < 2 or > 4096 ||
            record.Value.Any(char.IsControl) ||
            !string.Equals(record.Value, record.Value.Trim(), StringComparison.Ordinal))
            return Block(SourceNotExact,
                "ItemData must be a typed, non-operational REG_SZ/REG_EXPAND_SZ, " +
                "one exact Safer\\CodeIdentifiers\\{level}\\Paths\\{GUID} key, " +
                "with matching file/row SHA-256. No special Registry.pol operations.");

        var sourceLevel = level == "0" ? "Disallowed" : "Unrestricted";
        var gpmcLevelName = gpmcLevel == "0" ? "Disallowed" : "Unrestricted";
        var explanation = "Exactly one typed SRP ItemData PATH in a complete SHA-256-bound " +
            "scope-specific Registry.pol matches the GPMC Path Rule. " +
            "Only path and rule security LEVEL are compared. " +
            "Other rule flags, current MMC value, other GPO precedence " +
            "and effective client RSoP are NOT verified.";
        return new Result(level == gpmcLevel ? Match : Mismatch,
            explanation, gpmcPath, gpmcLevelName, sourceLevel,
            fileEvidence.Sha256);
    }

    private static bool TryGetGpmcLevel(string summary, out string level)
    {
        level = string.Empty;
        if (string.IsNullOrWhiteSpace(summary) || summary.Length > 8192)
            return false;
        var matches = summary.Split(';')
            .Select(part => part.Trim())
            .Where(part =>
            {
                var equals = part.IndexOf('=');
                return equals > 0 && part[..equals].Trim().Equals(
                    "SecurityLevel", StringComparison.OrdinalIgnoreCase);
            })
            .ToArray();
        if (matches.Length != 1)
            return false;
        var raw = matches[0][(matches[0].IndexOf('=') + 1)..].Trim();
        if (raw.Equals("Disallowed", StringComparison.OrdinalIgnoreCase))
            level = "0";
        else if (raw.Equals("Unrestricted", StringComparison.OrdinalIgnoreCase))
            level = "262144";
        return level.Length != 0;
    }

    private static bool TryParseSrpPathRuleKey(string key, out string level)
    {
        level = "";
        const string root =
            @"Software\Policies\Microsoft\Windows\Safer\CodeIdentifiers\";
        if (string.IsNullOrWhiteSpace(key) ||
            !key.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            return false;

        var parts = key[root.Length..].Split('\\', StringSplitOptions.None);
        if (parts.Length != 3 ||
            !parts[1].Equals("Paths", StringComparison.OrdinalIgnoreCase) ||
            !Guid.TryParseExact(parts[2], "B", out _))
            return false;

        if (parts[0] is not ("0" or "262144"))
            return false;
        level = parts[0];
        return true;
    }

    private static bool IsSha256(string? hash) =>
        hash is { Length: 64 } && hash.All(Uri.IsHexDigit);

    private static Result Block(string status, string detail) => new(status, detail);
}

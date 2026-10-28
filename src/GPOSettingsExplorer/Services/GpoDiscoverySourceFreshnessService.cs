using System;
using System.Collections.Generic;
using System.Linq;
using GPOSettingsExplorer.Models;

namespace GPOSettingsExplorer.Services;

/// <summary>
/// Reconciles a previously captured Registry.pol evidence row against a
/// newly scanned pinned-DC source file. This is a read-only source freshness
/// check, NOT effective RSoP or a proof of which DC MMC itself contacted.
/// </summary>
public static class GpoDiscoverySourceFreshnessService
{
    public sealed record ValidationResult(
        bool IsCurrent, string Status, string Explanation,
        DateTimeOffset CheckedAt)
    {
        public static ValidationResult Block(string status, string explanation,
            DateTimeOffset checkedAt) => new(false, status, explanation, checkedAt);
    }

    /// <summary>
    /// Only records from an entirely parsed, sha256-checked Registry.pol file
    /// with an exact scope and canonical pinned-DC source path may enter the
    /// production MMC/GPMC literal comparison pipeline. Partial files remain
    /// visible in source diagnostics, never as verified source values.
    /// </summary>
    public static IReadOnlyList<RealSettingRecord> SelectCompleteRegistryRecords(
        RealSettingsScanResult snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (string.IsNullOrWhiteSpace(snapshot.Domain) ||
            string.IsNullOrWhiteSpace(snapshot.DomainController))
            return Array.Empty<RealSettingRecord>();

        var goodFiles = snapshot.Files
            .Where(file => file.Status == "Read" && IsSha256(file.Sha256))
            .ToArray();
        return snapshot.Rows.Where(row =>
        {
            if (row.GpoId != snapshot.GpoId || !IsSha256(row.SourceSha256))
                return false;
            var expected = CanonicalFile(snapshot.Domain,
                snapshot.DomainController, snapshot.GpoId, row.Scope);
            if (string.IsNullOrEmpty(expected) ||
                !NormalizePath(row.SourceFile).Equals(expected,
                    StringComparison.OrdinalIgnoreCase))
                return false;
            return goodFiles.Count(file =>
                NormalizePath(file.SourceFile).Equals(expected,
                    StringComparison.OrdinalIgnoreCase) &&
                file.Sha256.Equals(row.SourceSha256,
                    StringComparison.OrdinalIgnoreCase)) == 1;
        }).ToArray();
    }

    public static ValidationResult Check(
        GpoDiscoveryEvidenceService.Evidence expected,
        RealSettingsScanResult current,
        string expectedDomain, string expectedDc)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(current);
        var checkedAt = current.CapturedAt;

        if (string.IsNullOrWhiteSpace(expectedDomain) ||
            string.IsNullOrWhiteSpace(expectedDc) ||
            current.GpoId != expected.GpoId ||
            !current.Domain.Equals(expectedDomain, StringComparison.OrdinalIgnoreCase) ||
            !current.DomainController.Equals(expectedDc, StringComparison.OrdinalIgnoreCase))
            return ValidationResult.Block("SOURCE_IDENTITY_CHANGED",
                "GPO, domain, or pinned domain controller differs from the captured evidence.",
                checkedAt);

        if (expected.SourceStatus != "REGISTRY_SOURCE_LOCATED" ||
            !IsSha256(expected.SourceFileHash) ||
            !IsSha256(expected.IdentityHash) ||
            string.IsNullOrWhiteSpace(expected.RegistryTarget) ||
            expected.RegistryTarget.Contains(" / **", StringComparison.Ordinal))
            return ValidationResult.Block("SOURCE_NOT_ELIGIBLE",
                "A unique Registry.pol source with verified identity and SHA-256 is required.",
                checkedAt);

        var relative = expected.Scope.Equals("Computer", StringComparison.OrdinalIgnoreCase)
            ? "/Machine/Registry.pol"
            : expected.Scope.Equals("User", StringComparison.OrdinalIgnoreCase)
                ? "/User/Registry.pol"
                : string.Empty;
        if (relative.Length == 0)
            return ValidationResult.Block("SOURCE_SCOPE_UNSUPPORTED",
                "Only Computer/Machine and User/User Registry.pol paths are supported.",
                checkedAt);

        // Exact scope-specific UNC path, never a suffix search that could
        // accept the same Registry.pol file name in a different GPO folder.
        var canonicalFile = CanonicalFile(expectedDomain, expectedDc,
            expected.GpoId, expected.Scope);
        var matchedFiles = current.Files.Where(file =>
            NormalizePath(file.SourceFile).Equals(
                canonicalFile, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matchedFiles.Length != 1)
            return ValidationResult.Block("SOURCE_FILE_AMBIGUOUS",
                "Expected one exact scope-specific Registry.pol source file record; found " +
                matchedFiles.Length + ".", checkedAt);

        var file = matchedFiles[0];
        if (file.Status != "Read" || !IsSha256(file.Sha256))
            return ValidationResult.Block("SOURCE_FILE_INCOMPLETE",
                "The Registry.pol file is missing, partially parsed, unreadable, or lacks a valid SHA-256.",
                checkedAt);

        if (!file.Sha256.Equals(expected.SourceFileHash, StringComparison.OrdinalIgnoreCase))
            return ValidationResult.Block("SOURCE_FILE_CHANGED",
                "The pinned DC Registry.pol SHA-256 differs from the original captured snapshot.",
                checkedAt);

        var sameTarget = current.Rows.Where(record =>
            record.GpoId == expected.GpoId &&
            record.Scope.Equals(expected.Scope, StringComparison.OrdinalIgnoreCase) &&
            NormalizePath(record.SourceFile).Equals(
                NormalizePath(file.SourceFile), StringComparison.OrdinalIgnoreCase) &&
            string.Equals(
                string.IsNullOrWhiteSpace(record.RegistryValue)
                    ? record.RegistryKey
                    : record.RegistryKey + " / " + record.RegistryValue,
                expected.RegistryTarget, StringComparison.OrdinalIgnoreCase)).ToArray();

        if (sameTarget.Length != 1)
            return ValidationResult.Block("SOURCE_TARGET_AMBIGUOUS",
                "The pinned source contains zero or multiple entries for this exact registry target.",
                checkedAt);

        var observed = sameTarget[0];
        if (!observed.SourceSha256.Equals(file.Sha256, StringComparison.OrdinalIgnoreCase) ||
            !observed.ValueType.Equals(expected.SourceStoredType, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(observed.Value, expected.SourceStoredValue, StringComparison.Ordinal))
            return ValidationResult.Block("SOURCE_VALUE_CHANGED",
                "Stored Registry.pol value/type or record SHA-256 differs from the original snapshot.",
                checkedAt);

        return new ValidationResult(true, "SOURCE_RECHECKED",
            "Re-read from the pinned DC; Registry.pol file SHA-256, exact key/value target, stored type, " +
            "value and GPO/scope all match the captured source.", checkedAt);
    }

    private static string CanonicalFile(string domain, string dc, Guid gpoId, string scope)
    {
        if (string.IsNullOrWhiteSpace(domain) || string.IsNullOrWhiteSpace(dc))
            return string.Empty;
        var folder = scope.Equals("Computer", StringComparison.OrdinalIgnoreCase)
            ? "Machine"
            : scope.Equals("User", StringComparison.OrdinalIgnoreCase)
                ? "User" : string.Empty;
        if (folder.Length == 0)
            return string.Empty;
        return NormalizePath(@"\\" + dc + @"\SYSVOL\" + domain +
            @"\Policies\" + gpoId.ToString("B") + @"\" + folder + @"\Registry.pol");
    }

    private static string NormalizePath(string? value) =>
        (value ?? string.Empty).Replace('\\', '/').TrimEnd('/');

    private static bool IsSha256(string? hash) =>
        hash is { Length: 64 } && hash.All(Uri.IsHexDigit);
}

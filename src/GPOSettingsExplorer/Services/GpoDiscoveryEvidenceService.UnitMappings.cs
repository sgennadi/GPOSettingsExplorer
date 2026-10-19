using System;
using System.Collections.Generic;
using System.Linq;

namespace GPOSettingsExplorer.Services;

public static partial class GpoDiscoveryEvidenceService
{
    /// <summary>
    /// A caller-reviewed unit rule for one exact policy and one exact source
    /// snapshot. The ADMX/ADML evidence digest is only an audit anchor, not
    /// evidence that all decimal elements share a unit.
    /// No automated ADMX label/description/range guessing is permitted.
    /// </summary>
    public sealed record VerifiedUnitMapping(
        Guid GpoId, string Scope, string RegistryTarget, string SourceUnit,
        string Provenance)
    {
        public string IdentitySha256 { get; init; } = string.Empty;
        public string SourceFileSha256 { get; init; } = string.Empty;
        public string DefinitionEvidenceSha256 { get; init; } = string.Empty;
        public bool ReviewedAgainstMmc { get; init; }
        public string ReviewReference { get; init; } = string.Empty;
    }

    /// <summary>
    /// Fail closed on missing/contradictory reviews, changed policy identity,
    /// changed Registry.pol snapshot, or unsupported unit pairs. Neither a
    /// nonempty provenance string nor an ADMX decimal range is proof of units.
    /// The production export deliberately passes no rules until a trusted
    /// review workflow can establish them.
    /// </summary>
    public static IReadOnlyList<Evidence> ApplyVerifiedUnitMappings(
        IEnumerable<Evidence> evidence, IEnumerable<VerifiedUnitMapping> mappings)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentNullException.ThrowIfNull(mappings);
        var rules = mappings.ToArray();
        return evidence.Select(item =>
        {
            // Reapplying mappings must not preserve an old or stale approval.
            var cleared = item with
            {
                SourceUnit = string.Empty,
                UnitMappingStatus = "NOT_EVALUATED",
                UnitMappingEvidence = null
            };
            var matching = rules.Where(rule => MatchesTarget(item, rule)).ToArray();
            if (matching.Length == 0)
                return cleared;

            // Include unsupported and unreviewed rules in the ambiguity test.
            // Otherwise a valid-looking rule can mask a contradictory one.
            if (matching.Length != 1)
                return cleared with
                {
                    UnitMappingStatus = "AMBIGUOUS_RULES",
                    Notes = cleared.Notes + " Conflicting/duplicate unit rules; conversion blocked."
                };

            var candidate = matching[0];
            if (item.SourceStatus != "REGISTRY_SOURCE_LOCATED" ||
                !item.SourceStoredType.Equals("REG_DWORD", StringComparison.OrdinalIgnoreCase) ||
                !IsSha256(item.SourceFileHash))
                return cleared with
                {
                    UnitMappingStatus = "SOURCE_NOT_VERIFIED",
                    Notes = cleared.Notes + " Unit mapping blocked: no supported, hashed DWORD Registry.pol source."
                };

            // Deliberately small conversion allowlist. Unknown units cannot
            // be normalized to a preferred unit by textual similarity.
            if (!string.Equals(candidate.SourceUnit, "minutes", StringComparison.Ordinal))
                return cleared with
                {
                    UnitMappingStatus = "UNSUPPORTED_UNIT",
                    Notes = cleared.Notes + " Unit mapping blocked: unsupported source unit."
                };

            if (!IsVerifiedUnitProof(item, candidate))
                return cleared with
                {
                    UnitMappingStatus = "UNVERIFIED_RULE",
                    Notes = cleared.Notes + " Unit mapping blocked: missing review or stale identity/source fingerprints."
                };

            return cleared with
            {
                SourceUnit = candidate.SourceUnit,
                UnitMappingStatus = "VERIFIED_EXPLICIT",
                UnitMappingEvidence = candidate,
                Notes = cleared.Notes + " Unit review reference: " + candidate.ReviewReference
            };
        }).ToArray();
    }

    private static bool MatchesTarget(Evidence item, VerifiedUnitMapping mapping) =>
        item.GpoId == mapping.GpoId &&
        string.Equals(item.Scope, mapping.Scope, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(item.RegistryTarget, mapping.RegistryTarget, StringComparison.OrdinalIgnoreCase) &&
        !string.IsNullOrWhiteSpace(item.RegistryTarget);

    private static bool IsVerifiedUnitProof(Evidence item, VerifiedUnitMapping mapping) =>
        MatchesTarget(item, mapping) &&
        item.SourceStatus == "REGISTRY_SOURCE_LOCATED" &&
        item.SourceStoredType.Equals("REG_DWORD", StringComparison.OrdinalIgnoreCase) &&
        mapping.SourceUnit == "minutes" &&
        mapping.ReviewedAgainstMmc &&
        !string.IsNullOrWhiteSpace(mapping.Provenance) &&
        !string.IsNullOrWhiteSpace(mapping.ReviewReference) &&
        IsSha256(item.IdentityHash) &&
        IsSha256(item.SourceFileHash) &&
        IsSha256(mapping.IdentitySha256) &&
        IsSha256(mapping.SourceFileSha256) &&
        IsSha256(mapping.DefinitionEvidenceSha256) &&
        string.Equals(item.IdentityHash, mapping.IdentitySha256, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(item.SourceFileHash, mapping.SourceFileSha256, StringComparison.OrdinalIgnoreCase);

    private static bool IsSha256(string? value) =>
        value is { Length: 64 } && value.All(Uri.IsHexDigit);
}

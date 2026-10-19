using GPOSettingsExplorer.Models;

namespace GPOSettingsExplorer.Services;

/// <summary>
/// Source-aware, read-only correlation of one GPMC entry with a native MMC
/// inventory captured for the SAME GPO. It is not an RSoP/setting-value check.
/// Exact section and first-column identity must both agree; the resolver never
/// uses "first match", a substring, a partially matched MMC folder, or the
/// first identical GPMC XML detail.
/// </summary>
public static class GpoExactMmcRouteResolver
{
    public const string Ready = "EXACT_ROW_OBSERVED";
    public const string NoInventory = "MMC_NOT_SCANNED";
    public const string SectionOnly = "SECTION_ONLY";
    public const string Ambiguous = "AMBIGUOUS";
    public const string SourceAmbiguous = "SOURCE_AMBIGUOUS";
    public const string Unsupported = "UNSUPPORTED_SOURCE";
    public const string NoRow = "EXACT_ROW_NOT_OBSERVED";

    public sealed record Resolution(
        string Status, string Explanation, MmcInventoryEntry? Entry,
        IReadOnlyList<MmcInventoryEntry> Candidates)
    {
        public bool CanOpen => Status == Ready && Entry is not null;
        public static Resolution Deny(string status, string explanation,
            IReadOnlyList<MmcInventoryEntry>? candidates = null) =>
            new(status, explanation, null,
                candidates ?? Array.Empty<MmcInventoryEntry>());
    }

    public static Resolution Resolve(
        PolicySettingInfo setting,
        IReadOnlyList<PolicySettingInfo> configured,
        IReadOnlyList<MmcInventoryEntry>? inventory,
        IReadOnlyList<AdmxPolicyDefinition>? admx = null)
    {
        ArgumentNullException.ThrowIfNull(setting);
        ArgumentNullException.ThrowIfNull(configured);

        if (setting.GpoId == Guid.Empty || !IsSupportedScope(setting.Scope) ||
            SecurityXmlEntryClassifier.IsTechnicalDetail(setting))
            return Resolution.Deny(Unsupported,
                "This is a nested XML detail or has no unique GPO/scope; it is not a native policy row.");

        if (SameSourceIdentityCount(setting, configured) != 1)
            return Resolution.Deny(SourceAmbiguous,
                "Zero or multiple indexed GPMC rows share the same source identity. " +
                "Do not choose a native MMC row by label alone.");

        if (inventory is null)
            return Resolution.Deny(NoInventory,
                "No same-GPO native MMC inventory is available. Scan this GPO read-only on demand.");

        var rows = inventory
            .Where(row => row.GpoId == setting.GpoId &&
                row.Scope.Equals(setting.Scope, StringComparison.OrdinalIgnoreCase) &&
                row.Navigation.Equals("Exact MMC row candidate", StringComparison.Ordinal) &&
                row.TreeSegments.Count >= 3 &&
                !MmcInventorySafetyRules.ShouldSkipNode(row.TreeSegments, out _))
            .ToArray();

        var path = GpoPolicySectionRoutes.Resolve(setting);
        var eligible = FindMatchingRows(setting, rows, path, admx);
        if (eligible.Length > 1)
            return Resolution.Deny(Ambiguous,
                $"{eligible.Length} native MMC rows satisfy the source identity. " +
                "The exact row cannot be selected automatically.", eligible);

        if (eligible.Length == 1)
            return new Resolution(Ready,
                "One native row matches the same-GPO/scope exact tree route " +
                "and source-specific literal identity. The editor will re-read " +
                "the row before activating it; setting values and RSoP are NOT verified.",
                eligible[0], eligible);

        // Route hints are useful, but they are not evidence for a native row.
        if (path.Count > 0)
            return Resolution.Deny(SectionOnly,
                "A documented MMC SECTION is known, but no uniquely matching " +
                "native setting row was observed there. " +
                "Scan MMC, inspect nested paths and review unsupported snap-ins.");

        return Resolution.Deny(NoRow,
            "The indexed GPMC extension has no documented exact MMC route. " +
            "Review a native row manually or use the dedicated source editor.");
    }

    /// <summary>
    /// MMC inventory rows eligible for explicit operator selection. A manual
    /// link is a REVIEWED ROUTE only, never evidence that stored source values
    /// agree with the MMC or that a client applied the policy.
    /// </summary>
    public static IReadOnlyList<MmcInventoryEntry> ReviewCandidates(
        PolicySettingInfo setting, IReadOnlyList<MmcInventoryEntry> inventory)
    {
        ArgumentNullException.ThrowIfNull(setting);
        ArgumentNullException.ThrowIfNull(inventory);
        return inventory.Where(row =>
            setting.GpoId != Guid.Empty &&
            row.GpoId == setting.GpoId &&
            row.Scope.Equals(setting.Scope, StringComparison.OrdinalIgnoreCase) &&
            row.Navigation == "Exact MMC row candidate" &&
            row.TreeSegments.Count >= 3 &&
            !MmcInventorySafetyRules.ShouldSkipNode(row.TreeSegments, out _))
            .OrderBy(row => string.Join(" > ", row.TreeSegments),
                StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row.SettingName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static bool MayReview(
        PolicySettingInfo setting, IReadOnlyList<PolicySettingInfo> configured,
        MmcInventoryEntry observed, IReadOnlyList<MmcInventoryEntry> inventory)
    {
        if (SameSourceIdentityCount(setting, configured) != 1)
            return false;

        // One native row must be present uniquely in the same-GPO/scope
        // observation; never let a picker reuse a stale or fabricated row.
        var eligible = ReviewCandidates(setting, inventory)
            .Where(row => row == observed).ToArray();
        return eligible.Length == 1;
    }

    private static MmcInventoryEntry[] FindMatchingRows(
        PolicySettingInfo setting, MmcInventoryEntry[] rows,
        IReadOnlyList<string> section, IReadOnlyList<AdmxPolicyDefinition>? admx)
    {
        var nativeName = setting.SettingName.Trim();
        var knownKerberos = KerberosPolicyMetadataService.IsKnownKerberosSetting(setting);
        if (knownKerberos &&
            KerberosPolicyMetadataService.TryGet(setting.SettingName, out var kerberos))
            nativeName = kerberos.DisplayName;
        var knownAccount = GpoKnownAccountPolicyService.TryDescribe(
            setting, out var account);
        if (knownAccount)
            nativeName = account.NativeName;

        var pathRule = SoftwareRestrictionMmcRouteService.IsPathRule(setting);
        if (pathRule)
        {
            if (!SoftwareRestrictionMmcRouteService.TryGetPathRuleTarget(setting, out var target))
                return Array.Empty<MmcInventoryEntry>();
            return rows.Where(row =>
                SoftwareRestrictionMmcRouteService.IsExactAdditionalRulesSection(
                    setting.Scope, string.Join(" > ", row.TreeSegments)) &&
                SoftwareRestrictionMmcRouteService.ExactPath(row.SettingName, target))
                .ToArray();
        }

        var definition = setting.Extension.Equals("RegistrySettings",
                             StringComparison.OrdinalIgnoreCase) ||
                         setting.Extension.Equals("Administrative Templates",
                             StringComparison.OrdinalIgnoreCase)
            ? UnifiedSettingsCatalogService.ResolveDefinition(
                setting, admx ?? Array.Empty<AdmxPolicyDefinition>())
            : null;

        if (definition is not null)
            return rows.Where(row =>
                GpoDiscoveryAdmxIdentityService.MatchesExactFolder(
                    setting.Scope, string.Join(" > ", row.TreeSegments),
                    definition.Category) &&
                ExactName(row.SettingName, definition.DisplayName))
                .ToArray();

        // A Registry.pol record without an exact ADMX definition is NOT a
        // native Administrative Templates row regardless of its display name.
        if (setting.Extension.Equals("RegistrySettings",
                StringComparison.OrdinalIgnoreCase) ||
            setting.Extension.Equals("Administrative Templates",
                StringComparison.OrdinalIgnoreCase))
            return Array.Empty<MmcInventoryEntry>();

        if ((knownKerberos || knownAccount) && section.Count > 0)
            return rows.Where(row => SamePath(section, row.TreeSegments) &&
                ExactName(row.SettingName, nativeName)).ToArray();

        // Advanced Audit Policy has another category level after "Audit
        // Policies". GPMC exposes an explicit SubcategoryName. Only a SINGLE
        // literal match anywhere BELOW the verified audit root may be used.
        if (setting.Extension.Equals("AuditSettings",
                StringComparison.OrdinalIgnoreCase) && section.Count > 0)
            return rows.Where(row => row.TreeSegments.Count > section.Count &&
                SamePathPrefix(section, row.TreeSegments) &&
                ExactName(row.SettingName, nativeName)).ToArray();

        // GPMC source type/category must yield a *complete* known native
        // section. A generic name observed elsewhere in the tree is not proof.
        if (section.Count >= 4)
            return rows.Where(row =>
                SamePath(section, row.TreeSegments) &&
                ExactName(row.SettingName, nativeName)).ToArray();

        return Array.Empty<MmcInventoryEntry>();
    }

    private static bool ExactName(string a, string b) =>
        !string.IsNullOrWhiteSpace(a) && !string.IsNullOrWhiteSpace(b) &&
        // Unlike fuzzy name probes, a full source-driven native row identity
        // uses strict whitespace normalization with no prefix matching.
        MmcPolicyNameMatcher.Exact(a, b);

    private static bool SamePath(IReadOnlyList<string> expected,
        IReadOnlyList<string> actual) =>
        expected.Count == actual.Count &&
        SamePathPrefix(expected, actual);

    private static bool SamePathPrefix(IReadOnlyList<string> prefix,
        IReadOnlyList<string> actual)
    {
        if (actual.Count < prefix.Count)
            return false;
        for (var i = 0; i < prefix.Count; i++)
            if (!MmcTreePathMatcher.SectionNameMatches(actual[i], prefix[i]))
                return false;
        return true;
    }

    private static bool IsSupportedScope(string scope) =>
        scope.Equals("Computer", StringComparison.OrdinalIgnoreCase) ||
        scope.Equals("User", StringComparison.OrdinalIgnoreCase);

    private static int SameSourceIdentityCount(
        PolicySettingInfo setting, IReadOnlyList<PolicySettingInfo> settings) =>
        settings.Count(other =>
            other.GpoId == setting.GpoId &&
            other.Scope.Equals(setting.Scope, StringComparison.OrdinalIgnoreCase) &&
            other.Extension.Equals(setting.Extension, StringComparison.OrdinalIgnoreCase) &&
            other.Category.Equals(setting.Category, StringComparison.OrdinalIgnoreCase) &&
            other.SettingName.Equals(setting.SettingName, StringComparison.OrdinalIgnoreCase) &&
            other.RegistryKey.Equals(setting.RegistryKey, StringComparison.OrdinalIgnoreCase) &&
            other.RegistryValue.Equals(setting.RegistryValue, StringComparison.OrdinalIgnoreCase) &&
            other.Value.Equals(setting.Value, StringComparison.Ordinal));
}

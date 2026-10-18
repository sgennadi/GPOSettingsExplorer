using System.Runtime.CompilerServices;
using GPOSettingsExplorer.Models;

namespace GPOSettingsExplorer.Services;

/// <summary>
/// Deterministic, UI-independent join between configured GPMC rows, ADMX
/// definitions and a *single* optionally observed MMC editor session.
/// Matching is conservative and ambiguity is never silently resolved.
/// </summary>
public static class UnifiedSettingsCatalogService
{
    // ADMX/ADML describe one reusable schema for a domain. The map is
    // deliberately cached by catalog snapshot identity, not by GPO GUID.
    // Replacing the catalog (a new immutable snapshot) creates a fresh map,
    // while old snapshots and their indexes can be garbage collected.
    private sealed record SchemaIndex(
        IReadOnlyDictionary<string, List<AdmxPolicyDefinition>> ByName,
        IReadOnlyDictionary<string, List<AdmxPolicyDefinition>> ByRegistry,
        IReadOnlyList<UnifiedSettingInfo> Fields,
        IReadOnlyList<(string Identity, UnifiedSettingInfo Row)> Policies);

    private static readonly ConditionalWeakTable<
        IReadOnlyList<AdmxPolicyDefinition>, SchemaIndex> SchemaIndexes = new();

    private static SchemaIndex IndexFor(IReadOnlyList<AdmxPolicyDefinition> definitions) =>
        SchemaIndexes.GetValue(definitions, CreateIndex);

    private static SchemaIndex CreateIndex(IReadOnlyList<AdmxPolicyDefinition> definitions)
    {
        var names = new Dictionary<string, List<AdmxPolicyDefinition>>(
            StringComparer.OrdinalIgnoreCase);
        var registry = new Dictionary<string, List<AdmxPolicyDefinition>>(
            StringComparer.OrdinalIgnoreCase);
        var fields = new List<UnifiedSettingInfo>();
        var policies = new List<(string Identity, UnifiedSettingInfo Row)>();

        foreach (var policy in definitions)
        {
            foreach (var scope in Scopes(policy.Scope))
            {
                var named = Named(scope, policy.DisplayName);
                if (!names.TryGetValue(named, out var sameName))
                    names[named] = sameName = new List<AdmxPolicyDefinition>();
                sameName.Add(policy);

                // One policy can mention the same target through its base
                // value and an element. Count it once *per catalog entry*,
                // but never erase duplicated entries: two identical ADMX
                // definitions are ambiguous for safe MMC/editor routing.
                var ownTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var (key, value) in RegistryTargets(policy))
                {
                    var identity = RegistryIdentity(scope, key, value);
                    if (!ownTargets.Add(identity))
                        continue;
                    if (!registry.TryGetValue(identity, out var matches))
                        registry[identity] = matches = new List<AdmxPolicyDefinition>();
                    matches.Add(policy);
                }

                var supported = IsDirectAdmxSupported(policy);
                foreach (var element in policy.Elements)
                {
                    var label = !string.IsNullOrWhiteSpace(element.Label)
                        ? element.Label
                        : !string.IsNullOrWhiteSpace(element.ValueName)
                            ? element.ValueName : element.Id;
                    fields.Add(new UnifiedSettingInfo
                    {
                        SettingName = label,
                        Scope = scope,
                        Category = policy.Category + " > " + policy.DisplayName,
                        State = "Template - state unknown",
                        Value = "",
                        Sources = "ADMX field definition",
                        Capability = supported
                            ? "Open exact field (guarded)"
                            : "Unsupported field (review only)",
                        RegistryTarget = JoinTarget(element.Key, element.ValueName),
                        Explanation = "Reusable ADMX element " + element.Id +
                            " from " + policy.AdmxFile + ". This is a field schema, " +
                            "not the current value of any GPO. The selected GPO " +
                            "is read separately when opening the editor.",
                        Kind = "ADMX field",
                        Admx = policy,
                        AdmxElement = element
                    });
                }
                policies.Add((DefinitionId(policy, scope), new UnifiedSettingInfo
                {
                    SettingName = policy.DisplayName,
                    Scope = scope,
                    Category = policy.Category,
                    State = "Template - state unknown",
                    Value = "",
                    Sources = "ADMX definition",
                    Capability = supported
                        ? "Configure in GPO (guarded)"
                        : "Native GPMC / review",
                    RegistryTarget = JoinTarget(policy.Key, policy.ValueName),
                    Explanation = "Available ADMX template, not a configured setting. " +
                        "Choose a target GPO before reading its actual current state or configuring. " +
                        "Nothing is presumed Not Configured.",
                    Kind = "ADMX template",
                    Admx = policy
                }));
            }
        }
        return new SchemaIndex(names, registry, fields, policies);
    }
    public static UnifiedCatalogResult Build(
        IReadOnlyList<PolicySettingInfo> configured,
        IReadOnlyList<AdmxPolicyDefinition>? catalog,
        IReadOnlyList<MmcInventoryEntry>? mmc,
        Guid? selectedGpoId,
        string mmcCoverage = "",
        RealSettingsScanResult? sourceFiles = null)
    {
        var source = selectedGpoId is Guid target
            ? configured.Where(s => s.GpoId == target).ToArray()
            : configured.ToArray();

        var definitions = catalog ?? Array.Empty<AdmxPolicyDefinition>();
        var observations = (mmc ?? Array.Empty<MmcInventoryEntry>())
            .Where(m => selectedGpoId is null || m.GpoId == selectedGpoId)
            .ToArray();

        // The same parsed schema/map is reused for every selected GPO.
        // Only GPMC/MMC/source evidence below is GPO-specific.
        var schema = IndexFor(definitions);
        var lookup = schema.ByName;
        var registryLookup = schema.ByRegistry;

        var observationsLookup = observations
            .GroupBy(m => $"{m.GpoId:B}|{Named(m.Scope, m.SettingName)}",
                StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToArray(),
                StringComparer.OrdinalIgnoreCase);

        var usedDefinitions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var usedMmc = new HashSet<MmcInventoryEntry>();
        var rows = new List<UnifiedSettingInfo>(
            source.Length + schema.Fields.Count + schema.Policies.Count +
            observations.Length);

        foreach (var setting in source)
        {
            var technical = SecurityXmlEntryClassifier.IsTechnicalDetail(setting);
            // Low-level GPMC XML leaf entries must never match an ADMX
            // editor by their generic label ('Registry', 'Member', etc.).
            var admx = technical ? null : FindDefinition(setting, lookup, registryLookup);
            if (admx is not null)
                usedDefinitions.Add(DefinitionId(admx, setting.Scope));
            var observed = technical ? null : FindObserved(setting, observationsLookup);
            if (observed is not null)
                usedMmc.Add(observed);

            var sources = technical ? "GPMC XML detail" : "GPMC configured";
            if (admx is not null) sources += " + ADMX";
            if (observed is not null) sources += " + MMC";

            rows.Add(new UnifiedSettingInfo
            {
                GpoId = setting.GpoId,
                GpoName = setting.GpoName,
                SettingName = setting.SettingName,
                Scope = setting.Scope,
                Category = SecurityXmlEntryClassifier.InferLegacyCategory(setting)
                           ?? setting.Category,
                State = technical ? "XML detail" : setting.State,
                Value = technical
                    ? SecurityXmlEntryClassifier.DisplaySummary(setting)
                    : setting.Value,
                Sources = sources,
                Capability = technical
                    ? "Inspect detail (read-only)"
                    : admx is not null && IsDirectAdmxSupported(admx)
                        ? "ADMX editor (guarded)"
                        : "View / supported editor",
                RegistryTarget = GpoSettingSourceProvenance.DisplayRegistryTarget(setting),
                Explanation = technical
                    ? "This is a nested GPMC XML description, not an independent policy row. " +
                      "Its full original value remains available in View details. " +
                      "MMC can open only a related section; exact editing is not verified."
                    : GpoSettingSourceProvenance.IsGppRegistrySource(setting)
                        ? "GPMC XML identifies one GPP Registry preference item: exact stored " +
                          "hive/key/value and action. This is NOT Registry.pol, local Regedit, " +
                          "native MMC row verification or effective client RSoP."
                        : "GPMC XML/index reports this configured setting. " +
                          "Its effective application requires independent RSoP/WMI/Security evaluation." +
                          (observed is null ? "" : " MMC value is a separate observation."),
                Kind = technical ? "GPMC detail" : "Configured",
                IsTechnicalDetail = technical,
                Configured = setting,
                Admx = admx,
                Mmc = observed
            });
        }

        foreach (var observed in observations)
        {
            if (usedMmc.Contains(observed))
                continue;

            var key = Named(observed.Scope, observed.SettingName);
            var synthetic = new PolicySettingInfo
            {
                GpoId = observed.GpoId,
                GpoName = observed.GpoName,
                Scope = observed.Scope,
                SettingName = observed.SettingName,
                Category = observed.SectionPath,
                Extension = observed.SectionPath.Contains(
                    "Administrative Templates", StringComparison.OrdinalIgnoreCase)
                    ? "Administrative Templates" : string.Empty
            };
            var admx = FindDefinition(synthetic, lookup, registryLookup);
            if (admx is not null)
                usedDefinitions.Add(DefinitionId(admx, observed.Scope));

            rows.Add(new UnifiedSettingInfo
            {
                GpoId = observed.GpoId,
                GpoName = observed.GpoName,
                SettingName = observed.SettingName,
                Scope = observed.Scope,
                Category = observed.SectionPath,
                State = observed.MmcState,
                Value = observed.MmcValue,
                Sources = admx is null ? "MMC observed" : "MMC observed + ADMX",
                Capability = admx is not null && IsDirectAdmxSupported(admx)
                    ? "ADMX editor (guarded)"
                    : observed.Navigation == "Exact MMC row candidate"
                        ? "Exact MMC row (manual)" : "View only",
                RegistryTarget = admx is null
                    ? "" : JoinTarget(admx.Key, admx.ValueName),
                Explanation = "Observed in one MMC editor; missing from the GPMC index " +
                    "does NOT prove the policy is Not Configured. " +
                    "Native editing requires a fresh exact-path/row verification.",
                Kind = "MMC observed",
                Mmc = observed,
                Admx = admx
            });
        }

        // Keep source-file records distinct from GPMC XML, MMC and ADMX:
        // joining only by same registry name could silently confuse
        // special commands, element values and CSE-specific semantics.
        // Explicitly show stored *source* state, not live/effective policy.
        var stored = sourceFiles is null ||
                     selectedGpoId is Guid selection && sourceFiles.GpoId != selection
            ? Array.Empty<RealSettingRecord>()
            : sourceFiles.Rows.ToArray();

        foreach (var record in stored)
        {
            rows.Add(new UnifiedSettingInfo
            {
                GpoId = record.GpoId,
                GpoName = record.GpoName,
                SettingName = record.SettingName,
                Scope = record.Scope,
                Category = record.Category,
                State = record.State,
                Value = record.Value,
                Sources = "SYSVOL policy source",
                Capability = "Inspect source (read-only)",
                RegistryTarget = string.IsNullOrWhiteSpace(record.RegistryKey)
                    ? "" : JoinTarget(record.RegistryKey, record.RegistryValue),
                Explanation = record.Evidence + " | " + record.SourceFile +
                    " | source SHA-256 " + record.SourceSha256 +
                    " | No statement about actual RSoP or target applicability.",
                Kind = "Stored source",
                StoredSource = record
            });
        }

        // Field rows and template metadata are allocated once per ADMX
        // snapshot, independent of the chosen GPO. Only GPO evidence rows
        // above are rebuilt when the selection or state changes.
        rows.AddRange(schema.Fields);
        foreach (var (identity, template) in schema.Policies)
        {
            if (!usedDefinitions.Contains(identity))
                rows.Add(template);
        }

        var ordered = rows.OrderBy(row => row.Kind switch
            {
                "Configured" => 0,
                "Stored source" => 1,
                "MMC observed" => 2,
                "ADMX field" => 3,
                "ADMX template" => 4,
                _ => 5
            }).ThenBy(row => row.SettingName, StringComparer.CurrentCultureIgnoreCase)
              .ThenBy(row => row.GpoName, StringComparer.CurrentCultureIgnoreCase)
              .ToArray();

        var message = catalog is null
            ? "ADMX not loaded; partial unified catalog"
            : "ADMX loaded";
        if (mmcCoverage.Length > 0)
            message += "; MMC " + mmcCoverage;
        else
            message += "; MMC not scanned (optional)";
        if (sourceFiles is not null)
            message += "; source files " + sourceFiles.Coverage;

        return new UnifiedCatalogResult(
            ordered,
            rows.Count(row => row.Kind == "Configured"),
            rows.Count(row => row.Kind == "ADMX template"),
            rows.Count(row => row.Kind == "MMC observed"),
            catalog is not null,
            message)
        {
            SourceFileEntries = stored.Length,
            AdmxFieldCount = schema.Fields.Count
        };
    }

    /// <summary>
    /// One clean field-oriented view over the joined evidence. ADMX field
    /// rows replace the duplicate configured-policy summary when element
    /// controls are available. Policy toggles without elements, unmatched
    /// GPMC settings, and unmapped MMC rows remain visible. The full source
    /// catalog is retained under All sources for audits and troubleshooting.
    /// </summary>
    public static bool IsPrimaryMapRow(UnifiedSettingInfo row) =>
        row.Kind switch
        {
            "ADMX field" => true,
            "ADMX template" => row.Admx is { Elements.Count: 0 },
            "Configured" => !row.IsTechnicalDetail &&
                (row.Admx is null || row.Admx.Elements.Count == 0),
            "MMC observed" => row.Admx is null ||
                row.Admx.Elements.Count == 0,
            _ => false
        };

    // Shared resolver used by All Settings, Global Search and the legacy
    // GPMC grid. A non-ADMX extension (Security, GPP Registry, etc.) must
    // never be treated as an Administrative Template merely by its label.
    public static AdmxPolicyDefinition? ResolveDefinition(
        PolicySettingInfo setting,
        IReadOnlyList<AdmxPolicyDefinition> policies)
    {
        var index = IndexFor(policies);
        return FindDefinition(setting, index.ByName, index.ByRegistry);
    }

    /// <summary>
    /// Find one specific ADMX input field by its scoped registry identity,
    /// never by localized display label or element order.
    /// </summary>
    public static AdmxElementDefinition? ResolveElement(
        PolicySettingInfo setting,
        AdmxPolicyDefinition definition)
    {
        if (!HasRegistryIdentity(setting) ||
            !Scopes(definition.Scope).Any(scope =>
                scope.Equals(setting.Scope, StringComparison.OrdinalIgnoreCase)))
            return null;

        var wanted = RegistryIdentity(setting.Scope,
            setting.RegistryKey, setting.RegistryValue);
        var matches = definition.Elements.Where(element =>
            !string.IsNullOrWhiteSpace(element.Key) &&
            !string.IsNullOrWhiteSpace(element.ValueName) &&
            RegistryIdentity(setting.Scope, element.Key, element.ValueName)
                .Equals(wanted, StringComparison.OrdinalIgnoreCase)).ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    // A malformed vendor ADMX with duplicate IDs or colliding registry
    // values cannot safely be written through the generic editor.
    public static bool IsDirectAdmxSupported(AdmxPolicyDefinition definition)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var element in definition.Elements)
        {
            if (element.Type == AdmxElementType.Unknown ||
                string.IsNullOrWhiteSpace(element.Id) ||
                !ids.Add(element.Id))
                return false;

            if (!string.IsNullOrWhiteSpace(element.Key) &&
                !string.IsNullOrWhiteSpace(element.ValueName) &&
                !targets.Add(RegistryIdentity(
                    definition.Scope, element.Key, element.ValueName)))
                return false;
        }
        return true;
    }

    /// <summary>
    /// A template may be edited only when ALL its registry targets map to
    /// exactly this one definition in the current ADMX snapshot. Looking up
    /// just the selected field is insufficient: another policy can share
    /// a different target written by the same editor.
    /// </summary>
    public static bool IsCatalogUniqueForWrite(
        AdmxPolicyDefinition definition,
        IReadOnlyList<AdmxPolicyDefinition> policies,
        string scope)
    {
        if (!IsDirectAdmxSupported(definition) ||
            !Scopes(definition.Scope).Any(s =>
                s.Equals(scope, StringComparison.OrdinalIgnoreCase)) ||
            !policies.Any(item => ReferenceEquals(item, definition)))
            return false;

        var targets = RegistryTargets(definition)
            .Select(t => RegistryIdentity(scope, t.Key, t.Value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        // A generic writer without a known target must not claim a write.
        if (targets.Length == 0)
            return false;

        var lookup = IndexFor(policies).ByRegistry;
        return targets.All(target =>
            lookup.TryGetValue(target, out var matches) &&
            matches.Count == 1 &&
            ReferenceEquals(matches[0], definition));
    }

    private static AdmxPolicyDefinition? FindDefinition(
        PolicySettingInfo setting,
        IReadOnlyDictionary<string, List<AdmxPolicyDefinition>> nameLookup,
        IReadOnlyDictionary<string, List<AdmxPolicyDefinition>> registryLookup)
    {
        if (!IsAdmxSource(setting))
            return null;

        if (HasRegistryIdentity(setting))
        {
            return registryLookup.TryGetValue(
                    RegistryIdentity(setting.Scope, setting.RegistryKey,
                        setting.RegistryValue), out var matches) &&
                   matches.Count == 1
                ? matches[0] : null;
        }

        if (!string.IsNullOrWhiteSpace(setting.RegistryKey) ||
            !nameLookup.TryGetValue(Named(setting.Scope, setting.SettingName),
                out var named))
            return null;

        var categories = named.Where(policy =>
            CategoriesAgree(setting.Category, policy.Category)).ToArray();
        return categories.Length == 1 ? categories[0] : null;
    }

    private static bool IsAdmxSource(PolicySettingInfo setting) =>
        setting.Extension.Equals("RegistrySettings", StringComparison.OrdinalIgnoreCase) ||
        setting.Extension.Equals("Administrative Templates",
            StringComparison.OrdinalIgnoreCase);

    private static bool HasRegistryIdentity(PolicySettingInfo setting) =>
        GpoSettingSourceProvenance.IsVerifiedRegistryTarget(setting) &&
        !string.IsNullOrWhiteSpace(setting.RegistryValue);

    private static IEnumerable<(string Key, string Value)> RegistryTargets(
        AdmxPolicyDefinition policy)
    {
        if (!string.IsNullOrWhiteSpace(policy.Key) &&
            !string.IsNullOrWhiteSpace(policy.ValueName))
            yield return (policy.Key, policy.ValueName);

        foreach (var element in policy.Elements)
            if (!string.IsNullOrWhiteSpace(element.Key) &&
                !string.IsNullOrWhiteSpace(element.ValueName))
                yield return (element.Key, element.ValueName);
    }

    private static string RegistryIdentity(string scope, string key, string value) =>
        scope.Trim() + "|" + NormalizeRegistryKey(scope, key) + "|" + value.Trim();

    private static string NormalizeRegistryKey(string scope, string key)
    {
        var normalized = (key ?? string.Empty).Trim()
            .Replace('/', '\\').Trim('\\');
        // Relative ADMX registry targets inherit the policy scope's hive.
        // An explicitly declared *wrong* hive must never be stripped and
        // accidentally matched to the current scope.
        var hive = scope.Equals("User", StringComparison.OrdinalIgnoreCase)
            ? "HKCU" : "HKLM";
        foreach (var (prefix, specifiedHive) in new[]
        {
            ("HKEY_LOCAL_MACHINE\\", "HKLM"),
            ("HKLM\\", "HKLM"),
            ("MACHINE\\", "HKLM"),
            ("HKEY_CURRENT_USER\\", "HKCU"),
            ("HKCU\\", "HKCU"),
            ("USER\\", "HKCU")
        })
        {
            if (!normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                continue;
            normalized = normalized[prefix.Length..];
            hive = specifiedHive;
            break;
        }
        return hive + "\\" + normalized.TrimEnd('\\');
    }

    private static MmcInventoryEntry? FindObserved(
        PolicySettingInfo setting,
        IReadOnlyDictionary<string, MmcInventoryEntry[]> lookup)
    {
        var key = $"{setting.GpoId:B}|{Named(setting.Scope, setting.SettingName)}";
        if (!lookup.TryGetValue(key, out var group))
            return null;
        var matched = group.Where(m =>
            CategoriesAgree(m.SectionPath, setting.Category)).ToArray();
        return matched.Length == 1 ? matched[0] : null;
    }

    public static bool CategoriesAgree(string a, string b)
    {
        static string Normalize(string s) => string.Join(" > ",
            (s ?? "").Replace("\\", ">", StringComparison.Ordinal)
                .Replace("/", ">", StringComparison.Ordinal)
                .Split('>', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));

        var left = Normalize(a);
        var right = Normalize(b);
        return left.Length > 0 && right.Length > 0 &&
            (left.Equals(right, StringComparison.OrdinalIgnoreCase) ||
             left.EndsWith(" > " + right, StringComparison.OrdinalIgnoreCase) ||
             right.EndsWith(" > " + left, StringComparison.OrdinalIgnoreCase));
    }

    private static bool KeyEqual(string a, string b) =>
        a.Trim().TrimEnd('\\')
            .Equals(b.Trim().TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);

    private static string DefinitionId(AdmxPolicyDefinition p, string scope) =>
        $"{scope}|{p.AdmxFile}|{p.Name}|{p.Category}";

    private static string Named(string scope, string name) =>
        scope.Trim() + "|" + name.Trim();

    private static IEnumerable<string> Scopes(string scope) =>
        scope.Equals("Both", StringComparison.OrdinalIgnoreCase)
            ? new[] { "Computer", "User" }
            : new[] { scope.Equals("Machine", StringComparison.OrdinalIgnoreCase)
                ? "Computer" : scope };

    private static string JoinTarget(string key, string value) =>
        string.IsNullOrWhiteSpace(key) ? "" :
            key + (string.IsNullOrWhiteSpace(value) ? "" : " / " + value);
}

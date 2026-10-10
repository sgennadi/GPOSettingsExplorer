using GPOSettingsExplorer.Models;

namespace GPOSettingsExplorer.Services;

/// <summary>
/// Deterministic, UI-independent join between configured GPMC rows, ADMX
/// definitions and a *single* optionally observed MMC editor session.
/// Matching is conservative and ambiguity is never silently resolved.
/// </summary>
public static class UnifiedSettingsCatalogService
{
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

        var lookup = new Dictionary<string, List<AdmxPolicyDefinition>>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var policy in definitions)
        {
            foreach (var scope in Scopes(policy.Scope))
            {
                var key = Named(scope, policy.DisplayName);
                if (!lookup.TryGetValue(key, out var list))
                    lookup[key] = list = new List<AdmxPolicyDefinition>();
                list.Add(policy);
            }
        }

        var observationsLookup = observations
            .GroupBy(m => $"{m.GpoId:B}|{Named(m.Scope, m.SettingName)}",
                StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToArray(),
                StringComparer.OrdinalIgnoreCase);

        var usedDefinitions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var usedMmc = new HashSet<MmcInventoryEntry>();
        var rows = new List<UnifiedSettingInfo>(
            source.Length + definitions.Count + observations.Length);

        foreach (var setting in source)
        {
            var technical = SecurityXmlEntryClassifier.IsTechnicalDetail(setting);
            // Low-level GPMC XML leaf entries must never match an ADMX
            // editor by their generic label ('Registry', 'Member', etc.).
            var admx = technical ? null : FindDefinition(setting, lookup);
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
                RegistryTarget = JoinTarget(setting.RegistryKey, setting.RegistryValue),
                Explanation = technical
                    ? "This is a nested GPMC XML description, not an independent policy row. " +
                      "Its full original value remains available in View details. " +
                      "MMC can open only a related section; exact editing is not verified."
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
                Category = observed.SectionPath
            };
            var admx = FindDefinition(synthetic, lookup);
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

        foreach (var admx in definitions)
        {
            foreach (var scope in Scopes(admx.Scope))
            {
                if (usedDefinitions.Contains(DefinitionId(admx, scope)))
                    continue;

                rows.Add(new UnifiedSettingInfo
                {
                    SettingName = admx.DisplayName,
                    Scope = scope,
                    Category = admx.Category,
                    State = "Template - state unknown",
                    Value = "",
                    Sources = "ADMX definition",
                    Capability = IsDirectAdmxSupported(admx)
                        ? "Configure in GPO (guarded)"
                        : "Native GPMC / review",
                    RegistryTarget = JoinTarget(admx.Key, admx.ValueName),
                    Explanation = "Available ADMX template, not a configured setting. " +
                        "Choose a target GPO before reading its actual current state or configuring. " +
                        "Nothing is presumed Not Configured.",
                    Kind = "ADMX template",
                    Admx = admx
                });
            }
        }

        var ordered = rows.OrderBy(row => row.Kind switch
            {
                "Configured" => 0,
                "Stored source" => 1,
                "MMC observed" => 2,
                "ADMX template" => 3,
                _ => 4
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
            SourceFileEntries = stored.Length
        };
    }

    public static AdmxPolicyDefinition? ResolveDefinition(
        PolicySettingInfo setting,
        IReadOnlyList<AdmxPolicyDefinition> policies)
    {
        // A setting opened from Global Search or the advanced GPMC grid must
        // use the same conservative identity rules as Unified Settings.
        // In particular, a shared display name is NOT sufficient.
        var scoped = policies
            .Where(policy => Scopes(policy.Scope).Any(scope =>
                scope.Equals(setting.Scope, StringComparison.OrdinalIgnoreCase)))
            .Where(policy => policy.DisplayName.Equals(
                setting.SettingName, StringComparison.CurrentCultureIgnoreCase))
            .ToArray();

        if (scoped.Length == 0)
            return null;

        var sameCategory = scoped.Where(policy =>
            CategoriesAgree(setting.Category, policy.Category)).ToArray();
        if (sameCategory.Length == 1)
            return sameCategory[0];

        if (string.IsNullOrWhiteSpace(setting.RegistryKey) ||
            string.IsNullOrWhiteSpace(setting.RegistryValue))
            return null;

        var sameRegistry = scoped.Where(policy =>
            (KeyEqual(setting.RegistryKey, policy.Key) &&
             setting.RegistryValue.Equals(policy.ValueName,
                 StringComparison.OrdinalIgnoreCase)) ||
            policy.Elements.Any(element =>
                KeyEqual(setting.RegistryKey, element.Key) &&
                setting.RegistryValue.Equals(element.ValueName,
                    StringComparison.OrdinalIgnoreCase)))
            .ToArray();
        return sameRegistry.Length == 1 ? sameRegistry[0] : null;
    }

    public static bool IsDirectAdmxSupported(AdmxPolicyDefinition definition) =>
        definition.Elements.All(e => e.Type != AdmxElementType.Unknown);

    private static AdmxPolicyDefinition? FindDefinition(
        PolicySettingInfo setting,
        IReadOnlyDictionary<string, List<AdmxPolicyDefinition>> lookup)
    {
        if (!lookup.TryGetValue(Named(setting.Scope, setting.SettingName), out var group))
            return null;

        var byCategory = group.Where(p =>
            CategoriesAgree(setting.Category, p.Category)).ToArray();

        if (byCategory.Length == 1)
            return byCategory[0];

        // Two different ADMXs may share a display label. Registry targets
        // can resolve only an *exact*, unique same-scope target, not a guess.
        if (string.IsNullOrWhiteSpace(setting.RegistryKey) ||
            string.IsNullOrWhiteSpace(setting.RegistryValue))
            return null;

        var registry = group.Where(p =>
            (KeyEqual(setting.RegistryKey, p.Key) &&
             setting.RegistryValue.Equals(p.ValueName, StringComparison.OrdinalIgnoreCase)) ||
            p.Elements.Any(e =>
                KeyEqual(setting.RegistryKey, e.Key) &&
                setting.RegistryValue.Equals(e.ValueName, StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        return registry.Length == 1 ? registry[0] : null;
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

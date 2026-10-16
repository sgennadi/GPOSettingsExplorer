using GPOSettingsExplorer.Models;

namespace GPOSettingsExplorer.Services;

/// <summary>
/// Read-only display hints for GPMC-originating setting sources.
/// A source path is NOT evidence that a file exists or a policy applies.
/// Never copy PolicySettingInfo.Value into a purported registry path.
/// </summary>
public static class GpoSourcePathPresentationService
{
    public sealed record Hint(string Family, string GpoRelativeFile,
        string ReportedTarget, string Limitations)
    {
        public string SearchText =>
            Family + " " + GpoRelativeFile + " " + ReportedTarget;
    }

    public static Hint Describe(PolicySettingInfo setting)
    {
        ArgumentNullException.ThrowIfNull(setting);
        var folder = setting.Scope.Equals("User", StringComparison.OrdinalIgnoreCase)
            ? "User" : "Machine";

        if (GpoSettingSourceProvenance.IsGppRegistrySource(setting))
            return new Hint("GPP Registry preferences",
                folder + @"\Preferences\Registry\Registry.xml",
                GpoSettingSourceProvenance.DisplayRegistryTarget(setting),
                "Reported GPMC key/action only. NOT Registry.pol. " +
                "Raw value data, targeting result and client registry are NOT verified.");

        if (setting.Extension.Equals("GppRegistry", StringComparison.OrdinalIgnoreCase))
            return new Hint("GPP Registry preferences (incomplete report)",
                folder + @"\Preferences\Registry\Registry.xml", "(unverified item)",
                "Incomplete GPMC Properties cannot establish a registry target.");

        if (GpoAdvancedAuditStoredSourceEvidenceService.IsSupported(setting))
            return new Hint("Advanced Audit Policy",
                @"Machine\Microsoft\Windows NT\Audit\audit.csv",
                "(System subcategory GUID; check stored source to validate)",
                "GPMC subcategory metadata is not audit.csv content or client auditpol/RSoP.");

        if (KerberosPolicyMetadataService.IsKnownKerberosSetting(setting) ||
            GpoKnownAccountPolicyService.TryDescribe(setting, out _))
            return new Hint("Security account policy",
                @"Machine\Microsoft\Windows NT\SecEdit\GptTmpl.inf",
                "(exact System Access/Kerberos Policy key checked separately)",
                "Security source is not Registry.pol. Match requires an explicit pinned-DC check.");

        if (SoftwareRestrictionMmcRouteService.IsPathRule(setting))
            return new Hint("Software Restriction Path Rule",
                folder + @"\Registry.pol",
                @"(Safer\CodeIdentifiers\...\Paths\{GUID}\ItemData; not verified)",
                "The saved path and security level require an exact typed source check.");

        if (setting.Extension.Equals("RegistrySettings", StringComparison.OrdinalIgnoreCase) ||
            setting.Extension.Equals("Administrative Templates", StringComparison.OrdinalIgnoreCase))
            return new Hint("Administrative Templates registry policy",
                folder + @"\Registry.pol",
                GpoSettingSourceProvenance.IsVerifiedRegistryTarget(setting)
                    ? GpoSettingSourceProvenance.RegistryTarget(setting)
                    : "(GPMC did not supply a registry target)",
                "GPMC registry metadata is NOT proof of an exact Registry.pol source value. " +
                "ADMX definition and native MMC row must be checked separately.");

        if (setting.Extension.Equals("SoftwareInstallationSettings",
                StringComparison.OrdinalIgnoreCase))
            return new Hint("Software Installation package",
                "(GPMC package/extension metadata; not Registry.pol)",
                "(no registry target)",
                "Applicability is nested package metadata, not a standalone registry setting.");

        return new Hint(GpoSettingSourceProvenance.SourceType(setting),
            "(unmapped source file)", "(no verified registry target)",
            "Inspect the GPMC report or native GPO editor manually; never infer a " +
            "registry key or an editable native MMC row from generic XML.");
    }

    /// <summary>
    /// Unique ADMX *declaration* for a known Administrative Templates policy.
    /// It may contain several elements/registry targets. This is neither an
    /// observed stored Registry.pol record nor proof that MMC has the row.
    /// Ambiguous policy names/categories return no declaration at all.
    /// </summary>
    public static string DescribeAdmxDeclaration(
        PolicySettingInfo setting, IReadOnlyList<AdmxPolicyDefinition>? catalog)
    {
        if (catalog is null || catalog.Count == 0 ||
            !(setting.Extension.Equals("RegistrySettings", StringComparison.OrdinalIgnoreCase) ||
              setting.Extension.Equals("Administrative Templates", StringComparison.OrdinalIgnoreCase)))
            return string.Empty;

        var policy = UnifiedSettingsCatalogService.ResolveDefinition(setting, catalog);
        if (policy is null)
            return string.Empty;

        var hive = setting.Scope.Equals("Computer", StringComparison.OrdinalIgnoreCase)
            ? "HKLM" : "HKCU";
        var targets = new List<string>();

        void Add(string key, string valueName)
        {
            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(valueName))
                return;
            var normalized = key.Trim().TrimStart('\\');
            if (normalized.StartsWith("HKEY_LOCAL_MACHINE\\", StringComparison.OrdinalIgnoreCase) ||
                normalized.StartsWith("HKLM\\", StringComparison.OrdinalIgnoreCase) ||
                normalized.StartsWith("HKEY_CURRENT_USER\\", StringComparison.OrdinalIgnoreCase) ||
                normalized.StartsWith("HKCU\\", StringComparison.OrdinalIgnoreCase))
                targets.Add(normalized + " / " + valueName.Trim());
            else
                targets.Add(hive + "\\" + normalized + " / " + valueName.Trim());
        }

        Add(policy.Key, policy.ValueName);
        foreach (var element in policy.Elements)
            Add(string.IsNullOrWhiteSpace(element.Key) ? policy.Key : element.Key,
                element.ValueName);

        var unique = targets.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (unique.Length == 0)
            return string.Empty;

        var name = Path.GetFileName(policy.AdmxFile);
        var source = string.IsNullOrWhiteSpace(name) ? "ADMX" : name;
        return "ADMX declaration (" + source + ", NOT stored-source verified): " +
            string.Join(" | ", unique.Take(6)) +
            (unique.Length > 6 ? " (+" + (unique.Length - 6) + " other targets)" : "");
    }

    /// <summary>
    /// Copy explicit operator-requested source LOCATION metadata only.
    /// Names and registry keys may still be confidential, so do not export
    /// or upload clipboard contents automatically. Raw policy values omitted.
    /// </summary>
    public static string CopyLocationText(
        PolicySettingInfo setting, string admxDeclaration = "")
    {
        var hint = Describe(setting);
        return "GPO: " + setting.GpoName + Environment.NewLine +
            "Scope: " + setting.Scope + Environment.NewLine +
            "Setting: " + setting.SettingName + Environment.NewLine +
            "GPMC source family: " + hint.Family + Environment.NewLine +
            "GPO-relative source: " + hint.GpoRelativeFile + Environment.NewLine +
            "GPMC-reported target: " + hint.ReportedTarget + Environment.NewLine +
            "ADMX reference: " + (string.IsNullOrWhiteSpace(admxDeclaration)
                ? "(not uniquely resolved)" : admxDeclaration) + Environment.NewLine +
            "Limitations: " + hint.Limitations + Environment.NewLine +
            "Native MMC value and effective client RSoP: NOT VERIFIED.";
    }
}

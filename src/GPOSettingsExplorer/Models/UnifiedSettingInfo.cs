using GPOSettingsExplorer.Models;

namespace GPOSettingsExplorer.Models;

/// <summary>
/// One row in the unified search index. Never converts a missing GPMC entry
/// into a Not Configured assertion. Template rows have no bound target GPO.
/// </summary>
public sealed record UnifiedSettingInfo
{
    public Guid? GpoId { get; init; }
    public string GpoName { get; init; } = "ADMX template - choose a GPO";
    public string SettingName { get; init; } = string.Empty;
    public string Scope { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public string State { get; init; } = "Unknown";
    public string Value { get; init; } = string.Empty;
    public string Sources { get; init; } = string.Empty;
    public string Capability { get; init; } = "View";
    public string RegistryTarget { get; init; } = string.Empty;
    public string Explanation { get; init; } = string.Empty;
    public string Kind { get; init; } = string.Empty;
    public bool IsTechnicalDetail { get; init; }
    public PolicySettingInfo? Configured { get; init; }
    public AdmxPolicyDefinition? Admx { get; init; }
    // Specific input definition from the universal ADMX schema; not a value
    // read from a particular GPO. Used to focus an exact field on demand.
    public AdmxElementDefinition? AdmxElement { get; init; }
    public MmcInventoryEntry? Mmc { get; init; }
    public RealSettingRecord? StoredSource { get; init; }

    public string SearchText =>
        string.Join(" ", new[] {
            GpoName, SettingName, Scope, Category, State, Value, Sources,
            Capability, RegistryTarget, Explanation, Admx?.AdmxFile ?? "",
            Admx?.Name ?? "", Admx?.DisplayName ?? "",
            AdmxElement?.Id ?? "", AdmxElement?.Key ?? "",
            AdmxElement?.ValueName ?? ""
        });
}

public sealed record UnifiedCatalogResult(
    IReadOnlyList<UnifiedSettingInfo> Rows,
    int ConfiguredCount,
    int TemplateCount,
    int MmcOnlyCount,
    bool AdmxLoaded,
    string Coverage)
{
    public int SourceFileEntries { get; init; }
    public int AdmxFieldCount { get; init; }

    public string Summary =>
        $"{Rows.Count:N0} total | {ConfiguredCount:N0} configured GPMC | " +
        $"{TemplateCount:N0} ADMX templates | {AdmxFieldCount:N0} ADMX fields | " +
        $"{MmcOnlyCount:N0} MMC-only | " +
        $"{SourceFileEntries:N0} source-file records | " + Coverage;
}

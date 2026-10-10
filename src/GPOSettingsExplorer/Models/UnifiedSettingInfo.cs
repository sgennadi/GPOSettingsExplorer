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
    public MmcInventoryEntry? Mmc { get; init; }
    public NativePolicyEvidence? Native { get; init; }

    public string SearchText =>
        string.Join(" ", new[] {
            GpoName, SettingName, Scope, Category, State, Value, Sources,
            Capability, RegistryTarget, Explanation, Admx?.AdmxFile ?? "",
            Native?.SourceLocation ?? "", Native?.DataType ?? ""
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
    public int NativeCount { get; init; }
    public string Summary =>
        $"{Rows.Count:N0} total | {ConfiguredCount:N0} configured GPMC | " +
        $"{TemplateCount:N0} ADMX templates | {MmcOnlyCount:N0} MMC-only | " +
        $"{NativeCount:N0} GPT-only | " + Coverage;
}

namespace GPOSettingsExplorer.Models;

public sealed class GppRegistryItemInfo
{
    public Guid GpoId { get; init; }
    public string GpoName { get; init; } = string.Empty;
    public string DomainName { get; init; } = string.Empty;
    public string Scope { get; init; } = "Computer";
    public string XmlPath { get; init; } = string.Empty;
    public string Uid { get; set; } = string.Empty;
    public int Ordinal { get; init; }
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Action { get; set; } = "U";
    public string Hive { get; set; } = "HKEY_LOCAL_MACHINE";
    public string Key { get; set; } = string.Empty;
    public string ValueName { get; set; } = string.Empty;
    public string ValueType { get; set; } = "REG_SZ";
    public string ValueData { get; set; } = string.Empty;
    public bool DefaultValue { get; set; }
    public bool DisplayDecimal { get; set; }
    public bool Disabled { get; set; }
    public bool BypassErrors { get; set; }
    public bool RemoveWhenNoLongerApplied { get; set; }
    public bool RunInUserContext { get; set; }
    public string FiltersXml { get; set; } = string.Empty;

    public bool HasFilters => !string.IsNullOrWhiteSpace(FiltersXml);

    public string ActionDisplay => Action.ToUpperInvariant() switch
    {
        "C" => "Create",
        "D" => "Delete",
        "R" => "Replace",
        _ => "Update"
    };

    public string Target =>
        string.IsNullOrWhiteSpace(ValueName)
            ? $"{Hive}\\{Key}"
            : $"{Hive}\\{Key}\\{ValueName}";

    public string SearchText =>
        $"{GpoName} {Scope} {ActionDisplay} {Hive} {Key} {ValueName} {ValueType} {ValueData} {Description}";

    public GppRegistryItemInfo Clone() => new()
    {
        GpoId = GpoId,
        GpoName = GpoName,
        DomainName = DomainName,
        Scope = Scope,
        XmlPath = XmlPath,
        Uid = Uid,
        Ordinal = Ordinal,
        DisplayName = DisplayName,
        Description = Description,
        Action = Action,
        Hive = Hive,
        Key = Key,
        ValueName = ValueName,
        ValueType = ValueType,
        ValueData = ValueData,
        DefaultValue = DefaultValue,
        DisplayDecimal = DisplayDecimal,
        Disabled = Disabled,
        BypassErrors = BypassErrors,
        RemoveWhenNoLongerApplied = RemoveWhenNoLongerApplied,
        RunInUserContext = RunInUserContext,
        FiltersXml = FiltersXml
    };
}

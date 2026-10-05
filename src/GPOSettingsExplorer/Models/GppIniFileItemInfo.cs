namespace GPOSettingsExplorer.Models;

public sealed class GppIniFileItemInfo
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
    public string Path { get; set; } = string.Empty;
    public string Section { get; set; } = string.Empty;
    public string Property { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;

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

    public string DeleteTargetDisplay
    {
        get
        {
            if (!Action.Equals("D", StringComparison.OrdinalIgnoreCase))
                return string.Empty;

            if (string.IsNullOrWhiteSpace(Section))
                return "File";

            if (string.IsNullOrWhiteSpace(Property))
                return "Section";

            return "Property";
        }
    }

    public string SearchText =>
        $"{GpoName} {Scope} {DisplayName} {Description} {ActionDisplay} " +
        $"{Path} {Section} {Property} {Value} {DeleteTargetDisplay}";
}

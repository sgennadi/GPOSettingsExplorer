namespace GPOSettingsExplorer.Models;

public sealed class GppEnvironmentVariableInfo
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
    public string Name { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public bool UserVariable { get; set; }
    public bool PartialPath { get; set; }

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

    public string VariableTypeDisplay =>
        UserVariable ? "User" : "System";

    public string SearchText =>
        $"{GpoName} {Scope} {DisplayName} {Description} {ActionDisplay} " +
        $"{Name} {Value} {VariableTypeDisplay}";
}

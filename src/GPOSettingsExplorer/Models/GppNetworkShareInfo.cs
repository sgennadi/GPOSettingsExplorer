namespace GPOSettingsExplorer.Models;

public sealed class GppNetworkShareInfo
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
    public string ShareName { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string Comment { get; set; } = string.Empty;

    public bool AllRegular { get; set; }
    public bool AllHidden { get; set; }
    public bool AllAdminDrive { get; set; }

    public string LimitUsersMode { get; set; } = "NO_CHANGE";
    public string UserLimit { get; set; } = string.Empty;
    public string AbeMode { get; set; } = "NO_CHANGE";

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

    public string LimitUsersDisplay => LimitUsersMode.ToUpperInvariant() switch
    {
        "SET_LIMIT" => string.IsNullOrWhiteSpace(UserLimit)
            ? "Limited"
            : $"Limit {UserLimit}",
        "MAX_ALLOWED" => "Maximum allowed",
        _ => "No change"
    };

    public string AbeDisplay => AbeMode.ToUpperInvariant() switch
    {
        "ENABLE" => "Enabled",
        "DISABLE" => "Disabled",
        _ => "No change"
    };

    public string SearchText =>
        $"{GpoName} {DisplayName} {Description} {ActionDisplay} {ShareName} " +
        $"{Path} {Comment} {LimitUsersDisplay} {AbeDisplay}";
}

namespace GPOSettingsExplorer.Models;

public sealed class GppFileItemInfo
{
    public Guid GpoId { get; init; }
    public string GpoName { get; init; } = string.Empty;
    public string DomainName { get; init; } = string.Empty;
    public string Scope { get; init; } = "Computer";
    public string XmlPath { get; init; } = string.Empty;
    public string Uid { get; set; } = string.Empty;
    public int Ordinal { get; init; }

    public string DisplayName { get; set; } = string.Empty;
    public string Action { get; set; } = "U";
    public string FromPath { get; set; } = string.Empty;
    public string TargetPath { get; set; } = string.Empty;
    public bool SuppressErrors { get; set; }
    public bool ReadOnly { get; set; }
    public bool Archive { get; set; }
    public bool Hidden { get; set; }

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

    public string SearchText =>
        $"{GpoName} {Scope} {DisplayName} {ActionDisplay} {FromPath} {TargetPath}";
}

public sealed class GppFolderItemInfo
{
    public Guid GpoId { get; init; }
    public string GpoName { get; init; } = string.Empty;
    public string DomainName { get; init; } = string.Empty;
    public string Scope { get; init; } = "Computer";
    public string XmlPath { get; init; } = string.Empty;
    public string Uid { get; set; } = string.Empty;
    public int Ordinal { get; init; }

    public string DisplayName { get; set; } = string.Empty;
    public string Action { get; set; } = "U";
    public string Path { get; set; } = string.Empty;
    public bool ReadOnly { get; set; }
    public bool Archive { get; set; }
    public bool Hidden { get; set; }

    public bool DeleteIgnoreErrors { get; set; }
    public bool DeleteReadOnly { get; set; }
    public bool DeleteFiles { get; set; }
    public bool DeleteSubFolders { get; set; }
    public bool DeleteFolder { get; set; }

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

    public string SearchText =>
        $"{GpoName} {Scope} {DisplayName} {ActionDisplay} {Path}";
}

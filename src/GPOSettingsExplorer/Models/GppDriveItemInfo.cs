namespace GPOSettingsExplorer.Models;

public sealed class GppDriveItemInfo
{
    public Guid GpoId { get; init; }
    public string GpoName { get; init; } = string.Empty;
    public string DomainName { get; init; } = string.Empty;
    public string XmlPath { get; init; } = string.Empty;
    public string Uid { get; set; } = string.Empty;
    public int Ordinal { get; init; }

    public string DisplayName { get; set; } = string.Empty;
    public string Action { get; set; } = "U";
    public string Letter { get; set; } = "S";
    public bool UseExactLetter { get; set; } = true;
    public string Path { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public bool Persistent { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string ThisDriveVisibility { get; set; } = "NOCHANGE";
    public string AllDrivesVisibility { get; set; } = "NOCHANGE";

    public bool Disabled { get; set; }
    public bool BypassErrors { get; set; }
    public bool RemoveWhenNoLongerApplied { get; set; }
    public bool RunInUserContext { get; set; }
    public string FiltersXml { get; set; } = string.Empty;

    // GPP cpassword is legacy encrypted data. It is preserved opaquely but never displayed.
    public string OpaqueCredential { get; set; } = string.Empty;
    public bool ClearStoredCredential { get; set; }

    public bool HasFilters => !string.IsNullOrWhiteSpace(FiltersXml);
    public bool HasStoredCredential => !string.IsNullOrWhiteSpace(OpaqueCredential);

    public string ActionDisplay => Action.ToUpperInvariant() switch
    {
        "C" => "Create",
        "D" => "Delete",
        "R" => "Replace",
        _ => "Update"
    };

    public string DriveDisplay =>
        UseExactLetter ? $"{Letter.ToUpperInvariant()}:" : $"{Letter.ToUpperInvariant()}: through Z:";

    public string SearchText =>
        $"{GpoName} {ActionDisplay} {DriveDisplay} {Path} {Label} {UserName} " +
        $"{ThisDriveVisibility} {AllDrivesVisibility}";
}

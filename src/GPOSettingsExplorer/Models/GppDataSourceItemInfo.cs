using System.Collections.ObjectModel;

namespace GPOSettingsExplorer.Models;

public sealed class GppDataSourceAttributeInfo
{
    public string Name { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}

public sealed class GppDataSourceItemInfo
{
    public Guid GpoId { get; init; }
    public string GpoName { get; init; } = string.Empty;
    public string DomainName { get; init; } = string.Empty;
    public string Scope { get; set; } = "Computer";
    public string XmlPath { get; init; } = string.Empty;
    public string Uid { get; set; } = string.Empty;
    public int Ordinal { get; init; }

    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Action { get; set; } = "U";
    public bool UserDsn { get; set; }
    public string Dsn { get; set; } = string.Empty;
    public string Driver { get; set; } = string.Empty;
    public string DsnDescription { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;

    public ObservableCollection<GppDataSourceAttributeInfo> Attributes { get; set; } = new();

    public bool Disabled { get; set; }
    public bool BypassErrors { get; set; }
    public bool RemoveWhenNoLongerApplied { get; set; }
    public bool RunInUserContext { get; set; }
    public string FiltersXml { get; set; } = string.Empty;

    public string OpaqueCredential { get; set; } = string.Empty;
    public bool ClearStoredCredential { get; set; }

    public bool HasStoredCredential => !string.IsNullOrWhiteSpace(OpaqueCredential);
    public bool HasFilters => !string.IsNullOrWhiteSpace(FiltersXml);

    public string ActionDisplay =>
        Action.ToUpperInvariant() switch
        {
            "C" => "Create",
            "D" => "Delete",
            "R" => "Replace",
            _ => "Update"
        };

    public string DsnTypeDisplay => UserDsn ? "User DSN" : "System DSN";

    public string AttributesPreview =>
        string.Join("; ", Attributes
            .Where(item => !string.IsNullOrWhiteSpace(item.Name))
            .Select(item => $"{item.Name}={item.Value}")
            .Take(12));

    public string SearchText =>
        $"{GpoName} {Scope} {DisplayName} {ActionDisplay} {DsnTypeDisplay} " +
        $"{Dsn} {Driver} {DsnDescription} {UserName} {AttributesPreview}";
}

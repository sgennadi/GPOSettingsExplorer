namespace GPOSettingsExplorer.Models;

public sealed class GppFileAssociationItemInfo
{
    public Guid GpoId { get; init; }
    public string GpoName { get; init; } = string.Empty;
    public string DomainName { get; init; } = string.Empty;
    public string Scope { get; set; } = "User";
    public string XmlPath { get; init; } = string.Empty;
    public string Uid { get; set; } = string.Empty;
    public int Ordinal { get; init; }

    public string ItemKind { get; set; } = "OpenWith";
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Action { get; set; } = "U";

    public string FileExtension { get; set; } = string.Empty;
    public string ApplicationPath { get; set; } = string.Empty;
    public bool DefaultApplication { get; set; }

    public string Application { get; set; } = string.Empty;
    public string ApplicationProgId { get; set; } = string.Empty;
    public bool ConfigureActions { get; set; }

    public bool Disabled { get; set; }
    public bool BypassErrors { get; set; }
    public bool RemoveWhenNoLongerApplied { get; set; }
    public bool RunInUserContext { get; set; } = true;
    public string FiltersXml { get; set; } = string.Empty;

    public bool HasFilters => !string.IsNullOrWhiteSpace(FiltersXml);

    public bool IsOpenWith =>
        ItemKind.Equals("OpenWith", StringComparison.OrdinalIgnoreCase);

    public bool IsFileType =>
        ItemKind.Equals("FileType", StringComparison.OrdinalIgnoreCase);

    public string KindDisplay =>
        IsOpenWith ? "Open With" :
        IsFileType ? "File Type" :
        ItemKind;

    public string ActionDisplay =>
        Action.ToUpperInvariant() switch
        {
            "C" => "Create",
            "D" => "Delete",
            "R" => "Replace",
            _ => "Update"
        };

    public string TargetDisplay =>
        IsOpenWith
            ? ApplicationPath
            : FirstNonEmpty(Application, ApplicationProgId);

    public string SearchText =>
        $"{GpoName} {Scope} {KindDisplay} {DisplayName} {ActionDisplay} " +
        $"{FileExtension} {ApplicationPath} {Application} {ApplicationProgId}";

    private static string FirstNonEmpty(params string[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))
        ?? string.Empty;
}

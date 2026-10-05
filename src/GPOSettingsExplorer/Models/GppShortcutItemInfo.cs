namespace GPOSettingsExplorer.Models;

public sealed class GppShortcutItemInfo
{
    public Guid GpoId { get; init; }
    public string GpoName { get; init; } = string.Empty;
    public string DomainName { get; init; } = string.Empty;
    public string Scope { get; init; } = "User";
    public string XmlPath { get; init; } = string.Empty;
    public string Uid { get; set; } = string.Empty;
    public int Ordinal { get; init; }

    public string DisplayName { get; set; } = string.Empty;
    public string Action { get; set; } = "U";
    public string ShortcutPath { get; set; } = string.Empty;
    public string TargetType { get; set; } = "FILESYSTEM";
    public string TargetPath { get; set; } = string.Empty;
    public string Arguments { get; set; } = string.Empty;
    public string StartIn { get; set; } = string.Empty;
    public string ShortcutKey { get; set; } = "0";
    public string Window { get; set; } = string.Empty;
    public string Comment { get; set; } = string.Empty;
    public string IconPath { get; set; } = string.Empty;
    public string IconIndex { get; set; } = "0";
    public string Pidl { get; set; } = string.Empty;

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

    public string TargetTypeDisplay => TargetType.ToUpperInvariant() switch
    {
        "URL" => "URL",
        "SHELL" => "Shell object",
        _ => "File system"
    };

    public string SearchText =>
        $"{GpoName} {Scope} {DisplayName} {ActionDisplay} {ShortcutPath} " +
        $"{TargetType} {TargetPath} {Arguments} {StartIn} {Comment} {IconPath}";
}

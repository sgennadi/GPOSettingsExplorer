namespace GPOSettingsExplorer.Models;

public sealed class GppFolderOptionsItemInfo
{
    public Guid GpoId { get; init; }
    public string GpoName { get; init; } = string.Empty;
    public string DomainName { get; init; } = string.Empty;
    public string Scope { get; set; } = "User";
    public string XmlPath { get; init; } = string.Empty;
    public string Uid { get; set; } = string.Empty;
    public int Ordinal { get; init; }

    public string ItemKind { get; set; } = "GlobalFolderOptionsVista";
    public string DisplayName { get; set; } = "Folder Options (Windows Vista and later)";
    public string Description { get; set; } = string.Empty;

    public bool ShowDriveLetter { get; set; } = true;
    public bool ShowPreviewHandlers { get; set; } = true;
    public bool UseCheckBoxes { get; set; }
    public bool UseSharingWizard { get; set; } = true;
    public bool AlwaysShowIcons { get; set; }
    public bool AlwaysShowMenus { get; set; }
    public string HiddenFiles { get; set; } = "HIDE";
    public bool DisplayIconThumb { get; set; } = true;
    public bool DisplayFileSize { get; set; } = true;
    public bool HideFileExtensions { get; set; } = true;
    public bool DisplaySimpleFolders { get; set; } = true;
    public string ListViewTyping { get; set; } = "SELECT";
    public bool SeparateProcess { get; set; }
    public bool ShowSuperHidden { get; set; }
    public bool ClassicViewState { get; set; }
    public bool PersistBrowsers { get; set; }
    public bool ShowCompressedColor { get; set; } = true;
    public bool ShowInfoTips { get; set; } = true;
    public bool FullPath { get; set; }

    public bool Disabled { get; set; }
    public bool BypassErrors { get; set; }
    public bool RemoveWhenNoLongerApplied { get; set; }
    public bool RunInUserContext { get; set; } = true;
    public string FiltersXml { get; set; } = string.Empty;

    public bool HasFilters => !string.IsNullOrWhiteSpace(FiltersXml);
    public bool SupportsStructuredEditing =>
        ItemKind.Equals("GlobalFolderOptionsVista", StringComparison.OrdinalIgnoreCase);

    public string KindDisplay => ItemKind switch
    {
        "GlobalFolderOptionsVista" => "Folder Options (Vista+)",
        "GlobalFolderOptions" => "Folder Options (XP legacy)",
        "OpenWith" => "Open With",
        "FileType" => "File Type",
        _ => ItemKind
    };

    public string HiddenFilesDisplay =>
        HiddenFiles.Equals("SHOW", StringComparison.OrdinalIgnoreCase)
            ? "Show hidden files"
            : "Hide hidden files";

    public string SearchText =>
        $"{GpoName} {KindDisplay} {DisplayName} {HiddenFilesDisplay} " +
        $"HideExtensions={HideFileExtensions} ShowSuperHidden={ShowSuperHidden} " +
        $"CheckBoxes={UseCheckBoxes} SharingWizard={UseSharingWizard}";
}

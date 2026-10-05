namespace GPOSettingsExplorer.Models;

public sealed class GppServiceItemInfo
{
    public Guid GpoId { get; init; }
    public string GpoName { get; init; } = string.Empty;
    public string DomainName { get; init; } = string.Empty;
    public string XmlPath { get; init; } = string.Empty;
    public string Uid { get; set; } = string.Empty;
    public int Ordinal { get; init; }

    public string DisplayName { get; set; } = string.Empty;
    public string ServiceName { get; set; } = string.Empty;
    public string ServiceAction { get; set; } = "NOCHANGE";
    public string StartupType { get; set; } = "NOCHANGE";
    public string Timeout { get; set; } = "30";
    public string AccountName { get; set; } = string.Empty;
    public bool InteractWithDesktop { get; set; }

    public string FirstFailure { get; set; } = "NOACTION";
    public string SecondFailure { get; set; } = "NOACTION";
    public string ThirdFailure { get; set; } = "NOACTION";
    public string ResetFailCountDelay { get; set; } = "0";
    public string RestartServiceDelay { get; set; } = "0";
    public string RestartComputerDelay { get; set; } = "0";
    public string RestartMessage { get; set; } = string.Empty;
    public string Program { get; set; } = string.Empty;
    public string Arguments { get; set; } = string.Empty;
    public string AppendArguments { get; set; } = string.Empty;

    public bool Disabled { get; set; }
    public bool BypassErrors { get; set; }
    public bool RemoveWhenNoLongerApplied { get; set; }
    public bool RunInUserContext { get; set; }
    public string FiltersXml { get; set; } = string.Empty;

    public string OpaqueCredential { get; set; } = string.Empty;
    public bool ClearStoredCredential { get; set; }

    public bool HasStoredCredential => !string.IsNullOrWhiteSpace(OpaqueCredential);
    public bool HasFilters => !string.IsNullOrWhiteSpace(FiltersXml);

    public string SearchText =>
        $"{GpoName} {DisplayName} {ServiceName} {ServiceAction} {StartupType} " +
        $"{AccountName} {FirstFailure} {SecondFailure} {ThirdFailure} {Program}";
}

namespace GPOSettingsExplorer.Models;

public sealed class GppScheduledTaskInfo
{
    public Guid GpoId { get; init; }
    public string GpoName { get; init; } = string.Empty;
    public string DomainName { get; init; } = string.Empty;
    public string Scope { get; init; } = "Computer";
    public string XmlPath { get; init; } = string.Empty;
    public string Uid { get; set; } = string.Empty;
    public int Ordinal { get; init; }

    public string ItemKind { get; set; } = "TaskV2";
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Action { get; set; } = "U";
    public string TaskName { get; set; } = string.Empty;
    public string RunAs { get; set; } = "SYSTEM";
    public string LogonType { get; set; } = "ServiceAccount";
    public string OpaqueCredential { get; set; } = string.Empty;
    public bool ClearStoredCredential { get; set; }

    public string Author { get; set; } = string.Empty;
    public string TaskDescription { get; set; } = string.Empty;
    public string RunLevel { get; set; } = "HighestAvailable";
    public bool TaskEnabled { get; set; } = true;
    public bool Hidden { get; set; }
    public bool WakeToRun { get; set; }
    public bool StartWhenAvailable { get; set; }
    public bool AllowStartOnDemand { get; set; } = true;
    public bool DisallowStartIfOnBatteries { get; set; }
    public bool StopIfGoingOnBatteries { get; set; }

    public string Command { get; set; } = string.Empty;
    public string Arguments { get; set; } = string.Empty;
    public string WorkingDirectory { get; set; } = string.Empty;

    public string TaskXml { get; set; } = string.Empty;
    public string TriggerSummary { get; set; } = string.Empty;

    public bool Disabled { get; set; }
    public bool BypassErrors { get; set; }
    public bool RemoveWhenNoLongerApplied { get; set; }
    public bool RunInUserContext { get; set; }
    public string FiltersXml { get; set; } = string.Empty;

    public bool HasFilters => !string.IsNullOrWhiteSpace(FiltersXml);
    public bool HasStoredCredential => !string.IsNullOrWhiteSpace(OpaqueCredential);

    public bool IsV2 =>
        ItemKind.Equals("TaskV2", StringComparison.OrdinalIgnoreCase) ||
        ItemKind.Equals("ImmediateTaskV2", StringComparison.OrdinalIgnoreCase);

    public bool IsImmediate =>
        ItemKind.StartsWith("Immediate", StringComparison.OrdinalIgnoreCase);

    public string ActionDisplay => Action.ToUpperInvariant() switch
    {
        "C" => "Create",
        "D" => "Delete",
        "R" => "Replace",
        _ => "Update"
    };

    public string KindDisplay => ItemKind switch
    {
        "ImmediateTaskV2" => "Immediate Task (Windows 7+)",
        "TaskV2" => "Scheduled Task (Windows 7+)",
        "ImmediateTask" => "Immediate Task (legacy)",
        _ => "Scheduled Task (legacy)"
    };

    public string SearchText =>
        $"{GpoName} {Scope} {KindDisplay} {DisplayName} {TaskName} " +
        $"{RunAs} {LogonType} {Command} {Arguments} {WorkingDirectory} " +
        $"{TaskDescription} {TriggerSummary}";
}

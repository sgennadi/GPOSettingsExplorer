namespace GPOSettingsExplorer.Models;

public sealed class GppScheduledTaskItemInfo
{
    public Guid GpoId { get; init; }
    public string GpoName { get; init; } = string.Empty;
    public string DomainName { get; init; } = string.Empty;
    public string Scope { get; set; } = "Computer";
    public string XmlPath { get; init; } = string.Empty;
    public string Uid { get; set; } = string.Empty;
    public int Ordinal { get; init; }

    public string TaskKind { get; set; } = "TaskV2";
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Action { get; set; } = "U";

    public string RunAs { get; set; } = string.Empty;
    public string LogonType { get; set; } = "InteractiveToken";
    public string RunLevel { get; set; } = "LeastPrivilege";
    public string Author { get; set; } = string.Empty;

    public string Command { get; set; } = string.Empty;
    public string Arguments { get; set; } = string.Empty;
    public string WorkingDirectory { get; set; } = string.Empty;

    public string TriggerType { get; set; } = "Daily";
    public string StartBoundary { get; set; } = string.Empty;
    public string DaysInterval { get; set; } = "1";
    public string WeeksInterval { get; set; } = "1";
    public string DaysOfWeek { get; set; } = "Monday";
    public string TriggerDelay { get; set; } = string.Empty;

    public bool TaskEnabled { get; set; } = true;
    public bool Hidden { get; set; }
    public bool StartWhenAvailable { get; set; }
    public bool RunOnlyIfNetworkAvailable { get; set; }
    public bool DisallowStartIfOnBatteries { get; set; } = true;
    public bool StopIfGoingOnBatteries { get; set; } = true;
    public bool WakeToRun { get; set; }
    public bool AllowStartOnDemand { get; set; } = true;
    public string MultipleInstancesPolicy { get; set; } = "IgnoreNew";
    public string ExecutionTimeLimit { get; set; } = "PT0S";
    public string Priority { get; set; } = "7";

    public bool Disabled { get; set; }
    public bool BypassErrors { get; set; }
    public bool RemoveWhenNoLongerApplied { get; set; }
    public bool RunInUserContext { get; set; }
    public string FiltersXml { get; set; } = string.Empty;

    public string OpaqueCredential { get; set; } = string.Empty;
    public bool ClearStoredCredential { get; set; }

    public string TaskXml { get; set; } = string.Empty;

    public bool IsImmediate =>
        TaskKind.Equals("ImmediateTaskV2", StringComparison.OrdinalIgnoreCase) ||
        TaskKind.Equals("ImmediateTask", StringComparison.OrdinalIgnoreCase);

    public bool IsLegacy =>
        TaskKind.Equals("Task", StringComparison.OrdinalIgnoreCase) ||
        TaskKind.Equals("ImmediateTask", StringComparison.OrdinalIgnoreCase);

    public bool SupportsStructuredEditing => !IsLegacy;

    public bool HasStoredCredential =>
        !string.IsNullOrWhiteSpace(OpaqueCredential);

    public bool HasFilters =>
        !string.IsNullOrWhiteSpace(FiltersXml);

    public string ActionDisplay =>
        Action.ToUpperInvariant() switch
        {
            "C" => "Create",
            "D" => "Delete",
            "R" => "Replace",
            _ => "Update"
        };

    public string KindDisplay =>
        TaskKind switch
        {
            "ImmediateTaskV2" => "Immediate Task (Windows 7+)",
            "TaskV2" => "Scheduled Task (Windows 7+)",
            "ImmediateTask" => "Immediate Task (legacy)",
            "Task" => "Scheduled Task (legacy)",
            _ => TaskKind
        };

    public string TriggerDisplay =>
        IsImmediate
            ? "Immediate"
            : TriggerType switch
            {
                "Daily" => $"Daily / {DaysInterval}",
                "Weekly" => $"Weekly / {WeeksInterval}: {DaysOfWeek}",
                "Once" => "Once",
                "AtStartup" => "At startup",
                "AtLogon" => "At logon",
                "Advanced" => "Advanced / other",
                _ => TriggerType
            };

    public string SearchText =>
        $"{GpoName} {Scope} {KindDisplay} {DisplayName} {ActionDisplay} " +
        $"{RunAs} {Command} {Arguments} {TriggerDisplay} {Description} {Author}";
}

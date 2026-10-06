namespace GPOSettingsExplorer.Models;

public sealed class GppPowerOptionItemInfo
{
    public Guid GpoId { get; init; }
    public string GpoName { get; init; } = string.Empty;
    public string DomainName { get; init; } = string.Empty;
    public string Scope { get; set; } = "Computer";
    public string XmlPath { get; init; } = string.Empty;
    public string Uid { get; set; } = string.Empty;
    public int Ordinal { get; init; }

    public string ItemKind { get; set; } = "GlobalPowerOptionsV2";
    public string DisplayName { get; set; } = "Power Plan";
    public string Description { get; set; } = string.Empty;
    public string Action { get; set; } = "U";
    public string PlanGuid { get; set; } = string.Empty;
    public bool SetAsDefault { get; set; }

    public string RequireWakePasswordAc { get; set; } = "NO";
    public string RequireWakePasswordDc { get; set; } = "NO";
    public string TurnOffHardDiskAc { get; set; } = "0";
    public string TurnOffHardDiskDc { get; set; } = "0";
    public string SleepAfterAc { get; set; } = "0";
    public string SleepAfterDc { get; set; } = "0";
    public string AllowHybridSleepAc { get; set; } = "OFF";
    public string AllowHybridSleepDc { get; set; } = "OFF";
    public string HibernateAfterAc { get; set; } = "0";
    public string HibernateAfterDc { get; set; } = "0";
    public string LidCloseAc { get; set; } = "DO_NOTHING";
    public string LidCloseDc { get; set; } = "DO_NOTHING";
    public string PowerButtonAc { get; set; } = "DO_NOTHING";
    public string PowerButtonDc { get; set; } = "DO_NOTHING";
    public string StartMenuPowerAc { get; set; } = "DO_NOTHING";
    public string StartMenuPowerDc { get; set; } = "DO_NOTHING";
    public string LinkPowerManagementAc { get; set; } = "OFF";
    public string LinkPowerManagementDc { get; set; } = "OFF";
    public string ProcessorMinAc { get; set; } = "5";
    public string ProcessorMinDc { get; set; } = "5";
    public string ProcessorMaxAc { get; set; } = "100";
    public string ProcessorMaxDc { get; set; } = "100";
    public string DisplayOffAc { get; set; } = "0";
    public string DisplayOffDc { get; set; } = "0";
    public string AdaptiveDisplayAc { get; set; } = "OFF";
    public string AdaptiveDisplayDc { get; set; } = "OFF";
    public string CriticalBatteryActionAc { get; set; } = "DO_NOTHING";
    public string CriticalBatteryActionDc { get; set; } = "HIBERNATE";
    public string LowBatteryLevelAc { get; set; } = "10";
    public string LowBatteryLevelDc { get; set; } = "10";
    public string CriticalBatteryLevelAc { get; set; } = "5";
    public string CriticalBatteryLevelDc { get; set; } = "5";
    public string LowBatteryNotificationAc { get; set; } = "OFF";
    public string LowBatteryNotificationDc { get; set; } = "ON";
    public string LowBatteryActionAc { get; set; } = "DO_NOTHING";
    public string LowBatteryActionDc { get; set; } = "DO_NOTHING";

    public bool Disabled { get; set; }
    public bool BypassErrors { get; set; }
    public bool RemoveWhenNoLongerApplied { get; set; }
    public bool RunInUserContext { get; set; }
    public string FiltersXml { get; set; } = string.Empty;

    public bool HasFilters => !string.IsNullOrWhiteSpace(FiltersXml);

    public bool SupportsStructuredEditing =>
        ItemKind.Equals("GlobalPowerOptionsV2", StringComparison.OrdinalIgnoreCase);

    public string KindDisplay =>
        ItemKind switch
        {
            "GlobalPowerOptionsV2" => "Power Plan (Vista+)",
            "GlobalPowerOptions" => "Global Power Options (legacy)",
            "PowerScheme" => "Power Scheme (legacy)",
            _ => ItemKind
        };

    public string ActionDisplay =>
        Action.ToUpperInvariant() switch
        {
            "C" => "Create",
            "D" => "Delete",
            "R" => "Replace",
            _ => "Update"
        };

    public string SearchText =>
        $"{GpoName} {Scope} {KindDisplay} {DisplayName} {ActionDisplay} " +
        $"{PlanGuid} {SleepAfterAc} {SleepAfterDc} {DisplayOffAc} {DisplayOffDc} " +
        $"{LidCloseAc} {LidCloseDc} {PowerButtonAc} {PowerButtonDc}";
}

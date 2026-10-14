using GPOSettingsExplorer.Models;

namespace GPOSettingsExplorer.Services;

/// <summary>
/// Exact known GPMC Group Policy Preferences extension-to-folder routes.
/// Every route is a SECTION HINT, not a verified native item editor.
/// Only observed same-GPO/scope native list rows may be promoted to a
/// unique exact route by GpoExactMmcRouteResolver.
/// </summary>
public static class GpoPreferencesMmcSections
{
    private sealed record Route(string Group, string Folder, bool UserOnly = false,
        bool ComputerOnly = false);

    private static readonly IReadOnlyDictionary<string, Route> Known =
        new Dictionary<string, Route>(StringComparer.OrdinalIgnoreCase)
        {
            ["DriveMapSettings"] = new("Windows Settings", "Drive Maps", UserOnly: true),
            ["DriveMapsSettings"] = new("Windows Settings", "Drive Maps", UserOnly: true),
            ["EnvironmentVariablesSettings"] = new("Windows Settings", "Environment"),
            ["EnvironmentSettings"] = new("Windows Settings", "Environment"),
            ["FilesSettings"] = new("Windows Settings", "Files"),
            ["FoldersSettings"] = new("Windows Settings", "Folders"),
            ["IniFilesSettings"] = new("Windows Settings", "Ini Files"),
            ["ShortcutsSettings"] = new("Windows Settings", "Shortcuts"),
            ["NetworkSharesSettings"] = new("Windows Settings", "Network Shares",
                ComputerOnly: true),
            ["RegistryPreferencesSettings"] = new("Windows Settings", "Registry"),
            ["GppRegistry"] = new("Windows Settings", "Registry"),
            ["PreferencesRegistrySettings"] = new("Windows Settings", "Registry"),
            ["ScheduledTasksSettings"] = new("Control Panel Settings", "Scheduled Tasks"),
            ["PrintersSettings"] = new("Control Panel Settings", "Printers"),
            ["LocalUsersAndGroupsSettings"] = new("Control Panel Settings",
                "Local Users and Groups"),
            ["LocalUsersGroupsSettings"] = new("Control Panel Settings",
                "Local Users and Groups"),
            ["ServicesSettings"] = new("Control Panel Settings", "Services",
                ComputerOnly: true),
            ["DataSourcesSettings"] = new("Control Panel Settings", "Data Sources"),
            ["PowerOptionsSettings"] = new("Control Panel Settings", "Power Options"),
            ["FolderOptionsSettings"] = new("Control Panel Settings", "Folder Options"),
            ["RegionalOptionsSettings"] = new("Control Panel Settings", "Regional Options"),
            ["InternetSettings"] = new("Control Panel Settings", "Internet Settings"),
            ["NetworkOptionsSettings"] = new("Control Panel Settings", "Network Options"),
            ["DevicesSettings"] = new("Control Panel Settings", "Devices"),
            ["StartMenuSettings"] = new("Windows Settings", "Start Menu"),
        };

    public static IReadOnlyList<string> Resolve(PolicySettingInfo setting)
    {
        ArgumentNullException.ThrowIfNull(setting);
        if (!Known.TryGetValue(setting.Extension.Trim(), out var route))
            return Array.Empty<string>();

        var user = setting.Scope.Equals("User", StringComparison.OrdinalIgnoreCase);
        var computer = setting.Scope.Equals("Computer", StringComparison.OrdinalIgnoreCase);
        if ((!user && !computer) || (route.UserOnly && !user) ||
            (route.ComputerOnly && !computer))
            return Array.Empty<string>();

        return new[]
        {
            user ? "User Configuration" : "Computer Configuration",
            "Preferences", route.Group, route.Folder
        };
    }
}

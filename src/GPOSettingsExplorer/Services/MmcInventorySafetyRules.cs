namespace GPOSettingsExplorer.Services;

/// <summary>
/// MMC has snap-ins that display property pages instead of stable, enumerable
/// policy lists. UI Automation selecting these nodes can initialize fragile
/// COM extensions, so explicitly exclude them from inventory automation.
/// These nodes remain visible in the coverage report and their GPO-backed
/// data is available through the corresponding dedicated application view.
/// </summary>
public static class MmcInventorySafetyRules
{
    public static bool ShouldSkipNode(
        IReadOnlyList<string> segments,
        out string explanation)
    {
        explanation = string.Empty;
        if (segments.Count == 0)
            return false;

        var name = segments[^1].Trim();
        var inWindowsSettings = segments.Any(segment =>
            segment.Equals("Windows Settings", StringComparison.OrdinalIgnoreCase));
        var inPolicies = segments.Any(segment =>
            segment.Equals("Policies", StringComparison.OrdinalIgnoreCase));
        var inGpoScope = segments.Any(segment =>
            segment.StartsWith("Computer Configuration", StringComparison.OrdinalIgnoreCase) ||
            segment.StartsWith("User Configuration", StringComparison.OrdinalIgnoreCase));

        var isScriptsSnapin =
            name.Equals("Scripts", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("Scripts (Startup/Shutdown", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("Scripts (Logon/Logoff", StringComparison.OrdinalIgnoreCase);

        if (inWindowsSettings && inPolicies && inGpoScope && isScriptsSnapin)
        {
            explanation = "Script snap-in excluded from automatic MMC navigation: " +
                "selecting this node can activate an unstable Scripts (Startup/Shutdown " +
                "or Logon/Logoff) MMC extension. Use the dedicated GPO Scripts " +
                "inventory/editor for startup, shutdown, logon and logoff scripts.";
            return true;
        }

        return false;
    }
}

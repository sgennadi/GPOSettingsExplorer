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

        // An MMC snap-in error dialog was observed on a domain controller
        // while inventory traversal reached this exact section. The snap-in
        // that threw cannot be proved from a screenshot alone: conservatively
        // exclude this node from automated Expand/Select until a separate
        // native MMC investigation establishes it is safe. The whole
        // Security Settings parent is NOT falsely marked as verified.
        var isUserRights = name.Equals(
            "User Rights Assignment", StringComparison.OrdinalIgnoreCase);
        var inSecuritySettings = segments.Any(segment =>
            segment.Equals("Security Settings", StringComparison.OrdinalIgnoreCase));
        var inLocalPolicies = segments.Any(segment =>
            segment.Equals("Local Policies", StringComparison.OrdinalIgnoreCase));
        if (inGpoScope && inPolicies && inWindowsSettings &&
            inSecuritySettings && inLocalPolicies && isUserRights)
        {
            explanation = "User Rights Assignment is excluded from unattended MMC " +
                "inventory after a snap-in error was observed during DC scanning " +
                "near this section. Root cause is not yet established. " +
                "Use GPMC/security-template source and a manually operated editor " +
                "for diagnosis. No automatic row or RSoP verification is claimed.";
            return true;
        }

        return false;
    }
}

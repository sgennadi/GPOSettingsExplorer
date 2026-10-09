using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Automation;
using GPOSettingsExplorer.Models;

namespace GPOSettingsExplorer.Services;

/// <summary>
/// Read-only best-effort walk of one *actual* Group Policy Management Editor.
/// Uses UI Automation for the left tree and the shared native MMC ListView
/// reader for its right pane. A scan is never proof that a setting is supported
/// or configured, and every skipped node is recorded as incomplete.
/// </summary>
public static class MmcFullSettingsInventoryService
{
    public const int MaxTreeNodes = 4000;
    public const int MaxRowsPerSection = 12000;
    public const int MaxObservedRows = 100000;
    public const int MaxTreeDepth = 18;
    public static readonly TimeSpan MaxDuration = TimeSpan.FromMinutes(10);

    public static Task<MmcInventoryScanResult> ScanAsync(
        GpoInfo gpo,
        string domainDn,
        IReadOnlyList<PolicySettingInfo> configured,
        IReadOnlyList<AdmxPolicyDefinition>? catalog,
        IProgress<string>? progress = null,
        CancellationToken token = default) =>
        Task.Run(() => Run(gpo, domainDn, configured, catalog, progress, token), token);

    private static MmcInventoryScanResult Run(
        GpoInfo gpo,
        string domainDn,
        IReadOnlyList<PolicySettingInfo> configured,
        IReadOnlyList<AdmxPolicyDefinition>? catalog,
        IProgress<string>? progress,
        CancellationToken token)
    {
        var observed = new List<MmcInventoryEntry>();
        var sections = new List<MmcInventorySection>();
        var started = DateTime.UtcNow;
        var deadline = started.Add(MaxDuration);
        var visited = 0;
        var reason = "Finished reachable MMC tree.";
        var interrupted = false;

        void Deadline()
        {
            token.ThrowIfCancellationRequested();
            if (DateTime.UtcNow >= deadline)
                throw new TimeoutException("MMC inventory reached its 10-minute limit.");
            if (visited >= MaxTreeNodes)
                throw new InvalidOperationException(
                    $"MMC inventory reached its {MaxTreeNodes:N0}-node traversal limit.");
            if (observed.Count >= MaxObservedRows)
                throw new InvalidOperationException(
                    $"MMC inventory reached its {MaxObservedRows:N0}-row capture limit.");
        }

        var system = Environment.GetFolderPath(Environment.SpecialFolder.System);
        var ldap = DomainConnectionState.BuildLdapPath(
            $"CN={gpo.Id:B},CN=Policies,CN=System,{domainDn}");

        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = Path.Combine(system, "mmc.exe"),
            Arguments = $"gpme.msc /gpobject:\"{ldap}\"",
            WorkingDirectory = system,
            UseShellExecute = true
        }) ?? throw new InvalidOperationException("Failed to start MMC.");

        progress?.Report("Opening a dedicated MMC editor for read-only inventory...");
        AutomationElement? root = null;
        var startDeadline = DateTime.UtcNow.AddSeconds(55);
        while (root is null && DateTime.UtcNow < startDeadline)
        {
            token.ThrowIfCancellationRequested();
            process.Refresh();
            if (process.HasExited)
                throw new InvalidOperationException("MMC closed before the policy tree appeared.");

            if (process.MainWindowHandle != IntPtr.Zero)
            {
                try
                {
                    var window = AutomationElement.FromHandle(process.MainWindowHandle);
                    root = window.FindFirst(TreeScope.Descendants,
                        new PropertyCondition(AutomationElement.ControlTypeProperty,
                            ControlType.Tree));
                }
                catch (COMException) { }
                catch (ElementNotAvailableException) { }
            }

            if (root is null)
                Thread.Sleep(250);
        }
        if (root is null)
            throw new TimeoutException("MMC policy tree could not be read within 55 seconds.");

        var scopeCondition = new PropertyCondition(
            AutomationElement.ControlTypeProperty, ControlType.TreeItem);

        IReadOnlyList<AutomationElement> Children(AutomationElement parent)
        {
            try
            {
                return parent.FindAll(TreeScope.Children, scopeCondition)
                    .Cast<AutomationElement>().ToArray();
            }
            catch (Exception ex) when (ex is COMException or ElementNotAvailableException
                                       or InvalidOperationException)
            {
                throw new InvalidOperationException(
                    "The MMC tree changed or a section could not be enumerated: " +
                    ex.Message, ex);
            }
        }

        static void Expand(AutomationElement item)
        {
            if (!item.TryGetCurrentPattern(ExpandCollapsePattern.Pattern, out var raw) ||
                raw is not ExpandCollapsePattern pattern)
                return;
            if (pattern.Current.ExpandCollapseState == ExpandCollapseState.Collapsed)
                pattern.Expand();
        }

        static bool Select(AutomationElement item)
        {
            try
            {
                if (!item.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var raw) ||
                    raw is not SelectionItemPattern pattern)
                    return false;
                pattern.Select();
                return pattern.Current.IsSelected;
            }
            catch (Exception ex) when (ex is COMException or ElementNotAvailableException
                                       or InvalidOperationException)
            {
                return false;
            }
        }

        void Visit(AutomationElement node, string[] ancestors)
        {
            Deadline();

            string name;
            try { name = (node.Current.Name ?? "").Trim(); }
            catch (Exception ex) when (ex is COMException or ElementNotAvailableException)
            {
                sections.Add(new MmcInventorySection(
                    string.Join(" > ", ancestors), "Read error", 0,
                    "An MMC tree node is no longer available."));
                return;
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                sections.Add(new MmcInventorySection(
                    string.Join(" > ", ancestors), "Read error", 0,
                    "MMC returned an unnamed tree item."));
                return;
            }

            visited++;
            var parts = ancestors.Append(name).ToArray();
            var sectionPath = string.Join(" > ", parts);
            progress?.Report($"MMC inventory: {visited:N0} nodes / {observed.Count:N0} rows - {sectionPath}");

            try { Expand(node); }
            catch (Exception ex) when (ex is COMException or ElementNotAvailableException
                                       or InvalidOperationException)
            {
                sections.Add(new MmcInventorySection(sectionPath,
                    "Read error", 0, "Could not expand the MMC section: " + ex.Message));
                return;
            }

            // MMC commonly populates descendants only after expanding a node.
            var children = Children(node);
            if (children.Count == 0)
            {
                Thread.Sleep(90);
                children = Children(node);
            }
            if (children.Count > 0)
            {
                if (parts.Length >= MaxTreeDepth)
                {
                    sections.Add(new MmcInventorySection(sectionPath, "Truncated", 0,
                        $"Tree depth limit of {MaxTreeDepth} reached while child nodes remain."));
                    return;
                }
                foreach (var child in children)
                    Visit(child, parts);
                return;
            }

            if (!Select(node))
            {
                sections.Add(new MmcInventorySection(sectionPath, "Selection failed", 0,
                    "MMC did not expose a verifiable selection for this leaf."));
                return;
            }

            // Do not invoke/double-click a setting: the scan is read-only.
            Thread.Sleep(185);
            var snapshot = GpoEditorNavigatorService.ReadInventoryList(
                process, MaxRowsPerSection, token);

            if (!snapshot.Complete)
            {
                sections.Add(new MmcInventorySection(sectionPath,
                    "Read error", snapshot.Rows.Count, snapshot.Error));
                return;
            }

            if (!snapshot.HasList)
            {
                sections.Add(new MmcInventorySection(sectionPath, "No list", 0,
                    "No native SysListView32. MMC may use a custom/virtualized view."));
                return;
            }
            if (snapshot.Rows.Count == 0)
            {
                sections.Add(new MmcInventorySection(sectionPath, "Empty list", 0,
                    "The selected MMC section displayed no native rows."));
                return;
            }

            var scopeIndex = Array.FindIndex(parts, item =>
                item.Contains("Computer Configuration", StringComparison.OrdinalIgnoreCase) ||
                item.Contains("User Configuration", StringComparison.OrdinalIgnoreCase));
            var scope = scopeIndex < 0 ? "Unknown" :
                parts[scopeIndex].Contains("User Configuration", StringComparison.OrdinalIgnoreCase)
                    ? "User" : "Computer";
            var editorPath = scopeIndex >= 0 ? parts.Skip(scopeIndex).ToArray() : parts;
            var directNames = snapshot.Rows.GroupBy(row => row.Name,
                StringComparer.CurrentCultureIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Count(),
                    StringComparer.CurrentCultureIgnoreCase);

            foreach (var row in snapshot.Rows)
            {
                Deadline();
                var navigation = scopeIndex >= 0 && directNames[row.Name] == 1
                    ? "Exact MMC row candidate"
                    : "Section only / ambiguous";
                var entry = new MmcInventoryEntry
                {
                    GpoId = gpo.Id,
                    GpoName = gpo.DisplayName,
                    Scope = scope,
                    SectionPath = sectionPath,
                    TreeSegments = editorPath,
                    SettingName = row.Name,
                    MmcState = ParseState(row.Value),
                    MmcValue = string.Join(" | ", new[] { row.Value, row.Additional }
                        .Where(value => value.Length > 0)),
                    Source = "MMC native list",
                    Navigation = navigation
                };
                observed.Add(MmcInventoryReconciliation.Reconcile(
                    entry, configured, catalog));
            }

            sections.Add(new MmcInventorySection(sectionPath,
                "Rows read", snapshot.Rows.Count));
        }

        try
        {
            foreach (var node in Children(root))
                Visit(node, Array.Empty<string>());
        }
        catch (OperationCanceledException)
        {
            interrupted = true;
            reason = "Canceled by user; captured records are retained.";
        }
        catch (Exception ex) when (ex is TimeoutException or InvalidOperationException
                                   or COMException or ElementNotAvailableException)
        {
            interrupted = true;
            reason = ex.Message;
        }

        if (sections.Any(s => s.Status is "Read error" or "Selection failed" or "Truncated"))
            reason += " Some sections were inaccessible or incomplete.";

        progress?.Report($"MMC scan: {observed.Count:N0} rows, {visited:N0} nodes; " +
                         (interrupted ? "PARTIAL: " + reason : "walk finished"));

        // Do not terminate the MMC editor: it remains available for verification.
        // A policy row was NEVER invoked by the inventory walk.
        return new MmcInventoryScanResult(
            observed, sections, visited, interrupted, reason, DateTimeOffset.Now);
    }

    public static string ParseState(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Equals("Not Configured", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("Not configured", StringComparison.OrdinalIgnoreCase))
            return "Not Configured (MMC)";
        if (trimmed.Equals("Enabled", StringComparison.OrdinalIgnoreCase))
            return "Enabled (MMC)";
        if (trimmed.Equals("Disabled", StringComparison.OrdinalIgnoreCase))
            return "Disabled (MMC)";
        return "Not reported (see MMC value)";
    }
}

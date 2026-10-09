using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Automation;
using GPOSettingsExplorer.Models;

namespace GPOSettingsExplorer.Services;

/// <summary>
/// Inspects MMC's tree without invoking policy dialogs or changing GPO data.
/// Only one GPM editor is started per scan, with explicit bounded retries.
/// </summary>
public static class MmcRouteAuditService
{
    public static Task<string> RunAsync(
        GpoInfo gpo,
        string domainDn,
        IReadOnlyList<PolicySettingInfo> settings,
        IProgress<string>? progress = null,
        CancellationToken token = default) =>
        Task.Run(() => Run(gpo, domainDn, settings, progress, token), token);

    private static string Run(
        GpoInfo gpo, string domainDn,
        IReadOnlyList<PolicySettingInfo> settings,
        IProgress<string>? progress, CancellationToken token)
    {
        var report = new StringBuilder();
        report.AppendLine("GPO SETTINGS EXPLORER - MMC ROUTE AUDIT (READ ONLY)");
        report.AppendLine($"Generated UTC: {DateTimeOffset.UtcNow:O}");
        report.AppendLine($"GPO: {gpo.DisplayName} ({gpo.Id:B})");
        report.AppendLine($"Domain: {gpo.DomainName}");
        report.AppendLine("Mode: tree inspection only; no properties dialogs and no SYSVOL/AD writes.");
        report.AppendLine("The MMC process remains open for the operator after the scan.");
        report.AppendLine();

        var targets = settings
            .Where(s => s.GpoId == gpo.Id)
            .Select(s => new
            {
                s.Extension, s.SettingName,
                Path = GpoEditorNavigatorService.NavigationTarget(s)
            })
            .Where(s => !string.IsNullOrWhiteSpace(s.Path))
            .GroupBy(s => s.Path, StringComparer.OrdinalIgnoreCase)
            .Select(g => new { Path = g.Key, Count = g.Count(),
                Extensions = string.Join(", ", g.Select(x => x.Extension)
                    .Distinct(StringComparer.OrdinalIgnoreCase)) })
            .OrderBy(g => g.Path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var deadline = DateTime.UtcNow.AddMinutes(3);
        var system = Environment.GetFolderPath(Environment.SpecialFolder.System);
        var path = Path.Combine(system, "mmc.exe");
        var ldap = DomainConnectionState.BuildLdapPath(
            $"CN={gpo.Id:B},CN=Policies,CN=System,{domainDn}");
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = path,
            Arguments = $"gpme.msc /gpobject:\"{ldap}\"",
            WorkingDirectory = system,
            UseShellExecute = true
        }) ?? throw new InvalidOperationException("MMC could not be started.");

        progress?.Report($"MMC started (PID {process.Id}). Waiting for policy tree...");
        AutomationElement? root = null;
        while (DateTime.UtcNow < deadline && root is null)
        {
            token.ThrowIfCancellationRequested();
            process.Refresh();
            if (process.HasExited)
                throw new InvalidOperationException("MMC closed before the audit began.");

            if (process.MainWindowHandle != IntPtr.Zero)
            {
                try
                {
                    var window = AutomationElement.FromHandle(process.MainWindowHandle);
                    root = window?.FindFirst(TreeScope.Descendants,
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
            throw new TimeoutException("MMC policy tree did not appear within the audit deadline.");

        report.AppendLine("### Observed MMC tree (maximum 220 nodes, depth 7)");
        var budget = 220;
        SnapshotTree(root, report, 0, 7, ref budget, token);
        report.AppendLine();
        report.AppendLine($"### Declared report paths ({targets.Length})");

        var found = 0;
        var missing = 0;
        var errors = 0;
        foreach (var target in targets)
        {
            token.ThrowIfCancellationRequested();
            if (DateTime.UtcNow > deadline)
            {
                report.AppendLine("TIME LIMIT - remaining declared paths were not checked.");
                break;
            }

            progress?.Report($"Checking {found + missing + errors + 1}/{targets.Length}: {target.Path}");
            var segments = target.Path.Split(" > ", StringSplitOptions.RemoveEmptyEntries);
            var node = root;
            var status = "FOUND";
            var detail = "";
            for (var i = 0; i < segments.Length; i++)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    // The root of the MMC policy tree has computer/user branches;
                    // every deeper level must be an immediate child.
                    var child = i == 0
                        ? FindFirstBranch(node, segments[i])
                        : FindImmediateChild(node, segments[i]);
                    if (child is null)
                    {
                        // MMC can populate children asynchronously after Expand().
                        Expand(node);
                        for (var retry = 0; retry < 8 && child is null; retry++)
                        {
                            Thread.Sleep(140);
                            child = i == 0
                                ? FindFirstBranch(node, segments[i])
                                : FindImmediateChild(node, segments[i]);
                        }
                    }

                    if (child is null)
                    {
                        status = "MISSING";
                        detail = $"Stopped at segment {i + 1}/{segments.Length}: {segments[i]}. " +
                            $"Visible children: {DescribeChildren(node)}";
                        break;
                    }

                    node = child;
                    if (i + 1 < segments.Length)
                        Expand(node);
                }
                catch (Exception ex) when (
                    ex is COMException or ElementNotAvailableException or InvalidOperationException)
                {
                    status = "ERROR";
                    detail = ex.GetType().Name + ": " + ex.Message;
                    break;
                }
            }

            report.AppendLine($"[{status}] {target.Path}");
            report.AppendLine($"    Report entries: {target.Count}; extension(s): {target.Extensions}");
            if (!string.IsNullOrWhiteSpace(detail))
                report.AppendLine("    " + detail);
            switch (status)
            {
                case "FOUND": found++; break;
                case "MISSING": missing++; break;
                default: errors++; break;
            }
        }

        report.AppendLine();
        report.AppendLine($"SUMMARY: found={found}; missing={missing}; errors={errors}; total={targets.Length}");
        report.AppendLine("FOUND confirms only a matching MMC tree path. It does not prove exact setting-dialog navigation.");
        var directory = StoragePaths.Audit;
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory,
            "MmcRouteAudit-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt");
        File.WriteAllText(file, report.ToString(), new UTF8Encoding(false));
        progress?.Report($"Saved MMC route audit: {file}");
        return file;
    }

    private static AutomationElement? FindFirstBranch(AutomationElement tree, string segment)
    {
        // MMC tree can start with an additional "GPO editor" root node.
        // Only the first requested policy segment may use descendants.
        var matches = tree.FindAll(TreeScope.Descendants,
            new AndCondition(
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.TreeItem),
                new PropertyCondition(AutomationElement.NameProperty, segment)));
        return matches.Count == 1 ? matches[0] : null;
    }

    private static AutomationElement? FindImmediateChild(AutomationElement parent, string segment)
    {
        var condition = new PropertyCondition(
            AutomationElement.ControlTypeProperty, ControlType.TreeItem);
        var children = parent.FindAll(TreeScope.Children, condition);
        foreach (AutomationElement child in children)
        {
            var actual = child.Current.Name?.Trim() ?? "";
            if (actual.Equals(segment, StringComparison.CurrentCultureIgnoreCase) ||
                actual.StartsWith(segment + " (", StringComparison.CurrentCultureIgnoreCase))
                return child;
        }
        return null;
    }

    private static void Expand(AutomationElement node)
    {
        if (!node.TryGetCurrentPattern(ExpandCollapsePattern.Pattern, out var pattern) ||
            pattern is not ExpandCollapsePattern expand ||
            expand.Current.ExpandCollapseState != ExpandCollapseState.Collapsed)
            return;
        expand.Expand();
    }

    private static string DescribeChildren(AutomationElement node)
    {
        try
        {
            var children = node.FindAll(TreeScope.Children,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.TreeItem));
            return string.Join(" | ", children.Cast<AutomationElement>()
                .Select(e => e.Current.Name).Where(s => !string.IsNullOrEmpty(s)).Take(20));
        }
        catch (COMException) { return "<UI Automation unavailable>"; }
        catch (ElementNotAvailableException) { return "<MMC changed the tree>"; }
    }

    private static void SnapshotTree(
        AutomationElement parent, StringBuilder report, int depth,
        int maxDepth, ref int remaining, CancellationToken token)
    {
        if (remaining <= 0 || depth > maxDepth)
            return;

        AutomationElementCollection nodes;
        try
        {
            nodes = parent.FindAll(TreeScope.Children,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.TreeItem));
        }
        catch (COMException) { return; }
        catch (ElementNotAvailableException) { return; }

        foreach (AutomationElement node in nodes)
        {
            token.ThrowIfCancellationRequested();
            if (--remaining <= 0)
                break;
            try
            {
                report.AppendLine(new string(' ', depth * 2) + "- " + node.Current.Name);
                if (depth < maxDepth)
                {
                    Expand(node);
                    SnapshotTree(node, report, depth + 1, maxDepth, ref remaining, token);
                }
            }
            catch (COMException) { }
            catch (ElementNotAvailableException) { }
            catch (InvalidOperationException) { }
        }
    }
}

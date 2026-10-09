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
        report.AppendLine($"Reference MMC GPO: {gpo.DisplayName} ({gpo.Id:B})");
        report.AppendLine($"GPOs represented in settings index: {settings.Select(x => x.GpoId).Distinct().Count()}");
        report.AppendLine("One MMC editor is used as a representative policy tree.");
        report.AppendLine("Individual GPOs can expose different policy nodes or installed CSEs.");
        report.AppendLine($"Domain: {gpo.DomainName}");
        report.AppendLine("Mode: tree inspection only; no properties dialogs and no SYSVOL/AD writes.");
        report.AppendLine("The MMC process remains open for the operator after the scan.");
        report.AppendLine();

        var targets = settings
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

        // Validate requested index routes BEFORE walking a potentially huge
        // ADMX tree. The old 220-node snapshot could exhaust the deadline
        // before representative paths were verified.
        report.AppendLine($"### All indexed MMC section paths ({targets.Length})");
        report.AppendLine("MMC routes are verified first, followed by a bounded tree snapshot. " +
            "Any unverified or ambiguous paths remain explicit.");

        var found = 0;
        var missing = 0;
        var errors = 0;
        var skipped = 0;
        foreach (var target in targets)
        {
            token.ThrowIfCancellationRequested();
            if (DateTime.UtcNow > deadline)
            {
                report.AppendLine("TIME LIMIT - remaining declared paths were not checked.");
                break;
            }

            progress?.Report($"Checking {found + missing + errors + skipped + 1}/{targets.Length}: {target.Path}");
            var segments = target.Path.Split(" > ", StringSplitOptions.RemoveEmptyEntries);
            if (MmcInventorySafetyRules.ShouldSkipNode(segments, out var skipExplanation))
            {
                report.AppendLine("[SKIPPED] " + target.Path);
                report.AppendLine("    " + skipExplanation);
                skipped++;
                continue;
            }
            var preexistingDialog = MmcInventoryDialogGuard.FindVisibleDialog(process.Id);
            if (preexistingDialog is not null)
            {
                report.AppendLine("[NOT CHECKED] " + target.Path);
                report.AppendLine("    MMC dialog blocks safe inspection: " + preexistingDialog);
                break;
            }
            var node = root;
            var status = "FOUND";
            var detail = "";
            for (var i = 0; i < segments.Length; i++)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    MmcInventoryDialogGuard.ThrowIfDialogOpen(process);
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

                    var actualLabel = node is null ? "" : child.Current.Name ?? "";
                    if (!actualLabel.Equals(segments[i], StringComparison.CurrentCultureIgnoreCase) &&
                        MmcTreePathMatcher.SectionNameMatches(actualLabel, segments[i]))
                    {
                        detail += (detail.Length == 0 ? "" : " ") +
                            $"Display-name alias at segment {i + 1}: {actualLabel}.";
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

        var uncheckedRoutes = targets.Length - found - missing - errors - skipped;
        report.AppendLine();
        report.AppendLine(
            $"SUMMARY: found={found}; missing={missing}; errors={errors}; skipped={skipped}; unchecked={uncheckedRoutes}; total={targets.Length}");
        report.AppendLine("FOUND confirms only a matching MMC tree path. It does not prove exact setting-dialog navigation.");
        report.AppendLine();

        const int snapshotNodeLimit = 1500;
        const int snapshotDepthLimit = 12;
        report.AppendLine($"### Observed MMC tree (limit {snapshotNodeLimit:N0} nodes, depth {snapshotDepthLimit})");
        report.AppendLine("Tree details are a bounded diagnostic, NOT a complete list of ADMX policies.");
        var remaining = snapshotNodeLimit;
        var coverage = new TreeSnapshotCoverage();
        if (uncheckedRoutes > 0 || DateTime.UtcNow >= deadline)
        {
            coverage.TimeLimitReached = true;
            report.AppendLine("[PARTIAL] Tree snapshot omitted: the route audit reached its deadline.");
        }
        else
        {
            SnapshotTree(root, report, 0, snapshotDepthLimit,
                ref remaining, token, Array.Empty<string>(), deadline,
                process, coverage);
        }

        report.AppendLine(
            $"TREE COVERAGE: {(coverage.IsComplete ? "BOUNDED COMPLETE" : "PARTIAL")}; " +
            $"nodes={coverage.Visited:N0}; unsafe_skipped={coverage.UnsafeSkipped}; " +
            $"depth_cutoffs={coverage.DepthCutoffs}; read_errors={coverage.ReadErrors}; " +
            $"node_limit={coverage.NodeLimitReached}; deadline={coverage.TimeLimitReached}");
        if (coverage.UnsafeSkipped > 0 || coverage.NodeLimitReached ||
            coverage.TimeLimitReached || coverage.DepthCutoffs > 0 || coverage.ReadErrors > 0)
        {
            report.AppendLine("PARTIAL means additional MMC nodes may exist; it is NOT evidence that " +
                "an unlisted GPO setting is missing or Not Configured.");
        }

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
        var children = parent.FindAll(TreeScope.Children, condition)
            .Cast<AutomationElement>().ToArray();
        var labels = children.Select(child => child.Current.Name ?? "").ToArray();
        var index = MmcTreePathMatcher.FindUniqueIndex(labels, segment);
        if (index == MmcTreePathMatcher.Ambiguous)
            throw new InvalidOperationException(
                $"Ambiguous MMC child section '{segment}': multiple matching nodes. " +
                "Exact navigation was refused.");
        return index < 0 ? null : children[index];
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

    private sealed class TreeSnapshotCoverage
    {
        public int Visited { get; set; }
        public int UnsafeSkipped { get; set; }
        public int DepthCutoffs { get; set; }
        public int ReadErrors { get; set; }
        public bool NodeLimitReached { get; set; }
        public bool TimeLimitReached { get; set; }
        public bool IsComplete =>
            Visited > 0 && UnsafeSkipped == 0 && DepthCutoffs == 0 &&
            ReadErrors == 0 && !NodeLimitReached && !TimeLimitReached;
    }

    private static void SnapshotTree(
        AutomationElement parent, StringBuilder report, int depth,
        int maxDepth, ref int remaining, CancellationToken token,
        IReadOnlyList<string> ancestors, DateTime deadline,
        Process process, TreeSnapshotCoverage coverage)
    {
        if (DateTime.UtcNow >= deadline)
        {
            coverage.TimeLimitReached = true;
            return;
        }

        if (remaining <= 0)
        {
            coverage.NodeLimitReached = true;
            return;
        }

        AutomationElementCollection nodes;
        try
        {
            MmcInventoryDialogGuard.ThrowIfDialogOpen(process);
            nodes = parent.FindAll(TreeScope.Children,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.TreeItem));
        }
        catch (Exception ex) when (ex is COMException or ElementNotAvailableException
                                   or InvalidOperationException)
        {
            coverage.ReadErrors++;
            report.AppendLine(new string(' ', Math.Max(0, depth * 2)) +
                "[READ ERROR] " + ex.Message);
            return;
        }

        foreach (AutomationElement node in nodes)
        {
            token.ThrowIfCancellationRequested();
            if (remaining <= 0)
            {
                coverage.NodeLimitReached = true;
                break;
            }
            if (DateTime.UtcNow >= deadline)
            {
                coverage.TimeLimitReached = true;
                break;
            }

            remaining--;
            coverage.Visited++;
            try
            {
                MmcInventoryDialogGuard.ThrowIfDialogOpen(process);
                var name = node.Current.Name ?? "<unnamed node>";
                var path = ancestors.Append(name).ToArray();
                var indent = new string(' ', depth * 2);
                report.AppendLine(indent + "- " + name);

                if (MmcInventorySafetyRules.ShouldSkipNode(path, out var reason))
                {
                    coverage.UnsafeSkipped++;
                    report.AppendLine(indent + "  [SKIPPED: unsafe snap-in] " + reason);
                    continue;
                }

                if (depth >= maxDepth)
                {
                    // We do not expand a node beyond the depth limit. If it
                    // exposes children through ExpandCollapse, report that
                    // its descendants were deliberately not inspected.
                    if (node.TryGetCurrentPattern(ExpandCollapsePattern.Pattern,
                            out var raw) &&
                        raw is ExpandCollapsePattern expand &&
                        expand.Current.ExpandCollapseState != ExpandCollapseState.LeafNode)
                    {
                        coverage.DepthCutoffs++;
                        report.AppendLine(indent + "  [NOT EXPANDED: depth limit]");
                    }
                    continue;
                }

                Expand(node);
                SnapshotTree(node, report, depth + 1, maxDepth,
                    ref remaining, token, path, deadline, process, coverage);
                if (coverage.TimeLimitReached || coverage.NodeLimitReached)
                    break;
            }
            catch (Exception ex) when (ex is COMException or ElementNotAvailableException
                                       or InvalidOperationException)
            {
                coverage.ReadErrors++;
                report.AppendLine(new string(' ', depth * 2) +
                    "[READ ERROR] " + ex.Message);
                // If an MMC modal popup was triggered by an extension, stop
                // rather than continuing blind into further snap-in nodes.
                if (MmcInventoryDialogGuard.FindVisibleDialog(process.Id) is not null)
                {
                    coverage.TimeLimitReached = true;
                    report.AppendLine("[PARTIAL] Visible MMC dialog; traversal stopped. " +
                        "The dialog was NOT dismissed automatically.");
                    break;
                }
            }
        }
    }
}

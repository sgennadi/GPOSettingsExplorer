using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GPOSettingsExplorer.Models;
using GPOSettingsExplorer.Services;

namespace GPOSettingsExplorer;

/// <summary>
/// Interactive AD container/link tree. Read-only unless the user explicitly
/// asks to open the existing protected link editor.
/// </summary>
public sealed class GpoHierarchyWindow : Window
{
    private readonly IReadOnlyList<GpoLinkTarget> _targets;
    private readonly IReadOnlyList<GpoLinkInfo> _links;
    private readonly TreeView _tree;
    private readonly TextBox _search;
    private readonly TextBlock _details;
    private readonly Button _edit;
    public GpoLinkInfo? SelectedLink { get; private set; }

    public GpoHierarchyWindow(
        IReadOnlyList<GpoLinkTarget> targets,
        IReadOnlyList<GpoLinkInfo> links)
    {
        _targets = targets;
        _links = links;
        Title = "GPO Hierarchy & Processing Order";
        Width = 1140;
        Height = 790;
        MinWidth = 660;
        MinHeight = 450;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var root = new DockPanel { Margin = new Thickness(10) };
        Content = root;
        var top = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
        DockPanel.SetDock(top, Dock.Top);
        top.Children.Add(new TextBlock
        {
            Text = "Configured GPO inheritance tree",
            FontSize = UiStyle.HeadingFontSize,
            FontWeight = FontWeights.SemiBold,
            Foreground = UiStyle.AccentBrush
        });
        top.Children.Add(new TextBlock
        {
            Text = "Link Order 1 has highest priority within one OU/domain/site. " +
                "This is a link configuration viewer, not the effective GPO result for a particular computer or user. " +
                "Security filters, WMI, inheritance, Enforced, loopback and client-side processing can alter application.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = UiStyle.WarningBrush,
            Margin = new Thickness(0, 5, 0, 6)
        });
        var searchBar = new WrapPanel();
        searchBar.Children.Add(new TextBlock
        {
            Text = "Find OU / GPO:", VerticalAlignment = VerticalAlignment.Center
        });
        _search = new TextBox { Width = 330 };
        _search.TextChanged += (_, _) => BuildTree();
        searchBar.Children.Add(_search);
        var expand = new Button { Content = "Expand all" };
        expand.Click += (_, _) =>
        {
            foreach (TreeViewItem item in _tree.Items)
                item.ExpandSubtree();
        };
        searchBar.Children.Add(expand);
        var collapse = new Button { Content = "Collapse all" };
        collapse.Click += (_, _) =>
        {
            foreach (TreeViewItem item in _tree.Items)
                item.IsExpanded = false;
        };
        searchBar.Children.Add(collapse);
        top.Children.Add(searchBar);

        _details = new TextBlock
        {
            Text = "Select a linked GPO to inspect its priority and edit the link.",
            Margin = new Thickness(3, 7, 3, 5),
            TextWrapping = TextWrapping.Wrap,
            Foreground = UiStyle.MutedBrush
        };
        DockPanel.SetDock(_details, Dock.Bottom);

        var footer = new WrapPanel
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 5, 0, 0)
        };
        DockPanel.SetDock(footer, Dock.Bottom);
        var export = new Button { Content = "Export hierarchy report" };
        export.Click += (_, _) =>
        {
            try
            {
                var file = GpoHierarchyReportService.Save(_targets, _links);
                _details.Text = "Report saved: " + file;
                Clipboard.SetText(file);
            }
            catch (Exception ex)
            {
                ErrorDialog.Show(this, "Export GPO hierarchy",
                    "Could not export link hierarchy.", ex);
            }
        };
        footer.Children.Add(export);
        _edit = new Button
        {
            Content = EditingGuard.IsEnabled ? "Edit selected link..." : "READ ONLY",
            IsEnabled = false,
            Style = (Style)FindResource("UiPrimaryButton"),
            ToolTip = "Uses the existing GPO link editor with WRITE ENABLED and change preview."
        };
        _edit.Click += (_, _) =>
        {
            if (SelectedLink is null)
                return;
            DialogResult = true;
        };
        footer.Children.Add(_edit);
        var close = new Button { Content = "Close", IsCancel = true };
        footer.Children.Add(close);

        _tree = new TreeView { MinHeight = 180 };
        _tree.SelectedItemChanged += (_, _) =>
        {
            SelectedLink = (_tree.SelectedItem as TreeViewItem)?.Tag as GpoLinkInfo;
            _edit.IsEnabled = EditingGuard.IsEnabled && SelectedLink is not null;
            if (SelectedLink is { } link)
                _details.Text = $"{link.GpoName}  |  {link.TargetType}: {link.TargetName}  |  " +
                    $"Link order {link.Order} (1 = highest local priority)  |  " +
                    $"Enabled: {link.Enabled}  |  Enforced: {link.Enforced}  |  " +
                    $"Block inheritance: {link.BlockInheritance}";
            else
                _details.Text = "Choose a GPO link to see its target, order and state.";
        };

        root.Children.Add(top);
        root.Children.Add(footer);
        root.Children.Add(_details);
        root.Children.Add(_tree);
        BuildTree();
    }

    private void BuildTree()
    {
        if (_tree is null)
            return;
        var query = _search?.Text.Trim() ?? string.Empty;
        var targets = _targets.Where(t =>
            query.Length == 0 ||
            t.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            t.DistinguishedName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            _links.Any(l => l.TargetDn.Equals(t.DistinguishedName, StringComparison.OrdinalIgnoreCase) &&
                l.GpoName.Contains(query, StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        var nodes = targets.ToDictionary(t => t.DistinguishedName,
            t => new TreeViewItem
            {
                Header = Text(t.DisplayName + (t.BlockInheritance ? "  [BLOCK INHERITANCE]" : ""),
                    t.BlockInheritance ? UiStyle.WarningBrush : UiStyle.AccentBrush),
                Tag = t,
                IsExpanded = true,
                ToolTip = t.DistinguishedName
            }, StringComparer.OrdinalIgnoreCase);

        _tree.Items.Clear();
        var siteRoot = new TreeViewItem
        {
            Header = Text("Sites", UiStyle.AccentBrush),
            IsExpanded = true
        };
        var domainRoot = new TreeViewItem
        {
            Header = Text("Domains and OUs", UiStyle.AccentBrush),
            IsExpanded = true
        };
        _tree.Items.Add(siteRoot);
        _tree.Items.Add(domainRoot);

        foreach (var target in targets.OrderBy(t => t.DistinguishedName.Length)
                     .ThenBy(t => t.DistinguishedName, StringComparer.OrdinalIgnoreCase))
        {
            var node = nodes[target.DistinguishedName];
            var parent = targets.Where(t =>
                    !t.DistinguishedName.Equals(target.DistinguishedName, StringComparison.OrdinalIgnoreCase) &&
                    target.DistinguishedName.EndsWith("," + t.DistinguishedName,
                        StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(t => t.DistinguishedName.Length)
                .FirstOrDefault();
            if (parent is not null)
                nodes[parent.DistinguishedName].Items.Add(node);
            else if (target.TargetType.Equals("Site", StringComparison.OrdinalIgnoreCase))
                siteRoot.Items.Add(node);
            else
                domainRoot.Items.Add(node);

            foreach (var link in _links.Where(l =>
                         l.TargetDn.Equals(target.DistinguishedName, StringComparison.OrdinalIgnoreCase))
                     .OrderBy(l => l.Order))
            {
                var color = !link.Enabled ? UiStyle.MutedBrush :
                    link.Enforced ? UiStyle.WarningBrush : UiStyle.SuccessBrush;
                var label = $"{link.Order}. {link.GpoName}" +
                    (link.Enforced ? "  [ENFORCED]" : "") +
                    (!link.Enabled ? "  [DISABLED]" : "");
                node.Items.Add(new TreeViewItem
                {
                    Header = Text(label, color),
                    Tag = link,
                    ToolTip = "Link Order 1 has the highest precedence in this container"
                });
            }
        }

        if (siteRoot.Items.Count == 0)
            _tree.Items.Remove(siteRoot);
        if (domainRoot.Items.Count == 0)
            _tree.Items.Remove(domainRoot);
    }

    private static TextBlock Text(string value, Brush foreground) => new()
    {
        Text = value,
        TextWrapping = TextWrapping.Wrap,
        Foreground = foreground,
        FontWeight = FontWeights.Medium
    };
}

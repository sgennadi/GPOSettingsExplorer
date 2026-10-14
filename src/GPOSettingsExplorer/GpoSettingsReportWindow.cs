using System.Windows;
using System.Windows.Controls;
using GPOSettingsExplorer.Models;
using GPOSettingsExplorer.Services;

namespace GPOSettingsExplorer;

/// <summary>
/// Native, read-only GPMC-style report view. It intentionally does not embed
/// Internet Explorer/WebBrowser or execute any HTML/script from policy values.
/// </summary>
public sealed class GpoSettingsReportWindow : Window
{
    private readonly TreeView _tree = new();
    private readonly TextBox _findText = new() { MinWidth = 150, Width = 260 };
    private readonly TextBlock _findStatus = new()
    {
        VerticalAlignment = VerticalAlignment.Center,
        TextWrapping = TextWrapping.Wrap
    };
    private Button? _copyRegistryTarget;
    private readonly TextBox _details = new()
    {
        IsReadOnly = true,
        TextWrapping = TextWrapping.Wrap,
        AcceptsReturn = true,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto
    };

    public GpoSettingsReportWindow(
        GpoInfo gpo, GpoSettingsReport report, Action openNativeGpoEditor,
        string? initialSearch = null)
    {
        ArgumentNullException.ThrowIfNull(gpo);
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(openNativeGpoEditor);

        Title = "GPO Settings - " + gpo.DisplayName;
        Width = 1110;
        Height = 740;
        MinWidth = 700;
        MinHeight = 450;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        UiStyle.ApplyWindowDefaults(this);
        var root = new DockPanel { Margin = new Thickness(10) };

        var top = new StackPanel();
        var heading = new TextBlock
        {
            Text = "GPO SETTINGS - GPMC REPORT (READ ONLY)",
            FontSize = UiStyle.HeadingFontSize,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(4, 3, 4, 6)
        };
        top.Children.Add(heading);
        top.Children.Add(new TextBlock
        {
            Text = gpo.DisplayName + "   |   " + gpo.DomainName +
                "\nComputer: " + (gpo.ComputerEnabled ? "Enabled" : "Disabled") +
                "   User: " + (gpo.UserEnabled ? "Enabled" : "Disabled") +
                "   |   Captured: " + report.CapturedUtc.ToLocalTime().ToString("g"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(4, 0, 4, 5),
            Foreground = UiStyle.MutedBrush
        });
        var toolbar = new WrapPanel { Margin = new Thickness(0, 2, 0, 7) };
        var expand = Button("Expand sections");
        expand.Click += (_, _) =>
        {
            foreach (var section in _tree.Items.OfType<TreeViewItem>())
            {
                section.IsExpanded = true;
                foreach (var child in section.Items.OfType<TreeViewItem>())
                    child.IsExpanded = true;
            }
        };
        var collapse = Button("Collapse all");
        collapse.Click += (_, _) =>
        {
            foreach (var section in _tree.Items.OfType<TreeViewItem>())
                Collapse(section);
        };
        var copy = Button("Copy selected details");
        copy.Click += (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(_details.Text))
                Clipboard.SetText(_details.Text);
        };
        var copyRegistry = Button("Copy registry target");
        copyRegistry.IsEnabled = false;
        copyRegistry.ToolTip = "Only available for a structurally parsed GPP Registry source. " +
            "This does not open local Regedit or confirm applied policy.";
        _copyRegistryTarget = copyRegistry;
        copyRegistry.Click += (_, _) =>
        {
            if (_tree.SelectedItem is TreeViewItem { Tag: GpoReportNode node } &&
                !string.IsNullOrWhiteSpace(node.RegistryTarget))
                Clipboard.SetText(node.RegistryTarget);
        };
        var editor = Button("Open GPO editor...");
        editor.Click += (_, _) =>
        {
            try { openNativeGpoEditor(); }
            catch (Exception ex)
            {
                CrashLogService.Write("GPO report: launch GPMC editor", ex);
                ErrorDialog.Show(this, "Open GPO editor",
                    "The native GPO editor could not be opened.", ex);
            }
        };
        toolbar.Children.Add(expand);
        toolbar.Children.Add(collapse);
        toolbar.Children.Add(copy);
        toolbar.Children.Add(copyRegistry);
        toolbar.Children.Add(editor);
        toolbar.Children.Add(new TextBlock
        {
            Text = "Find in report:", VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 4, 0)
        });
        toolbar.Children.Add(_findText);
        var findNext = Button("Find next");
        findNext.Click += (_, _) => FindNextMatch();
        _findText.KeyDown += (_, args) =>
        {
            if (args.Key == System.Windows.Input.Key.Enter)
            {
                FindNextMatch();
                args.Handled = true;
            }
        };
        toolbar.Children.Add(findNext);
        toolbar.Children.Add(_findStatus);
        top.Children.Add(toolbar);
        DockPanel.SetDock(top, Dock.Top);
        root.Children.Add(top);

        var bottom = new DockPanel { Margin = new Thickness(4, 7, 4, 0) };
        var close = Button("Close");
        close.Click += (_, _) => Close();
        DockPanel.SetDock(close, Dock.Right);
        bottom.Children.Add(close);
        bottom.Children.Add(new TextBlock
        {
            Text = report.Limited
                ? "PARTIAL DISPLAY: report reached a bounded node or depth cap. " +
                  "Use native GPMC for remaining values. No GPO was changed."
                : "Stored policy evidence from GPMC, NOT effective RSoP. " +
                  "Sections may include confidential domain information. No GPO was changed.",
            Foreground = UiStyle.WarningBrush,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center
        });
        DockPanel.SetDock(bottom, Dock.Bottom);
        root.Children.Add(bottom);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition
            { Width = new GridLength(4, GridUnitType.Star), MinWidth = 230 });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition
            { Width = new GridLength(5, GridUnitType.Star), MinWidth = 250 });

        _tree.HorizontalAlignment = HorizontalAlignment.Stretch;
        _tree.VerticalAlignment = VerticalAlignment.Stretch;
        ScrollViewer.SetVerticalScrollBarVisibility(
            _tree, ScrollBarVisibility.Auto);
        ScrollViewer.SetHorizontalScrollBarVisibility(
            _tree, ScrollBarVisibility.Auto);
        _tree.SelectedItemChanged += (_, _) =>
        {
            if (_tree.SelectedItem is TreeViewItem { Tag: GpoReportNode node })
            {
                _details.Text = node.Details;
                if (_copyRegistryTarget is not null)
                    _copyRegistryTarget.IsEnabled =
                        !string.IsNullOrWhiteSpace(node.RegistryTarget);
            }
        };
        foreach (var section in report.Sections)
            _tree.Items.Add(CreateItem(section, depth: 0));
        Grid.SetColumn(_tree, 0);
        grid.Children.Add(_tree);
        var splitter = new GridSplitter
        {
            Width = 7,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        Grid.SetColumn(splitter, 1);
        grid.Children.Add(splitter);
        _details.Text = "Select a section or setting on the left to inspect " +
                        "GPMC stored evidence. This view never changes policies.";
        _details.FontFamily = UiStyle.MonospaceFontFamily;
        _details.FontSize = UiStyle.MonospaceFontSize;
        Grid.SetColumn(_details, 2);
        grid.Children.Add(_details);
        root.Children.Add(grid);

        Content = root;
        if (!string.IsNullOrWhiteSpace(initialSearch))
            Loaded += (_, _) =>
            {
                _findText.Text = initialSearch;
                FindNextMatch();
            };
    }

    /// <summary>
    /// Find exact source/report information without opening MMC, modifying
    /// a GPO or guessing which registry key matches an arbitrary XML field.
    /// Search only the already bounded native GPMC report tree.
    /// </summary>
    private void FindNextMatch()
    {
        var query = _findText.Text.Trim();
        if (query.Length == 0)
        {
            _findStatus.Text = "Enter a setting, key path or value name.";
            return;
        }

        var matches = new List<(TreeViewItem Item, TreeViewItem[] Parents)>();
        void Walk(TreeViewItem item, List<TreeViewItem> ancestors)
        {
            if (item.Tag is GpoReportNode node &&
                (node.Label.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                 node.Details.Contains(query, StringComparison.CurrentCultureIgnoreCase)))
                matches.Add((item, ancestors.ToArray()));
            ancestors.Add(item);
            foreach (var child in item.Items.OfType<TreeViewItem>())
                Walk(child, ancestors);
            ancestors.RemoveAt(ancestors.Count - 1);
        }

        foreach (var item in _tree.Items.OfType<TreeViewItem>())
            Walk(item, new List<TreeViewItem>());

        if (matches.Count == 0)
        {
            _findStatus.Text = "No matching source row (" + query + ").";
            return;
        }

        var last = matches.FindIndex(m => ReferenceEquals(m.Item, _tree.SelectedItem));
        var next = (last + 1) % matches.Count;
        var selected = matches[next];
        foreach (var parent in selected.Parents)
            parent.IsExpanded = true;
        selected.Item.IsSelected = true;
        selected.Item.BringIntoView();
        _findStatus.Text = "Match " + (next + 1) + "/" + matches.Count +
            " (stored GPMC source; not RSoP).";
    }

    private static Button Button(string name) => new()
    {
        Content = name,
        MinWidth = 118,
        MinHeight = 30,
        Margin = new Thickness(2, 2, 5, 2)
    };

    private static TreeViewItem CreateItem(GpoReportNode node, int depth)
    {
        var item = new TreeViewItem
        {
            Header = new TextBlock
            {
                Text = node.Label,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 550
            },
            Tag = node,
            IsExpanded = depth < 2,
            ToolTip = node.Details.Length > 500
                ? node.Details[..500] + "..."
                : node.Details
        };
        foreach (var child in node.Children)
            item.Items.Add(CreateItem(child, depth + 1));
        return item;
    }

    private static void Collapse(TreeViewItem item)
    {
        item.IsExpanded = false;
        foreach (var child in item.Items.OfType<TreeViewItem>())
            Collapse(child);
    }
}

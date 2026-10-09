using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using GPOSettingsExplorer.Models;
using GPOSettingsExplorer.Services;

namespace GPOSettingsExplorer;

public partial class MainWindow
{
    private readonly GpoLinkService _gpoLinkService = new();
    private readonly ObservableCollection<GpoLinkInfo> _links = new();

    private IReadOnlyList<GpoLinkTarget> _linkTargets = Array.Empty<GpoLinkTarget>();
    private ICollectionView? _linksView;
    private string? _selectedHierarchyTargetDn;

    private void OpenHierarchyLinksTab_Click(object sender, RoutedEventArgs e)
    {
        MainTabs.SelectedItem = GpoLinksTab;
    }

    private async void LoadLinks_Click(object sender, RoutedEventArgs e)
    {
        await LoadLinksAsync();
    }

    private async Task LoadLinksAsync()
    {
        if (_domainContext is null)
        {
            return;
        }

        SetBusy(true, "Loading GPO links...");

        try
        {
            var targets = await Task.Run(() =>
                _gpoLinkService.LoadTargets(
                    _domainContext.DomainDistinguishedName,
                    _domainContext.ConfigurationNamingContext));

            var links = await Task.Run(() =>
                _gpoLinkService.LoadLinks(targets, _gpos));

            _linkTargets = targets;
            _selectedHierarchyTargetDn = null;
            ReplaceCollection(_links, links);
            PopulateGpoHierarchyTree();

            _linksView ??= CollectionViewSource.GetDefaultView(_links);
            _linksView.Filter = FilterLink;
            LinksGrid.ItemsSource = _linksView;

            LinksCountText.Text = $"{_links.Count:N0} links | {_linkTargets.Count:N0} targets";
            StatusText.Text = "GPO links loaded";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Load GPO Links",
                MessageBoxButton.OK, MessageBoxImage.Error);
            StatusText.Text = "GPO links load failed";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void ShowGpoHierarchy_Click(object sender, RoutedEventArgs e)
    {
        if (_domainContext is null)
            return;

        if (_linkTargets.Count == 0)
            await LoadLinksAsync();

        if (_linkTargets.Count == 0)
            return;

        var hierarchy = new GpoHierarchyWindow(_linkTargets, _links.ToArray())
        {
            Owner = this
        };

        if (hierarchy.ShowDialog() != true || hierarchy.SelectedLink is not { } chosen)
            return;

        var selected = _links.FirstOrDefault(link =>
            link.GpoId == chosen.GpoId &&
            link.TargetDn.Equals(chosen.TargetDn, StringComparison.OrdinalIgnoreCase));
        if (selected is null)
        {
            await LoadLinksAsync();
            selected = _links.FirstOrDefault(link =>
                link.GpoId == chosen.GpoId &&
                link.TargetDn.Equals(chosen.TargetDn, StringComparison.OrdinalIgnoreCase));
        }
        if (selected is null)
            return;

        LinksGrid.SelectedItem = selected;
        await EditSelectedLinkAsync();
    }

    private async void NewLink_Click(object sender, RoutedEventArgs e)
    {
        if (_domainContext is null)
        {
            return;
        }

        if (_linkTargets.Count == 0)
        {
            await LoadLinksAsync();
        }

        if (_linkTargets.Count == 0 || _gpos.Count == 0)
        {
            return;
        }

        var editor = new GpoLinkEditorWindow(_gpos, _linkTargets)
        {
            Owner = this
        };

        if (editor.ShowDialog() != true ||
            editor.SelectedGpo is null ||
            editor.SelectedTarget is null)
        {
            return;
        }

        SetBusy(true, "Creating GPO link...");

        try
        {
            await Task.Run(() =>
                _gpoLinkService.UpsertLink(
                    editor.SelectedTarget.DistinguishedName,
                    editor.SelectedGpo.Id,
                    editor.LinkEnabled,
                    editor.Enforced,
                    editor.Order));

            _auditService.Write(
                "Create link",
                "GPO Link",
                editor.SelectedGpo.DisplayName,
                $"Target: {editor.SelectedTarget.DistinguishedName}; Enabled: {editor.LinkEnabled}; Enforced: {editor.Enforced}; Order: {editor.Order}");

            await LoadLinksAsync();
            StatusText.Text = "GPO link created";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Create GPO Link",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void EditLink_Click(object sender, RoutedEventArgs e)
    {
        await EditSelectedLinkAsync();
    }

    private async void LinksGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        await EditSelectedLinkAsync();
    }

    private async Task EditSelectedLinkAsync()
    {
        if (LinksGrid.SelectedItem is not GpoLinkInfo selected)
        {
            return;
        }

        if (_linkTargets.Count == 0)
        {
            await LoadLinksAsync();
        }

        var editor = new GpoLinkEditorWindow(_gpos, _linkTargets, selected)
        {
            Owner = this
        };

        if (editor.ShowDialog() != true ||
            editor.SelectedGpo is null ||
            editor.SelectedTarget is null)
        {
            return;
        }

        SetBusy(true, "Updating GPO link...");

        try
        {
            await Task.Run(() =>
                _gpoLinkService.UpsertLink(
                    selected.TargetDn,
                    selected.GpoId,
                    editor.LinkEnabled,
                    editor.Enforced,
                    editor.Order));

            _auditService.Write(
                "Edit link",
                "GPO Link",
                selected.GpoName,
                $"Target: {selected.TargetDn}",
                before: $"Enabled={selected.Enabled}; Enforced={selected.Enforced}; Order={selected.Order}",
                after: $"Enabled={editor.LinkEnabled}; Enforced={editor.Enforced}; Order={editor.Order}");

            await LoadLinksAsync();
            StatusText.Text = "GPO link updated";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Edit GPO Link",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void RemoveLink_Click(object sender, RoutedEventArgs e)
    {
        if (LinksGrid.SelectedItem is not GpoLinkInfo selected)
        {
            return;
        }

        if (MessageBox.Show(
                this,
                $"Remove link '{selected.GpoName}' from '{selected.TargetName}'?",
                "Remove GPO Link",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        SetBusy(true, "Removing GPO link...");

        try
        {
            await Task.Run(() =>
                _gpoLinkService.RemoveLink(selected.TargetDn, selected.GpoId));

            _auditService.Write(
                "Remove link",
                "GPO Link",
                selected.GpoName,
                $"Target: {selected.TargetDn}; Order: {selected.Order}; Enabled: {selected.Enabled}; Enforced: {selected.Enforced}");

            await LoadLinksAsync();
            StatusText.Text = "GPO link removed";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Remove GPO Link",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void ToggleBlockInheritance_Click(object sender, RoutedEventArgs e)
    {
        if (LinksGrid.SelectedItem is not GpoLinkInfo selected)
        {
            return;
        }

        if (selected.TargetType.Equals("Site", StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show(this,
                "Block inheritance applies to domains and OUs, not sites.",
                "Block Inheritance",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var newValue = !selected.BlockInheritance;
        var action = newValue ? "enable" : "disable";

        if (MessageBox.Show(
                this,
                $"Do you want to {action} Block Inheritance on '{selected.TargetName}'?",
                "Block Inheritance",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        SetBusy(true, "Updating inheritance...");

        try
        {
            await Task.Run(() =>
                _gpoLinkService.SetBlockInheritance(selected.TargetDn, newValue));

            _auditService.Write(
                "Block inheritance",
                selected.TargetType,
                selected.TargetName,
                selected.TargetDn,
                before: selected.BlockInheritance.ToString(),
                after: newValue.ToString());

            await LoadLinksAsync();
            StatusText.Text = "Block inheritance updated";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Block Inheritance",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void LinkSearchBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(LinkSearchBox.Text))
            _selectedHierarchyTargetDn = null; // An explicit global search overrides the container filter.
        _linksView?.Refresh();
    }

    private void ShowAllGpoLinks_Click(object sender, RoutedEventArgs e)
    {
        _selectedHierarchyTargetDn = null;
        LinkSearchBox.Text = "";
        _linksView?.Refresh();
        LinkSelectionHint.Text = "All direct GPO links shown. Select a container or GPO in the hierarchy.";
    }

    private bool FilterLink(object item)
    {
        if (item is not GpoLinkInfo link)
        {
            return false;
        }

        if (_selectedHierarchyTargetDn is not null &&
            !link.TargetDn.Equals(_selectedHierarchyTargetDn, StringComparison.OrdinalIgnoreCase))
            return false;

        var search = LinkSearchBox?.Text?.Trim();
        if (string.IsNullOrWhiteSpace(search))
        {
            return true;
        }

        return link.GpoName.Contains(search, StringComparison.CurrentCultureIgnoreCase) ||
               link.TargetName.Contains(search, StringComparison.CurrentCultureIgnoreCase) ||
               link.TargetDn.Contains(search, StringComparison.OrdinalIgnoreCase) ||
               link.TargetType.Contains(search, StringComparison.OrdinalIgnoreCase);
    }

    private void PopulateGpoHierarchyTree()
    {
        if (GpoHierarchyTree is null)
            return;

        var text = HierarchySearchBox?.Text.Trim() ?? string.Empty;
        var all = _linkTargets.ToDictionary(t => t.DistinguishedName,
            StringComparer.OrdinalIgnoreCase);
        var visible = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (text.Length == 0)
        {
            foreach (var target in _linkTargets)
                visible.Add(target.DistinguishedName);
        }
        else
        {
            var matchingLinks = _links
                .Where(l => l.GpoName.Contains(text, StringComparison.CurrentCultureIgnoreCase))
                .Select(l => l.TargetDn)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var target in _linkTargets)
            {
                if (!target.Name.Contains(text, StringComparison.CurrentCultureIgnoreCase) &&
                    !target.DistinguishedName.Contains(text, StringComparison.OrdinalIgnoreCase) &&
                    !matchingLinks.Contains(target.DistinguishedName))
                    continue;

                visible.Add(target.DistinguishedName);
                foreach (var parentDn in ParentDns(target.DistinguishedName))
                    if (all.ContainsKey(parentDn))
                        visible.Add(parentDn);
            }
        }

        var nodes = new Dictionary<string, TreeViewItem>(StringComparer.OrdinalIgnoreCase);
        foreach (var target in _linkTargets.Where(t => visible.Contains(t.DistinguishedName)))
        {
            var label = target.DisplayName + (target.BlockInheritance ? "  [BLOCK INHERITANCE]" : "");
            nodes[target.DistinguishedName] = new TreeViewItem
            {
                Header = new TextBlock
                {
                    Text = label,
                    Foreground = target.BlockInheritance ? UiStyle.WarningBrush : UiStyle.AccentBrush,
                    TextWrapping = TextWrapping.Wrap
                },
                Tag = target,
                ToolTip = target.DistinguishedName,
                IsExpanded = text.Length > 0
            };
        }

        GpoHierarchyTree.Items.Clear();
        if (nodes.Count == 0)
        {
            HierarchyCountText.Text = _linkTargets.Count == 0
                ? "No hierarchy loaded yet. Use Refresh hierarchy."
                : "No matching OUs, sites or linked GPOs.";
            return;
        }

        var siteRoot = new TreeViewItem
        {
            Header = new TextBlock { Text = "Sites", Foreground = UiStyle.AccentBrush },
            IsExpanded = true
        };
        var domainRoot = new TreeViewItem
        {
            Header = new TextBlock { Text = "Domains and OUs", Foreground = UiStyle.AccentBrush },
            IsExpanded = true
        };
        GpoHierarchyTree.Items.Add(siteRoot);
        GpoHierarchyTree.Items.Add(domainRoot);

        foreach (var target in _linkTargets.Where(t => nodes.ContainsKey(t.DistinguishedName))
                     .OrderBy(t => t.DistinguishedName.Length)
                     .ThenBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            var node = nodes[target.DistinguishedName];
            var parent = ParentDns(target.DistinguishedName)
                .Select(dn => nodes.TryGetValue(dn, out var candidate) ? candidate : null)
                .FirstOrDefault(candidate => candidate is not null);
            if (parent is not null)
                parent.Items.Add(node);
            else if (target.TargetType.Equals("Site", StringComparison.OrdinalIgnoreCase))
                siteRoot.Items.Add(node);
            else
                domainRoot.Items.Add(node);

            foreach (var link in _links.Where(l =>
                         l.TargetDn.Equals(target.DistinguishedName,
                             StringComparison.OrdinalIgnoreCase))
                     .OrderBy(l => l.Order))
            {
                if (text.Length > 0 && !link.GpoName.Contains(text, StringComparison.OrdinalIgnoreCase) &&
                    !target.Name.Contains(text, StringComparison.CurrentCultureIgnoreCase) &&
                    !target.DistinguishedName.Contains(text, StringComparison.OrdinalIgnoreCase))
                    continue;

                node.Items.Add(new TreeViewItem
                {
                    Header = new TextBlock
                    {
                        Text = $"#{link.Order}  {link.GpoName}" +
                               (link.Enforced ? "  [ENFORCED]" : "") +
                               (!link.Enabled ? "  [DISABLED]" : ""),
                        Foreground = !link.Enabled ? UiStyle.MutedBrush :
                            link.Enforced ? UiStyle.WarningBrush : UiStyle.SuccessBrush,
                        TextWrapping = TextWrapping.Wrap
                    },
                    Tag = link,
                    ToolTip = $"{link.TargetDn} | Order {link.Order} | Enabled={link.Enabled} | Enforced={link.Enforced}"
                });
            }
        }

        HierarchyCountText.Text =
            $"{_linkTargets.Count:N0} containers, {_links.Count:N0} links | " +
            $"{nodes.Count:N0} containers shown. Select a linked GPO to edit its link on the right.";
    }

    private static IEnumerable<string> ParentDns(string distinguishedName)
    {
        // An escaped comma in a DN is part of the RDN, not a hierarchy boundary.
        for (var i = 0; i < distinguishedName.Length; i++)
        {
            if (distinguishedName[i] != ',')
                continue;
            var slashes = 0;
            for (var p = i - 1; p >= 0 && distinguishedName[p] == '\\'; p--)
                slashes++;
            if (slashes % 2 == 0)
                yield return distinguishedName[(i + 1)..];
        }
    }

    private void HierarchySearchBox_TextChanged(object sender, TextChangedEventArgs e) =>
        PopulateGpoHierarchyTree();

    private void ExpandGpoHierarchy_Click(object sender, RoutedEventArgs e)
    {
        foreach (var root in GpoHierarchyTree.Items.OfType<TreeViewItem>())
            root.ExpandSubtree();
    }

    private void CollapseGpoHierarchy_Click(object sender, RoutedEventArgs e)
    {
        static void Collapse(TreeViewItem item)
        {
            foreach (var child in item.Items.OfType<TreeViewItem>())
                Collapse(child);
            item.IsExpanded = false;
        }
        foreach (var root in GpoHierarchyTree.Items.OfType<TreeViewItem>())
            Collapse(root);
    }

    private void GpoHierarchyTree_SelectedItemChanged(object sender,
        RoutedPropertyChangedEventArgs<object> e)
    {
        var tag = (GpoHierarchyTree.SelectedItem as TreeViewItem)?.Tag;
        if (tag is GpoLinkTarget target)
        {
            // Selecting a container shows only its direct links, not inherited links.
            _selectedHierarchyTargetDn = target.DistinguishedName;
            LinkSearchBox.Text = string.Empty;
            _linksView?.Refresh();
            LinkSelectionHint.Text = $"Direct links on {target.DisplayName}. Select a GPO below this OU to edit its link.";
            return;
        }
        if (tag is not GpoLinkInfo link)
            return;

        // Make the exact link visible even if a previous search or container filter hid it.
        _selectedHierarchyTargetDn = null;
        LinkSearchBox.Text = string.Empty;
        _linksView?.Refresh();
        var selected = _links.FirstOrDefault(item =>
            item.GpoId == link.GpoId &&
            item.TargetDn.Equals(link.TargetDn, StringComparison.OrdinalIgnoreCase));
        if (selected is not null)
        {
            LinksGrid.SelectedItem = selected;
            LinksGrid.ScrollIntoView(selected);
            StatusText.Text = "Selected hierarchy link: " + selected.GpoName;
        }
    }

    private void LinksGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LinkSelectionHint is null)
            return;
        LinkSelectionHint.Text = LinksGrid.SelectedItem is GpoLinkInfo link
            ? $"{link.GpoName} -> {link.TargetName} | Order {link.Order} | " +
              $"Enabled={link.Enabled}, Enforced={link.Enforced}. Use Edit selected link to make changes."
            : "Select a linked GPO in the hierarchy or the links list.";
    }

    private void ExportGpoHierarchy_Click(object sender, RoutedEventArgs e)
    {
        if (_linkTargets.Count == 0)
        {
            MessageBox.Show(this, "Load the hierarchy first.", "GPO Hierarchy",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        try
        {
            var path = GpoHierarchyReportService.Save(_linkTargets, _links.ToArray());
            Clipboard.SetText(path);
            StatusText.Text = "Hierarchy report saved (path copied): " + path;
        }
        catch (Exception ex)
        {
            ErrorDialog.Show(this, "Export GPO hierarchy", "Could not save the hierarchy report.", ex);
        }
    }

}

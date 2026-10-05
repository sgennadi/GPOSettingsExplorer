using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
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
            ReplaceCollection(_links, links);

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
        _linksView?.Refresh();
    }

    private bool FilterLink(object item)
    {
        if (item is not GpoLinkInfo link)
        {
            return false;
        }

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
}

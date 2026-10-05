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
    private readonly GppShortcutService _gppShortcutService =
        new(new GppDocumentService());

    private readonly ObservableCollection<GppShortcutItemInfo> _gppShortcutItems = new();
    private ICollectionView? _gppShortcutView;
    private CancellationTokenSource? _gppShortcutCancellation;
    private bool _gppShortcutInitialized;

    private void GppShortcutsTab_Loaded(object sender, RoutedEventArgs e)
    {
        if (_gppShortcutInitialized)
            return;

        _gppShortcutInitialized = true;

        _gppShortcutView =
            CollectionViewSource.GetDefaultView(_gppShortcutItems);
        _gppShortcutView.Filter = FilterGppShortcut;
        GppShortcutsGrid.ItemsSource = _gppShortcutView;

        GppShortcutsScopeCombo.ItemsSource =
            new[] { "All", "Computer", "User" };
        GppShortcutsScopeCombo.SelectedIndex = 0;
    }

    private async void LoadGppShortcuts_Click(
        object sender,
        RoutedEventArgs e)
    {
        await LoadGppShortcutsAsync();
    }

    private async Task LoadGppShortcutsAsync()
    {
        if (_gpos.Count == 0)
            return;

        _gppShortcutCancellation?.Cancel();
        _gppShortcutCancellation = new CancellationTokenSource();

        SetBusy(true, "Loading Shortcuts preferences...");

        try
        {
            var progress = new Progress<string>(message =>
            {
                StatusText.Text = message;
                HeaderStatusText.Text = message;
            });

            var rows = await Task.Run(() =>
                _gppShortcutService.Load(
                    _gpos,
                    progress,
                    _gppShortcutCancellation.Token));

            ReplaceCollection(_gppShortcutItems, rows);
            _gppShortcutView?.Refresh();
            UpdateGppShortcutCount();

            HeaderStatusText.Text =
                $"{_gpos.Count:N0} GPOs | {_gppShortcutItems.Count:N0} Shortcut items";
            StatusText.Text = "Shortcuts preferences loaded";
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Shortcuts loading canceled";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                ex.Message,
                "Load Shortcuts",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            StatusText.Text = "Shortcuts load failed";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void NewGppShortcut_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_domainContext is null || _gpos.Count == 0)
            return;

        var target = new GppScopeTargetWindow(
            _gpos,
            "New Shortcut",
            "Select the target GPO and whether this Shortcut preference belongs to Computer or User Configuration:",
            selectedScope: "User")
        {
            Owner = this
        };

        if (target.ShowDialog() != true ||
            target.SelectedGpo is null)
            return;

        var item = _gppShortcutService.CreateNew(
            target.SelectedGpo,
            target.SelectedScope);

        var editor = new GppShortcutEditorWindow(item)
        {
            Owner = this
        };

        if (editor.ShowDialog() == true)
            await SaveGppShortcutAsync(editor.Item, "Create");
    }

    private async void EditGppShortcut_Click(
        object sender,
        RoutedEventArgs e)
    {
        await EditSelectedGppShortcutAsync();
    }

    private async void GppShortcutsGrid_MouseDoubleClick(
        object sender,
        MouseButtonEventArgs e)
    {
        await EditSelectedGppShortcutAsync();
    }

    private async Task EditSelectedGppShortcutAsync()
    {
        if (GppShortcutsGrid.SelectedItem
            is not GppShortcutItemInfo selected)
            return;

        var editable = CopyShortcutItem(selected);

        var editor = new GppShortcutEditorWindow(editable)
        {
            Owner = this
        };

        if (editor.ShowDialog() == true)
            await SaveGppShortcutAsync(editor.Item, "Edit");
    }

    private async void CloneGppShortcut_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (GppShortcutsGrid.SelectedItem
            is not GppShortcutItemInfo selected)
            return;

        var sourceGpo = _gpos.FirstOrDefault(
            gpo => gpo.Id == selected.GpoId);

        var target = new GppScopeTargetWindow(
            _gpos,
            "Clone Shortcut",
            "Select the destination GPO and scope for the cloned Shortcut preference:",
            sourceGpo,
            selected.Scope)
        {
            Owner = this
        };

        if (target.ShowDialog() != true ||
            target.SelectedGpo is null)
            return;

        var clone = _gppShortcutService.CreateNew(
            target.SelectedGpo,
            target.SelectedScope);

        _gppShortcutService.CopyEditableValues(
            selected,
            clone);

        clone.Uid =
            Guid.NewGuid().ToString("B").ToUpperInvariant();

        if (!string.IsNullOrWhiteSpace(clone.DisplayName))
            clone.DisplayName += " - Copy";

        var editor = new GppShortcutEditorWindow(clone)
        {
            Owner = this
        };

        if (editor.ShowDialog() == true)
            await SaveGppShortcutAsync(editor.Item, "Clone");
    }

    private async Task SaveGppShortcutAsync(
        GppShortcutItemInfo item,
        string action)
    {
        if (_domainContext is null)
            return;

        var gpo = _gpos.FirstOrDefault(
            candidate => candidate.Id == item.GpoId);

        if (gpo is null)
        {
            MessageBox.Show(
                this,
                "The target GPO no longer exists. Refresh the GPO list.",
                "Shortcuts",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        SetBusy(
            true,
            "Backing up GPO before Shortcuts change...");

        try
        {
            var backup = await Task.Run(() =>
                _gpmService.BackupGpo(
                    _domainContext.DomainName,
                    gpo.Id,
                    $"Automatic backup before {action.ToLowerInvariant()} Shortcut preference"));

            StatusText.Text = "Saving Shortcut preference...";

            await Task.Run(() =>
                _gppShortcutService.Save(
                    gpo,
                    _domainContext.DomainDistinguishedName,
                    item));

            _auditService.Write(
                action,
                "GPP Shortcuts",
                gpo.DisplayName,
                $"Scope: {item.Scope}; Shortcut: {item.ShortcutPath}; " +
                $"Action: {item.ActionDisplay}; Target type: {item.TargetType}; " +
                $"Target: {item.TargetPath}; Targeting: {item.HasFilters}; " +
                $"Backup: {backup}",
                after: GppShortcutSummary(item));

            if (_settings.Count > 0)
                await RefreshSingleGpoSettingsAsync(gpo);

            await LoadGppShortcutsAsync();

            if (_gppDocumentInitialized)
                await LoadGppDocumentsAsync();

            StatusText.Text =
                $"{action} Shortcut preference completed. Backup: {backup}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                ex.Message,
                "Save Shortcut",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            StatusText.Text = "Shortcut change failed";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void DeleteGppShortcut_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_domainContext is null ||
            GppShortcutsGrid.SelectedItem
                is not GppShortcutItemInfo selected)
            return;

        var gpo = _gpos.FirstOrDefault(
            candidate => candidate.Id == selected.GpoId);

        if (gpo is null)
            return;

        if (MessageBox.Show(
                this,
                $"Delete Shortcut preference '{selected.DisplayName}'?\n\n" +
                $"{selected.ShortcutPath}\n{selected.TargetTypeDisplay}: {selected.TargetPath}\n\n" +
                "A full GPO backup will be created first.",
                "Delete Shortcut",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        SetBusy(
            true,
            "Backing up GPO before deleting Shortcut...");

        try
        {
            var backup = await Task.Run(() =>
                _gpmService.BackupGpo(
                    _domainContext.DomainName,
                    gpo.Id,
                    "Automatic backup before deleting Shortcut preference"));

            await Task.Run(() =>
                _gppShortcutService.Delete(
                    gpo,
                    _domainContext.DomainDistinguishedName,
                    selected));

            _auditService.Write(
                "Delete",
                "GPP Shortcuts",
                gpo.DisplayName,
                $"Scope: {selected.Scope}; Shortcut: {selected.ShortcutPath}; " +
                $"Target: {selected.TargetPath}; Backup: {backup}",
                before: GppShortcutSummary(selected),
                after: "<Removed>");

            if (_settings.Count > 0)
                await RefreshSingleGpoSettingsAsync(gpo);

            await LoadGppShortcutsAsync();

            if (_gppDocumentInitialized)
                await LoadGppDocumentsAsync();

            StatusText.Text =
                $"Shortcut deleted. Backup: {backup}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                ex.Message,
                "Delete Shortcut",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            StatusText.Text = "Shortcut delete failed";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void ShowGppShortcutRawXml_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (GppShortcutsGrid.SelectedItem
            is not GppShortcutItemInfo selected)
            return;

        if (!_gppDocumentInitialized)
        {
            _gppDocumentInitialized = true;

            _gppDocumentView =
                CollectionViewSource.GetDefaultView(_gppDocuments);
            _gppDocumentView.Filter = FilterGppDocument;
            GppXmlGrid.ItemsSource = _gppDocumentView;

            GppXmlScopeCombo.ItemsSource =
                new[] { "All", "Computer", "User" };
            GppXmlScopeCombo.SelectedIndex = 0;

            var types = new[] { "All" }
                .Concat(
                    _gppDocumentService.GetKnownTypes()
                        .Select(type => type.Name))
                .Distinct(StringComparer.CurrentCultureIgnoreCase)
                .OrderBy(name =>
                    name.Equals(
                        "All",
                        StringComparison.OrdinalIgnoreCase)
                        ? string.Empty
                        : name)
                .ToArray();

            GppXmlTypeCombo.ItemsSource = types;
            GppXmlTypeCombo.SelectedIndex = 0;
        }

        MainTabs.SelectedItem = GppXmlTab;
        await LoadGppDocumentsAsync();

        GppXmlSearchBox.Text = selected.GpoName;
        GppXmlScopeCombo.SelectedItem = selected.Scope;
        GppXmlTypeCombo.SelectedItem = "Shortcuts";
        _gppDocumentView?.Refresh();

        StatusText.Text =
            $"GPP XML filter set to Shortcuts for {selected.GpoName}";
    }

    private void GppShortcutsSearchBox_TextChanged(
        object sender,
        System.Windows.Controls.TextChangedEventArgs e)
    {
        _gppShortcutView?.Refresh();
        UpdateGppShortcutCount();
    }

    private void GppShortcutsScopeCombo_SelectionChanged(
        object sender,
        System.Windows.Controls.SelectionChangedEventArgs e)
    {
        _gppShortcutView?.Refresh();
        UpdateGppShortcutCount();
    }

    private bool FilterGppShortcut(object item)
    {
        if (item is not GppShortcutItemInfo shortcut)
            return false;

        var scope =
            Convert.ToString(GppShortcutsScopeCombo?.SelectedItem);

        if (!string.IsNullOrWhiteSpace(scope) &&
            !scope.Equals(
                "All",
                StringComparison.OrdinalIgnoreCase) &&
            !shortcut.Scope.Equals(
                scope,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var search =
            GppShortcutsSearchBox?.Text?.Trim();

        return string.IsNullOrWhiteSpace(search) ||
               shortcut.SearchText.Contains(
                   search,
                   StringComparison.CurrentCultureIgnoreCase);
    }

    private void UpdateGppShortcutCount()
    {
        if (GppShortcutsCountText is null)
            return;

        var shown =
            _gppShortcutView?.Cast<object>().Count() ?? 0;

        GppShortcutsCountText.Text =
            $"{shown:N0} shown / {_gppShortcutItems.Count:N0} total";
    }

    private static GppShortcutItemInfo CopyShortcutItem(
        GppShortcutItemInfo source)
    {
        return new GppShortcutItemInfo
        {
            GpoId = source.GpoId,
            GpoName = source.GpoName,
            DomainName = source.DomainName,
            Scope = source.Scope,
            XmlPath = source.XmlPath,
            Uid = source.Uid,
            Ordinal = source.Ordinal,
            DisplayName = source.DisplayName,
            Action = source.Action,
            ShortcutPath = source.ShortcutPath,
            TargetType = source.TargetType,
            TargetPath = source.TargetPath,
            Arguments = source.Arguments,
            StartIn = source.StartIn,
            ShortcutKey = source.ShortcutKey,
            Window = source.Window,
            Comment = source.Comment,
            IconPath = source.IconPath,
            IconIndex = source.IconIndex,
            Pidl = source.Pidl,
            Disabled = source.Disabled,
            BypassErrors = source.BypassErrors,
            RemoveWhenNoLongerApplied =
                source.RemoveWhenNoLongerApplied,
            RunInUserContext = source.RunInUserContext,
            FiltersXml = source.FiltersXml
        };
    }

    private static string GppShortcutSummary(
        GppShortcutItemInfo item) =>
        $"Scope={item.Scope}; Action={item.ActionDisplay}; " +
        $"ShortcutPath={item.ShortcutPath}; TargetType={item.TargetType}; " +
        $"Target={item.TargetPath}; Arguments={item.Arguments}; " +
        $"StartIn={item.StartIn}; Icon={item.IconPath},{item.IconIndex}; " +
        $"Disabled={item.Disabled}; Targeting={item.HasFilters}";
}

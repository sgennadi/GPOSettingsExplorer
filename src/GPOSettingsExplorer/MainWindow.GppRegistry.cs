using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using GPOSettingsExplorer.Models;
using GPOSettingsExplorer.Services;

namespace GPOSettingsExplorer;

public partial class MainWindow
{
    private readonly GppRegistryService _gppRegistryService = new();
    private readonly ObservableCollection<GppRegistryItemInfo> _gppRegistryItems = new();

    private ICollectionView? _gppRegistryView;
    private CancellationTokenSource? _gppRegistryCancellation;
    private bool _gppRegistryInitialized;

    private void GppRegistryTab_Loaded(object sender, RoutedEventArgs e)
    {
        if (_gppRegistryInitialized)
            return;

        _gppRegistryInitialized = true;

        _gppRegistryView = CollectionViewSource.GetDefaultView(_gppRegistryItems);
        _gppRegistryView.Filter = FilterGppRegistry;
        GppRegistryGrid.ItemsSource = _gppRegistryView;

        GppRegistryScopeCombo.ItemsSource = new[] { "All", "Computer", "User" };
        GppRegistryScopeCombo.SelectedIndex = 0;
    }

    private async void LoadGppRegistry_Click(object sender, RoutedEventArgs e)
    {
        await LoadGppRegistryAsync();
    }

    private async Task LoadGppRegistryAsync()
    {
        if (_gpos.Count == 0)
            return;

        _gppRegistryCancellation?.Cancel();
        _gppRegistryCancellation = new CancellationTokenSource();

        SetBusy(true, "Loading Group Policy Preferences Registry items...");

        try
        {
            var progress = new Progress<string>(message =>
            {
                StatusText.Text = message;
                HeaderStatusText.Text = message;
            });

            var rows = await Task.Run(() =>
                _gppRegistryService.Load(
                    _gpos,
                    progress,
                    _gppRegistryCancellation.Token));

            ReplaceCollection(_gppRegistryItems, rows);
            _gppRegistryView?.Refresh();

            UpdateGppRegistryCount();
            HeaderStatusText.Text =
                $"{_gpos.Count:N0} GPOs | {_gppRegistryItems.Count:N0} GPP Registry items";
            StatusText.Text = "Registry Preferences loaded";
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Registry Preferences loading canceled";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                ex.Message,
                "Load Registry Preferences",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            StatusText.Text = "Registry Preferences load failed";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void NewGppRegistry_Click(object sender, RoutedEventArgs e)
    {
        if (_domainContext is null || _gpos.Count == 0)
            return;

        var target = new GppRegistryTargetWindow(
            _gpos,
            "New Registry Preference")
        {
            Owner = this
        };

        if (target.ShowDialog() != true || target.SelectedGpo is null)
            return;

        var item = _gppRegistryService.CreateNew(
            target.SelectedGpo,
            target.SelectedScope);

        var editor = new GppRegistryEditorWindow(item)
        {
            Owner = this
        };

        if (editor.ShowDialog() == true)
            await SaveGppRegistryItemAsync(editor.Item, "Create");
    }

    private async void EditGppRegistry_Click(object sender, RoutedEventArgs e)
    {
        await EditSelectedGppRegistryAsync();
    }

    private async void GppRegistryGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        await EditSelectedGppRegistryAsync();
    }

    private async Task EditSelectedGppRegistryAsync()
    {
        if (GppRegistryGrid.SelectedItem is not GppRegistryItemInfo selected)
            return;

        var editable = CopyItem(selected);

        var editor = new GppRegistryEditorWindow(editable)
        {
            Owner = this
        };

        if (editor.ShowDialog() == true)
            await SaveGppRegistryItemAsync(editor.Item, "Edit");
    }

    private async void CloneGppRegistry_Click(object sender, RoutedEventArgs e)
    {
        if (GppRegistryGrid.SelectedItem is not GppRegistryItemInfo selected)
            return;

        var sourceGpo = _gpos.FirstOrDefault(gpo => gpo.Id == selected.GpoId);

        var target = new GppRegistryTargetWindow(
            _gpos,
            "Clone Registry Preference",
            sourceGpo,
            selected.Scope)
        {
            Owner = this
        };

        if (target.ShowDialog() != true || target.SelectedGpo is null)
            return;

        var clone = _gppRegistryService.CreateNew(
            target.SelectedGpo,
            target.SelectedScope);

        CopyEditableValues(selected, clone);
        clone.Uid = Guid.NewGuid().ToString("B").ToUpperInvariant();
        clone.DisplayName = selected.DisplayName + " - Copy";

        var editor = new GppRegistryEditorWindow(clone)
        {
            Owner = this
        };

        if (editor.ShowDialog() == true)
            await SaveGppRegistryItemAsync(editor.Item, "Clone");
    }

    private async Task SaveGppRegistryItemAsync(
        GppRegistryItemInfo item,
        string action)
    {
        if (_domainContext is null)
            return;

        var gpo = _gpos.FirstOrDefault(candidate => candidate.Id == item.GpoId);
        if (gpo is null)
        {
            MessageBox.Show(
                this,
                "The target GPO no longer exists. Refresh the GPO list.",
                "Registry Preference",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        SetBusy(true, "Backing up GPO before Registry Preferences change...");

        try
        {
            var backup = await Task.Run(() =>
                _gpmService.BackupGpo(
                    _domainContext.DomainName,
                    gpo.Id,
                    $"Automatic backup before {action.ToLowerInvariant()} Registry Preference"));

            StatusText.Text = "Saving Registry Preference...";

            await Task.Run(() =>
                _gppRegistryService.Save(
                    gpo,
                    _domainContext.DomainDistinguishedName,
                    item));

            _auditService.Write(
                action,
                "GPP Registry",
                gpo.DisplayName,
                $"Scope: {item.Scope}; Target: {item.Target}; Action: {item.ActionDisplay}; " +
                $"Type: {item.ValueType}; Backup: {backup}",
                after: GppRegistrySummary(item));

            if (_settings.Count > 0)
                await RefreshSingleGpoSettingsAsync(gpo);

            await LoadGppRegistryAsync();

            StatusText.Text =
                $"{action} Registry Preference completed. Backup: {backup}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                ex.Message,
                "Save Registry Preference",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            StatusText.Text = "Registry Preference change failed";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void DeleteGppRegistry_Click(object sender, RoutedEventArgs e)
    {
        if (_domainContext is null ||
            GppRegistryGrid.SelectedItem is not GppRegistryItemInfo selected)
            return;

        var gpo = _gpos.FirstOrDefault(candidate => candidate.Id == selected.GpoId);
        if (gpo is null)
            return;

        if (MessageBox.Show(
                this,
                $"Delete Registry Preference '{selected.DisplayName}'?\n\n" +
                $"{selected.ActionDisplay}: {selected.Target}\n\n" +
                "A full GPO backup will be created first.",
                "Delete Registry Preference",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        SetBusy(true, "Backing up GPO before deleting Registry Preference...");

        try
        {
            var backup = await Task.Run(() =>
                _gpmService.BackupGpo(
                    _domainContext.DomainName,
                    gpo.Id,
                    "Automatic backup before deleting Registry Preference"));

            await Task.Run(() =>
                _gppRegistryService.Delete(
                    gpo,
                    _domainContext.DomainDistinguishedName,
                    selected));

            _auditService.Write(
                "Delete",
                "GPP Registry",
                gpo.DisplayName,
                $"Scope: {selected.Scope}; Target: {selected.Target}; Backup: {backup}",
                before: GppRegistrySummary(selected),
                after: "<Removed>");

            if (_settings.Count > 0)
                await RefreshSingleGpoSettingsAsync(gpo);

            await LoadGppRegistryAsync();
            StatusText.Text = $"Registry Preference deleted. Backup: {backup}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                ex.Message,
                "Delete Registry Preference",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            StatusText.Text = "Registry Preference delete failed";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void OpenGppRegistryXml_Click(object sender, RoutedEventArgs e)
    {
        if (GppRegistryGrid.SelectedItem is not GppRegistryItemInfo selected)
            return;

        if (!File.Exists(selected.XmlPath))
        {
            MessageBox.Show(
                this,
                $"Registry.xml does not exist:\n{selected.XmlPath}",
                "Registry Preferences",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"/select,\"{selected.XmlPath}\"",
            UseShellExecute = true
        });
    }

    private void GppRegistrySearchBox_TextChanged(
        object sender,
        System.Windows.Controls.TextChangedEventArgs e)
    {
        _gppRegistryView?.Refresh();
        UpdateGppRegistryCount();
    }

    private void GppRegistryScopeCombo_SelectionChanged(
        object sender,
        System.Windows.Controls.SelectionChangedEventArgs e)
    {
        _gppRegistryView?.Refresh();
        UpdateGppRegistryCount();
    }

    private bool FilterGppRegistry(object item)
    {
        if (item is not GppRegistryItemInfo row)
            return false;

        var scope = Convert.ToString(GppRegistryScopeCombo?.SelectedItem) ?? "All";
        if (!scope.Equals("All", StringComparison.OrdinalIgnoreCase) &&
            !row.Scope.Equals(scope, StringComparison.OrdinalIgnoreCase))
            return false;

        var search = GppRegistrySearchBox?.Text?.Trim();
        return string.IsNullOrWhiteSpace(search) ||
               row.SearchText.Contains(
                   search,
                   StringComparison.CurrentCultureIgnoreCase);
    }

    private void UpdateGppRegistryCount()
    {
        if (_gppRegistryView is null)
            return;

        var shown = _gppRegistryView.Cast<object>().Count();
        GppRegistryCountText.Text =
            $"{shown:N0} shown / {_gppRegistryItems.Count:N0} total";
    }

    private static GppRegistryItemInfo CopyItem(GppRegistryItemInfo source)
    {
        var copy = new GppRegistryItemInfo
        {
            GpoId = source.GpoId,
            GpoName = source.GpoName,
            DomainName = source.DomainName,
            Scope = source.Scope,
            XmlPath = source.XmlPath,
            Uid = source.Uid,
            Ordinal = source.Ordinal
        };

        CopyEditableValues(source, copy);
        return copy;
    }

    private static void CopyEditableValues(
        GppRegistryItemInfo source,
        GppRegistryItemInfo destination)
    {
        destination.DisplayName = source.DisplayName;
        destination.Description = source.Description;
        destination.Action = source.Action;
        destination.Hive = source.Hive;
        destination.Key = source.Key;
        destination.ValueName = source.ValueName;
        destination.ValueType = source.ValueType;
        destination.ValueData = source.ValueData;
        destination.DefaultValue = source.DefaultValue;
        destination.DisplayDecimal = source.DisplayDecimal;
        destination.Disabled = source.Disabled;
        destination.BypassErrors = source.BypassErrors;
        destination.RemoveWhenNoLongerApplied = source.RemoveWhenNoLongerApplied;
        destination.RunInUserContext = source.RunInUserContext;
        destination.FiltersXml = source.FiltersXml;
    }

    private static string GppRegistrySummary(GppRegistryItemInfo item) =>
        $"{item.ActionDisplay}; {item.Target}; Type={item.ValueType}; Value={item.ValueData}; " +
        $"Disabled={item.Disabled}; Targeting={item.HasFilters}";
}

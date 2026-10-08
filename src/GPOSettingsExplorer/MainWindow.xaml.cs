using GPOSettingsExplorer.Models;
using GPOSettingsExplorer.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;

namespace GPOSettingsExplorer;

public partial class MainWindow : Window
{
    private readonly DomainContextService _domainContextService = new();
    private readonly GpmService _gpmService = new();
    private readonly WmiFilterService _wmiFilterService = new();
    private readonly WmiBackupService _wmiBackupService = new();

    private readonly ObservableCollection<GpoInfo> _gpos = new();
    private readonly ObservableCollection<PolicySettingInfo> _settings = new();
    private readonly ObservableCollection<WmiFilterInfo> _wmiFilters = new();

    private ICollectionView _gpoView = null!;
    private ICollectionView _settingsView = null!;
    private ICollectionView _wmiView = null!;

    private DomainContext? _domainContext;
    private CancellationTokenSource? _indexCancellation;

    public MainWindow()
    {
        InitializeComponent();

        GpoGrid.ItemsSource = _gpos;
        SettingsGrid.ItemsSource = _settings;
        WmiGrid.ItemsSource = _wmiFilters;

        _gpoView = CollectionViewSource.GetDefaultView(_gpos);
        _settingsView = CollectionViewSource.GetDefaultView(_settings);
        _wmiView = CollectionViewSource.GetDefaultView(_wmiFilters);

        _gpoView.Filter = FilterGpo;
        _settingsView.Filter = FilterSetting;
        _wmiView.Filter = FilterWmi;

        InitializeSecurityUi();
        InitializeWorkspaceUi();
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        UpdateWriteModeUi();
        ApplyStoredUpdateState();

        await RefreshAllAsync();

        _ =
            CheckForUpdatesAutomaticallyAsync();
    }

    private async void RefreshAll_Click(object sender, RoutedEventArgs e)
    {
        await RefreshAllAsync();
    }

    private async Task RefreshAllAsync()
    {
        SetBusy(true, "Detecting domain...");

        try
        {
            _domainContext = _domainContextService.Detect();

            DomainConnectionState.SetContext(
                _domainContext);

            DomainText.Text = _domainContext.DomainName;
            ServerText.Text = _domainContext.ConnectedServer;

            _gpoCapabilities.Clear();

            if (!_gpmService.IsAvailable)
            {
                HeaderStatusText.Text = "GPMC/RSAT is not installed. GPO operations are unavailable.";
                _gpos.Clear();
            }
            else
            {
                HeaderStatusText.Text = "Loading GPOs...";

                var gpos = await Task.Run(() =>
                    _gpmService.LoadGpos(
                        _domainContext.DomainName));

                ReplaceCollection(
                    _gpos,
                    gpos);

                if (SecurityGpoCombo.SelectedItem is null &&
                    _gpos.Count > 0)
                {
                    SecurityGpoCombo.SelectedIndex = 0;
                }

                HeaderStatusText.Text =
                    $"Loading WMI filters... | {_gpos.Count:N0} GPOs";

                Exception? wmiLoadError = null;

                try
                {
                    var filters = (await Task.Run(() =>
                            _wmiFilterService.LoadFilters(
                                _domainContext.DomainName)))
                        .ToList();

                    foreach (var filter in filters)
                    {
                        filter.UsedByCount =
                            _gpos.Count(g =>
                                !string.IsNullOrWhiteSpace(
                                    g.WmiFilterPath) &&
                                g.WmiFilterPath.Contains(
                                    filter.Id,
                                    StringComparison.OrdinalIgnoreCase));
                    }

                    ReplaceCollection(
                        _wmiFilters,
                        filters);
                }
                catch (Exception ex)
                {
                    // WMI filter discovery is optional for the main GPO list.
                    // A provider/LDAP problem must never make the application
                    // look as if GPO discovery itself failed.
                    wmiLoadError = ex;
                    _wmiFilters.Clear();
                }

                if (wmiLoadError is null)
                {
                    HeaderStatusText.Text =
                        $"{_gpos.Count:N0} GPOs | {_wmiFilters.Count:N0} WMI filters";
                    StatusText.Text =
                        "Ready";
                }
                else
                {
                    HeaderStatusText.Text =
                        $"{_gpos.Count:N0} GPOs | WMI filters unavailable";
                    StatusText.Text =
                        $"Ready - WMI filters unavailable: {wmiLoadError.Message}";
                }
            }

            RestoreWorkspaceAfterRefresh();

            if (!_gpmService.IsAvailable)
            {
                StatusText.Text = "Ready";
            }
            else if (AllSettingsTab.IsSelected)
            {
                _ = EnsureSettingsIndexAsync(
                    forceRebuild: false);
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = "Load failed";
            MessageBox.Show(this, ex.Message, "GPO Settings Explorer",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void BuildSettingsIndex_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_domainContext is null ||
            _gpos.Count == 0)
        {
            return;
        }

        MainTabs.SelectedItem =
            AllSettingsTab;

        await EnsureSettingsIndexAsync(
            forceRebuild: true);
    }

    private void OpenGpoEditor_Click(object sender, RoutedEventArgs e)
    {
        OpenSelectedGpo();
    }

    private void GpoGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        OpenSelectedGpo();
    }

    private void SettingsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        OpenSelectedSettingGpo();
    }

    private void OpenSettingGpo_Click(object sender, RoutedEventArgs e)
    {
        OpenSelectedSettingGpo();
    }

    private void OpenSelectedGpo()
    {
        if (_domainContext is null || GpoGrid.SelectedItem is not GpoInfo selected)
        {
            return;
        }

        try
        {
            MarkGpoRecent(
                selected.Id);

            _gpmService.OpenEditor(selected, _domainContext.DomainDistinguishedName);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Open GPO Editor",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OpenSelectedSettingGpo()
    {
        if (_domainContext is null || SettingsGrid.SelectedItem is not PolicySettingInfo setting)
        {
            return;
        }

        var gpo = _gpos.FirstOrDefault(g => g.Id == setting.GpoId);
        if (gpo is null)
        {
            return;
        }

        try
        {
            MarkGpoRecent(
                gpo.Id);

            _gpmService.OpenEditor(gpo, _domainContext.DomainDistinguishedName);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Open GPO Editor",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void NewWmiFilter_Click(object sender, RoutedEventArgs e)
    {
        if (_domainContext is null)
        {
            return;
        }

        var filter = new WmiFilterInfo
        {
            Domain = _domainContext.DomainName,
            Author = $"{Environment.UserDomainName}\\{Environment.UserName}",
            SourceOrganization = _domainContext.DomainName,
            Name = "New WMI Filter"
        };
        filter.Rules.Add(new WmiRuleInfo
        {
            QueryLanguage = "WQL",
            TargetNamespace = @"root\CIMv2",
            Query = "SELECT * FROM Win32_OperatingSystem"
        });

        var editor = new WmiFilterEditorWindow(filter) { Owner = this };
        if (editor.ShowDialog() == true)
        {
            await SaveWmiFilterAsync(editor.Filter);
        }
    }

    private async void EditWmiFilter_Click(object sender, RoutedEventArgs e)
    {
        await EditSelectedWmiFilterAsync();
    }

    private async void WmiGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        await EditSelectedWmiFilterAsync();
    }

    private async Task EditSelectedWmiFilterAsync()
    {
        if (WmiGrid.SelectedItem is not WmiFilterInfo selected)
        {
            return;
        }

        var editor = new WmiFilterEditorWindow(selected.Clone()) { Owner = this };
        if (editor.ShowDialog() == true)
        {
            await SaveWmiFilterAsync(editor.Filter);
        }
    }

    private async Task SaveWmiFilterAsync(WmiFilterInfo filter)
    {
        if (_domainContext is null)
        {
            return;
        }

        SetBusy(true, "Saving WMI filter...");

        try
        {
            var existing = string.IsNullOrWhiteSpace(filter.Id)
                ? null
                : _wmiFilters.FirstOrDefault(item =>
                    item.Id.Equals(filter.Id, StringComparison.OrdinalIgnoreCase));

            string backupPath = string.Empty;
            if (existing is not null)
            {
                backupPath = _wmiBackupService.Backup(
                    existing,
                    "Automatic backup before editing WMI filter");
            }

            var saved = await Task.Run(() =>
                _wmiFilterService.Save(_domainContext.DomainName, filter));

            _auditService.Write(
                existing is null ? "Create" : "Edit",
                "WMI Filter",
                saved.Name,
                string.IsNullOrWhiteSpace(backupPath)
                    ? $"ID: {saved.Id}"
                    : $"ID: {saved.Id}; Backup: {backupPath}",
                before: existing is null ? string.Empty : WmiSummary(existing),
                after: WmiSummary(saved));

            await RefreshAllAsync();
            StatusText.Text = existing is null ? "WMI filter created" : "WMI filter saved";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Save WMI Filter",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void CloneWmiFilter_Click(object sender, RoutedEventArgs e)
    {
        if (_domainContext is null || WmiGrid.SelectedItem is not WmiFilterInfo selected)
        {
            return;
        }

        var dialog = new InputDialog(
            "Clone WMI Filter",
            "Name for the cloned filter:",
            $"{selected.Name} - Copy")
        {
            Owner = this
        };

        if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(dialog.Value))
        {
            return;
        }

        SetBusy(true, "Cloning WMI filter...");

        try
        {
            var clone = await Task.Run(() =>
                _wmiFilterService.Clone(_domainContext.DomainName, selected, dialog.Value));

            _auditService.Write(
                "Clone",
                "WMI Filter",
                clone.Name,
                $"Source: {selected.Name}; New ID: {clone.Id}",
                before: WmiSummary(selected),
                after: WmiSummary(clone));

            await RefreshAllAsync();
            StatusText.Text = "WMI filter cloned";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Clone WMI Filter",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void DeleteWmiFilter_Click(object sender, RoutedEventArgs e)
    {
        if (_domainContext is null || WmiGrid.SelectedItem is not WmiFilterInfo selected)
        {
            return;
        }

        var usedBy = _gpos
            .Where(gpo =>
                !string.IsNullOrWhiteSpace(gpo.WmiFilterPath) &&
                gpo.WmiFilterPath.Contains(selected.Id, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var text = usedBy.Length > 0
            ? $"The WMI filter '{selected.Name}' is used by {usedBy.Length} GPO(s).\n\nGPO Settings Explorer will back up those GPOs, unlink the filter, back up the filter definition, and then delete it. Continue?"
            : $"Delete WMI filter '{selected.Name}'? A JSON backup will be created first.";

        if (MessageBox.Show(this, text, "Delete WMI Filter",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        SetBusy(true, "Deleting WMI filter...");

        try
        {
            var wmiBackup = _wmiBackupService.Backup(
                selected,
                "Automatic backup before deleting WMI filter");

            foreach (var gpo in usedBy)
            {
                StatusText.Text = $"Backing up and unlinking {gpo.DisplayName}...";
                var gpoBackup = await Task.Run(() =>
                    _gpmService.BackupGpo(
                        _domainContext.DomainName,
                        gpo.Id,
                        $"Automatic backup before removing WMI filter '{selected.Name}'"));

                await Task.Run(() =>
                    _gpmService.SetWmiFilter(_domainContext.DomainName, gpo.Id, null));

                _auditService.Write(
                    "Unlink WMI filter",
                    "GPO",
                    gpo.DisplayName,
                    $"Filter: {selected.Name}; Backup: {gpoBackup}",
                    before: selected.Name,
                    after: "<None>");
            }

            await Task.Run(() =>
                _wmiFilterService.Delete(_domainContext.DomainName, selected.Id));

            _auditService.Write(
                "Delete",
                "WMI Filter",
                selected.Name,
                $"ID: {selected.Id}; Used by GPOs: {usedBy.Length}; Backup: {wmiBackup}",
                before: WmiSummary(selected));

            await RefreshAllAsync();
            StatusText.Text = "WMI filter deleted";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Delete WMI Filter",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void TestWmiFilter_Click(object sender, RoutedEventArgs e)
    {
        if (WmiGrid.SelectedItem is not WmiFilterInfo selected)
        {
            return;
        }

        var dialog = new InputDialog(
            "Test WMI Filter",
            "Computer name:",
            Environment.MachineName)
        {
            Owner = this
        };

        if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(dialog.Value))
        {
            return;
        }

        SetBusy(true, $"Testing WMI filter on {dialog.Value}...");

        try
        {
            var result = await Task.Run(() => _wmiFilterService.Test(selected, dialog.Value));
            new WmiTestResultsWindow(dialog.Value, result)
            {
                Owner = this
            }.ShowDialog();

            StatusText.Text = "WMI filter test complete";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Test WMI Filter",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void AssignWmiFilter_Click(object sender, RoutedEventArgs e)
    {
        if (_domainContext is null ||
            GpoGrid.SelectedItem is not GpoInfo gpo ||
            _wmiFilters.Count == 0)
        {
            return;
        }

        var picker = new WmiFilterPickerWindow(_wmiFilters, gpo.WmiFilterName)
        {
            Owner = this
        };

        if (picker.ShowDialog() != true || picker.SelectedFilter is null)
        {
            return;
        }

        SetBusy(true, "Assigning WMI filter...");

        try
        {
            var selectedFilter = picker.SelectedFilter;
            var backup = await Task.Run(() =>
                _gpmService.BackupGpo(
                    _domainContext.DomainName,
                    gpo.Id,
                    $"Automatic backup before assigning WMI filter '{selectedFilter.Name}'"));

            await Task.Run(() =>
                _gpmService.SetWmiFilter(_domainContext.DomainName, gpo.Id, selectedFilter));

            _auditService.Write(
                "Assign WMI filter",
                "GPO",
                gpo.DisplayName,
                $"Backup: {backup}",
                before: string.IsNullOrWhiteSpace(gpo.WmiFilterName) ? "<None>" : gpo.WmiFilterName,
                after: selectedFilter.Name);

            await RefreshAllAsync();
            StatusText.Text = "WMI filter assigned";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Assign WMI Filter",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void RemoveWmiFilter_Click(object sender, RoutedEventArgs e)
    {
        if (_domainContext is null || GpoGrid.SelectedItem is not GpoInfo gpo)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(gpo.WmiFilterName))
        {
            MessageBox.Show(this, "The selected GPO does not have a WMI filter.",
                "Remove WMI Filter", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (MessageBox.Show(this,
                $"Remove WMI filter '{gpo.WmiFilterName}' from GPO '{gpo.DisplayName}'?",
                "Remove WMI Filter",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        SetBusy(true, "Removing WMI filter...");

        try
        {
            var backup = await Task.Run(() =>
                _gpmService.BackupGpo(
                    _domainContext.DomainName,
                    gpo.Id,
                    $"Automatic backup before removing WMI filter '{gpo.WmiFilterName}'"));

            await Task.Run(() =>
                _gpmService.SetWmiFilter(_domainContext.DomainName, gpo.Id, null));

            _auditService.Write(
                "Remove WMI filter",
                "GPO",
                gpo.DisplayName,
                $"Backup: {backup}",
                before: gpo.WmiFilterName,
                after: "<None>");

            await RefreshAllAsync();
            StatusText.Text = "WMI filter removed";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Remove WMI Filter",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void GpoSearchBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        => _gpoView.Refresh();

    private void SettingsSearchBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        => _settingsView.Refresh();

    private void WmiSearchBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        => _wmiView.Refresh();

    private bool FilterGpo(object item)
    {
        if (item is not GpoInfo gpo)
        {
            return false;
        }

        if (!MatchesGpoQuickFilter(
                gpo))
        {
            return false;
        }

        var search = GpoSearchBox?.Text?.Trim();
        if (string.IsNullOrWhiteSpace(search))
        {
            return true;
        }

        return gpo.DisplayName.Contains(search, StringComparison.CurrentCultureIgnoreCase) ||
               gpo.IdText.Contains(search, StringComparison.OrdinalIgnoreCase) ||
               gpo.ScopeState.Contains(search, StringComparison.CurrentCultureIgnoreCase) ||
               gpo.WmiFilterName.Contains(search, StringComparison.CurrentCultureIgnoreCase);
    }

    private bool FilterSetting(object item)
    {
        if (item is not PolicySettingInfo setting)
        {
            return false;
        }

        var search = SettingsSearchBox?.Text?.Trim();
        return string.IsNullOrWhiteSpace(search) ||
               setting.SearchText.Contains(search, StringComparison.CurrentCultureIgnoreCase);
    }

    private bool FilterWmi(object item)
    {
        if (item is not WmiFilterInfo filter)
        {
            return false;
        }

        var search = WmiSearchBox?.Text?.Trim();
        if (string.IsNullOrWhiteSpace(search))
        {
            return true;
        }

        return filter.Name.Contains(search, StringComparison.CurrentCultureIgnoreCase) ||
               filter.Description.Contains(search, StringComparison.CurrentCultureIgnoreCase) ||
               filter.Author.Contains(search, StringComparison.CurrentCultureIgnoreCase) ||
               filter.Id.Contains(search, StringComparison.OrdinalIgnoreCase) ||
               filter.QueriesPreview.Contains(search, StringComparison.CurrentCultureIgnoreCase);
    }

    private static string WmiSummary(WmiFilterInfo filter)
    {
        var rules = string.Join(" | ", filter.Rules.Select(rule =>
            $"{rule.TargetNamespace}: {rule.Query}"));

        return $"Name={filter.Name}; Description={filter.Description}; Author={filter.Author}; Rules={rules}";
    }

    private void SetBusy(bool busy, string? message = null)
    {
        BusyProgress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        Mouse.OverrideCursor = busy ? Cursors.Wait : null;

        if (!string.IsNullOrWhiteSpace(message))
        {
            StatusText.Text = message;
        }
    }

    private static void ReplaceCollection<T>(
        ObservableCollection<T> target,
        IEnumerable<T> source)
    {
        target.Clear();
        foreach (var item in source)
        {
            target.Add(item);
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _indexCancellation?.Cancel();
        base.OnClosed(e);
    }
}

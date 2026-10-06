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
    private readonly GppPowerOptionsService _gppPowerOptionsService =
        new(new GppDocumentService());

    private readonly ObservableCollection<GppPowerOptionItemInfo>
        _gppPowerOptions = new();

    private ICollectionView? _gppPowerOptionsView;
    private CancellationTokenSource? _gppPowerOptionsCancellation;
    private bool _gppPowerOptionsInitialized;

    private void GppPowerOptionsTab_Loaded(
        object sender,
        RoutedEventArgs e)
    {
        if (_gppPowerOptionsInitialized)
            return;

        _gppPowerOptionsInitialized = true;

        _gppPowerOptionsView =
            CollectionViewSource.GetDefaultView(
                _gppPowerOptions);

        _gppPowerOptionsView.Filter =
            FilterGppPowerOption;

        GppPowerOptionsGrid.ItemsSource =
            _gppPowerOptionsView;

        GppPowerOptionsScopeCombo.ItemsSource =
            new[]
            {
                "All",
                "Computer",
                "User"
            };

        GppPowerOptionsScopeCombo.SelectedIndex =
            0;

        GppPowerOptionsTypeCombo.ItemsSource =
            new[]
            {
                "All",
                "Power Plan (Vista+)",
                "Legacy"
            };

        GppPowerOptionsTypeCombo.SelectedIndex =
            0;
    }

    private async void LoadGppPowerOptions_Click(
        object sender,
        RoutedEventArgs e)
    {
        await LoadGppPowerOptionsAsync();
    }

    private async Task LoadGppPowerOptionsAsync()
    {
        if (_gpos.Count == 0)
            return;

        _gppPowerOptionsCancellation?.Cancel();

        _gppPowerOptionsCancellation =
            new CancellationTokenSource();

        SetBusy(
            true,
            "Loading Power Options preferences...");

        try
        {
            var progress =
                new Progress<string>(
                    message =>
                    {
                        StatusText.Text =
                            message;

                        HeaderStatusText.Text =
                            message;
                    });

            var rows =
                await Task.Run(
                    () =>
                        _gppPowerOptionsService.Load(
                            _gpos,
                            progress,
                            _gppPowerOptionsCancellation.Token));

            ReplaceCollection(
                _gppPowerOptions,
                rows);

            _gppPowerOptionsView?.Refresh();

            UpdateGppPowerOptionsCount();

            HeaderStatusText.Text =
                $"{_gpos.Count:N0} GPOs | {_gppPowerOptions.Count:N0} Power Options items";

            StatusText.Text =
                "Power Options preferences loaded";
        }
        catch (OperationCanceledException)
        {
            StatusText.Text =
                "Power Options loading canceled";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                ex.Message,
                "Load Power Options",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            StatusText.Text =
                "Power Options load failed";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void NewGppPowerOption_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_domainContext is null ||
            _gpos.Count == 0)
            return;

        var target =
            new GppScopeTargetWindow(
                _gpos,
                "New Power Options Preference",
                "Select the target GPO and scope:")
            {
                Owner = this
            };

        if (target.ShowDialog() != true ||
            target.SelectedGpo is null)
            return;

        var item =
            _gppPowerOptionsService.CreateNew(
                target.SelectedGpo,
                target.SelectedScope);

        var editor =
            new GppPowerOptionsEditorWindow(
                item)
            {
                Owner = this
            };

        if (editor.ShowDialog() == true)
        {
            await SaveGppPowerOptionAsync(
                editor.Item,
                "Create");
        }
    }

    private async void EditGppPowerOption_Click(
        object sender,
        RoutedEventArgs e)
    {
        await EditSelectedGppPowerOptionAsync();
    }

    private async void GppPowerOptionsGrid_MouseDoubleClick(
        object sender,
        MouseButtonEventArgs e)
    {
        await EditSelectedGppPowerOptionAsync();
    }

    private async Task EditSelectedGppPowerOptionAsync()
    {
        if (GppPowerOptionsGrid.SelectedItem
            is not GppPowerOptionItemInfo selected)
            return;

        if (!selected.SupportsStructuredEditing)
        {
            MessageBox.Show(
                this,
                "This is a legacy Power Options preference item. Structured editing is disabled to avoid changing the legacy format. Use Show raw XML for controlled editing.",
                "Power Options",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            return;
        }

        var editable =
            CopyPowerOptionItem(
                selected);

        var editor =
            new GppPowerOptionsEditorWindow(
                editable)
            {
                Owner = this
            };

        if (editor.ShowDialog() == true)
        {
            await SaveGppPowerOptionAsync(
                editor.Item,
                "Edit");
        }
    }

    private async void CloneGppPowerOption_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (GppPowerOptionsGrid.SelectedItem
            is not GppPowerOptionItemInfo selected)
            return;

        if (!selected.SupportsStructuredEditing)
        {
            MessageBox.Show(
                this,
                "Legacy Power Options items are not cloned by the structured editor. Use raw XML export/import if needed.",
                "Clone Power Options",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            return;
        }

        var sourceGpo =
            _gpos.FirstOrDefault(
                gpo =>
                    gpo.Id == selected.GpoId);

        var target =
            new GppScopeTargetWindow(
                _gpos,
                "Clone Power Options Preference",
                $"Select the destination GPO and scope for '{selected.DisplayName}':",
                sourceGpo,
                selected.Scope)
            {
                Owner = this
            };

        if (target.ShowDialog() != true ||
            target.SelectedGpo is null)
            return;

        var clone =
            _gppPowerOptionsService.CreateNew(
                target.SelectedGpo,
                target.SelectedScope);

        _gppPowerOptionsService.CopyEditableValues(
            selected,
            clone);

        clone.Uid =
            Guid.NewGuid()
                .ToString("B")
                .ToUpperInvariant();

        if (!string.IsNullOrWhiteSpace(
                clone.DisplayName))
        {
            clone.DisplayName +=
                " - Copy";
        }

        var editor =
            new GppPowerOptionsEditorWindow(
                clone)
            {
                Owner = this
            };

        if (editor.ShowDialog() == true)
        {
            await SaveGppPowerOptionAsync(
                editor.Item,
                "Clone");
        }
    }

    private async Task SaveGppPowerOptionAsync(
        GppPowerOptionItemInfo item,
        string action)
    {
        if (_domainContext is null)
            return;

        var gpo =
            _gpos.FirstOrDefault(
                candidate =>
                    candidate.Id == item.GpoId);

        if (gpo is null)
        {
            MessageBox.Show(
                this,
                "The target GPO no longer exists. Refresh the GPO list.",
                "Power Options",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return;
        }

        SetBusy(
            true,
            "Backing up GPO before Power Options change...");

        try
        {
            var backup =
                await Task.Run(
                    () =>
                        _gpmService.BackupGpo(
                            _domainContext.DomainName,
                            gpo.Id,
                            $"Automatic backup before {action.ToLowerInvariant()} Power Options preference"));

            await Task.Run(
                () =>
                    _gppPowerOptionsService.Save(
                        gpo,
                        _domainContext.DomainDistinguishedName,
                        item));

            _auditService.Write(
                action,
                "GPP Power Options",
                gpo.DisplayName,
                $"Scope: {item.Scope}; Type: {item.KindDisplay}; " +
                $"Action: {item.ActionDisplay}; Plan: {item.DisplayName}; " +
                $"GUID: {item.PlanGuid}; Default: {item.SetAsDefault}; " +
                $"Targeting: {item.HasFilters}; Backup: {backup}",
                after:
                    GppPowerOptionSummary(
                        item));

            if (_settings.Count > 0)
            {
                await RefreshSingleGpoSettingsAsync(
                    gpo);
            }

            await LoadGppPowerOptionsAsync();

            if (_gppDocumentInitialized)
            {
                await LoadGppDocumentsAsync();
            }

            StatusText.Text =
                $"{action} Power Options preference completed. Backup: {backup}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                ex.Message,
                "Save Power Options",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            StatusText.Text =
                "Power Options change failed";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void DeleteGppPowerOption_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_domainContext is null ||
            GppPowerOptionsGrid.SelectedItem
                is not GppPowerOptionItemInfo selected)
            return;

        var gpo =
            _gpos.FirstOrDefault(
                candidate =>
                    candidate.Id == selected.GpoId);

        if (gpo is null)
            return;

        if (MessageBox.Show(
                this,
                $"Delete Power Options preference '{selected.DisplayName}'?\n\n" +
                $"{selected.KindDisplay}; {selected.ActionDisplay}; {selected.PlanGuid}\n\n" +
                "This removes the preference item from the GPO. A full GPO backup will be created first.",
                "Delete Power Options Preference",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) !=
            MessageBoxResult.Yes)
        {
            return;
        }

        SetBusy(
            true,
            "Backing up GPO before deleting Power Options preference...");

        try
        {
            var backup =
                await Task.Run(
                    () =>
                        _gpmService.BackupGpo(
                            _domainContext.DomainName,
                            gpo.Id,
                            "Automatic backup before deleting Power Options preference"));

            await Task.Run(
                () =>
                    _gppPowerOptionsService.Delete(
                        gpo,
                        _domainContext.DomainDistinguishedName,
                        selected));

            _auditService.Write(
                "Delete",
                "GPP Power Options",
                gpo.DisplayName,
                $"Scope: {selected.Scope}; Type: {selected.KindDisplay}; " +
                $"Plan: {selected.DisplayName}; Backup: {backup}",
                before:
                    GppPowerOptionSummary(
                        selected),
                after:
                    "<Removed>");

            if (_settings.Count > 0)
            {
                await RefreshSingleGpoSettingsAsync(
                    gpo);
            }

            await LoadGppPowerOptionsAsync();

            if (_gppDocumentInitialized)
            {
                await LoadGppDocumentsAsync();
            }

            StatusText.Text =
                $"Power Options preference deleted. Backup: {backup}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                ex.Message,
                "Delete Power Options Preference",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            StatusText.Text =
                "Power Options preference delete failed";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void ShowGppPowerOptionRawXml_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (GppPowerOptionsGrid.SelectedItem
            is not GppPowerOptionItemInfo selected)
            return;

        if (!_gppDocumentInitialized)
        {
            _gppDocumentInitialized =
                true;

            _gppDocumentView =
                CollectionViewSource.GetDefaultView(
                    _gppDocuments);

            _gppDocumentView.Filter =
                FilterGppDocument;

            GppXmlGrid.ItemsSource =
                _gppDocumentView;

            GppXmlScopeCombo.ItemsSource =
                new[]
                {
                    "All",
                    "Computer",
                    "User"
                };

            GppXmlScopeCombo.SelectedIndex =
                0;

            var types =
                new[]
                {
                    "All"
                }
                .Concat(
                    _gppDocumentService
                        .GetKnownTypes()
                        .Select(
                            type =>
                                type.Name))
                .Distinct(
                    StringComparer.CurrentCultureIgnoreCase)
                .OrderBy(
                    name =>
                        name.Equals(
                            "All",
                            StringComparison.OrdinalIgnoreCase)
                            ? string.Empty
                            : name)
                .ToArray();

            GppXmlTypeCombo.ItemsSource =
                types;

            GppXmlTypeCombo.SelectedIndex =
                0;
        }

        MainTabs.SelectedItem =
            GppXmlTab;

        await LoadGppDocumentsAsync();

        GppXmlSearchBox.Text =
            selected.GpoName;

        GppXmlScopeCombo.SelectedItem =
            selected.Scope;

        GppXmlTypeCombo.SelectedItem =
            "Power Options";

        _gppDocumentView?.Refresh();

        StatusText.Text =
            $"GPP XML filter set to Power Options for {selected.GpoName}";
    }

    private void GppPowerOptionsSearchBox_TextChanged(
        object sender,
        System.Windows.Controls.TextChangedEventArgs e)
    {
        _gppPowerOptionsView?.Refresh();
        UpdateGppPowerOptionsCount();
    }

    private void GppPowerOptionsFilter_SelectionChanged(
        object sender,
        System.Windows.Controls.SelectionChangedEventArgs e)
    {
        _gppPowerOptionsView?.Refresh();
        UpdateGppPowerOptionsCount();
    }

    private bool FilterGppPowerOption(
        object item)
    {
        if (item
            is not GppPowerOptionItemInfo power)
            return false;

        var scope =
            Convert.ToString(
                GppPowerOptionsScopeCombo?
                    .SelectedItem);

        if (!string.IsNullOrWhiteSpace(scope) &&
            !scope.Equals(
                "All",
                StringComparison.OrdinalIgnoreCase) &&
            !power.Scope.Equals(
                scope,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var type =
            Convert.ToString(
                GppPowerOptionsTypeCombo?
                    .SelectedItem);

        if (!string.IsNullOrWhiteSpace(type) &&
            !type.Equals(
                "All",
                StringComparison.OrdinalIgnoreCase))
        {
            var matches =
                type.Equals(
                    "Legacy",
                    StringComparison.OrdinalIgnoreCase)
                    ? !power.SupportsStructuredEditing
                    : power.SupportsStructuredEditing;

            if (!matches)
                return false;
        }

        var search =
            GppPowerOptionsSearchBox?
                .Text?
                .Trim();

        return string.IsNullOrWhiteSpace(search) ||
               power.SearchText.Contains(
                   search,
                   StringComparison.CurrentCultureIgnoreCase);
    }

    private void UpdateGppPowerOptionsCount()
    {
        if (GppPowerOptionsCountText is null)
            return;

        var shown =
            _gppPowerOptionsView?
                .Cast<object>()
                .Count()
            ?? 0;

        GppPowerOptionsCountText.Text =
            $"{shown:N0} shown / {_gppPowerOptions.Count:N0} total";
    }

    private static GppPowerOptionItemInfo CopyPowerOptionItem(
        GppPowerOptionItemInfo source) =>
        new()
        {
            GpoId = source.GpoId,
            GpoName = source.GpoName,
            DomainName = source.DomainName,
            Scope = source.Scope,
            XmlPath = source.XmlPath,
            Uid = source.Uid,
            Ordinal = source.Ordinal,
            ItemKind = source.ItemKind,
            DisplayName = source.DisplayName,
            Description = source.Description,
            Action = source.Action,
            PlanGuid = source.PlanGuid,
            SetAsDefault = source.SetAsDefault,
            RequireWakePasswordAc = source.RequireWakePasswordAc,
            RequireWakePasswordDc = source.RequireWakePasswordDc,
            TurnOffHardDiskAc = source.TurnOffHardDiskAc,
            TurnOffHardDiskDc = source.TurnOffHardDiskDc,
            SleepAfterAc = source.SleepAfterAc,
            SleepAfterDc = source.SleepAfterDc,
            AllowHybridSleepAc = source.AllowHybridSleepAc,
            AllowHybridSleepDc = source.AllowHybridSleepDc,
            HibernateAfterAc = source.HibernateAfterAc,
            HibernateAfterDc = source.HibernateAfterDc,
            LidCloseAc = source.LidCloseAc,
            LidCloseDc = source.LidCloseDc,
            PowerButtonAc = source.PowerButtonAc,
            PowerButtonDc = source.PowerButtonDc,
            StartMenuPowerAc = source.StartMenuPowerAc,
            StartMenuPowerDc = source.StartMenuPowerDc,
            LinkPowerManagementAc = source.LinkPowerManagementAc,
            LinkPowerManagementDc = source.LinkPowerManagementDc,
            ProcessorMinAc = source.ProcessorMinAc,
            ProcessorMinDc = source.ProcessorMinDc,
            ProcessorMaxAc = source.ProcessorMaxAc,
            ProcessorMaxDc = source.ProcessorMaxDc,
            DisplayOffAc = source.DisplayOffAc,
            DisplayOffDc = source.DisplayOffDc,
            AdaptiveDisplayAc = source.AdaptiveDisplayAc,
            AdaptiveDisplayDc = source.AdaptiveDisplayDc,
            CriticalBatteryActionAc = source.CriticalBatteryActionAc,
            CriticalBatteryActionDc = source.CriticalBatteryActionDc,
            LowBatteryLevelAc = source.LowBatteryLevelAc,
            LowBatteryLevelDc = source.LowBatteryLevelDc,
            CriticalBatteryLevelAc = source.CriticalBatteryLevelAc,
            CriticalBatteryLevelDc = source.CriticalBatteryLevelDc,
            LowBatteryNotificationAc = source.LowBatteryNotificationAc,
            LowBatteryNotificationDc = source.LowBatteryNotificationDc,
            LowBatteryActionAc = source.LowBatteryActionAc,
            LowBatteryActionDc = source.LowBatteryActionDc,
            Disabled = source.Disabled,
            BypassErrors = source.BypassErrors,
            RemoveWhenNoLongerApplied =
                source.RemoveWhenNoLongerApplied,
            RunInUserContext =
                source.RunInUserContext,
            FiltersXml =
                source.FiltersXml
        };

    private static string GppPowerOptionSummary(
        GppPowerOptionItemInfo item) =>
        $"Scope={item.Scope}; Type={item.KindDisplay}; " +
        $"Action={item.ActionDisplay}; Plan={item.DisplayName}; GUID={item.PlanGuid}; " +
        $"Default={item.SetAsDefault}; SleepAC={item.SleepAfterAc}; SleepDC={item.SleepAfterDc}; " +
        $"DisplayAC={item.DisplayOffAc}; DisplayDC={item.DisplayOffDc}; " +
        $"LidAC={item.LidCloseAc}; LidDC={item.LidCloseDc}; " +
        $"ProcessorAC={item.ProcessorMinAc}-{item.ProcessorMaxAc}; " +
        $"ProcessorDC={item.ProcessorMinDc}-{item.ProcessorMaxDc}; " +
        $"ItemDisabled={item.Disabled}; Targeting={item.HasFilters}";
}

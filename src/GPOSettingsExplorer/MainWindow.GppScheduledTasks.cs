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
    private readonly GppScheduledTaskService _gppScheduledTaskService =
        new(new GppDocumentService());

    private readonly ObservableCollection<GppScheduledTaskItemInfo>
        _gppScheduledTasks = new();

    private ICollectionView? _gppScheduledTaskView;
    private CancellationTokenSource? _gppScheduledTaskCancellation;
    private bool _gppScheduledTaskInitialized;

    private void GppScheduledTasksTab_Loaded(
        object sender,
        RoutedEventArgs e)
    {
        if (_gppScheduledTaskInitialized)
            return;

        _gppScheduledTaskInitialized = true;

        _gppScheduledTaskView =
            CollectionViewSource.GetDefaultView(_gppScheduledTasks);

        _gppScheduledTaskView.Filter =
            FilterGppScheduledTask;

        GppScheduledTasksGrid.ItemsSource =
            _gppScheduledTaskView;

        GppScheduledTasksScopeCombo.ItemsSource =
            new[] { "All", "Computer", "User" };

        GppScheduledTasksScopeCombo.SelectedIndex = 0;

        GppScheduledTasksKindCombo.ItemsSource =
            new[] { "All", "Scheduled", "Immediate", "Legacy" };

        GppScheduledTasksKindCombo.SelectedIndex = 0;
    }

    private async void LoadGppScheduledTasks_Click(
        object sender,
        RoutedEventArgs e)
    {
        await LoadGppScheduledTasksAsync();
    }

    private async Task LoadGppScheduledTasksAsync()
    {
        if (_gpos.Count == 0)
            return;

        _gppScheduledTaskCancellation?.Cancel();
        _gppScheduledTaskCancellation = new CancellationTokenSource();

        SetBusy(true, "Loading Scheduled Tasks preferences...");

        try
        {
            var progress = new Progress<string>(message =>
            {
                StatusText.Text = message;
                HeaderStatusText.Text = message;
            });

            var rows = await Task.Run(() =>
                _gppScheduledTaskService.Load(
                    _gpos,
                    progress,
                    _gppScheduledTaskCancellation.Token));

            ReplaceCollection(_gppScheduledTasks, rows);
            _gppScheduledTaskView?.Refresh();

            UpdateGppScheduledTaskCount();

            HeaderStatusText.Text =
                $"{_gpos.Count:N0} GPOs | {_gppScheduledTasks.Count:N0} Scheduled Task items";

            StatusText.Text = "Scheduled Tasks preferences loaded";
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Scheduled Tasks loading canceled";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                ex.Message,
                "Load Scheduled Tasks",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            StatusText.Text = "Scheduled Tasks load failed";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void NewGppScheduledTask_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_domainContext is null || _gpos.Count == 0)
            return;

        var target = new GppScopeTargetWindow(
            _gpos,
            "New Scheduled Task Preference",
            "Select the target GPO and scope:")
        {
            Owner = this
        };

        if (target.ShowDialog() != true ||
            target.SelectedGpo is null)
            return;

        var kindDialog = new InputDialog(
            "Scheduled Task Type",
            "Enter 1 for Scheduled Task or 2 for Immediate Task:",
            "1")
        {
            Owner = this
        };

        if (kindDialog.ShowDialog() != true)
            return;

        var immediate = kindDialog.Value.Trim() == "2";

        var item = _gppScheduledTaskService.CreateNew(
            target.SelectedGpo,
            target.SelectedScope,
            immediate);

        var editor = new GppScheduledTaskEditorWindow(item)
        {
            Owner = this
        };

        if (editor.ShowDialog() == true)
        {
            await SaveGppScheduledTaskAsync(
                editor.Item,
                "Create");
        }
    }

    private async void EditGppScheduledTask_Click(
        object sender,
        RoutedEventArgs e)
    {
        await EditSelectedGppScheduledTaskAsync();
    }

    private async void GppScheduledTasksGrid_MouseDoubleClick(
        object sender,
        MouseButtonEventArgs e)
    {
        await EditSelectedGppScheduledTaskAsync();
    }

    private async Task EditSelectedGppScheduledTaskAsync()
    {
        if (GppScheduledTasksGrid.SelectedItem
            is not GppScheduledTaskItemInfo selected)
            return;

        if (!selected.SupportsStructuredEditing)
        {
            MessageBox.Show(
                this,
                "This is a legacy Scheduled Task preference. Structured rewriting is disabled to avoid damaging legacy XML. Use 'Show raw XML' for controlled editing.",
                "Scheduled Task",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var editable = CopyScheduledTaskItem(selected);

        var editor = new GppScheduledTaskEditorWindow(editable)
        {
            Owner = this
        };

        if (editor.ShowDialog() == true)
        {
            await SaveGppScheduledTaskAsync(
                editor.Item,
                "Edit");
        }
    }

    private async void CloneGppScheduledTask_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (GppScheduledTasksGrid.SelectedItem
            is not GppScheduledTaskItemInfo selected)
            return;

        if (!selected.SupportsStructuredEditing)
        {
            MessageBox.Show(
                this,
                "Legacy Scheduled Task items are not cloned by the structured editor. Use raw XML export/import if needed.",
                "Clone Scheduled Task",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var sourceGpo = _gpos.FirstOrDefault(
            gpo => gpo.Id == selected.GpoId);

        var target = new GppScopeTargetWindow(
            _gpos,
            "Clone Scheduled Task Preference",
            $"Select the destination GPO and scope for '{selected.DisplayName}':",
            sourceGpo,
            selected.Scope)
        {
            Owner = this
        };

        if (target.ShowDialog() != true ||
            target.SelectedGpo is null)
            return;

        if (selected.HasStoredCredential)
        {
            MessageBox.Show(
                this,
                "The source task contains legacy GPP cpassword data. The cloned task will not copy that credential.",
                "Clone Scheduled Task",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        var clone = _gppScheduledTaskService.CreateNew(
            target.SelectedGpo,
            target.SelectedScope,
            selected.IsImmediate);

        _gppScheduledTaskService.CopyEditableValues(
            selected,
            clone,
            preserveCredential: false);

        clone.Uid = Guid.NewGuid()
            .ToString("B")
            .ToUpperInvariant();

        if (!string.IsNullOrWhiteSpace(clone.DisplayName))
            clone.DisplayName += " - Copy";

        var editor = new GppScheduledTaskEditorWindow(clone)
        {
            Owner = this
        };

        if (editor.ShowDialog() == true)
        {
            await SaveGppScheduledTaskAsync(
                editor.Item,
                "Clone");
        }
    }

    private async Task SaveGppScheduledTaskAsync(
        GppScheduledTaskItemInfo item,
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
                "Scheduled Task",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        SetBusy(
            true,
            "Backing up GPO before Scheduled Task change...");

        try
        {
            var backup = await Task.Run(() =>
                _gpmService.BackupGpo(
                    _domainContext.DomainName,
                    gpo.Id,
                    $"Automatic backup before {action.ToLowerInvariant()} Scheduled Task preference"));

            await Task.Run(() =>
                _gppScheduledTaskService.Save(
                    gpo,
                    _domainContext.DomainDistinguishedName,
                    item));

            _auditService.Write(
                action,
                "GPP Scheduled Tasks",
                gpo.DisplayName,
                $"Scope: {item.Scope}; Type: {item.KindDisplay}; " +
                $"Action: {item.ActionDisplay}; Task: {item.DisplayName}; " +
                $"Command: {item.Command}; Trigger: {item.TriggerDisplay}; " +
                $"Targeting: {item.HasFilters}; Backup: {backup}",
                after: GppScheduledTaskSummary(item));

            if (_settings.Count > 0)
                await RefreshSingleGpoSettingsAsync(gpo);

            await LoadGppScheduledTasksAsync();

            if (_gppDocumentInitialized)
                await LoadGppDocumentsAsync();

            StatusText.Text =
                $"{action} Scheduled Task preference completed. Backup: {backup}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                ex.Message,
                "Save Scheduled Task",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            StatusText.Text = "Scheduled Task change failed";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void DeleteGppScheduledTask_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_domainContext is null ||
            GppScheduledTasksGrid.SelectedItem
                is not GppScheduledTaskItemInfo selected)
            return;

        var gpo = _gpos.FirstOrDefault(
            candidate => candidate.Id == selected.GpoId);

        if (gpo is null)
            return;

        if (MessageBox.Show(
                this,
                $"Delete Scheduled Task preference '{selected.DisplayName}'?\n\n" +
                $"{selected.KindDisplay}; {selected.ActionDisplay}; {selected.Command}\n\n" +
                "This removes the preference item from the GPO. A full GPO backup will be created first.",
                "Delete Scheduled Task Preference",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        SetBusy(
            true,
            "Backing up GPO before deleting Scheduled Task preference...");

        try
        {
            var backup = await Task.Run(() =>
                _gpmService.BackupGpo(
                    _domainContext.DomainName,
                    gpo.Id,
                    "Automatic backup before deleting Scheduled Task preference"));

            await Task.Run(() =>
                _gppScheduledTaskService.Delete(
                    gpo,
                    _domainContext.DomainDistinguishedName,
                    selected));

            _auditService.Write(
                "Delete",
                "GPP Scheduled Tasks",
                gpo.DisplayName,
                $"Scope: {selected.Scope}; Type: {selected.KindDisplay}; " +
                $"Task: {selected.DisplayName}; Backup: {backup}",
                before: GppScheduledTaskSummary(selected),
                after: "<Removed>");

            if (_settings.Count > 0)
                await RefreshSingleGpoSettingsAsync(gpo);

            await LoadGppScheduledTasksAsync();

            if (_gppDocumentInitialized)
                await LoadGppDocumentsAsync();

            StatusText.Text =
                $"Scheduled Task preference deleted. Backup: {backup}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                ex.Message,
                "Delete Scheduled Task Preference",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            StatusText.Text = "Scheduled Task preference delete failed";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void ShowGppScheduledTaskRawXml_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (GppScheduledTasksGrid.SelectedItem
            is not GppScheduledTaskItemInfo selected)
            return;

        if (!_gppDocumentInitialized)
        {
            _gppDocumentInitialized = true;

            _gppDocumentView =
                CollectionViewSource.GetDefaultView(_gppDocuments);

            _gppDocumentView.Filter =
                FilterGppDocument;

            GppXmlGrid.ItemsSource =
                _gppDocumentView;

            GppXmlScopeCombo.ItemsSource =
                new[] { "All", "Computer", "User" };

            GppXmlScopeCombo.SelectedIndex = 0;

            var types = new[] { "All" }
                .Concat(
                    _gppDocumentService
                        .GetKnownTypes()
                        .Select(type => type.Name))
                .Distinct(StringComparer.CurrentCultureIgnoreCase)
                .OrderBy(name =>
                    name.Equals("All", StringComparison.OrdinalIgnoreCase)
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
        GppXmlTypeCombo.SelectedItem = "Scheduled Tasks";

        _gppDocumentView?.Refresh();

        StatusText.Text =
            $"GPP XML filter set to Scheduled Tasks for {selected.GpoName}";
    }

    private void GppScheduledTasksSearchBox_TextChanged(
        object sender,
        System.Windows.Controls.TextChangedEventArgs e)
    {
        _gppScheduledTaskView?.Refresh();
        UpdateGppScheduledTaskCount();
    }

    private void GppScheduledTasksFilter_SelectionChanged(
        object sender,
        System.Windows.Controls.SelectionChangedEventArgs e)
    {
        _gppScheduledTaskView?.Refresh();
        UpdateGppScheduledTaskCount();
    }

    private bool FilterGppScheduledTask(object item)
    {
        if (item is not GppScheduledTaskItemInfo task)
            return false;

        var scope = Convert.ToString(
            GppScheduledTasksScopeCombo?.SelectedItem);

        if (!string.IsNullOrWhiteSpace(scope) &&
            !scope.Equals("All", StringComparison.OrdinalIgnoreCase) &&
            !task.Scope.Equals(scope, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var kind = Convert.ToString(
            GppScheduledTasksKindCombo?.SelectedItem);

        if (!string.IsNullOrWhiteSpace(kind) &&
            !kind.Equals("All", StringComparison.OrdinalIgnoreCase))
        {
            var matches = kind switch
            {
                "Immediate" => task.IsImmediate && !task.IsLegacy,
                "Legacy" => task.IsLegacy,
                _ => !task.IsImmediate && !task.IsLegacy
            };

            if (!matches)
                return false;
        }

        var search = GppScheduledTasksSearchBox?
            .Text?
            .Trim();

        return string.IsNullOrWhiteSpace(search) ||
               task.SearchText.Contains(
                   search,
                   StringComparison.CurrentCultureIgnoreCase);
    }

    private void UpdateGppScheduledTaskCount()
    {
        if (GppScheduledTasksCountText is null)
            return;

        var shown = _gppScheduledTaskView?
            .Cast<object>()
            .Count() ?? 0;

        GppScheduledTasksCountText.Text =
            $"{shown:N0} shown / {_gppScheduledTasks.Count:N0} total";
    }

    private static GppScheduledTaskItemInfo CopyScheduledTaskItem(
        GppScheduledTaskItemInfo source) =>
        new()
        {
            GpoId = source.GpoId,
            GpoName = source.GpoName,
            DomainName = source.DomainName,
            Scope = source.Scope,
            XmlPath = source.XmlPath,
            Uid = source.Uid,
            Ordinal = source.Ordinal,
            TaskKind = source.TaskKind,
            DisplayName = source.DisplayName,
            Description = source.Description,
            Action = source.Action,
            RunAs = source.RunAs,
            LogonType = source.LogonType,
            RunLevel = source.RunLevel,
            Author = source.Author,
            Command = source.Command,
            Arguments = source.Arguments,
            WorkingDirectory = source.WorkingDirectory,
            TriggerType = source.TriggerType,
            StartBoundary = source.StartBoundary,
            DaysInterval = source.DaysInterval,
            WeeksInterval = source.WeeksInterval,
            DaysOfWeek = source.DaysOfWeek,
            TriggerDelay = source.TriggerDelay,
            TaskEnabled = source.TaskEnabled,
            Hidden = source.Hidden,
            StartWhenAvailable = source.StartWhenAvailable,
            RunOnlyIfNetworkAvailable = source.RunOnlyIfNetworkAvailable,
            DisallowStartIfOnBatteries = source.DisallowStartIfOnBatteries,
            StopIfGoingOnBatteries = source.StopIfGoingOnBatteries,
            WakeToRun = source.WakeToRun,
            AllowStartOnDemand = source.AllowStartOnDemand,
            MultipleInstancesPolicy = source.MultipleInstancesPolicy,
            ExecutionTimeLimit = source.ExecutionTimeLimit,
            Priority = source.Priority,
            Disabled = source.Disabled,
            BypassErrors = source.BypassErrors,
            RemoveWhenNoLongerApplied = source.RemoveWhenNoLongerApplied,
            RunInUserContext = source.RunInUserContext,
            FiltersXml = source.FiltersXml,
            OpaqueCredential = source.OpaqueCredential,
            TaskXml = source.TaskXml
        };

    private static string GppScheduledTaskSummary(
        GppScheduledTaskItemInfo item) =>
        $"Scope={item.Scope}; Type={item.KindDisplay}; " +
        $"Action={item.ActionDisplay}; Name={item.DisplayName}; " +
        $"RunAs={item.RunAs}; Command={item.Command}; Arguments={item.Arguments}; " +
        $"Trigger={item.TriggerDisplay}; Enabled={item.TaskEnabled}; " +
        $"ItemDisabled={item.Disabled}; Targeting={item.HasFilters}; " +
        $"LegacyCredentialPresent={item.HasStoredCredential}";
}

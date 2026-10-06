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
    private readonly GppFolderOptionsService _gppFolderOptionsService =
        new(new GppDocumentService());

    private readonly ObservableCollection<GppFolderOptionsItemInfo>
        _gppFolderOptions = new();

    private ICollectionView? _gppFolderOptionsView;
    private CancellationTokenSource? _gppFolderOptionsCancellation;
    private bool _gppFolderOptionsInitialized;

    private void GppFolderOptionsTab_Loaded(
        object sender,
        RoutedEventArgs e)
    {
        if (_gppFolderOptionsInitialized)
            return;

        _gppFolderOptionsInitialized = true;

        _gppFolderOptionsView =
            CollectionViewSource.GetDefaultView(_gppFolderOptions);

        _gppFolderOptionsView.Filter =
            FilterGppFolderOption;

        GppFolderOptionsGrid.ItemsSource =
            _gppFolderOptionsView;

        GppFolderOptionsTypeCombo.ItemsSource =
            new[]
            {
                "All",
                "Folder Options (Vista+)",
                "Legacy / associations"
            };

        GppFolderOptionsTypeCombo.SelectedIndex = 0;
    }

    private async void LoadGppFolderOptions_Click(
        object sender,
        RoutedEventArgs e)
    {
        await LoadGppFolderOptionsAsync();
    }

    private async Task LoadGppFolderOptionsAsync()
    {
        if (_gpos.Count == 0)
            return;

        _gppFolderOptionsCancellation?.Cancel();
        _gppFolderOptionsCancellation =
            new CancellationTokenSource();

        SetBusy(true, "Loading Folder Options preferences...");

        try
        {
            var progress = new Progress<string>(
                message =>
                {
                    StatusText.Text = message;
                    HeaderStatusText.Text = message;
                });

            var rows = await Task.Run(() =>
                _gppFolderOptionsService.Load(
                    _gpos,
                    progress,
                    _gppFolderOptionsCancellation.Token));

            ReplaceCollection(_gppFolderOptions, rows);
            _gppFolderOptionsView?.Refresh();
            UpdateGppFolderOptionsCount();

            HeaderStatusText.Text =
                $"{_gpos.Count:N0} GPOs | {_gppFolderOptions.Count:N0} Folder Options items";

            StatusText.Text =
                "Folder Options preferences loaded";
        }
        catch (OperationCanceledException)
        {
            StatusText.Text =
                "Folder Options loading canceled";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                ex.Message,
                "Load Folder Options",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            StatusText.Text =
                "Folder Options load failed";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void NewGppFolderOption_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_domainContext is null || _gpos.Count == 0)
            return;

        var target = new GppScopeTargetWindow(
            _gpos,
            "New Folder Options Preference",
            "Select the target GPO. Global Folder Options are User Configuration preferences.",
            selectedScope: "User")
        {
            Owner = this
        };

        if (target.ShowDialog() != true ||
            target.SelectedGpo is null)
            return;

        if (!target.SelectedScope.Equals(
                "User",
                StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show(
                this,
                "Global Folder Options are supported in User Configuration. Select User scope.",
                "Folder Options",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var item = _gppFolderOptionsService.CreateNew(
            target.SelectedGpo);

        var editor = new GppFolderOptionsEditorWindow(item)
        {
            Owner = this
        };

        if (editor.ShowDialog() == true)
        {
            await SaveGppFolderOptionAsync(
                editor.Item,
                "Create");
        }
    }

    private async void EditGppFolderOption_Click(
        object sender,
        RoutedEventArgs e)
    {
        await EditSelectedGppFolderOptionAsync();
    }

    private async void GppFolderOptionsGrid_MouseDoubleClick(
        object sender,
        MouseButtonEventArgs e)
    {
        await EditSelectedGppFolderOptionAsync();
    }

    private async Task EditSelectedGppFolderOptionAsync()
    {
        if (GppFolderOptionsGrid.SelectedItem
            is not GppFolderOptionsItemInfo selected)
            return;

        if (!selected.SupportsStructuredEditing)
        {
            MessageBox.Show(
                this,
                "This is a legacy Folder Options or file-association item. Structured editing is disabled. Use Show raw XML for controlled editing.",
                "Folder Options",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var editable = CopyFolderOptionItem(selected);

        var editor = new GppFolderOptionsEditorWindow(editable)
        {
            Owner = this
        };

        if (editor.ShowDialog() == true)
        {
            await SaveGppFolderOptionAsync(
                editor.Item,
                "Edit");
        }
    }

    private async void CloneGppFolderOption_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (GppFolderOptionsGrid.SelectedItem
            is not GppFolderOptionsItemInfo selected)
            return;

        if (!selected.SupportsStructuredEditing)
        {
            MessageBox.Show(
                this,
                "Legacy Folder Options/file-association items are not cloned by the structured editor. Use raw XML export/import if needed.",
                "Clone Folder Options",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var sourceGpo = _gpos.FirstOrDefault(
            gpo => gpo.Id == selected.GpoId);

        var target = new GppScopeTargetWindow(
            _gpos,
            "Clone Folder Options Preference",
            $"Select the destination GPO for '{selected.DisplayName}'.",
            sourceGpo,
            "User")
        {
            Owner = this
        };

        if (target.ShowDialog() != true ||
            target.SelectedGpo is null)
            return;

        if (!target.SelectedScope.Equals(
                "User",
                StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show(
                this,
                "Global Folder Options are supported in User Configuration. Select User scope.",
                "Clone Folder Options",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var clone = _gppFolderOptionsService.CreateNew(
            target.SelectedGpo);

        _gppFolderOptionsService.CopyEditableValues(
            selected,
            clone);

        clone.Uid = Guid.NewGuid()
            .ToString("B")
            .ToUpperInvariant();

        if (!string.IsNullOrWhiteSpace(clone.DisplayName))
            clone.DisplayName += " - Copy";

        var editor = new GppFolderOptionsEditorWindow(clone)
        {
            Owner = this
        };

        if (editor.ShowDialog() == true)
        {
            await SaveGppFolderOptionAsync(
                editor.Item,
                "Clone");
        }
    }

    private async Task SaveGppFolderOptionAsync(
        GppFolderOptionsItemInfo item,
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
                "Folder Options",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        SetBusy(
            true,
            "Backing up GPO before Folder Options change...");

        try
        {
            var backup = await Task.Run(() =>
                _gpmService.BackupGpo(
                    _domainContext.DomainName,
                    gpo.Id,
                    $"Automatic backup before {action.ToLowerInvariant()} Folder Options preference"));

            await Task.Run(() =>
                _gppFolderOptionsService.Save(
                    gpo,
                    _domainContext.DomainDistinguishedName,
                    item));

            _auditService.Write(
                action,
                "GPP Folder Options",
                gpo.DisplayName,
                $"Type: {item.KindDisplay}; Hidden: {item.HiddenFilesDisplay}; " +
                $"Hide extensions: {item.HideFileExtensions}; " +
                $"Super hidden: {item.ShowSuperHidden}; Targeting: {item.HasFilters}; " +
                $"Backup: {backup}",
                after: GppFolderOptionSummary(item));

            if (_settings.Count > 0)
                await RefreshSingleGpoSettingsAsync(gpo);

            await LoadGppFolderOptionsAsync();

            if (_gppDocumentInitialized)
                await LoadGppDocumentsAsync();

            StatusText.Text =
                $"{action} Folder Options preference completed. Backup: {backup}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                ex.Message,
                "Save Folder Options",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            StatusText.Text =
                "Folder Options change failed";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void DeleteGppFolderOption_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_domainContext is null ||
            GppFolderOptionsGrid.SelectedItem
                is not GppFolderOptionsItemInfo selected)
            return;

        var gpo = _gpos.FirstOrDefault(
            candidate => candidate.Id == selected.GpoId);

        if (gpo is null)
            return;

        if (MessageBox.Show(
                this,
                $"Delete Folder Options item '{selected.DisplayName}'?\n\n" +
                $"{selected.KindDisplay}; scope: {selected.Scope}\n\n" +
                "A full GPO backup will be created first.",
                "Delete Folder Options Preference",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        SetBusy(
            true,
            "Backing up GPO before deleting Folder Options preference...");

        try
        {
            var backup = await Task.Run(() =>
                _gpmService.BackupGpo(
                    _domainContext.DomainName,
                    gpo.Id,
                    "Automatic backup before deleting Folder Options preference"));

            await Task.Run(() =>
                _gppFolderOptionsService.Delete(
                    gpo,
                    _domainContext.DomainDistinguishedName,
                    selected));

            _auditService.Write(
                "Delete",
                "GPP Folder Options",
                gpo.DisplayName,
                $"Type: {selected.KindDisplay}; Scope: {selected.Scope}; Backup: {backup}",
                before: GppFolderOptionSummary(selected),
                after: "<Removed>");

            if (_settings.Count > 0)
                await RefreshSingleGpoSettingsAsync(gpo);

            await LoadGppFolderOptionsAsync();

            if (_gppDocumentInitialized)
                await LoadGppDocumentsAsync();

            StatusText.Text =
                $"Folder Options preference deleted. Backup: {backup}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                ex.Message,
                "Delete Folder Options Preference",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            StatusText.Text =
                "Folder Options delete failed";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void ShowGppFolderOptionRawXml_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (GppFolderOptionsGrid.SelectedItem
            is not GppFolderOptionsItemInfo selected)
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
        GppXmlTypeCombo.SelectedItem = "Folder Options";
        _gppDocumentView?.Refresh();

        StatusText.Text =
            $"GPP XML filter set to Folder Options for {selected.GpoName}";
    }

    private void GppFolderOptionsSearchBox_TextChanged(
        object sender,
        System.Windows.Controls.TextChangedEventArgs e)
    {
        _gppFolderOptionsView?.Refresh();
        UpdateGppFolderOptionsCount();
    }

    private void GppFolderOptionsType_SelectionChanged(
        object sender,
        System.Windows.Controls.SelectionChangedEventArgs e)
    {
        _gppFolderOptionsView?.Refresh();
        UpdateGppFolderOptionsCount();
    }

    private bool FilterGppFolderOption(object item)
    {
        if (item is not GppFolderOptionsItemInfo option)
            return false;

        var type = Convert.ToString(
            GppFolderOptionsTypeCombo?.SelectedItem);

        if (!string.IsNullOrWhiteSpace(type) &&
            !type.Equals("All", StringComparison.OrdinalIgnoreCase))
        {
            var structured =
                type.Equals(
                    "Folder Options (Vista+)",
                    StringComparison.OrdinalIgnoreCase);

            if (option.SupportsStructuredEditing != structured)
                return false;
        }

        var search =
            GppFolderOptionsSearchBox?.Text?.Trim();

        return string.IsNullOrWhiteSpace(search) ||
               option.SearchText.Contains(
                   search,
                   StringComparison.CurrentCultureIgnoreCase);
    }

    private void UpdateGppFolderOptionsCount()
    {
        if (GppFolderOptionsCountText is null)
            return;

        var shown = _gppFolderOptionsView?
            .Cast<object>()
            .Count() ?? 0;

        GppFolderOptionsCountText.Text =
            $"{shown:N0} shown / {_gppFolderOptions.Count:N0} total";
    }

    private static GppFolderOptionsItemInfo CopyFolderOptionItem(
        GppFolderOptionsItemInfo source) =>
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
            ShowDriveLetter = source.ShowDriveLetter,
            ShowPreviewHandlers = source.ShowPreviewHandlers,
            UseCheckBoxes = source.UseCheckBoxes,
            UseSharingWizard = source.UseSharingWizard,
            AlwaysShowIcons = source.AlwaysShowIcons,
            AlwaysShowMenus = source.AlwaysShowMenus,
            HiddenFiles = source.HiddenFiles,
            DisplayIconThumb = source.DisplayIconThumb,
            DisplayFileSize = source.DisplayFileSize,
            HideFileExtensions = source.HideFileExtensions,
            DisplaySimpleFolders = source.DisplaySimpleFolders,
            ListViewTyping = source.ListViewTyping,
            SeparateProcess = source.SeparateProcess,
            ShowSuperHidden = source.ShowSuperHidden,
            ClassicViewState = source.ClassicViewState,
            PersistBrowsers = source.PersistBrowsers,
            ShowCompressedColor = source.ShowCompressedColor,
            ShowInfoTips = source.ShowInfoTips,
            FullPath = source.FullPath,
            Disabled = source.Disabled,
            BypassErrors = source.BypassErrors,
            RemoveWhenNoLongerApplied =
                source.RemoveWhenNoLongerApplied,
            RunInUserContext =
                source.RunInUserContext,
            FiltersXml = source.FiltersXml
        };

    private static string GppFolderOptionSummary(
        GppFolderOptionsItemInfo item) =>
        $"Scope={item.Scope}; Type={item.KindDisplay}; " +
        $"Hidden={item.HiddenFiles}; HideFileExt={item.HideFileExtensions}; " +
        $"ShowSuperHidden={item.ShowSuperHidden}; CheckBoxes={item.UseCheckBoxes}; " +
        $"SharingWizard={item.UseSharingWizard}; SeparateProcess={item.SeparateProcess}; " +
        $"ItemDisabled={item.Disabled}; Targeting={item.HasFilters}";
}

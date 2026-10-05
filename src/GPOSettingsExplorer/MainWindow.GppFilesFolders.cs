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
    private readonly GppFilesFoldersService _gppFilesFoldersService =
        new(new GppDocumentService());

    private readonly ObservableCollection<GppFileItemInfo> _gppFileItems = new();
    private readonly ObservableCollection<GppFolderItemInfo> _gppFolderItems = new();

    private ICollectionView? _gppFileView;
    private ICollectionView? _gppFolderView;
    private CancellationTokenSource? _gppFilesFoldersCancellation;
    private bool _gppFilesFoldersInitialized;

    private void GppFilesFoldersTab_Loaded(object sender, RoutedEventArgs e)
    {
        if (_gppFilesFoldersInitialized)
            return;

        _gppFilesFoldersInitialized = true;

        _gppFileView = CollectionViewSource.GetDefaultView(_gppFileItems);
        _gppFileView.Filter = FilterGppFile;
        GppFilesGrid.ItemsSource = _gppFileView;

        _gppFolderView = CollectionViewSource.GetDefaultView(_gppFolderItems);
        _gppFolderView.Filter = FilterGppFolder;
        GppFoldersGrid.ItemsSource = _gppFolderView;

        GppFilesScopeCombo.ItemsSource = new[] { "All", "Computer", "User" };
        GppFilesScopeCombo.SelectedIndex = 0;

        GppFoldersScopeCombo.ItemsSource = new[] { "All", "Computer", "User" };
        GppFoldersScopeCombo.SelectedIndex = 0;
    }

    private async void LoadGppFilesFolders_Click(object sender, RoutedEventArgs e)
    {
        await LoadGppFilesFoldersAsync();
    }

    private async Task LoadGppFilesFoldersAsync()
    {
        if (_gpos.Count == 0)
            return;

        _gppFilesFoldersCancellation?.Cancel();
        _gppFilesFoldersCancellation = new CancellationTokenSource();

        SetBusy(true, "Loading Files and Folders preferences...");

        try
        {
            var progress = new Progress<string>(message =>
            {
                StatusText.Text = message;
                HeaderStatusText.Text = message;
            });

            var token = _gppFilesFoldersCancellation.Token;

            var files = await Task.Run(() =>
                _gppFilesFoldersService.LoadFiles(
                    _gpos,
                    progress,
                    token));

            token.ThrowIfCancellationRequested();

            var folders = await Task.Run(() =>
                _gppFilesFoldersService.LoadFolders(
                    _gpos,
                    progress,
                    token));

            ReplaceCollection(_gppFileItems, files);
            ReplaceCollection(_gppFolderItems, folders);

            _gppFileView?.Refresh();
            _gppFolderView?.Refresh();

            UpdateGppFileCount();
            UpdateGppFolderCount();

            HeaderStatusText.Text =
                $"{_gpos.Count:N0} GPOs | {_gppFileItems.Count:N0} Files | {_gppFolderItems.Count:N0} Folders";
            StatusText.Text = "Files and Folders preferences loaded";
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Files and Folders loading canceled";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                ex.Message,
                "Load Files and Folders",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            StatusText.Text = "Files and Folders load failed";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void NewGppFile_Click(object sender, RoutedEventArgs e)
    {
        if (_domainContext is null || _gpos.Count == 0)
            return;

        var target = new GppScopeTargetWindow(
            _gpos,
            "New File Preference",
            "Select the target GPO and scope for this Files preference:",
            selectedScope: "Computer")
        {
            Owner = this
        };

        if (target.ShowDialog() != true || target.SelectedGpo is null)
            return;

        var item = _gppFilesFoldersService.CreateNewFile(
            target.SelectedGpo,
            target.SelectedScope);

        var editor = new GppFileEditorWindow(item)
        {
            Owner = this
        };

        if (editor.ShowDialog() == true)
            await SaveGppFileAsync(editor.Item, "Create");
    }

    private async void EditGppFile_Click(object sender, RoutedEventArgs e)
    {
        await EditSelectedGppFileAsync();
    }

    private async void GppFilesGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        await EditSelectedGppFileAsync();
    }

    private async Task EditSelectedGppFileAsync()
    {
        if (GppFilesGrid.SelectedItem is not GppFileItemInfo selected)
            return;

        var editable = CopyFileItem(selected);

        var editor = new GppFileEditorWindow(editable)
        {
            Owner = this
        };

        if (editor.ShowDialog() == true)
            await SaveGppFileAsync(editor.Item, "Edit");
    }

    private async void CloneGppFile_Click(object sender, RoutedEventArgs e)
    {
        if (GppFilesGrid.SelectedItem is not GppFileItemInfo selected)
            return;

        var sourceGpo = _gpos.FirstOrDefault(gpo => gpo.Id == selected.GpoId);

        var target = new GppScopeTargetWindow(
            _gpos,
            "Clone File Preference",
            "Select the destination GPO and scope for the cloned Files preference:",
            sourceGpo,
            selected.Scope)
        {
            Owner = this
        };

        if (target.ShowDialog() != true || target.SelectedGpo is null)
            return;

        var clone = _gppFilesFoldersService.CreateNewFile(
            target.SelectedGpo,
            target.SelectedScope);

        _gppFilesFoldersService.CopyFileValues(selected, clone);
        clone.Uid = Guid.NewGuid().ToString("B").ToUpperInvariant();

        if (!string.IsNullOrWhiteSpace(clone.DisplayName))
            clone.DisplayName += " - Copy";

        var editor = new GppFileEditorWindow(clone)
        {
            Owner = this
        };

        if (editor.ShowDialog() == true)
            await SaveGppFileAsync(editor.Item, "Clone");
    }

    private async Task SaveGppFileAsync(
        GppFileItemInfo item,
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
                "Files",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        SetBusy(true, "Backing up GPO before Files change...");

        try
        {
            var backup = await Task.Run(() =>
                _gpmService.BackupGpo(
                    _domainContext.DomainName,
                    gpo.Id,
                    $"Automatic backup before {action.ToLowerInvariant()} Files preference"));

            await Task.Run(() =>
                _gppFilesFoldersService.SaveFile(
                    gpo,
                    _domainContext.DomainDistinguishedName,
                    item));

            _auditService.Write(
                action,
                "GPP Files",
                gpo.DisplayName,
                $"Scope: {item.Scope}; Action: {item.ActionDisplay}; " +
                $"Source: {item.FromPath}; Target: {item.TargetPath}; " +
                $"Targeting: {item.HasFilters}; Backup: {backup}",
                after: GppFileSummary(item));

            if (_settings.Count > 0)
                await RefreshSingleGpoSettingsAsync(gpo);

            await LoadGppFilesFoldersAsync();

            if (_gppDocumentInitialized)
                await LoadGppDocumentsAsync();

            StatusText.Text =
                $"{action} Files preference completed. Backup: {backup}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                ex.Message,
                "Save File Preference",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            StatusText.Text = "Files change failed";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void DeleteGppFile_Click(object sender, RoutedEventArgs e)
    {
        if (_domainContext is null ||
            GppFilesGrid.SelectedItem is not GppFileItemInfo selected)
            return;

        var gpo = _gpos.FirstOrDefault(candidate => candidate.Id == selected.GpoId);
        if (gpo is null)
            return;

        if (MessageBox.Show(
                this,
                $"Delete Files preference '{selected.DisplayName}'?\n\n" +
                $"{selected.ActionDisplay}: {selected.FromPath} -> {selected.TargetPath}\n\n" +
                "A full GPO backup will be created first.",
                "Delete File Preference",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        SetBusy(true, "Backing up GPO before deleting Files preference...");

        try
        {
            var backup = await Task.Run(() =>
                _gpmService.BackupGpo(
                    _domainContext.DomainName,
                    gpo.Id,
                    "Automatic backup before deleting Files preference"));

            await Task.Run(() =>
                _gppFilesFoldersService.DeleteFile(
                    gpo,
                    _domainContext.DomainDistinguishedName,
                    selected));

            _auditService.Write(
                "Delete",
                "GPP Files",
                gpo.DisplayName,
                $"Scope: {selected.Scope}; Target: {selected.TargetPath}; Backup: {backup}",
                before: GppFileSummary(selected),
                after: "<Removed>");

            if (_settings.Count > 0)
                await RefreshSingleGpoSettingsAsync(gpo);

            await LoadGppFilesFoldersAsync();

            if (_gppDocumentInitialized)
                await LoadGppDocumentsAsync();

            StatusText.Text = $"File preference deleted. Backup: {backup}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                ex.Message,
                "Delete File Preference",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            StatusText.Text = "Files delete failed";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void NewGppFolder_Click(object sender, RoutedEventArgs e)
    {
        if (_domainContext is null || _gpos.Count == 0)
            return;

        var target = new GppScopeTargetWindow(
            _gpos,
            "New Folder Preference",
            "Select the target GPO and scope for this Folders preference:",
            selectedScope: "Computer")
        {
            Owner = this
        };

        if (target.ShowDialog() != true || target.SelectedGpo is null)
            return;

        var item = _gppFilesFoldersService.CreateNewFolder(
            target.SelectedGpo,
            target.SelectedScope);

        var editor = new GppFolderEditorWindow(item)
        {
            Owner = this
        };

        if (editor.ShowDialog() == true)
            await SaveGppFolderAsync(editor.Item, "Create");
    }

    private async void EditGppFolder_Click(object sender, RoutedEventArgs e)
    {
        await EditSelectedGppFolderAsync();
    }

    private async void GppFoldersGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        await EditSelectedGppFolderAsync();
    }

    private async Task EditSelectedGppFolderAsync()
    {
        if (GppFoldersGrid.SelectedItem is not GppFolderItemInfo selected)
            return;

        var editable = CopyFolderItem(selected);

        var editor = new GppFolderEditorWindow(editable)
        {
            Owner = this
        };

        if (editor.ShowDialog() == true)
            await SaveGppFolderAsync(editor.Item, "Edit");
    }

    private async void CloneGppFolder_Click(object sender, RoutedEventArgs e)
    {
        if (GppFoldersGrid.SelectedItem is not GppFolderItemInfo selected)
            return;

        var sourceGpo = _gpos.FirstOrDefault(gpo => gpo.Id == selected.GpoId);

        var target = new GppScopeTargetWindow(
            _gpos,
            "Clone Folder Preference",
            "Select the destination GPO and scope for the cloned Folders preference:",
            sourceGpo,
            selected.Scope)
        {
            Owner = this
        };

        if (target.ShowDialog() != true || target.SelectedGpo is null)
            return;

        var clone = _gppFilesFoldersService.CreateNewFolder(
            target.SelectedGpo,
            target.SelectedScope);

        _gppFilesFoldersService.CopyFolderValues(selected, clone);
        clone.Uid = Guid.NewGuid().ToString("B").ToUpperInvariant();

        if (!string.IsNullOrWhiteSpace(clone.DisplayName))
            clone.DisplayName += " - Copy";

        var editor = new GppFolderEditorWindow(clone)
        {
            Owner = this
        };

        if (editor.ShowDialog() == true)
            await SaveGppFolderAsync(editor.Item, "Clone");
    }

    private async Task SaveGppFolderAsync(
        GppFolderItemInfo item,
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
                "Folders",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        SetBusy(true, "Backing up GPO before Folders change...");

        try
        {
            var backup = await Task.Run(() =>
                _gpmService.BackupGpo(
                    _domainContext.DomainName,
                    gpo.Id,
                    $"Automatic backup before {action.ToLowerInvariant()} Folders preference"));

            await Task.Run(() =>
                _gppFilesFoldersService.SaveFolder(
                    gpo,
                    _domainContext.DomainDistinguishedName,
                    item));

            _auditService.Write(
                action,
                "GPP Folders",
                gpo.DisplayName,
                $"Scope: {item.Scope}; Action: {item.ActionDisplay}; Path: {item.Path}; " +
                $"Targeting: {item.HasFilters}; Backup: {backup}",
                after: GppFolderSummary(item));

            if (_settings.Count > 0)
                await RefreshSingleGpoSettingsAsync(gpo);

            await LoadGppFilesFoldersAsync();

            if (_gppDocumentInitialized)
                await LoadGppDocumentsAsync();

            StatusText.Text =
                $"{action} Folders preference completed. Backup: {backup}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                ex.Message,
                "Save Folder Preference",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            StatusText.Text = "Folders change failed";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void DeleteGppFolder_Click(object sender, RoutedEventArgs e)
    {
        if (_domainContext is null ||
            GppFoldersGrid.SelectedItem is not GppFolderItemInfo selected)
            return;

        var gpo = _gpos.FirstOrDefault(candidate => candidate.Id == selected.GpoId);
        if (gpo is null)
            return;

        if (MessageBox.Show(
                this,
                $"Delete Folders preference '{selected.DisplayName}'?\n\n" +
                $"{selected.ActionDisplay}: {selected.Path}\n\n" +
                "A full GPO backup will be created first.",
                "Delete Folder Preference",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        SetBusy(true, "Backing up GPO before deleting Folders preference...");

        try
        {
            var backup = await Task.Run(() =>
                _gpmService.BackupGpo(
                    _domainContext.DomainName,
                    gpo.Id,
                    "Automatic backup before deleting Folders preference"));

            await Task.Run(() =>
                _gppFilesFoldersService.DeleteFolder(
                    gpo,
                    _domainContext.DomainDistinguishedName,
                    selected));

            _auditService.Write(
                "Delete",
                "GPP Folders",
                gpo.DisplayName,
                $"Scope: {selected.Scope}; Path: {selected.Path}; Backup: {backup}",
                before: GppFolderSummary(selected),
                after: "<Removed>");

            if (_settings.Count > 0)
                await RefreshSingleGpoSettingsAsync(gpo);

            await LoadGppFilesFoldersAsync();

            if (_gppDocumentInitialized)
                await LoadGppDocumentsAsync();

            StatusText.Text = $"Folder preference deleted. Backup: {backup}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                ex.Message,
                "Delete Folder Preference",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            StatusText.Text = "Folders delete failed";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void ShowGppFileRawXml_Click(object sender, RoutedEventArgs e)
    {
        if (GppFilesGrid.SelectedItem is not GppFileItemInfo selected)
            return;

        await ShowGppFilesFoldersRawXmlAsync(
            selected.GpoName,
            selected.Scope,
            "Files");
    }

    private async void ShowGppFolderRawXml_Click(object sender, RoutedEventArgs e)
    {
        if (GppFoldersGrid.SelectedItem is not GppFolderItemInfo selected)
            return;

        await ShowGppFilesFoldersRawXmlAsync(
            selected.GpoName,
            selected.Scope,
            "Folders");
    }

    private async Task ShowGppFilesFoldersRawXmlAsync(
        string gpoName,
        string scope,
        string typeName)
    {
        if (!_gppDocumentInitialized)
        {
            _gppDocumentInitialized = true;

            _gppDocumentView = CollectionViewSource.GetDefaultView(_gppDocuments);
            _gppDocumentView.Filter = FilterGppDocument;
            GppXmlGrid.ItemsSource = _gppDocumentView;

            GppXmlScopeCombo.ItemsSource = new[] { "All", "Computer", "User" };
            GppXmlScopeCombo.SelectedIndex = 0;

            var types = new[] { "All" }
                .Concat(_gppDocumentService.GetKnownTypes().Select(type => type.Name))
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

        GppXmlSearchBox.Text = gpoName;
        GppXmlScopeCombo.SelectedItem = scope;
        GppXmlTypeCombo.SelectedItem = typeName;
        _gppDocumentView?.Refresh();

        StatusText.Text =
            $"GPP XML filter set to {typeName} for {gpoName}";
    }

    private void GppFilesSearchBox_TextChanged(
        object sender,
        System.Windows.Controls.TextChangedEventArgs e)
    {
        _gppFileView?.Refresh();
        UpdateGppFileCount();
    }

    private void GppFilesScopeCombo_SelectionChanged(
        object sender,
        System.Windows.Controls.SelectionChangedEventArgs e)
    {
        _gppFileView?.Refresh();
        UpdateGppFileCount();
    }

    private bool FilterGppFile(object item)
    {
        if (item is not GppFileItemInfo file)
            return false;

        var scope = Convert.ToString(GppFilesScopeCombo?.SelectedItem);
        if (!string.IsNullOrWhiteSpace(scope) &&
            !scope.Equals("All", StringComparison.OrdinalIgnoreCase) &&
            !file.Scope.Equals(scope, StringComparison.OrdinalIgnoreCase))
            return false;

        var search = GppFilesSearchBox?.Text?.Trim();
        return string.IsNullOrWhiteSpace(search) ||
               file.SearchText.Contains(
                   search,
                   StringComparison.CurrentCultureIgnoreCase);
    }

    private void UpdateGppFileCount()
    {
        if (GppFilesCountText is null)
            return;

        var shown = _gppFileView?.Cast<object>().Count() ?? 0;
        GppFilesCountText.Text =
            $"{shown:N0} shown / {_gppFileItems.Count:N0} total";
    }

    private void GppFoldersSearchBox_TextChanged(
        object sender,
        System.Windows.Controls.TextChangedEventArgs e)
    {
        _gppFolderView?.Refresh();
        UpdateGppFolderCount();
    }

    private void GppFoldersScopeCombo_SelectionChanged(
        object sender,
        System.Windows.Controls.SelectionChangedEventArgs e)
    {
        _gppFolderView?.Refresh();
        UpdateGppFolderCount();
    }

    private bool FilterGppFolder(object item)
    {
        if (item is not GppFolderItemInfo folder)
            return false;

        var scope = Convert.ToString(GppFoldersScopeCombo?.SelectedItem);
        if (!string.IsNullOrWhiteSpace(scope) &&
            !scope.Equals("All", StringComparison.OrdinalIgnoreCase) &&
            !folder.Scope.Equals(scope, StringComparison.OrdinalIgnoreCase))
            return false;

        var search = GppFoldersSearchBox?.Text?.Trim();
        return string.IsNullOrWhiteSpace(search) ||
               folder.SearchText.Contains(
                   search,
                   StringComparison.CurrentCultureIgnoreCase);
    }

    private void UpdateGppFolderCount()
    {
        if (GppFoldersCountText is null)
            return;

        var shown = _gppFolderView?.Cast<object>().Count() ?? 0;
        GppFoldersCountText.Text =
            $"{shown:N0} shown / {_gppFolderItems.Count:N0} total";
    }

    private static GppFileItemInfo CopyFileItem(GppFileItemInfo source) =>
        new()
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
            FromPath = source.FromPath,
            TargetPath = source.TargetPath,
            SuppressErrors = source.SuppressErrors,
            ReadOnly = source.ReadOnly,
            Archive = source.Archive,
            Hidden = source.Hidden,
            Disabled = source.Disabled,
            BypassErrors = source.BypassErrors,
            RemoveWhenNoLongerApplied = source.RemoveWhenNoLongerApplied,
            RunInUserContext = source.RunInUserContext,
            FiltersXml = source.FiltersXml
        };

    private static GppFolderItemInfo CopyFolderItem(GppFolderItemInfo source) =>
        new()
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
            Path = source.Path,
            ReadOnly = source.ReadOnly,
            Archive = source.Archive,
            Hidden = source.Hidden,
            DeleteIgnoreErrors = source.DeleteIgnoreErrors,
            DeleteReadOnly = source.DeleteReadOnly,
            DeleteFiles = source.DeleteFiles,
            DeleteSubFolders = source.DeleteSubFolders,
            DeleteFolder = source.DeleteFolder,
            Disabled = source.Disabled,
            BypassErrors = source.BypassErrors,
            RemoveWhenNoLongerApplied = source.RemoveWhenNoLongerApplied,
            RunInUserContext = source.RunInUserContext,
            FiltersXml = source.FiltersXml
        };

    private static string GppFileSummary(GppFileItemInfo item) =>
        $"Scope={item.Scope}; Action={item.ActionDisplay}; " +
        $"Source={item.FromPath}; Target={item.TargetPath}; " +
        $"ReadOnly={item.ReadOnly}; Archive={item.Archive}; Hidden={item.Hidden}; " +
        $"Disabled={item.Disabled}; Targeting={item.HasFilters}";

    private static string GppFolderSummary(GppFolderItemInfo item) =>
        $"Scope={item.Scope}; Action={item.ActionDisplay}; Path={item.Path}; " +
        $"ReadOnly={item.ReadOnly}; Archive={item.Archive}; Hidden={item.Hidden}; " +
        $"DeleteFiles={item.DeleteFiles}; DeleteSubFolders={item.DeleteSubFolders}; " +
        $"DeleteFolder={item.DeleteFolder}; Disabled={item.Disabled}; " +
        $"Targeting={item.HasFilters}";
}

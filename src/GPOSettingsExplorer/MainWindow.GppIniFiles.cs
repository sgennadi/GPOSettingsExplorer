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
    private readonly GppIniFileService _gppIniFileService =
        new(new GppDocumentService());

    private readonly ObservableCollection<GppIniFileItemInfo>
        _gppIniFileItems = new();

    private ICollectionView? _gppIniFileView;
    private CancellationTokenSource? _gppIniFileCancellation;
    private bool _gppIniFileInitialized;

    private void GppIniFilesTab_Loaded(
        object sender,
        RoutedEventArgs e)
    {
        if (_gppIniFileInitialized)
            return;

        try
        {
            _gppIniFileView =
                CollectionViewSource.GetDefaultView(
                    _gppIniFileItems);

            _gppIniFileView.Filter =
                FilterGppIniFile;

            GppIniFilesGrid.ItemsSource =
                _gppIniFileView;

            GppIniFilesScopeCombo.ItemsSource =
                new[]
                {
                    "All",
                    "Computer",
                    "User"
                };

            GppIniFilesScopeCombo.SelectedIndex =
                0;

            _gppIniFileInitialized =
                true;
        }
        catch (Exception ex)
        {
            var log =
                CrashLogService.Write(
                    "Initialize GPP INI Files tab",
                    ex);

            StatusText.Text =
                "INI Files tab initialization failed";

            MessageBox.Show(
                this,
                BuildRecoverableErrorMessage(
                    "The INI Files tab could not be initialized.",
                    ex,
                    log),
                "GPP INI Files",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async void LoadGppIniFiles_Click(
        object sender,
        RoutedEventArgs e)
    {
        try
        {
            await LoadGppIniFilesAsync();
        }
        catch (Exception ex)
        {
            HandleIniFilesUnexpectedError(
                "Load INI Files command",
                ex);
        }
    }

    private async Task LoadGppIniFilesAsync()
    {
        if (_gpos.Count == 0)
            return;

        if (!_gppIniFileInitialized)
        {
            GppIniFilesTab_Loaded(
                this,
                new RoutedEventArgs());

            if (!_gppIniFileInitialized)
                return;
        }

        var previous =
            _gppIniFileCancellation;

        var current =
            new CancellationTokenSource();

        _gppIniFileCancellation =
            current;

        try
        {
            try
            {
                previous?.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }

            SetBusy(
                true,
                "Loading INI Files preferences...");

            var progress =
                new Progress<string>(
                    message =>
                    {
                        if (!ReferenceEquals(
                                _gppIniFileCancellation,
                                current))
                        {
                            return;
                        }

                        StatusText.Text =
                            message;
                        HeaderStatusText.Text =
                            message;
                    });

            var rows =
                await Task.Run(
                    () =>
                        _gppIniFileService.Load(
                            _gpos.ToArray(),
                            progress,
                            current.Token),
                    current.Token);

            current.Token.ThrowIfCancellationRequested();

            if (!ReferenceEquals(
                    _gppIniFileCancellation,
                    current))
            {
                return;
            }

            ReplaceCollection(
                _gppIniFileItems,
                rows);

            _gppIniFileView?.Refresh();

            UpdateGppIniFileCount();

            HeaderStatusText.Text =
                $"{_gpos.Count:N0} GPOs | {_gppIniFileItems.Count:N0} INI File items";

            StatusText.Text =
                "INI Files preferences loaded";
        }
        catch (OperationCanceledException)
        {
            if (ReferenceEquals(
                    _gppIniFileCancellation,
                    current))
            {
                StatusText.Text =
                    "INI Files loading canceled";
            }
        }
        catch (Exception ex)
        {
            var log =
                CrashLogService.Write(
                    "Load GPP INI Files",
                    ex);

            if (ReferenceEquals(
                    _gppIniFileCancellation,
                    current))
            {
                StatusText.Text =
                    "INI Files load failed";

                ErrorDialog.Show(
                    this,
                    "Load INI Files",
                    "INI Files could not be loaded. The application will stay open.",
                    ex,
                    log);
            }
        }
        finally
        {
            if (ReferenceEquals(
                    _gppIniFileCancellation,
                    current))
            {
                _gppIniFileCancellation =
                    null;

                SetBusy(false);
            }

            current.Dispose();
            previous?.Dispose();
        }
    }

    private async void NewGppIniFile_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_domainContext is null ||
            _gpos.Count == 0)
            return;

        var target =
            new GppScopeTargetWindow(
                _gpos,
                "New INI File Preference",
                "Select the target GPO and whether this INI/INF preference belongs to Computer or User Configuration:")
            {
                Owner = this
            };

        if (target.ShowDialog() != true ||
            target.SelectedGpo is null)
            return;

        var item =
            _gppIniFileService.CreateNew(
                target.SelectedGpo,
                target.SelectedScope);

        var editor =
            new GppIniFileEditorWindow(
                item)
            {
                Owner = this
            };

        if (editor.ShowDialog() == true)
        {
            await SaveGppIniFileAsync(
                editor.Item,
                "Create");
        }
    }

    private async void EditGppIniFile_Click(
        object sender,
        RoutedEventArgs e)
    {
        await EditSelectedGppIniFileAsync();
    }

    private async void GppIniFilesGrid_MouseDoubleClick(
        object sender,
        MouseButtonEventArgs e)
    {
        await EditSelectedGppIniFileAsync();
    }

    private async Task EditSelectedGppIniFileAsync()
    {
        if (GppIniFilesGrid.SelectedItem
            is not GppIniFileItemInfo selected)
            return;

        var editor =
            new GppIniFileEditorWindow(
                CopyIniFileItem(
                    selected))
            {
                Owner = this
            };

        if (editor.ShowDialog() == true)
        {
            await SaveGppIniFileAsync(
                editor.Item,
                "Edit");
        }
    }

    private async void CloneGppIniFile_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (GppIniFilesGrid.SelectedItem
            is not GppIniFileItemInfo selected)
            return;

        var sourceGpo =
            _gpos.FirstOrDefault(
                gpo =>
                    gpo.Id == selected.GpoId);

        var target =
            new GppScopeTargetWindow(
                _gpos,
                "Clone INI File Preference",
                "Select the destination GPO and scope for the cloned INI/INF preference:",
                sourceGpo,
                selected.Scope)
            {
                Owner = this
            };

        if (target.ShowDialog() != true ||
            target.SelectedGpo is null)
            return;

        var clone =
            _gppIniFileService.CreateNew(
                target.SelectedGpo,
                target.SelectedScope);

        _gppIniFileService.CopyEditableValues(
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
            new GppIniFileEditorWindow(
                clone)
            {
                Owner = this
            };

        if (editor.ShowDialog() == true)
        {
            await SaveGppIniFileAsync(
                editor.Item,
                "Clone");
        }
    }

    private async Task SaveGppIniFileAsync(
        GppIniFileItemInfo item,
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
                "INI File",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return;
        }

        SetBusy(
            true,
            "Backing up GPO before INI File change...");

        try
        {
            var backup =
                await Task.Run(
                    () =>
                        _gpmService.BackupGpo(
                            _domainContext.DomainName,
                            gpo.Id,
                            $"Automatic backup before {action.ToLowerInvariant()} INI File preference"));

            await Task.Run(
                () =>
                    _gppIniFileService.Save(
                        gpo,
                        _domainContext.DomainDistinguishedName,
                        item));

            _auditService.Write(
                action,
                "GPP INI Files",
                gpo.DisplayName,
                $"Scope: {item.Scope}; Action: {item.ActionDisplay}; " +
                $"Path: {item.Path}; Section: {item.Section}; Property: {item.Property}; " +
                $"Delete target: {item.DeleteTargetDisplay}; Targeting: {item.HasFilters}; " +
                $"Backup: {backup}",
                after:
                    GppIniFileSummary(
                        item));

            if (_settings.Count > 0)
            {
                await RefreshSingleGpoSettingsAsync(
                    gpo);
            }

            await LoadGppIniFilesAsync();

            if (_gppDocumentInitialized)
            {
                await LoadGppDocumentsAsync();
            }

            StatusText.Text =
                $"{action} INI File preference completed. Backup: {backup}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                ex.Message,
                "Save INI File",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            StatusText.Text =
                "INI File change failed";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void DeleteGppIniFile_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_domainContext is null ||
            GppIniFilesGrid.SelectedItem
                is not GppIniFileItemInfo selected)
            return;

        var gpo =
            _gpos.FirstOrDefault(
                candidate =>
                    candidate.Id == selected.GpoId);

        if (gpo is null)
            return;

        if (MessageBox.Show(
                this,
                $"Delete INI File preference '{selected.DisplayName}'?\n\n" +
                $"{selected.ActionDisplay}: {selected.Path}\n" +
                $"Section: {selected.Section}\nProperty: {selected.Property}\n\n" +
                "This removes the preference item from the GPO. It does not immediately delete the client file. A full GPO backup will be created first.",
                "Delete INI File Preference",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) !=
            MessageBoxResult.Yes)
        {
            return;
        }

        SetBusy(
            true,
            "Backing up GPO before deleting INI File preference...");

        try
        {
            var backup =
                await Task.Run(
                    () =>
                        _gpmService.BackupGpo(
                            _domainContext.DomainName,
                            gpo.Id,
                            "Automatic backup before deleting INI File preference"));

            await Task.Run(
                () =>
                    _gppIniFileService.Delete(
                        gpo,
                        _domainContext.DomainDistinguishedName,
                        selected));

            _auditService.Write(
                "Delete",
                "GPP INI Files",
                gpo.DisplayName,
                $"Scope: {selected.Scope}; Path: {selected.Path}; " +
                $"Section: {selected.Section}; Property: {selected.Property}; Backup: {backup}",
                before:
                    GppIniFileSummary(
                        selected),
                after:
                    "<Removed>");

            if (_settings.Count > 0)
            {
                await RefreshSingleGpoSettingsAsync(
                    gpo);
            }

            await LoadGppIniFilesAsync();

            if (_gppDocumentInitialized)
            {
                await LoadGppDocumentsAsync();
            }

            StatusText.Text =
                $"INI File preference deleted. Backup: {backup}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                ex.Message,
                "Delete INI File Preference",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            StatusText.Text =
                "INI File preference delete failed";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void ShowGppIniFileRawXml_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (GppIniFilesGrid.SelectedItem
            is not GppIniFileItemInfo selected)
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
            "INI Files";

        _gppDocumentView?.Refresh();

        StatusText.Text =
            $"GPP XML filter set to INI Files for {selected.GpoName}";
    }

    private void GppIniFilesSearchBox_TextChanged(
        object sender,
        System.Windows.Controls.TextChangedEventArgs e)
    {
        _gppIniFileView?.Refresh();
        UpdateGppIniFileCount();
    }

    private void GppIniFilesScopeCombo_SelectionChanged(
        object sender,
        System.Windows.Controls.SelectionChangedEventArgs e)
    {
        _gppIniFileView?.Refresh();
        UpdateGppIniFileCount();
    }

    private bool FilterGppIniFile(
        object item)
    {
        if (item is not GppIniFileItemInfo ini)
            return false;

        var scope =
            Convert.ToString(
                GppIniFilesScopeCombo?.SelectedItem);

        if (!string.IsNullOrWhiteSpace(scope) &&
            !scope.Equals(
                "All",
                StringComparison.OrdinalIgnoreCase) &&
            !ini.Scope.Equals(
                scope,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var search =
            GppIniFilesSearchBox?
                .Text?
                .Trim();

        return string.IsNullOrWhiteSpace(search) ||
               ini.SearchText.Contains(
                   search,
                   StringComparison.CurrentCultureIgnoreCase);
    }

    private void UpdateGppIniFileCount()
    {
        if (GppIniFilesCountText is null)
            return;

        var shown =
            _gppIniFileView?
                .Cast<object>()
                .Count()
            ?? 0;

        GppIniFilesCountText.Text =
            $"{shown:N0} shown / {_gppIniFileItems.Count:N0} total";
    }

    private static GppIniFileItemInfo CopyIniFileItem(
        GppIniFileItemInfo source) =>
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
            Description = source.Description,
            Action = source.Action,
            Path = source.Path,
            Section = source.Section,
            Property = source.Property,
            Value = source.Value,
            Disabled = source.Disabled,
            BypassErrors = source.BypassErrors,
            RemoveWhenNoLongerApplied =
                source.RemoveWhenNoLongerApplied,
            RunInUserContext = source.RunInUserContext,
            FiltersXml = source.FiltersXml
        };

    private void HandleIniFilesUnexpectedError(
        string context,
        Exception ex)
    {
        var log =
            CrashLogService.Write(
                context,
                ex);

        StatusText.Text =
            "INI Files operation failed";

        ErrorDialog.Show(
            this,
            "GPP INI Files",
            "The INI Files operation failed. The application will stay open.",
            ex,
            log);
    }

    private static string BuildRecoverableErrorMessage(
        string message,
        Exception ex,
        string log)
    {
        var result =
            message +
            "\n\n" +
            ex.Message;

        if (!string.IsNullOrWhiteSpace(
                log))
        {
            result +=
                "\n\nDiagnostic log:\n" +
                log;
        }

        return result;
    }

    private static string GppIniFileSummary(
        GppIniFileItemInfo item) =>
        $"Scope={item.Scope}; Action={item.ActionDisplay}; Path={item.Path}; " +
        $"Section={item.Section}; Property={item.Property}; Value={item.Value}; " +
        $"DeleteTarget={item.DeleteTargetDisplay}; Disabled={item.Disabled}; " +
        $"Targeting={item.HasFilters}";
}

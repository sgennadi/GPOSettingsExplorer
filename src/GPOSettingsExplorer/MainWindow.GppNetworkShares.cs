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
    private readonly GppNetworkShareService _gppNetworkShareService =
        new(new GppDocumentService());

    private readonly ObservableCollection<GppNetworkShareInfo>
        _gppNetworkShareItems = new();

    private ICollectionView? _gppNetworkShareView;
    private CancellationTokenSource? _gppNetworkShareCancellation;
    private bool _gppNetworkShareInitialized;

    private void GppNetworkSharesTab_Loaded(
        object sender,
        RoutedEventArgs e)
    {
        if (_gppNetworkShareInitialized)
            return;

        _gppNetworkShareInitialized = true;

        _gppNetworkShareView =
            CollectionViewSource.GetDefaultView(
                _gppNetworkShareItems);

        _gppNetworkShareView.Filter =
            FilterGppNetworkShare;

        GppNetworkSharesGrid.ItemsSource =
            _gppNetworkShareView;
    }

    private async void LoadGppNetworkShares_Click(
        object sender,
        RoutedEventArgs e)
    {
        await LoadGppNetworkSharesAsync();
    }

    private async Task LoadGppNetworkSharesAsync()
    {
        if (_gpos.Count == 0)
            return;

        _gppNetworkShareCancellation?.Cancel();

        _gppNetworkShareCancellation =
            new CancellationTokenSource();

        SetBusy(
            true,
            "Loading Network Shares preferences...");

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
                        _gppNetworkShareService.Load(
                            _gpos,
                            progress,
                            _gppNetworkShareCancellation.Token));

            ReplaceCollection(
                _gppNetworkShareItems,
                rows);

            _gppNetworkShareView?.Refresh();

            UpdateGppNetworkShareCount();

            HeaderStatusText.Text =
                $"{_gpos.Count:N0} GPOs | {_gppNetworkShareItems.Count:N0} Network Share items";

            StatusText.Text =
                "Network Shares preferences loaded";
        }
        catch (OperationCanceledException)
        {
            StatusText.Text =
                "Network Shares loading canceled";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                ex.Message,
                "Load Network Shares",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            StatusText.Text =
                "Network Shares load failed";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void NewGppNetworkShare_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_domainContext is null ||
            _gpos.Count == 0)
            return;

        var target =
            new GppComputerTargetWindow(
                _gpos,
                "New Network Share",
                "Network Shares preferences are computer policy. Select the target GPO:")
            {
                Owner = this
            };

        if (target.ShowDialog() != true ||
            target.SelectedGpo is null)
            return;

        var item =
            _gppNetworkShareService.CreateNew(
                target.SelectedGpo);

        var editor =
            new GppNetworkShareEditorWindow(
                item)
            {
                Owner = this
            };

        if (editor.ShowDialog() == true)
        {
            await SaveGppNetworkShareAsync(
                editor.Item,
                "Create");
        }
    }

    private async void EditGppNetworkShare_Click(
        object sender,
        RoutedEventArgs e)
    {
        await EditSelectedGppNetworkShareAsync();
    }

    private async void GppNetworkSharesGrid_MouseDoubleClick(
        object sender,
        MouseButtonEventArgs e)
    {
        await EditSelectedGppNetworkShareAsync();
    }

    private async Task EditSelectedGppNetworkShareAsync()
    {
        if (GppNetworkSharesGrid.SelectedItem
            is not GppNetworkShareInfo selected)
            return;

        var editor =
            new GppNetworkShareEditorWindow(
                CopyNetworkShareItem(
                    selected))
            {
                Owner = this
            };

        if (editor.ShowDialog() == true)
        {
            await SaveGppNetworkShareAsync(
                editor.Item,
                "Edit");
        }
    }

    private async void CloneGppNetworkShare_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (GppNetworkSharesGrid.SelectedItem
            is not GppNetworkShareInfo selected)
            return;

        var target =
            new GppComputerTargetWindow(
                _gpos,
                "Clone Network Share",
                "Select the destination GPO for the cloned Network Share preference:")
            {
                Owner = this
            };

        if (target.ShowDialog() != true ||
            target.SelectedGpo is null)
            return;

        var clone =
            _gppNetworkShareService.CreateNew(
                target.SelectedGpo);

        _gppNetworkShareService.CopyEditableValues(
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
            new GppNetworkShareEditorWindow(
                clone)
            {
                Owner = this
            };

        if (editor.ShowDialog() == true)
        {
            await SaveGppNetworkShareAsync(
                editor.Item,
                "Clone");
        }
    }

    private async Task SaveGppNetworkShareAsync(
        GppNetworkShareInfo item,
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
                "Network Share",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return;
        }

        SetBusy(
            true,
            "Backing up GPO before Network Share change...");

        try
        {
            var backup =
                await Task.Run(
                    () =>
                        _gpmService.BackupGpo(
                            _domainContext.DomainName,
                            gpo.Id,
                            $"Automatic backup before {action.ToLowerInvariant()} Network Share preference"));

            await Task.Run(
                () =>
                    _gppNetworkShareService.Save(
                        gpo,
                        _domainContext.DomainDistinguishedName,
                        item));

            _auditService.Write(
                action,
                "GPP Network Shares",
                gpo.DisplayName,
                $"Action: {item.ActionDisplay}; Share: {item.ShareName}; " +
                $"Path: {item.Path}; Limit: {item.LimitUsersDisplay}; " +
                $"ABE: {item.AbeDisplay}; Bulk regular: {item.AllRegular}; " +
                $"Bulk hidden: {item.AllHidden}; Bulk admin: {item.AllAdminDrive}; " +
                $"Targeting: {item.HasFilters}; Backup: {backup}",
                after:
                    GppNetworkShareSummary(
                        item));

            if (_settings.Count > 0)
            {
                await RefreshSingleGpoSettingsAsync(
                    gpo);
            }

            await LoadGppNetworkSharesAsync();

            if (_gppDocumentInitialized)
            {
                await LoadGppDocumentsAsync();
            }

            StatusText.Text =
                $"{action} Network Share preference completed. Backup: {backup}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                ex.Message,
                "Save Network Share",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            StatusText.Text =
                "Network Share change failed";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void DeleteGppNetworkShare_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_domainContext is null ||
            GppNetworkSharesGrid.SelectedItem
                is not GppNetworkShareInfo selected)
            return;

        var gpo =
            _gpos.FirstOrDefault(
                candidate =>
                    candidate.Id == selected.GpoId);

        if (gpo is null)
            return;

        if (MessageBox.Show(
                this,
                $"Delete Network Share preference '{selected.DisplayName}'?\n\n" +
                $"{selected.ActionDisplay}: {selected.ShareName} -> {selected.Path}\n\n" +
                "This removes the preference item from the GPO. A full GPO backup will be created first.",
                "Delete Network Share",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) !=
            MessageBoxResult.Yes)
        {
            return;
        }

        SetBusy(
            true,
            "Backing up GPO before deleting Network Share...");

        try
        {
            var backup =
                await Task.Run(
                    () =>
                        _gpmService.BackupGpo(
                            _domainContext.DomainName,
                            gpo.Id,
                            "Automatic backup before deleting Network Share preference"));

            await Task.Run(
                () =>
                    _gppNetworkShareService.Delete(
                        gpo,
                        _domainContext.DomainDistinguishedName,
                        selected));

            _auditService.Write(
                "Delete",
                "GPP Network Shares",
                gpo.DisplayName,
                $"Share: {selected.ShareName}; Path: {selected.Path}; Backup: {backup}",
                before:
                    GppNetworkShareSummary(
                        selected),
                after:
                    "<Removed>");

            if (_settings.Count > 0)
            {
                await RefreshSingleGpoSettingsAsync(
                    gpo);
            }

            await LoadGppNetworkSharesAsync();

            if (_gppDocumentInitialized)
            {
                await LoadGppDocumentsAsync();
            }

            StatusText.Text =
                $"Network Share preference deleted. Backup: {backup}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                ex.Message,
                "Delete Network Share",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            StatusText.Text =
                "Network Share delete failed";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void ShowGppNetworkShareRawXml_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (GppNetworkSharesGrid.SelectedItem
            is not GppNetworkShareInfo selected)
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
            "Computer";

        GppXmlTypeCombo.SelectedItem =
            "Network Shares";

        _gppDocumentView?.Refresh();

        StatusText.Text =
            $"GPP XML filter set to Network Shares for {selected.GpoName}";
    }

    private void GppNetworkSharesSearchBox_TextChanged(
        object sender,
        System.Windows.Controls.TextChangedEventArgs e)
    {
        _gppNetworkShareView?.Refresh();
        UpdateGppNetworkShareCount();
    }

    private bool FilterGppNetworkShare(
        object item)
    {
        if (item is not GppNetworkShareInfo share)
            return false;

        var search =
            GppNetworkSharesSearchBox?
                .Text?
                .Trim();

        return string.IsNullOrWhiteSpace(search) ||
               share.SearchText.Contains(
                   search,
                   StringComparison.CurrentCultureIgnoreCase);
    }

    private void UpdateGppNetworkShareCount()
    {
        if (GppNetworkSharesCountText is null)
            return;

        var shown =
            _gppNetworkShareView?
                .Cast<object>()
                .Count()
            ?? 0;

        GppNetworkSharesCountText.Text =
            $"{shown:N0} shown / {_gppNetworkShareItems.Count:N0} total";
    }

    private static GppNetworkShareInfo CopyNetworkShareItem(
        GppNetworkShareInfo source) =>
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
            ShareName = source.ShareName,
            Path = source.Path,
            Comment = source.Comment,
            AllRegular = source.AllRegular,
            AllHidden = source.AllHidden,
            AllAdminDrive = source.AllAdminDrive,
            LimitUsersMode = source.LimitUsersMode,
            UserLimit = source.UserLimit,
            AbeMode = source.AbeMode,
            Disabled = source.Disabled,
            BypassErrors = source.BypassErrors,
            RemoveWhenNoLongerApplied =
                source.RemoveWhenNoLongerApplied,
            RunInUserContext = source.RunInUserContext,
            FiltersXml = source.FiltersXml
        };

    private static string GppNetworkShareSummary(
        GppNetworkShareInfo item) =>
        $"Action={item.ActionDisplay}; Share={item.ShareName}; Path={item.Path}; " +
        $"Comment={item.Comment}; Limit={item.LimitUsersDisplay}; ABE={item.AbeDisplay}; " +
        $"AllRegular={item.AllRegular}; AllHidden={item.AllHidden}; " +
        $"AllAdminDrive={item.AllAdminDrive}; Disabled={item.Disabled}; " +
        $"Targeting={item.HasFilters}";
}

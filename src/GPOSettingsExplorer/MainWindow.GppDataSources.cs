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
    private readonly GppDataSourceService _gppDataSourceService =
        new(new GppDocumentService());

    private readonly ObservableCollection<GppDataSourceItemInfo>
        _gppDataSources = new();

    private ICollectionView? _gppDataSourceView;
    private CancellationTokenSource? _gppDataSourceCancellation;
    private bool _gppDataSourceInitialized;

    private void GppDataSourcesTab_Loaded(
        object sender,
        RoutedEventArgs e)
    {
        if (_gppDataSourceInitialized)
            return;

        _gppDataSourceInitialized = true;

        _gppDataSourceView =
            CollectionViewSource.GetDefaultView(_gppDataSources);

        _gppDataSourceView.Filter =
            FilterGppDataSource;

        GppDataSourcesGrid.ItemsSource =
            _gppDataSourceView;

        GppDataSourcesScopeCombo.ItemsSource =
            new[] { "All", "Computer", "User" };

        GppDataSourcesScopeCombo.SelectedIndex = 0;

        GppDataSourcesTypeCombo.ItemsSource =
            new[] { "All", "System DSN", "User DSN" };

        GppDataSourcesTypeCombo.SelectedIndex = 0;
    }

    private async void LoadGppDataSources_Click(
        object sender,
        RoutedEventArgs e)
    {
        await LoadGppDataSourcesAsync();
    }

    private async Task LoadGppDataSourcesAsync()
    {
        if (_gpos.Count == 0)
            return;

        _gppDataSourceCancellation?.Cancel();
        _gppDataSourceCancellation =
            new CancellationTokenSource();

        SetBusy(
            true,
            "Loading Data Sources preferences...");

        try
        {
            var progress =
                new Progress<string>(
                    message =>
                    {
                        StatusText.Text = message;
                        HeaderStatusText.Text = message;
                    });

            var rows =
                await Task.Run(
                    () =>
                        _gppDataSourceService.Load(
                            _gpos,
                            progress,
                            _gppDataSourceCancellation.Token));

            ReplaceCollection(
                _gppDataSources,
                rows);

            _gppDataSourceView?.Refresh();

            UpdateGppDataSourceCount();

            HeaderStatusText.Text =
                $"{_gpos.Count:N0} GPOs | {_gppDataSources.Count:N0} Data Source items";

            StatusText.Text =
                "Data Sources preferences loaded";
        }
        catch (OperationCanceledException)
        {
            StatusText.Text =
                "Data Sources loading canceled";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                ex.Message,
                "Load Data Sources",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            StatusText.Text =
                "Data Sources load failed";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void NewGppDataSource_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_domainContext is null ||
            _gpos.Count == 0)
            return;

        var target =
            new GppScopeTargetWindow(
                _gpos,
                "New Data Source Preference",
                "Select the target GPO and scope:")
            {
                Owner = this
            };

        if (target.ShowDialog() != true ||
            target.SelectedGpo is null)
            return;

        var item =
            _gppDataSourceService.CreateNew(
                target.SelectedGpo,
                target.SelectedScope);

        var editor =
            new GppDataSourceEditorWindow(
                item)
            {
                Owner = this
            };

        if (editor.ShowDialog() == true)
        {
            await SaveGppDataSourceAsync(
                editor.Item,
                "Create");
        }
    }

    private async void EditGppDataSource_Click(
        object sender,
        RoutedEventArgs e)
    {
        await EditSelectedGppDataSourceAsync();
    }

    private async void GppDataSourcesGrid_MouseDoubleClick(
        object sender,
        MouseButtonEventArgs e)
    {
        await EditSelectedGppDataSourceAsync();
    }

    private async Task EditSelectedGppDataSourceAsync()
    {
        if (GppDataSourcesGrid.SelectedItem
            is not GppDataSourceItemInfo selected)
            return;

        var editable =
            CopyDataSourceItem(
                selected);

        var editor =
            new GppDataSourceEditorWindow(
                editable)
            {
                Owner = this
            };

        if (editor.ShowDialog() == true)
        {
            await SaveGppDataSourceAsync(
                editor.Item,
                "Edit");
        }
    }

    private async void CloneGppDataSource_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (GppDataSourcesGrid.SelectedItem
            is not GppDataSourceItemInfo selected)
            return;

        var sourceGpo =
            _gpos.FirstOrDefault(
                gpo =>
                    gpo.Id == selected.GpoId);

        var target =
            new GppScopeTargetWindow(
                _gpos,
                "Clone Data Source Preference",
                $"Select the destination GPO and scope for '{selected.Dsn}':",
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
                "The source Data Source contains legacy GPP cpassword data. The cloned item will not copy that credential. The account name can still be retained.",
                "Clone Data Source",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        var clone =
            _gppDataSourceService.CreateNew(
                target.SelectedGpo,
                target.SelectedScope);

        _gppDataSourceService.CopyEditableValues(
            selected,
            clone,
            preserveCredential: false);

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

        if (!string.IsNullOrWhiteSpace(
                clone.Dsn))
        {
            clone.Dsn +=
                "_Copy";
        }

        var editor =
            new GppDataSourceEditorWindow(
                clone)
            {
                Owner = this
            };

        if (editor.ShowDialog() == true)
        {
            await SaveGppDataSourceAsync(
                editor.Item,
                "Clone");
        }
    }

    private async Task SaveGppDataSourceAsync(
        GppDataSourceItemInfo item,
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
                "Data Source",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return;
        }

        SetBusy(
            true,
            "Backing up GPO before Data Source change...");

        try
        {
            var backup =
                await Task.Run(
                    () =>
                        _gpmService.BackupGpo(
                            _domainContext.DomainName,
                            gpo.Id,
                            $"Automatic backup before {action.ToLowerInvariant()} Data Source preference"));

            await Task.Run(
                () =>
                    _gppDataSourceService.Save(
                        gpo,
                        _domainContext.DomainDistinguishedName,
                        item));

            _auditService.Write(
                action,
                "GPP Data Sources",
                gpo.DisplayName,
                $"Scope: {item.Scope}; Type: {item.DsnTypeDisplay}; " +
                $"Action: {item.ActionDisplay}; DSN: {item.Dsn}; " +
                $"Driver: {item.Driver}; Targeting: {item.HasFilters}; " +
                $"Legacy credential preserved: {item.HasStoredCredential && !item.ClearStoredCredential}; " +
                $"Backup: {backup}",
                after:
                    GppDataSourceSummary(
                        item));

            if (_settings.Count > 0)
            {
                await RefreshSingleGpoSettingsAsync(
                    gpo);
            }

            await LoadGppDataSourcesAsync();

            if (_gppDocumentInitialized)
            {
                await LoadGppDocumentsAsync();
            }

            StatusText.Text =
                $"{action} Data Source preference completed. Backup: {backup}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                ex.Message,
                "Save Data Source",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            StatusText.Text =
                "Data Source change failed";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void DeleteGppDataSource_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_domainContext is null ||
            GppDataSourcesGrid.SelectedItem
                is not GppDataSourceItemInfo selected)
            return;

        var gpo =
            _gpos.FirstOrDefault(
                candidate =>
                    candidate.Id == selected.GpoId);

        if (gpo is null)
            return;

        if (MessageBox.Show(
                this,
                $"Delete {selected.DsnTypeDisplay} preference '{selected.Dsn}'?\n\n" +
                $"{selected.ActionDisplay}; Driver: {selected.Driver}\n\n" +
                "This removes the preference item from the GPO. A full GPO backup will be created first.",
                "Delete Data Source Preference",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) !=
            MessageBoxResult.Yes)
        {
            return;
        }

        SetBusy(
            true,
            "Backing up GPO before deleting Data Source preference...");

        try
        {
            var backup =
                await Task.Run(
                    () =>
                        _gpmService.BackupGpo(
                            _domainContext.DomainName,
                            gpo.Id,
                            "Automatic backup before deleting Data Source preference"));

            await Task.Run(
                () =>
                    _gppDataSourceService.Delete(
                        gpo,
                        _domainContext.DomainDistinguishedName,
                        selected));

            _auditService.Write(
                "Delete",
                "GPP Data Sources",
                gpo.DisplayName,
                $"Scope: {selected.Scope}; Type: {selected.DsnTypeDisplay}; " +
                $"DSN: {selected.Dsn}; Driver: {selected.Driver}; Backup: {backup}",
                before:
                    GppDataSourceSummary(
                        selected),
                after:
                    "<Removed>");

            if (_settings.Count > 0)
            {
                await RefreshSingleGpoSettingsAsync(
                    gpo);
            }

            await LoadGppDataSourcesAsync();

            if (_gppDocumentInitialized)
            {
                await LoadGppDocumentsAsync();
            }

            StatusText.Text =
                $"Data Source preference deleted. Backup: {backup}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                ex.Message,
                "Delete Data Source Preference",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            StatusText.Text =
                "Data Source preference delete failed";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void ShowGppDataSourceRawXml_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (GppDataSourcesGrid.SelectedItem
            is not GppDataSourceItemInfo selected)
            return;

        if (!_gppDocumentInitialized)
        {
            _gppDocumentInitialized = true;

            _gppDocumentView =
                CollectionViewSource.GetDefaultView(
                    _gppDocuments);

            _gppDocumentView.Filter =
                FilterGppDocument;

            GppXmlGrid.ItemsSource =
                _gppDocumentView;

            GppXmlScopeCombo.ItemsSource =
                new[] { "All", "Computer", "User" };

            GppXmlScopeCombo.SelectedIndex =
                0;

            var types =
                new[] { "All" }
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
            "Data Sources";

        _gppDocumentView?.Refresh();

        StatusText.Text =
            $"GPP XML filter set to Data Sources for {selected.GpoName}";
    }

    private void GppDataSourcesSearchBox_TextChanged(
        object sender,
        System.Windows.Controls.TextChangedEventArgs e)
    {
        _gppDataSourceView?.Refresh();
        UpdateGppDataSourceCount();
    }

    private void GppDataSourcesFilter_SelectionChanged(
        object sender,
        System.Windows.Controls.SelectionChangedEventArgs e)
    {
        _gppDataSourceView?.Refresh();
        UpdateGppDataSourceCount();
    }

    private bool FilterGppDataSource(
        object item)
    {
        if (item
            is not GppDataSourceItemInfo dataSource)
            return false;

        var scope =
            Convert.ToString(
                GppDataSourcesScopeCombo?
                    .SelectedItem);

        if (!string.IsNullOrWhiteSpace(scope) &&
            !scope.Equals(
                "All",
                StringComparison.OrdinalIgnoreCase) &&
            !dataSource.Scope.Equals(
                scope,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var type =
            Convert.ToString(
                GppDataSourcesTypeCombo?
                    .SelectedItem);

        if (!string.IsNullOrWhiteSpace(type) &&
            !type.Equals(
                "All",
                StringComparison.OrdinalIgnoreCase) &&
            !dataSource.DsnTypeDisplay.Equals(
                type,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var search =
            GppDataSourcesSearchBox?
                .Text?
                .Trim();

        return string.IsNullOrWhiteSpace(search) ||
               dataSource.SearchText.Contains(
                   search,
                   StringComparison.CurrentCultureIgnoreCase);
    }

    private void UpdateGppDataSourceCount()
    {
        if (GppDataSourcesCountText is null)
            return;

        var shown =
            _gppDataSourceView?
                .Cast<object>()
                .Count()
            ?? 0;

        GppDataSourcesCountText.Text =
            $"{shown:N0} shown / {_gppDataSources.Count:N0} total";
    }

    private static GppDataSourceItemInfo CopyDataSourceItem(
        GppDataSourceItemInfo source) =>
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
            UserDsn = source.UserDsn,
            Dsn = source.Dsn,
            Driver = source.Driver,
            DsnDescription = source.DsnDescription,
            UserName = source.UserName,
            Attributes =
                new System.Collections.ObjectModel.ObservableCollection<GppDataSourceAttributeInfo>(
                    source.Attributes.Select(
                        attribute =>
                            new GppDataSourceAttributeInfo
                            {
                                Name = attribute.Name,
                                Value = attribute.Value
                            })),
            Disabled = source.Disabled,
            BypassErrors = source.BypassErrors,
            RemoveWhenNoLongerApplied =
                source.RemoveWhenNoLongerApplied,
            RunInUserContext =
                source.RunInUserContext,
            FiltersXml = source.FiltersXml,
            OpaqueCredential =
                source.OpaqueCredential,
            ClearStoredCredential = false
        };

    private static string GppDataSourceSummary(
        GppDataSourceItemInfo item) =>
        $"Scope={item.Scope}; Type={item.DsnTypeDisplay}; " +
        $"Action={item.ActionDisplay}; DSN={item.Dsn}; " +
        $"Driver={item.Driver}; Description={item.DsnDescription}; " +
        $"User={item.UserName}; Attributes={item.AttributesPreview}; " +
        $"ItemDisabled={item.Disabled}; Targeting={item.HasFilters}; " +
        $"LegacyCredentialPresent={item.HasStoredCredential}";
}

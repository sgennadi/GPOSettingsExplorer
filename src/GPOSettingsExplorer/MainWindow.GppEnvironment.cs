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
    private readonly GppEnvironmentService _gppEnvironmentService =
        new(new GppDocumentService());

    private readonly ObservableCollection<GppEnvironmentVariableInfo>
        _gppEnvironmentItems = new();

    private ICollectionView? _gppEnvironmentView;
    private CancellationTokenSource? _gppEnvironmentCancellation;
    private bool _gppEnvironmentInitialized;

    private void GppEnvironmentTab_Loaded(
        object sender,
        RoutedEventArgs e)
    {
        if (_gppEnvironmentInitialized)
            return;

        _gppEnvironmentInitialized = true;

        _gppEnvironmentView =
            CollectionViewSource.GetDefaultView(
                _gppEnvironmentItems);

        _gppEnvironmentView.Filter =
            FilterGppEnvironment;

        GppEnvironmentGrid.ItemsSource =
            _gppEnvironmentView;

        GppEnvironmentScopeCombo.ItemsSource =
            new[]
            {
                "All",
                "Computer",
                "User"
            };

        GppEnvironmentScopeCombo.SelectedIndex =
            0;
    }

    private async void LoadGppEnvironment_Click(
        object sender,
        RoutedEventArgs e)
    {
        await LoadGppEnvironmentAsync();
    }

    private async Task LoadGppEnvironmentAsync()
    {
        if (_gpos.Count == 0)
            return;

        _gppEnvironmentCancellation?.Cancel();

        _gppEnvironmentCancellation =
            new CancellationTokenSource();

        SetBusy(
            true,
            "Loading Environment Variables preferences...");

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
                        _gppEnvironmentService.Load(
                            _gpos,
                            progress,
                            _gppEnvironmentCancellation.Token));

            ReplaceCollection(
                _gppEnvironmentItems,
                rows);

            _gppEnvironmentView?.Refresh();

            UpdateGppEnvironmentCount();

            HeaderStatusText.Text =
                $"{_gpos.Count:N0} GPOs | {_gppEnvironmentItems.Count:N0} Environment Variable items";

            StatusText.Text =
                "Environment Variables preferences loaded";
        }
        catch (OperationCanceledException)
        {
            StatusText.Text =
                "Environment Variables loading canceled";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                ex.Message,
                "Load Environment Variables",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            StatusText.Text =
                "Environment Variables load failed";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void NewGppEnvironment_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_domainContext is null ||
            _gpos.Count == 0)
            return;

        var target =
            new GppScopeTargetWindow(
                _gpos,
                "New Environment Variable",
                "Select the target GPO and scope for this Environment Variables preference:",
                selectedScope: "Computer")
            {
                Owner = this
            };

        if (target.ShowDialog() != true ||
            target.SelectedGpo is null)
            return;

        var item =
            _gppEnvironmentService.CreateNew(
                target.SelectedGpo,
                target.SelectedScope);

        var editor =
            new GppEnvironmentEditorWindow(
                item)
            {
                Owner = this
            };

        if (editor.ShowDialog() == true)
        {
            await SaveGppEnvironmentAsync(
                editor.Item,
                "Create");
        }
    }

    private async void EditGppEnvironment_Click(
        object sender,
        RoutedEventArgs e)
    {
        await EditSelectedGppEnvironmentAsync();
    }

    private async void GppEnvironmentGrid_MouseDoubleClick(
        object sender,
        MouseButtonEventArgs e)
    {
        await EditSelectedGppEnvironmentAsync();
    }

    private async Task EditSelectedGppEnvironmentAsync()
    {
        if (GppEnvironmentGrid.SelectedItem
            is not GppEnvironmentVariableInfo selected)
            return;

        var editable =
            CopyEnvironmentItem(
                selected);

        var editor =
            new GppEnvironmentEditorWindow(
                editable)
            {
                Owner = this
            };

        if (editor.ShowDialog() == true)
        {
            await SaveGppEnvironmentAsync(
                editor.Item,
                "Edit");
        }
    }

    private async void CloneGppEnvironment_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (GppEnvironmentGrid.SelectedItem
            is not GppEnvironmentVariableInfo selected)
            return;

        var sourceGpo =
            _gpos.FirstOrDefault(
                gpo =>
                    gpo.Id == selected.GpoId);

        var target =
            new GppScopeTargetWindow(
                _gpos,
                "Clone Environment Variable",
                "Select the destination GPO and scope for the cloned Environment Variable preference:",
                sourceGpo,
                selected.Scope)
            {
                Owner = this
            };

        if (target.ShowDialog() != true ||
            target.SelectedGpo is null)
            return;

        var clone =
            _gppEnvironmentService.CreateNew(
                target.SelectedGpo,
                target.SelectedScope);

        _gppEnvironmentService.CopyEditableValues(
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
            new GppEnvironmentEditorWindow(
                clone)
            {
                Owner = this
            };

        if (editor.ShowDialog() == true)
        {
            await SaveGppEnvironmentAsync(
                editor.Item,
                "Clone");
        }
    }

    private async Task SaveGppEnvironmentAsync(
        GppEnvironmentVariableInfo item,
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
                "Environment Variables",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return;
        }

        SetBusy(
            true,
            "Backing up GPO before Environment Variables change...");

        try
        {
            var backup =
                await Task.Run(
                    () =>
                        _gpmService.BackupGpo(
                            _domainContext.DomainName,
                            gpo.Id,
                            $"Automatic backup before {action.ToLowerInvariant()} Environment Variable preference"));

            StatusText.Text =
                "Saving Environment Variable preference...";

            await Task.Run(
                () =>
                    _gppEnvironmentService.Save(
                        gpo,
                        _domainContext.DomainDistinguishedName,
                        item));

            _auditService.Write(
                action,
                "GPP Environment Variables",
                gpo.DisplayName,
                $"Scope: {item.Scope}; Action: {item.ActionDisplay}; " +
                $"Variable: {item.Name}; Type: {item.VariableTypeDisplay}; " +
                $"Partial PATH: {item.PartialPath}; Targeting: {item.HasFilters}; " +
                $"Backup: {backup}",
                after:
                    GppEnvironmentSummary(
                        item));

            if (_settings.Count > 0)
            {
                await RefreshSingleGpoSettingsAsync(
                    gpo);
            }

            await LoadGppEnvironmentAsync();

            if (_gppDocumentInitialized)
            {
                await LoadGppDocumentsAsync();
            }

            StatusText.Text =
                $"{action} Environment Variable preference completed. Backup: {backup}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                ex.Message,
                "Save Environment Variable",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            StatusText.Text =
                "Environment Variables change failed";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void DeleteGppEnvironment_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_domainContext is null ||
            GppEnvironmentGrid.SelectedItem
                is not GppEnvironmentVariableInfo selected)
            return;

        var gpo =
            _gpos.FirstOrDefault(
                candidate =>
                    candidate.Id == selected.GpoId);

        if (gpo is null)
            return;

        if (MessageBox.Show(
                this,
                $"Delete Environment Variable preference '{selected.DisplayName}'?\n\n" +
                $"{selected.ActionDisplay}: {selected.VariableTypeDisplay} {selected.Name} = {selected.Value}\n\n" +
                "A full GPO backup will be created first.",
                "Delete Environment Variable",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) !=
            MessageBoxResult.Yes)
        {
            return;
        }

        SetBusy(
            true,
            "Backing up GPO before deleting Environment Variable...");

        try
        {
            var backup =
                await Task.Run(
                    () =>
                        _gpmService.BackupGpo(
                            _domainContext.DomainName,
                            gpo.Id,
                            "Automatic backup before deleting Environment Variable preference"));

            await Task.Run(
                () =>
                    _gppEnvironmentService.Delete(
                        gpo,
                        _domainContext.DomainDistinguishedName,
                        selected));

            _auditService.Write(
                "Delete",
                "GPP Environment Variables",
                gpo.DisplayName,
                $"Scope: {selected.Scope}; Variable: {selected.Name}; Backup: {backup}",
                before:
                    GppEnvironmentSummary(
                        selected),
                after:
                    "<Removed>");

            if (_settings.Count > 0)
            {
                await RefreshSingleGpoSettingsAsync(
                    gpo);
            }

            await LoadGppEnvironmentAsync();

            if (_gppDocumentInitialized)
            {
                await LoadGppDocumentsAsync();
            }

            StatusText.Text =
                $"Environment Variable deleted. Backup: {backup}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                ex.Message,
                "Delete Environment Variable",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            StatusText.Text =
                "Environment Variable delete failed";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void ShowGppEnvironmentRawXml_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (GppEnvironmentGrid.SelectedItem
            is not GppEnvironmentVariableInfo selected)
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
            "Environment Variables";

        _gppDocumentView?.Refresh();

        StatusText.Text =
            $"GPP XML filter set to Environment Variables for {selected.GpoName}";
    }

    private void GppEnvironmentSearchBox_TextChanged(
        object sender,
        System.Windows.Controls.TextChangedEventArgs e)
    {
        _gppEnvironmentView?.Refresh();
        UpdateGppEnvironmentCount();
    }

    private void GppEnvironmentScopeCombo_SelectionChanged(
        object sender,
        System.Windows.Controls.SelectionChangedEventArgs e)
    {
        _gppEnvironmentView?.Refresh();
        UpdateGppEnvironmentCount();
    }

    private bool FilterGppEnvironment(
        object item)
    {
        if (item
            is not GppEnvironmentVariableInfo environment)
            return false;

        var scope =
            Convert.ToString(
                GppEnvironmentScopeCombo?.SelectedItem);

        if (!string.IsNullOrWhiteSpace(scope) &&
            !scope.Equals(
                "All",
                StringComparison.OrdinalIgnoreCase) &&
            !environment.Scope.Equals(
                scope,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var search =
            GppEnvironmentSearchBox?
                .Text?
                .Trim();

        return string.IsNullOrWhiteSpace(search) ||
               environment.SearchText.Contains(
                   search,
                   StringComparison.CurrentCultureIgnoreCase);
    }

    private void UpdateGppEnvironmentCount()
    {
        if (GppEnvironmentCountText is null)
            return;

        var shown =
            _gppEnvironmentView?
                .Cast<object>()
                .Count()
            ?? 0;

        GppEnvironmentCountText.Text =
            $"{shown:N0} shown / {_gppEnvironmentItems.Count:N0} total";
    }

    private static GppEnvironmentVariableInfo
        CopyEnvironmentItem(
            GppEnvironmentVariableInfo source) =>
        new()
        {
            GpoId =
                source.GpoId,
            GpoName =
                source.GpoName,
            DomainName =
                source.DomainName,
            Scope =
                source.Scope,
            XmlPath =
                source.XmlPath,
            Uid =
                source.Uid,
            Ordinal =
                source.Ordinal,
            DisplayName =
                source.DisplayName,
            Description =
                source.Description,
            Action =
                source.Action,
            Name =
                source.Name,
            Value =
                source.Value,
            UserVariable =
                source.UserVariable,
            PartialPath =
                source.PartialPath,
            Disabled =
                source.Disabled,
            BypassErrors =
                source.BypassErrors,
            RemoveWhenNoLongerApplied =
                source.RemoveWhenNoLongerApplied,
            RunInUserContext =
                source.RunInUserContext,
            FiltersXml =
                source.FiltersXml
        };

    private static string GppEnvironmentSummary(
        GppEnvironmentVariableInfo item) =>
        $"Scope={item.Scope}; Action={item.ActionDisplay}; " +
        $"Name={item.Name}; Value={item.Value}; Type={item.VariableTypeDisplay}; " +
        $"PartialPath={item.PartialPath}; Disabled={item.Disabled}; " +
        $"Targeting={item.HasFilters}";
}

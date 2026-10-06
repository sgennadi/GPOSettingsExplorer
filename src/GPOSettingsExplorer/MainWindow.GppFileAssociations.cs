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
    private readonly GppFileAssociationService _gppFileAssociationService =
        new(new GppDocumentService());

    private readonly ObservableCollection<GppFileAssociationItemInfo>
        _gppFileAssociations = new();

    private ICollectionView? _gppFileAssociationsView;
    private CancellationTokenSource? _gppFileAssociationsCancellation;
    private bool _gppFileAssociationsInitialized;

    private void GppFileAssociationsTab_Loaded(
        object sender,
        RoutedEventArgs e)
    {
        if (_gppFileAssociationsInitialized)
            return;

        _gppFileAssociationsInitialized = true;

        _gppFileAssociationsView =
            CollectionViewSource.GetDefaultView(
                _gppFileAssociations);

        _gppFileAssociationsView.Filter =
            FilterGppFileAssociation;

        GppFileAssociationsGrid.ItemsSource =
            _gppFileAssociationsView;

        GppFileAssociationsTypeCombo.ItemsSource =
            new[] { "All", "Open With", "File Type" };

        GppFileAssociationsTypeCombo.SelectedIndex = 0;
    }

    private async void LoadGppFileAssociations_Click(
        object sender,
        RoutedEventArgs e)
    {
        await LoadGppFileAssociationsAsync();
    }

    private async Task LoadGppFileAssociationsAsync()
    {
        if (_gpos.Count == 0)
            return;

        _gppFileAssociationsCancellation?.Cancel();
        _gppFileAssociationsCancellation =
            new CancellationTokenSource();

        SetBusy(true, "Loading file associations...");

        try
        {
            var progress = new Progress<string>(
                message =>
                {
                    StatusText.Text = message;
                    HeaderStatusText.Text = message;
                });

            var rows = await Task.Run(() =>
                _gppFileAssociationService.Load(
                    _gpos,
                    progress,
                    _gppFileAssociationsCancellation.Token));

            ReplaceCollection(
                _gppFileAssociations,
                rows);

            _gppFileAssociationsView?.Refresh();
            UpdateGppFileAssociationCount();

            HeaderStatusText.Text =
                $"{_gpos.Count:N0} GPOs | {_gppFileAssociations.Count:N0} file association items";

            StatusText.Text =
                "File associations loaded";
        }
        catch (OperationCanceledException)
        {
            StatusText.Text =
                "File association loading canceled";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                ex.Message,
                "Load File Associations",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            StatusText.Text =
                "File association load failed";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void NewOpenWith_Click(
        object sender,
        RoutedEventArgs e)
    {
        await NewGppFileAssociationAsync("OpenWith");
    }

    private async void NewFileType_Click(
        object sender,
        RoutedEventArgs e)
    {
        await NewGppFileAssociationAsync("FileType");
    }

    private async Task NewGppFileAssociationAsync(
        string kind)
    {
        if (_domainContext is null || _gpos.Count == 0)
            return;

        var isFileType =
            kind.Equals(
                "FileType",
                StringComparison.OrdinalIgnoreCase);

        var requiredScope =
            isFileType
                ? "Computer"
                : "User";

        var target = new GppScopeTargetWindow(
            _gpos,
            isFileType
                ? "New File Type Preference"
                : "New Open With Preference",
            isFileType
                ? "Select the target GPO. File Type preferences use Computer Configuration."
                : "Select the target GPO. Open With preferences use User Configuration.",
            selectedScope: requiredScope)
        {
            Owner = this
        };

        if (target.ShowDialog() != true ||
            target.SelectedGpo is null)
            return;

        if (!target.SelectedScope.Equals(
                requiredScope,
                StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show(
                this,
                $"{(isFileType ? "File Type" : "Open With")} preferences require {requiredScope} Configuration.",
                "File Association",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var item =
            _gppFileAssociationService.CreateNew(
                target.SelectedGpo,
                kind);

        var editor =
            new GppFileAssociationEditorWindow(
                item)
            {
                Owner = this
            };

        if (editor.ShowDialog() == true)
        {
            await SaveGppFileAssociationAsync(
                editor.Item,
                "Create");
        }
    }

    private async void EditGppFileAssociation_Click(
        object sender,
        RoutedEventArgs e)
    {
        await EditSelectedGppFileAssociationAsync();
    }

    private async void GppFileAssociationsGrid_MouseDoubleClick(
        object sender,
        MouseButtonEventArgs e)
    {
        await EditSelectedGppFileAssociationAsync();
    }

    private async Task EditSelectedGppFileAssociationAsync()
    {
        if (GppFileAssociationsGrid.SelectedItem
            is not GppFileAssociationItemInfo selected)
            return;

        var editable =
            CopyGppFileAssociationItem(
                selected);

        var editor =
            new GppFileAssociationEditorWindow(
                editable)
            {
                Owner = this
            };

        if (editor.ShowDialog() == true)
        {
            await SaveGppFileAssociationAsync(
                editor.Item,
                "Edit");
        }
    }

    private async void CloneGppFileAssociation_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (GppFileAssociationsGrid.SelectedItem
            is not GppFileAssociationItemInfo selected)
            return;

        var sourceGpo =
            _gpos.FirstOrDefault(
                gpo => gpo.Id == selected.GpoId);

        var target =
            new GppScopeTargetWindow(
                _gpos,
                "Clone File Association Preference",
                $"Select the destination GPO. {selected.KindDisplay} requires {selected.Scope} Configuration.",
                sourceGpo,
                selected.Scope)
            {
                Owner = this
            };

        if (target.ShowDialog() != true ||
            target.SelectedGpo is null)
            return;

        if (!target.SelectedScope.Equals(
                selected.Scope,
                StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show(
                this,
                $"{selected.KindDisplay} requires {selected.Scope} Configuration.",
                "Clone File Association",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var clone =
            _gppFileAssociationService.CreateNew(
                target.SelectedGpo,
                selected.ItemKind);

        _gppFileAssociationService.CopyEditableValues(
            selected,
            clone);

        clone.Uid =
            Guid.NewGuid()
                .ToString("B")
                .ToUpperInvariant();

        if (!string.IsNullOrWhiteSpace(clone.DisplayName))
            clone.DisplayName += " - Copy";

        var editor =
            new GppFileAssociationEditorWindow(
                clone)
            {
                Owner = this
            };

        if (editor.ShowDialog() == true)
        {
            await SaveGppFileAssociationAsync(
                editor.Item,
                "Clone");
        }
    }

    private async Task SaveGppFileAssociationAsync(
        GppFileAssociationItemInfo item,
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
                "File Association",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        SetBusy(
            true,
            "Backing up GPO before file association change...");

        try
        {
            var backup =
                await Task.Run(
                    () =>
                        _gpmService.BackupGpo(
                            _domainContext.DomainName,
                            gpo.Id,
                            $"Automatic backup before {action.ToLowerInvariant()} file association preference"));

            await Task.Run(
                () =>
                    _gppFileAssociationService.Save(
                        gpo,
                        _domainContext.DomainDistinguishedName,
                        item));

            _auditService.Write(
                action,
                "GPP File Association",
                gpo.DisplayName,
                $"Scope: {item.Scope}; Type: {item.KindDisplay}; " +
                $"Extension: .{item.FileExtension}; Target: {item.TargetDisplay}; " +
                $"Action: {item.ActionDisplay}; Targeting: {item.HasFilters}; " +
                $"Backup: {backup}",
                after:
                    GppFileAssociationSummary(
                        item));

            if (_settings.Count > 0)
                await RefreshSingleGpoSettingsAsync(gpo);

            await LoadGppFileAssociationsAsync();

            if (_gppDocumentInitialized)
                await LoadGppDocumentsAsync();

            if (_gppFolderOptionsInitialized)
                await LoadGppFolderOptionsAsync();

            StatusText.Text =
                $"{action} file association completed. Backup: {backup}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                ex.Message,
                "Save File Association",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            StatusText.Text =
                "File association change failed";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void DeleteGppFileAssociation_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_domainContext is null ||
            GppFileAssociationsGrid.SelectedItem
                is not GppFileAssociationItemInfo selected)
            return;

        var gpo =
            _gpos.FirstOrDefault(
                candidate =>
                    candidate.Id == selected.GpoId);

        if (gpo is null)
            return;

        if (MessageBox.Show(
                this,
                $"Delete {selected.KindDisplay} preference for '.{selected.FileExtension}'?\n\n" +
                $"Target: {selected.TargetDisplay}\n\n" +
                "A full GPO backup will be created first.",
                "Delete File Association",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) !=
            MessageBoxResult.Yes)
        {
            return;
        }

        SetBusy(
            true,
            "Backing up GPO before deleting file association...");

        try
        {
            var backup =
                await Task.Run(
                    () =>
                        _gpmService.BackupGpo(
                            _domainContext.DomainName,
                            gpo.Id,
                            "Automatic backup before deleting file association preference"));

            await Task.Run(
                () =>
                    _gppFileAssociationService.Delete(
                        gpo,
                        _domainContext.DomainDistinguishedName,
                        selected));

            _auditService.Write(
                "Delete",
                "GPP File Association",
                gpo.DisplayName,
                $"Scope: {selected.Scope}; Type: {selected.KindDisplay}; " +
                $"Extension: .{selected.FileExtension}; Backup: {backup}",
                before:
                    GppFileAssociationSummary(
                        selected),
                after:
                    "<Removed>");

            if (_settings.Count > 0)
                await RefreshSingleGpoSettingsAsync(gpo);

            await LoadGppFileAssociationsAsync();

            if (_gppDocumentInitialized)
                await LoadGppDocumentsAsync();

            if (_gppFolderOptionsInitialized)
                await LoadGppFolderOptionsAsync();

            StatusText.Text =
                $"File association deleted. Backup: {backup}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                ex.Message,
                "Delete File Association",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            StatusText.Text =
                "File association delete failed";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void ShowGppFileAssociationRawXml_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (GppFileAssociationsGrid.SelectedItem
            is not GppFileAssociationItemInfo selected)
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

            GppXmlScopeCombo.SelectedIndex = 0;

            var types = new[] { "All" }
                .Concat(
                    _gppDocumentService
                        .GetKnownTypes()
                        .Select(type => type.Name))
                .Distinct(StringComparer.CurrentCultureIgnoreCase)
                .OrderBy(name =>
                    name.Equals(
                        "All",
                        StringComparison.OrdinalIgnoreCase)
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

    private void GppFileAssociationsSearchBox_TextChanged(
        object sender,
        System.Windows.Controls.TextChangedEventArgs e)
    {
        _gppFileAssociationsView?.Refresh();
        UpdateGppFileAssociationCount();
    }

    private void GppFileAssociationsType_SelectionChanged(
        object sender,
        System.Windows.Controls.SelectionChangedEventArgs e)
    {
        _gppFileAssociationsView?.Refresh();
        UpdateGppFileAssociationCount();
    }

    private bool FilterGppFileAssociation(
        object item)
    {
        if (item
            is not GppFileAssociationItemInfo association)
            return false;

        var type =
            Convert.ToString(
                GppFileAssociationsTypeCombo?
                    .SelectedItem);

        if (!string.IsNullOrWhiteSpace(type) &&
            !type.Equals(
                "All",
                StringComparison.OrdinalIgnoreCase) &&
            !association.KindDisplay.Equals(
                type,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var search =
            GppFileAssociationsSearchBox?
                .Text?
                .Trim();

        return string.IsNullOrWhiteSpace(search) ||
               association.SearchText.Contains(
                   search,
                   StringComparison.CurrentCultureIgnoreCase);
    }

    private void UpdateGppFileAssociationCount()
    {
        if (GppFileAssociationsCountText is null)
            return;

        var shown =
            _gppFileAssociationsView?
                .Cast<object>()
                .Count()
            ?? 0;

        GppFileAssociationsCountText.Text =
            $"{shown:N0} shown / {_gppFileAssociations.Count:N0} total";
    }

    private static GppFileAssociationItemInfo CopyGppFileAssociationItem(
        GppFileAssociationItemInfo source) =>
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
            FileExtension = source.FileExtension,
            ApplicationPath = source.ApplicationPath,
            DefaultApplication = source.DefaultApplication,
            Application = source.Application,
            ApplicationProgId = source.ApplicationProgId,
            ConfigureActions = source.ConfigureActions,
            Disabled = source.Disabled,
            BypassErrors = source.BypassErrors,
            RemoveWhenNoLongerApplied =
                source.RemoveWhenNoLongerApplied,
            RunInUserContext =
                source.RunInUserContext,
            FiltersXml = source.FiltersXml
        };

    private static string GppFileAssociationSummary(
        GppFileAssociationItemInfo item) =>
        $"Scope={item.Scope}; Type={item.KindDisplay}; " +
        $"Action={item.ActionDisplay}; Extension=.{item.FileExtension}; " +
        $"Target={item.TargetDisplay}; Default={item.DefaultApplication}; " +
        $"ConfigureActions={item.ConfigureActions}; " +
        $"ItemDisabled={item.Disabled}; Targeting={item.HasFilters}";
}

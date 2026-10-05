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
    private readonly GppDriveService _gppDriveService =
        new(new GppDocumentService());

    private readonly ObservableCollection<GppDriveItemInfo> _gppDriveItems = new();
    private ICollectionView? _gppDriveView;
    private CancellationTokenSource? _gppDriveCancellation;
    private bool _gppDriveInitialized;

    private void GppDrivesTab_Loaded(object sender, RoutedEventArgs e)
    {
        if (_gppDriveInitialized)
            return;

        _gppDriveInitialized = true;
        _gppDriveView = CollectionViewSource.GetDefaultView(_gppDriveItems);
        _gppDriveView.Filter = FilterGppDrive;
        GppDrivesGrid.ItemsSource = _gppDriveView;
    }

    private async void LoadGppDrives_Click(object sender, RoutedEventArgs e)
    {
        await LoadGppDrivesAsync();
    }

    private async Task LoadGppDrivesAsync()
    {
        if (_gpos.Count == 0)
            return;

        _gppDriveCancellation?.Cancel();
        _gppDriveCancellation = new CancellationTokenSource();

        SetBusy(true, "Loading Drive Maps preferences...");

        try
        {
            var progress = new Progress<string>(message =>
            {
                StatusText.Text = message;
                HeaderStatusText.Text = message;
            });

            var rows = await Task.Run(() =>
                _gppDriveService.Load(
                    _gpos,
                    progress,
                    _gppDriveCancellation.Token));

            ReplaceCollection(_gppDriveItems, rows);
            _gppDriveView?.Refresh();
            UpdateGppDriveCount();

            HeaderStatusText.Text =
                $"{_gpos.Count:N0} GPOs | {_gppDriveItems.Count:N0} Drive Maps items";
            StatusText.Text = "Drive Maps preferences loaded";
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Drive Maps loading canceled";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                ex.Message,
                "Load Drive Maps",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            StatusText.Text = "Drive Maps load failed";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void NewGppDrive_Click(object sender, RoutedEventArgs e)
    {
        if (_domainContext is null || _gpos.Count == 0)
            return;

        var picker = new GpoPickerWindow(
            _gpos,
            "New Drive Map",
            "Select the GPO that will contain the User Configuration Drive Maps preference:")
        {
            Owner = this
        };

        if (picker.ShowDialog() != true || picker.SelectedGpo is null)
            return;

        var item = _gppDriveService.CreateNew(picker.SelectedGpo);

        var editor = new GppDriveEditorWindow(item)
        {
            Owner = this
        };

        if (editor.ShowDialog() == true)
            await SaveGppDriveAsync(editor.Item, "Create");
    }

    private async void EditGppDrive_Click(object sender, RoutedEventArgs e)
    {
        await EditSelectedGppDriveAsync();
    }

    private async void GppDrivesGrid_MouseDoubleClick(
        object sender,
        MouseButtonEventArgs e)
    {
        await EditSelectedGppDriveAsync();
    }

    private async Task EditSelectedGppDriveAsync()
    {
        if (GppDrivesGrid.SelectedItem is not GppDriveItemInfo selected)
            return;

        var editable = CopyDriveItem(selected, preserveCredential: true);

        var editor = new GppDriveEditorWindow(editable)
        {
            Owner = this
        };

        if (editor.ShowDialog() == true)
            await SaveGppDriveAsync(editor.Item, "Edit");
    }

    private async void CloneGppDrive_Click(object sender, RoutedEventArgs e)
    {
        if (GppDrivesGrid.SelectedItem is not GppDriveItemInfo selected)
            return;

        var sourceGpo = _gpos.FirstOrDefault(gpo => gpo.Id == selected.GpoId);

        var picker = new GpoPickerWindow(
            _gpos,
            "Clone Drive Map",
            "Select the target GPO. The cloned Drive Maps item will be created in User Configuration:",
            sourceGpo)
        {
            Owner = this
        };

        if (picker.ShowDialog() != true || picker.SelectedGpo is null)
            return;

        if (selected.HasStoredCredential)
        {
            MessageBox.Show(
                this,
                "The source item contains a legacy stored cpassword value. " +
                "For safety, the cloned item will not copy that credential. " +
                "The account name can still be retained.",
                "Clone Drive Map",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        var clone = _gppDriveService.CreateNew(picker.SelectedGpo);
        CopyDriveValues(selected, clone, preserveCredential: false);
        clone.Uid = Guid.NewGuid().ToString("B").ToUpperInvariant();
        clone.DisplayName = clone.Letter + ":";

        var editor = new GppDriveEditorWindow(clone)
        {
            Owner = this
        };

        if (editor.ShowDialog() == true)
            await SaveGppDriveAsync(editor.Item, "Clone");
    }

    private async Task SaveGppDriveAsync(
        GppDriveItemInfo item,
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
                "Drive Maps",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        SetBusy(true, "Backing up GPO before Drive Maps change...");

        try
        {
            var backup = await Task.Run(() =>
                _gpmService.BackupGpo(
                    _domainContext.DomainName,
                    gpo.Id,
                    $"Automatic backup before {action.ToLowerInvariant()} Drive Maps preference"));

            StatusText.Text = "Saving Drive Maps preference...";

            await Task.Run(() =>
                _gppDriveService.Save(
                    gpo,
                    _domainContext.DomainDistinguishedName,
                    item));

            _auditService.Write(
                action,
                "GPP Drive Maps",
                gpo.DisplayName,
                $"Drive: {item.DriveDisplay}; Action: {item.ActionDisplay}; " +
                $"Path: {item.Path}; Persistent: {item.Persistent}; " +
                $"Targeting: {item.HasFilters}; Backup: {backup}",
                after: GppDriveSummary(item));

            if (_settings.Count > 0)
                await RefreshSingleGpoSettingsAsync(gpo);

            await LoadGppDrivesAsync();

            if (_gppDocumentInitialized)
                await LoadGppDocumentsAsync();

            StatusText.Text =
                $"{action} Drive Maps preference completed. Backup: {backup}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                ex.Message,
                "Save Drive Map",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            StatusText.Text = "Drive Maps change failed";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void DeleteGppDrive_Click(object sender, RoutedEventArgs e)
    {
        if (_domainContext is null ||
            GppDrivesGrid.SelectedItem is not GppDriveItemInfo selected)
            return;

        var gpo = _gpos.FirstOrDefault(candidate => candidate.Id == selected.GpoId);
        if (gpo is null)
            return;

        if (MessageBox.Show(
                this,
                $"Delete Drive Maps preference '{selected.DisplayName}'?\n\n" +
                $"{selected.ActionDisplay}: {selected.DriveDisplay} -> {selected.Path}\n\n" +
                "A full GPO backup will be created first.",
                "Delete Drive Map",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        SetBusy(true, "Backing up GPO before deleting Drive Map...");

        try
        {
            var backup = await Task.Run(() =>
                _gpmService.BackupGpo(
                    _domainContext.DomainName,
                    gpo.Id,
                    "Automatic backup before deleting Drive Maps preference"));

            await Task.Run(() =>
                _gppDriveService.Delete(
                    gpo,
                    _domainContext.DomainDistinguishedName,
                    selected));

            _auditService.Write(
                "Delete",
                "GPP Drive Maps",
                gpo.DisplayName,
                $"Drive: {selected.DriveDisplay}; Path: {selected.Path}; Backup: {backup}",
                before: GppDriveSummary(selected),
                after: "<Removed>");

            if (_settings.Count > 0)
                await RefreshSingleGpoSettingsAsync(gpo);

            await LoadGppDrivesAsync();

            if (_gppDocumentInitialized)
                await LoadGppDocumentsAsync();

            StatusText.Text = $"Drive Map deleted. Backup: {backup}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                ex.Message,
                "Delete Drive Map",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            StatusText.Text = "Drive Maps delete failed";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void ShowGppDriveRawXml_Click(object sender, RoutedEventArgs e)
    {
        if (GppDrivesGrid.SelectedItem is not GppDriveItemInfo selected)
            return;

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
                .OrderBy(name => name.Equals("All", StringComparison.OrdinalIgnoreCase)
                    ? string.Empty
                    : name)
                .ToArray();

            GppXmlTypeCombo.ItemsSource = types;
            GppXmlTypeCombo.SelectedIndex = 0;
        }

        MainTabs.SelectedItem = GppXmlTab;
        await LoadGppDocumentsAsync();

        GppXmlSearchBox.Text = selected.GpoName;
        GppXmlScopeCombo.SelectedItem = "User";
        GppXmlTypeCombo.SelectedItem = "Drive Maps";
        _gppDocumentView?.Refresh();

        StatusText.Text =
            $"GPP XML filter set to Drive Maps for {selected.GpoName}";
    }

    private void GppDrivesSearchBox_TextChanged(
        object sender,
        System.Windows.Controls.TextChangedEventArgs e)
    {
        _gppDriveView?.Refresh();
        UpdateGppDriveCount();
    }

    private bool FilterGppDrive(object item)
    {
        if (item is not GppDriveItemInfo drive)
            return false;

        var search = GppDrivesSearchBox?.Text?.Trim();

        return string.IsNullOrWhiteSpace(search) ||
               drive.SearchText.Contains(
                   search,
                   StringComparison.CurrentCultureIgnoreCase);
    }

    private void UpdateGppDriveCount()
    {
        if (_gppDriveView is null)
            return;

        var shown = _gppDriveView.Cast<object>().Count();
        GppDrivesCountText.Text =
            $"{shown:N0} shown / {_gppDriveItems.Count:N0} total";
    }

    private static GppDriveItemInfo CopyDriveItem(
        GppDriveItemInfo source,
        bool preserveCredential)
    {
        var copy = new GppDriveItemInfo
        {
            GpoId = source.GpoId,
            GpoName = source.GpoName,
            DomainName = source.DomainName,
            XmlPath = source.XmlPath,
            Uid = source.Uid,
            Ordinal = source.Ordinal
        };

        CopyDriveValues(source, copy, preserveCredential);
        return copy;
    }

    private static void CopyDriveValues(
        GppDriveItemInfo source,
        GppDriveItemInfo destination,
        bool preserveCredential)
    {
        destination.DisplayName = source.DisplayName;
        destination.Action = source.Action;
        destination.Letter = source.Letter;
        destination.UseExactLetter = source.UseExactLetter;
        destination.Path = source.Path;
        destination.Label = source.Label;
        destination.Persistent = source.Persistent;
        destination.UserName = source.UserName;
        destination.ThisDriveVisibility = source.ThisDriveVisibility;
        destination.AllDrivesVisibility = source.AllDrivesVisibility;
        destination.Disabled = source.Disabled;
        destination.BypassErrors = source.BypassErrors;
        destination.RemoveWhenNoLongerApplied = source.RemoveWhenNoLongerApplied;
        destination.RunInUserContext = source.RunInUserContext;
        destination.FiltersXml = source.FiltersXml;
        destination.OpaqueCredential =
            preserveCredential ? source.OpaqueCredential : string.Empty;
        destination.ClearStoredCredential = false;
    }

    private static string GppDriveSummary(GppDriveItemInfo item) =>
        $"{item.ActionDisplay}; {item.DriveDisplay}; Path={item.Path}; " +
        $"Label={item.Label}; Persistent={item.Persistent}; " +
        $"UserName={item.UserName}; Targeting={item.HasFilters}; " +
        $"StoredCredential={item.HasStoredCredential}";
}

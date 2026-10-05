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
    private readonly GppServicePreferenceService _gppServicePreferenceService =
        new(new GppDocumentService());

    private readonly ObservableCollection<GppServiceItemInfo> _gppServiceItems = new();
    private ICollectionView? _gppServiceView;
    private CancellationTokenSource? _gppServiceCancellation;
    private bool _gppServiceInitialized;

    private void GppServicesTab_Loaded(object sender, RoutedEventArgs e)
    {
        if (_gppServiceInitialized)
            return;

        _gppServiceInitialized = true;
        _gppServiceView = CollectionViewSource.GetDefaultView(_gppServiceItems);
        _gppServiceView.Filter = FilterGppService;
        GppServicesGrid.ItemsSource = _gppServiceView;
    }

    private async void LoadGppServices_Click(object sender, RoutedEventArgs e)
    {
        await LoadGppServicesAsync();
    }

    private async Task LoadGppServicesAsync()
    {
        if (_gpos.Count == 0)
            return;

        _gppServiceCancellation?.Cancel();
        _gppServiceCancellation = new CancellationTokenSource();

        SetBusy(true, "Loading Services preferences...");

        try
        {
            var progress = new Progress<string>(message =>
            {
                StatusText.Text = message;
                HeaderStatusText.Text = message;
            });

            var rows = await Task.Run(() =>
                _gppServicePreferenceService.Load(
                    _gpos,
                    progress,
                    _gppServiceCancellation.Token));

            ReplaceCollection(_gppServiceItems, rows);
            _gppServiceView?.Refresh();
            UpdateGppServiceCount();

            HeaderStatusText.Text =
                $"{_gpos.Count:N0} GPOs | {_gppServiceItems.Count:N0} Services items";
            StatusText.Text = "Services preferences loaded";
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Services loading canceled";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                ex.Message,
                "Load Services Preferences",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            StatusText.Text = "Services preferences load failed";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void NewGppService_Click(object sender, RoutedEventArgs e)
    {
        if (_domainContext is null || _gpos.Count == 0)
            return;

        var picker = new GpoPickerWindow(
            _gpos,
            "New Services Preference",
            "Select the GPO that will contain the Computer Configuration Services preference:")
        {
            Owner = this
        };

        if (picker.ShowDialog() != true || picker.SelectedGpo is null)
            return;

        var item = _gppServicePreferenceService.CreateNew(picker.SelectedGpo);

        var editor = new GppServiceEditorWindow(item)
        {
            Owner = this
        };

        if (editor.ShowDialog() == true)
            await SaveGppServiceAsync(editor.Item, "Create");
    }

    private async void EditGppService_Click(object sender, RoutedEventArgs e)
    {
        await EditSelectedGppServiceAsync();
    }

    private async void GppServicesGrid_MouseDoubleClick(
        object sender,
        MouseButtonEventArgs e)
    {
        await EditSelectedGppServiceAsync();
    }

    private async Task EditSelectedGppServiceAsync()
    {
        if (GppServicesGrid.SelectedItem is not GppServiceItemInfo selected)
            return;

        var editable = CopyServiceItem(selected, preserveCredential: true);

        var editor = new GppServiceEditorWindow(editable)
        {
            Owner = this
        };

        if (editor.ShowDialog() == true)
            await SaveGppServiceAsync(editor.Item, "Edit");
    }

    private async void CloneGppService_Click(object sender, RoutedEventArgs e)
    {
        if (GppServicesGrid.SelectedItem is not GppServiceItemInfo selected)
            return;

        var sourceGpo = _gpos.FirstOrDefault(gpo => gpo.Id == selected.GpoId);

        var picker = new GpoPickerWindow(
            _gpos,
            "Clone Services Preference",
            "Select the target GPO. The cloned item will be created in Computer Configuration:",
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
                "The source Services item contains a legacy stored cPassword value. " +
                "For safety, the cloned item will not copy that credential.",
                "Clone Services Preference",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        var clone = _gppServicePreferenceService.CreateNew(picker.SelectedGpo);
        CopyServiceValues(selected, clone, preserveCredential: false);
        clone.Uid = Guid.NewGuid().ToString("B").ToUpperInvariant();

        var editor = new GppServiceEditorWindow(clone)
        {
            Owner = this
        };

        if (editor.ShowDialog() == true)
            await SaveGppServiceAsync(editor.Item, "Clone");
    }

    private async Task SaveGppServiceAsync(
        GppServiceItemInfo item,
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
                "Services Preference",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        SetBusy(true, "Backing up GPO before Services change...");

        try
        {
            var backup = await Task.Run(() =>
                _gpmService.BackupGpo(
                    _domainContext.DomainName,
                    gpo.Id,
                    $"Automatic backup before {action.ToLowerInvariant()} Services preference"));

            StatusText.Text = "Saving Services preference...";

            await Task.Run(() =>
                _gppServicePreferenceService.Save(
                    gpo,
                    _domainContext.DomainDistinguishedName,
                    item));

            _auditService.Write(
                action,
                "GPP Services",
                gpo.DisplayName,
                $"Service: {item.ServiceName}; Service action: {item.ServiceAction}; " +
                $"Startup: {item.StartupType}; Account: {item.AccountName}; " +
                $"Targeting: {item.HasFilters}; Backup: {backup}",
                after: GppServiceSummary(item));

            if (_settings.Count > 0)
                await RefreshSingleGpoSettingsAsync(gpo);

            await LoadGppServicesAsync();

            if (_gppDocumentInitialized)
                await LoadGppDocumentsAsync();

            StatusText.Text =
                $"{action} Services preference completed. Backup: {backup}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                ex.Message,
                "Save Services Preference",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            StatusText.Text = "Services preference change failed";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void DeleteGppService_Click(object sender, RoutedEventArgs e)
    {
        if (_domainContext is null ||
            GppServicesGrid.SelectedItem is not GppServiceItemInfo selected)
            return;

        var gpo = _gpos.FirstOrDefault(candidate => candidate.Id == selected.GpoId);
        if (gpo is null)
            return;

        if (MessageBox.Show(
                this,
                $"Delete Services preference '{selected.DisplayName}'?\n\n" +
                $"Service: {selected.ServiceName}\n" +
                $"Action: {selected.ServiceAction}\n" +
                $"Startup: {selected.StartupType}\n\n" +
                "A full GPO backup will be created first.",
                "Delete Services Preference",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        SetBusy(true, "Backing up GPO before deleting Services preference...");

        try
        {
            var backup = await Task.Run(() =>
                _gpmService.BackupGpo(
                    _domainContext.DomainName,
                    gpo.Id,
                    "Automatic backup before deleting Services preference"));

            await Task.Run(() =>
                _gppServicePreferenceService.Delete(
                    gpo,
                    _domainContext.DomainDistinguishedName,
                    selected));

            _auditService.Write(
                "Delete",
                "GPP Services",
                gpo.DisplayName,
                $"Service: {selected.ServiceName}; Backup: {backup}",
                before: GppServiceSummary(selected),
                after: "<Removed>");

            if (_settings.Count > 0)
                await RefreshSingleGpoSettingsAsync(gpo);

            await LoadGppServicesAsync();

            if (_gppDocumentInitialized)
                await LoadGppDocumentsAsync();

            StatusText.Text = $"Services preference deleted. Backup: {backup}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                ex.Message,
                "Delete Services Preference",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            StatusText.Text = "Services preference delete failed";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void ShowGppServiceRawXml_Click(object sender, RoutedEventArgs e)
    {
        if (GppServicesGrid.SelectedItem is not GppServiceItemInfo selected)
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
        GppXmlScopeCombo.SelectedItem = "Computer";
        GppXmlTypeCombo.SelectedItem = "Services";
        _gppDocumentView?.Refresh();

        StatusText.Text =
            $"GPP XML filter set to Services for {selected.GpoName}";
    }

    private void GppServicesSearchBox_TextChanged(
        object sender,
        System.Windows.Controls.TextChangedEventArgs e)
    {
        _gppServiceView?.Refresh();
        UpdateGppServiceCount();
    }

    private bool FilterGppService(object item)
    {
        if (item is not GppServiceItemInfo service)
            return false;

        var search = GppServicesSearchBox?.Text?.Trim();

        return string.IsNullOrWhiteSpace(search) ||
               service.SearchText.Contains(
                   search,
                   StringComparison.CurrentCultureIgnoreCase);
    }

    private void UpdateGppServiceCount()
    {
        if (_gppServiceView is null)
            return;

        var shown = _gppServiceView.Cast<object>().Count();
        GppServicesCountText.Text =
            $"{shown:N0} shown / {_gppServiceItems.Count:N0} total";
    }

    private static GppServiceItemInfo CopyServiceItem(
        GppServiceItemInfo source,
        bool preserveCredential)
    {
        var copy = new GppServiceItemInfo
        {
            GpoId = source.GpoId,
            GpoName = source.GpoName,
            DomainName = source.DomainName,
            XmlPath = source.XmlPath,
            Uid = source.Uid,
            Ordinal = source.Ordinal
        };

        CopyServiceValues(source, copy, preserveCredential);
        return copy;
    }

    private static void CopyServiceValues(
        GppServiceItemInfo source,
        GppServiceItemInfo destination,
        bool preserveCredential)
    {
        destination.DisplayName = source.DisplayName;
        destination.ServiceName = source.ServiceName;
        destination.ServiceAction = source.ServiceAction;
        destination.StartupType = source.StartupType;
        destination.Timeout = source.Timeout;
        destination.AccountName = source.AccountName;
        destination.InteractWithDesktop = source.InteractWithDesktop;
        destination.FirstFailure = source.FirstFailure;
        destination.SecondFailure = source.SecondFailure;
        destination.ThirdFailure = source.ThirdFailure;
        destination.ResetFailCountDelay = source.ResetFailCountDelay;
        destination.RestartServiceDelay = source.RestartServiceDelay;
        destination.RestartComputerDelay = source.RestartComputerDelay;
        destination.RestartMessage = source.RestartMessage;
        destination.Program = source.Program;
        destination.Arguments = source.Arguments;
        destination.AppendArguments = source.AppendArguments;
        destination.Disabled = source.Disabled;
        destination.BypassErrors = source.BypassErrors;
        destination.RemoveWhenNoLongerApplied = source.RemoveWhenNoLongerApplied;
        destination.RunInUserContext = source.RunInUserContext;
        destination.FiltersXml = source.FiltersXml;
        destination.OpaqueCredential =
            preserveCredential ? source.OpaqueCredential : string.Empty;
        destination.ClearStoredCredential = false;
    }

    private static string GppServiceSummary(GppServiceItemInfo item) =>
        $"Service={item.ServiceName}; Action={item.ServiceAction}; " +
        $"Startup={item.StartupType}; Timeout={item.Timeout}; " +
        $"Account={item.AccountName}; Recovery={item.FirstFailure}/{item.SecondFailure}/{item.ThirdFailure}; " +
        $"Targeting={item.HasFilters}; StoredCredential={item.HasStoredCredential}";
}

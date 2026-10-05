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
    private readonly GppPrinterService _gppPrinterService =
        new(new GppDocumentService());

    private readonly ObservableCollection<GppPrinterItemInfo>
        _gppPrinterItems = new();

    private ICollectionView? _gppPrinterView;
    private CancellationTokenSource? _gppPrinterCancellation;
    private bool _gppPrinterInitialized;

    private void GppPrintersTab_Loaded(
        object sender,
        RoutedEventArgs e)
    {
        if (_gppPrinterInitialized)
            return;

        _gppPrinterInitialized = true;

        _gppPrinterView =
            CollectionViewSource.GetDefaultView(
                _gppPrinterItems);

        _gppPrinterView.Filter =
            FilterGppPrinter;

        GppPrintersGrid.ItemsSource =
            _gppPrinterView;

        GppPrintersScopeCombo.ItemsSource =
            new[]
            {
                "All",
                "Computer",
                "User"
            };

        GppPrintersScopeCombo.SelectedIndex =
            0;

        GppPrintersTypeCombo.ItemsSource =
            new[]
            {
                "All",
                "Shared",
                "TCP/IP",
                "Local"
            };

        GppPrintersTypeCombo.SelectedIndex =
            0;
    }

    private async void LoadGppPrinters_Click(
        object sender,
        RoutedEventArgs e)
    {
        await LoadGppPrintersAsync();
    }

    private async Task LoadGppPrintersAsync()
    {
        if (_gpos.Count == 0)
            return;

        _gppPrinterCancellation?.Cancel();
        _gppPrinterCancellation =
            new CancellationTokenSource();

        SetBusy(
            true,
            "Loading Printers preferences...");

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
                        _gppPrinterService.Load(
                            _gpos,
                            progress,
                            _gppPrinterCancellation.Token));

            ReplaceCollection(
                _gppPrinterItems,
                rows);

            _gppPrinterView?.Refresh();

            UpdateGppPrinterCount();

            HeaderStatusText.Text =
                $"{_gpos.Count:N0} GPOs | {_gppPrinterItems.Count:N0} Printer items";

            StatusText.Text =
                "Printers preferences loaded";
        }
        catch (OperationCanceledException)
        {
            StatusText.Text =
                "Printers loading canceled";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                ex.Message,
                "Load Printers",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            StatusText.Text =
                "Printers load failed";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void NewGppPrinter_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_domainContext is null ||
            _gpos.Count == 0)
            return;

        var target =
            new GppPrinterTargetWindow(
                _gpos)
            {
                Owner = this
            };

        if (target.ShowDialog() != true ||
            target.SelectedGpo is null)
            return;

        var item =
            _gppPrinterService.CreateNew(
                target.SelectedGpo,
                target.SelectedScope,
                target.SelectedPrinterKind);

        var editor =
            new GppPrinterEditorWindow(
                item)
            {
                Owner = this
            };

        if (editor.ShowDialog() == true)
        {
            await SaveGppPrinterAsync(
                editor.Item,
                "Create");
        }
    }

    private async void EditGppPrinter_Click(
        object sender,
        RoutedEventArgs e)
    {
        await EditSelectedGppPrinterAsync();
    }

    private async void GppPrintersGrid_MouseDoubleClick(
        object sender,
        MouseButtonEventArgs e)
    {
        await EditSelectedGppPrinterAsync();
    }

    private async Task EditSelectedGppPrinterAsync()
    {
        if (GppPrintersGrid.SelectedItem
            is not GppPrinterItemInfo selected)
            return;

        var editable =
            CopyPrinterItem(
                selected);

        var editor =
            new GppPrinterEditorWindow(
                editable)
            {
                Owner = this
            };

        if (editor.ShowDialog() == true)
        {
            await SaveGppPrinterAsync(
                editor.Item,
                "Edit");
        }
    }

    private async void CloneGppPrinter_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (GppPrintersGrid.SelectedItem
            is not GppPrinterItemInfo selected)
            return;

        var sourceGpo =
            _gpos.FirstOrDefault(
                gpo =>
                    gpo.Id == selected.GpoId);

        var target =
            new GppScopeTargetWindow(
                _gpos,
                "Clone Printer Preference",
                $"Select the destination GPO and scope for the cloned {selected.PrinterKindDisplay} printer preference:",
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
                "The source shared printer contains legacy GPP cpassword data. " +
                "The cloned printer will not copy that credential. The account name can still be retained.",
                "Clone Printer",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        var clone =
            _gppPrinterService.CreateNew(
                target.SelectedGpo,
                target.SelectedScope,
                selected.PrinterKind);

        _gppPrinterService.CopyEditableValues(
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

        var editor =
            new GppPrinterEditorWindow(
                clone)
            {
                Owner = this
            };

        if (editor.ShowDialog() == true)
        {
            await SaveGppPrinterAsync(
                editor.Item,
                "Clone");
        }
    }

    private async Task SaveGppPrinterAsync(
        GppPrinterItemInfo item,
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
                "Printer",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return;
        }

        SetBusy(
            true,
            "Backing up GPO before Printer change...");

        try
        {
            var backup =
                await Task.Run(
                    () =>
                        _gpmService.BackupGpo(
                            _domainContext.DomainName,
                            gpo.Id,
                            $"Automatic backup before {action.ToLowerInvariant()} Printer preference"));

            await Task.Run(
                () =>
                    _gppPrinterService.Save(
                        gpo,
                        _domainContext.DomainDistinguishedName,
                        item));

            _auditService.Write(
                action,
                "GPP Printers",
                gpo.DisplayName,
                $"Scope: {item.Scope}; Type: {item.PrinterKindDisplay}; " +
                $"Action: {item.ActionDisplay}; Target: {item.TargetDisplay}; " +
                $"Default: {item.DefaultPrinter}; Targeting: {item.HasFilters}; " +
                $"Legacy credential preserved: {item.HasStoredCredential && !item.ClearStoredCredential}; " +
                $"Backup: {backup}",
                after:
                    GppPrinterSummary(
                        item));

            if (_settings.Count > 0)
            {
                await RefreshSingleGpoSettingsAsync(
                    gpo);
            }

            await LoadGppPrintersAsync();

            if (_gppDocumentInitialized)
            {
                await LoadGppDocumentsAsync();
            }

            StatusText.Text =
                $"{action} Printer preference completed. Backup: {backup}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                ex.Message,
                "Save Printer",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            StatusText.Text =
                "Printer change failed";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void DeleteGppPrinter_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_domainContext is null ||
            GppPrintersGrid.SelectedItem
                is not GppPrinterItemInfo selected)
            return;

        var gpo =
            _gpos.FirstOrDefault(
                candidate =>
                    candidate.Id == selected.GpoId);

        if (gpo is null)
            return;

        if (MessageBox.Show(
                this,
                $"Delete {selected.PrinterKindDisplay} Printer preference '{selected.DisplayName}'?\n\n" +
                $"{selected.ActionDisplay}: {selected.TargetDisplay}\n\n" +
                "This removes the preference item from the GPO. A full GPO backup will be created first.",
                "Delete Printer Preference",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) !=
            MessageBoxResult.Yes)
        {
            return;
        }

        SetBusy(
            true,
            "Backing up GPO before deleting Printer preference...");

        try
        {
            var backup =
                await Task.Run(
                    () =>
                        _gpmService.BackupGpo(
                            _domainContext.DomainName,
                            gpo.Id,
                            "Automatic backup before deleting Printer preference"));

            await Task.Run(
                () =>
                    _gppPrinterService.Delete(
                        gpo,
                        _domainContext.DomainDistinguishedName,
                        selected));

            _auditService.Write(
                "Delete",
                "GPP Printers",
                gpo.DisplayName,
                $"Scope: {selected.Scope}; Type: {selected.PrinterKindDisplay}; " +
                $"Target: {selected.TargetDisplay}; Backup: {backup}",
                before:
                    GppPrinterSummary(
                        selected),
                after:
                    "<Removed>");

            if (_settings.Count > 0)
            {
                await RefreshSingleGpoSettingsAsync(
                    gpo);
            }

            await LoadGppPrintersAsync();

            if (_gppDocumentInitialized)
            {
                await LoadGppDocumentsAsync();
            }

            StatusText.Text =
                $"Printer preference deleted. Backup: {backup}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                ex.Message,
                "Delete Printer Preference",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            StatusText.Text =
                "Printer preference delete failed";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void ShowGppPrinterRawXml_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (GppPrintersGrid.SelectedItem
            is not GppPrinterItemInfo selected)
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
            "Printers";

        _gppDocumentView?.Refresh();

        StatusText.Text =
            $"GPP XML filter set to Printers for {selected.GpoName}";
    }

    private void GppPrintersSearchBox_TextChanged(
        object sender,
        System.Windows.Controls.TextChangedEventArgs e)
    {
        _gppPrinterView?.Refresh();
        UpdateGppPrinterCount();
    }

    private void GppPrintersFilter_SelectionChanged(
        object sender,
        System.Windows.Controls.SelectionChangedEventArgs e)
    {
        _gppPrinterView?.Refresh();
        UpdateGppPrinterCount();
    }

    private bool FilterGppPrinter(
        object item)
    {
        if (item
            is not GppPrinterItemInfo printer)
            return false;

        var scope =
            Convert.ToString(
                GppPrintersScopeCombo?.SelectedItem);

        if (!string.IsNullOrWhiteSpace(scope) &&
            !scope.Equals(
                "All",
                StringComparison.OrdinalIgnoreCase) &&
            !printer.Scope.Equals(
                scope,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var type =
            Convert.ToString(
                GppPrintersTypeCombo?.SelectedItem);

        if (!string.IsNullOrWhiteSpace(type) &&
            !type.Equals(
                "All",
                StringComparison.OrdinalIgnoreCase) &&
            !printer.PrinterKindDisplay.Equals(
                type,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var search =
            GppPrintersSearchBox?
                .Text?
                .Trim();

        return string.IsNullOrWhiteSpace(search) ||
               printer.SearchText.Contains(
                   search,
                   StringComparison.CurrentCultureIgnoreCase);
    }

    private void UpdateGppPrinterCount()
    {
        if (GppPrintersCountText is null)
            return;

        var shown =
            _gppPrinterView?
                .Cast<object>()
                .Count()
            ?? 0;

        GppPrintersCountText.Text =
            $"{shown:N0} shown / {_gppPrinterItems.Count:N0} total";
    }

    private static GppPrinterItemInfo CopyPrinterItem(
        GppPrinterItemInfo source) =>
        new()
        {
            GpoId = source.GpoId,
            GpoName = source.GpoName,
            DomainName = source.DomainName,
            Scope = source.Scope,
            XmlPath = source.XmlPath,
            Uid = source.Uid,
            Ordinal = source.Ordinal,
            PrinterKind = source.PrinterKind,
            DisplayName = source.DisplayName,
            Description = source.Description,
            Action = source.Action,
            PrinterName = source.PrinterName,
            Path = source.Path,
            Port = source.Port,
            Location = source.Location,
            Comment = source.Comment,
            DefaultPrinter = source.DefaultPrinter,
            SkipLocal = source.SkipLocal,
            DeleteAll = source.DeleteAll,
            Persistent = source.Persistent,
            DeleteMaps = source.DeleteMaps,
            UserName = source.UserName,
            OpaqueCredential = source.OpaqueCredential,
            ClearStoredCredential = false,
            IpAddress = source.IpAddress,
            UseDns = source.UseDns,
            LocalName = source.LocalName,
            LprQueue = source.LprQueue,
            SnmpCommunity = source.SnmpCommunity,
            Protocol = source.Protocol,
            PortNumber = source.PortNumber,
            DoubleSpool = source.DoubleSpool,
            SnmpEnabled = source.SnmpEnabled,
            SnmpDevIndex = source.SnmpDevIndex,
            Disabled = source.Disabled,
            BypassErrors = source.BypassErrors,
            RemoveWhenNoLongerApplied =
                source.RemoveWhenNoLongerApplied,
            RunInUserContext =
                source.RunInUserContext,
            FiltersXml = source.FiltersXml
        };

    private static string GppPrinterSummary(
        GppPrinterItemInfo item) =>
        $"Scope={item.Scope}; Type={item.PrinterKindDisplay}; " +
        $"Action={item.ActionDisplay}; Target={item.TargetDisplay}; " +
        $"Path={item.Path}; Port={item.Port}; Default={item.DefaultPrinter}; " +
        $"DeleteAll={item.DeleteAll}; Disabled={item.Disabled}; " +
        $"LegacyCredentialPresent={item.HasStoredCredential}; Targeting={item.HasFilters}";
}

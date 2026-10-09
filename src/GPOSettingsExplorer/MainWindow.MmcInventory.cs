using System.ComponentModel;
using System.Text;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using GPOSettingsExplorer.Models;
using GPOSettingsExplorer.Services;
using Microsoft.Win32;

namespace GPOSettingsExplorer;

public partial class MainWindow
{
    private IReadOnlyList<MmcInventoryEntry> _mmcInventoryRows =
        Array.Empty<MmcInventoryEntry>();
    private ICollectionView? _mmcInventoryView;
    private MmcInventoryScanResult? _mmcInventoryResult;
    private CancellationTokenSource? _mmcInventoryCancellation;
    private readonly GpoEditorNavigatorService _mmcInventoryNavigator = new();

    private void InitializeMmcInventoryUi()
    {
        _mmcInventoryView = CollectionViewSource.GetDefaultView(_mmcInventoryRows);
        _mmcInventoryView.Filter = IsMmcInventoryMatch;
        MmcInventoryGrid.ItemsSource = _mmcInventoryView;
    }

    private async void ScanMmcInventory_Click(object sender, RoutedEventArgs e)
    {
        if (_mmcInventoryCancellation is not null || _domainContext is null)
            return;

        var gpo = MmcInventoryGpoCombo.SelectedItem as GpoInfo ??
                  GpoGrid.SelectedItem as GpoInfo ??
                  _gpos.FirstOrDefault();

        if (gpo is null)
        {
            MessageBox.Show(this, "Load and select a reference GPO.",
                "MMC inventory", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        MmcInventoryGpoCombo.SelectedItem = gpo;
        var consent = MessageBox.Show(this,
            $"Scan MMC's real tree and right-hand native lists for:\n\n{gpo.DisplayName}\n\n" +
            "This read-only scan may take several minutes. It launches a separate GPO editor " +
            "and leaves it open for manual verification. MMC may contain custom controls " +
            "that cannot be read; all such coverage gaps will be reported.\n\n" +
            "No GPO settings will be written. Continue?",
            "MMC Full Settings Inventory", MessageBoxButton.YesNo,
            MessageBoxImage.Information);
        if (consent != MessageBoxResult.Yes)
            return;

        _mmcInventoryCancellation = new CancellationTokenSource();
        var cancellation = _mmcInventoryCancellation;
        MmcInventoryScanButton.IsEnabled = false;
        MmcInventoryCancelButton.IsEnabled = true;
        MmcInventoryCoverageText.Foreground = UiStyle.WarningBrush;
        MmcInventoryCoverageText.Text = "Opening MMC... inventory is read-only.";
        SetBusy(true, "Scanning MMC settings...");

        try
        {
            // The existing GPMC index is used only for correlation, not as the
            // MMC source. Avoid forcing a domain-wide reindex on every scan.
            // ADMX is cached and may be unavailable without blocking MMC.
            try
            {
                await EnsureAdmxCatalogAsync();
            }
            catch (Exception ex)
            {
                StatusText.Text = "ADMX correlation unavailable: " + ex.Message;
            }

            var configured = _settings.Where(setting => setting.GpoId == gpo.Id).ToArray();
            var admx = _admxPolicies?.ToArray();
            var progress = new Progress<string>(message =>
            {
                StatusText.Text = message;
                MmcInventoryCoverageText.Text = message;
            });
            var result = await MmcFullSettingsInventoryService.ScanAsync(
                gpo, _domainContext.DomainDistinguishedName,
                configured, admx, progress, cancellation.Token);

            _mmcInventoryResult = result;
            // Replace the entire immutable scan snapshot rather than raising
            // tens of thousands of individual ObservableCollection events.
            _mmcInventoryRows = result.Rows;
            _mmcInventoryView = CollectionViewSource.GetDefaultView(_mmcInventoryRows);
            _mmcInventoryView.Filter = IsMmcInventoryMatch;
            MmcInventoryGrid.ItemsSource = _mmcInventoryView;
            MmcInventorySectionsGrid.ItemsSource = result.Sections;
            UpdateMmcInventoryCount();
            await RefreshUnifiedCatalogAsync();
            MmcInventoryCoverageText.Text = result.Coverage +
                (configured.Length == 0
                    ? " | GPMC configured index not available for this GPO."
                    : $" | GPMC configured rows for comparison: {configured.Length:N0}.") +
                (admx is null ? " | ADMX catalog not loaded." : "");
            MmcInventoryCoverageText.Foreground =
                result.IsComplete ? UiStyle.SuccessBrush : UiStyle.WarningBrush;
            StatusText.Text = "MMC inventory: " + result.Coverage;
        }
        catch (OperationCanceledException)
        {
            MmcInventoryCoverageText.Text =
                "MMC inventory canceled during startup. Previous inventory, if any, is unchanged.";
            StatusText.Text = "MMC inventory canceled";
        }
        catch (Exception ex)
        {
            MmcInventoryCoverageText.Text = "MMC inventory failed: " + ex.Message;
            MmcInventoryCoverageText.Foreground = UiStyle.ErrorBrush;
            ErrorDialog.Show(this, "MMC Inventory",
                "Could not complete the read-only MMC inventory.", ex);
        }
        finally
        {
            _mmcInventoryCancellation = null;
            cancellation.Dispose();
            MmcInventoryScanButton.IsEnabled = true;
            MmcInventoryCancelButton.IsEnabled = false;
            SetBusy(false);
        }
    }

    private void CancelMmcInventory_Click(object sender, RoutedEventArgs e)
    {
        _mmcInventoryCancellation?.Cancel();
        StatusText.Text = "Canceling MMC inventory...";
    }

    private void MmcInventorySearchBox_TextChanged(
        object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        _mmcInventoryView?.Refresh();
        UpdateMmcInventoryCount();
    }

    private bool IsMmcInventoryMatch(object item)
    {
        if (item is not MmcInventoryEntry row)
            return false;
        var term = MmcInventorySearchBox?.Text.Trim();
        return string.IsNullOrWhiteSpace(term) ||
               row.SearchText.Contains(term, StringComparison.CurrentCultureIgnoreCase);
    }

    private void UpdateMmcInventoryCount()
    {
        if (MmcInventoryShownCountText is null)
            return;
        var count = _mmcInventoryView?.Cast<object>().Count() ?? 0;
        MmcInventoryShownCountText.Text =
            $"{count:N0} shown / {_mmcInventoryRows.Count:N0} observed";
    }

    private async void OpenMmcInventoryRow_Click(object sender, RoutedEventArgs e) =>
        await OpenSelectedMmcInventoryRowAsync();

    private async void MmcInventoryGrid_MouseDoubleClick(
        object sender, MouseButtonEventArgs e) =>
        await OpenSelectedMmcInventoryRowAsync();

    private async Task OpenSelectedMmcInventoryRowAsync()
    {
        if (_domainContext is null ||
            MmcInventoryGrid.SelectedItem is not MmcInventoryEntry row)
            return;

        if (row.Navigation != "Exact MMC row candidate")
        {
            MessageBox.Show(this,
                "This MMC row cannot be opened as a uniquely identified setting. " +
                "Open the corresponding section manually instead; no approximate target will be selected.",
                "Exact MMC navigation unavailable",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var gpo = _gpos.FirstOrDefault(item => item.Id == row.GpoId);
        if (gpo is null)
        {
            MessageBox.Show(this, "The reference GPO is no longer loaded.",
                "MMC inventory", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (MessageBox.Show(this,
            $"Open exact MMC row in a new Group Policy editor?\n\n" +
            $"GPO: {gpo.DisplayName}\nSection: {row.SectionPath}\nRow: {row.SettingName}\n\n" +
            "This opens the native editor without automatically saving a policy change.",
            "Open exact MMC setting",
            MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        SetBusy(true, "Navigating MMC to selected row...");
        try
        {
            var progress = new Progress<string>(message => StatusText.Text = message);
            var success = await _mmcInventoryNavigator.OpenInventoryEntryAsync(
                gpo, _domainContext.DomainDistinguishedName, row, progress);
            if (!success)
                MessageBox.Show(this,
                    "The row or its MMC section could not be verified exactly. " +
                    "No substitute row was opened. Review navigation diagnostics or open the native editor manually.",
                    "MMC navigation incomplete",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            ErrorDialog.Show(this, "MMC navigation", "Could not open the exact MMC row.", ex);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void ExportMmcInventory_Click(object sender, RoutedEventArgs e)
    {
        if (_mmcInventoryResult is null)
        {
            MessageBox.Show(this, "Scan MMC first.", "MMC inventory",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Export MMC observed rows and coverage",
            Filter = "CSV files (*.csv)|*.csv",
            FileName = $"MMC-inventory-{DateTime.Now:yyyyMMdd-HHmmss}.csv",
            InitialDirectory = StoragePaths.Exports,
            AddExtension = true,
            DefaultExt = ".csv"
        };
        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            using var writer = new StreamWriter(dialog.FileName, false,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            writer.WriteLine(
                "RecordType,GPO,Scope,State,Name,SectionPath,MMCValue,GPMC,ADMX,Navigation,Source,CoverageStatus,Details");

            foreach (var row in _mmcInventoryView!.Cast<object>().OfType<MmcInventoryEntry>())
            {
                writer.WriteLine(string.Join(",", new[]
                {
                    Csv("ROW"), Csv(row.GpoName), Csv(row.Scope), Csv(row.MmcState),
                    Csv(row.SettingName), Csv(row.SectionPath), Csv(row.MmcValue),
                    Csv(row.GpmcMatch), Csv(row.AdmxMatch),
                    Csv(row.Navigation), Csv(row.Source), Csv(""), Csv("")
                }));
            }

            foreach (var section in _mmcInventoryResult.Sections)
            {
                writer.WriteLine(string.Join(",", new[]
                {
                    Csv("SECTION"), Csv(""), Csv(""), Csv(""),
                    Csv(""), Csv(section.Path), Csv(""), Csv(""), Csv(""),
                    Csv(""), Csv(""), Csv(section.Status), Csv(section.Details)
                }));
            }

            writer.WriteLine(string.Join(",", new[]
            {
                Csv("SCAN SUMMARY"), Csv(""), Csv(""), Csv(""),
                Csv(""), Csv(""), Csv(""), Csv(""), Csv(""),
                Csv(""), Csv(""), Csv(_mmcInventoryResult.IsComplete
                    ? "No detected read failures" : "PARTIAL"),
                Csv(_mmcInventoryResult.Coverage)
            }));
            StatusText.Text = "Exported MMC inventory and coverage: " + dialog.FileName;
        }
        catch (Exception ex)
        {
            ErrorDialog.Show(this, "Export MMC inventory", "Could not write inventory CSV.", ex);
        }
    }
}

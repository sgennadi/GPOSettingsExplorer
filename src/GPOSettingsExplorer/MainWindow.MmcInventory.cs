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
    private Guid? _mmcInventoryScannedGpoId;
    private string _mmcInventoryScannedDomain = string.Empty;
    private string _mmcInventoryScannedDc = string.Empty;
    private CancellationTokenSource? _mmcInventoryCancellation;
    private readonly GpoEditorNavigatorService _mmcInventoryNavigator = new();
    private bool _autoGpmcIndexOnStartup;

    private void InitializeMmcInventoryUi()
    {
        _autoGpmcIndexOnStartup = GpoStartupIndexPreferences.Load();
        AutoGpmcIndexCheckBox.IsChecked = _autoGpmcIndexOnStartup;
        _mmcInventoryView = CollectionViewSource.GetDefaultView(_mmcInventoryRows);
        _mmcInventoryView.Filter = IsMmcInventoryMatch;
        MmcInventoryGrid.ItemsSource = _mmcInventoryView;
    }

    private async void AutoGpmcIndexCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || AutoGpmcIndexCheckBox is null)
            return;

        var requested = AutoGpmcIndexCheckBox.IsChecked == true;
        if (requested == _autoGpmcIndexOnStartup)
            return;

        try
        {
            GpoStartupIndexPreferences.Save(requested);
            _autoGpmcIndexOnStartup = requested;
            StatusText.Text = requested
                ? "Cached GPMC XML indexing enabled for startup. This will NOT auto-open MMC or prove RSoP."
                : "Automatic GPMC settings indexing disabled. Manual indexing remains available.";

            if (requested && _domainContext is not null &&
                _gpmService.IsAvailable && _gpos.Count > 0)
                await EnsureSettingsIndexAsync(forceRebuild: false);
        }
        catch (Exception ex)
        {
            AutoGpmcIndexCheckBox.IsChecked = _autoGpmcIndexOnStartup;
            MessageBox.Show(this, ex.Message,
                "Could not save background GPMC indexing preference",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void ScanMmcInventory_Click(object sender, RoutedEventArgs e) =>
        await ScanMmcInventoryAsync();

    private bool HasCurrentMmcInventoryFor(GpoInfo gpo)
    {
        if (_domainContext is null)
            return false;
        var domain = _domainContext.DomainName;
        var dc = DomainConnectionState.GetServerFor(domain);
        // Aborted snapshots remain visible in Coverage but are NEVER
        // permitted as exact-row evidence for follow-up navigation.
        return _mmcInventoryResult is { HasReusableRowEvidence: true } &&
            _mmcInventoryScannedGpoId == gpo.Id &&
            _mmcInventoryScannedDomain.Equals(domain, StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(dc) &&
            _mmcInventoryScannedDc.Equals(dc, StringComparison.OrdinalIgnoreCase) &&
            _mmcInventoryRows.All(row => row.GpoId == gpo.Id);
    }

    /// <summary>
    /// One explicit, cancelable, read-only native scan of one selected GPO.
    /// It is NEVER invoked on all GPOs at startup.
    /// </summary>
    private async Task<bool> ScanMmcInventoryAsync(
        GpoInfo? targetGpo = null, Window? dialogOwner = null,
        IProgress<string>? externalProgress = null)
    {
        if (_mmcInventoryCancellation is not null || _domainContext is null)
            return false;

        var gpo = targetGpo ??
                  MmcInventoryGpoCombo.SelectedItem as GpoInfo ??
                  GpoGrid.SelectedItem as GpoInfo ??
                  _gpos.FirstOrDefault();

        if (gpo is null)
        {
            MessageBox.Show(dialogOwner ?? this, "Load and select a reference GPO.",
                "MMC inventory", MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }

        MmcInventoryGpoCombo.SelectedItem = gpo;
        var scanDomain = _domainContext.DomainName;
        var scanDc = DomainConnectionState.GetServerFor(scanDomain);
        if (string.IsNullOrWhiteSpace(scanDc) ||
            !gpo.DomainName.Equals(scanDomain, StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show(dialogOwner ?? this,
                "A connected and pinned DC matching the chosen GPO is required.",
                "MMC inventory", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
        var consent = MessageBox.Show(dialogOwner ?? this,
            $"Scan MMC's real tree and right-hand native lists for:\n\n{gpo.DisplayName}\n\n" +
            "This read-only scan may take several minutes. It launches a separate GPO editor " +
            "and leaves it open for manual verification. MMC may contain custom controls " +
            "that cannot be read; all such coverage gaps will be reported.\n\n" +
            "No GPO settings will be written. Continue?",
            "MMC Full Settings Inventory", MessageBoxButton.YesNo,
            MessageBoxImage.Information);
        if (consent != MessageBoxResult.Yes)
            return false;

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
                externalProgress?.Report(message);
            });
            var result = await MmcFullSettingsInventoryService.ScanAsync(
                gpo, _domainContext.DomainDistinguishedName,
                configured, admx, progress, cancellation.Token);

            var current = DomainConnectionState.Context;
            if (current is null ||
                !current.DomainName.Equals(scanDomain, StringComparison.OrdinalIgnoreCase) ||
                !current.ConnectedServer.Equals(scanDc, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    "The domain/DC session changed during the MMC scan. " +
                    "This observation cannot be combined with the old source snapshot.");

            _mmcInventoryResult = result;
            _mmcInventoryScannedGpoId = gpo.Id;
            _mmcInventoryScannedDomain = scanDomain;
            _mmcInventoryScannedDc = scanDc;
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
            MmcInventoryCoverageText.Foreground = result.Interrupted
                ? UiStyle.ErrorBrush
                : result.IsComplete ? UiStyle.SuccessBrush : UiStyle.WarningBrush;
            if (result.Interrupted)
            {
                MmcInventoryCoverageText.Text =
                    "STOPPED / PARTIAL: Native MMC could not be safely scanned. " +
                    "Previously captured rows remain visible for troubleshooting but " +
                    "CANNOT be used to verify or open exact settings. " +
                    "Do not select 'always ignore errors' in MMC. " +
                    result.Coverage;
                StatusText.Text = "MMC scan STOPPED / PARTIAL - not eligible for exact navigation.";
                externalProgress?.Report(MmcInventoryCoverageText.Text);
                return false;
            }

            StatusText.Text = "MMC inventory: " + result.Coverage;
            return true;
        }
        catch (OperationCanceledException)
        {
            MmcInventoryCoverageText.Text =
                "MMC inventory canceled during startup. Previous inventory, if any, is unchanged.";
            StatusText.Text = "MMC inventory canceled";
            return false;
        }
        catch (Exception ex)
        {
            MmcInventoryCoverageText.Text = "MMC inventory failed: " + ex.Message;
            MmcInventoryCoverageText.Foreground = UiStyle.ErrorBrush;
            ErrorDialog.Show(dialogOwner ?? this, "MMC Inventory",
                "Could not complete the read-only MMC inventory.", ex);
            return false;
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

    private sealed record DiscoverySnapshot(
        IReadOnlyList<GpoDiscoveryEvidenceService.Evidence> Rows,
        string Domain,
        string DomainController,
        string Coverage);

    /// <summary>
    /// Only correlate previously captured, same-GPO, same-domain, pinned-DC
    /// snapshots. This never launches MMC or reads fresh SYSVOL implicitly.
    /// </summary>
    private DiscoverySnapshot CreateDiscoverySnapshot(GpoInfo gpo)
    {
        var configured = _settings.Where(row => row.GpoId == gpo.Id).ToArray();
        var domain = _domainContext?.DomainName ?? "";
        var dc = DomainConnectionState.GetServerFor(domain);
        var mmcAvailable = _mmcInventoryResult is { HasReusableRowEvidence: true } &&
            _mmcInventoryScannedGpoId == gpo.Id &&
            _mmcInventoryScannedDomain.Equals(domain, StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(dc) &&
            _mmcInventoryScannedDc.Equals(dc, StringComparison.OrdinalIgnoreCase) &&
            _mmcInventoryRows.All(row => row.GpoId == gpo.Id);
        var sourceAvailable = _realSourceSnapshot is not null &&
            _realSourceSnapshot.GpoId == gpo.Id &&
            _realSourceSnapshot.Matches(domain, dc);

        var evidence = GpoDiscoveryEvidenceService.CorrelateMmc(
            GpoDiscoveryEvidenceService.Build(configured),
            mmcAvailable ? _mmcInventoryRows : Array.Empty<MmcInventoryEntry>(),
            _admxPolicies);
        if (sourceAvailable)
            evidence = GpoDiscoveryEvidenceService.CorrelateRegistrySource(
                evidence, GpoDiscoverySourceFreshnessService
                    .SelectCompleteRegistryRecords(_realSourceSnapshot!));
        evidence = GpoDiscoveryEvidenceService.VerifyLiteralRegistryValues(evidence);

        var coverage = $"{configured.Length:N0} GPMC index rows; " +
            (mmcAvailable
                ? $"{_mmcInventoryRows.Count:N0} MMC rows ({(_mmcInventoryResult!.IsComplete ? "complete" : "PARTIAL")})"
                : "MMC not captured for this GPO") + "; " +
            (sourceAvailable
                ? $"{GpoDiscoverySourceFreshnessService.SelectCompleteRegistryRecords(_realSourceSnapshot!).Count:N0} complete Registry.pol records / " +
                  $"{_realSourceSnapshot.Rows.Count:N0} total source rows " +
                  (_realSourceSnapshot.IsPartial ? "(PARTIAL)" : "(available files scanned)")
                : "Registry.pol source not captured for this GPO/pinned DC") + "; " +
            (_admxPolicies is null ? "ADMX missing" : "ADMX catalog loaded");
        return new DiscoverySnapshot(evidence, domain, dc, coverage);
    }

    private async void OpenDiscoveryVerification_Click(object sender, RoutedEventArgs e)
    {
        var gpo = MmcInventoryGpoCombo.SelectedItem as GpoInfo ??
                  GpoGrid.SelectedItem as GpoInfo;
        if (_domainContext is null || gpo is null)
        {
            MessageBox.Show(this, "Connect to the domain and select a specific GPO.",
                "Discovery & Verification", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!_settings.Any(setting => setting.GpoId == gpo.Id))
        {
            MessageBox.Show(this,
                "Load the GPMC settings index for this GPO first. " +
                "No indexed rows does not establish that the GPO is empty.",
                "Discovery & Verification", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // ADMX loading is optional: without it, all unit reviews are blocked
        // but ordinary source/MMC evidence remains visible.
        if (_admxPolicies is null)
        {
            try
            {
                await EnsureAdmxCatalogAsync();
            }
            catch (Exception ex)
            {
                StatusText.Text = "ADMX unavailable; unit verification blocked: " + ex.Message;
            }
        }

        try
        {
            var snapshot = CreateDiscoverySnapshot(gpo);
            var window = new GpoDiscoveryVerificationWindow(
                gpo, snapshot.Domain, snapshot.DomainController,
                snapshot.Rows, _admxPolicies,
                _admxCatalogService.LastSourcePath,
                _admxCatalogService.LastLanguage, snapshot.Coverage)
            {
                Owner = this
            };
            window.ShowDialog();
        }
        catch (Exception ex)
        {
            ErrorDialog.Show(this, "Discovery & Verification",
                "Unable to open the read-only evidence review.", ex);
        }
    }

    private void ExportDiscoveryEvidence_Click(object sender, RoutedEventArgs e)
    {
        var gpo = MmcInventoryGpoCombo.SelectedItem as GpoInfo ??
                  GpoGrid.SelectedItem as GpoInfo;
        if (gpo is null)
        {
            MessageBox.Show(this, "Select a GPO first.", "Discovery evidence",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var configured = _settings.Where(s => s.GpoId == gpo.Id).ToArray();
        if (configured.Length == 0)
        {
            MessageBox.Show(this,
                "No configured settings are currently indexed for this GPO. Load the settings index first. This is not proof the GPO is empty.",
                "Discovery evidence", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (MessageBox.Show(this,
            "The evidence export can contain raw Group Policy values, registry paths and computer/security configuration. " +
            "Treat the resulting TSV as confidential. Do not upload it to public repositories or share it outside authorized administrators.\\n\\n" +
            "Continue with local export?", "Sensitive policy data",
            MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        var dialog = new SaveFileDialog
        {
            Title = "Export GPO discovery evidence (read-only)",
            FileName = "gpo-discovery-" + gpo.Id.ToString("N") + ".tsv",
            Filter = "Tab-separated evidence (*.tsv)|*.tsv",
            DefaultExt = ".tsv",
            AddExtension = true
        };
        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            var snapshot = CreateDiscoverySnapshot(gpo);
            System.IO.File.WriteAllText(dialog.FileName,
                GpoDiscoveryEvidenceService.ExportTsv(snapshot.Rows),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            StatusText.Text = $"Exported {snapshot.Rows.Count:N0} discovery evidence records (limited literal comparisons; not effective RSoP).";
        }
        catch (Exception ex)
        {
            ErrorDialog.Show(this, "Discovery evidence",
                "Could not export the discovery evidence.", ex);
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

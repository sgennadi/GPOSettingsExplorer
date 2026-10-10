using System.ComponentModel;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using GPOSettingsExplorer.Models;
using GPOSettingsExplorer.Services;
using Microsoft.Win32;

namespace GPOSettingsExplorer;

/// <summary>
/// Primary All Settings workspace. GPMC/ADMX/MMC are evidence sources, not
/// competing navigation pages. The original source grids remain available
/// under Advanced sources for diagnostics and compatibility.
/// </summary>
public partial class MainWindow
{
    private readonly SemaphoreSlim _unifiedCatalogGate = new(1, 1);
    private IReadOnlyList<UnifiedSettingInfo> _unifiedRows =
        Array.Empty<UnifiedSettingInfo>();
    private ICollectionView? _unifiedView;
    private UnifiedCatalogResult? _unifiedCatalog;
    private long _unifiedBuildVersion;
    private bool _unifiedInitialized;
    private RealSettingsScanResult? _realSourceSnapshot;
    private CancellationTokenSource? _realSourceCancellation;

    private void InitializeUnifiedSettingsUi()
    {
        UnifiedSourceCombo.ItemsSource = new[]
        {
            "All sources",
            "Configured (GPMC)",
            "ADMX templates",
            "MMC observed",
            "Stored GPO files (read only)"
        };
        UnifiedStateCombo.ItemsSource = new[]
        {
            "All states",
            "Configured only",
            "MMC Not Configured",
            "Unknown / templates",
            "Stored source values"
        };
        UnifiedSourceCombo.SelectedIndex = 0;
        UnifiedStateCombo.SelectedIndex = 0;

        _unifiedView = CollectionViewSource.GetDefaultView(_unifiedRows);
        _unifiedView.Filter = IsUnifiedRowVisible;
        UnifiedSettingsGrid.ItemsSource = _unifiedView;
        _unifiedInitialized = true;
    }

    private async Task EnsureUnifiedCatalogReadyAsync(bool updateIndex = true)
    {
        if (_domainContext is null || _gpos.Count == 0)
            return;

        if (updateIndex)
            await EnsureSettingsIndexAsync(forceRebuild: false);

        try
        {
            await EnsureAdmxCatalogAsync();
        }
        catch (Exception ex)
        {
            // GPMC-configured settings must remain usable if the central
            // ADMX store is temporarily unavailable.
            CrashLogService.Write("Unified settings ADMX catalog load", ex);
            StatusText.Text = "ADMX definitions unavailable; showing GPMC/MMC evidence.";
        }

        await RefreshUnifiedCatalogAsync();
    }

    private async Task RefreshUnifiedCatalogAsync()
    {
        if (!_unifiedInitialized)
            return;

        var version = Interlocked.Increment(ref _unifiedBuildVersion);
        await _unifiedCatalogGate.WaitAsync();
        try
        {
            if (version != Interlocked.Read(ref _unifiedBuildVersion))
                return;

            // Snapshot mutable WPF collections on the UI thread.
            var gpmc = _settings.ToArray();
            var admx = _admxPolicies?.ToArray();
            var mmc = _mmcInventoryResult?.Rows.ToArray();
            var mmcCoverage = _mmcInventoryResult?.Coverage ?? "";
            var target = GetSelectedUnifiedGpoFilter();
            var currentDc = DomainConnectionState.GetServerFor(
                _domainContext?.DomainName ?? "");
            var sources = _realSourceSnapshot is not null &&
                _realSourceSnapshot.Matches(
                    _domainContext?.DomainName ?? "", currentDc)
                ? _realSourceSnapshot : null;

            var result = await Task.Run(() =>
                UnifiedSettingsCatalogService.Build(
                    gpmc, admx, mmc, target, mmcCoverage, sources));

            if (version != Interlocked.Read(ref _unifiedBuildVersion))
                return;

            _unifiedCatalog = result;
            _unifiedRows = result.Rows;

            // Immutable snapshot avoids tens of thousands of per-item
            // CollectionChanged events at 200% HiDPI.
            _unifiedView = CollectionViewSource.GetDefaultView(_unifiedRows);
            _unifiedView.Filter = IsUnifiedRowVisible;
            UnifiedSettingsGrid.ItemsSource = _unifiedView;
            UpdateUnifiedCount();
        }
        catch (Exception ex)
        {
            CrashLogService.Write("Build unified settings catalog", ex);
            UnifiedSettingsCountText.Text =
                "Unified catalog unavailable: " + ex.Message;
        }
        finally
        {
            _unifiedCatalogGate.Release();
        }
    }

    private Guid? GetSelectedUnifiedGpoFilter()
    {
        if (UnifiedAllGposCheckBox?.IsChecked != false)
            return null;

        var gpo = UnifiedGpoFilterCombo?.SelectedItem as GpoInfo ??
                  GpoGrid.SelectedItem as GpoInfo ??
                  _gpos.FirstOrDefault();
        if (gpo is not null && UnifiedGpoFilterCombo?.SelectedItem is null)
            UnifiedGpoFilterCombo.SelectedItem = gpo;
        return gpo?.Id;
    }

    private void UnifiedSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_unifiedInitialized)
            return;
        _unifiedView?.Refresh();
        UpdateUnifiedCount();
    }

    private async void UnifiedFilterChanged(object sender, RoutedEventArgs e)
    {
        if (!_unifiedInitialized)
            return;

        if (ReferenceEquals(sender, UnifiedAllGposCheckBox) ||
            ReferenceEquals(sender, UnifiedGpoFilterCombo))
        {
            await RefreshUnifiedCatalogAsync();
        }
        else
        {
            _unifiedView?.Refresh();
            UpdateUnifiedCount();
        }
    }

    private bool IsUnifiedRowVisible(object item)
    {
        if (item is not UnifiedSettingInfo row)
            return false;

        var search = UnifiedSearchBox?.Text?.Trim();
        if (!string.IsNullOrWhiteSpace(search) &&
            !row.SearchText.Contains(search,
                StringComparison.CurrentCultureIgnoreCase))
            return false;

        var source = UnifiedSourceCombo?.SelectedItem as string;
        // Hide noisy security descriptor and member XML leaves from the
        // primary policy list, while retaining them in Advanced GPMC source.
        if (row.IsTechnicalDetail && UnifiedShowTechnicalCheckBox?.IsChecked != true)
            return false;

        if (source == "Configured (GPMC)" &&
                row.Kind is not ("Configured" or "GPMC detail") ||
            source == "ADMX templates" && row.Kind != "ADMX template" ||
            source == "MMC observed" && row.Mmc is null ||
            source == "Stored GPO files (read only)" && row.StoredSource is null)
            return false;

        return (UnifiedStateCombo?.SelectedItem as string) switch
        {
            "Configured only" => row.Kind == "Configured",
            "MMC Not Configured" => row.Mmc is not null &&
                row.State.Equals("Not Configured (MMC)",
                    StringComparison.OrdinalIgnoreCase),
            "Unknown / templates" => row.Kind == "ADMX template" ||
                row.State.StartsWith("Not reported", StringComparison.OrdinalIgnoreCase),
            "Stored source values" => row.StoredSource is not null,
            _ => true
        };
    }

    private void UpdateUnifiedCount()
    {
        if (UnifiedSettingsCountText is null)
            return;
        var count = _unifiedView?.Cast<object>().Count() ?? 0;
        var technicalCount = _unifiedRows.Count(row => row.IsTechnicalDetail);
        var hiddenSuffix = technicalCount > 0 &&
                           UnifiedShowTechnicalCheckBox?.IsChecked != true
            ? $" | {technicalCount:N0} technical XML details hidden (toggle Show XML details)"
            : "";
        UnifiedSettingsCountText.Text = _unifiedCatalog is null
            ? "Unified catalog not loaded. Open this tab or click Refresh catalog."
            : $"{count:N0} shown | {_unifiedCatalog.Summary}{hiddenSuffix}";
    }

    private async void AllSettingsSubTabs_SelectionChanged(
        object sender, SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(e.OriginalSource, AllSettingsSubTabs) ||
            _domainContext is null ||
            !AdmxCatalogTab.IsSelected)
            return;

        try
        {
            await EnsureAdmxCatalogAsync();
            ApplyAdmxFilter();
        }
        catch (Exception ex)
        {
            CrashLogService.Write("Advanced ADMX catalog", ex);
            StatusText.Text = "ADMX source unavailable: " + ex.Message;
        }
    }

    private async void ReadRealSources_Click(object sender, RoutedEventArgs e)
    {
        if (_domainContext is null || _realSourceCancellation is not null)
            return;

        var gpo = UnifiedGpoFilterCombo.SelectedItem as GpoInfo ??
                  GpoGrid.SelectedItem as GpoInfo;
        if (gpo is null)
        {
            MessageBox.Show(this,
                "Select a GPO in the GPOs list or in the All Settings GPO selector first. " +
                "Reading every GPO in the domain is not automatic.",
                "Read GPO source files", MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var cancellation = new CancellationTokenSource();
        _realSourceCancellation = cancellation;
        ReadRealSourcesButton.IsEnabled = false;
        SetBusy(true, "Reading selected GPO source files (read-only)...");
        try
        {
            // This does not call MMC. No policy registry hive is mounted.
            var result = await Task.Run(() =>
                new RealSettingsSourceService().Scan(gpo, cancellation.Token),
                cancellation.Token);
            _realSourceSnapshot = result;
            await RefreshUnifiedCatalogAsync();
            // Show the records that were just scanned rather than leaving
            // thousands of unrelated ADMX templates in the foreground.
            UnifiedSearchBox.Text = string.Empty;
            UnifiedSourceCombo.SelectedItem = "Stored GPO files (read only)";
            StatusText.Text = "Real Settings read-only source scan: " + result.Coverage;
            if (result.IsPartial)
                MessageBox.Show(this,
                    "The source-file scan is incomplete. No values were inferred.\n\n" +
                    string.Join("\n", result.Files
                        .Where(f => f.HasError)
                        .Select(f => f.Status + ": " + f.SourceFile + " - " + f.Details)),
                    "Source scan partial",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Real Settings source scan canceled; prior snapshot preserved.";
        }
        catch (Exception ex)
        {
            CrashLogService.Write("Real Settings source scan", ex);
            ErrorDialog.Show(this, "Real Settings",
                "Could not read the selected GPO source files.", ex);
        }
        finally
        {
            _realSourceCancellation = null;
            cancellation.Dispose();
            ReadRealSourcesButton.IsEnabled = true;
            SetBusy(false);
        }
    }

    private async void RefreshUnifiedSettings_Click(object sender, RoutedEventArgs e)
    {
        await EnsureUnifiedCatalogReadyAsync();
    }

    private async void EditUnifiedSetting_Click(object sender, RoutedEventArgs e) =>
        await EditSelectedUnifiedSettingAsync();

    private async void UnifiedSettingsGrid_MouseDoubleClick(
        object sender, MouseButtonEventArgs e) =>
        await EditSelectedUnifiedSettingAsync();

    private void UnifiedSettingsGrid_SelectionChanged(
        object sender, SelectionChangedEventArgs e)
    {
        if (UnifiedSelectedDetailsText is null)
            return;

        if (UnifiedSettingsGrid.SelectedItem is not UnifiedSettingInfo row)
        {
            UnifiedActionButton.Content = "View / Edit...";
            UnifiedSelectedDetailsText.Text =
                "Select a setting to see evidence, state and the supported editing route.";
            return;
        }

        UnifiedActionButton.Content = row.StoredSource is not null
            ? "View source evidence..."
            : row.IsTechnicalDetail
            ? "View XML details..."
            : row.Kind == "ADMX template" ? "Configure in GPO..."
            : row.Admx is not null ? "Edit policy..."
            : row.Mmc is { Navigation: "Exact MMC row candidate" }
                ? "Open exact MMC row..."
                : "View / open section...";
        UnifiedSelectedDetailsText.Text =
            $"{row.Sources} | {row.Capability} | {row.Explanation}";
    }

    private async Task EditSelectedUnifiedSettingAsync()
    {
        if (_domainContext is null ||
            UnifiedSettingsGrid.SelectedItem is not UnifiedSettingInfo row)
            return;

        if (row.StoredSource is RealSettingRecord sourceEvidence)
        {
            new RealSettingEvidenceWindow(sourceEvidence) { Owner = this }.ShowDialog();
            return;
        }

        if (row.Configured is not null)
        {
            await EditConfiguredSettingAsync(row.Configured);
            await RefreshUnifiedCatalogAsync();
            return;
        }

        if (row.Admx is not null)
        {
            await ConfigureAdmxDefinitionAsync(row.Admx);
            await RefreshUnifiedCatalogAsync();
            return;
        }

        if (row.Mmc is { Navigation: "Exact MMC row candidate" } observed)
        {
            MmcInventoryGrid.SelectedItem = observed;
            await OpenSelectedMmcInventoryRowAsync();
            return;
        }

        MessageBox.Show(this,
            "No verified direct editor exists for this row. The source and " +
            "displayed value remain available for inspection. Open the " +
            "selected GPO in the standard editor to review unsupported types. " +
            "No change has been attempted.",
            "Read-only or unsupported setting",
            MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void OpenUnifiedSettingGpo_Click(object sender, RoutedEventArgs e)
    {
        if (_domainContext is null ||
            UnifiedSettingsGrid.SelectedItem is not UnifiedSettingInfo row)
            return;

        var gpo = row.GpoId is Guid id
            ? _gpos.FirstOrDefault(g => g.Id == id)
            : null;

        if (gpo is null)
        {
            MessageBox.Show(this,
                "This is a template without a target GPO. Use Edit / Configure " +
                "to choose the GPO before applying it. No target is assumed.",
                "Select target GPO", MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        MarkGpoRecent(gpo.Id);
        _gpmService.OpenEditor(gpo, _domainContext.DomainDistinguishedName);
    }

    private void ExportUnifiedSettings_Click(object sender, RoutedEventArgs e)
    {
        if (_unifiedCatalog is null || _unifiedView is null)
        {
            MessageBox.Show(this,
                "Load the unified catalog before exporting.",
                "Unified Settings", MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var save = new SaveFileDialog
        {
            Title = "Export unified GPMC / ADMX / MMC settings",
            Filter = "CSV files (*.csv)|*.csv",
            FileName = $"GPO-Unified-Settings-{DateTime.Now:yyyyMMdd-HHmmss}.csv",
            InitialDirectory = StoragePaths.Exports,
            AddExtension = true,
            DefaultExt = ".csv"
        };
        if (save.ShowDialog(this) != true)
            return;

        try
        {
            using var writer = new StreamWriter(save.FileName, false,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            writer.WriteLine("GPO,Scope,Kind,State,Setting,Category,Value,Sources,Capability,RegistryTarget,Evidence");

            foreach (var row in _unifiedView.Cast<object>().OfType<UnifiedSettingInfo>())
                writer.WriteLine(string.Join(",", new[]
                {
                    Csv(row.GpoName), Csv(row.Scope), Csv(row.Kind),
                    Csv(row.State), Csv(row.SettingName), Csv(row.Category),
                    Csv(row.Value), Csv(row.Sources), Csv(row.Capability),
                    Csv(row.RegistryTarget), Csv(row.Explanation)
                }));

            _auditService.Write("Export", "Settings", "Unified catalog",
                $"Exported {_unifiedView.Cast<object>().Count():N0} visible rows to {save.FileName}; " +
                (_unifiedCatalog?.Coverage ?? ""));
            StatusText.Text = "Unified settings exported: " + save.FileName;
        }
        catch (Exception ex)
        {
            ErrorDialog.Show(this, "Export unified settings",
                "Could not export the CSV file.", ex);
        }
    }
}

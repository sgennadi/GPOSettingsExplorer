using System.Collections;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using GPOSettingsExplorer.Models;
using GPOSettingsExplorer.Services;

namespace GPOSettingsExplorer;

public partial class MainWindow
{
    private readonly DiagnosticsService _diagnosticsService =
        new();

    private readonly GlobalSearchService _globalSearchService =
        new();

    private readonly UpdateService _updateService =
        new();

    private readonly UpdateCheckStateService _updateCheckStateService =
        new();

    private UpdateInfo? _lastUpdateInfo;

    private void Connection_Click(
        object sender,
        RoutedEventArgs e)
    {
        var window =
            new ConnectionWindow
            {
                Owner =
                    this
            };

        if (window.ShowDialog() !=
            true)
        {
            return;
        }

        if (window.RelaunchStarted)
        {
            Application.Current.Shutdown();
            return;
        }

        _ =
            RefreshAllAsync();
    }

    private void WriteModeToggle_Changed(
        object sender,
        RoutedEventArgs e)
    {
        if (WriteModeToggle is null)
            return;

        var enabled =
            WriteModeToggle.IsChecked ==
            true;

        if (enabled &&
            !EditingGuard.IsEnabled)
        {
            var answer =
                MessageBox.Show(
                    this,
                    "Enable write operations for this session?\n\nAutomatic GPO backups remain enabled where supported, but changes can affect many users and computers.",
                    "Enable Group Policy Editing",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

            if (answer !=
                MessageBoxResult.Yes)
            {
                WriteModeToggle.IsChecked =
                    false;

                UpdateWriteModeUi();
                return;
            }
        }

        EditingGuard.SetEnabled(
            enabled);

        UpdateWriteModeUi();
    }

    private void UpdateWriteModeUi()
    {
        if (WriteModeToggle is null)
            return;

        var enabled =
            EditingGuard.IsEnabled;

        if (WriteModeToggle.IsChecked !=
            enabled)
        {
            WriteModeToggle.IsChecked =
                enabled;
        }

        WriteModeToggle.Content =
            enabled
                ? "WRITE ENABLED"
                : "Safe mode: READ ONLY";

        WriteModeToggle.ToolTip =
            enabled
                ? "Write operations are enabled for this session."
                : "Write operations are blocked. Click to enable editing.";
    }

    private void Diagnostics_Click(
        object sender,
        RoutedEventArgs e)
    {
        var window =
            new DiagnosticsWindow(
                _diagnosticsService,
                _gpmService,
                () =>
                    GpoGrid.SelectedItem
                    as GpoInfo)
            {
                Owner =
                    this
            };

        window.ShowDialog();
    }

    private async void GlobalSearch_Click(
        object sender,
        RoutedEventArgs e)
    {
        await ShowGlobalSearchAsync();
    }

    private async void GlobalSearchBox_KeyDown(
        object sender,
        KeyEventArgs e)
    {
        if (e.Key !=
            Key.Enter)
        {
            return;
        }

        e.Handled =
            true;

        await ShowGlobalSearchAsync();
    }

    private async Task ShowGlobalSearchAsync()
    {
        var query =
            GlobalSearchBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(
                query))
        {
            GlobalSearchBox.Focus();
            return;
        }

        SetBusy(
            true,
            "Preparing global search data...");

        try
        {
            await EnsureSettingsIndexAsync(
                forceRebuild: false);

            await EnsureGpoScriptsLoadedAsync();

            if (!_gppDocumentInitialized)
            {
                GppXmlTab_Loaded(
                    GppXmlTab,
                    new RoutedEventArgs());
            }

            if (_gppDocuments.Count == 0)
            {
                await LoadGppDocumentsAsync();
            }
        }
        catch (Exception ex)
        {
            CrashLogService.Write(
                "Prepare global search",
                ex);

            StatusText.Text =
                $"Global search is using the data that is already available: {ex.Message}";
        }
        finally
        {
            SetBusy(
                false);
        }

        var window =
            new GlobalSearchWindow(
                _globalSearchService,
                this,
                query,
                NavigateToGlobalSearchObject)
            {
                Owner =
                    this
            };

        window.ShowDialog();
    }

    private void NavigateToGlobalSearchObject(
        object target)
    {
        foreach (var tabObject in MainTabs.Items)
        {
            if (tabObject is not TabItem tab ||
                tab.Content is not DependencyObject root)
            {
                continue;
            }

            var grid =
                FindDataGridContaining(
                    root,
                    target,
                    out var filtered);

            if (grid is null)
                continue;

            MainTabs.SelectedItem =
                tab;

            if (!filtered)
            {
                grid.SelectedItem =
                    target;

                grid.ScrollIntoView(
                    target);

                grid.Focus();
            }
            else
            {
                MessageBox.Show(
                    this,
                    "The item is in this tab, but the tab currently has a filter that hides it. Clear that tab's search/filter box to show the row.",
                    "Global Search",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }

            return;
        }

        MessageBox.Show(
            this,
            "The item belongs to data that is loaded in memory, but its source tab is not currently initialized. Open the corresponding feature tab and run the search again.",
            "Global Search",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private static DataGrid? FindDataGridContaining(
        DependencyObject root,
        object target,
        out bool filtered)
    {
        filtered =
            false;

        if (root is DataGrid grid)
        {
            foreach (var item in grid.Items)
            {
                if (ReferenceEquals(
                        item,
                        target))
                {
                    return grid;
                }
            }

            if (grid.ItemsSource is
                System.ComponentModel.ICollectionView view &&
                ContainsReference(
                    view.SourceCollection,
                    target))
            {
                filtered =
                    true;

                return grid;
            }
        }

        foreach (var child in LogicalTreeHelper.GetChildren(
                     root))
        {
            if (child is not DependencyObject dependency)
                continue;

            var result =
                FindDataGridContaining(
                    dependency,
                    target,
                    out filtered);

            if (result is not null)
                return result;
        }

        return null;
    }

    private static bool ContainsReference(
        IEnumerable source,
        object target)
    {
        foreach (var item in source)
        {
            if (ReferenceEquals(
                    item,
                    target))
            {
                return true;
            }
        }

        return false;
    }

    private async Task CheckForUpdatesAutomaticallyAsync()
    {
        if (!_updateCheckStateService.ShouldCheckAutomatically(
                DateTime.UtcNow))
        {
            ApplyStoredUpdateState();
            return;
        }

        _updateCheckStateService.MarkAttempt(
            DateTime.UtcNow);

        try
        {
            var update =
                await _updateService.CheckAsync();

            _lastUpdateInfo =
                update;

            _updateCheckStateService.MarkSuccessful(
                DateTime.UtcNow,
                update.TagName);

            ApplyUpdateState(
                update,
                automatic:
                    true);
        }
        catch (Exception ex)
        {
            // Startup update checks are intentionally silent. A temporary
            // internet/GitHub failure must never interrupt AD administration.
            CrashLogService.Write(
                "Automatic update check",
                ex);

            ApplyStoredUpdateState();
        }
    }

    private void ApplyStoredUpdateState()
    {
        var state =
            _updateCheckStateService.Load();

        if (string.IsNullOrWhiteSpace(
                state.LastSeenTag))
        {
            UpdateButton.ToolTip =
                "Checks GitHub Releases for a newer x64/ARM64 portable build.";

            return;
        }

        var lastChecked =
            state.LastSuccessfulCheckUtc ==
            DateTime.MinValue
                ? string.Empty
                : $" Last checked: {state.LastSuccessfulCheckUtc.ToLocalTime():g}.";

        if (TryParseReleaseVersion(
                state.LastSeenTag,
                out var latest) &&
            latest >
            GetCurrentApplicationVersion())
        {
            UpdateButton.Content =
                $"Update {state.LastSeenTag}";

            UpdateButton.ToolTip =
                $"A newer release was found during the last successful check.{lastChecked} Click to verify and install.";

            return;
        }

        UpdateButton.ToolTip =
            $"Last seen release: {state.LastSeenTag}.{lastChecked}";
    }

    private static Version GetCurrentApplicationVersion()
    {
        var version =
            typeof(MainWindow)
                .Assembly
                .GetName()
                .Version
            ?? new Version(
                0,
                0,
                0);

        return new Version(
            Math.Max(
                0,
                version.Major),
            Math.Max(
                0,
                version.Minor),
            Math.Max(
                0,
                version.Build));
    }

    private static bool TryParseReleaseVersion(
        string tag,
        out Version version)
    {
        var text =
            tag.Trim();

        if (text.StartsWith(
                'v') ||
            text.StartsWith(
                'V'))
        {
            text =
                text[1..];
        }

        if (!Version.TryParse(
                text,
                out var parsed))
        {
            version =
                new Version(
                    0,
                    0,
                    0);

            return false;
        }

        version =
            new Version(
                Math.Max(
                    0,
                    parsed.Major),
                Math.Max(
                    0,
                    parsed.Minor),
                Math.Max(
                    0,
                    parsed.Build));

        return true;
    }

    private void ApplyUpdateState(
        UpdateInfo update,
        bool automatic)
    {
        if (update.IsUpdateAvailable)
        {
            UpdateButton.Content =
                $"Update {update.TagName}";

            UpdateButton.ToolTip =
                automatic
                    ? $"A newer version is available. Current: {update.CurrentVersion}; latest: {update.LatestVersion}. Click to review and install."
                    : $"Current: {update.CurrentVersion}; latest: {update.LatestVersion}.";

            if (automatic)
            {
                StatusText.Text =
                    $"Update {update.TagName} is available";
            }

            return;
        }

        UpdateButton.Content =
            "Up to date";

        UpdateButton.ToolTip =
            $"GPO Settings Explorer {update.CurrentVersion} is current. Last checked: {DateTime.Now:g}.";
    }

    private async void CheckUpdates_Click(
        object sender,
        RoutedEventArgs e)
    {
        SetBusy(
            true,
            "Checking for updates...");

        try
        {
            _updateCheckStateService.MarkAttempt(
                DateTime.UtcNow);

            var update =
                await _updateService.CheckAsync();

            _lastUpdateInfo =
                update;

            _updateCheckStateService.MarkSuccessful(
                DateTime.UtcNow,
                update.TagName);

            ApplyUpdateState(
                update,
                automatic:
                    false);

            if (!update.IsUpdateAvailable)
            {
                MessageBox.Show(
                    this,
                    $"GPO Settings Explorer {update.CurrentVersion} is the latest release.",
                    "Check for Updates",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                return;
            }

            var answer =
                MessageBox.Show(
                    this,
                    $"GPO Settings Explorer {update.TagName} is available.\n\nCurrent version: {update.CurrentVersion}\nLatest version: {update.LatestVersion}\n\nDownload and install it now?",
                    "Update Available",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

            if (answer !=
                MessageBoxResult.Yes)
            {
                if (!string.IsNullOrWhiteSpace(
                        update.ReleaseUrl))
                {
                    Process.Start(
                        new ProcessStartInfo
                        {
                            FileName =
                                update.ReleaseUrl,
                            UseShellExecute =
                                true
                        });
                }

                return;
            }

            SetBusy(
                true,
                $"Downloading {update.TagName}...");

            UpdateButton.Content =
                $"Downloading {update.TagName}";

            var progress =
                new Progress<double>(
                    value =>
                    {
                        StatusText.Text =
                            $"Downloading {update.TagName}: {value:P0}";

                        UpdateButton.ToolTip =
                            $"Downloading update: {value:P0}";
                    });

            var package =
                await _updateService.DownloadAsync(
                    update,
                    progress);

            StatusText.Text =
                "Starting updater...";

            UpdateButton.Content =
                "Installing update";

            UpdateButton.ToolTip =
                "The application will restart after the update is installed.";

            UpdateService.StageInstallerAndRestart(
                package);

            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            var log =
                CrashLogService.Write(
                    "Check/apply update",
                    ex);

            MessageBox.Show(
                this,
                ex.Message +
                (string.IsNullOrWhiteSpace(
                    log)
                    ? string.Empty
                    : $"\n\nDiagnostic log:\n{log}"),
                "Update",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            UpdateButton.Content =
                "Check updates";

            UpdateButton.ToolTip =
                "The last update check failed. Click to try again.";

            StatusText.Text =
                "Update failed";
        }
        finally
        {
            SetBusy(
                false);
        }
    }

}

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

    private string? _pendingUpdatePackage;


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
        RefreshPermissionAwareUi();
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
                () => GpoGrid.SelectedItem as GpoInfo,
                () => _settings.ToArray(),
                () => _domainContext?.DomainDistinguishedName ?? string.Empty)
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

    private async Task NavigateToGlobalSearchObject(
        object target,
        bool openExact)
    {
        if (target is GpoInfo gpo)
        {
            MainTabs.SelectedItem =
                GposTab;

            var selected =
                _gpos.FirstOrDefault(
                    item =>
                        item.Id ==
                        gpo.Id)
                ?? gpo;

            GpoGrid.SelectedItem =
                selected;

            GpoGrid.ScrollIntoView(
                selected);

            if (openExact)
            {
                OpenSelectedGpo();
            }

            return;
        }

        if (target is PolicySettingInfo setting)
        {
            MainTabs.SelectedItem = AllSettingsTab;
            await EnsureUnifiedCatalogReadyAsync();
            if (UnifiedAllGposCheckBox.IsChecked == false)
            {
                UnifiedAllGposCheckBox.IsChecked = true;
                await RefreshUnifiedCatalogAsync();
            }
            UnifiedSearchBox.Text = string.Empty;
            UnifiedSourceCombo.SelectedIndex = 0;
            UnifiedStateCombo.SelectedIndex = 0;

            var selected = _settings.FirstOrDefault(item =>
                item.GpoId == setting.GpoId &&
                item.Scope.Equals(setting.Scope, StringComparison.OrdinalIgnoreCase) &&
                item.SettingName.Equals(setting.SettingName, StringComparison.CurrentCultureIgnoreCase) &&
                item.Category.Equals(setting.Category, StringComparison.CurrentCultureIgnoreCase))
                ?? setting;
            var unified = _unifiedRows.FirstOrDefault(row =>
                ReferenceEquals(row.Configured, selected) ||
                (row.Configured?.GpoId == selected.GpoId &&
                 row.Configured.Scope.Equals(selected.Scope, StringComparison.OrdinalIgnoreCase) &&
                 row.Configured.SettingName.Equals(selected.SettingName, StringComparison.CurrentCultureIgnoreCase) &&
                 row.Configured.Category.Equals(selected.Category, StringComparison.CurrentCultureIgnoreCase)));

            if (unified is not null)
            {
                UnifiedSettingsGrid.SelectedItem = unified;
                UnifiedSettingsGrid.ScrollIntoView(unified);
                UnifiedSettingsGrid.Focus();
            }
            else
            {
                // Retain legacy selection for an item whose index changed during
                // the live reload. Never choose a same-name policy elsewhere.
                AdvancedSourcesExpander.IsExpanded = true;
                AllSettingsSubTabs.SelectedIndex = 0;
                SettingsGrid.SelectedItem = selected;
                SettingsGrid.ScrollIntoView(selected);
            }

            if (openExact)
            {
                SettingsGrid.SelectedItem = selected;
                MarkGpoRecent(setting.GpoId);
                await EditSelectedSettingAsync();
            }
            return;
        }

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

            MainTabs.SelectedItem = tab;
            DependencyObject editorRoot = root;
            if (ReferenceEquals(tab, GppPreferencesTab))
            {
                // The former top-level GPP tabs are now child tabs. Activate
                // the owning editor (including nested Files/Users subtabs)
                // before choosing a source-specific edit command.
                ActivateNestedSourceTabs(root, target);
                foreach (var candidate in GppPreferencesTabs.Items.OfType<TabItem>())
                {
                    if (candidate.Content is DependencyObject candidateRoot &&
                        FindDataGridContaining(candidateRoot, target, out _) is not null)
                    {
                        editorRoot = candidateRoot;
                        break;
                    }
                }
            }

            if (!filtered)
            {
                grid.SelectedItem =
                    target;

                grid.ScrollIntoView(
                    target);

                grid.Focus();

                if (openExact)
                {
                    MarkRecentFromObject(
                        target);

                    if (!TryInvokeSourceEditor(
                            editorRoot))
                    {
                        StatusText.Text =
                            "The item was selected in its source tab; no dedicated editor action was found.";
                    }
                }
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

    private static void ActivateNestedSourceTabs(
        DependencyObject root, object target)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root))
        {
            if (child is System.Windows.Controls.TabControl tabs)
            {
                foreach (var tab in tabs.Items.OfType<TabItem>())
                {
                    if (tab.Content is not DependencyObject inside ||
                        FindDataGridContaining(inside, target, out _) is null)
                        continue;
                    tabs.SelectedItem = tab;
                    ActivateNestedSourceTabs(inside, target);
                    return;
                }
            }

            if (child is DependencyObject dependency)
                ActivateNestedSourceTabs(dependency, target);
        }
    }

    private void MarkRecentFromObject(
        object target)
    {
        var property =
            target.GetType()
                .GetProperty(
                    "GpoId");

        if (property?.PropertyType ==
                typeof(Guid) &&
            property.GetValue(
                target) is Guid id)
        {
            MarkGpoRecent(
                id);
        }
    }

    private static bool TryInvokeSourceEditor(
        DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(
                     root))
        {
            if (child is Button button)
            {
                var text =
                    Convert.ToString(
                        button.Content)
                    ?? string.Empty;

                if (text.Equals(
                        "Edit",
                        StringComparison.CurrentCultureIgnoreCase) ||
                    text.StartsWith(
                        "Edit ",
                        StringComparison.CurrentCultureIgnoreCase) ||
                    text.StartsWith(
                        "Configure",
                        StringComparison.CurrentCultureIgnoreCase))
                {
                    button.RaiseEvent(
                        new RoutedEventArgs(
                            Button.ClickEvent));

                    return true;
                }
            }

            if (child is DependencyObject dependency &&
                TryInvokeSourceEditor(
                    dependency))
            {
                return true;
            }
        }

        return false;
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

        // A working AD connection is not an Internet connection. Check
        // GitHub reachability before contacting GitHub Releases or marking
        // an update-check attempt. Offline DCs must remain silent.
        if (!await GitHubConnectivityService.IsAvailableAsync())
        {
            UpdateButton.Content = "Updates offline";
            UpdateButton.ToolTip =
                "GitHub is unreachable. Automatic update checking is skipped.";
            return;
        }

        _updateCheckStateService.MarkAttempt(
            DateTime.UtcNow);

        try
        {
            var update =
                await _updateService.CheckAsync();

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
            // Startup update checks are intentionally silent. Firewall,
            // proxy and Internet restrictions are expected on many servers
            // and must not create crash-style diagnostic logs.
            if (!UpdateService.IsExpectedConnectivityFailure(
                    ex))
            {
                CrashLogService.Write(
                    "Automatic update check",
                    ex);
            }

            ApplyStoredUpdateState();

            if (UpdateService.IsExpectedConnectivityFailure(
                    ex))
            {
                UpdateButton.ToolTip =
                    UpdateService.BuildConnectivityFailureMessage(
                        ex);
            }
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
        // Explicit click may recheck after connectivity returns. A failed
        // GitHub reachability probe suppresses all release/download requests.
        if (!await GitHubConnectivityService.IsAvailableAsync(force: true))
        {
            UpdateButton.Content = "Updates offline";
            UpdateButton.ToolTip =
                "GitHub is not reachable; no update request was sent.";
            StatusText.Text =
                "Offline: skipped update checking. Local GPO features remain available.";
            return;
        }

        SetBusy(
            true,
            "Checking for updates...");

        try
        {
            _updateCheckStateService.MarkAttempt(
                DateTime.UtcNow);

            var update =
                await _updateService.CheckAsync();

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

            SetBusy(
                false);

            var window =
                new UpdateAvailableWindow(
                    update)
                {
                    Owner =
                        this
                };

            if (window.ShowDialog() !=
                    true ||
                window.Choice ==
                UpdateInstallChoice.Later)
            {
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

            if (window.Choice ==
                UpdateInstallChoice.InstallOnExit)
            {
                _pendingUpdatePackage =
                    package;

                UpdateButton.Content =
                    $"Install {update.TagName} on exit";

                UpdateButton.ToolTip =
                    "The verified update package will be installed when GPO Settings Explorer closes.";

                StatusText.Text =
                    $"Update {update.TagName} is ready and will install on exit";

                return;
            }

            StatusText.Text =
                "Starting updater...";

            UpdateButton.Content =
                "Installing update";

            UpdateButton.ToolTip =
                "The application will restart after the staged update is installed.";

            UpdateService.StageInstallerAndRestart(
                package);

            _pendingUpdatePackage =
                null;

            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            UpdateButton.Content =
                "Check updates";

            StatusText.Text =
                "Update check unavailable";

            if (UpdateService.IsExpectedConnectivityFailure(
                    ex))
            {
                var message =
                    UpdateService.BuildConnectivityFailureMessage(
                        ex);

                UpdateButton.ToolTip =
                    message;

                MessageBox.Show(
                    this,
                    message,
                    "Check for Updates",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                return;
            }

            UpdateButton.ToolTip =
                "The last update check failed. Click to try again.";

            ErrorDialog.Show(
                this,
                "Update",
                "The update could not be checked, downloaded, verified or staged.",
                ex);
        }
        finally
        {
            SetBusy(
                false);
        }
    }

}

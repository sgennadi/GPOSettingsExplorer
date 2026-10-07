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

    private void GlobalSearch_Click(
        object sender,
        RoutedEventArgs e)
    {
        ShowGlobalSearch();
    }

    private void GlobalSearchBox_KeyDown(
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

        ShowGlobalSearch();
    }

    private void ShowGlobalSearch()
    {
        var query =
            GlobalSearchBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(
                query))
        {
            GlobalSearchBox.Focus();
            return;
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

    private async void CheckUpdates_Click(
        object sender,
        RoutedEventArgs e)
    {
        SetBusy(
            true,
            "Checking for updates...");

        try
        {
            var update =
                await _updateService.CheckAsync();

            if (!update.IsUpdateAvailable)
            {
                UpdateButton.Content =
                    "Up to date";

                MessageBox.Show(
                    this,
                    $"GPO Settings Explorer {update.CurrentVersion} is the latest release.",
                    "Check for Updates",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                return;
            }

            UpdateButton.Content =
                $"Update {update.TagName}";

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

            var progress =
                new Progress<double>(
                    value =>
                    {
                        StatusText.Text =
                            $"Downloading {update.TagName}: {value:P0}";
                    });

            var package =
                await _updateService.DownloadAsync(
                    update,
                    progress);

            StatusText.Text =
                "Starting updater...";

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

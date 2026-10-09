using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using GPOSettingsExplorer.Models;
using GPOSettingsExplorer.Services;

namespace GPOSettingsExplorer;

public sealed class DiagnosticsWindow : Window
{
    private readonly DiagnosticsService _service;
    private readonly GpmService _gpmService;
    private readonly Func<GpoInfo?> _selectedGpo;
    private readonly DataGrid _grid;
    private readonly TextBlock _status;

    private IReadOnlyList<DiagnosticItem> _items =
        Array.Empty<DiagnosticItem>();

    public DiagnosticsWindow(
        DiagnosticsService service,
        GpmService gpmService,
        Func<GpoInfo?> selectedGpo)
    {
        _service =
            service;

        _gpmService =
            gpmService;

        _selectedGpo =
            selectedGpo;

        Title =
            "Diagnostics";

        Width =
            980;

        Height =
            650;

        MinWidth =
            720;

        MinHeight =
            480;

        WindowStartupLocation =
            WindowStartupLocation.CenterOwner;

        var root =
            new DockPanel
            {
                Margin =
                    new Thickness(12)
            };

        var toolbar =
            new WrapPanel();

        DockPanel.SetDock(
            toolbar,
            Dock.Top);

        var refresh =
            new Button
            {
                Content =
                    "Refresh"
            };

        refresh.Click +=
            (_, _) =>
                Refresh();

        var testWrite =
            new Button
            {
                Content =
                    "Test selected GPO write access"
            };

        testWrite.Click +=
            (_, _) =>
                TestWrite();

        var support =
            new Button
            {
                Content =
                    "Create support package"
            };

        support.Click +=
            (_, _) =>
                CreateSupportPackage();

        var openLogs =
            new Button
            {
                Content =
                    "Open logs"
            };

        openLogs.Click +=
            (_, _) =>
                OpenLogs();

        var rollback =
            new Button
            {
                Content =
                    "Rollback last update",
                IsEnabled =
                    UpdateService.FindLatestRollbackDirectory()
                    is not null
            };

        rollback.Click +=
            (_, _) =>
                RollbackLastUpdate();

        toolbar.Children.Add(
            refresh);

        toolbar.Children.Add(
            testWrite);

        toolbar.Children.Add(
            support);

        toolbar.Children.Add(openLogs);

        var reviewPending = new Button
        {
            Content = "Review local logs...",
            ToolTip = "Review pending diagnostics offline or online. No automatic uploads."
        };
        reviewPending.Click += async (_, _) => await ReviewLocalLogsAsync();
        toolbar.Children.Add(reviewPending);

        toolbar.Children.Add(rollback);

        _status =
            new TextBlock
            {
                Margin =
                    new Thickness(
                        12,
                        0,
                        0,
                        0),
                VerticalAlignment =
                    VerticalAlignment.Center,
                TextWrapping =
                    TextWrapping.Wrap,
                Foreground =
                    System.Windows.Media.Brushes.DimGray
            };

        toolbar.Children.Add(
            _status);

        _grid =
            new DataGrid
            {
                AutoGenerateColumns =
                    false,
                IsReadOnly =
                    true
            };

        _grid.Columns.Add(
            new DataGridTextColumn
            {
                Header =
                    "Check",
                Binding =
                    new System.Windows.Data.Binding(
                        nameof(DiagnosticItem.Name)),
                Width =
                    new DataGridLength(
                        1.2,
                        DataGridLengthUnitType.Star)
            });

        _grid.Columns.Add(
            new DataGridTextColumn
            {
                Header =
                    "Value",
                Binding =
                    new System.Windows.Data.Binding(
                        nameof(DiagnosticItem.Value)),
                Width =
                    new DataGridLength(
                        2,
                        DataGridLengthUnitType.Star)
            });

        _grid.Columns.Add(
            new DataGridTextColumn
            {
                Header =
                    "Details",
                Binding =
                    new System.Windows.Data.Binding(
                        nameof(DiagnosticItem.Details)),
                Width =
                    new DataGridLength(
                        2,
                        DataGridLengthUnitType.Star)
            });

        var close =
            new Button
            {
                Content =
                    "Close",
                IsCancel =
                    true,
                HorizontalAlignment =
                    HorizontalAlignment.Right
            };

        DockPanel.SetDock(
            close,
            Dock.Bottom);

        root.Children.Add(
            toolbar);

        root.Children.Add(
            close);

        root.Children.Add(
            _grid);

        Content =
            root;

        Loaded +=
            (_, _) =>
                Refresh();
    }

    private void Refresh()
    {
        _items =
            _service.Collect(
                _gpmService);

        _grid.ItemsSource =
            _items;

        var failures =
            _items.Count(
                item =>
                    item.Success ==
                    false);

        _status.Text =
            failures == 0
                ? "No failed checks."
                : $"{failures:N0} failed check(s).";
    }

    private void TestWrite()
    {
        var gpo =
            _selectedGpo();

        if (gpo is null)
        {
            MessageBox.Show(
                this,
                "Select a GPO in the GPOs tab first.",
                "Diagnostics",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            return;
        }

        var item =
            _service.TestGpoWriteAccess(
                gpo);

        _items =
            _items
                .Where(
                    existing =>
                        !existing.Name.Equals(
                            item.Name,
                            StringComparison.OrdinalIgnoreCase))
                .Append(
                    item)
                .ToArray();

        _grid.ItemsSource =
            _items;

        _status.Text =
            item.Success ==
            true
                ? "Selected GPO is writable."
                : "Selected GPO write test failed.";
    }

    private void CreateSupportPackage()
    {
        try
        {
            var path =
                _service.CreateSupportPackage(
                    _items.Count == 0
                        ? _service.Collect(
                            _gpmService)
                        : _items);

            Clipboard.SetText(
                path);

            MessageBox.Show(
                this,
                $"Support package created. The path was copied to the clipboard.\n\n{path}",
                "Diagnostics",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                ex.Message,
                "Create Support Package",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async Task ReviewLocalLogsAsync()
    {
        try
        {
            var queue = new DiagnosticLogQueueService();
            var pending = await Task.Run(queue.GetPending);
            if (pending.Count == 0)
            {
                MessageBox.Show(this,
                    "No pending diagnostic logs were found. Already submitted logs " +
                    "are archived locally for fourteen days.",
                    "Diagnostic queue", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var review = new DiagnosticSubmissionWindow(queue, pending)
            {
                Owner = this
            };
            review.ShowDialog();
            _status.Text = $"{queue.GetPending().Count:N0} diagnostics remain pending.";
        }
        catch (Exception ex)
        {
            ErrorDialog.Show(this,
                "Review diagnostic logs",
                "Could not prepare the local diagnostic queue.", ex);
        }
    }

    private void RollbackLastUpdate()
    {
        var rollback =
            UpdateService.FindLatestRollbackDirectory();

        if (rollback is null)
        {
            MessageBox.Show(
                this,
                "No retained update rollback is available.",
                "Rollback Update",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            return;
        }

        if (MessageBox.Show(
                this,
                $"Restore the application files saved before the last update?\n\n{rollback}",
                "Rollback Update",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) !=
            MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            UpdateService.StageRollbackAndRestart(
                rollback);

            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            ErrorDialog.Show(
                this,
                "Rollback Update",
                "The previous application version could not be staged for rollback.",
                ex);
        }
    }

    private static void OpenLogs()
    {
        var path =
            Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData),
                "GPOSettingsExplorer",
                "Logs");

        Directory.CreateDirectory(
            path);

        Process.Start(
            new ProcessStartInfo
            {
                FileName =
                    "explorer.exe",
                Arguments =
                    $"\"{path}\"",
                UseShellExecute =
                    true
            });
    }
}

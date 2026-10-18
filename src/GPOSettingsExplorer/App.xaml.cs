using System.Windows;
using System.Windows.Threading;
using GPOSettingsExplorer.Services;

namespace GPOSettingsExplorer;

public partial class App : Application
{
    private string? _updateStartupHealthMarker;

    protected override void OnStartup(
        StartupEventArgs e)
    {
        DispatcherUnhandledException +=
            App_DispatcherUnhandledException;

        AppDomain.CurrentDomain.UnhandledException +=
            CurrentDomain_UnhandledException;

        TaskScheduler.UnobservedTaskException +=
            TaskScheduler_UnobservedTaskException;

        AdaptiveWindowManager.Register();
        CommandLineOptions.Initialize(
            e.Args);

        base.OnStartup(e);

        if (UpdateInstaller.TryApply(
                e.Args))
        {
            Shutdown();
            return;
        }

        // Only a rendered, visible WPF window may acknowledge startup.
        // Signaling at OnStartup entered too early: it could report a
        // healthy update even if the window constructor subsequently failed.
        _updateStartupHealthMarker = ResolveStartupHealthMarker(e.Args);

        if (CommandLineOptions.Current.OfflineAnalysis)
        {
            ShowOfflineWindow();
            return;
        }

        if (CommandLineOptions.Current.ConnectedSession)
        {
            DomainConnectionState.SetProfile(
                DomainConnectionProfile.CurrentSession(
                    CommandLineOptions.Current.DomainName,
                    CommandLineOptions.Current.DomainController));

            ShowMainWindow();
            return;
        }

        var connection =
            new ConnectionWindow();
        RegisterStartupHealthAck(connection);

        var connected =
            connection.ShowDialog() ==
            true;

        if (!connected ||
            connection.RelaunchStarted)
        {
            Shutdown();
            return;
        }

        if (connection.OfflineModeSelected)
            ShowOfflineWindow();
        else
            ShowMainWindow();
    }

    private void ShowOfflineWindow()
    {
        EditingGuard.SetEnabled(false);
        var window = new GpoAdvancedAnalysisWindow(null, null);
        MainWindow = window;
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        RegisterStartupHealthAck(window);
        window.Show();
    }

    private void ShowMainWindow()
    {
        EditingGuard.SetEnabled(
            false);

        var window =
            new MainWindow();

        MainWindow =
            window;

        RegisterStartupHealthAck(window);
        window.Show();

        ShutdownMode =
            ShutdownMode.OnMainWindowClose;
    }

    private static string? ResolveStartupHealthMarker(string[] args)
    {
        const string flag = "--update-health-marker";
        const string prefix = "GPOSettingsExplorer-Health-";
        const string suffix = ".ok";
        var index = Array.IndexOf(args, flag);
        if (index < 0 || index + 1 >= args.Length)
            return null;

        try
        {
            var requested = Path.GetFullPath(args[index + 1]);
            var fileName = Path.GetFileName(requested);
            if (!fileName.StartsWith(prefix, StringComparison.Ordinal) ||
                !fileName.EndsWith(suffix, StringComparison.Ordinal) ||
                fileName.Length != prefix.Length + 32 + suffix.Length ||
                !Guid.TryParseExact(fileName.Substring(prefix.Length, 32), "N", out _))
                return null;

            var expected = Path.GetFullPath(
                Path.Combine(Path.GetTempPath(), fileName));
            return requested.Equals(expected, StringComparison.OrdinalIgnoreCase)
                ? requested : null;
        }
        catch
        {
            return null;
        }
    }

    private void RegisterStartupHealthAck(Window window)
    {
        if (_updateStartupHealthMarker is not { } marker)
            return;

        // ContentRendered fires after the dialog/main window is actually
        // presented, not merely after the WPF application object is created.
        window.ContentRendered += (_, _) =>
        {
            if (_updateStartupHealthMarker != marker)
                return;
            try
            {
                File.WriteAllText(marker, "WPF window rendered");
                _updateStartupHealthMarker = null;
            }
            catch (Exception ex)
            {
                CrashLogService.Write("Update startup health marker", ex);
            }
        };
    }

    private void App_DispatcherUnhandledException(
        object sender,
        DispatcherUnhandledExceptionEventArgs e)
    {
        var path =
            CrashLogService.Write(
                "WPF DispatcherUnhandledException",
                e.Exception);

        if (IsFatal(
                e.Exception))
        {
            return;
        }

        e.Handled =
            true;

        try
        {
            System.Windows.Input.Mouse.OverrideCursor =
                null;

            ErrorDialog.Show(
                Current?.MainWindow,
                "GPO Settings Explorer",
                "An unexpected error was caught. The application will stay open.",
                e.Exception,
                path);
        }
        catch
        {
        }
    }

    private static void CurrentDomain_UnhandledException(
        object sender,
        UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
        {
            CrashLogService.Write(
                "AppDomain.UnhandledException",
                exception);
        }
        else
        {
            CrashLogService.Write(
                "AppDomain.UnhandledException",
                Convert.ToString(
                    e.ExceptionObject)
                ?? "Unknown unhandled exception.");
        }
    }

    private static void TaskScheduler_UnobservedTaskException(
        object? sender,
        UnobservedTaskExceptionEventArgs e)
    {
        CrashLogService.Write(
            "TaskScheduler.UnobservedTaskException",
            e.Exception);

        e.SetObserved();
    }

    private static bool IsFatal(
        Exception exception) =>
        exception is OutOfMemoryException or
                     StackOverflowException or
                     AccessViolationException;
}

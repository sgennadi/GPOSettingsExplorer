using System.Windows;
using System.Windows.Threading;
using GPOSettingsExplorer.Services;

namespace GPOSettingsExplorer;

public partial class App : Application
{
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

        var connected =
            connection.ShowDialog() ==
            true;

        if (!connected ||
            connection.RelaunchStarted)
        {
            Shutdown();
            return;
        }

        ShowMainWindow();
    }

    private void ShowMainWindow()
    {
        EditingGuard.SetEnabled(
            false);

        var window =
            new MainWindow();

        MainWindow =
            window;

        window.Show();

        ShutdownMode =
            ShutdownMode.OnMainWindowClose;
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

            var message =
                "An unexpected error was caught. The application will stay open.";

            if (!string.IsNullOrWhiteSpace(
                    path))
            {
                message +=
                    $"\n\nDiagnostic log:\n{path}";
            }

            MessageBox.Show(
                Current?.MainWindow,
                message +
                $"\n\n{e.Exception.Message}",
                "GPO Settings Explorer",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
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

using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using GPOSettingsExplorer.Services;

namespace GPOSettingsExplorer;

public sealed class ErrorDialogWindow : Window
{
    public ErrorDialogWindow(
        string title,
        string context,
        Exception exception,
        string logPath)
    {
        Title =
            title;

        Width =
            820;

        Height =
            560;

        MinWidth =
            640;

        MinHeight =
            420;

        WindowStartupLocation =
            WindowStartupLocation.CenterOwner;

        var root =
            new DockPanel
            {
                Margin =
                    new Thickness(14)
            };

        var header =
            new StackPanel
            {
                Margin =
                    new Thickness(
                        0,
                        0,
                        0,
                        10)
            };

        DockPanel.SetDock(
            header,
            Dock.Top);

        header.Children.Add(
            new TextBlock
            {
                Text =
                    context,
                FontSize =
                    UiStyle.HeadingFontSize,
                FontWeight =
                    FontWeights.SemiBold,
                TextWrapping =
                    TextWrapping.Wrap
            });

        header.Children.Add(
            new TextBlock
            {
                Text =
                    exception.Message,
                Margin =
                    new Thickness(
                        0,
                        6,
                        0,
                        0),
                TextWrapping =
                    TextWrapping.Wrap
            });

        var details =
            new TextBox
            {
                IsReadOnly =
                    true,
                AcceptsReturn =
                    true,
                TextWrapping =
                    TextWrapping.NoWrap,
                HorizontalScrollBarVisibility =
                    ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility =
                    ScrollBarVisibility.Auto,
                MinHeight =
                    230,
                Text =
                    exception.ToString()
            };

        var footer =
            new WrapPanel
            {
                HorizontalAlignment =
                    HorizontalAlignment.Right,
                Margin =
                    new Thickness(
                        0,
                        12,
                        0,
                        0)
            };

        DockPanel.SetDock(
            footer,
            Dock.Bottom);

        var copy =
            new Button
            {
                Content =
                    "Copy error"
            };

        copy.Click +=
            (_, _) =>
                Clipboard.SetText(
                    $"{context}\r\n\r\n{exception}\r\n\r\nLog: {logPath}");

        var openLog =
            new Button
            {
                Content =
                    "Open log",
                IsEnabled =
                    !string.IsNullOrWhiteSpace(
                        logPath) &&
                    File.Exists(
                        logPath)
            };

        openLog.Click +=
            (_, _) =>
                OpenPath(
                    logPath);

        var openFolder =
            new Button
            {
                Content =
                    "Open logs folder"
            };

        openFolder.Click +=
            (_, _) =>
            {
                var directory =
                    string.IsNullOrWhiteSpace(
                        logPath)
                        ? CrashLogService.LogDirectory
                        : Path.GetDirectoryName(
                            logPath)
                          ?? CrashLogService.LogDirectory;

                Directory.CreateDirectory(
                    directory);

                OpenPath(
                    directory);
            };

        var support =
            new Button
            {
                Content =
                    "Create support package"
            };

        support.Click +=
            (_, _) =>
            {
                try
                {
                    var diagnostics =
                        new DiagnosticsService()
                            .Collect(
                                new GpmService());

                    var path =
                        new DiagnosticsService()
                            .CreateSupportPackage(
                                diagnostics);

                    Clipboard.SetText(
                        path);

                    MessageBox.Show(
                        this,
                        $"Support package created and its path was copied.\n\n{path}",
                        "Support Package",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        this,
                        ex.Message,
                        "Support Package",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            };

        var close =
            new Button
            {
                Content =
                    "Close",
                IsDefault =
                    true,
                IsCancel =
                    true
            };

        footer.Children.Add(
            copy);

        footer.Children.Add(
            openLog);

        footer.Children.Add(
            openFolder);

        footer.Children.Add(
            support);

        footer.Children.Add(
            close);

        root.Children.Add(
            header);

        root.Children.Add(
            footer);

        root.Children.Add(
            details);

        Content =
            root;
    }

    private static void OpenPath(
        string path)
    {
        if (string.IsNullOrWhiteSpace(
                path))
        {
            return;
        }

        Process.Start(
            new ProcessStartInfo
            {
                FileName =
                    "explorer.exe",
                Arguments =
                    File.Exists(
                        path)
                        ? $"/select,\"{path}\""
                        : $"\"{path}\"",
                UseShellExecute =
                    true
            });
    }
}

public static class ErrorDialog
{
    public static void Show(
        Window? owner,
        string title,
        string context,
        Exception exception,
        string? existingLog = null)
    {
        var log =
            string.IsNullOrWhiteSpace(
                existingLog)
                ? CrashLogService.Write(
                    context,
                    exception)
                : existingLog;

        var window =
            new ErrorDialogWindow(
                title,
                context,
                exception,
                log)
            {
                Owner =
                    owner
            };

        window.ShowDialog();
    }
}

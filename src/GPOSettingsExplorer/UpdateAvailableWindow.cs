using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using GPOSettingsExplorer.Services;

namespace GPOSettingsExplorer;

public enum UpdateInstallChoice
{
    Later,
    InstallNow,
    InstallOnExit
}

public sealed class UpdateAvailableWindow : Window
{
    public UpdateInstallChoice Choice { get; private set; } =
        UpdateInstallChoice.Later;

    public UpdateAvailableWindow(
        UpdateInfo update)
    {
        Title =
            $"GPO Settings Explorer {update.TagName}";

        Width =
            780;

        Height =
            620;

        MinWidth =
            640;

        MinHeight =
            480;

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
                    $"Update {update.TagName} is available",
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
                    $"Current: {update.CurrentVersion}    Latest: {update.LatestVersion}" +
                    (update.PublishedAt is null
                        ? string.Empty
                        : $"    Published: {update.PublishedAt.Value.LocalDateTime:g}"),
                Margin =
                    new Thickness(
                        0,
                        5,
                        0,
                        0),
                Foreground =
                    System.Windows.Media.Brushes.DimGray,
                TextWrapping =
                    TextWrapping.Wrap
            });

        var notes =
            new TextBox
            {
                IsReadOnly =
                    true,
                AcceptsReturn =
                    true,
                TextWrapping =
                    TextWrapping.Wrap,
                VerticalScrollBarVisibility =
                    ScrollBarVisibility.Auto,
                MinHeight =
                    260,
                Text =
                    string.IsNullOrWhiteSpace(
                        update.ReleaseNotes)
                        ? "No release notes were published for this version."
                        : update.ReleaseNotes
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

        var release =
            new Button
            {
                Content =
                    "Open release"
            };

        release.Click +=
            (_, _) =>
            {
                if (string.IsNullOrWhiteSpace(
                        update.ReleaseUrl))
                {
                    return;
                }

                Process.Start(
                    new ProcessStartInfo
                    {
                        FileName =
                            update.ReleaseUrl,
                        UseShellExecute =
                            true
                    });
            };

        var exit =
            new Button
            {
                Content =
                    "Install on exit"
            };

        exit.Click +=
            (_, _) =>
            {
                Choice =
                    UpdateInstallChoice.InstallOnExit;

                DialogResult =
                    true;
            };

        var now =
            new Button
            {
                Content =
                    "Install now",
                IsDefault =
                    true
            };

        now.Click +=
            (_, _) =>
            {
                Choice =
                    UpdateInstallChoice.InstallNow;

                DialogResult =
                    true;
            };

        var later =
            new Button
            {
                Content =
                    "Later",
                IsCancel =
                    true
            };

        footer.Children.Add(
            release);

        footer.Children.Add(
            exit);

        footer.Children.Add(
            now);

        footer.Children.Add(
            later);

        root.Children.Add(
            header);

        root.Children.Add(
            footer);

        root.Children.Add(
            notes);

        Content =
            root;
    }
}

using System.Windows;
using System.Windows.Controls;
using GPOSettingsExplorer.Models;

namespace GPOSettingsExplorer;

public sealed class AuditEntryWindow : Window
{
    private readonly Func<Task>? _restore;

    public AuditEntryWindow(
        AuditEntryInfo entry,
        Func<Task>? restore = null)
    {
        _restore =
            restore;

        Title =
            $"Audit - {entry.Action} - {entry.ObjectName}";

        Width =
            1080;

        Height =
            700;

        MinWidth =
            760;

        MinHeight =
            500;

        WindowStartupLocation =
            WindowStartupLocation.CenterOwner;

        var root =
            new DockPanel
            {
                Margin =
                    new Thickness(12)
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
                    $"{entry.Action} — {entry.ObjectName}",
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
                    $"{entry.Timestamp:yyyy-MM-dd HH:mm:ss zzz} | {entry.User} | {entry.Computer} | {entry.ObjectType}",
                Margin =
                    new Thickness(
                        0,
                        4,
                        0,
                        0),
                Foreground =
                    System.Windows.Media.Brushes.DimGray,
                TextWrapping =
                    TextWrapping.Wrap
            });

        if (!string.IsNullOrWhiteSpace(
                entry.Details))
        {
            header.Children.Add(
                new TextBlock
                {
                    Text =
                        entry.Details,
                    Margin =
                        new Thickness(
                            0,
                            4,
                            0,
                            0),
                    TextWrapping =
                        TextWrapping.Wrap
                });
        }

        if (entry.Action.Equals("Edit GPO Script", StringComparison.OrdinalIgnoreCase))
        {
            var verifiedFingerprints =
                entry.Before.Contains("SHA-256:", StringComparison.Ordinal) &&
                entry.After.Contains("SHA-256:", StringComparison.Ordinal);

            header.Children.Add(new TextBlock
            {
                Text = verifiedFingerprints
                    ? "Before/After are verified SHA-256 and format metadata, not script contents. " +
                      "The GPO backup is the source for full restoration or manual content comparison."
                    : "Historical script audit: the application did not capture Before/After. " +
                      "Use the linked GPO backup and current script for a separate manual comparison. " +
                      "Do not assume the original script contents from this entry.",
                Foreground = verifiedFingerprints ? UiStyle.MutedBrush : UiStyle.WarningBrush,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 7, 0, 0)
            });
        }

        var footer =
            new WrapPanel
            {
                HorizontalAlignment =
                    HorizontalAlignment.Right
            };

        DockPanel.SetDock(
            footer,
            Dock.Bottom);

        var copy =
            new Button
            {
                Content =
                    "Copy"
            };

        copy.Click +=
            (_, _) =>
                Clipboard.SetText(
                    $"Time: {entry.Timestamp:O}\r\n" +
                    $"User: {entry.User}\r\n" +
                    $"Computer: {entry.Computer}\r\n" +
                    $"Action: {entry.Action}\r\n" +
                    $"Object: {entry.ObjectType} / {entry.ObjectName}\r\n" +
                    $"Details: {entry.Details}\r\n\r\n" +
                    $"Before:\r\n{entry.Before}\r\n\r\n" +
                    $"After:\r\n{entry.After}");

        footer.Children.Add(
            copy);

        if (_restore is not null)
        {
            var restoreButton =
                new Button
                {
                    Content =
                        "Restore linked backup"
                };

            restoreButton.Click +=
                async (_, _) =>
                {
                    restoreButton.IsEnabled =
                        false;

                    try
                    {
                        await _restore();
                    }
                    finally
                    {
                        if (IsLoaded)
                        {
                            restoreButton.IsEnabled =
                                true;
                        }
                    }
                };

            footer.Children.Add(
                restoreButton);
        }

        footer.Children.Add(
            new Button
            {
                Content =
                    "Close",
                IsCancel =
                    true,
                IsDefault =
                    true
            });

        var grid =
            new Grid();

        grid.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width =
                    new GridLength(
                        1,
                        GridUnitType.Star)
            });

        grid.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width =
                    new GridLength(
                        1,
                        GridUnitType.Star)
            });

        grid.RowDefinitions.Add(
            new RowDefinition
            {
                Height =
                    GridLength.Auto
            });

        grid.RowDefinitions.Add(
            new RowDefinition
            {
                Height =
                    new GridLength(
                        1,
                        GridUnitType.Star)
            });

        var beforeLabel =
            new TextBlock
            {
                Text =
                    "Before",
                FontWeight =
                    FontWeights.SemiBold,
                Margin =
                    new Thickness(4)
            };

        var afterLabel =
            new TextBlock
            {
                Text =
                    "After",
                FontWeight =
                    FontWeights.SemiBold,
                Margin =
                    new Thickness(4)
            };

        Grid.SetColumn(
            afterLabel,
            1);

        grid.Children.Add(
            beforeLabel);

        grid.Children.Add(
            afterLabel);

        var before =
            CreateBox(
                entry.Before);

        var after =
            CreateBox(
                entry.After);

        Grid.SetRow(
            before,
            1);

        Grid.SetRow(
            after,
            1);

        Grid.SetColumn(
            after,
            1);

        grid.Children.Add(
            before);

        grid.Children.Add(
            after);

        root.Children.Add(
            header);

        root.Children.Add(
            footer);

        root.Children.Add(
            grid);

        Content =
            root;
    }

    private static TextBox CreateBox(
        string value) =>
        new()
        {
            Text =
                string.IsNullOrWhiteSpace(
                    value)
                    ? "<empty>"
                    : value,
            IsReadOnly =
                true,
            AcceptsReturn =
                true,
            AcceptsTab =
                true,
            TextWrapping =
                TextWrapping.NoWrap,
            FontFamily =
                UiStyle.MonospaceFontFamily,
            HorizontalScrollBarVisibility =
                ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility =
                ScrollBarVisibility.Auto,
            Margin =
                new Thickness(4)
        };
}

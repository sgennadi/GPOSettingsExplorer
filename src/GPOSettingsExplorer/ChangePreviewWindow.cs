using System.Windows;
using System.Windows.Controls;
using GPOSettingsExplorer.Services;

namespace GPOSettingsExplorer;

public sealed class ChangePreviewWindow : Window
{
    public bool Approved { get; private set; }

    public ChangePreviewWindow(
        ChangePreviewRequest request)
    {
        Title =
            request.Title;

        Width =
            1120;

        Height =
            720;

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
                    request.Title,
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
                    request.Target,
                TextWrapping =
                    TextWrapping.Wrap,
                Margin =
                    new Thickness(
                        0,
                        4,
                        0,
                        0)
            });

        if (!string.IsNullOrWhiteSpace(
                request.Details))
        {
            header.Children.Add(
                new TextBlock
                {
                    Text =
                        request.Details,
                    TextWrapping =
                        TextWrapping.Wrap,
                    Foreground =
                        System.Windows.Media.Brushes.DimGray,
                    Margin =
                        new Thickness(
                            0,
                            4,
                            0,
                            0)
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

        var cancel =
            new Button
            {
                Content =
                    "Cancel",
                IsCancel =
                    true
            };

        var apply =
            new Button
            {
                Content =
                    request.ActionText,
                IsDefault =
                    true
            };

        apply.Click +=
            (_, _) =>
            {
                Approved =
                    true;

                DialogResult =
                    true;
            };

        footer.Children.Add(
            cancel);

        footer.Children.Add(
            apply);

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
                    new Thickness(
                        4,
                        4,
                        4,
                        8)
            };

        var afterLabel =
            new TextBlock
            {
                Text =
                    "After",
                FontWeight =
                    FontWeights.SemiBold,
                Margin =
                    new Thickness(
                        12,
                        4,
                        4,
                        8)
            };

        Grid.SetColumn(
            beforeLabel,
            0);

        Grid.SetColumn(
            afterLabel,
            1);

        grid.Children.Add(
            beforeLabel);

        grid.Children.Add(
            afterLabel);

        var before =
            PreviewBox(
                request.Before);

        var after =
            PreviewBox(
                request.After);

        Grid.SetRow(
            before,
            1);

        Grid.SetColumn(
            before,
            0);

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

    private static TextBox PreviewBox(
        string value) =>
        new()
        {
            Text =
                value,
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

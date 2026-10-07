using System.Windows;
using System.Windows.Controls;
using GPOSettingsExplorer.Models;

namespace GPOSettingsExplorer;

public sealed class SettingValueWindow : Window
{
    public SettingValueWindow(
        GpoInfo gpo,
        PolicySettingInfo setting,
        Action openGpoEditor)
    {
        Title =
            $"Setting Value - {setting.SettingName}";

        Width = 820;
        Height = 560;
        MinWidth = 620;
        MinHeight = 420;
        WindowStartupLocation =
            WindowStartupLocation.CenterOwner;

        var root =
            new DockPanel
            {
                Margin =
                    new Thickness(14)
            };

        var footer =
            new WrapPanel
            {
                HorizontalAlignment =
                    HorizontalAlignment.Right
            };

        DockPanel.SetDock(
            footer,
            Dock.Bottom);

        var open =
            new Button
            {
                Content = "Open GPO editor..."
            };

        open.Click += (_, _) =>
        {
            try
            {
                openGpoEditor();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    this,
                    ex.Message,
                    "Open GPO Editor",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        };

        var close =
            new Button
            {
                Content = "Close",
                IsCancel = true
            };

        footer.Children.Add(open);
        footer.Children.Add(close);

        var panel =
            new StackPanel();

        panel.Children.Add(
            new TextBlock
            {
                Text = setting.SettingName,
                FontSize = UiStyle.HeadingFontSize,
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap,
                Margin =
                    new Thickness(
                        4,
                        0,
                        4,
                        10)
            });

        panel.Children.Add(
            Meta(
                "GPO",
                gpo.DisplayName));
        panel.Children.Add(
            Meta(
                "Scope",
                setting.Scope));
        panel.Children.Add(
            Meta(
                "Extension",
                setting.Extension));
        panel.Children.Add(
            Meta(
                "Category",
                setting.Category));
        panel.Children.Add(
            Meta(
                "State",
                setting.State));
        panel.Children.Add(
            Meta(
                "Registry key",
                setting.RegistryKey));
        panel.Children.Add(
            Meta(
                "Registry value",
                setting.RegistryValue));

        var valueBox =
            new TextBox
            {
                Text =
                    string.IsNullOrWhiteSpace(setting.Value)
                        ? "<not reported>"
                        : setting.Value,
                IsReadOnly = true,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                MinHeight = 150,
                VerticalScrollBarVisibility =
                    ScrollBarVisibility.Auto,
                Margin =
                    new Thickness(
                        4,
                        8,
                        4,
                        8)
            };

        panel.Children.Add(
            new GroupBox
            {
                Header = "Value",
                Content = valueBox,
                Margin =
                    new Thickness(
                        4,
                        10,
                        4,
                        8)
            });

        panel.Children.Add(
            new TextBlock
            {
                Text =
                    "This setting is not backed by an ADMX definition that can be safely edited by the built-in registry-policy editor. The selected value is shown directly here. Use Open GPO editor for the native editor.",
                TextWrapping =
                    TextWrapping.Wrap,
                Margin =
                    new Thickness(
                        8,
                        4,
                        8,
                        8),
                Foreground =
                    System.Windows.Media.Brushes.DimGray
            });

        var scroll =
            new ScrollViewer
            {
                VerticalScrollBarVisibility =
                    ScrollBarVisibility.Auto,
                Content = panel
            };

        root.Children.Add(
            footer);
        root.Children.Add(
            scroll);

        Content =
            root;

        Loaded += (_, _) =>
        {
            valueBox.BringIntoView();
            valueBox.Focus();
            valueBox.SelectAll();
        };
    }

    private static FrameworkElement Meta(
        string name,
        string value)
    {
        var grid =
            new Grid
            {
                Margin =
                    new Thickness(
                        4,
                        2,
                        4,
                        2)
            };

        grid.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width =
                    new GridLength(130)
            });

        grid.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width =
                    new GridLength(
                        1,
                        GridUnitType.Star)
            });

        var label =
            new TextBlock
            {
                Text = name + ":",
                FontWeight =
                    FontWeights.SemiBold,
                TextWrapping =
                    TextWrapping.Wrap
            };

        var text =
            new TextBlock
            {
                Text =
                    string.IsNullOrWhiteSpace(value)
                        ? "<not reported>"
                        : value,
                TextWrapping =
                    TextWrapping.Wrap
            };

        Grid.SetColumn(
            label,
            0);

        Grid.SetColumn(
            text,
            1);

        grid.Children.Add(
            label);
        grid.Children.Add(
            text);

        return grid;
    }
}

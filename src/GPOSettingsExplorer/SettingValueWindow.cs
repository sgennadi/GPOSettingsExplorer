using System.Windows;
using System.Windows.Controls;
using GPOSettingsExplorer.Models;

namespace GPOSettingsExplorer;

public sealed class SettingValueWindow : Window
{
    private readonly ComboBox? _booleanCombo;

    public bool? SelectedBooleanValue =>
        _booleanCombo?.SelectedItem is bool value
            ? value
            : null;

    public SettingValueWindow(
        GpoInfo gpo,
        PolicySettingInfo setting,
        bool canEditBoolean,
        Func<Task<bool>> openExactGpoEditor)
    {
        Title =
            $"Setting Value - {setting.SettingName}";

        Width = 660;
        Height =
            canEditBoolean
                ? 360
                : 430;

        MinWidth = 520;
        MinHeight =
            canEditBoolean
                ? 310
                : 350;

        WindowStartupLocation =
            WindowStartupLocation.CenterOwner;

        var root =
            new DockPanel
            {
                Margin =
                    new Thickness(12)
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
                Content = "Open exact setting in GPO editor..."
            };

        open.Click += async (_, _) =>
        {
            open.IsEnabled = false;

            try
            {
                var exact =
                    await openExactGpoEditor();

                if (!exact)
                {
                    MessageBox.Show(
                        this,
                        "The GPO editor was opened, but MMC could not automatically select the exact setting on this Windows build. The GPO remains open for manual navigation.",
                        "GPO Editor Navigation",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
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
            finally
            {
                open.IsEnabled = true;
            }
        };

        footer.Children.Add(
            open);

        if (canEditBoolean &&
            bool.TryParse(
                setting.Value,
                out var currentBoolean))
        {
            _booleanCombo =
                new ComboBox
                {
                    ItemsSource =
                        new object[]
                        {
                            true,
                            false
                        },
                    SelectedItem =
                        currentBoolean,
                    MinWidth = 120
                };

            var apply =
                new Button
                {
                    Content = "Apply",
                    IsDefault = true
                };

            apply.Click += (_, _) =>
            {
                DialogResult = true;
            };

            footer.Children.Add(
                apply);
        }

        footer.Children.Add(
            new Button
            {
                Content = "Close",
                IsCancel = true
            });

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
                        8)
            });

        panel.Children.Add(
            Meta(
                "GPO",
                gpo.DisplayName));

        panel.Children.Add(
            Meta(
                "Category",
                setting.Category));

        panel.Children.Add(
            Meta(
                "Registry",
                BuildRegistrySummary(
                    setting)));

        if (_booleanCombo is not null)
        {
            var valueGrid =
                new Grid
                {
                    Margin =
                        new Thickness(
                            4,
                            12,
                            4,
                            8)
                };

            valueGrid.ColumnDefinitions.Add(
                new ColumnDefinition
                {
                    Width =
                        new GridLength(130)
                });

            valueGrid.ColumnDefinitions.Add(
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
                    Text = "Value:",
                    FontWeight =
                        FontWeights.SemiBold,
                    VerticalAlignment =
                        VerticalAlignment.Center
                };

            Grid.SetColumn(
                label,
                0);

            Grid.SetColumn(
                _booleanCombo,
                1);

            valueGrid.Children.Add(
                label);

            valueGrid.Children.Add(
                _booleanCombo);

            panel.Children.Add(
                valueGrid);

            panel.Children.Add(
                new TextBlock
                {
                    Text =
                        "This Boolean Security Option can be changed directly. A GPO backup is created before the value is written.",
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
        }
        else
        {
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
                    MinHeight = 80,
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
                            8,
                            4,
                            8)
                });
        }

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
            _booleanCombo?.Focus();
        };
    }

    private static string BuildRegistrySummary(
        PolicySettingInfo setting)
    {
        if (string.IsNullOrWhiteSpace(
                setting.RegistryKey))
        {
            return "<not reported>";
        }

        return string.IsNullOrWhiteSpace(
                setting.RegistryValue)
            ? setting.RegistryKey
            : setting.RegistryKey +
              " \\ " +
              setting.RegistryValue;
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

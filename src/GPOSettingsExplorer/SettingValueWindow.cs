using System.Windows;
using System.Windows.Controls;
using GPOSettingsExplorer.Models;
using GPOSettingsExplorer.Services;

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
        bool exactNavigationAvailable,
        Func<IProgress<string>, Task<bool>> openExactGpoEditor)
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

        var navigationStatus =
            new TextBlock
            {
                Text =
                    exactNavigationAvailable
                        ? $"Exact MMC target: {GpoEditorNavigatorService.NavigationTarget(setting)}"
                        : setting.Extension.Equals(
                            "RegistrySettings",
                            StringComparison.OrdinalIgnoreCase) &&
                          setting.Value.Contains(
                              "AdmSetting=false",
                              StringComparison.OrdinalIgnoreCase)
                            ? "This is an Extra Registry Setting from registry.pol (AdmSetting=false), not a Group Policy Preferences Registry item. There is no deterministic row for it in the standard GPO editor."
                            : "The standard GPO editor will be opened.",
                TextWrapping =
                    TextWrapping.Wrap,
                Foreground =
                    System.Windows.Media.Brushes.DimGray,
                Margin =
                    new Thickness(
                        4,
                        0,
                        4,
                        8)
            };

        var open =
            new Button
            {
                Content =
                    exactNavigationAvailable
                        ? "Open exact setting in GPO editor..."
                        : "Open GPO editor..."
            };

        open.Click += async (_, _) =>
        {
            var originalContent =
                open.Content;

            open.IsEnabled =
                false;

            open.Content =
                "Opening...";

            try
            {
                navigationStatus.Text =
                    "Starting Group Policy Management Editor...";

                var progress =
                    new Progress<string>(
                        message =>
                            navigationStatus.Text =
                                message);

                var exact =
                    await openExactGpoEditor(
                        progress);

                if (exact ||
                    !exactNavigationAvailable)
                {
                    Close();
                    return;
                }

                MessageBox.Show(
                    this,
                    BuildNavigationFallbackMessage(
                        setting),
                    "GPO Editor Navigation",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                navigationStatus.Text =
                    "GPO editor navigation failed.";

                ErrorDialog.Show(
                    this,
                    "Open GPO Editor",
                    $"Unable to navigate to: {GpoEditorNavigatorService.NavigationTarget(setting)}",
                    ex);
            }
            finally
            {
                if (IsLoaded)
                {
                    open.Content =
                        originalContent;

                    open.IsEnabled =
                        true;
                }
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
            navigationStatus);

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

    private static string BuildNavigationFallbackMessage(
        PolicySettingInfo setting)
    {
        if (setting.Extension.Equals(
                "RegistrySettings",
                StringComparison.OrdinalIgnoreCase))
        {
            return "The selected GPO was opened. This raw Registry setting does not map to a deterministic editable row in the standard GPO editor, so the closest Registry node remains open.";
        }

        if (setting.Extension.Equals(
                "AuditSettings",
                StringComparison.OrdinalIgnoreCase))
        {
            return "The selected GPO was opened at Advanced Audit Policy Configuration, but MMC did not expose the exact audit subcategory row reliably. The matching audit category remains open for manual selection.";
        }

        if (setting.Extension.Equals(
                "SoftwareInstallationSettings",
                StringComparison.OrdinalIgnoreCase))
        {
            return "The selected GPO was opened at Software installation, but MMC did not expose the exact package row reliably. The Software installation node remains open for manual selection.";
        }

        return "The selected GPO was opened. MMC did not expose the exact Security Option row reliably, so the Security Options node remains open for manual selection.";
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

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

        Width = 760;
        Height =
            canEditBoolean
                ? 360
                : 430;

        MinWidth = 540;
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
                Foreground = UiStyle.MutedBrush,
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
                        : setting.Extension.Equals("RegistrySettings", StringComparison.OrdinalIgnoreCase)
                            ? "Open Administrative Templates (manual)..."
                            : setting.Extension.Equals("SoftwareInstallationSettings", StringComparison.OrdinalIgnoreCase)
                                ? "Open Software installation (manual)..."
                                : setting.Extension.Equals("PublicKeySettings", StringComparison.OrdinalIgnoreCase)
                                    ? "Open Public Key Policies (manual)..."
                                    : setting.Extension.Equals("NrptSettings", StringComparison.OrdinalIgnoreCase)
                                        ? "Open Name Resolution Policy (manual)..."
                                        : "Open related GPO editor section..."
            };

        open.Click += async (_, _) =>
        {
            var originalContent = open.Content;

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

                // A detached MMC window is not a successful edit/save.
                // Keep the value and registry details available until the
                // operator explicitly clicks Close, regardless of result.
                if (exact)
                {
                    navigationStatus.Foreground = UiStyle.SuccessBrush;
                    navigationStatus.Text =
                        "Verified exact policy opened in MMC. This value window remains available.";
                    return;
                }

                if (!exactNavigationAvailable)
                {
                    navigationStatus.Foreground = UiStyle.WarningBrush;
                    var sectionPath = GpoEditorNavigatorService.NavigationTarget(setting);
                    navigationStatus.Text =
                        setting.Extension.Equals("RegistrySettings", StringComparison.OrdinalIgnoreCase)
                            ? "MMC opened for Administrative Templates. This raw registry.pol entry may not have a matching ADMX editor. Value retained below."
                            : $"Section-only MMC navigation attempted: {sectionPath}. Exact row editing is unavailable for this report entry; the value remains visible.";
                    return;
                }

                navigationStatus.Foreground = UiStyle.WarningBrush;

                MessageBox.Show(
                    this,
                    setting.Extension.Equals(
                        "RegistrySettings",
                        StringComparison.OrdinalIgnoreCase)
                        ? "A raw registry.pol entry cannot be opened as a Group Policy Preferences item. The corresponding ADMX definition may be missing. The value remains visible in this window."
                        : "The GPO editor opened the correct Security Options category, but the requested policy name could not be verified in MMC. No unrelated policy was opened. If the list cannot be inspected, try launching this program and MMC at the same elevation. The diagnostic log is stored in %LOCALAPPDATA%\\GPOSettingsExplorer\\Logs.",
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

        footer.Children.Add(open);

        // A raw registry.pol entry has no guaranteed MMC row. Make its real
        // key/value easy to inspect without opening the live local registry.
        if (!string.IsNullOrWhiteSpace(setting.RegistryKey))
        {
            var copyKey = new Button
            {
                Content = "Copy registry key",
                ToolTip = "Copy the registry.pol key path; this does not alter the GPO."
            };
            copyKey.Click += (_, _) => Clipboard.SetText(setting.RegistryKey);
            footer.Children.Add(copyKey);
        }

        if (!string.IsNullOrWhiteSpace(setting.RegistryValue))
        {
            var copyValueName = new Button { Content = "Copy value name" };
            copyValueName.Click += (_, _) => Clipboard.SetText(setting.RegistryValue);
            footer.Children.Add(copyValueName);
        }

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
                IsCancel = true,
                ToolTip = "Only this button (or Escape) closes the value window."
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

        if (setting.Extension.Equals("SoftwareInstallationSettings", StringComparison.OrdinalIgnoreCase) &&
            (setting.SettingName.Contains("Trustee ", StringComparison.OrdinalIgnoreCase) ||
             setting.SettingName.Contains("Applicability", StringComparison.OrdinalIgnoreCase)))
        {
            panel.Children.Add(new TextBlock
            {
                Text = "This is package security descriptor/auditing metadata from the GPO report, not a standalone Administrative Templates policy. Open Software installation and inspect the relevant package's security properties. The XML value below remains read-only.",
                Foreground = UiStyle.WarningBrush,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(4, 8, 4, 4)
            });
        }

        if (setting.Extension.Equals("PublicKeySettings", StringComparison.OrdinalIgnoreCase) ||
            setting.Extension.Equals("NrptSettings", StringComparison.OrdinalIgnoreCase))
        {
            panel.Children.Add(new TextBlock
            {
                Text = "MMC section: " + GpoEditorNavigatorService.NavigationTarget(setting) +
                    ". This XML summary does not identify a uniquely editable MMC field, so the program opens the related section without guessing a settings dialog.",
                Foreground = UiStyle.AccentBrush,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(4, 8, 4, 4)
            });
        }

        if (setting.Extension.Equals("RegistrySettings", StringComparison.OrdinalIgnoreCase) &&
            setting.Value.Contains("AdmSetting=false", StringComparison.OrdinalIgnoreCase))
        {
            panel.Children.Add(new TextBlock
            {
                Text = "Raw registry.pol setting (AdmSetting=false): MMC cannot navigate to an exact ADMX editor when the policy definition is absent. This is not a GPP Registry item. Use the values below or install the matching ADMX files to enable policy-level editing.",
                Foreground = UiStyle.WarningBrush,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(4, 8, 4, 4)
            });
        }

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

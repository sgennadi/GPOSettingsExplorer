using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Xml.Linq;
using GPOSettingsExplorer.Models;

namespace GPOSettingsExplorer;

public sealed class GppPowerOptionsEditorWindow : Window
{
    private readonly GppPowerOptionItemInfo _item;

    private readonly TextBox _nameBox;
    private readonly TextBox _descriptionBox;
    private readonly ComboBox _actionCombo;
    private readonly TextBox _planGuidBox;
    private readonly CheckBox _defaultCheck;

    private readonly Dictionary<string, TextBox> _numericBoxes =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, ComboBox> _comboBoxes =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly CheckBox _disabledCheck;
    private readonly CheckBox _bypassErrorsCheck;
    private readonly CheckBox _removePolicyCheck;
    private readonly CheckBox _userContextCheck;
    private readonly TextBox _filtersBox;

    public GppPowerOptionItemInfo Item => _item;

    public GppPowerOptionsEditorWindow(
        GppPowerOptionItemInfo item)
    {
        _item = item;

        Title = "Power Options Preference Editor";
        Width = 1080;
        Height = 820;
        MinWidth = 860;
        MinHeight = 620;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var root = new DockPanel { Margin = new Thickness(12) };

        var footer = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        DockPanel.SetDock(footer, Dock.Bottom);

        var cancel = new Button
        {
            Content = "Cancel",
            IsCancel = true
        };

        var save = new Button
        {
            Content = "Save",
            IsDefault = true,
            IsEnabled = item.SupportsStructuredEditing
        };
        save.Click += Save_Click;

        footer.Children.Add(cancel);
        footer.Children.Add(save);

        var header = new Border
        {
            BorderBrush = System.Windows.SystemColors.ControlDarkBrush,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(6),
            Margin = new Thickness(0, 0, 0, 8)
        };
        DockPanel.SetDock(header, Dock.Top);

        header.Child = new TextBlock
        {
            Text =
                $"GPO: {item.GpoName}   |   Scope: {item.Scope} Configuration   |   {item.KindDisplay}",
            FontWeight = FontWeights.SemiBold
        };

        var tabs = new TabControl();

        var generalGrid = CreateGrid(5);

        _nameBox = new TextBox
        {
            Text = item.DisplayName
        };

        _descriptionBox = new TextBox
        {
            Text = item.Description,
            AcceptsReturn = true,
            MinHeight = 70
        };

        _actionCombo = new ComboBox
        {
            ItemsSource = new[]
            {
                new Choice("C", "Create"),
                new Choice("U", "Update"),
                new Choice("R", "Replace"),
                new Choice("D", "Delete")
            },
            DisplayMemberPath = nameof(Choice.Display)
        };

        _actionCombo.SelectedItem =
            _actionCombo.Items
                .Cast<Choice>()
                .FirstOrDefault(
                    choice =>
                        choice.Value.Equals(
                            NormalizeAction(item.Action),
                            StringComparison.OrdinalIgnoreCase))
            ?? _actionCombo.Items.Cast<Choice>().First();

        _actionCombo.SelectionChanged +=
            (_, _) => UpdateGuidState();

        _planGuidBox = new TextBox
        {
            Text = item.PlanGuid
        };

        _defaultCheck = new CheckBox
        {
            Content = "Set this power plan as the default",
            IsChecked = item.SetAsDefault
        };

        AddRow(generalGrid, 0, "Display name:", _nameBox);
        AddRow(generalGrid, 1, "Description:", _descriptionBox);
        AddRow(generalGrid, 2, "Action:", _actionCombo);
        AddRow(generalGrid, 3, "Power plan GUID:", _planGuidBox);
        AddRow(generalGrid, 4, "Default plan:", _defaultCheck);

        var generalPanel = new StackPanel();
        generalPanel.Children.Add(generalGrid);

        if (!item.SupportsStructuredEditing)
        {
            generalPanel.Children.Add(
                new Border
                {
                    Margin = new Thickness(14, 8, 14, 8),
                    Padding = new Thickness(8),
                    BorderBrush = System.Windows.SystemColors.ControlDarkBrush,
                    BorderThickness = new Thickness(1),
                    Child = new TextBlock
                    {
                        Text =
                            "This is a legacy Power Options item. It is shown for visibility, but structured rewriting is disabled. Use Show raw XML from the main window for controlled editing.",
                        TextWrapping = TextWrapping.Wrap
                    }
                });
        }

        tabs.Items.Add(
            new TabItem
            {
                Header = "General",
                Content = new ScrollViewer
                {
                    VerticalScrollBarVisibility =
                        ScrollBarVisibility.Auto,
                    Content = generalPanel
                }
            });

        tabs.Items.Add(
            new TabItem
            {
                Header = "AC Power",
                Content = BuildPowerGrid(
                    "AC",
                    item)
            });

        tabs.Items.Add(
            new TabItem
            {
                Header = "Battery",
                Content = BuildPowerGrid(
                    "DC",
                    item)
            });

        var commonPanel = new StackPanel
        {
            Margin = new Thickness(14)
        };

        _disabledCheck =
            Check(
                "Disable this preference item",
                item.Disabled);

        _bypassErrorsCheck =
            Check(
                "Continue processing if this preference item fails",
                item.BypassErrors);

        _removePolicyCheck =
            Check(
                "Remove this item when it is no longer applied",
                item.RemoveWhenNoLongerApplied);

        _userContextCheck =
            Check(
                "Run in logged-on user's security context",
                item.RunInUserContext);

        commonPanel.Children.Add(_disabledCheck);
        commonPanel.Children.Add(_bypassErrorsCheck);
        commonPanel.Children.Add(_removePolicyCheck);
        commonPanel.Children.Add(_userContextCheck);

        tabs.Items.Add(
            new TabItem
            {
                Header = "Common",
                Content = commonPanel
            });

        var targetingPanel = new DockPanel
        {
            Margin = new Thickness(10)
        };

        var targetingHelp = new TextBlock
        {
            Text =
                "Advanced item-level targeting XML. Existing targeting is preserved. Leave empty to remove item-level targeting.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(4, 4, 4, 8)
        };
        DockPanel.SetDock(
            targetingHelp,
            Dock.Top);

        targetingPanel.Children.Add(
            targetingHelp);

        _filtersBox = new TextBox
        {
            Text = item.FiltersXml,
            AcceptsReturn = true,
            AcceptsTab = true,
            HorizontalScrollBarVisibility =
                ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility =
                ScrollBarVisibility.Auto
        };

        targetingPanel.Children.Add(
            _filtersBox);

        tabs.Items.Add(
            new TabItem
            {
                Header = item.HasFilters
                    ? "Item-level Targeting *"
                    : "Item-level Targeting",
                Content = targetingPanel
            });

        root.Children.Add(footer);
        root.Children.Add(header);
        root.Children.Add(tabs);

        Content = root;

        UpdateGuidState();
    }

    private FrameworkElement BuildPowerGrid(
        string suffix,
        GppPowerOptionItemInfo item)
    {
        var panel = new StackPanel();

        panel.Children.Add(
            new TextBlock
            {
                Text = suffix == "AC"
                    ? "Settings used while connected to AC power."
                    : "Settings used while running on battery power.",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(14, 10, 14, 4)
            });

        var grid = CreateGrid(18);

        AddComboRow(
            grid,
            0,
            suffix,
            "Wake password:",
            "RequireWakePassword",
            suffix == "AC"
                ? item.RequireWakePasswordAc
                : item.RequireWakePasswordDc,
            new[] { "YES", "NO" });

        AddNumericRow(
            grid,
            1,
            suffix,
            "Turn off hard disk:",
            "TurnOffHardDisk",
            suffix == "AC"
                ? item.TurnOffHardDiskAc
                : item.TurnOffHardDiskDc);

        AddNumericRow(
            grid,
            2,
            suffix,
            "Sleep after:",
            "SleepAfter",
            suffix == "AC"
                ? item.SleepAfterAc
                : item.SleepAfterDc);

        AddComboRow(
            grid,
            3,
            suffix,
            "Hybrid sleep:",
            "AllowHybridSleep",
            suffix == "AC"
                ? item.AllowHybridSleepAc
                : item.AllowHybridSleepDc,
            new[] { "ON", "OFF" });

        AddNumericRow(
            grid,
            4,
            suffix,
            "Hibernate after:",
            "HibernateAfter",
            suffix == "AC"
                ? item.HibernateAfterAc
                : item.HibernateAfterDc);

        var powerActions =
            new[]
            {
                "DO_NOTHING",
                "SLEEP",
                "HIBERNATE",
                "SHUT_DOWN"
            };

        AddComboRow(
            grid,
            5,
            suffix,
            "Lid close action:",
            "LidClose",
            suffix == "AC"
                ? item.LidCloseAc
                : item.LidCloseDc,
            powerActions);

        AddComboRow(
            grid,
            6,
            suffix,
            "Power button action:",
            "PowerButton",
            suffix == "AC"
                ? item.PowerButtonAc
                : item.PowerButtonDc,
            powerActions);

        AddComboRow(
            grid,
            7,
            suffix,
            "Start menu power action:",
            "StartMenuPower",
            suffix == "AC"
                ? item.StartMenuPowerAc
                : item.StartMenuPowerDc,
            powerActions);

        AddComboRow(
            grid,
            8,
            suffix,
            "Link power management:",
            "LinkPowerManagement",
            suffix == "AC"
                ? item.LinkPowerManagementAc
                : item.LinkPowerManagementDc,
            new[] { "ON", "OFF" });

        AddNumericRow(
            grid,
            9,
            suffix,
            "Processor minimum (%):",
            "ProcessorMin",
            suffix == "AC"
                ? item.ProcessorMinAc
                : item.ProcessorMinDc);

        AddNumericRow(
            grid,
            10,
            suffix,
            "Processor maximum (%):",
            "ProcessorMax",
            suffix == "AC"
                ? item.ProcessorMaxAc
                : item.ProcessorMaxDc);

        AddNumericRow(
            grid,
            11,
            suffix,
            "Display off:",
            "DisplayOff",
            suffix == "AC"
                ? item.DisplayOffAc
                : item.DisplayOffDc);

        AddComboRow(
            grid,
            12,
            suffix,
            "Adaptive display:",
            "AdaptiveDisplay",
            suffix == "AC"
                ? item.AdaptiveDisplayAc
                : item.AdaptiveDisplayDc,
            new[] { "ON", "OFF" });

        AddComboRow(
            grid,
            13,
            suffix,
            "Critical battery action:",
            "CriticalBatteryAction",
            suffix == "AC"
                ? item.CriticalBatteryActionAc
                : item.CriticalBatteryActionDc,
            powerActions);

        AddNumericRow(
            grid,
            14,
            suffix,
            "Low battery level (%):",
            "LowBatteryLevel",
            suffix == "AC"
                ? item.LowBatteryLevelAc
                : item.LowBatteryLevelDc);

        AddNumericRow(
            grid,
            15,
            suffix,
            "Critical battery level (%):",
            "CriticalBatteryLevel",
            suffix == "AC"
                ? item.CriticalBatteryLevelAc
                : item.CriticalBatteryLevelDc);

        AddComboRow(
            grid,
            16,
            suffix,
            "Low battery notification:",
            "LowBatteryNotification",
            suffix == "AC"
                ? item.LowBatteryNotificationAc
                : item.LowBatteryNotificationDc,
            new[] { "ON", "OFF" });

        AddComboRow(
            grid,
            17,
            suffix,
            "Low battery action:",
            "LowBatteryAction",
            suffix == "AC"
                ? item.LowBatteryActionAc
                : item.LowBatteryActionDc,
            powerActions);

        panel.Children.Add(grid);

        panel.Children.Add(
            new TextBlock
            {
                Text =
                    "Timeout fields use the integer values stored by Group Policy Preferences. Processor and battery levels are percentages.",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(14, 4, 14, 12),
                Foreground = System.Windows.SystemColors.GrayTextBrush
            });

        return new ScrollViewer
        {
            VerticalScrollBarVisibility =
                ScrollBarVisibility.Auto,
            Content = panel
        };
    }

    private void AddNumericRow(
        Grid grid,
        int row,
        string suffix,
        string label,
        string key,
        string value)
    {
        var box = new TextBox
        {
            Text = value
        };

        _numericBoxes[key + suffix] =
            box;

        AddRow(
            grid,
            row,
            label,
            box);
    }

    private void AddComboRow(
        Grid grid,
        int row,
        string suffix,
        string label,
        string key,
        string selected,
        IEnumerable<string> values)
    {
        var combo = new ComboBox
        {
            ItemsSource = values.ToArray()
        };

        combo.SelectedItem =
            combo.Items
                .Cast<string>()
                .FirstOrDefault(
                    value =>
                        value.Equals(
                            selected,
                            StringComparison.OrdinalIgnoreCase))
            ?? combo.Items.Cast<string>().FirstOrDefault();

        _comboBoxes[key + suffix] =
            combo;

        AddRow(
            grid,
            row,
            label,
            combo);
    }

    private void Save_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(
                _nameBox.Text))
        {
            MessageBox.Show(
                this,
                "Display name cannot be empty.",
                "Power Options Preference",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            _nameBox.Focus();
            return;
        }

        var action =
            (_actionCombo.SelectedItem
                as Choice)?.Value
            ?? "U";

        if (!action.Equals(
                "C",
                StringComparison.OrdinalIgnoreCase) &&
            !Guid.TryParse(
                _planGuidBox.Text.Trim(),
                out _))
        {
            MessageBox.Show(
                this,
                "Power plan GUID must be a valid GUID for Update, Replace, or Delete.",
                "Power Options Preference",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            _planGuidBox.Focus();
            return;
        }

        foreach (var key in new[]
        {
            "TurnOffHardDiskAC",
            "TurnOffHardDiskDC",
            "SleepAfterAC",
            "SleepAfterDC",
            "HibernateAfterAC",
            "HibernateAfterDC",
            "DisplayOffAC",
            "DisplayOffDC"
        })
        {
            if (!_numericBoxes.TryGetValue(
                    key,
                    out var box))
            {
                continue;
            }

            if (!byte.TryParse(
                    box.Text.Trim(),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out _))
            {
                MessageBox.Show(
                    this,
                    $"{key} must be an integer from 0 to 255.",
                    "Power Options Preference",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                box.Focus();
                return;
            }
        }

        foreach (var key in new[]
        {
            "ProcessorMinAC",
            "ProcessorMinDC",
            "ProcessorMaxAC",
            "ProcessorMaxDC",
            "LowBatteryLevelAC",
            "LowBatteryLevelDC",
            "CriticalBatteryLevelAC",
            "CriticalBatteryLevelDC"
        })
        {
            if (!_numericBoxes.TryGetValue(
                    key,
                    out var box))
            {
                continue;
            }

            if (!byte.TryParse(
                    box.Text.Trim(),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var number) ||
                number > 100)
            {
                MessageBox.Show(
                    this,
                    $"{key} must be an integer from 0 to 100.",
                    "Power Options Preference",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                box.Focus();
                return;
            }
        }

        if (Number("ProcessorMinAC") >
                Number("ProcessorMaxAC") ||
            Number("ProcessorMinDC") >
                Number("ProcessorMaxDC"))
        {
            MessageBox.Show(
                this,
                "Processor minimum state cannot be greater than maximum state.",
                "Power Options Preference",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (Number("CriticalBatteryLevelAC") >
                Number("LowBatteryLevelAC") ||
            Number("CriticalBatteryLevelDC") >
                Number("LowBatteryLevelDC"))
        {
            MessageBox.Show(
                this,
                "Critical battery level cannot be greater than low battery level.",
                "Power Options Preference",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (!string.IsNullOrWhiteSpace(
                _filtersBox.Text))
        {
            try
            {
                var filters =
                    XElement.Parse(
                        _filtersBox.Text);

                if (!filters.Name.LocalName.Equals(
                        "Filters",
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        "The root element must be <Filters>.");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    this,
                    $"Item-level targeting XML is invalid:\n\n{ex.Message}",
                    "Power Options Preference",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }
        }

        _item.DisplayName =
            _nameBox.Text.Trim();

        _item.Description =
            _descriptionBox.Text.Trim();

        _item.Action =
            action;

        _item.PlanGuid =
            action.Equals(
                "C",
                StringComparison.OrdinalIgnoreCase)
                ? string.Empty
                : new Guid(
                        _planGuidBox.Text.Trim())
                    .ToString("B")
                    .ToUpperInvariant();

        _item.SetAsDefault =
            _defaultCheck.IsChecked == true;

        _item.RequireWakePasswordAc =
            Selected("RequireWakePasswordAC");

        _item.RequireWakePasswordDc =
            Selected("RequireWakePasswordDC");

        _item.TurnOffHardDiskAc =
            Text("TurnOffHardDiskAC");

        _item.TurnOffHardDiskDc =
            Text("TurnOffHardDiskDC");

        _item.SleepAfterAc =
            Text("SleepAfterAC");

        _item.SleepAfterDc =
            Text("SleepAfterDC");

        _item.AllowHybridSleepAc =
            Selected("AllowHybridSleepAC");

        _item.AllowHybridSleepDc =
            Selected("AllowHybridSleepDC");

        _item.HibernateAfterAc =
            Text("HibernateAfterAC");

        _item.HibernateAfterDc =
            Text("HibernateAfterDC");

        _item.LidCloseAc =
            Selected("LidCloseAC");

        _item.LidCloseDc =
            Selected("LidCloseDC");

        _item.PowerButtonAc =
            Selected("PowerButtonAC");

        _item.PowerButtonDc =
            Selected("PowerButtonDC");

        _item.StartMenuPowerAc =
            Selected("StartMenuPowerAC");

        _item.StartMenuPowerDc =
            Selected("StartMenuPowerDC");

        _item.LinkPowerManagementAc =
            Selected("LinkPowerManagementAC");

        _item.LinkPowerManagementDc =
            Selected("LinkPowerManagementDC");

        _item.ProcessorMinAc =
            Text("ProcessorMinAC");

        _item.ProcessorMinDc =
            Text("ProcessorMinDC");

        _item.ProcessorMaxAc =
            Text("ProcessorMaxAC");

        _item.ProcessorMaxDc =
            Text("ProcessorMaxDC");

        _item.DisplayOffAc =
            Text("DisplayOffAC");

        _item.DisplayOffDc =
            Text("DisplayOffDC");

        _item.AdaptiveDisplayAc =
            Selected("AdaptiveDisplayAC");

        _item.AdaptiveDisplayDc =
            Selected("AdaptiveDisplayDC");

        _item.CriticalBatteryActionAc =
            Selected("CriticalBatteryActionAC");

        _item.CriticalBatteryActionDc =
            Selected("CriticalBatteryActionDC");

        _item.LowBatteryLevelAc =
            Text("LowBatteryLevelAC");

        _item.LowBatteryLevelDc =
            Text("LowBatteryLevelDC");

        _item.CriticalBatteryLevelAc =
            Text("CriticalBatteryLevelAC");

        _item.CriticalBatteryLevelDc =
            Text("CriticalBatteryLevelDC");

        _item.LowBatteryNotificationAc =
            Selected("LowBatteryNotificationAC");

        _item.LowBatteryNotificationDc =
            Selected("LowBatteryNotificationDC");

        _item.LowBatteryActionAc =
            Selected("LowBatteryActionAC");

        _item.LowBatteryActionDc =
            Selected("LowBatteryActionDC");

        _item.Disabled =
            _disabledCheck.IsChecked == true;

        _item.BypassErrors =
            _bypassErrorsCheck.IsChecked == true;

        _item.RemoveWhenNoLongerApplied =
            _removePolicyCheck.IsChecked == true;

        _item.RunInUserContext =
            _userContextCheck.IsChecked == true;

        _item.FiltersXml =
            _filtersBox.Text.Trim();

        DialogResult = true;
    }

    private void UpdateGuidState()
    {
        var action =
            (_actionCombo.SelectedItem
                as Choice)?.Value
            ?? "U";

        _planGuidBox.IsEnabled =
            !action.Equals(
                "C",
                StringComparison.OrdinalIgnoreCase);
    }

    private int Number(
        string key) =>
        int.TryParse(
            Text(key),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var number)
            ? number
            : 0;

    private string Text(
        string key) =>
        _numericBoxes.TryGetValue(
            key,
            out var box)
            ? box.Text.Trim()
            : string.Empty;

    private string Selected(
        string key) =>
        _comboBoxes.TryGetValue(
            key,
            out var combo)
            ? Convert.ToString(
                  combo.SelectedItem)
              ?? string.Empty
            : string.Empty;

    private static Grid CreateGrid(
        int rows)
    {
        var grid = new Grid
        {
            Margin = new Thickness(14)
        };

        grid.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width =
                    new GridLength(
                        220)
            });

        grid.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width =
                    new GridLength(
                        1,
                        GridUnitType.Star)
            });

        for (var index = 0;
             index < rows;
             index++)
        {
            grid.RowDefinitions.Add(
                new RowDefinition
                {
                    Height =
                        GridLength.Auto
                });
        }

        return grid;
    }

    private static void AddRow(
        Grid grid,
        int row,
        string label,
        FrameworkElement editor)
    {
        var caption = new TextBlock
        {
            Text = label,
            VerticalAlignment =
                VerticalAlignment.Top,
            TextWrapping =
                TextWrapping.Wrap,
            Margin =
                new Thickness(
                    4,
                    9,
                    8,
                    8)
        };

        editor.Margin =
            new Thickness(
                4,
                6,
                4,
                6);

        Grid.SetRow(
            caption,
            row);

        Grid.SetColumn(
            caption,
            0);

        Grid.SetRow(
            editor,
            row);

        Grid.SetColumn(
            editor,
            1);

        grid.Children.Add(
            caption);

        grid.Children.Add(
            editor);
    }

    private static CheckBox Check(
        string text,
        bool value) =>
        new()
        {
            Content = text,
            IsChecked = value,
            Margin =
                new Thickness(
                    4,
                    5,
                    4,
                    5)
        };

    private static string NormalizeAction(
        string value) =>
        value.ToUpperInvariant() switch
        {
            "C" => "C",
            "R" => "R",
            "D" => "D",
            _ => "U"
        };

    private sealed record Choice(
        string Value,
        string Display);
}

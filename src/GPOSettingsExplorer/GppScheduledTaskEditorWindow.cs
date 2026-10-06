using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Xml.Linq;
using GPOSettingsExplorer.Models;

namespace GPOSettingsExplorer;

public sealed class GppScheduledTaskEditorWindow : Window
{
    private readonly GppScheduledTaskItemInfo _item;

    private readonly TextBox _nameBox;
    private readonly TextBox _descriptionBox;
    private readonly ComboBox _actionCombo;
    private readonly TextBox _runAsBox;
    private readonly ComboBox _logonTypeCombo;
    private readonly ComboBox _runLevelCombo;
    private readonly TextBox _authorBox;

    private readonly TextBox _commandBox;
    private readonly TextBox _argumentsBox;
    private readonly TextBox _workingDirectoryBox;

    private readonly ComboBox _triggerTypeCombo;
    private readonly TextBox _startBoundaryBox;
    private readonly TextBox _daysIntervalBox;
    private readonly TextBox _weeksIntervalBox;
    private readonly TextBox _daysOfWeekBox;
    private readonly TextBox _triggerDelayBox;

    private readonly CheckBox _taskEnabledCheck;
    private readonly CheckBox _hiddenCheck;
    private readonly CheckBox _startWhenAvailableCheck;
    private readonly CheckBox _networkCheck;
    private readonly CheckBox _disallowBatteryCheck;
    private readonly CheckBox _stopOnBatteryCheck;
    private readonly CheckBox _wakeCheck;
    private readonly CheckBox _allowDemandCheck;
    private readonly ComboBox _instancesCombo;
    private readonly TextBox _executionLimitBox;
    private readonly TextBox _priorityBox;

    private readonly CheckBox _disabledCheck;
    private readonly CheckBox _bypassErrorsCheck;
    private readonly CheckBox _removePolicyCheck;
    private readonly CheckBox _userContextCheck;
    private readonly CheckBox _clearCredentialCheck;
    private readonly TextBox _filtersBox;

    public GppScheduledTaskItemInfo Item => _item;

    public GppScheduledTaskEditorWindow(GppScheduledTaskItemInfo item)
    {
        _item = item;

        Title = "Scheduled Task Preference Editor";
        Width = 1040;
        Height = 820;
        MinWidth = 820;
        MinHeight = 640;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var root = new DockPanel { Margin = new Thickness(12) };

        var footer = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        DockPanel.SetDock(footer, Dock.Bottom);

        var cancel = new Button { Content = "Cancel", IsCancel = true };
        var save = new Button { Content = "Save", IsDefault = true };
        save.Click += Save_Click;
        footer.Children.Add(cancel);
        footer.Children.Add(save);

        var header = new Border
        {
            BorderBrush = System.Windows.Media.Brushes.LightGray,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(6),
            Margin = new Thickness(0, 0, 0, 8)
        };
        DockPanel.SetDock(header, Dock.Top);

        header.Child = new TextBlock
        {
            Text = $"GPO: {item.GpoName}   |   Scope: {item.Scope} Configuration   |   {item.KindDisplay}",
            FontWeight = FontWeights.SemiBold
        };

        var tabs = new TabControl();

        var generalGrid = CreateGrid(7);
        _nameBox = new TextBox { Text = item.DisplayName };
        _descriptionBox = new TextBox
        {
            Text = item.Description,
            AcceptsReturn = true,
            MinHeight = 70
        };
        _actionCombo = Combo(
            new[] { "C", "U", "R", "D" },
            NormalizeAction(item.Action));
        _runAsBox = new TextBox { Text = item.RunAs };
        _logonTypeCombo = Combo(
            new[] { "InteractiveToken", "Password", "S4U", "ServiceAccount", "InteractiveTokenOrPassword" },
            FirstNonEmpty(item.LogonType, "InteractiveToken"));
        _runLevelCombo = Combo(
            new[] { "LeastPrivilege", "HighestAvailable" },
            FirstNonEmpty(item.RunLevel, "LeastPrivilege"));
        _authorBox = new TextBox { Text = item.Author };

        AddRow(generalGrid, 0, "Task name:", _nameBox);
        AddRow(generalGrid, 1, "Description:", _descriptionBox);
        AddRow(generalGrid, 2, "Preference action:", _actionCombo);
        AddRow(generalGrid, 3, "Run as:", _runAsBox);
        AddRow(generalGrid, 4, "Logon type:", _logonTypeCombo);
        AddRow(generalGrid, 5, "Run level:", _runLevelCombo);
        AddRow(generalGrid, 6, "Author:", _authorBox);

        var generalPanel = new StackPanel();
        generalPanel.Children.Add(generalGrid);

        var credentialBox = new GroupBox
        {
            Header = "Stored credential",
            Margin = new Thickness(14, 8, 14, 8)
        };

        var credentialPanel = new StackPanel { Margin = new Thickness(8) };
        credentialPanel.Children.Add(new TextBlock
        {
            Text = item.HasStoredCredential
                ? "This existing task preference contains legacy encrypted cPassword data. It is preserved opaquely and is never displayed."
                : "No stored cPassword is present. GPO Settings Explorer does not create new legacy GPP passwords.",
            TextWrapping = TextWrapping.Wrap
        });

        _clearCredentialCheck = new CheckBox
        {
            Content = "Remove the stored legacy credential when saving",
            IsChecked = false,
            IsEnabled = item.HasStoredCredential,
            Margin = new Thickness(0, 8, 0, 0)
        };

        credentialPanel.Children.Add(_clearCredentialCheck);
        credentialBox.Content = credentialPanel;
        generalPanel.Children.Add(credentialBox);

        tabs.Items.Add(new TabItem
        {
            Header = "General",
            Content = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = generalPanel
            }
        });

        var actionGrid = CreateGrid(3);
        _commandBox = new TextBox { Text = item.Command };
        _argumentsBox = new TextBox { Text = item.Arguments };
        _workingDirectoryBox = new TextBox { Text = item.WorkingDirectory };
        AddRow(actionGrid, 0, "Program / script:", _commandBox);
        AddRow(actionGrid, 1, "Arguments:", _argumentsBox);
        AddRow(actionGrid, 2, "Start in:", _workingDirectoryBox);

        tabs.Items.Add(new TabItem
        {
            Header = "Action",
            Content = actionGrid
        });

        var triggerGrid = CreateGrid(6);
        _triggerTypeCombo = Combo(
            item.IsImmediate
                ? new[] { "Immediate" }
                : new[] { "Daily", "Weekly", "Once", "AtStartup", "AtLogon" },
            item.IsImmediate ? "Immediate" : FirstNonEmpty(item.TriggerType, "Daily"));
        _triggerTypeCombo.SelectionChanged += (_, _) => UpdateTriggerFields();

        _startBoundaryBox = new TextBox { Text = item.StartBoundary };
        _daysIntervalBox = new TextBox { Text = item.DaysInterval };
        _weeksIntervalBox = new TextBox { Text = item.WeeksInterval };
        _daysOfWeekBox = new TextBox { Text = item.DaysOfWeek };
        _triggerDelayBox = new TextBox { Text = item.TriggerDelay };

        AddRow(triggerGrid, 0, "Trigger:", _triggerTypeCombo);
        AddRow(triggerGrid, 1, "Start boundary:", _startBoundaryBox);
        AddRow(triggerGrid, 2, "Days interval:", _daysIntervalBox);
        AddRow(triggerGrid, 3, "Weeks interval:", _weeksIntervalBox);
        AddRow(triggerGrid, 4, "Days of week:", _daysOfWeekBox);
        AddRow(triggerGrid, 5, "Delay:", _triggerDelayBox);

        tabs.Items.Add(new TabItem
        {
            Header = "Trigger",
            Content = triggerGrid
        });

        var settingsGrid = CreateGrid(7);
        _instancesCombo = Combo(
            new[] { "Parallel", "Queue", "IgnoreNew", "StopExisting" },
            FirstNonEmpty(item.MultipleInstancesPolicy, "IgnoreNew"));
        _executionLimitBox = new TextBox { Text = item.ExecutionTimeLimit };
        _priorityBox = new TextBox { Text = item.Priority };

        _taskEnabledCheck = Check("Enable task", item.TaskEnabled);
        _hiddenCheck = Check("Hidden task", item.Hidden);
        _startWhenAvailableCheck = Check("Run task as soon as possible after a missed start", item.StartWhenAvailable);
        _networkCheck = Check("Start only if a network connection is available", item.RunOnlyIfNetworkAvailable);
        _disallowBatteryCheck = Check("Do not start if running on batteries", item.DisallowStartIfOnBatteries);
        _stopOnBatteryCheck = Check("Stop if computer switches to battery power", item.StopIfGoingOnBatteries);
        _wakeCheck = Check("Wake the computer to run this task", item.WakeToRun);
        _allowDemandCheck = Check("Allow task to be run on demand", item.AllowStartOnDemand);

        var checks = new StackPanel();
        checks.Children.Add(_taskEnabledCheck);
        checks.Children.Add(_hiddenCheck);
        checks.Children.Add(_startWhenAvailableCheck);
        checks.Children.Add(_networkCheck);
        checks.Children.Add(_disallowBatteryCheck);
        checks.Children.Add(_stopOnBatteryCheck);
        checks.Children.Add(_wakeCheck);
        checks.Children.Add(_allowDemandCheck);

        AddRow(settingsGrid, 0, "Multiple instances:", _instancesCombo);
        AddRow(settingsGrid, 1, "Execution time limit:", _executionLimitBox);
        AddRow(settingsGrid, 2, "Priority:", _priorityBox);
        AddRow(settingsGrid, 3, "Task settings:", checks);

        tabs.Items.Add(new TabItem
        {
            Header = "Settings",
            Content = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = settingsGrid
            }
        });

        var commonPanel = new StackPanel { Margin = new Thickness(14) };
        _disabledCheck = Check("Disable this preference item", item.Disabled);
        _bypassErrorsCheck = Check("Continue processing if this preference item fails", item.BypassErrors);
        _removePolicyCheck = Check("Remove this item when it is no longer applied", item.RemoveWhenNoLongerApplied);
        _userContextCheck = Check("Run in logged-on user's security context", item.RunInUserContext);

        commonPanel.Children.Add(_disabledCheck);
        commonPanel.Children.Add(_bypassErrorsCheck);
        commonPanel.Children.Add(_removePolicyCheck);
        commonPanel.Children.Add(_userContextCheck);

        tabs.Items.Add(new TabItem
        {
            Header = "Common",
            Content = commonPanel
        });

        var targetingPanel = new DockPanel { Margin = new Thickness(10) };
        var targetingHelp = new TextBlock
        {
            Text = "Advanced item-level targeting XML. Existing targeting is preserved. Leave empty to remove item-level targeting.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(4, 4, 4, 8)
        };
        DockPanel.SetDock(targetingHelp, Dock.Top);
        targetingPanel.Children.Add(targetingHelp);

        _filtersBox = new TextBox
        {
            Text = item.FiltersXml,
            AcceptsReturn = true,
            AcceptsTab = true,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        targetingPanel.Children.Add(_filtersBox);

        tabs.Items.Add(new TabItem
        {
            Header = item.HasFilters ? "Item-level Targeting *" : "Item-level Targeting",
            Content = targetingPanel
        });

        if (!item.SupportsStructuredEditing)
        {
            save.IsEnabled = false;
            tabs.Items.Add(new TabItem
            {
                Header = "Legacy",
                Content = new TextBlock
                {
                    Margin = new Thickness(14),
                    TextWrapping = TextWrapping.Wrap,
                    Text = "This is a legacy Task/ImmediateTask preference. It is displayed here, but structured rewriting is disabled. Use Show raw XML from the main window to make a controlled raw edit."
                }
            });
        }

        root.Children.Add(footer);
        root.Children.Add(header);
        root.Children.Add(tabs);
        Content = root;

        UpdateTriggerFields();
    }

    private void UpdateTriggerFields()
    {
        var type = Convert.ToString(_triggerTypeCombo.SelectedItem) ?? string.Empty;
        var immediate = type.Equals("Immediate", StringComparison.OrdinalIgnoreCase);
        var calendar = type is "Daily" or "Weekly" or "Once";

        _startBoundaryBox.IsEnabled = !immediate && calendar;
        _daysIntervalBox.IsEnabled = type == "Daily";
        _weeksIntervalBox.IsEnabled = type == "Weekly";
        _daysOfWeekBox.IsEnabled = type == "Weekly";
        _triggerDelayBox.IsEnabled = type is "AtStartup" or "AtLogon";
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_nameBox.Text))
        {
            MessageBox.Show(this, "Task name cannot be empty.", "Scheduled Task",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            _nameBox.Focus();
            return;
        }

        var trigger = Convert.ToString(_triggerTypeCombo.SelectedItem) ?? string.Empty;

        if (trigger == "Daily" &&
            (!uint.TryParse(_daysIntervalBox.Text.Trim(), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out var days) || days < 1))
        {
            MessageBox.Show(this, "Days interval must be a positive integer.", "Scheduled Task",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            _daysIntervalBox.Focus();
            return;
        }

        if (trigger == "Weekly" &&
            (!uint.TryParse(_weeksIntervalBox.Text.Trim(), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out var weeks) || weeks < 1))
        {
            MessageBox.Show(this, "Weeks interval must be a positive integer.", "Scheduled Task",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            _weeksIntervalBox.Focus();
            return;
        }

        if (!string.IsNullOrWhiteSpace(_startBoundaryBox.Text) &&
            trigger is "Daily" or "Weekly" or "Once" &&
            !DateTime.TryParse(_startBoundaryBox.Text.Trim(), CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces, out _))
        {
            MessageBox.Show(this,
                "Start boundary must be a valid date/time, for example 2026-10-06T18:30:00.",
                "Scheduled Task", MessageBoxButton.OK, MessageBoxImage.Warning);
            _startBoundaryBox.Focus();
            return;
        }

        if (!string.IsNullOrWhiteSpace(_filtersBox.Text))
        {
            try
            {
                var root = XElement.Parse(_filtersBox.Text);
                if (!root.Name.LocalName.Equals("Filters", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("The root element must be <Filters>.");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this,
                    $"Item-level targeting XML is invalid:\n\n{ex.Message}",
                    "Scheduled Task", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }

        _item.DisplayName = _nameBox.Text.Trim();
        _item.Description = _descriptionBox.Text.Trim();
        _item.Action = Convert.ToString(_actionCombo.SelectedItem) ?? "U";
        _item.RunAs = _runAsBox.Text.Trim();
        _item.LogonType = Convert.ToString(_logonTypeCombo.SelectedItem) ?? "InteractiveToken";
        _item.RunLevel = Convert.ToString(_runLevelCombo.SelectedItem) ?? "LeastPrivilege";
        _item.Author = _authorBox.Text.Trim();

        _item.Command = _commandBox.Text.Trim();
        _item.Arguments = _argumentsBox.Text;
        _item.WorkingDirectory = _workingDirectoryBox.Text.Trim();

        _item.TriggerType = trigger;
        _item.StartBoundary = _startBoundaryBox.Text.Trim();
        _item.DaysInterval = _daysIntervalBox.Text.Trim();
        _item.WeeksInterval = _weeksIntervalBox.Text.Trim();
        _item.DaysOfWeek = _daysOfWeekBox.Text.Trim();
        _item.TriggerDelay = _triggerDelayBox.Text.Trim();

        _item.TaskEnabled = _taskEnabledCheck.IsChecked == true;
        _item.Hidden = _hiddenCheck.IsChecked == true;
        _item.StartWhenAvailable = _startWhenAvailableCheck.IsChecked == true;
        _item.RunOnlyIfNetworkAvailable = _networkCheck.IsChecked == true;
        _item.DisallowStartIfOnBatteries = _disallowBatteryCheck.IsChecked == true;
        _item.StopIfGoingOnBatteries = _stopOnBatteryCheck.IsChecked == true;
        _item.WakeToRun = _wakeCheck.IsChecked == true;
        _item.AllowStartOnDemand = _allowDemandCheck.IsChecked == true;
        _item.MultipleInstancesPolicy =
            Convert.ToString(_instancesCombo.SelectedItem) ?? "IgnoreNew";
        _item.ExecutionTimeLimit = _executionLimitBox.Text.Trim();
        _item.Priority = _priorityBox.Text.Trim();

        _item.Disabled = _disabledCheck.IsChecked == true;
        _item.BypassErrors = _bypassErrorsCheck.IsChecked == true;
        _item.RemoveWhenNoLongerApplied = _removePolicyCheck.IsChecked == true;
        _item.RunInUserContext = _userContextCheck.IsChecked == true;
        _item.ClearStoredCredential = _clearCredentialCheck.IsChecked == true;
        _item.FiltersXml = _filtersBox.Text.Trim();

        DialogResult = true;
    }

    private static Grid CreateGrid(int rows)
    {
        var grid = new Grid { Margin = new Thickness(14) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(210) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        for (var i = 0; i < rows; i++)
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        return grid;
    }

    private static void AddRow(Grid grid, int row, string label, FrameworkElement editor)
    {
        var caption = new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(4, 9, 8, 8),
            TextWrapping = TextWrapping.Wrap
        };

        editor.Margin = new Thickness(4, 6, 4, 6);
        Grid.SetRow(caption, row);
        Grid.SetColumn(caption, 0);
        Grid.SetRow(editor, row);
        Grid.SetColumn(editor, 1);
        grid.Children.Add(caption);
        grid.Children.Add(editor);
    }

    private static ComboBox Combo(IEnumerable<string> values, string selected)
    {
        var combo = new ComboBox
        {
            ItemsSource = values.ToArray()
        };
        combo.SelectedItem = combo.Items.Cast<string>()
            .FirstOrDefault(value => value.Equals(selected, StringComparison.OrdinalIgnoreCase))
            ?? combo.Items.Cast<string>().FirstOrDefault();
        return combo;
    }

    private static CheckBox Check(string text, bool value) =>
        new()
        {
            Content = text,
            IsChecked = value,
            Margin = new Thickness(4, 5, 4, 5)
        };

    private static string NormalizeAction(string value) =>
        value.ToUpperInvariant() switch
        {
            "C" => "C",
            "R" => "R",
            "D" => "D",
            _ => "U"
        };

    private static string FirstNonEmpty(params string[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
}

using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Xml.Linq;
using GPOSettingsExplorer.Models;

namespace GPOSettingsExplorer;

public sealed class GppServiceEditorWindow : Window
{
    private readonly GppServiceItemInfo _item;

    private readonly TextBox _serviceNameBox;
    private readonly ComboBox _serviceActionCombo;
    private readonly ComboBox _startupTypeCombo;
    private readonly TextBox _timeoutBox;
    private readonly TextBox _accountNameBox;
    private readonly CheckBox _interactCheck;

    private readonly ComboBox _firstFailureCombo;
    private readonly ComboBox _secondFailureCombo;
    private readonly ComboBox _thirdFailureCombo;
    private readonly TextBox _resetFailCountBox;
    private readonly TextBox _restartServiceDelayBox;
    private readonly TextBox _restartComputerDelayBox;
    private readonly TextBox _restartMessageBox;
    private readonly TextBox _programBox;
    private readonly TextBox _argumentsBox;
    private readonly TextBox _appendArgumentsBox;

    private readonly CheckBox _disabledCheck;
    private readonly CheckBox _bypassErrorsCheck;
    private readonly CheckBox _removePolicyCheck;
    private readonly CheckBox _userContextCheck;
    private readonly CheckBox _clearCredentialCheck;
    private readonly TextBox _filtersBox;

    public GppServiceItemInfo Item => _item;

    public GppServiceEditorWindow(GppServiceItemInfo item)
    {
        _item = item;

        Title = "Services Preference Editor";
        Width = 1000;
        Height = 790;
        MinWidth = 800;
        MinHeight = 600;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var root = new DockPanel { Margin = new Thickness(12) };

        var footer = new WrapPanel
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
            Text = $"GPO: {item.GpoName}   |   Scope: Computer Configuration",
            FontWeight = FontWeights.SemiBold
        };

        var tabs = new TabControl();

        var serviceGrid = CreateGrid(6);

        _serviceNameBox = new TextBox { Text = item.ServiceName };

        _serviceActionCombo = new ComboBox
        {
            ItemsSource = new[]
            {
                "NOCHANGE",
                "START",
                "STOP",
                "RESTART",
                "RESTART_IF_REQUIRED"
            },
            SelectedItem = NormalizeServiceAction(item.ServiceAction)
        };

        _startupTypeCombo = new ComboBox
        {
            ItemsSource = new[]
            {
                "NOCHANGE",
                "AUTOMATIC",
                "BOOT",
                "DISABLED",
                "MANUAL",
                "SYSTEM"
            },
            SelectedItem = NormalizeStartupType(item.StartupType)
        };

        _timeoutBox = new TextBox { Text = item.Timeout };
        _accountNameBox = new TextBox { Text = item.AccountName };

        _interactCheck = new CheckBox
        {
            Content = "Allow service to interact with the desktop",
            IsChecked = item.InteractWithDesktop
        };

        AddRow(serviceGrid, 0, "Service name:", _serviceNameBox);
        AddRow(serviceGrid, 1, "Service action:", _serviceActionCombo);
        AddRow(serviceGrid, 2, "Startup type:", _startupTypeCombo);
        AddRow(serviceGrid, 3, "Timeout (seconds):", _timeoutBox);
        AddRow(serviceGrid, 4, "Log on as:", _accountNameBox);
        AddRow(serviceGrid, 5, "Interaction:", _interactCheck);

        var servicePanel = new StackPanel();
        servicePanel.Children.Add(serviceGrid);

        var credentialBox = new GroupBox
        {
            Header = "Stored credential",
            Margin = new Thickness(14, 8, 14, 8)
        };

        var credentialPanel = new StackPanel { Margin = new Thickness(8) };

        credentialPanel.Children.Add(new TextBlock
        {
            Text = item.HasStoredCredential
                ? "This existing Services preference contains a legacy encrypted cPassword. It is preserved opaquely and is never displayed."
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
        servicePanel.Children.Add(credentialBox);

        tabs.Items.Add(new TabItem
        {
            Header = "Service",
            Content = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = servicePanel
            }
        });

        var recoveryGrid = CreateGrid(10);

        var failureActions = new[]
        {
            "NOACTION",
            "START",
            "STOP",
            "RESTART",
            "RESTART_IF_REQUIRED"
        };

        _firstFailureCombo = Combo(failureActions, NormalizeFailure(item.FirstFailure));
        _secondFailureCombo = Combo(failureActions, NormalizeFailure(item.SecondFailure));
        _thirdFailureCombo = Combo(failureActions, NormalizeFailure(item.ThirdFailure));

        _resetFailCountBox = new TextBox { Text = item.ResetFailCountDelay };
        _restartServiceDelayBox = new TextBox { Text = item.RestartServiceDelay };
        _restartComputerDelayBox = new TextBox { Text = item.RestartComputerDelay };
        _restartMessageBox = new TextBox
        {
            Text = item.RestartMessage,
            AcceptsReturn = true,
            MinHeight = 60
        };
        _programBox = new TextBox { Text = item.Program };
        _argumentsBox = new TextBox { Text = item.Arguments };
        _appendArgumentsBox = new TextBox { Text = item.AppendArguments };

        AddRow(recoveryGrid, 0, "First failure:", _firstFailureCombo);
        AddRow(recoveryGrid, 1, "Second failure:", _secondFailureCombo);
        AddRow(recoveryGrid, 2, "Third failure:", _thirdFailureCombo);
        AddRow(recoveryGrid, 3, "Reset count delay:", _resetFailCountBox);
        AddRow(recoveryGrid, 4, "Restart service delay:", _restartServiceDelayBox);
        AddRow(recoveryGrid, 5, "Restart computer delay:", _restartComputerDelayBox);
        AddRow(recoveryGrid, 6, "Restart message:", _restartMessageBox);
        AddRow(recoveryGrid, 7, "Run program:", _programBox);
        AddRow(recoveryGrid, 8, "Program arguments:", _argumentsBox);
        AddRow(recoveryGrid, 9, "Append arguments:", _appendArgumentsBox);

        tabs.Items.Add(new TabItem
        {
            Header = "Recovery",
            Content = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = recoveryGrid
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
            FontFamily = UiStyle.MonospaceFontFamily,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };

        targetingPanel.Children.Add(_filtersBox);

        tabs.Items.Add(new TabItem
        {
            Header = item.HasFilters ? "Item-level Targeting *" : "Item-level Targeting",
            Content = targetingPanel
        });

        root.Children.Add(footer);
        root.Children.Add(header);
        root.Children.Add(tabs);
        Content = root;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_serviceNameBox.Text))
        {
            MessageBox.Show(
                this,
                "Service name cannot be empty.",
                "Services Preference",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            _serviceNameBox.Focus();
            return;
        }

        foreach (var pair in new[]
        {
            ("Timeout", _timeoutBox),
            ("Reset fail count delay", _resetFailCountBox),
            ("Restart service delay", _restartServiceDelayBox),
            ("Restart computer delay", _restartComputerDelayBox)
        })
        {
            if (!uint.TryParse(
                    pair.Item2.Text.Trim(),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out _))
            {
                MessageBox.Show(
                    this,
                    $"{pair.Item1} must be a non-negative integer.",
                    "Services Preference",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                pair.Item2.Focus();
                return;
            }
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
                MessageBox.Show(
                    this,
                    $"Item-level targeting XML is invalid:\n\n{ex.Message}",
                    "Services Preference",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }
        }

        _item.ServiceName = _serviceNameBox.Text.Trim();
        _item.DisplayName = _item.ServiceName;
        _item.ServiceAction =
            Convert.ToString(_serviceActionCombo.SelectedItem) ?? "NOCHANGE";
        _item.StartupType =
            Convert.ToString(_startupTypeCombo.SelectedItem) ?? "NOCHANGE";
        _item.Timeout = _timeoutBox.Text.Trim();
        _item.AccountName = _accountNameBox.Text.Trim();
        _item.InteractWithDesktop = _interactCheck.IsChecked == true;

        _item.FirstFailure =
            Convert.ToString(_firstFailureCombo.SelectedItem) ?? "NOACTION";
        _item.SecondFailure =
            Convert.ToString(_secondFailureCombo.SelectedItem) ?? "NOACTION";
        _item.ThirdFailure =
            Convert.ToString(_thirdFailureCombo.SelectedItem) ?? "NOACTION";
        _item.ResetFailCountDelay = _resetFailCountBox.Text.Trim();
        _item.RestartServiceDelay = _restartServiceDelayBox.Text.Trim();
        _item.RestartComputerDelay = _restartComputerDelayBox.Text.Trim();
        _item.RestartMessage = _restartMessageBox.Text;
        _item.Program = _programBox.Text.Trim();
        _item.Arguments = _argumentsBox.Text;
        _item.AppendArguments = _appendArgumentsBox.Text;

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
        var grid = new Grid { Margin = new Thickness(10) };

        for (var i = 0; i < rows; i++)
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        return grid;
    }

    private static ComboBox Combo(
        IEnumerable<string> values,
        string selected) =>
        new()
        {
            ItemsSource = values.ToArray(),
            SelectedItem = selected
        };

    private static CheckBox Check(string text, bool value) =>
        new()
        {
            Content = text,
            IsChecked = value,
            Margin = new Thickness(4, 6, 4, 6)
        };

    private static void AddRow(
        Grid grid,
        int row,
        string label,
        UIElement editor)
    {
        var text = new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 8, 10, 4)
        };

        if (editor is FrameworkElement element)
            element.Margin = new Thickness(4);

        Grid.SetRow(text, row);
        Grid.SetColumn(text, 0);
        Grid.SetRow(editor, row);
        Grid.SetColumn(editor, 1);

        grid.Children.Add(text);
        grid.Children.Add(editor);
    }

    private static string NormalizeServiceAction(string value) =>
        value.Trim().ToUpperInvariant() switch
        {
            "START" => "START",
            "STOP" => "STOP",
            "RESTART" => "RESTART",
            "RESTART_IF_REQUIRED" => "RESTART_IF_REQUIRED",
            _ => "NOCHANGE"
        };

    private static string NormalizeStartupType(string value) =>
        value.Trim().ToUpperInvariant() switch
        {
            "AUTOMATIC" => "AUTOMATIC",
            "BOOT" => "BOOT",
            "DISABLED" => "DISABLED",
            "MANUAL" => "MANUAL",
            "SYSTEM" => "SYSTEM",
            _ => "NOCHANGE"
        };

    private static string NormalizeFailure(string value) =>
        value.Trim().ToUpperInvariant() switch
        {
            "START" => "START",
            "STOP" => "STOP",
            "RESTART" => "RESTART",
            "RESTART_IF_REQUIRED" => "RESTART_IF_REQUIRED",
            _ => "NOACTION"
        };
}

using System.Windows;
using System.Windows.Controls;
using GPOSettingsExplorer.Models;

namespace GPOSettingsExplorer;

public sealed class GppRegistryEditorWindow : Window
{
    private readonly GppRegistryItemInfo _item;

    private readonly TextBox _displayNameBox;
    private readonly ComboBox _actionCombo;
    private readonly ComboBox _hiveCombo;
    private readonly TextBox _keyBox;
    private readonly TextBox _valueNameBox;
    private readonly ComboBox _typeCombo;
    private readonly TextBox _valueBox;
    private readonly TextBox _descriptionBox;
    private readonly CheckBox _defaultValueCheck;
    private readonly CheckBox _displayDecimalCheck;
    private readonly CheckBox _disabledCheck;
    private readonly CheckBox _bypassErrorsCheck;
    private readonly CheckBox _removePolicyCheck;
    private readonly CheckBox _userContextCheck;
    private readonly TextBox _filtersBox;

    public GppRegistryItemInfo Item => _item;

    public GppRegistryEditorWindow(GppRegistryItemInfo item)
    {
        _item = item;

        Title = "Registry Preference Editor";
        Width = 980;
        Height = 760;
        MinWidth = 780;
        MinHeight = 580;
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

        var header = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(4, 0, 4, 8)
        };
        DockPanel.SetDock(header, Dock.Top);

        header.Children.Add(new TextBlock
        {
            Text = $"GPO: {item.GpoName}",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 20, 0)
        });
        header.Children.Add(new TextBlock
        {
            Text = $"Scope: {item.Scope}",
            FontWeight = FontWeights.SemiBold
        });

        var tabs = new TabControl();

        var basicGrid = new Grid { Margin = new Thickness(10) };
        for (var i = 0; i < 9; i++)
            basicGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        basicGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
        basicGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        _displayNameBox = new TextBox { Text = item.DisplayName };
        _actionCombo = new ComboBox
        {
            ItemsSource = new[] { "Create", "Update", "Replace", "Delete" },
            SelectedItem = ActionDisplay(item.Action)
        };

        _hiveCombo = new ComboBox
        {
            ItemsSource = new[]
            {
                "HKEY_CLASSES_ROOT",
                "HKEY_CURRENT_USER",
                "HKEY_LOCAL_MACHINE",
                "HKEY_USERS",
                "HKEY_CURRENT_CONFIG"
            },
            SelectedItem = string.IsNullOrWhiteSpace(item.Hive)
                ? "HKEY_LOCAL_MACHINE"
                : item.Hive
        };

        _keyBox = new TextBox { Text = item.Key };
        _valueNameBox = new TextBox { Text = item.ValueName };

        _typeCombo = new ComboBox
        {
            ItemsSource = new[]
            {
                "<Key only>",
                "REG_SZ",
                "REG_DWORD",
                "REG_EXPAND_SZ",
                "REG_MULTI_SZ"
            },
            SelectedItem = string.IsNullOrWhiteSpace(item.ValueType)
                ? "<Key only>"
                : item.ValueType
        };
        _typeCombo.SelectionChanged += (_, _) => UpdateValueEditorHints();

        _valueBox = new TextBox
        {
            Text = item.ValueData,
            AcceptsReturn = true,
            MinHeight = 115,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };

        _descriptionBox = new TextBox
        {
            Text = item.Description,
            AcceptsReturn = true,
            MinHeight = 70
        };

        _defaultValueCheck = new CheckBox
        {
            Content = "Configure the default value for the key",
            IsChecked = item.DefaultValue
        };
        _defaultValueCheck.Checked += (_, _) => UpdateValueNameState();
        _defaultValueCheck.Unchecked += (_, _) => UpdateValueNameState();

        _displayDecimalCheck = new CheckBox
        {
            Content = "Display DWORD value as decimal",
            IsChecked = item.DisplayDecimal
        };

        AddRow(basicGrid, 0, "Display name:", _displayNameBox);
        AddRow(basicGrid, 1, "Action:", _actionCombo);
        AddRow(basicGrid, 2, "Hive:", _hiveCombo);
        AddRow(basicGrid, 3, "Key:", _keyBox);
        AddRow(basicGrid, 4, "Value name:", _valueNameBox);
        AddRow(basicGrid, 5, "Value type:", _typeCombo);
        AddRow(basicGrid, 6, "Value:", _valueBox);
        AddRow(basicGrid, 7, "Description:", _descriptionBox);

        var valueOptions = new WrapPanel { Orientation = Orientation.Horizontal };
        valueOptions.Children.Add(_defaultValueCheck);
        valueOptions.Children.Add(_displayDecimalCheck);
        AddRow(basicGrid, 8, "Value options:", valueOptions);

        var basicTab = new TabItem
        {
            Header = "Registry Setting",
            Content = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = basicGrid
            }
        };

        var commonPanel = new StackPanel { Margin = new Thickness(14) };

        _disabledCheck = new CheckBox
        {
            Content = "Disable this preference item",
            IsChecked = item.Disabled,
            Margin = new Thickness(4, 6, 4, 6)
        };

        _bypassErrorsCheck = new CheckBox
        {
            Content = "Continue processing if this preference item fails",
            IsChecked = item.BypassErrors,
            Margin = new Thickness(4, 6, 4, 6)
        };

        _removePolicyCheck = new CheckBox
        {
            Content = "Remove this item when it is no longer applied",
            IsChecked = item.RemoveWhenNoLongerApplied,
            Margin = new Thickness(4, 6, 4, 6)
        };

        _userContextCheck = new CheckBox
        {
            Content = "Run in logged-on user's security context",
            IsChecked = item.RunInUserContext,
            Margin = new Thickness(4, 6, 4, 6)
        };

        commonPanel.Children.Add(_disabledCheck);
        commonPanel.Children.Add(_bypassErrorsCheck);
        commonPanel.Children.Add(_removePolicyCheck);
        commonPanel.Children.Add(_userContextCheck);
        commonPanel.Children.Add(new TextBlock
        {
            Text = "These options map to the standard common Group Policy Preferences XML attributes. Existing attributes that are not edited here are preserved.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = System.Windows.Media.Brushes.DimGray,
            Margin = new Thickness(4, 18, 4, 4)
        });

        var commonTab = new TabItem
        {
            Header = "Common",
            Content = commonPanel
        };

        var targetingPanel = new DockPanel { Margin = new Thickness(10) };
        var targetingHelp = new TextBlock
        {
            Text = "Advanced item-level targeting XML. Leave empty for no targeting. Existing targeting is preserved unless you edit it here.",
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

        var targetingTab = new TabItem
        {
            Header = item.HasFilters ? "Item-level Targeting *" : "Item-level Targeting",
            Content = targetingPanel
        };

        tabs.Items.Add(basicTab);
        tabs.Items.Add(commonTab);
        tabs.Items.Add(targetingTab);

        root.Children.Add(footer);
        root.Children.Add(header);
        root.Children.Add(tabs);
        Content = root;

        UpdateValueNameState();
        UpdateValueEditorHints();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var type = Convert.ToString(_typeCombo.SelectedItem) ?? "REG_SZ";
        if (type == "<Key only>")
            type = string.Empty;

        if (string.IsNullOrWhiteSpace(_keyBox.Text))
        {
            MessageBox.Show(
                this,
                "Registry key cannot be empty.",
                "Registry Preference",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            _keyBox.Focus();
            return;
        }

        if (!string.IsNullOrWhiteSpace(type) &&
            !_defaultValueCheck.IsChecked.GetValueOrDefault() &&
            string.IsNullOrWhiteSpace(_valueNameBox.Text))
        {
            if (MessageBox.Show(
                    this,
                    "Value name is empty. Configure the default registry value instead?",
                    "Registry Preference",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                _defaultValueCheck.IsChecked = true;
            }
        }

        if (type == "REG_DWORD")
        {
            var text = _valueBox.Text.Trim();
            var valid = text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                ? uint.TryParse(
                    text[2..],
                    System.Globalization.NumberStyles.HexNumber,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out _)
                : uint.TryParse(
                    text,
                    System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out _);

            if (!valid)
            {
                MessageBox.Show(
                    this,
                    "REG_DWORD must be a decimal number or hexadecimal value prefixed with 0x.",
                    "Registry Preference",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                _valueBox.Focus();
                return;
            }
        }

        if (!string.IsNullOrWhiteSpace(_filtersBox.Text))
        {
            try
            {
                var root = System.Xml.Linq.XElement.Parse(_filtersBox.Text);
                if (!root.Name.LocalName.Equals("Filters", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("The root element must be <Filters>.");
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    this,
                    $"Item-level targeting XML is invalid:\n\n{ex.Message}",
                    "Registry Preference",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }
        }

        _item.DisplayName = _displayNameBox.Text.Trim();
        _item.Action = ActionCode(Convert.ToString(_actionCombo.SelectedItem));
        _item.Hive = Convert.ToString(_hiveCombo.SelectedItem) ?? "HKEY_LOCAL_MACHINE";
        _item.Key = _keyBox.Text.Trim().Trim('\\');
        _item.DefaultValue = _defaultValueCheck.IsChecked == true;
        _item.ValueName = _item.DefaultValue ? string.Empty : _valueNameBox.Text.Trim();
        _item.ValueType = type;
        _item.ValueData = string.IsNullOrWhiteSpace(type) ? string.Empty : _valueBox.Text;
        _item.Description = _descriptionBox.Text;
        _item.DisplayDecimal = _displayDecimalCheck.IsChecked == true;
        _item.Disabled = _disabledCheck.IsChecked == true;
        _item.BypassErrors = _bypassErrorsCheck.IsChecked == true;
        _item.RemoveWhenNoLongerApplied = _removePolicyCheck.IsChecked == true;
        _item.RunInUserContext = _userContextCheck.IsChecked == true;
        _item.FiltersXml = _filtersBox.Text.Trim();

        DialogResult = true;
    }

    private void UpdateValueNameState()
    {
        if (_valueNameBox is null)
            return;

        _valueNameBox.IsEnabled = _defaultValueCheck.IsChecked != true;
        if (_defaultValueCheck.IsChecked == true)
            _valueNameBox.ToolTip = "The default registry value will be configured.";
        else
            _valueNameBox.ToolTip = null;
    }

    private void UpdateValueEditorHints()
    {
        if (_typeCombo is null || _valueBox is null || _displayDecimalCheck is null)
            return;

        var type = Convert.ToString(_typeCombo.SelectedItem) ?? "REG_SZ";

        _valueBox.IsEnabled = type != "<Key only>";
        _displayDecimalCheck.IsEnabled = type == "REG_DWORD";

        _valueBox.ToolTip = type switch
        {
            "REG_DWORD" => "Enter a decimal value or hexadecimal value prefixed with 0x.",
            "REG_MULTI_SZ" => "Enter one string per line.",
            "REG_EXPAND_SZ" => "Environment variables such as %SystemRoot% are preserved.",
            "<Key only>" => "No value data is needed when only creating or deleting a key.",
            _ => null
        };
    }

    private static void AddRow(Grid grid, int row, string label, UIElement editor)
    {
        var text = new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(4, 8, 10, 4)
        };

        if (editor is FrameworkElement element)
            element.Margin = new Thickness(4, 4, 4, 4);

        Grid.SetRow(text, row);
        Grid.SetColumn(text, 0);
        Grid.SetRow(editor, row);
        Grid.SetColumn(editor, 1);

        grid.Children.Add(text);
        grid.Children.Add(editor);
    }

    private static string ActionDisplay(string action) =>
        action.Trim().ToUpperInvariant() switch
        {
            "C" => "Create",
            "D" => "Delete",
            "R" => "Replace",
            _ => "Update"
        };

    private static string ActionCode(string? action) =>
        action?.Trim().ToUpperInvariant() switch
        {
            "CREATE" => "C",
            "DELETE" => "D",
            "REPLACE" => "R",
            _ => "U"
        };
}

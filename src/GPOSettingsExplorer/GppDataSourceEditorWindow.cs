using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Xml.Linq;
using GPOSettingsExplorer.Models;

namespace GPOSettingsExplorer;

public sealed class GppDataSourceEditorWindow : Window
{
    private readonly GppDataSourceItemInfo _item;

    private readonly TextBox _displayNameBox;
    private readonly TextBox _descriptionBox;
    private readonly ComboBox _actionCombo;
    private readonly CheckBox _userDsnCheck;
    private readonly TextBox _dsnBox;
    private readonly TextBox _driverBox;
    private readonly TextBox _dsnDescriptionBox;
    private readonly TextBox _userNameBox;
    private readonly DataGrid _attributesGrid;

    private readonly CheckBox _disabledCheck;
    private readonly CheckBox _bypassErrorsCheck;
    private readonly CheckBox _removePolicyCheck;
    private readonly CheckBox _userContextCheck;
    private readonly CheckBox _clearCredentialCheck;
    private readonly TextBox _filtersBox;

    public GppDataSourceItemInfo Item => _item;

    public GppDataSourceEditorWindow(GppDataSourceItemInfo item)
    {
        _item = item;

        Title = "Data Source Preference Editor";
        Width = 980;
        Height = 760;
        MinWidth = 780;
        MinHeight = 580;
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
            BorderBrush = System.Windows.SystemColors.ControlDarkBrush,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(6),
            Margin = new Thickness(0, 0, 0, 8)
        };
        DockPanel.SetDock(header, Dock.Top);

        header.Child = new TextBlock
        {
            Text = $"GPO: {item.GpoName}   |   Scope: {item.Scope} Configuration",
            FontWeight = FontWeights.SemiBold
        };

        var tabs = new TabControl();

        var generalGrid = CreateGrid(8);

        _displayNameBox = new TextBox { Text = item.DisplayName };
        _descriptionBox = new TextBox
        {
            Text = item.Description,
            AcceptsReturn = true,
            MinHeight = 64
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
        _actionCombo.SelectedItem = _actionCombo.Items
            .Cast<Choice>()
            .FirstOrDefault(choice =>
                choice.Value.Equals(
                    NormalizeAction(item.Action),
                    StringComparison.OrdinalIgnoreCase))
            ?? _actionCombo.Items.Cast<Choice>().First();

        _userDsnCheck = new CheckBox
        {
            Content = "User DSN",
            IsChecked = item.UserDsn
        };

        _dsnBox = new TextBox { Text = item.Dsn };
        _driverBox = new TextBox { Text = item.Driver };
        _dsnDescriptionBox = new TextBox { Text = item.DsnDescription };
        _userNameBox = new TextBox { Text = item.UserName };

        AddRow(generalGrid, 0, "Display name:", _displayNameBox);
        AddRow(generalGrid, 1, "Description:", _descriptionBox);
        AddRow(generalGrid, 2, "Action:", _actionCombo);
        AddRow(generalGrid, 3, "DSN type:", _userDsnCheck);
        AddRow(generalGrid, 4, "DSN name:", _dsnBox);
        AddRow(generalGrid, 5, "ODBC driver:", _driverBox);
        AddRow(generalGrid, 6, "DSN description:", _dsnDescriptionBox);
        AddRow(generalGrid, 7, "User name:", _userNameBox);

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
                ? "This existing Data Source preference contains legacy encrypted cPassword data. It is preserved opaquely and is never displayed."
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

        var attributesPanel = new DockPanel { Margin = new Thickness(10) };

        var attributesButtons = new StackPanel
        {
            Orientation = Orientation.Horizontal
        };
        DockPanel.SetDock(attributesButtons, Dock.Top);

        var addAttribute = new Button { Content = "Add attribute" };
        addAttribute.Click += (_, _) =>
        {
            var row = new GppDataSourceAttributeInfo();
            _item.Attributes.Add(row);
            _attributesGrid.SelectedItem = row;
            _attributesGrid.ScrollIntoView(row);
        };

        var removeAttribute = new Button { Content = "Remove selected" };
        removeAttribute.Click += (_, _) =>
        {
            if (_attributesGrid.SelectedItem is GppDataSourceAttributeInfo selected)
                _item.Attributes.Remove(selected);
        };

        attributesButtons.Children.Add(addAttribute);
        attributesButtons.Children.Add(removeAttribute);

        var attributesHelp = new TextBlock
        {
            Text = "Driver-specific ODBC attributes are stored as name/value pairs. Examples include Server, Database, DSN, Trusted_Connection, Port, or vendor-specific settings.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(4, 4, 4, 8)
        };
        DockPanel.SetDock(attributesHelp, Dock.Top);

        _attributesGrid = new DataGrid
        {
            ItemsSource = _item.Attributes,
            AutoGenerateColumns = false,
            IsReadOnly = false,
            CanUserAddRows = false,
            CanUserDeleteRows = false,
            SelectionMode = DataGridSelectionMode.Single
        };

        _attributesGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Attribute name",
            Binding = new System.Windows.Data.Binding(nameof(GppDataSourceAttributeInfo.Name))
            {
                UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged
            },
            Width = new DataGridLength(1, DataGridLengthUnitType.Star)
        });

        _attributesGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Value",
            Binding = new System.Windows.Data.Binding(nameof(GppDataSourceAttributeInfo.Value))
            {
                UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged
            },
            Width = new DataGridLength(2, DataGridLengthUnitType.Star)
        });

        attributesPanel.Children.Add(attributesButtons);
        attributesPanel.Children.Add(attributesHelp);
        attributesPanel.Children.Add(_attributesGrid);

        tabs.Items.Add(new TabItem
        {
            Header = "ODBC Attributes",
            Content = attributesPanel
        });

        var commonPanel = new StackPanel { Margin = new Thickness(14) };

        _disabledCheck = Check(
            "Disable this preference item",
            item.Disabled);

        _bypassErrorsCheck = Check(
            "Continue processing if this preference item fails",
            item.BypassErrors);

        _removePolicyCheck = Check(
            "Remove this item when it is no longer applied",
            item.RemoveWhenNoLongerApplied);

        _userContextCheck = Check(
            "Run in logged-on user's security context",
            item.RunInUserContext);

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
            Header = item.HasFilters
                ? "Item-level Targeting *"
                : "Item-level Targeting",
            Content = targetingPanel
        });

        root.Children.Add(footer);
        root.Children.Add(header);
        root.Children.Add(tabs);
        Content = root;
    }

    private void Save_Click(
        object sender,
        RoutedEventArgs e)
    {
        _attributesGrid.CommitEdit(
            DataGridEditingUnit.Cell,
            exitEditingMode: true);

        _attributesGrid.CommitEdit(
            DataGridEditingUnit.Row,
            exitEditingMode: true);

        if (string.IsNullOrWhiteSpace(_dsnBox.Text))
        {
            MessageBox.Show(
                this,
                "DSN name cannot be empty.",
                "Data Source Preference",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            _dsnBox.Focus();
            return;
        }

        var action = (_actionCombo.SelectedItem as Choice)?.Value ?? "U";

        if (!action.Equals("D", StringComparison.OrdinalIgnoreCase) &&
            string.IsNullOrWhiteSpace(_driverBox.Text))
        {
            MessageBox.Show(
                this,
                "ODBC driver name cannot be empty unless the action is Delete.",
                "Data Source Preference",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            _driverBox.Focus();
            return;
        }

        var duplicate = _item.Attributes
            .Where(attribute =>
                !string.IsNullOrWhiteSpace(attribute.Name))
            .GroupBy(
                attribute => attribute.Name.Trim(),
                StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);

        if (duplicate is not null)
        {
            MessageBox.Show(
                this,
                $"Duplicate ODBC attribute: {duplicate.Key}",
                "Data Source Preference",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (!string.IsNullOrWhiteSpace(_filtersBox.Text))
        {
            try
            {
                var root = XElement.Parse(_filtersBox.Text);

                if (!root.Name.LocalName.Equals(
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
                    "Data Source Preference",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }
        }

        _item.DisplayName =
            string.IsNullOrWhiteSpace(_displayNameBox.Text)
                ? _dsnBox.Text.Trim()
                : _displayNameBox.Text.Trim();

        _item.Description = _descriptionBox.Text.Trim();
        _item.Action = action;
        _item.UserDsn = _userDsnCheck.IsChecked == true;
        _item.Dsn = _dsnBox.Text.Trim();
        _item.Driver = _driverBox.Text.Trim();
        _item.DsnDescription = _dsnDescriptionBox.Text.Trim();
        _item.UserName = _userNameBox.Text.Trim();
        _item.Disabled = _disabledCheck.IsChecked == true;
        _item.BypassErrors = _bypassErrorsCheck.IsChecked == true;
        _item.RemoveWhenNoLongerApplied =
            _removePolicyCheck.IsChecked == true;
        _item.RunInUserContext =
            _userContextCheck.IsChecked == true;
        _item.ClearStoredCredential =
            _clearCredentialCheck.IsChecked == true;
        _item.FiltersXml = _filtersBox.Text.Trim();

        DialogResult = true;
    }

    private static Grid CreateGrid(int rows)
    {
        var grid = new Grid { Margin = new Thickness(14) };

        grid.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width = new GridLength(180)
            });

        grid.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width = new GridLength(
                    1,
                    GridUnitType.Star)
            });

        for (var index = 0; index < rows; index++)
        {
            grid.RowDefinitions.Add(
                new RowDefinition
                {
                    Height = GridLength.Auto
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
            VerticalAlignment = VerticalAlignment.Top,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(4, 9, 8, 8)
        };

        editor.Margin = new Thickness(4, 6, 4, 6);

        Grid.SetRow(caption, row);
        Grid.SetColumn(caption, 0);
        Grid.SetRow(editor, row);
        Grid.SetColumn(editor, 1);

        grid.Children.Add(caption);
        grid.Children.Add(editor);
    }

    private static CheckBox Check(
        string text,
        bool value) =>
        new()
        {
            Content = text,
            IsChecked = value,
            Margin = new Thickness(4, 5, 4, 5)
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

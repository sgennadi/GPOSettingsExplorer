using System.Windows;
using System.Windows.Controls;
using System.Xml.Linq;
using GPOSettingsExplorer.Models;

namespace GPOSettingsExplorer;

public sealed class GppEnvironmentEditorWindow : Window
{
    private readonly GppEnvironmentVariableInfo _item;

    private readonly TextBox _displayNameBox;
    private readonly TextBox _descriptionBox;
    private readonly ComboBox _actionCombo;
    private readonly TextBox _nameBox;
    private readonly TextBox _valueBox;
    private readonly ComboBox _variableTypeCombo;
    private readonly CheckBox _partialPathCheck;

    private readonly CheckBox _disabledCheck;
    private readonly CheckBox _bypassErrorsCheck;
    private readonly CheckBox _removePolicyCheck;
    private readonly CheckBox _userContextCheck;
    private readonly TextBox _filtersBox;

    public GppEnvironmentVariableInfo Item => _item;

    public GppEnvironmentEditorWindow(
        GppEnvironmentVariableInfo item)
    {
        _item = item;

        Title = "Environment Variable Preference Editor";
        Width = 950;
        Height = 720;
        MinWidth = 760;
        MinHeight = 580;
        WindowStartupLocation =
            WindowStartupLocation.CenterOwner;

        var root = new DockPanel
        {
            Margin = new Thickness(12)
        };

        var footer = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        DockPanel.SetDock(
            footer,
            Dock.Bottom);

        var cancel = new Button
        {
            Content = "Cancel",
            IsCancel = true
        };

        var save = new Button
        {
            Content = "Save",
            IsDefault = true
        };
        save.Click += Save_Click;

        footer.Children.Add(cancel);
        footer.Children.Add(save);

        var header = new Border
        {
            BorderBrush =
                System.Windows.Media.Brushes.LightGray,
            BorderThickness =
                new Thickness(0, 0, 0, 1),
            Padding = new Thickness(6),
            Margin = new Thickness(0, 0, 0, 8)
        };
        DockPanel.SetDock(
            header,
            Dock.Top);

        header.Child = new TextBlock
        {
            Text =
                $"GPO: {item.GpoName}   |   Scope: {item.Scope} Configuration",
            FontWeight =
                FontWeights.SemiBold
        };

        var tabs = new TabControl();

        var generalGrid = new Grid
        {
            Margin = new Thickness(10)
        };

        for (var i = 0; i < 8; i++)
        {
            generalGrid.RowDefinitions.Add(
                new RowDefinition
                {
                    Height = GridLength.Auto
                });
        }

        generalGrid.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width = new GridLength(190)
            });

        generalGrid.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width = new GridLength(
                    1,
                    GridUnitType.Star)
            });

        _displayNameBox = new TextBox
        {
            Text = item.DisplayName
        };

        _descriptionBox = new TextBox
        {
            Text = item.Description,
            AcceptsReturn = true,
            MinHeight = 62
        };

        _actionCombo = new ComboBox
        {
            ItemsSource =
                new[]
                {
                    "Create",
                    "Update",
                    "Replace",
                    "Delete"
                },
            SelectedItem =
                ActionDisplay(item.Action)
        };

        _nameBox = new TextBox
        {
            Text = item.Name,
            ToolTip =
                "Environment variable name, for example TEMP or PATH."
        };
        _nameBox.TextChanged += (_, _) =>
            UpdatePartialPathUi();

        _valueBox = new TextBox
        {
            Text = item.Value,
            AcceptsReturn = true,
            MinHeight = 100,
            VerticalScrollBarVisibility =
                ScrollBarVisibility.Auto
        };

        _variableTypeCombo = new ComboBox
        {
            ItemsSource =
                new[]
                {
                    "System variable",
                    "User variable"
                },
            SelectedItem =
                item.UserVariable
                    ? "User variable"
                    : "System variable"
        };
        _variableTypeCombo.SelectionChanged +=
            (_, _) => UpdatePartialPathUi();

        _partialPathCheck = new CheckBox
        {
            Content =
                "Add or remove a semicolon-delimited segment of the system PATH variable",
            IsChecked =
                item.PartialPath
        };

        AddRow(
            generalGrid,
            0,
            "Display name:",
            _displayNameBox);

        AddRow(
            generalGrid,
            1,
            "Description:",
            _descriptionBox);

        AddRow(
            generalGrid,
            2,
            "Action:",
            _actionCombo);

        AddRow(
            generalGrid,
            3,
            "Variable name:",
            _nameBox);

        AddRow(
            generalGrid,
            4,
            "Value:",
            _valueBox);

        AddRow(
            generalGrid,
            5,
            "Variable type:",
            _variableTypeCombo);

        AddRow(
            generalGrid,
            6,
            "PATH behavior:",
            _partialPathCheck);

        var note = new TextBlock
        {
            Text =
                "Partial PATH mode is valid only for the system PATH variable. " +
                "The XML is written in the native Group Policy Preferences EnvironmentVariables format.",
            TextWrapping =
                TextWrapping.Wrap,
            Foreground =
                System.Windows.Media.Brushes.DimGray,
            Margin =
                new Thickness(4, 12, 4, 4)
        };
        Grid.SetRow(
            note,
            7);
        Grid.SetColumnSpan(
            note,
            2);
        generalGrid.Children.Add(note);

        tabs.Items.Add(
            new TabItem
            {
                Header = "Environment Variable",
                Content =
                    new ScrollViewer
                    {
                        VerticalScrollBarVisibility =
                            ScrollBarVisibility.Auto,
                        Content =
                            generalGrid
                    }
            });

        var commonPanel = new StackPanel
        {
            Margin =
                new Thickness(14)
        };

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

        commonPanel.Children.Add(
            _disabledCheck);
        commonPanel.Children.Add(
            _bypassErrorsCheck);
        commonPanel.Children.Add(
            _removePolicyCheck);
        commonPanel.Children.Add(
            _userContextCheck);

        tabs.Items.Add(
            new TabItem
            {
                Header = "Common",
                Content =
                    commonPanel
            });

        var targetingPanel = new DockPanel
        {
            Margin =
                new Thickness(10)
        };

        var targetingHelp = new TextBlock
        {
            Text =
                "Advanced item-level targeting XML. Existing targeting is preserved. " +
                "Leave empty to remove item-level targeting.",
            TextWrapping =
                TextWrapping.Wrap,
            Margin =
                new Thickness(4, 4, 4, 8)
        };
        DockPanel.SetDock(
            targetingHelp,
            Dock.Top);
        targetingPanel.Children.Add(
            targetingHelp);

        _filtersBox = new TextBox
        {
            Text =
                item.FiltersXml,
            AcceptsReturn =
                true,
            AcceptsTab =
                true,
            FontFamily =
                UiStyle.MonospaceFontFamily,
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
                Header =
                    item.HasFilters
                        ? "Item-level Targeting *"
                        : "Item-level Targeting",
                Content =
                    targetingPanel
            });

        root.Children.Add(
            footer);
        root.Children.Add(
            header);
        root.Children.Add(
            tabs);

        Content = root;

        UpdatePartialPathUi();
    }

    private void Save_Click(
        object sender,
        RoutedEventArgs e)
    {
        var name =
            _nameBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(name))
        {
            Warn(
                "Environment variable name cannot be empty.",
                _nameBox);
            return;
        }

        if (name.Contains(
                '=',
                StringComparison.Ordinal))
        {
            Warn(
                "Environment variable name cannot contain '='.",
                _nameBox);
            return;
        }

        var userVariable =
            string.Equals(
                Convert.ToString(
                    _variableTypeCombo.SelectedItem),
                "User variable",
                StringComparison.OrdinalIgnoreCase);

        var partial =
            _partialPathCheck.IsChecked == true;

        if (partial &&
            (userVariable ||
             !name.Equals(
                 "PATH",
                 StringComparison.OrdinalIgnoreCase)))
        {
            Warn(
                "Partial PATH mode is available only for the system PATH variable.",
                _nameBox);
            return;
        }

        if (!ValidateFilters())
            return;

        _item.Name = name;
        _item.Value =
            _valueBox.Text;

        _item.DisplayName =
            string.IsNullOrWhiteSpace(
                _displayNameBox.Text)
                ? $"{name} = {_item.Value}"
                : _displayNameBox.Text.Trim();

        _item.Description =
            _descriptionBox.Text;

        _item.Action =
            ActionCode(
                Convert.ToString(
                    _actionCombo.SelectedItem));

        _item.UserVariable =
            userVariable;

        _item.PartialPath =
            partial;

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

    private void UpdatePartialPathUi()
    {
        if (_partialPathCheck is null ||
            _variableTypeCombo is null ||
            _nameBox is null)
            return;

        var userVariable =
            string.Equals(
                Convert.ToString(
                    _variableTypeCombo.SelectedItem),
                "User variable",
                StringComparison.OrdinalIgnoreCase);

        var canUsePartial =
            !userVariable &&
            _nameBox.Text.Trim().Equals(
                "PATH",
                StringComparison.OrdinalIgnoreCase);

        _partialPathCheck.IsEnabled =
            canUsePartial;

        if (!canUsePartial)
            _partialPathCheck.IsChecked = false;
    }

    private bool ValidateFilters()
    {
        if (string.IsNullOrWhiteSpace(
                _filtersBox.Text))
            return true;

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

            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                $"Item-level targeting XML is invalid:\n\n{ex.Message}",
                "Environment Variable",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return false;
        }
    }

    private void Warn(
        string message,
        Control control)
    {
        MessageBox.Show(
            this,
            message,
            "Environment Variable",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);

        control.Focus();
    }

    private static CheckBox Check(
        string text,
        bool value) =>
        new()
        {
            Content =
                text,
            IsChecked =
                value,
            Margin =
                new Thickness(4, 6, 10, 6)
        };

    private static void AddRow(
        Grid grid,
        int row,
        string label,
        UIElement editor)
    {
        var text =
            new TextBlock
            {
                Text =
                    label,
                VerticalAlignment =
                    VerticalAlignment.Center,
                Margin =
                    new Thickness(4, 8, 10, 4)
            };

        if (editor is FrameworkElement element)
        {
            element.Margin =
                new Thickness(4);
        }

        Grid.SetRow(
            text,
            row);
        Grid.SetColumn(
            text,
            0);
        Grid.SetRow(
            editor,
            row);
        Grid.SetColumn(
            editor,
            1);

        grid.Children.Add(
            text);
        grid.Children.Add(
            editor);
    }

    private static string ActionDisplay(
        string action) =>
        action.Trim()
            .ToUpperInvariant() switch
        {
            "C" => "Create",
            "D" => "Delete",
            "R" => "Replace",
            _ => "Update"
        };

    private static string ActionCode(
        string? action) =>
        action?.Trim()
            .ToUpperInvariant() switch
        {
            "CREATE" => "C",
            "DELETE" => "D",
            "REPLACE" => "R",
            _ => "U"
        };
}

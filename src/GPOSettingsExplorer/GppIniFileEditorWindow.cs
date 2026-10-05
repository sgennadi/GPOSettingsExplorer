using System.Windows;
using System.Windows.Controls;
using System.Xml.Linq;
using GPOSettingsExplorer.Models;

namespace GPOSettingsExplorer;

public sealed class GppIniFileEditorWindow : Window
{
    private readonly GppIniFileItemInfo _item;

    private readonly TextBox _displayNameBox;
    private readonly TextBox _descriptionBox;
    private readonly ComboBox _actionCombo;
    private readonly TextBox _pathBox;
    private readonly TextBox _sectionBox;
    private readonly TextBox _propertyBox;
    private readonly TextBox _valueBox;

    private readonly TextBlock _deleteMeaningText;

    private readonly CheckBox _disabledCheck;
    private readonly CheckBox _bypassErrorsCheck;
    private readonly CheckBox _removePolicyCheck;
    private readonly CheckBox _userContextCheck;
    private readonly TextBox _filtersBox;

    public GppIniFileItemInfo Item => _item;

    public GppIniFileEditorWindow(
        GppIniFileItemInfo item)
    {
        _item = item;

        Title = "INI File Preference Editor";
        Width = 950;
        Height = 710;
        MinWidth = 760;
        MinHeight = 560;
        WindowStartupLocation =
            WindowStartupLocation.CenterOwner;

        var root = new DockPanel
        {
            Margin = new Thickness(12)
        };

        var footer = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment =
                HorizontalAlignment.Right
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

        var generalGrid =
            CreateGrid(
                8,
                190);

        _displayNameBox =
            new TextBox
            {
                Text =
                    item.DisplayName
            };

        _descriptionBox =
            new TextBox
            {
                Text =
                    item.Description,
                AcceptsReturn =
                    true,
                Height =
                    56
            };

        _actionCombo =
            new ComboBox
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
                    ActionDisplay(
                        item.Action)
            };

        _actionCombo.SelectionChanged +=
            (_, _) =>
                UpdateDeleteMeaning();

        _pathBox =
            new TextBox
            {
                Text =
                    item.Path,
                ToolTip =
                    @"Full .ini or .inf path on the client, for example C:\ProgramData\App\settings.ini."
            };

        _sectionBox =
            new TextBox
            {
                Text =
                    item.Section
            };

        _propertyBox =
            new TextBox
            {
                Text =
                    item.Property
            };

        _valueBox =
            new TextBox
            {
                Text =
                    item.Value,
                AcceptsReturn =
                    true,
                Height =
                    80
            };

        _sectionBox.TextChanged +=
            (_, _) =>
                UpdateDeleteMeaning();

        _propertyBox.TextChanged +=
            (_, _) =>
                UpdateDeleteMeaning();

        _deleteMeaningText =
            new TextBlock
            {
                TextWrapping =
                    TextWrapping.Wrap,
                Foreground =
                    System.Windows.Media.Brushes.DimGray,
                Margin =
                    new Thickness(4, 10, 4, 4)
            };

        AddRow(
            generalGrid,
            0,
            "Preference name:",
            _displayNameBox);

        AddRow(
            generalGrid,
            1,
            "Item description:",
            _descriptionBox);

        AddRow(
            generalGrid,
            2,
            "Action:",
            _actionCombo);

        AddRow(
            generalGrid,
            3,
            "File path:",
            _pathBox);

        AddRow(
            generalGrid,
            4,
            "Section name:",
            _sectionBox);

        AddRow(
            generalGrid,
            5,
            "Property name:",
            _propertyBox);

        AddRow(
            generalGrid,
            6,
            "Property value:",
            _valueBox);

        Grid.SetRow(
            _deleteMeaningText,
            7);
        Grid.SetColumnSpan(
            _deleteMeaningText,
            2);
        generalGrid.Children.Add(
            _deleteMeaningText);

        tabs.Items.Add(
            new TabItem
            {
                Header = "INI / INF Entry",
                Content =
                    new ScrollViewer
                    {
                        VerticalScrollBarVisibility =
                            ScrollBarVisibility.Auto,
                        Content =
                            generalGrid
                    }
            });

        var commonPanel =
            new StackPanel
            {
                Margin =
                    new Thickness(14)
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

        var targeting =
            new DockPanel
            {
                Margin =
                    new Thickness(10)
            };

        var targetingNote =
            new TextBlock
            {
                Text =
                    "Advanced item-level targeting XML. Existing targeting is preserved. Leave empty to remove item-level targeting.",
                TextWrapping =
                    TextWrapping.Wrap,
                Margin =
                    new Thickness(4, 4, 4, 8)
            };

        DockPanel.SetDock(
            targetingNote,
            Dock.Top);

        _filtersBox =
            new TextBox
            {
                Text =
                    item.FiltersXml,
                AcceptsReturn =
                    true,
                AcceptsTab =
                    true,
                FontFamily =
                    new System.Windows.Media.FontFamily(
                        "Consolas"),
                HorizontalScrollBarVisibility =
                    ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility =
                    ScrollBarVisibility.Auto
            };

        targeting.Children.Add(
            targetingNote);
        targeting.Children.Add(
            _filtersBox);

        tabs.Items.Add(
            new TabItem
            {
                Header =
                    item.HasFilters
                        ? "Item-level Targeting *"
                        : "Item-level Targeting",
                Content =
                    targeting
            });

        root.Children.Add(
            footer);
        root.Children.Add(
            header);
        root.Children.Add(
            tabs);

        Content = root;

        UpdateDeleteMeaning();
    }

    private void Save_Click(
        object sender,
        RoutedEventArgs e)
    {
        var path =
            _pathBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(
                path))
        {
            Warn(
                "INI/INF file path cannot be empty.",
                _pathBox);
            return;
        }

        var action =
            ActionCode(
                Convert.ToString(
                    _actionCombo.SelectedItem));

        var section =
            _sectionBox.Text.Trim();

        var property =
            _propertyBox.Text.Trim();

        if (action != "D")
        {
            if (string.IsNullOrWhiteSpace(
                    section))
            {
                Warn(
                    "Section name is required for Create, Update, and Replace.",
                    _sectionBox);
                return;
            }

            if (string.IsNullOrWhiteSpace(
                    property))
            {
                Warn(
                    "Property name is required for Create, Update, and Replace.",
                    _propertyBox);
                return;
            }
        }

        if (!ValidateFilters())
            return;

        _item.DisplayName =
            string.IsNullOrWhiteSpace(
                _displayNameBox.Text)
                ? FirstNonEmpty(
                    property,
                    section,
                    path,
                    "INI File")
                : _displayNameBox.Text.Trim();

        _item.Description =
            _descriptionBox.Text;

        _item.Action =
            action;

        _item.Path =
            path;

        _item.Section =
            section;

        _item.Property =
            property;

        _item.Value =
            _valueBox.Text;

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

        DialogResult =
            true;
    }

    private void UpdateDeleteMeaning()
    {
        if (_deleteMeaningText is null ||
            _actionCombo is null ||
            _sectionBox is null ||
            _propertyBox is null)
            return;

        var action =
            ActionCode(
                Convert.ToString(
                    _actionCombo.SelectedItem));

        if (action != "D")
        {
            _deleteMeaningText.Text =
                "Create, Update, and Replace configure a property inside an INI/INF section. Section name and property name are required.";
            return;
        }

        var section =
            _sectionBox.Text.Trim();

        var property =
            _propertyBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(
                section))
        {
            _deleteMeaningText.Text =
                "Delete target: the entire INI/INF file (section and property are empty).";
        }
        else if (string.IsNullOrWhiteSpace(
                     property))
        {
            _deleteMeaningText.Text =
                $"Delete target: section [{section}] (property is empty).";
        }
        else
        {
            _deleteMeaningText.Text =
                $"Delete target: property '{property}' in section [{section}].";
        }
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
                "INI File",
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
            "INI File",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);

        control.Focus();
    }

    private static Grid CreateGrid(
        int rows,
        double labelWidth)
    {
        var grid =
            new Grid();

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

        grid.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width =
                    new GridLength(
                        labelWidth)
            });

        grid.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width =
                    new GridLength(
                        1,
                        GridUnitType.Star)
            });

        return grid;
    }

    private static void AddRow(
        Grid grid,
        int row,
        string label,
        UIElement editor)
    {
        var caption =
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
            Content =
                text,
            IsChecked =
                value,
            Margin =
                new Thickness(4, 6, 10, 6)
        };

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

    private static string FirstNonEmpty(
        params string[] values) =>
        values.FirstOrDefault(
            value =>
                !string.IsNullOrWhiteSpace(
                    value))
        ?? string.Empty;
}

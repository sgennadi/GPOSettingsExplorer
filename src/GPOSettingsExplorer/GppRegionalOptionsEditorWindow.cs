using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Xml.Linq;
using GPOSettingsExplorer.Models;

namespace GPOSettingsExplorer;

public sealed class GppRegionalOptionsEditorWindow : Window
{
    private readonly GppRegionalOptionsItemInfo _item;
    private readonly Dictionary<string, TextBox> _boxes =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly TextBox _displayNameBox;
    private readonly CheckBox _disabledCheck;
    private readonly CheckBox _bypassErrorsCheck;
    private readonly CheckBox _removePolicyCheck;
    private readonly CheckBox _userContextCheck;
    private readonly TextBox _filtersBox;

    public GppRegionalOptionsItemInfo Item => _item;

    public GppRegionalOptionsEditorWindow(
        GppRegionalOptionsItemInfo item)
    {
        _item = item;

        Title = "Regional Options Preference Editor";
        Width = 1020;
        Height = 800;
        MinWidth = 820;
        MinHeight = 620;
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
            BorderBrush = System.Windows.SystemColors.ControlDarkBrush,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(6),
            Margin = new Thickness(0, 0, 0, 8)
        };
        DockPanel.SetDock(header, Dock.Top);

        header.Child = new TextBlock
        {
            Text = $"GPO: {item.GpoName}   |   User Configuration",
            FontWeight = FontWeights.SemiBold
        };

        var tabs = new TabControl();

        var localeGrid = CreateGrid(3);
        _displayNameBox = new TextBox { Text = item.DisplayName };
        AddBox(localeGrid, 0, "Locale ID (LCID):", "LocaleId", item.LocaleId);
        AddBox(localeGrid, 1, "Locale name:", "LocaleName", item.LocaleName);
        AddRow(localeGrid, 2, "Display name:", _displayNameBox);

        tabs.Items.Add(new TabItem
        {
            Header = "Locale",
            Content = localeGrid
        });

        var numbersGrid = CreateGrid(9);
        AddBox(numbersGrid, 0, "Decimal symbol:", "NumberDecimalSymbol", item.NumberDecimalSymbol);
        AddBox(numbersGrid, 1, "Decimal digits:", "NumberDecimals", item.NumberDecimals);
        AddBox(numbersGrid, 2, "Digit grouping symbol:", "NumberGroupSymbol", item.NumberGroupSymbol);
        AddBox(numbersGrid, 3, "Digit grouping format:", "NumberGrouping", item.NumberGrouping);
        AddBox(numbersGrid, 4, "Negative sign:", "NumberNegativeSymbol", item.NumberNegativeSymbol);
        AddBox(numbersGrid, 5, "Negative number format:", "NumberNegativeFormat", item.NumberNegativeFormat);
        AddBox(numbersGrid, 6, "Leading zero:", "NumberLeadingZeros", item.NumberLeadingZeros);
        AddBox(numbersGrid, 7, "List separator:", "ListSeparator", item.ListSeparator);
        AddBox(numbersGrid, 8, "Measurement system:", "MeasurementSystem", item.MeasurementSystem);

        tabs.Items.Add(new TabItem
        {
            Header = "Numbers",
            Content = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = numbersGrid
            }
        });

        var currencyGrid = CreateGrid(7);
        AddBox(currencyGrid, 0, "Currency symbol:", "CurrencySymbol", item.CurrencySymbol);
        AddBox(currencyGrid, 1, "Positive format:", "CurrencyPositiveFormat", item.CurrencyPositiveFormat);
        AddBox(currencyGrid, 2, "Negative format:", "CurrencyNegativeFormat", item.CurrencyNegativeFormat);
        AddBox(currencyGrid, 3, "Decimal symbol:", "CurrencyDecimalSymbol", item.CurrencyDecimalSymbol);
        AddBox(currencyGrid, 4, "Decimal digits:", "CurrencyDecimals", item.CurrencyDecimals);
        AddBox(currencyGrid, 5, "Digit grouping symbol:", "CurrencyGroupSymbol", item.CurrencyGroupSymbol);
        AddBox(currencyGrid, 6, "Digit grouping format:", "CurrencyGrouping", item.CurrencyGrouping);

        tabs.Items.Add(new TabItem
        {
            Header = "Currency",
            Content = currencyGrid
        });

        var timeGrid = CreateGrid(4);
        AddBox(timeGrid, 0, "Time format:", "TimeFormat", item.TimeFormat);
        AddBox(timeGrid, 1, "Time separator:", "TimeSeparator", item.TimeSeparator);
        AddBox(timeGrid, 2, "AM symbol:", "AmSymbol", item.AmSymbol);
        AddBox(timeGrid, 3, "PM symbol:", "PmSymbol", item.PmSymbol);

        tabs.Items.Add(new TabItem
        {
            Header = "Time",
            Content = timeGrid
        });

        var dateGrid = CreateGrid(4);
        AddBox(dateGrid, 0, "Two-digit year max:", "InterpretYearMax", item.InterpretYearMax);
        AddBox(dateGrid, 1, "Short date format:", "ShortDateFormat", item.ShortDateFormat);
        AddBox(dateGrid, 2, "Date separator:", "DateSeparator", item.DateSeparator);
        AddBox(dateGrid, 3, "Long date format:", "LongDateFormat", item.LongDateFormat);

        tabs.Items.Add(new TabItem
        {
            Header = "Date",
            Content = dateGrid
        });

        var commonPanel = new StackPanel { Margin = new Thickness(14) };
        _disabledCheck = Check("Disable this preference item", item.Disabled);
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
            Text =
                "Advanced item-level targeting XML. Existing targeting is preserved. Leave empty to remove item-level targeting.",
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
        if (!int.TryParse(
                Text("LocaleId"),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var localeId) ||
            localeId <= 0)
        {
            Warn("Locale ID must be a positive decimal LCID.", "LocaleId");
            return;
        }

        if (string.IsNullOrWhiteSpace(Text("LocaleName")))
        {
            Warn("Locale name cannot be empty.", "LocaleName");
            return;
        }

        if (!ValidateInt("NumberDecimals", 0, 9, "Number decimals") ||
            !ValidateInt("NumberNegativeFormat", 0, 4, "Negative number format") ||
            !ValidateInt("NumberLeadingZeros", 0, 1, "Leading zero") ||
            !ValidateInt("MeasurementSystem", 0, 1, "Measurement system") ||
            !ValidateInt("CurrencyPositiveFormat", 0, 3, "Positive currency format") ||
            !ValidateInt("CurrencyNegativeFormat", 0, 15, "Negative currency format") ||
            !ValidateInt("CurrencyDecimals", 0, 9, "Currency decimals") ||
            !ValidateInt("InterpretYearMax", 99, 9999, "Two-digit year max"))
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(_filtersBox.Text))
        {
            try
            {
                var filters = XElement.Parse(_filtersBox.Text);
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
                    "Regional Options Preference",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }
        }

        _item.DisplayName = string.IsNullOrWhiteSpace(_displayNameBox.Text)
            ? Text("LocaleName")
            : _displayNameBox.Text.Trim();

        _item.LocaleId = Text("LocaleId");
        _item.LocaleName = Text("LocaleName");
        _item.NumberDecimalSymbol = Text("NumberDecimalSymbol");
        _item.NumberDecimals = Text("NumberDecimals");
        _item.NumberGroupSymbol = Text("NumberGroupSymbol");
        _item.NumberGrouping = Text("NumberGrouping");
        _item.NumberNegativeSymbol = Text("NumberNegativeSymbol");
        _item.NumberNegativeFormat = Text("NumberNegativeFormat");
        _item.NumberLeadingZeros = Text("NumberLeadingZeros");
        _item.ListSeparator = Text("ListSeparator");
        _item.MeasurementSystem = Text("MeasurementSystem");
        _item.CurrencySymbol = Text("CurrencySymbol");
        _item.CurrencyPositiveFormat = Text("CurrencyPositiveFormat");
        _item.CurrencyNegativeFormat = Text("CurrencyNegativeFormat");
        _item.CurrencyDecimalSymbol = Text("CurrencyDecimalSymbol");
        _item.CurrencyDecimals = Text("CurrencyDecimals");
        _item.CurrencyGroupSymbol = Text("CurrencyGroupSymbol");
        _item.CurrencyGrouping = Text("CurrencyGrouping");
        _item.TimeFormat = Text("TimeFormat");
        _item.TimeSeparator = Text("TimeSeparator");
        _item.AmSymbol = Text("AmSymbol");
        _item.PmSymbol = Text("PmSymbol");
        _item.InterpretYearMax = Text("InterpretYearMax");
        _item.ShortDateFormat = Text("ShortDateFormat");
        _item.DateSeparator = Text("DateSeparator");
        _item.LongDateFormat = Text("LongDateFormat");
        _item.Disabled = _disabledCheck.IsChecked == true;
        _item.BypassErrors = _bypassErrorsCheck.IsChecked == true;
        _item.RemoveWhenNoLongerApplied =
            _removePolicyCheck.IsChecked == true;
        _item.RunInUserContext =
            _userContextCheck.IsChecked == true;
        _item.FiltersXml = _filtersBox.Text.Trim();

        DialogResult = true;
    }

    private bool ValidateInt(
        string key,
        int min,
        int max,
        string label)
    {
        if (int.TryParse(
                Text(key),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var number) &&
            number >= min &&
            number <= max)
        {
            return true;
        }

        Warn($"{label} must be an integer from {min} to {max}.", key);
        return false;
    }

    private void Warn(string text, string key)
    {
        MessageBox.Show(
            this,
            text,
            "Regional Options Preference",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);

        if (_boxes.TryGetValue(key, out var box))
            box.Focus();
    }

    private string Text(string key) =>
        _boxes.TryGetValue(key, out var box)
            ? box.Text.Trim()
            : string.Empty;

    private void AddBox(
        Grid grid,
        int row,
        string label,
        string key,
        string value)
    {
        var box = new TextBox { Text = value };
        _boxes[key] = box;
        AddRow(grid, row, label, box);
    }

    private static Grid CreateGrid(int rows)
    {
        var grid = new Grid { Margin = new Thickness(14) };
        grid.ColumnDefinitions.Add(
            new ColumnDefinition { Width = new GridLength(210) });
        grid.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width = new GridLength(1, GridUnitType.Star)
            });

        for (var index = 0; index < rows; index++)
        {
            grid.RowDefinitions.Add(
                new RowDefinition { Height = GridLength.Auto });
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
}

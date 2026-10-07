using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Xml.Linq;
using GPOSettingsExplorer.Models;

namespace GPOSettingsExplorer;

public sealed class GppNetworkShareEditorWindow : Window
{
    private readonly GppNetworkShareInfo _item;

    private readonly TextBox _displayNameBox;
    private readonly TextBox _descriptionBox;
    private readonly ComboBox _actionCombo;
    private readonly TextBox _shareNameBox;
    private readonly TextBox _pathBox;
    private readonly TextBox _commentBox;

    private readonly CheckBox _allRegularCheck;
    private readonly CheckBox _allHiddenCheck;
    private readonly CheckBox _allAdminDriveCheck;

    private readonly ComboBox _limitUsersCombo;
    private readonly TextBox _userLimitBox;
    private readonly ComboBox _abeCombo;

    private readonly CheckBox _disabledCheck;
    private readonly CheckBox _bypassErrorsCheck;
    private readonly CheckBox _removePolicyCheck;
    private readonly CheckBox _userContextCheck;
    private readonly TextBox _filtersBox;

    public GppNetworkShareInfo Item => _item;

    public GppNetworkShareEditorWindow(
        GppNetworkShareInfo item)
    {
        _item = item;

        Title = "Network Share Preference Editor";
        Width = 980;
        Height = 780;
        MinWidth = 780;
        MinHeight = 620;
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
                $"GPO: {item.GpoName}   |   Computer Configuration",
            FontWeight =
                FontWeights.SemiBold
        };

        var tabs = new TabControl();

        var general = new StackPanel
        {
            Margin = new Thickness(10)
        };

        var generalGrid =
            CreateGrid(
                6,
                190);

        _displayNameBox =
            new TextBox
            {
                Text = item.DisplayName
            };

        _descriptionBox =
            new TextBox
            {
                Text = item.Description,
                AcceptsReturn = true,
                MinHeight = 56
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

        _shareNameBox =
            new TextBox
            {
                Text = item.ShareName,
                ToolTip =
                    "Share name presented by the client computer."
            };

        _pathBox =
            new TextBox
            {
                Text = item.Path,
                ToolTip =
                    @"Local filesystem path on the client computer, for example C:\Shares\Department."
            };

        _commentBox =
            new TextBox
            {
                Text = item.Comment,
                AcceptsReturn = true,
                MinHeight = 56
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
            "Share name:",
            _shareNameBox);

        AddRow(
            generalGrid,
            4,
            "Local folder path:",
            _pathBox);

        AddRow(
            generalGrid,
            5,
            "Share comment:",
            _commentBox);

        general.Children.Add(
            generalGrid);

        var bulkPanel =
            new StackPanel
            {
                Margin =
                    new Thickness(8)
            };

        _allRegularCheck =
            Check(
                "Apply the operation to all regular shares",
                item.AllRegular);

        _allHiddenCheck =
            Check(
                "Apply the operation to all hidden shares",
                item.AllHidden);

        _allAdminDriveCheck =
            Check(
                "Apply the operation to all administrative drive-letter shares",
                item.AllAdminDrive);

        bulkPanel.Children.Add(
            _allRegularCheck);

        bulkPanel.Children.Add(
            _allHiddenCheck);

        bulkPanel.Children.Add(
            _allAdminDriveCheck);

        general.Children.Add(
            new GroupBox
            {
                Header = "Bulk share selection",
                Margin =
                    new Thickness(4, 14, 4, 4),
                Content =
                    bulkPanel
            });

        general.Children.Add(
            new TextBlock
            {
                Text =
                    "Bulk share flags are normally used with Update or Delete. When any bulk flag is selected, the individual share name and path are not required.",
                TextWrapping =
                    TextWrapping.Wrap,
                Foreground =
                    System.Windows.Media.Brushes.DimGray,
                Margin =
                    new Thickness(8, 6, 8, 4)
            });

        tabs.Items.Add(
            new TabItem
            {
                Header = "Share",
                Content =
                    new ScrollViewer
                    {
                        VerticalScrollBarVisibility =
                            ScrollBarVisibility.Auto,
                        Content =
                            general
                    }
            });

        var optionsGrid =
            CreateGrid(
                4,
                220);

        _limitUsersCombo =
            new ComboBox
            {
                ItemsSource =
                    new[]
                    {
                        "No change",
                        "Maximum allowed",
                        "Set limit"
                    },
                SelectedItem =
                    LimitDisplay(
                        item.LimitUsersMode)
            };

        _limitUsersCombo.SelectionChanged +=
            (_, _) =>
                UpdateLimitUi();

        _userLimitBox =
            new TextBox
            {
                Text =
                    item.UserLimit
            };

        _abeCombo =
            new ComboBox
            {
                ItemsSource =
                    new[]
                    {
                        "No change",
                        "Enabled",
                        "Disabled"
                    },
                SelectedItem =
                    AbeDisplay(
                        item.AbeMode)
            };

        AddRow(
            optionsGrid,
            0,
            "User limit:",
            _limitUsersCombo);

        AddRow(
            optionsGrid,
            1,
            "Maximum users:",
            _userLimitBox);

        AddRow(
            optionsGrid,
            2,
            "Access-based enumeration:",
            _abeCombo);

        var optionsNote =
            new TextBlock
            {
                Text =
                    "Access-based enumeration controls whether users can see folders in the share for which they do not have read access. Existing share ACLs are not changed by these options.",
                TextWrapping =
                    TextWrapping.Wrap,
                Foreground =
                    System.Windows.Media.Brushes.DimGray,
                Margin =
                    new Thickness(4, 12, 4, 4)
            };

        Grid.SetRow(
            optionsNote,
            3);
        Grid.SetColumnSpan(
            optionsNote,
            2);
        optionsGrid.Children.Add(
            optionsNote);

        tabs.Items.Add(
            new TabItem
            {
                Header = "Share Options",
                Content =
                    new ScrollViewer
                    {
                        VerticalScrollBarVisibility =
                            ScrollBarVisibility.Auto,
                        Content =
                            optionsGrid
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

        UpdateLimitUi();
    }

    private void Save_Click(
        object sender,
        RoutedEventArgs e)
    {
        var shareName =
            _shareNameBox.Text.Trim();

        var path =
            _pathBox.Text.Trim();

        var bulk =
            _allRegularCheck.IsChecked == true ||
            _allHiddenCheck.IsChecked == true ||
            _allAdminDriveCheck.IsChecked == true;

        var action =
            ActionCode(
                Convert.ToString(
                    _actionCombo.SelectedItem));

        if (!bulk &&
            string.IsNullOrWhiteSpace(
                shareName))
        {
            Warn(
                "Share name cannot be empty unless a bulk share option is selected.",
                _shareNameBox);
            return;
        }

        if (!bulk &&
            action != "D" &&
            string.IsNullOrWhiteSpace(path))
        {
            Warn(
                "Create, Update, and Replace require a local folder path.",
                _pathBox);
            return;
        }

        var limitMode =
            LimitCode(
                Convert.ToString(
                    _limitUsersCombo.SelectedItem));

        var userLimit =
            _userLimitBox.Text.Trim();

        if (limitMode == "SET_LIMIT" &&
            (!uint.TryParse(
                 userLimit,
                 NumberStyles.Integer,
                 CultureInfo.InvariantCulture,
                 out var limit) ||
             limit == 0))
        {
            Warn(
                "Maximum users must be a positive whole number.",
                _userLimitBox);
            return;
        }

        if (!ValidateFilters())
            return;

        _item.DisplayName =
            string.IsNullOrWhiteSpace(
                _displayNameBox.Text)
                ? (string.IsNullOrWhiteSpace(shareName)
                    ? "Network Share"
                    : shareName)
                : _displayNameBox.Text.Trim();

        _item.Description =
            _descriptionBox.Text;

        _item.Action =
            action;

        _item.ShareName =
            shareName;

        _item.Path =
            path;

        _item.Comment =
            _commentBox.Text;

        _item.AllRegular =
            _allRegularCheck.IsChecked == true;

        _item.AllHidden =
            _allHiddenCheck.IsChecked == true;

        _item.AllAdminDrive =
            _allAdminDriveCheck.IsChecked == true;

        _item.LimitUsersMode =
            limitMode;

        _item.UserLimit =
            limitMode == "SET_LIMIT"
                ? userLimit
                : string.Empty;

        _item.AbeMode =
            AbeCode(
                Convert.ToString(
                    _abeCombo.SelectedItem));

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

    private void UpdateLimitUi()
    {
        if (_userLimitBox is null ||
            _limitUsersCombo is null)
            return;

        _userLimitBox.IsEnabled =
            LimitCode(
                Convert.ToString(
                    _limitUsersCombo.SelectedItem))
            == "SET_LIMIT";
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
                "Network Share",
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
            "Network Share",
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

    private static string LimitDisplay(
        string value) =>
        value.Trim()
            .ToUpperInvariant() switch
        {
            "SET_LIMIT" =>
                "Set limit",
            "MAX_ALLOWED" =>
                "Maximum allowed",
            _ =>
                "No change"
        };

    private static string LimitCode(
        string? value) =>
        value?.Trim()
            .ToUpperInvariant() switch
        {
            "SET LIMIT" =>
                "SET_LIMIT",
            "MAXIMUM ALLOWED" =>
                "MAX_ALLOWED",
            _ =>
                "NO_CHANGE"
        };

    private static string AbeDisplay(
        string value) =>
        value.Trim()
            .ToUpperInvariant() switch
        {
            "ENABLE" =>
                "Enabled",
            "DISABLE" =>
                "Disabled",
            _ =>
                "No change"
        };

    private static string AbeCode(
        string? value) =>
        value?.Trim()
            .ToUpperInvariant() switch
        {
            "ENABLED" =>
                "ENABLE",
            "DISABLED" =>
                "DISABLE",
            _ =>
                "NO_CHANGE"
        };
}

using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Xml.Linq;
using GPOSettingsExplorer.Models;

namespace GPOSettingsExplorer;

public sealed class GppShortcutEditorWindow : Window
{
    private readonly GppShortcutItemInfo _item;

    private readonly TextBox _displayNameBox;
    private readonly ComboBox _actionCombo;
    private readonly TextBox _shortcutPathBox;
    private readonly ComboBox _targetTypeCombo;
    private readonly TextBox _targetPathBox;
    private readonly TextBox _argumentsBox;
    private readonly TextBox _startInBox;
    private readonly TextBox _shortcutKeyBox;
    private readonly TextBox _windowBox;
    private readonly TextBox _commentBox;
    private readonly TextBox _iconPathBox;
    private readonly TextBox _iconIndexBox;
    private readonly TextBox _pidlBox;

    private readonly CheckBox _disabledCheck;
    private readonly CheckBox _bypassErrorsCheck;
    private readonly CheckBox _removePolicyCheck;
    private readonly CheckBox _userContextCheck;
    private readonly TextBox _filtersBox;

    public GppShortcutItemInfo Item => _item;

    public GppShortcutEditorWindow(GppShortcutItemInfo item)
    {
        _item = item;

        Title = "Shortcuts Preference Editor";
        Width = 1000;
        Height = 790;
        MinWidth = 800;
        MinHeight = 620;
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
            Text = $"GPO: {item.GpoName}   |   Scope: {item.Scope} Configuration",
            FontWeight = FontWeights.SemiBold
        };

        var tabs = new TabControl();

        var generalGrid = new Grid { Margin = new Thickness(10) };
        for (var i = 0; i < 13; i++)
            generalGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        generalGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
        generalGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        _displayNameBox = new TextBox { Text = item.DisplayName };

        _actionCombo = new ComboBox
        {
            ItemsSource = new[] { "Create", "Update", "Replace", "Delete" },
            SelectedItem = ActionDisplay(item.Action)
        };

        _shortcutPathBox = new TextBox
        {
            Text = item.ShortcutPath,
            ToolTip = @"Examples: %DesktopDir%\My App.lnk or %StartMenuDir%\My App.lnk"
        };

        _targetTypeCombo = new ComboBox
        {
            ItemsSource = new[] { "FILESYSTEM", "URL", "SHELL" },
            SelectedItem = NormalizeTargetType(item.TargetType)
        };
        _targetTypeCombo.SelectionChanged += (_, _) => UpdateTargetTypeUi();

        _targetPathBox = new TextBox
        {
            Text = item.TargetPath,
            ToolTip = "Local/UNC path, absolute URL, or shell object depending on target type."
        };

        _argumentsBox = new TextBox { Text = item.Arguments };
        _startInBox = new TextBox { Text = item.StartIn };
        _shortcutKeyBox = new TextBox
        {
            Text = string.IsNullOrWhiteSpace(item.ShortcutKey) ? "0" : item.ShortcutKey
        };
        _windowBox = new TextBox
        {
            Text = item.Window,
            ToolTip = "Raw GPP window value. Existing values are preserved."
        };
        _commentBox = new TextBox
        {
            Text = item.Comment,
            AcceptsReturn = true,
            Height = 62
        };
        _iconPathBox = new TextBox { Text = item.IconPath };
        _iconIndexBox = new TextBox
        {
            Text = string.IsNullOrWhiteSpace(item.IconIndex) ? "0" : item.IconIndex
        };
        _pidlBox = new TextBox
        {
            Text = item.Pidl,
            ToolTip = "Optional raw shell PIDL value used by some shell shortcuts."
        };

        AddRow(generalGrid, 0, "Display name:", _displayNameBox);
        AddRow(generalGrid, 1, "Action:", _actionCombo);
        AddRow(generalGrid, 2, "Shortcut path:", _shortcutPathBox);
        AddRow(generalGrid, 3, "Target type:", _targetTypeCombo);
        AddRow(generalGrid, 4, "Target path:", _targetPathBox);
        AddRow(generalGrid, 5, "Arguments:", _argumentsBox);
        AddRow(generalGrid, 6, "Start in:", _startInBox);
        AddRow(generalGrid, 7, "Shortcut key:", _shortcutKeyBox);
        AddRow(generalGrid, 8, "Window:", _windowBox);
        AddRow(generalGrid, 9, "Comment / tooltip:", _commentBox);
        AddRow(generalGrid, 10, "Icon path:", _iconPathBox);
        AddRow(generalGrid, 11, "Icon index:", _iconIndexBox);
        AddRow(generalGrid, 12, "PIDL:", _pidlBox);

        tabs.Items.Add(new TabItem
        {
            Header = "Shortcut",
            Content = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = generalGrid
            }
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

        commonPanel.Children.Add(new TextBlock
        {
            Text =
                "Shortcut Preferences support Computer and User Configuration. " +
                "The security-context option is preserved exactly in the item XML.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = System.Windows.Media.Brushes.DimGray,
            Margin = new Thickness(4, 14, 4, 4)
        });

        tabs.Items.Add(new TabItem
        {
            Header = "Common",
            Content = commonPanel
        });

        var targetingPanel = new DockPanel { Margin = new Thickness(10) };

        var targetingHelp = new TextBlock
        {
            Text =
                "Advanced item-level targeting XML. Existing targeting is preserved. " +
                "Leave empty to remove item-level targeting.",
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
            Header = item.HasFilters
                ? "Item-level Targeting *"
                : "Item-level Targeting",
            Content = targetingPanel
        });

        root.Children.Add(footer);
        root.Children.Add(header);
        root.Children.Add(tabs);
        Content = root;

        UpdateTargetTypeUi();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var action = ActionCode(
            Convert.ToString(_actionCombo.SelectedItem));

        var shortcutPath = _shortcutPathBox.Text.Trim();
        var targetType = NormalizeTargetType(
            Convert.ToString(_targetTypeCombo.SelectedItem));
        var targetPath = _targetPathBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(shortcutPath))
        {
            Warn(
                "Shortcut path cannot be empty.",
                _shortcutPathBox);
            return;
        }

        if (action != "D" &&
            string.IsNullOrWhiteSpace(targetPath))
        {
            Warn(
                "Create, Update, and Replace require a target path.",
                _targetPathBox);
            return;
        }

        if (targetType == "URL" &&
            action != "D" &&
            !Uri.TryCreate(
                targetPath,
                UriKind.Absolute,
                out _))
        {
            Warn(
                "URL shortcut target must be an absolute URL.",
                _targetPathBox);
            return;
        }

        var iconIndex = _iconIndexBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(iconIndex))
            iconIndex = "0";

        if (!int.TryParse(
                iconIndex,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out _))
        {
            Warn(
                "Icon index must be an integer.",
                _iconIndexBox);
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
                    "Shortcut",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }
        }

        _item.DisplayName = string.IsNullOrWhiteSpace(_displayNameBox.Text)
            ? shortcutPath
            : _displayNameBox.Text.Trim();
        _item.Action = action;
        _item.ShortcutPath = shortcutPath;
        _item.TargetType = targetType;
        _item.TargetPath = targetPath;
        _item.Arguments = _argumentsBox.Text;
        _item.StartIn = _startInBox.Text.Trim();
        _item.ShortcutKey = _shortcutKeyBox.Text.Trim();
        _item.Window = _windowBox.Text.Trim();
        _item.Comment = _commentBox.Text;
        _item.IconPath = _iconPathBox.Text.Trim();
        _item.IconIndex = iconIndex;
        _item.Pidl = _pidlBox.Text;

        _item.Disabled = _disabledCheck.IsChecked == true;
        _item.BypassErrors = _bypassErrorsCheck.IsChecked == true;
        _item.RemoveWhenNoLongerApplied =
            _removePolicyCheck.IsChecked == true;
        _item.RunInUserContext =
            _userContextCheck.IsChecked == true;
        _item.FiltersXml = _filtersBox.Text.Trim();

        DialogResult = true;
    }

    private void UpdateTargetTypeUi()
    {
        if (_targetTypeCombo is null)
            return;

        var type = NormalizeTargetType(
            Convert.ToString(_targetTypeCombo.SelectedItem));

        var fileSystem = type == "FILESYSTEM";
        var supportsWindowAndComment =
            type is "FILESYSTEM" or "SHELL";

        _argumentsBox.IsEnabled = fileSystem;
        _startInBox.IsEnabled = fileSystem;
        _windowBox.IsEnabled = supportsWindowAndComment;
        _commentBox.IsEnabled = supportsWindowAndComment;
    }

    private void Warn(
        string message,
        Control control)
    {
        MessageBox.Show(
            this,
            message,
            "Shortcut",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);

        control.Focus();
    }

    private static CheckBox Check(
        string text,
        bool value) =>
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

    private static string NormalizeTargetType(string? value) =>
        value?.Trim().ToUpperInvariant() switch
        {
            "URL" => "URL",
            "SHELL" => "SHELL",
            _ => "FILESYSTEM"
        };
}

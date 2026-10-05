using System.Windows;
using System.Windows.Controls;
using System.Xml.Linq;
using GPOSettingsExplorer.Models;

namespace GPOSettingsExplorer;

public sealed class GppDriveEditorWindow : Window
{
    private readonly GppDriveItemInfo _item;

    private readonly ComboBox _actionCombo;
    private readonly ComboBox _letterCombo;
    private readonly CheckBox _exactLetterCheck;
    private readonly TextBox _pathBox;
    private readonly TextBox _labelBox;
    private readonly CheckBox _persistentCheck;
    private readonly TextBox _userNameBox;
    private readonly ComboBox _thisDriveCombo;
    private readonly ComboBox _allDrivesCombo;

    private readonly CheckBox _disabledCheck;
    private readonly CheckBox _bypassErrorsCheck;
    private readonly CheckBox _removePolicyCheck;
    private readonly CheckBox _userContextCheck;
    private readonly CheckBox _clearCredentialCheck;
    private readonly TextBox _filtersBox;

    public GppDriveItemInfo Item => _item;

    public GppDriveEditorWindow(GppDriveItemInfo item)
    {
        _item = item;

        Title = "Drive Maps Preference Editor";
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
            BorderBrush = System.Windows.Media.Brushes.LightGray,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(6),
            Margin = new Thickness(0, 0, 0, 8)
        };
        DockPanel.SetDock(header, Dock.Top);

        header.Child = new TextBlock
        {
            Text = $"GPO: {item.GpoName}   |   Scope: User Configuration",
            FontWeight = FontWeights.SemiBold
        };

        var tabs = new TabControl();

        var generalGrid = new Grid { Margin = new Thickness(10) };
        for (var i = 0; i < 9; i++)
            generalGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        generalGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
        generalGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        _actionCombo = new ComboBox
        {
            ItemsSource = new[] { "Create", "Update", "Replace", "Delete" },
            SelectedItem = ActionDisplay(item.Action)
        };

        _letterCombo = new ComboBox
        {
            ItemsSource = Enumerable.Range('A', 26)
                .Select(value => ((char)value).ToString())
                .ToArray(),
            SelectedItem = string.IsNullOrWhiteSpace(item.Letter)
                ? "S"
                : item.Letter.Trim().TrimEnd(':').ToUpperInvariant()
        };

        _exactLetterCheck = new CheckBox
        {
            Content = "Use this exact drive letter",
            IsChecked = item.UseExactLetter,
            VerticalAlignment = VerticalAlignment.Center
        };

        _pathBox = new TextBox
        {
            Text = item.Path,
            ToolTip = @"UNC path such as \\server\share"
        };

        _labelBox = new TextBox { Text = item.Label };

        _persistentCheck = new CheckBox
        {
            Content = "Reconnect",
            IsChecked = item.Persistent
        };

        _userNameBox = new TextBox
        {
            Text = item.UserName,
            ToolTip = @"Optional NETBIOS account name such as DOMAIN\user"
        };

        var visibility = new[] { "NOCHANGE", "SHOW", "HIDE" };

        _thisDriveCombo = new ComboBox
        {
            ItemsSource = visibility,
            SelectedItem = NormalizeVisibility(item.ThisDriveVisibility)
        };

        _allDrivesCombo = new ComboBox
        {
            ItemsSource = visibility,
            SelectedItem = NormalizeVisibility(item.AllDrivesVisibility)
        };

        AddRow(generalGrid, 0, "Action:", _actionCombo);
        AddRow(generalGrid, 1, "Drive letter:", _letterCombo);
        AddRow(generalGrid, 2, "Letter behavior:", _exactLetterCheck);
        AddRow(generalGrid, 3, "Location:", _pathBox);
        AddRow(generalGrid, 4, "Label:", _labelBox);
        AddRow(generalGrid, 5, "Reconnect:", _persistentCheck);
        AddRow(generalGrid, 6, "Connect as:", _userNameBox);
        AddRow(generalGrid, 7, "This drive visibility:", _thisDriveCombo);
        AddRow(generalGrid, 8, "All drives visibility:", _allDrivesCombo);

        var generalPanel = new DockPanel();
        generalPanel.Children.Add(generalGrid);

        tabs.Items.Add(new TabItem
        {
            Header = "Drive Mapping",
            Content = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = generalPanel
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

        var credentialBox = new GroupBox
        {
            Header = "Stored credential",
            Margin = new Thickness(4, 16, 4, 4)
        };

        var credentialPanel = new StackPanel { Margin = new Thickness(8) };

        credentialPanel.Children.Add(new TextBlock
        {
            Text = item.HasStoredCredential
                ? "This existing Drive Maps item contains a legacy GPP cpassword value. GPO Settings Explorer preserves it opaquely and never displays the password."
                : "This Drive Maps item does not contain a stored cpassword value. GPO Settings Explorer does not create new legacy cpassword credentials.",
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
        commonPanel.Children.Add(credentialBox);

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
            FontFamily = new System.Windows.Media.FontFamily("Consolas"),
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
        var action = ActionCode(Convert.ToString(_actionCombo.SelectedItem));
        var letter = Convert.ToString(_letterCombo.SelectedItem) ?? "S";
        var path = _pathBox.Text.Trim();

        if (action is "C" or "R")
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                MessageBox.Show(
                    this,
                    "Create and Replace require a UNC path.",
                    "Drive Maps",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                _pathBox.Focus();
                return;
            }
        }

        if (!string.IsNullOrWhiteSpace(path) &&
            !path.StartsWith(@"\\", StringComparison.Ordinal))
        {
            MessageBox.Show(
                this,
                "Drive location must be a UNC path beginning with \\\\.",
                "Drive Maps",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            _pathBox.Focus();
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
                MessageBox.Show(
                    this,
                    $"Item-level targeting XML is invalid:\n\n{ex.Message}",
                    "Drive Maps",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }
        }

        _item.Action = action;
        _item.Letter = letter;
        _item.DisplayName = letter + ":";
        _item.UseExactLetter = _exactLetterCheck.IsChecked == true;
        _item.Path = path;
        _item.Label = _labelBox.Text.Trim();
        _item.Persistent = _persistentCheck.IsChecked == true;
        _item.UserName = _userNameBox.Text.Trim();
        _item.ThisDriveVisibility =
            Convert.ToString(_thisDriveCombo.SelectedItem) ?? "NOCHANGE";
        _item.AllDrivesVisibility =
            Convert.ToString(_allDrivesCombo.SelectedItem) ?? "NOCHANGE";

        _item.Disabled = _disabledCheck.IsChecked == true;
        _item.BypassErrors = _bypassErrorsCheck.IsChecked == true;
        _item.RemoveWhenNoLongerApplied = _removePolicyCheck.IsChecked == true;
        _item.RunInUserContext = _userContextCheck.IsChecked == true;
        _item.ClearStoredCredential = _clearCredentialCheck.IsChecked == true;
        _item.FiltersXml = _filtersBox.Text.Trim();

        DialogResult = true;
    }

    private static CheckBox Check(string text, bool value) =>
        new()
        {
            Content = text,
            IsChecked = value,
            Margin = new Thickness(4, 6, 4, 6)
        };

    private static void AddRow(Grid grid, int row, string label, UIElement editor)
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

    private static string NormalizeVisibility(string value) =>
        value.Trim().ToUpperInvariant() switch
        {
            "SHOW" => "SHOW",
            "HIDE" => "HIDE",
            _ => "NOCHANGE"
        };
}

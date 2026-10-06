using System.Windows;
using System.Windows.Controls;
using System.Xml.Linq;
using GPOSettingsExplorer.Models;

namespace GPOSettingsExplorer;

public sealed class GppFileAssociationEditorWindow : Window
{
    private readonly GppFileAssociationItemInfo _item;

    private readonly TextBox _nameBox;
    private readonly TextBox _descriptionBox;
    private readonly ComboBox _actionCombo;
    private readonly TextBox _extensionBox;

    private readonly TextBox _applicationPathBox;
    private readonly CheckBox _defaultApplicationCheck;

    private readonly TextBox _applicationBox;
    private readonly TextBox _progIdBox;
    private readonly CheckBox _configureActionsCheck;

    private readonly CheckBox _disabledCheck;
    private readonly CheckBox _bypassErrorsCheck;
    private readonly CheckBox _removePolicyCheck;
    private readonly CheckBox _userContextCheck;
    private readonly TextBox _filtersBox;

    public GppFileAssociationItemInfo Item => _item;

    public GppFileAssociationEditorWindow(
        GppFileAssociationItemInfo item)
    {
        _item = item;

        Title = "File Association Preference Editor";
        Width = 900;
        Height = 700;
        MinWidth = 720;
        MinHeight = 520;
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
            Text =
                $"GPO: {item.GpoName}   |   {item.Scope} Configuration   |   {item.KindDisplay}",
            FontWeight = FontWeights.SemiBold
        };

        var tabs = new TabControl();

        var generalGrid = CreateGrid(item.IsOpenWith ? 7 : 8);

        _nameBox = new TextBox { Text = item.DisplayName };
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

        _actionCombo.SelectedItem =
            _actionCombo.Items.Cast<Choice>()
                .FirstOrDefault(choice =>
                    choice.Value.Equals(
                        NormalizeAction(item.Action),
                        StringComparison.OrdinalIgnoreCase))
            ?? _actionCombo.Items.Cast<Choice>().First();

        _extensionBox = new TextBox { Text = item.FileExtension };

        _applicationPathBox = new TextBox { Text = item.ApplicationPath };
        _defaultApplicationCheck = new CheckBox
        {
            Content = "Make this the default application",
            IsChecked = item.DefaultApplication
        };

        _applicationBox = new TextBox { Text = item.Application };
        _progIdBox = new TextBox { Text = item.ApplicationProgId };
        _configureActionsCheck = new CheckBox
        {
            Content = "Configure actions for this file type",
            IsChecked = item.ConfigureActions
        };

        AddRow(generalGrid, 0, "Display name:", _nameBox);
        AddRow(generalGrid, 1, "Description:", _descriptionBox);
        AddRow(generalGrid, 2, "Action:", _actionCombo);
        AddRow(generalGrid, 3, "File extension:", _extensionBox);

        if (item.IsOpenWith)
        {
            AddRow(generalGrid, 4, "Application path:", _applicationPathBox);
            AddRow(generalGrid, 5, "Default application:", _defaultApplicationCheck);

            var note = new TextBlock
            {
                Text =
                    "Open With preferences are applied in User Configuration.",
                TextWrapping = TextWrapping.Wrap,
                Foreground = System.Windows.SystemColors.GrayTextBrush,
                Margin = new Thickness(4)
            };
            Grid.SetRow(note, 6);
            Grid.SetColumn(note, 0);
            Grid.SetColumnSpan(note, 2);
            generalGrid.Children.Add(note);
        }
        else
        {
            AddRow(generalGrid, 4, "Application name:", _applicationBox);
            AddRow(generalGrid, 5, "Application ProgID:", _progIdBox);
            AddRow(generalGrid, 6, "Actions:", _configureActionsCheck);

            var note = new TextBlock
            {
                Text =
                    "File Type preferences are applied in Computer Configuration.",
                TextWrapping = TextWrapping.Wrap,
                Foreground = System.Windows.SystemColors.GrayTextBrush,
                Margin = new Thickness(4)
            };
            Grid.SetRow(note, 7);
            Grid.SetColumn(note, 0);
            Grid.SetColumnSpan(note, 2);
            generalGrid.Children.Add(note);
        }

        tabs.Items.Add(new TabItem
        {
            Header = "General",
            Content = generalGrid
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

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var extension = _extensionBox.Text.Trim().TrimStart('.');

        if (string.IsNullOrWhiteSpace(extension))
        {
            MessageBox.Show(
                this,
                "File extension cannot be empty.",
                "File Association Preference",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            _extensionBox.Focus();
            return;
        }

        var action =
            (_actionCombo.SelectedItem as Choice)?.Value ?? "U";

        if (_item.IsOpenWith &&
            !action.Equals("D", StringComparison.OrdinalIgnoreCase) &&
            string.IsNullOrWhiteSpace(_applicationPathBox.Text))
        {
            MessageBox.Show(
                this,
                "Application path cannot be empty unless the action is Delete.",
                "File Association Preference",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            _applicationPathBox.Focus();
            return;
        }

        if (_item.IsFileType &&
            !action.Equals("D", StringComparison.OrdinalIgnoreCase) &&
            string.IsNullOrWhiteSpace(_applicationBox.Text) &&
            string.IsNullOrWhiteSpace(_progIdBox.Text))
        {
            MessageBox.Show(
                this,
                "Specify an application name or ProgID unless the action is Delete.",
                "File Association Preference",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            _applicationBox.Focus();
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
                    "File Association Preference",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }
        }

        _item.DisplayName =
            string.IsNullOrWhiteSpace(_nameBox.Text)
                ? extension
                : _nameBox.Text.Trim();

        _item.Description = _descriptionBox.Text.Trim();
        _item.Action = action;
        _item.FileExtension = extension;
        _item.ApplicationPath = _applicationPathBox.Text.Trim();
        _item.DefaultApplication =
            _defaultApplicationCheck.IsChecked == true;
        _item.Application = _applicationBox.Text.Trim();
        _item.ApplicationProgId = _progIdBox.Text.Trim();
        _item.ConfigureActions =
            _configureActionsCheck.IsChecked == true;
        _item.Disabled = _disabledCheck.IsChecked == true;
        _item.BypassErrors = _bypassErrorsCheck.IsChecked == true;
        _item.RemoveWhenNoLongerApplied =
            _removePolicyCheck.IsChecked == true;
        _item.RunInUserContext =
            _userContextCheck.IsChecked == true;
        _item.FiltersXml = _filtersBox.Text.Trim();

        DialogResult = true;
    }

    private static Grid CreateGrid(int rows)
    {
        var grid = new Grid { Margin = new Thickness(14) };
        grid.ColumnDefinitions.Add(
            new ColumnDefinition { Width = new GridLength(190) });
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

    private sealed record Choice(
        string Value,
        string Display);
}

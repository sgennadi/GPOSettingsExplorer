using System.Windows;
using System.Windows.Controls;
using System.Xml.Linq;
using GPOSettingsExplorer.Models;

namespace GPOSettingsExplorer;

public sealed class GppFileEditorWindow : Window
{
    private readonly GppFileItemInfo _item;
    private readonly TextBox _displayNameBox;
    private readonly ComboBox _actionCombo;
    private readonly TextBox _sourceBox;
    private readonly TextBox _targetBox;
    private readonly CheckBox _suppressErrorsCheck;
    private readonly CheckBox _readOnlyCheck;
    private readonly CheckBox _archiveCheck;
    private readonly CheckBox _hiddenCheck;
    private readonly CheckBox _disabledCheck;
    private readonly CheckBox _bypassErrorsCheck;
    private readonly CheckBox _removePolicyCheck;
    private readonly CheckBox _userContextCheck;
    private readonly TextBox _filtersBox;

    public GppFileItemInfo Item => _item;

    public GppFileEditorWindow(GppFileItemInfo item)
    {
        _item = item;

        Title = "Files Preference Editor";
        Width = 960;
        Height = 700;
        MinWidth = 760;
        MinHeight = 560;
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
        for (var i = 0; i < 8; i++)
            generalGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        generalGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
        generalGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        _displayNameBox = new TextBox { Text = item.DisplayName };

        _actionCombo = new ComboBox
        {
            ItemsSource = new[] { "Create", "Update", "Replace", "Delete" },
            SelectedItem = ActionDisplay(item.Action)
        };
        _actionCombo.SelectionChanged += (_, _) => UpdateActionUi();

        _sourceBox = new TextBox
        {
            Text = item.FromPath,
            ToolTip = @"Source file. Local path, UNC path, or environment-variable path."
        };

        _targetBox = new TextBox
        {
            Text = item.TargetPath,
            ToolTip = @"Destination file path."
        };

        _suppressErrorsCheck = Check(
            "Suppress file-operation errors",
            item.SuppressErrors);

        var attributes = new StackPanel { Orientation = Orientation.Horizontal };
        _readOnlyCheck = Check("Read-only", item.ReadOnly);
        _archiveCheck = Check("Archive", item.Archive);
        _hiddenCheck = Check("Hidden", item.Hidden);

        attributes.Children.Add(_readOnlyCheck);
        attributes.Children.Add(_archiveCheck);
        attributes.Children.Add(_hiddenCheck);

        AddRow(generalGrid, 0, "Display name:", _displayNameBox);
        AddRow(generalGrid, 1, "Action:", _actionCombo);
        AddRow(generalGrid, 2, "Source file:", _sourceBox);
        AddRow(generalGrid, 3, "Target file:", _targetBox);
        AddRow(generalGrid, 4, "File attributes:", attributes);
        AddRow(generalGrid, 5, "Operation:", _suppressErrorsCheck);

        var note = new TextBlock
        {
            Text =
                "Create, Update, and Replace require a source file. Delete only requires the target file. " +
                "Existing unsupported XML attributes are preserved by the structured editor.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = System.Windows.Media.Brushes.DimGray,
            Margin = new Thickness(4, 12, 4, 4)
        };
        Grid.SetRow(note, 6);
        Grid.SetColumnSpan(note, 2);
        generalGrid.Children.Add(note);

        tabs.Items.Add(new TabItem
        {
            Header = "File",
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
            FontFamily = new System.Windows.Media.FontFamily("Consolas"),
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

        UpdateActionUi();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var action = ActionCode(
            Convert.ToString(_actionCombo.SelectedItem));

        var source = _sourceBox.Text.Trim();
        var target = _targetBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(target))
        {
            Warn("Target file path cannot be empty.", _targetBox);
            return;
        }

        if (action != "D" &&
            string.IsNullOrWhiteSpace(source))
        {
            Warn(
                "Create, Update, and Replace require a source file.",
                _sourceBox);
            return;
        }

        if (!ValidateFilters())
            return;

        _item.DisplayName = string.IsNullOrWhiteSpace(_displayNameBox.Text)
            ? target
            : _displayNameBox.Text.Trim();
        _item.Action = action;
        _item.FromPath = source;
        _item.TargetPath = target;
        _item.SuppressErrors = _suppressErrorsCheck.IsChecked == true;
        _item.ReadOnly = _readOnlyCheck.IsChecked == true;
        _item.Archive = _archiveCheck.IsChecked == true;
        _item.Hidden = _hiddenCheck.IsChecked == true;

        _item.Disabled = _disabledCheck.IsChecked == true;
        _item.BypassErrors = _bypassErrorsCheck.IsChecked == true;
        _item.RemoveWhenNoLongerApplied =
            _removePolicyCheck.IsChecked == true;
        _item.RunInUserContext =
            _userContextCheck.IsChecked == true;
        _item.FiltersXml = _filtersBox.Text.Trim();

        DialogResult = true;
    }

    private bool ValidateFilters()
    {
        if (string.IsNullOrWhiteSpace(_filtersBox.Text))
            return true;

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

            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                $"Item-level targeting XML is invalid:\n\n{ex.Message}",
                "Files",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return false;
        }
    }

    private void UpdateActionUi()
    {
        if (_actionCombo is null || _sourceBox is null)
            return;

        var action = ActionCode(
            Convert.ToString(_actionCombo.SelectedItem));

        _sourceBox.IsEnabled = action != "D";
    }

    private void Warn(string message, Control control)
    {
        MessageBox.Show(
            this,
            message,
            "Files",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
        control.Focus();
    }

    private static CheckBox Check(string text, bool value) =>
        new()
        {
            Content = text,
            IsChecked = value,
            Margin = new Thickness(4, 6, 10, 6)
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
}

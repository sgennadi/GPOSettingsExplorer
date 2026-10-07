using System.Windows;
using System.Windows.Controls;
using System.Xml.Linq;
using GPOSettingsExplorer.Models;

namespace GPOSettingsExplorer;

public sealed class GppFolderOptionsEditorWindow : Window
{
    private readonly GppFolderOptionsItemInfo _item;
    private readonly TextBox _nameBox;
    private readonly TextBox _descriptionBox;
    private readonly ComboBox _hiddenFilesCombo;
    private readonly ComboBox _listViewTypingCombo;

    private readonly Dictionary<string, CheckBox> _checks =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly CheckBox _disabledCheck;
    private readonly CheckBox _bypassErrorsCheck;
    private readonly CheckBox _removePolicyCheck;
    private readonly CheckBox _userContextCheck;
    private readonly TextBox _filtersBox;

    public GppFolderOptionsItemInfo Item => _item;

    public GppFolderOptionsEditorWindow(
        GppFolderOptionsItemInfo item)
    {
        _item = item;

        Title = "Folder Options Preference Editor";
        Width = 980;
        Height = 780;
        MinWidth = 760;
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
        var save = new Button
        {
            Content = "Save",
            IsDefault = true,
            IsEnabled = item.SupportsStructuredEditing
        };
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
                $"GPO: {item.GpoName}   |   Scope: {item.Scope} Configuration   |   {item.KindDisplay}",
            FontWeight = FontWeights.SemiBold
        };

        var tabs = new TabControl();

        var viewGrid = CreateGrid(12);

        _nameBox = new TextBox { Text = item.DisplayName };
        _descriptionBox = new TextBox
        {
            Text = item.Description,
            AcceptsReturn = true,
            MinHeight = 64
        };

        _hiddenFilesCombo = Combo(
            new[] { "HIDE", "SHOW" },
            item.HiddenFiles);

        _listViewTypingCombo = Combo(
            new[] { "SELECT", "AUTO" },
            item.ListViewTyping);

        AddRow(viewGrid, 0, "Display name:", _nameBox);
        AddRow(viewGrid, 1, "Description:", _descriptionBox);
        AddCheckRow(viewGrid, 2, "Show drive letters", "ShowDriveLetter", item.ShowDriveLetter);
        AddCheckRow(viewGrid, 3, "Show preview handlers in preview pane", "ShowPreviewHandlers", item.ShowPreviewHandlers);
        AddCheckRow(viewGrid, 4, "Use check boxes to select items", "UseCheckBoxes", item.UseCheckBoxes);
        AddCheckRow(viewGrid, 5, "Use Sharing Wizard", "UseSharingWizard", item.UseSharingWizard);
        AddCheckRow(viewGrid, 6, "Always show icons, never thumbnails", "AlwaysShowIcons", item.AlwaysShowIcons);
        AddCheckRow(viewGrid, 7, "Always show menus", "AlwaysShowMenus", item.AlwaysShowMenus);
        AddRow(viewGrid, 8, "Hidden files:", _hiddenFilesCombo);
        AddCheckRow(viewGrid, 9, "Display file icons on thumbnails", "DisplayIconThumb", item.DisplayIconThumb);
        AddCheckRow(viewGrid, 10, "Display file size information in folder tips", "DisplayFileSize", item.DisplayFileSize);
        AddCheckRow(viewGrid, 11, "Hide extensions for known file types", "HideFileExtensions", item.HideFileExtensions);

        tabs.Items.Add(new TabItem
        {
            Header = "View",
            Content = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = viewGrid
            }
        });

        var advancedGrid = CreateGrid(9);
        AddCheckRow(advancedGrid, 0, "Use simple folder view in navigation pane", "DisplaySimpleFolders", item.DisplaySimpleFolders);
        AddRow(advancedGrid, 1, "When typing into list view:", _listViewTypingCombo);
        AddCheckRow(advancedGrid, 2, "Launch folder windows in a separate process", "SeparateProcess", item.SeparateProcess);
        AddCheckRow(advancedGrid, 3, "Show protected operating system files", "ShowSuperHidden", item.ShowSuperHidden);
        AddCheckRow(advancedGrid, 4, "Use classic view state behavior", "ClassicViewState", item.ClassicViewState);
        AddCheckRow(advancedGrid, 5, "Persist each folder's view state", "PersistBrowsers", item.PersistBrowsers);
        AddCheckRow(advancedGrid, 6, "Show compressed/encrypted NTFS files in color", "ShowCompressedColor", item.ShowCompressedColor);
        AddCheckRow(advancedGrid, 7, "Show pop-up descriptions for folder and desktop items", "ShowInfoTips", item.ShowInfoTips);
        AddCheckRow(advancedGrid, 8, "Display full path in title bar", "FullPath", item.FullPath);

        tabs.Items.Add(new TabItem
        {
            Header = "Advanced",
            Content = advancedGrid
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

        if (!item.SupportsStructuredEditing)
        {
            commonPanel.Children.Add(new Border
            {
                Margin = new Thickness(4, 12, 4, 4),
                Padding = new Thickness(8),
                BorderBrush = System.Windows.SystemColors.ControlDarkBrush,
                BorderThickness = new Thickness(1),
                Child = new TextBlock
                {
                    Text =
                        "This item is a legacy Folder Options or file-association item. It is visible here, but structured rewriting is disabled. Use Show raw XML from the main window.",
                    TextWrapping = TextWrapping.Wrap
                }
            });
        }

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
        if (string.IsNullOrWhiteSpace(_nameBox.Text))
        {
            MessageBox.Show(
                this,
                "Display name cannot be empty.",
                "Folder Options Preference",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            _nameBox.Focus();
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
                    "Folder Options Preference",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }
        }

        _item.DisplayName = _nameBox.Text.Trim();
        _item.Description = _descriptionBox.Text.Trim();
        _item.ShowDriveLetter = Checked("ShowDriveLetter");
        _item.ShowPreviewHandlers = Checked("ShowPreviewHandlers");
        _item.UseCheckBoxes = Checked("UseCheckBoxes");
        _item.UseSharingWizard = Checked("UseSharingWizard");
        _item.AlwaysShowIcons = Checked("AlwaysShowIcons");
        _item.AlwaysShowMenus = Checked("AlwaysShowMenus");
        _item.HiddenFiles =
            Convert.ToString(_hiddenFilesCombo.SelectedItem) ?? "HIDE";
        _item.DisplayIconThumb = Checked("DisplayIconThumb");
        _item.DisplayFileSize = Checked("DisplayFileSize");
        _item.HideFileExtensions = Checked("HideFileExtensions");
        _item.DisplaySimpleFolders = Checked("DisplaySimpleFolders");
        _item.ListViewTyping =
            Convert.ToString(_listViewTypingCombo.SelectedItem) ?? "SELECT";
        _item.SeparateProcess = Checked("SeparateProcess");
        _item.ShowSuperHidden = Checked("ShowSuperHidden");
        _item.ClassicViewState = Checked("ClassicViewState");
        _item.PersistBrowsers = Checked("PersistBrowsers");
        _item.ShowCompressedColor = Checked("ShowCompressedColor");
        _item.ShowInfoTips = Checked("ShowInfoTips");
        _item.FullPath = Checked("FullPath");
        _item.Disabled = _disabledCheck.IsChecked == true;
        _item.BypassErrors = _bypassErrorsCheck.IsChecked == true;
        _item.RemoveWhenNoLongerApplied =
            _removePolicyCheck.IsChecked == true;
        _item.RunInUserContext =
            _userContextCheck.IsChecked == true;
        _item.FiltersXml = _filtersBox.Text.Trim();

        DialogResult = true;
    }

    private bool Checked(string key) =>
        _checks.TryGetValue(key, out var check) &&
        check.IsChecked == true;

    private void AddCheckRow(
        Grid grid,
        int row,
        string text,
        string key,
        bool value)
    {
        var check = Check(text, value);
        _checks[key] = check;
        AddRow(grid, row, string.Empty, check);
    }

    private static ComboBox Combo(
        IEnumerable<string> values,
        string selected)
    {
        var combo = new ComboBox
        {
            ItemsSource = values.ToArray()
        };

        combo.SelectedItem = combo.Items
            .Cast<string>()
            .FirstOrDefault(value =>
                value.Equals(
                    selected,
                    StringComparison.OrdinalIgnoreCase))
            ?? combo.Items.Cast<string>().FirstOrDefault();

        return combo;
    }

    private static Grid CreateGrid(int rows)
    {
        var grid = new Grid { Margin = new Thickness(14) };
        grid.ColumnDefinitions.Add(
            new ColumnDefinition { Width = new GridLength(230) });
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

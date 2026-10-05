using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Xml.Linq;
using GPOSettingsExplorer.Models;

namespace GPOSettingsExplorer;

public sealed class GppLocalGroupEditorWindow : Window
{
    private readonly GppLocalGroupInfo _item;
    private readonly ObservableCollection<GppLocalGroupMemberInfo> _members;

    private readonly TextBox _displayNameBox;
    private readonly TextBox _itemDescriptionBox;
    private readonly ComboBox _actionCombo;
    private readonly TextBox _groupNameBox;
    private readonly TextBox _groupSidBox;
    private readonly TextBox _newNameBox;
    private readonly TextBox _groupDescriptionBox;
    private readonly ComboBox _currentUserActionCombo;
    private readonly CheckBox _removeAccountsCheck;
    private readonly CheckBox _deleteAllUsersCheck;
    private readonly CheckBox _deleteAllGroupsCheck;
    private readonly CheckBox _propertiesDisabledCheck;
    private readonly DataGrid _membersGrid;

    private readonly CheckBox _disabledCheck;
    private readonly CheckBox _bypassErrorsCheck;
    private readonly CheckBox _removePolicyCheck;
    private readonly CheckBox _userContextCheck;
    private readonly TextBox _filtersBox;

    public GppLocalGroupInfo Item => _item;

    public GppLocalGroupEditorWindow(
        GppLocalGroupInfo item)
    {
        _item = item;
        _members = new ObservableCollection<GppLocalGroupMemberInfo>(
            item.Members.Select(member => member.Clone()));

        Title = "Local Group Preference Editor";
        Width = 1080;
        Height = 840;
        MinWidth = 850;
        MinHeight = 650;
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

        var general =
            new StackPanel
            {
                Margin = new Thickness(10)
            };

        var grid =
            new Grid();

        for (var i = 0; i < 8; i++)
        {
            grid.RowDefinitions.Add(
                new RowDefinition
                {
                    Height = GridLength.Auto
                });
        }

        grid.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width = new GridLength(210)
            });

        grid.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width =
                    new GridLength(
                        1,
                        GridUnitType.Star)
            });

        _displayNameBox =
            new TextBox
            {
                Text = item.DisplayName
            };

        _itemDescriptionBox =
            new TextBox
            {
                Text = item.Description
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

        _groupNameBox =
            new TextBox
            {
                Text = item.GroupName,
                ToolTip =
                    "Local group name. Built-in localized groups may also be targeted by SID."
            };

        _groupSidBox =
            new TextBox
            {
                Text = item.GroupSid,
                ToolTip =
                    "Optional group SID. If present, Windows uses the SID in preference to the group name."
            };

        _newNameBox =
            new TextBox
            {
                Text = item.NewName,
                ToolTip =
                    "Optional new group name for Update."
            };

        _groupDescriptionBox =
            new TextBox
            {
                Text = item.GroupDescription,
                AcceptsReturn = true,
                Height = 58
            };

        _currentUserActionCombo =
            new ComboBox
            {
                ItemsSource =
                    new[]
                    {
                        "None",
                        "Add",
                        "Remove"
                    },
                SelectedItem =
                    CurrentUserActionDisplay(
                        item.CurrentUserAction)
            };

        AddRow(
            grid,
            0,
            "Display name:",
            _displayNameBox);

        AddRow(
            grid,
            1,
            "Item description:",
            _itemDescriptionBox);

        AddRow(
            grid,
            2,
            "Action:",
            _actionCombo);

        AddRow(
            grid,
            3,
            "Group name:",
            _groupNameBox);

        AddRow(
            grid,
            4,
            "Group SID:",
            _groupSidBox);

        AddRow(
            grid,
            5,
            "Rename to:",
            _newNameBox);

        AddRow(
            grid,
            6,
            "Group description:",
            _groupDescriptionBox);

        AddRow(
            grid,
            7,
            "Current user:",
            _currentUserActionCombo);

        general.Children.Add(
            grid);

        var behaviorBox =
            new GroupBox
            {
                Header =
                    "Membership behavior",
                Margin =
                    new Thickness(4, 12, 4, 4)
            };

        var behaviorPanel =
            new StackPanel
            {
                Margin =
                    new Thickness(8)
            };

        _removeAccountsCheck =
            Check(
                "Do not add or remove the currently logged-on user",
                item.RemoveAccounts);

        _deleteAllUsersCheck =
            Check(
                "Delete all member users before applying listed members",
                item.DeleteAllUsers);

        _deleteAllGroupsCheck =
            Check(
                "Delete all member groups before applying listed members",
                item.DeleteAllGroups);

        _propertiesDisabledCheck =
            Check(
                "Disable group property changes but still process membership",
                item.PropertiesDisabled);

        behaviorPanel.Children.Add(
            _removeAccountsCheck);
        behaviorPanel.Children.Add(
            _deleteAllUsersCheck);
        behaviorPanel.Children.Add(
            _deleteAllGroupsCheck);
        behaviorPanel.Children.Add(
            _propertiesDisabledCheck);

        behaviorBox.Content =
            behaviorPanel;

        general.Children.Add(
            behaviorBox);

        general.Children.Add(
            new TextBlock
            {
                Text =
                    "If Group SID is specified it takes precedence over the name. " +
                    "Delete-all options can remove existing local group membership; a full GPO backup is created before the change.",
                TextWrapping =
                    TextWrapping.Wrap,
                Foreground =
                    System.Windows.Media.Brushes.DimGray,
                Margin =
                    new Thickness(4, 10, 4, 4)
            });

        tabs.Items.Add(
            new TabItem
            {
                Header = "Group",
                Content =
                    new ScrollViewer
                    {
                        VerticalScrollBarVisibility =
                            ScrollBarVisibility.Auto,
                        Content = general
                    }
            });

        var membersRoot =
            new DockPanel
            {
                Margin = new Thickness(10)
            };

        var memberButtons =
            new StackPanel
            {
                Orientation =
                    Orientation.Horizontal
            };
        DockPanel.SetDock(
            memberButtons,
            Dock.Top);

        var addMember =
            new Button
            {
                Content = "Add member"
            };
        addMember.Click +=
            (_, _) =>
            {
                var member =
                    new GppLocalGroupMemberInfo
                    {
                        Action = "ADD"
                    };

                _members.Add(
                    member);

                _membersGrid.SelectedItem =
                    member;

                _membersGrid.ScrollIntoView(
                    member);
            };

        var removeMember =
            new Button
            {
                Content = "Remove selected row"
            };
        removeMember.Click +=
            (_, _) =>
            {
                if (_membersGrid.SelectedItem
                    is GppLocalGroupMemberInfo selected)
                {
                    _members.Remove(
                        selected);
                }
            };

        memberButtons.Children.Add(
            addMember);
        memberButtons.Children.Add(
            removeMember);

        _membersGrid =
            new DataGrid
            {
                AutoGenerateColumns =
                    false,
                CanUserAddRows =
                    false,
                CanUserDeleteRows =
                    false,
                ItemsSource =
                    _members
            };

        _membersGrid.Columns.Add(
            new DataGridTextColumn
            {
                Header = "Name",
                Binding =
                    new Binding(
                        nameof(
                            GppLocalGroupMemberInfo.Name))
                    {
                        UpdateSourceTrigger =
                            UpdateSourceTrigger.PropertyChanged
                    },
                Width =
                    new DataGridLength(
                        2,
                        DataGridLengthUnitType.Star)
            });

        _membersGrid.Columns.Add(
            new DataGridComboBoxColumn
            {
                Header = "Action",
                ItemsSource =
                    new[]
                    {
                        "ADD",
                        "REMOVE"
                    },
                SelectedItemBinding =
                    new Binding(
                        nameof(
                            GppLocalGroupMemberInfo.Action))
                    {
                        UpdateSourceTrigger =
                            UpdateSourceTrigger.PropertyChanged
                    },
                Width =
                    new DataGridLength(
                        110)
            });

        _membersGrid.Columns.Add(
            new DataGridTextColumn
            {
                Header = "SID",
                Binding =
                    new Binding(
                        nameof(
                            GppLocalGroupMemberInfo.Sid))
                    {
                        UpdateSourceTrigger =
                            UpdateSourceTrigger.PropertyChanged
                    },
                Width =
                    new DataGridLength(
                        2,
                        DataGridLengthUnitType.Star)
            });

        membersRoot.Children.Add(
            memberButtons);

        membersRoot.Children.Add(
            _membersGrid);

        tabs.Items.Add(
            new TabItem
            {
                Header =
                    $"Members ({_members.Count})",
                Content =
                    membersRoot
            });

        var common =
            new StackPanel
            {
                Margin = new Thickness(14)
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

        common.Children.Add(
            _disabledCheck);
        common.Children.Add(
            _bypassErrorsCheck);
        common.Children.Add(
            _removePolicyCheck);
        common.Children.Add(
            _userContextCheck);

        tabs.Items.Add(
            new TabItem
            {
                Header = "Common",
                Content = common
            });

        var targeting =
            new DockPanel
            {
                Margin =
                    new Thickness(10)
            };

        var targetingHelp =
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
            targetingHelp,
            Dock.Top);

        targeting.Children.Add(
            targetingHelp);

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

        Content =
            root;
    }

    private void Save_Click(
        object sender,
        RoutedEventArgs e)
    {
        _membersGrid.CommitEdit(
            DataGridEditingUnit.Cell,
            true);
        _membersGrid.CommitEdit(
            DataGridEditingUnit.Row,
            true);

        var groupName =
            _groupNameBox.Text.Trim();

        var groupSid =
            _groupSidBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(
                groupName) &&
            string.IsNullOrWhiteSpace(
                groupSid))
        {
            MessageBox.Show(
                this,
                "Enter a local group name or a group SID.",
                "Local Group",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            _groupNameBox.Focus();
            return;
        }

        foreach (var member
                 in _members)
        {
            if (string.IsNullOrWhiteSpace(
                    member.Name) &&
                string.IsNullOrWhiteSpace(
                    member.Sid))
            {
                MessageBox.Show(
                    this,
                    "Every member row must contain a name or SID.",
                    "Local Group",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }
        }

        if (!ValidateFilters())
            return;

        if ((_deleteAllUsersCheck.IsChecked == true ||
             _deleteAllGroupsCheck.IsChecked == true) &&
            MessageBox.Show(
                this,
                "Delete-all membership options are enabled. Existing local group membership may be removed before the listed members are applied. Continue?",
                "Confirm Membership Replacement",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        _item.DisplayName =
            string.IsNullOrWhiteSpace(
                _displayNameBox.Text)
                ? (!string.IsNullOrWhiteSpace(
                        groupName)
                    ? groupName
                    : groupSid)
                : _displayNameBox.Text.Trim();

        _item.Description =
            _itemDescriptionBox.Text;

        _item.Action =
            ActionCode(
                Convert.ToString(
                    _actionCombo.SelectedItem));

        _item.GroupName =
            groupName;

        _item.GroupSid =
            groupSid;

        _item.NewName =
            _newNameBox.Text.Trim();

        _item.GroupDescription =
            _groupDescriptionBox.Text;

        _item.CurrentUserAction =
            CurrentUserActionCode(
                Convert.ToString(
                    _currentUserActionCombo.SelectedItem));

        _item.RemoveAccounts =
            _removeAccountsCheck.IsChecked == true;

        _item.DeleteAllUsers =
            _deleteAllUsersCheck.IsChecked == true;

        _item.DeleteAllGroups =
            _deleteAllGroupsCheck.IsChecked == true;

        _item.PropertiesDisabled =
            _propertiesDisabledCheck.IsChecked == true;

        _item.Members =
            _members
                .Select(
                    member =>
                        member.Clone())
                .ToList();

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
                "Local Group",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return false;
        }
    }

    private static CheckBox Check(
        string text,
        bool value) =>
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
        var text =
            new TextBlock
            {
                Text = label,
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

    private static string CurrentUserActionDisplay(
        string action) =>
        action.Trim()
            .ToUpperInvariant() switch
        {
            "ADD" => "Add",
            "REMOVE" => "Remove",
            _ => "None"
        };

    private static string CurrentUserActionCode(
        string? action) =>
        action?.Trim()
            .ToUpperInvariant() switch
        {
            "ADD" => "ADD",
            "REMOVE" => "REMOVE",
            _ => string.Empty
        };
}

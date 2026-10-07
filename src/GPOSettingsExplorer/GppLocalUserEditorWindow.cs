using System.Windows;
using System.Windows.Controls;
using System.Xml.Linq;
using GPOSettingsExplorer.Models;

namespace GPOSettingsExplorer;

public sealed class GppLocalUserEditorWindow : Window
{
    private readonly GppLocalUserInfo _item;

    private readonly TextBox _displayNameBox;
    private readonly TextBox _itemDescriptionBox;
    private readonly ComboBox _actionCombo;
    private readonly TextBox _userNameBox;
    private readonly TextBox _newNameBox;
    private readonly TextBox _fullNameBox;
    private readonly TextBox _accountDescriptionBox;
    private readonly TextBox _expiresBox;

    private readonly CheckBox _changeLogonCheck;
    private readonly CheckBox _noChangeCheck;
    private readonly CheckBox _neverExpiresCheck;
    private readonly CheckBox _accountDisabledCheck;
    private readonly CheckBox _clearCredentialCheck;

    private readonly CheckBox _disabledCheck;
    private readonly CheckBox _bypassErrorsCheck;
    private readonly CheckBox _removePolicyCheck;
    private readonly CheckBox _userContextCheck;
    private readonly TextBox _filtersBox;

    public GppLocalUserInfo Item => _item;

    public GppLocalUserEditorWindow(
        GppLocalUserInfo item)
    {
        _item = item;

        Title = "Local User Preference Editor";
        Width = 1000;
        Height = 800;
        MinWidth = 800;
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
            Padding =
                new Thickness(6),
            Margin =
                new Thickness(0, 0, 0, 8)
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

        var tabs =
            new TabControl();

        var general =
            new StackPanel
            {
                Margin =
                    new Thickness(10)
            };

        var grid =
            new Grid();

        for (var i = 0;
             i < 8;
             i++)
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
                    new GridLength(200)
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
                Text =
                    item.DisplayName
            };

        _itemDescriptionBox =
            new TextBox
            {
                Text =
                    item.Description
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

        _userNameBox =
            new TextBox
            {
                Text =
                    item.UserName,
                ToolTip =
                    "Local account name only. Do not enter a domain prefix."
            };

        _newNameBox =
            new TextBox
            {
                Text =
                    item.NewName,
                ToolTip =
                    "Optional new local account name. Applies to Update."
            };

        _fullNameBox =
            new TextBox
            {
                Text =
                    item.FullName
            };

        _accountDescriptionBox =
            new TextBox
            {
                Text =
                    item.AccountDescription,
                AcceptsReturn =
                    true,
                MinHeight =
                    58
            };

        _expiresBox =
            new TextBox
            {
                Text =
                    item.Expires,
                ToolTip =
                    "Optional native GPP expires value. Existing values are preserved as text."
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
            "User name:",
            _userNameBox);

        AddRow(
            grid,
            4,
            "Rename to:",
            _newNameBox);

        AddRow(
            grid,
            5,
            "Full name:",
            _fullNameBox);

        AddRow(
            grid,
            6,
            "Account description:",
            _accountDescriptionBox);

        AddRow(
            grid,
            7,
            "Expires:",
            _expiresBox);

        general.Children.Add(
            grid);

        var flagsBox =
            new GroupBox
            {
                Header =
                    "Account options",
                Margin =
                    new Thickness(4, 12, 4, 4)
            };

        var flagsPanel =
            new StackPanel
            {
                Margin =
                    new Thickness(8)
            };

        _changeLogonCheck =
            Check(
                "User must change password at next logon",
                item.ChangePasswordAtLogon);

        _noChangeCheck =
            Check(
                "User cannot change password",
                item.UserCannotChangePassword);

        _neverExpiresCheck =
            Check(
                "Password never expires",
                item.PasswordNeverExpires);

        _accountDisabledCheck =
            Check(
                "Account is disabled",
                item.AccountDisabled);

        flagsPanel.Children.Add(
            _changeLogonCheck);
        flagsPanel.Children.Add(
            _noChangeCheck);
        flagsPanel.Children.Add(
            _neverExpiresCheck);
        flagsPanel.Children.Add(
            _accountDisabledCheck);

        flagsBox.Content =
            flagsPanel;

        general.Children.Add(
            flagsBox);

        var credentialBox =
            new GroupBox
            {
                Header =
                    "Legacy stored credential",
                Margin =
                    new Thickness(4, 12, 4, 4)
            };

        var credentialPanel =
            new StackPanel
            {
                Margin =
                    new Thickness(8)
            };

        credentialPanel.Children.Add(
            new TextBlock
            {
                Text =
                    item.HasStoredCredential
                        ? "This existing preference contains legacy GPP cpassword data. GPO Settings Explorer preserves it opaquely and never decrypts or displays it."
                        : "No cpassword is stored. GPO Settings Explorer does not create new legacy GPP password data.",
                TextWrapping =
                    TextWrapping.Wrap
            });

        _clearCredentialCheck =
            new CheckBox
            {
                Content =
                    "Remove the stored legacy credential when saving",
                IsChecked =
                    false,
                IsEnabled =
                    item.HasStoredCredential,
                Margin =
                    new Thickness(0, 8, 0, 0)
            };

        credentialPanel.Children.Add(
            _clearCredentialCheck);

        credentialBox.Content =
            credentialPanel;

        general.Children.Add(
            credentialBox);

        tabs.Items.Add(
            new TabItem
            {
                Header =
                    "Local User",
                Content =
                    new ScrollViewer
                    {
                        VerticalScrollBarVisibility =
                            ScrollBarVisibility.Auto,
                        Content =
                            general
                    }
            });

        var common =
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
                Header =
                    "Common",
                Content =
                    common
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
        var userName =
            _userNameBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(
                userName))
        {
            Warn(
                "Local user name cannot be empty.",
                _userNameBox);
            return;
        }

        if (userName.Contains(
                '\\'))
        {
            Warn(
                "Enter a local user name without a domain prefix.",
                _userNameBox);
            return;
        }

        if (!ValidateFilters())
            return;

        _item.DisplayName =
            string.IsNullOrWhiteSpace(
                _displayNameBox.Text)
                ? userName
                : _displayNameBox.Text.Trim();

        _item.Description =
            _itemDescriptionBox.Text;

        _item.Action =
            ActionCode(
                Convert.ToString(
                    _actionCombo.SelectedItem));

        _item.UserName =
            userName;

        _item.NewName =
            _newNameBox.Text.Trim();

        _item.FullName =
            _fullNameBox.Text.Trim();

        _item.AccountDescription =
            _accountDescriptionBox.Text;

        _item.Expires =
            _expiresBox.Text.Trim();

        _item.ChangePasswordAtLogon =
            _changeLogonCheck.IsChecked == true;

        _item.UserCannotChangePassword =
            _noChangeCheck.IsChecked == true;

        _item.PasswordNeverExpires =
            _neverExpiresCheck.IsChecked == true;

        _item.AccountDisabled =
            _accountDisabledCheck.IsChecked == true;

        _item.ClearStoredCredential =
            _clearCredentialCheck.IsChecked == true;

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
                "Local User",
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
            "Local User",
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

using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Xml.Linq;
using GPOSettingsExplorer.Models;

namespace GPOSettingsExplorer;

public sealed class GppPrinterEditorWindow : Window
{
    private readonly GppPrinterItemInfo _item;

    private readonly TextBox _displayNameBox;
    private readonly TextBox _descriptionBox;
    private readonly ComboBox _kindCombo;
    private readonly ComboBox _actionCombo;

    private readonly Grid _sharedPanel;
    private readonly TextBox _sharedPathBox;
    private readonly TextBox _sharedPortBox;
    private readonly TextBox _sharedLocationBox;
    private readonly TextBox _sharedCommentBox;
    private readonly CheckBox _sharedDefaultCheck;
    private readonly CheckBox _sharedSkipLocalCheck;
    private readonly CheckBox _sharedDeleteAllCheck;
    private readonly CheckBox _sharedPersistentCheck;
    private readonly CheckBox _sharedDeleteMapsCheck;
    private readonly TextBox _sharedUserNameBox;
    private readonly CheckBox _clearCredentialCheck;

    private readonly Grid _portPanel;
    private readonly TextBox _ipAddressBox;
    private readonly CheckBox _useDnsCheck;
    private readonly TextBox _localNameBox;
    private readonly TextBox _portDriverPathBox;
    private readonly TextBox _portLocationBox;
    private readonly TextBox _portCommentBox;
    private readonly CheckBox _portDefaultCheck;
    private readonly CheckBox _portSkipLocalCheck;
    private readonly CheckBox _portDeleteAllCheck;
    private readonly ComboBox _protocolCombo;
    private readonly TextBox _portNumberBox;
    private readonly TextBox _lprQueueBox;
    private readonly TextBox _snmpCommunityBox;
    private readonly CheckBox _doubleSpoolCheck;
    private readonly CheckBox _snmpEnabledCheck;
    private readonly TextBox _snmpIndexBox;

    private readonly Grid _localPanel;
    private readonly TextBox _printerNameBox;
    private readonly TextBox _localPortBox;
    private readonly TextBox _localDriverPathBox;
    private readonly TextBox _localLocationBox;
    private readonly TextBox _localCommentBox;
    private readonly CheckBox _localDefaultCheck;
    private readonly CheckBox _localDeleteAllCheck;

    private readonly CheckBox _disabledCheck;
    private readonly CheckBox _bypassErrorsCheck;
    private readonly CheckBox _removePolicyCheck;
    private readonly CheckBox _userContextCheck;
    private readonly TextBox _filtersBox;

    public GppPrinterItemInfo Item => _item;

    public GppPrinterEditorWindow(
        GppPrinterItemInfo item)
    {
        _item = item;

        Title = "Printer Preference Editor";
        Width = 1060;
        Height = 850;
        MinWidth = 820;
        MinHeight = 650;
        WindowStartupLocation =
            WindowStartupLocation.CenterOwner;

        var root = new DockPanel
        {
            Margin = new Thickness(12)
        };

        var footer = new StackPanel
        {
            Orientation =
                Orientation.Horizontal,
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

        var printerRoot =
            new StackPanel
            {
                Margin =
                    new Thickness(10)
            };

        var baseGrid =
            CreateGrid(4, 190);

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
                    58
            };

        _kindCombo =
            new ComboBox
            {
                ItemsSource =
                    new[]
                    {
                        "Shared",
                        "TCP/IP",
                        "Local"
                    },
                SelectedItem =
                    KindDisplay(
                        item.PrinterKind),
                IsEnabled =
                    item.IsNew
            };

        _kindCombo.SelectionChanged +=
            (_, _) =>
                UpdateKindUi();

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

        AddRow(
            baseGrid,
            0,
            "Display name:",
            _displayNameBox);

        AddRow(
            baseGrid,
            1,
            "Item description:",
            _descriptionBox);

        AddRow(
            baseGrid,
            2,
            "Printer type:",
            _kindCombo);

        AddRow(
            baseGrid,
            3,
            "Action:",
            _actionCombo);

        printerRoot.Children.Add(
            baseGrid);

        _sharedPanel =
            CreateGrid(
                11,
                200);

        _sharedPathBox =
            new TextBox
            {
                Text =
                    item.Path,
                ToolTip =
                    @"Fully qualified shared printer path, for example \\printserver\queue."
            };

        _sharedPortBox =
            new TextBox
            {
                Text =
                    item.Port,
                ToolTip =
                    "Optional local port mapping."
            };

        _sharedLocationBox =
            new TextBox
            {
                Text =
                    item.Location
            };

        _sharedCommentBox =
            new TextBox
            {
                Text =
                    item.Comment
            };

        _sharedDefaultCheck =
            Check(
                "Set as default printer",
                item.DefaultPrinter);

        _sharedSkipLocalCheck =
            Check(
                "Do not change default printer when a local printer is configured",
                item.SkipLocal);

        _sharedDeleteAllCheck =
            Check(
                "Delete all shared printer connections",
                item.DeleteAll);

        _sharedPersistentCheck =
            Check(
                "Persistent connection",
                item.Persistent);

        _sharedDeleteMapsCheck =
            Check(
                "Allow deletion of shared connections from all local ports",
                item.DeleteMaps);

        _sharedUserNameBox =
            new TextBox
            {
                Text =
                    item.UserName,
                ToolTip =
                    @"Optional account used to connect to the printer driver share."
            };

        _clearCredentialCheck =
            new CheckBox
            {
                Content =
                    item.HasStoredCredential
                        ? "Remove existing legacy cpassword when saving"
                        : "No stored legacy cpassword",
                IsChecked =
                    false,
                IsEnabled =
                    item.HasStoredCredential,
                Margin =
                    new Thickness(4, 6, 4, 6)
            };

        AddRow(
            _sharedPanel,
            0,
            "Shared printer path:",
            _sharedPathBox);

        AddRow(
            _sharedPanel,
            1,
            "Local port:",
            _sharedPortBox);

        AddRow(
            _sharedPanel,
            2,
            "Location:",
            _sharedLocationBox);

        AddRow(
            _sharedPanel,
            3,
            "Comment:",
            _sharedCommentBox);

        AddRow(
            _sharedPanel,
            4,
            "Default printer:",
            _sharedDefaultCheck);

        AddRow(
            _sharedPanel,
            5,
            "Default behavior:",
            _sharedSkipLocalCheck);

        AddRow(
            _sharedPanel,
            6,
            "Bulk delete:",
            _sharedDeleteAllCheck);

        AddRow(
            _sharedPanel,
            7,
            "Connection:",
            _sharedPersistentCheck);

        AddRow(
            _sharedPanel,
            8,
            "Delete mappings:",
            _sharedDeleteMapsCheck);

        AddRow(
            _sharedPanel,
            9,
            "Connect as:",
            _sharedUserNameBox);

        AddRow(
            _sharedPanel,
            10,
            "Legacy credential:",
            _clearCredentialCheck);

        var sharedBox =
            new GroupBox
            {
                Header =
                    "Shared printer",
                Content =
                    _sharedPanel,
                Margin =
                    new Thickness(4, 12, 4, 4)
            };

        printerRoot.Children.Add(
            sharedBox);

        _portPanel =
            CreateGrid(
                16,
                200);

        _ipAddressBox =
            new TextBox
            {
                Text =
                    item.IpAddress,
                ToolTip =
                    "Printer IP address, or DNS name when Use DNS is enabled."
            };

        _useDnsCheck =
            Check(
                "Treat address as DNS name",
                item.UseDns);

        _localNameBox =
            new TextBox
            {
                Text =
                    item.LocalName
            };

        _portDriverPathBox =
            new TextBox
            {
                Text =
                    item.Path,
                ToolTip =
                    @"UNC shared-printer path used as the driver installation source."
            };

        _portLocationBox =
            new TextBox
            {
                Text =
                    item.Location
            };

        _portCommentBox =
            new TextBox
            {
                Text =
                    item.Comment
            };

        _portDefaultCheck =
            Check(
                "Set as default printer",
                item.DefaultPrinter);

        _portSkipLocalCheck =
            Check(
                "Do not change default printer when a local printer is configured",
                item.SkipLocal);

        _portDeleteAllCheck =
            Check(
                "Delete all TCP/IP printer connections",
                item.DeleteAll);

        _protocolCombo =
            new ComboBox
            {
                ItemsSource =
                    new[]
                    {
                        "RAW TCP",
                        "LPR"
                    },
                SelectedItem =
                    ProtocolDisplay(
                        item.Protocol)
            };

        _protocolCombo.SelectionChanged +=
            (_, _) =>
                UpdateProtocolUi();

        _portNumberBox =
            new TextBox
            {
                Text =
                    string.IsNullOrWhiteSpace(
                        item.PortNumber)
                        ? "9100"
                        : item.PortNumber
            };

        _lprQueueBox =
            new TextBox
            {
                Text =
                    item.LprQueue
            };

        _snmpCommunityBox =
            new TextBox
            {
                Text =
                    item.SnmpCommunity
            };

        _doubleSpoolCheck =
            Check(
                "Enable double spooling",
                item.DoubleSpool);

        _snmpEnabledCheck =
            Check(
                "Enable SNMP",
                item.SnmpEnabled);

        _snmpIndexBox =
            new TextBox
            {
                Text =
                    string.IsNullOrWhiteSpace(
                        item.SnmpDevIndex)
                        ? "1"
                        : item.SnmpDevIndex
            };

        AddRow(
            _portPanel,
            0,
            "IP / DNS name:",
            _ipAddressBox);

        AddRow(
            _portPanel,
            1,
            "Address type:",
            _useDnsCheck);

        AddRow(
            _portPanel,
            2,
            "Local printer name:",
            _localNameBox);

        AddRow(
            _portPanel,
            3,
            "Driver source:",
            _portDriverPathBox);

        AddRow(
            _portPanel,
            4,
            "Location:",
            _portLocationBox);

        AddRow(
            _portPanel,
            5,
            "Comment:",
            _portCommentBox);

        AddRow(
            _portPanel,
            6,
            "Default printer:",
            _portDefaultCheck);

        AddRow(
            _portPanel,
            7,
            "Default behavior:",
            _portSkipLocalCheck);

        AddRow(
            _portPanel,
            8,
            "Bulk delete:",
            _portDeleteAllCheck);

        AddRow(
            _portPanel,
            9,
            "Protocol:",
            _protocolCombo);

        AddRow(
            _portPanel,
            10,
            "TCP port:",
            _portNumberBox);

        AddRow(
            _portPanel,
            11,
            "LPR queue:",
            _lprQueueBox);

        AddRow(
            _portPanel,
            12,
            "SNMP community:",
            _snmpCommunityBox);

        AddRow(
            _portPanel,
            13,
            "Spooling:",
            _doubleSpoolCheck);

        AddRow(
            _portPanel,
            14,
            "SNMP:",
            _snmpEnabledCheck);

        AddRow(
            _portPanel,
            15,
            "SNMP device index:",
            _snmpIndexBox);

        var portBox =
            new GroupBox
            {
                Header =
                    "TCP/IP printer",
                Content =
                    _portPanel,
                Margin =
                    new Thickness(4, 12, 4, 4)
            };

        printerRoot.Children.Add(
            portBox);

        _localPanel =
            CreateGrid(
                7,
                200);

        _printerNameBox =
            new TextBox
            {
                Text =
                    item.PrinterName
            };

        _localPortBox =
            new TextBox
            {
                Text =
                    item.Port,
                ToolTip =
                    "Local printer port such as LPT1:."
            };

        _localDriverPathBox =
            new TextBox
            {
                Text =
                    item.Path,
                ToolTip =
                    @"UNC shared-printer path used as the driver installation source."
            };

        _localLocationBox =
            new TextBox
            {
                Text =
                    item.Location
            };

        _localCommentBox =
            new TextBox
            {
                Text =
                    item.Comment
            };

        _localDefaultCheck =
            Check(
                "Set as default printer",
                item.DefaultPrinter);

        _localDeleteAllCheck =
            Check(
                "Delete all local printers",
                item.DeleteAll);

        AddRow(
            _localPanel,
            0,
            "Printer name:",
            _printerNameBox);

        AddRow(
            _localPanel,
            1,
            "Local port:",
            _localPortBox);

        AddRow(
            _localPanel,
            2,
            "Driver source:",
            _localDriverPathBox);

        AddRow(
            _localPanel,
            3,
            "Location:",
            _localLocationBox);

        AddRow(
            _localPanel,
            4,
            "Comment:",
            _localCommentBox);

        AddRow(
            _localPanel,
            5,
            "Default printer:",
            _localDefaultCheck);

        AddRow(
            _localPanel,
            6,
            "Bulk delete:",
            _localDeleteAllCheck);

        var localBox =
            new GroupBox
            {
                Header =
                    "Local printer",
                Content =
                    _localPanel,
                Margin =
                    new Thickness(4, 12, 4, 4)
            };

        printerRoot.Children.Add(
            localBox);

        tabs.Items.Add(
            new TabItem
            {
                Header =
                    "Printer",
                Content =
                    new ScrollViewer
                    {
                        VerticalScrollBarVisibility =
                            ScrollBarVisibility.Auto,
                        Content =
                            printerRoot
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

        common.Children.Add(
            new TextBlock
            {
                Text =
                    "Existing legacy shared-printer cpassword data is preserved only during an edit and is never shown or decrypted. New and cloned printer preferences never receive a copied cpassword.",
                TextWrapping =
                    TextWrapping.Wrap,
                Foreground =
                    System.Windows.Media.Brushes.DimGray,
                Margin =
                    new Thickness(4, 14, 4, 4)
            });

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

        UpdateKindUi();
        UpdateProtocolUi();
    }

    private void Save_Click(
        object sender,
        RoutedEventArgs e)
    {
        var kind =
            KindDisplay(
                Convert.ToString(
                    _kindCombo.SelectedItem));

        var action =
            ActionCode(
                Convert.ToString(
                    _actionCombo.SelectedItem));

        if (kind == "Shared")
        {
            var path =
                _sharedPathBox.Text.Trim();

            if (action != "D" &&
                string.IsNullOrWhiteSpace(
                    path))
            {
                Warn(
                    "Shared printer path cannot be empty.",
                    _sharedPathBox);
                return;
            }

            if (!string.IsNullOrWhiteSpace(
                    path) &&
                !path.StartsWith(
                    @"\\",
                    StringComparison.Ordinal))
            {
                Warn(
                    "Shared printer path must be a UNC path beginning with \\.",
                    _sharedPathBox);
                return;
            }
        }
        else if (kind == "TCP/IP")
        {
            if (action != "D" &&
                string.IsNullOrWhiteSpace(
                    _ipAddressBox.Text))
            {
                Warn(
                    "Enter the printer IP address or DNS name.",
                    _ipAddressBox);
                return;
            }

            if (action != "D" &&
                string.IsNullOrWhiteSpace(
                    _portDriverPathBox.Text))
            {
                Warn(
                    "Enter the UNC shared-printer path used for driver installation.",
                    _portDriverPathBox);
                return;
            }

            if (!string.IsNullOrWhiteSpace(
                    _portDriverPathBox.Text) &&
                !_portDriverPathBox.Text.Trim().StartsWith(
                    @"\\",
                    StringComparison.Ordinal))
            {
                Warn(
                    "Driver source must be a UNC printer path beginning with \\.",
                    _portDriverPathBox);
                return;
            }

            if (!ValidateInteger(
                    _portNumberBox,
                    "TCP/IP printer port number"))
                return;

            if (!ValidateInteger(
                    _snmpIndexBox,
                    "SNMP device index"))
                return;

            if (ProtocolCode(
                    Convert.ToString(
                        _protocolCombo.SelectedItem)) ==
                "PROTOCOL_LPR_TYPE" &&
                _portNumberBox.Text.Trim() != "515")
            {
                Warn(
                    "LPR protocol requires TCP port 515.",
                    _portNumberBox);
                return;
            }
        }
        else
        {
            if (string.IsNullOrWhiteSpace(
                    _printerNameBox.Text))
            {
                Warn(
                    "Local printer name cannot be empty.",
                    _printerNameBox);
                return;
            }

            if (action != "D" &&
                string.IsNullOrWhiteSpace(
                    _localPortBox.Text))
            {
                Warn(
                    "Local printer port cannot be empty.",
                    _localPortBox);
                return;
            }

            if (action != "D" &&
                string.IsNullOrWhiteSpace(
                    _localDriverPathBox.Text))
            {
                Warn(
                    "Enter the UNC shared-printer path used for driver installation.",
                    _localDriverPathBox);
                return;
            }

            if (!string.IsNullOrWhiteSpace(
                    _localDriverPathBox.Text) &&
                !_localDriverPathBox.Text.Trim().StartsWith(
                    @"\\",
                    StringComparison.Ordinal))
            {
                Warn(
                    "Driver source must be a UNC printer path beginning with \\.",
                    _localDriverPathBox);
                return;
            }
        }

        if (!ValidateFilters())
            return;

        _item.DisplayName =
            _displayNameBox.Text.Trim();

        _item.Description =
            _descriptionBox.Text;

        _item.PrinterKind =
            kind;

        _item.Action =
            action;

        if (kind == "Shared")
        {
            _item.Path =
                _sharedPathBox.Text.Trim();
            _item.Port =
                _sharedPortBox.Text.Trim();
            _item.Location =
                _sharedLocationBox.Text;
            _item.Comment =
                _sharedCommentBox.Text;
            _item.DefaultPrinter =
                _sharedDefaultCheck.IsChecked == true;
            _item.SkipLocal =
                _sharedSkipLocalCheck.IsChecked == true;
            _item.DeleteAll =
                _sharedDeleteAllCheck.IsChecked == true;
            _item.Persistent =
                _sharedPersistentCheck.IsChecked == true;
            _item.DeleteMaps =
                _sharedDeleteMapsCheck.IsChecked == true;
            _item.UserName =
                _sharedUserNameBox.Text.Trim();
            _item.ClearStoredCredential =
                _clearCredentialCheck.IsChecked == true;
        }
        else if (kind == "TCP/IP")
        {
            _item.IpAddress =
                _ipAddressBox.Text.Trim();
            _item.UseDns =
                _useDnsCheck.IsChecked == true;
            _item.LocalName =
                _localNameBox.Text.Trim();
            _item.Path =
                _portDriverPathBox.Text.Trim();
            _item.Location =
                _portLocationBox.Text;
            _item.Comment =
                _portCommentBox.Text;
            _item.DefaultPrinter =
                _portDefaultCheck.IsChecked == true;
            _item.SkipLocal =
                _portSkipLocalCheck.IsChecked == true;
            _item.DeleteAll =
                _portDeleteAllCheck.IsChecked == true;
            _item.Protocol =
                ProtocolCode(
                    Convert.ToString(
                        _protocolCombo.SelectedItem));
            _item.PortNumber =
                _portNumberBox.Text.Trim();
            _item.LprQueue =
                _lprQueueBox.Text.Trim();
            _item.SnmpCommunity =
                _snmpCommunityBox.Text.Trim();
            _item.DoubleSpool =
                _doubleSpoolCheck.IsChecked == true;
            _item.SnmpEnabled =
                _snmpEnabledCheck.IsChecked == true;
            _item.SnmpDevIndex =
                _snmpIndexBox.Text.Trim();
            _item.OpaqueCredential =
                string.Empty;
            _item.ClearStoredCredential =
                false;
        }
        else
        {
            _item.PrinterName =
                _printerNameBox.Text.Trim();
            _item.Port =
                _localPortBox.Text.Trim();
            _item.Path =
                _localDriverPathBox.Text.Trim();
            _item.Location =
                _localLocationBox.Text;
            _item.Comment =
                _localCommentBox.Text;
            _item.DefaultPrinter =
                _localDefaultCheck.IsChecked == true;
            _item.DeleteAll =
                _localDeleteAllCheck.IsChecked == true;
            _item.OpaqueCredential =
                string.Empty;
            _item.ClearStoredCredential =
                false;
        }

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

    private void UpdateKindUi()
    {
        if (_sharedPanel is null ||
            _portPanel is null ||
            _localPanel is null ||
            _kindCombo is null)
            return;

        var kind =
            KindDisplay(
                Convert.ToString(
                    _kindCombo.SelectedItem));

        _sharedPanel.Visibility =
            kind == "Shared"
                ? Visibility.Visible
                : Visibility.Collapsed;

        _portPanel.Visibility =
            kind == "TCP/IP"
                ? Visibility.Visible
                : Visibility.Collapsed;

        _localPanel.Visibility =
            kind == "Local"
                ? Visibility.Visible
                : Visibility.Collapsed;
    }

    private void UpdateProtocolUi()
    {
        if (_protocolCombo is null ||
            _portNumberBox is null ||
            _lprQueueBox is null)
            return;

        var lpr =
            ProtocolCode(
                Convert.ToString(
                    _protocolCombo.SelectedItem)) ==
            "PROTOCOL_LPR_TYPE";

        _lprQueueBox.IsEnabled =
            lpr;

        if (lpr &&
            (_portNumberBox.Text.Trim() == "9100" ||
             string.IsNullOrWhiteSpace(
                 _portNumberBox.Text)))
        {
            _portNumberBox.Text =
                "515";
        }
        else if (!lpr &&
                 (_portNumberBox.Text.Trim() == "515" ||
                  string.IsNullOrWhiteSpace(
                      _portNumberBox.Text)))
        {
            _portNumberBox.Text =
                "9100";
        }
    }

    private bool ValidateInteger(
        TextBox box,
        string fieldName)
    {
        if (!int.TryParse(
                box.Text.Trim(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var number) ||
            number < 0)
        {
            Warn(
                $"{fieldName} must be a non-negative integer.",
                box);

            return false;
        }

        return true;
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
                "Printer",
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
            "Printer",
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

        for (var i = 0;
             i < rows;
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

    private static string KindDisplay(
        string? kind) =>
        kind?.Trim()
            .ToUpperInvariant() switch
        {
            "TCP/IP" or "TCPIP" or "PORT" =>
                "TCP/IP",
            "LOCAL" =>
                "Local",
            _ =>
                "Shared"
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

    private static string ProtocolDisplay(
        string protocol) =>
        protocol.Trim()
            .ToUpperInvariant() switch
        {
            "PROTOCOL_LPR_TYPE" or "LPR" =>
                "LPR",
            _ =>
                "RAW TCP"
        };

    private static string ProtocolCode(
        string? protocol) =>
        protocol?.Trim()
            .ToUpperInvariant() switch
        {
            "LPR" or "PROTOCOL_LPR_TYPE" =>
                "PROTOCOL_LPR_TYPE",
            _ =>
                "PROTOCOL_RAWTCP_TYPE"
        };
}

using System.Windows;
using System.Windows.Controls;
using System.Xml.Linq;
using GPOSettingsExplorer.Models;
using GPOSettingsExplorer.Services;

namespace GPOSettingsExplorer;

public sealed class GppScheduledTaskEditorWindow : Window
{
    private readonly GppScheduledTaskInfo _item;
    private readonly GppScheduledTaskService _service;

    private readonly TextBox _displayNameBox;
    private readonly TextBox _itemDescriptionBox;
    private readonly ComboBox _actionCombo;
    private readonly TextBox _taskNameBox;

    private readonly TextBox _runAsBox;
    private readonly ComboBox _logonTypeCombo;
    private readonly ComboBox _runLevelCombo;
    private readonly CheckBox _clearCredentialCheck;

    private readonly TextBox _authorBox;
    private readonly TextBox _taskDescriptionBox;

    private readonly TextBox _commandBox;
    private readonly TextBox _argumentsBox;
    private readonly TextBox _workingDirectoryBox;

    private readonly CheckBox _taskEnabledCheck;
    private readonly CheckBox _hiddenCheck;
    private readonly CheckBox _wakeToRunCheck;
    private readonly CheckBox _startWhenAvailableCheck;
    private readonly CheckBox _allowStartOnDemandCheck;
    private readonly CheckBox _disallowBatteryCheck;
    private readonly CheckBox _stopBatteryCheck;

    private readonly CheckBox _disabledCheck;
    private readonly CheckBox _bypassErrorsCheck;
    private readonly CheckBox _removePolicyCheck;
    private readonly CheckBox _userContextCheck;

    private readonly TextBox _taskXmlBox;
    private readonly TextBlock _triggerSummaryText;
    private readonly TextBox _filtersBox;

    public GppScheduledTaskInfo Item => _item;

    public GppScheduledTaskEditorWindow(
        GppScheduledTaskInfo item,
        GppScheduledTaskService service)
    {
        _item = item;
        _service = service;

        Title =
            item.IsV2
                ? "Scheduled Task Preference Editor"
                : "Legacy Scheduled Task Viewer";

        Width = 1080;
        Height = 860;
        MinWidth = 840;
        MinHeight = 650;
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
            Content =
                item.IsV2
                    ? "Save"
                    : "Close",
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
                $"GPO: {item.GpoName}   |   Scope: {item.Scope} Configuration   |   {item.KindDisplay}",
            FontWeight =
                FontWeights.SemiBold
        };

        var tabs =
            new TabControl();

        var generalGrid =
            CreateGrid(9, 200);

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
                    item.Description,
                AcceptsReturn =
                    true,
                MinHeight =
                    56
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

        _taskNameBox =
            new TextBox
            {
                Text =
                    item.TaskName
            };

        _runAsBox =
            new TextBox
            {
                Text =
                    item.RunAs,
                ToolTip =
                    @"Examples: SYSTEM, DOMAIN\svc-account, %LogonDomain%\%LogonUser%"
            };

        _logonTypeCombo =
            new ComboBox
            {
                ItemsSource =
                    new[]
                    {
                        "InteractiveToken",
                        "ServiceAccount",
                        "Group"
                    },
                SelectedItem =
                    NormalizeLogonType(
                        item.LogonType)
            };

        _runLevelCombo =
            new ComboBox
            {
                ItemsSource =
                    new[]
                    {
                        "LeastPrivilege",
                        "HighestAvailable"
                    },
                SelectedItem =
                    string.Equals(
                        item.RunLevel,
                        "LeastPrivilege",
                        StringComparison.OrdinalIgnoreCase)
                        ? "LeastPrivilege"
                        : "HighestAvailable"
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

        _authorBox =
            new TextBox
            {
                Text =
                    item.Author
            };

        _taskDescriptionBox =
            new TextBox
            {
                Text =
                    item.TaskDescription,
                AcceptsReturn =
                    true,
                MinHeight =
                    70
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
            _itemDescriptionBox);

        AddRow(
            generalGrid,
            2,
            "Preference action:",
            _actionCombo);

        AddRow(
            generalGrid,
            3,
            "Task name:",
            _taskNameBox);

        AddRow(
            generalGrid,
            4,
            "Run as:",
            _runAsBox);

        AddRow(
            generalGrid,
            5,
            "Logon type:",
            _logonTypeCombo);

        AddRow(
            generalGrid,
            6,
            "Run level:",
            _runLevelCombo);

        AddRow(
            generalGrid,
            7,
            "Legacy credential:",
            _clearCredentialCheck);

        var legacyCredentialInfo =
            new TextBlock
            {
                Text =
                    "Existing GPP cpassword data is treated as opaque legacy data: it can be preserved or removed, but this editor never decrypts it and never creates new cpassword values.",
                TextWrapping =
                    TextWrapping.Wrap,
                Foreground =
                    System.Windows.Media.Brushes.DimGray,
                Margin =
                    new Thickness(4, 10, 4, 4)
            };

        Grid.SetRow(
            legacyCredentialInfo,
            8);
        Grid.SetColumnSpan(
            legacyCredentialInfo,
            2);
        generalGrid.Children.Add(
            legacyCredentialInfo);

        tabs.Items.Add(
            new TabItem
            {
                Header = "General",
                Content =
                    new ScrollViewer
                    {
                        VerticalScrollBarVisibility =
                            ScrollBarVisibility.Auto,
                        Content =
                            generalGrid
                    }
            });

        var actionGrid =
            CreateGrid(5, 200);

        _commandBox =
            new TextBox
            {
                Text =
                    item.Command
            };

        _argumentsBox =
            new TextBox
            {
                Text =
                    item.Arguments,
                AcceptsReturn =
                    true,
                Height =
                    65
            };

        _workingDirectoryBox =
            new TextBox
            {
                Text =
                    item.WorkingDirectory
            };

        _triggerSummaryText =
            new TextBlock
            {
                Text =
                    item.TriggerSummary,
                TextWrapping =
                    TextWrapping.Wrap,
                Margin =
                    new Thickness(4),
                Foreground =
                    System.Windows.Media.Brushes.DimGray
            };

        AddRow(
            actionGrid,
            0,
            "Program / command:",
            _commandBox);

        AddRow(
            actionGrid,
            1,
            "Arguments:",
            _argumentsBox);

        AddRow(
            actionGrid,
            2,
            "Start in:",
            _workingDirectoryBox);

        AddRow(
            actionGrid,
            3,
            "Triggers:",
            _triggerSummaryText);

        var triggerNote =
            new TextBlock
            {
                Text =
                    "All existing Task Scheduler triggers are preserved. Complex trigger editing is available on the Advanced Task XML tab; structured fields only update registration, principal, settings, and the first Exec action.",
                TextWrapping =
                    TextWrapping.Wrap,
                Foreground =
                    System.Windows.Media.Brushes.DimGray,
                Margin =
                    new Thickness(4, 12, 4, 4)
            };

        Grid.SetRow(
            triggerNote,
            4);
        Grid.SetColumnSpan(
            triggerNote,
            2);
        actionGrid.Children.Add(
            triggerNote);

        tabs.Items.Add(
            new TabItem
            {
                Header = "Action & Triggers",
                Content =
                    new ScrollViewer
                    {
                        VerticalScrollBarVisibility =
                            ScrollBarVisibility.Auto,
                        Content =
                            actionGrid
                    }
            });

        var settingsPanel =
            new StackPanel
            {
                Margin =
                    new Thickness(14)
            };

        _authorBox.Margin =
            new Thickness(4);

        _taskDescriptionBox.Margin =
            new Thickness(4);

        var authorGrid =
            CreateGrid(2, 190);

        AddRow(
            authorGrid,
            0,
            "Author:",
            _authorBox);

        AddRow(
            authorGrid,
            1,
            "Task description:",
            _taskDescriptionBox);

        settingsPanel.Children.Add(
            authorGrid);

        _taskEnabledCheck =
            Check(
                "Task is enabled",
                item.TaskEnabled);

        _hiddenCheck =
            Check(
                "Hidden task",
                item.Hidden);

        _wakeToRunCheck =
            Check(
                "Wake the computer to run this task",
                item.WakeToRun);

        _startWhenAvailableCheck =
            Check(
                "Run as soon as possible after a scheduled start is missed",
                item.StartWhenAvailable);

        _allowStartOnDemandCheck =
            Check(
                "Allow task to be run on demand",
                item.AllowStartOnDemand);

        _disallowBatteryCheck =
            Check(
                "Do not start the task when running on batteries",
                item.DisallowStartIfOnBatteries);

        _stopBatteryCheck =
            Check(
                "Stop the task if the computer switches to battery power",
                item.StopIfGoingOnBatteries);

        settingsPanel.Children.Add(
            _taskEnabledCheck);
        settingsPanel.Children.Add(
            _hiddenCheck);
        settingsPanel.Children.Add(
            _wakeToRunCheck);
        settingsPanel.Children.Add(
            _startWhenAvailableCheck);
        settingsPanel.Children.Add(
            _allowStartOnDemandCheck);
        settingsPanel.Children.Add(
            _disallowBatteryCheck);
        settingsPanel.Children.Add(
            _stopBatteryCheck);

        tabs.Items.Add(
            new TabItem
            {
                Header = "Settings",
                Content =
                    new ScrollViewer
                    {
                        VerticalScrollBarVisibility =
                            ScrollBarVisibility.Auto,
                        Content =
                            settingsPanel
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

        var advanced =
            new DockPanel
            {
                Margin =
                    new Thickness(10)
            };

        var advancedButtons =
            new WrapPanel
            {
                Orientation =
                    Orientation.Horizontal
            };
        DockPanel.SetDock(
            advancedButtons,
            Dock.Top);

        var parseButton =
            new Button
            {
                Content =
                    "Reload structured fields from XML"
            };

        parseButton.Click +=
            ParseXml_Click;

        var syncButton =
            new Button
            {
                Content =
                    "Update XML from structured fields"
            };

        syncButton.Click +=
            SyncXml_Click;

        var validateButton =
            new Button
            {
                Content =
                    "Validate XML"
            };

        validateButton.Click +=
            ValidateXml_Click;

        advancedButtons.Children.Add(
            parseButton);

        advancedButtons.Children.Add(
            syncButton);

        advancedButtons.Children.Add(
            validateButton);

        _taskXmlBox =
            new TextBox
            {
                Text =
                    item.TaskXml,
                AcceptsReturn =
                    true,
                AcceptsTab =
                    true,
                FontFamily =
                    UiStyle.MonospaceFontFamily,
                HorizontalScrollBarVisibility =
                    ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility =
                    ScrollBarVisibility.Auto
            };

        advanced.Children.Add(
            advancedButtons);

        advanced.Children.Add(
            _taskXmlBox);

        tabs.Items.Add(
            new TabItem
            {
                Header =
                    "Advanced Task XML",
                Content =
                    advanced
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
                    UiStyle.MonospaceFontFamily,
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

        if (!item.IsV2)
        {
            SetStructuredEditorsEnabled(
                false,
                generalGrid,
                actionGrid,
                settingsPanel,
                commonPanel);

            _taskXmlBox.IsReadOnly =
                true;

            _filtersBox.IsReadOnly =
                true;

            parseButton.IsEnabled =
                false;

            syncButton.IsEnabled =
                false;

            validateButton.IsEnabled =
                false;
        }

        root.Children.Add(
            footer);

        root.Children.Add(
            header);

        root.Children.Add(
            tabs);

        Content = root;
    }

    private void Save_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (!_item.IsV2)
        {
            DialogResult =
                false;
            Close();
            return;
        }

        if (string.IsNullOrWhiteSpace(
                _taskNameBox.Text))
        {
            Warn(
                "Task name cannot be empty.",
                _taskNameBox);
            return;
        }

        if (string.IsNullOrWhiteSpace(
                _runAsBox.Text))
        {
            Warn(
                "Run-as account cannot be empty.",
                _runAsBox);
            return;
        }

        var action =
            ActionCode(
                Convert.ToString(
                    _actionCombo.SelectedItem));

        if (action != "D" &&
            string.IsNullOrWhiteSpace(
                _commandBox.Text))
        {
            Warn(
                "The first Exec action requires a program or command. Complex tasks can be edited on the Advanced Task XML tab.",
                _commandBox);
            return;
        }

        if (!TryValidateXml(
                _taskXmlBox.Text,
                out var taskError))
        {
            MessageBox.Show(
                this,
                taskError,
                "Task XML",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (!TryValidateFilters(
                _filtersBox.Text,
                out var filterError))
        {
            MessageBox.Show(
                this,
                filterError,
                "Item-level Targeting",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        CopyEditorsToItem();

        try
        {
            _service.SynchronizeTaskXmlFromStructuredFields(
                _item);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                ex.Message,
                "Scheduled Task",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        DialogResult =
            true;
    }

    private void SyncXml_Click(
        object sender,
        RoutedEventArgs e)
    {
        CopyEditorsToItem();

        try
        {
            _item.TaskXml =
                _taskXmlBox.Text;

            _service.SynchronizeTaskXmlFromStructuredFields(
                _item);

            _taskXmlBox.Text =
                _item.TaskXml;

            _triggerSummaryText.Text =
                _item.TriggerSummary;

            MessageBox.Show(
                this,
                "Structured fields were written into the Task XML. Existing triggers and additional actions were preserved.",
                "Scheduled Task",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                ex.Message,
                "Scheduled Task",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void ParseXml_Click(
        object sender,
        RoutedEventArgs e)
    {
        _item.TaskXml =
            _taskXmlBox.Text;

        try
        {
            _service.RefreshStructuredFieldsFromTaskXml(
                _item);

            LoadStructuredEditorsFromItem();

            MessageBox.Show(
                this,
                "Structured fields were refreshed from the Task XML.",
                "Scheduled Task",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                ex.Message,
                "Scheduled Task",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void ValidateXml_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (!TryValidateXml(
                _taskXmlBox.Text,
                out var error))
        {
            MessageBox.Show(
                this,
                error,
                "Task XML",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        MessageBox.Show(
            this,
            "Task XML is well-formed and has a <Task> root element.",
            "Task XML",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void CopyEditorsToItem()
    {
        _item.DisplayName =
            string.IsNullOrWhiteSpace(
                _displayNameBox.Text)
                ? _taskNameBox.Text.Trim()
                : _displayNameBox.Text.Trim();

        _item.Description =
            _itemDescriptionBox.Text;

        _item.Action =
            ActionCode(
                Convert.ToString(
                    _actionCombo.SelectedItem));

        _item.TaskName =
            _taskNameBox.Text.Trim();

        _item.RunAs =
            _runAsBox.Text.Trim();

        _item.LogonType =
            NormalizeLogonType(
                Convert.ToString(
                    _logonTypeCombo.SelectedItem));

        _item.RunLevel =
            Convert.ToString(
                _runLevelCombo.SelectedItem)
            ?? "HighestAvailable";

        _item.ClearStoredCredential =
            _clearCredentialCheck.IsChecked ==
            true;

        _item.Author =
            _authorBox.Text;

        _item.TaskDescription =
            _taskDescriptionBox.Text;

        _item.Command =
            _commandBox.Text.Trim();

        _item.Arguments =
            _argumentsBox.Text;

        _item.WorkingDirectory =
            _workingDirectoryBox.Text.Trim();

        _item.TaskEnabled =
            _taskEnabledCheck.IsChecked == true;

        _item.Hidden =
            _hiddenCheck.IsChecked == true;

        _item.WakeToRun =
            _wakeToRunCheck.IsChecked == true;

        _item.StartWhenAvailable =
            _startWhenAvailableCheck.IsChecked ==
            true;

        _item.AllowStartOnDemand =
            _allowStartOnDemandCheck.IsChecked ==
            true;

        _item.DisallowStartIfOnBatteries =
            _disallowBatteryCheck.IsChecked ==
            true;

        _item.StopIfGoingOnBatteries =
            _stopBatteryCheck.IsChecked ==
            true;

        _item.Disabled =
            _disabledCheck.IsChecked == true;

        _item.BypassErrors =
            _bypassErrorsCheck.IsChecked == true;

        _item.RemoveWhenNoLongerApplied =
            _removePolicyCheck.IsChecked == true;

        _item.RunInUserContext =
            _userContextCheck.IsChecked == true;

        _item.TaskXml =
            _taskXmlBox.Text;

        _item.FiltersXml =
            _filtersBox.Text.Trim();
    }

    private void LoadStructuredEditorsFromItem()
    {
        _runAsBox.Text =
            _item.RunAs;

        _logonTypeCombo.SelectedItem =
            NormalizeLogonType(
                _item.LogonType);

        _runLevelCombo.SelectedItem =
            _item.RunLevel;

        _authorBox.Text =
            _item.Author;

        _taskDescriptionBox.Text =
            _item.TaskDescription;

        _commandBox.Text =
            _item.Command;

        _argumentsBox.Text =
            _item.Arguments;

        _workingDirectoryBox.Text =
            _item.WorkingDirectory;

        _taskEnabledCheck.IsChecked =
            _item.TaskEnabled;

        _hiddenCheck.IsChecked =
            _item.Hidden;

        _wakeToRunCheck.IsChecked =
            _item.WakeToRun;

        _startWhenAvailableCheck.IsChecked =
            _item.StartWhenAvailable;

        _allowStartOnDemandCheck.IsChecked =
            _item.AllowStartOnDemand;

        _disallowBatteryCheck.IsChecked =
            _item.DisallowStartIfOnBatteries;

        _stopBatteryCheck.IsChecked =
            _item.StopIfGoingOnBatteries;

        _triggerSummaryText.Text =
            _item.TriggerSummary;
    }

    private static bool TryValidateXml(
        string xml,
        out string error)
    {
        try
        {
            var root =
                XElement.Parse(
                    xml);

            if (!root.Name.LocalName.Equals(
                    "Task",
                    StringComparison.OrdinalIgnoreCase))
            {
                error =
                    "Task Scheduler XML must have a <Task> root element.";
                return false;
            }

            error =
                string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            error =
                $"Task Scheduler XML is invalid:\n\n{ex.Message}";
            return false;
        }
    }

    private static bool TryValidateFilters(
        string xml,
        out string error)
    {
        if (string.IsNullOrWhiteSpace(xml))
        {
            error =
                string.Empty;
            return true;
        }

        try
        {
            var root =
                XElement.Parse(
                    xml);

            if (!root.Name.LocalName.Equals(
                    "Filters",
                    StringComparison.OrdinalIgnoreCase))
            {
                error =
                    "Item-level targeting XML must have a <Filters> root element.";
                return false;
            }

            error =
                string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            error =
                $"Item-level targeting XML is invalid:\n\n{ex.Message}";
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
            "Scheduled Task",
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

    private static void SetStructuredEditorsEnabled(
        bool enabled,
        params UIElement[] elements)
    {
        foreach (var element in elements)
            element.IsEnabled = enabled;
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

    private static string NormalizeLogonType(
        string? value) =>
        value?.Trim()
            .Replace(
                " ",
                string.Empty,
                StringComparison.Ordinal)
            .ToUpperInvariant() switch
        {
            "GROUP" =>
                "Group",
            "SERVICEACCOUNT" =>
                "ServiceAccount",
            _ =>
                "InteractiveToken"
        };
}

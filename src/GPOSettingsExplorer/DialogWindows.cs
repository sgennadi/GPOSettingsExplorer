using GPOSettingsExplorer.Models;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace GPOSettingsExplorer;

public sealed class InputDialog : Window
{
    private readonly TextBox _textBox;

    public string Value => _textBox.Text.Trim();

    public InputDialog(string title, string prompt, string initialValue = "")
    {
        Title = title;
        Width = 520;
        Height = 175;
        ResizeMode = ResizeMode.CanResizeWithGrip;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var root = new DockPanel { Margin = new Thickness(12) };

        var buttons = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        DockPanel.SetDock(buttons, Dock.Bottom);

        var cancel = new Button { Content = "Cancel", IsCancel = true };
        var ok = new Button { Content = "OK", IsDefault = true };
        ok.Click += (_, _) => DialogResult = true;
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);

        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = prompt, Margin = new Thickness(4) });
        _textBox = new TextBox { Text = initialValue };
        _textBox.SelectAll();
        panel.Children.Add(_textBox);

        root.Children.Add(buttons);
        root.Children.Add(panel);
        Content = root;

        Loaded += (_, _) => _textBox.Focus();
    }
}

public sealed class WmiFilterPickerWindow : Window
{
    private readonly ComboBox _combo;

    public WmiFilterInfo? SelectedFilter => _combo.SelectedItem as WmiFilterInfo;

    public WmiFilterPickerWindow(IEnumerable<WmiFilterInfo> filters, string? currentFilterName)
    {
        Title = "Assign WMI Filter";
        Width = 650;
        Height = 190;
        ResizeMode = ResizeMode.CanResizeWithGrip;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var root = new DockPanel { Margin = new Thickness(12) };

        var buttons = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        DockPanel.SetDock(buttons, Dock.Bottom);

        var cancel = new Button { Content = "Cancel", IsCancel = true };
        var assign = new Button { Content = "Assign", IsDefault = true };
        assign.Click += (_, _) =>
        {
            if (SelectedFilter is null)
            {
                MessageBox.Show(this, "Select a WMI filter.", "Assign WMI Filter",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            DialogResult = true;
        };

        buttons.Children.Add(cancel);
        buttons.Children.Add(assign);

        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = "WMI filter:",
            Margin = new Thickness(4)
        });

        _combo = new ComboBox
        {
            DisplayMemberPath = nameof(WmiFilterInfo.Name),
            ItemsSource = filters.ToArray(),
            MinWidth = 240
        };

        var current = filters.FirstOrDefault(f =>
            string.Equals(f.Name, currentFilterName, StringComparison.OrdinalIgnoreCase));
        _combo.SelectedItem = current ?? filters.FirstOrDefault();

        panel.Children.Add(_combo);
        root.Children.Add(buttons);
        root.Children.Add(panel);
        Content = root;
    }
}

public sealed class WmiTestResultsWindow : Window
{
    public WmiTestResultsWindow(string computerName, IReadOnlyList<WmiTestResult> results)
    {
        Title = $"WMI Filter Test - {computerName}";
        Width = 1050;
        Height = 500;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var allSucceeded = results.All(r => string.IsNullOrEmpty(r.Error));
        var overallMatch = results.Count > 0 && results.All(r => r.IsMatch && string.IsNullOrEmpty(r.Error));

        var root = new DockPanel { Margin = new Thickness(10) };

        var summary = new TextBlock
        {
            Text = !allSucceeded
                ? "Result: ERROR - one or more rules could not be evaluated"
                : overallMatch
                    ? "Result: TRUE - all rules matched"
                    : "Result: FALSE - one or more rules did not match",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(4, 4, 4, 10)
        };
        DockPanel.SetDock(summary, Dock.Top);

        var close = new Button
        {
            Content = "Close",
            IsCancel = true,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        close.Click += (_, _) => Close();
        DockPanel.SetDock(close, Dock.Bottom);

        var grid = new DataGrid
        {
            ItemsSource = results,
            AutoGenerateColumns = false,
            IsReadOnly = true,
            CanUserAddRows = false
        };
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "#",
            Binding = new Binding(nameof(WmiTestResult.RuleNumber)),
            Width = 45
        });
        grid.Columns.Add(new DataGridCheckBoxColumn
        {
            Header = "Match",
            Binding = new Binding(nameof(WmiTestResult.IsMatch)),
            Width = 65
        });
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "Namespace",
            Binding = new Binding(nameof(WmiTestResult.Namespace)),
            Width = 160
        });
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "Query",
            Binding = new Binding(nameof(WmiTestResult.Query)),
            Width = new DataGridLength(2, DataGridLengthUnitType.Star)
        });
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "Time",
            Binding = new Binding(nameof(WmiTestResult.Duration)),
            Width = 110
        });
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "Error",
            Binding = new Binding(nameof(WmiTestResult.Error)),
            Width = new DataGridLength(1.5, DataGridLengthUnitType.Star)
        });

        root.Children.Add(summary);
        root.Children.Add(close);
        root.Children.Add(grid);
        Content = root;
    }
}

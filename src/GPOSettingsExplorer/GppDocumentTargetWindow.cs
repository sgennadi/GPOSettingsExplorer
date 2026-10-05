using System.Windows;
using System.Windows.Controls;
using GPOSettingsExplorer.Models;

namespace GPOSettingsExplorer;

public sealed class GppDocumentTargetWindow : Window
{
    private readonly ComboBox _gpoCombo;
    private readonly ComboBox _scopeCombo;
    private readonly ComboBox _typeCombo;

    public GpoInfo? SelectedGpo => _gpoCombo.SelectedItem as GpoInfo;
    public string SelectedScope => Convert.ToString(_scopeCombo.SelectedItem) ?? "Computer";
    public GppDocumentTypeInfo? SelectedType => _typeCombo.SelectedItem as GppDocumentTypeInfo;

    public GppDocumentTargetWindow(
        IEnumerable<GpoInfo> gpos,
        IEnumerable<GppDocumentTypeInfo> types,
        string title)
    {
        Title = title;
        Width = 760;
        Height = 280;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var root = new DockPanel { Margin = new Thickness(14) };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        DockPanel.SetDock(buttons, Dock.Bottom);

        var cancel = new Button { Content = "Cancel", IsCancel = true };
        var ok = new Button { Content = "Continue", IsDefault = true };
        ok.Click += (_, _) =>
        {
            if (SelectedGpo is null || SelectedType is null)
            {
                MessageBox.Show(
                    this,
                    "Select a GPO and a Preferences type.",
                    Title,
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            DialogResult = true;
        };

        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);

        var grid = new Grid();
        for (var i = 0; i < 3; i++)
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        _gpoCombo = new ComboBox
        {
            ItemsSource = gpos.OrderBy(g => g.DisplayName).ToArray(),
            DisplayMemberPath = nameof(GpoInfo.DisplayName),
            IsTextSearchEnabled = true,
            Margin = new Thickness(4)
        };
        _gpoCombo.SelectedIndex = _gpoCombo.Items.Count > 0 ? 0 : -1;

        _scopeCombo = new ComboBox
        {
            ItemsSource = new[] { "Computer", "User" },
            SelectedIndex = 0,
            Margin = new Thickness(4)
        };

        _typeCombo = new ComboBox
        {
            ItemsSource = types.OrderBy(t => t.Name).ToArray(),
            DisplayMemberPath = nameof(GppDocumentTypeInfo.Name),
            IsTextSearchEnabled = true,
            Margin = new Thickness(4)
        };
        _typeCombo.SelectedIndex = _typeCombo.Items.Count > 0 ? 0 : -1;

        AddRow(grid, 0, "GPO:", _gpoCombo);
        AddRow(grid, 1, "Scope:", _scopeCombo);
        AddRow(grid, 2, "Preferences type:", _typeCombo);

        root.Children.Add(buttons);
        root.Children.Add(grid);
        Content = root;
    }

    private static void AddRow(Grid grid, int row, string label, FrameworkElement editor)
    {
        var text = new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 8, 8, 8)
        };

        Grid.SetRow(text, row);
        Grid.SetColumn(text, 0);
        Grid.SetRow(editor, row);
        Grid.SetColumn(editor, 1);

        grid.Children.Add(text);
        grid.Children.Add(editor);
    }
}

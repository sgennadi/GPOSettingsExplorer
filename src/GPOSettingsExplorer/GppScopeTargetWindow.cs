using System.Windows;
using System.Windows.Controls;
using GPOSettingsExplorer.Models;

namespace GPOSettingsExplorer;

public sealed class GppScopeTargetWindow : Window
{
    private readonly ComboBox _gpoCombo;
    private readonly ComboBox _scopeCombo;

    public GpoInfo? SelectedGpo => _gpoCombo.SelectedItem as GpoInfo;
    public string SelectedScope => Convert.ToString(_scopeCombo.SelectedItem) ?? "Computer";

    public GppScopeTargetWindow(
        IEnumerable<GpoInfo> gpos,
        string title,
        string prompt,
        GpoInfo? selectedGpo = null,
        string? selectedScope = null)
    {
        Title = title;
        Width = 740;
        Height = 260;
        ResizeMode = ResizeMode.CanResizeWithGrip;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var root = new DockPanel { Margin = new Thickness(14) };

        var footer = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        DockPanel.SetDock(footer, Dock.Bottom);

        var cancel = new Button { Content = "Cancel", IsCancel = true };
        var ok = new Button { Content = "Continue", IsDefault = true };
        ok.Click += Ok_Click;
        footer.Children.Add(cancel);
        footer.Children.Add(ok);

        var panel = new StackPanel();

        panel.Children.Add(new TextBlock
        {
            Text = prompt,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(4, 4, 4, 10)
        });

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(135) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        _gpoCombo = new ComboBox
        {
            ItemsSource = gpos.OrderBy(gpo => gpo.DisplayName).ToArray(),
            DisplayMemberPath = nameof(GpoInfo.DisplayName),
            IsTextSearchEnabled = true
        };

        _gpoCombo.SelectedItem = selectedGpo is null
            ? _gpoCombo.Items.Cast<GpoInfo>().FirstOrDefault()
            : _gpoCombo.Items.Cast<GpoInfo>()
                  .FirstOrDefault(gpo => gpo.Id == selectedGpo.Id)
              ?? _gpoCombo.Items.Cast<GpoInfo>().FirstOrDefault();

        _scopeCombo = new ComboBox
        {
            ItemsSource = new[] { "Computer", "User" },
            SelectedItem = string.Equals(
                selectedScope,
                "User",
                StringComparison.OrdinalIgnoreCase)
                ? "User"
                : "Computer"
        };

        AddRow(grid, 0, "Target GPO:", _gpoCombo);
        AddRow(grid, 1, "Scope:", _scopeCombo);

        panel.Children.Add(grid);
        root.Children.Add(footer);
        root.Children.Add(panel);
        Content = root;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedGpo is null)
        {
            MessageBox.Show(
                this,
                "Select a target GPO.",
                Title,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        DialogResult = true;
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
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 8, 8, 8)
        };

        editor.Margin = new Thickness(4, 6, 4, 6);

        Grid.SetRow(caption, row);
        Grid.SetColumn(caption, 0);
        Grid.SetRow(editor, row);
        Grid.SetColumn(editor, 1);

        grid.Children.Add(caption);
        grid.Children.Add(editor);
    }
}

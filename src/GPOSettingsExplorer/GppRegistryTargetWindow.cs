using System.Windows;
using System.Windows.Controls;
using GPOSettingsExplorer.Models;

namespace GPOSettingsExplorer;

public sealed class GppRegistryTargetWindow : Window
{
    private readonly ComboBox _gpoCombo;
    private readonly ComboBox _scopeCombo;

    public GpoInfo? SelectedGpo => _gpoCombo.SelectedItem as GpoInfo;
    public string SelectedScope => Convert.ToString(_scopeCombo.SelectedItem) ?? "Computer";

    public GppRegistryTargetWindow(
        IEnumerable<GpoInfo> gpos,
        string title,
        GpoInfo? selectedGpo = null,
        string? selectedScope = null)
    {
        Title = title;
        Width = 700;
        Height = 230;
        ResizeMode = ResizeMode.CanResizeWithGrip;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var root = new DockPanel { Margin = new Thickness(14) };

        var buttons = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        DockPanel.SetDock(buttons, Dock.Bottom);

        var cancel = new Button { Content = "Cancel", IsCancel = true };
        var ok = new Button { Content = "Continue", IsDefault = true };
        ok.Click += (_, _) =>
        {
            if (SelectedGpo is null)
            {
                MessageBox.Show(
                    this,
                    "Select a GPO.",
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
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var gpoLabel = new TextBlock
        {
            Text = "GPO:",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4)
        };

        _gpoCombo = new ComboBox
        {
            ItemsSource = gpos.OrderBy(gpo => gpo.DisplayName).ToArray(),
            DisplayMemberPath = nameof(GpoInfo.DisplayName),
            IsTextSearchEnabled = true,
            Margin = new Thickness(4)
        };

        _gpoCombo.SelectedItem =
            selectedGpo is null
                ? _gpoCombo.Items.Cast<GpoInfo>().FirstOrDefault()
                : _gpoCombo.Items.Cast<GpoInfo>().FirstOrDefault(gpo => gpo.Id == selectedGpo.Id)
                  ?? _gpoCombo.Items.Cast<GpoInfo>().FirstOrDefault();

        var scopeLabel = new TextBlock
        {
            Text = "Scope:",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4)
        };

        _scopeCombo = new ComboBox
        {
            ItemsSource = new[] { "Computer", "User" },
            Margin = new Thickness(4)
        };

        _scopeCombo.SelectedItem =
            string.Equals(selectedScope, "User", StringComparison.OrdinalIgnoreCase)
                ? "User"
                : "Computer";

        Grid.SetRow(gpoLabel, 0);
        Grid.SetColumn(gpoLabel, 0);
        Grid.SetRow(_gpoCombo, 0);
        Grid.SetColumn(_gpoCombo, 1);
        Grid.SetRow(scopeLabel, 1);
        Grid.SetColumn(scopeLabel, 0);
        Grid.SetRow(_scopeCombo, 1);
        Grid.SetColumn(_scopeCombo, 1);

        grid.Children.Add(gpoLabel);
        grid.Children.Add(_gpoCombo);
        grid.Children.Add(scopeLabel);
        grid.Children.Add(_scopeCombo);

        root.Children.Add(buttons);
        root.Children.Add(grid);
        Content = root;
    }
}

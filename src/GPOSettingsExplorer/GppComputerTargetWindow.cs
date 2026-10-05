using System.Windows;
using System.Windows.Controls;
using GPOSettingsExplorer.Models;

namespace GPOSettingsExplorer;

public sealed class GppComputerTargetWindow : Window
{
    private readonly ComboBox _gpoCombo;

    public GpoInfo? SelectedGpo =>
        _gpoCombo.SelectedItem as GpoInfo;

    public GppComputerTargetWindow(
        IEnumerable<GpoInfo> gpos,
        string title,
        string prompt)
    {
        Title = title;
        Width = 720;
        Height = 220;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation =
            WindowStartupLocation.CenterOwner;

        var root = new DockPanel
        {
            Margin = new Thickness(14)
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

        var ok = new Button
        {
            Content = "Continue",
            IsDefault = true
        };
        ok.Click += Ok_Click;

        footer.Children.Add(cancel);
        footer.Children.Add(ok);

        var panel = new StackPanel();

        panel.Children.Add(
            new TextBlock
            {
                Text = prompt,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(4, 4, 4, 10)
            });

        var grid = new Grid();

        grid.RowDefinitions.Add(
            new RowDefinition
            {
                Height = GridLength.Auto
            });

        grid.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width = new GridLength(135)
            });

        grid.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width = new GridLength(
                    1,
                    GridUnitType.Star)
            });

        _gpoCombo = new ComboBox
        {
            ItemsSource =
                gpos.OrderBy(
                        gpo =>
                            gpo.DisplayName)
                    .ToArray(),
            DisplayMemberPath =
                nameof(GpoInfo.DisplayName),
            IsTextSearchEnabled =
                true
        };

        _gpoCombo.SelectedIndex =
            _gpoCombo.Items.Count > 0
                ? 0
                : -1;

        var label = new TextBlock
        {
            Text = "Target GPO:",
            VerticalAlignment =
                VerticalAlignment.Center,
            Margin =
                new Thickness(4, 8, 8, 8)
        };

        _gpoCombo.Margin =
            new Thickness(4, 6, 4, 6);

        Grid.SetRow(
            label,
            0);
        Grid.SetColumn(
            label,
            0);

        Grid.SetRow(
            _gpoCombo,
            0);
        Grid.SetColumn(
            _gpoCombo,
            1);

        grid.Children.Add(label);
        grid.Children.Add(_gpoCombo);

        panel.Children.Add(grid);

        root.Children.Add(footer);
        root.Children.Add(panel);

        Content = root;
    }

    private void Ok_Click(
        object sender,
        RoutedEventArgs e)
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
}

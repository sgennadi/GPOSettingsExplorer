using System.Windows;
using System.Windows.Controls;
using GPOSettingsExplorer.Models;

namespace GPOSettingsExplorer;

public sealed class GppPrinterTargetWindow : Window
{
    private readonly ComboBox _gpoCombo;
    private readonly ComboBox _scopeCombo;
    private readonly ComboBox _kindCombo;

    public GpoInfo? SelectedGpo =>
        _gpoCombo.SelectedItem as GpoInfo;

    public string SelectedScope =>
        Convert.ToString(
            _scopeCombo.SelectedItem)
        ?? "Computer";

    public string SelectedPrinterKind =>
        Convert.ToString(
            _kindCombo.SelectedItem)
        ?? "Shared";

    public GppPrinterTargetWindow(
        IEnumerable<GpoInfo> gpos)
    {
        Title = "New Printer Preference";
        Width = 760;
        Height = 300;
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

        var panel =
            new StackPanel();

        panel.Children.Add(
            new TextBlock
            {
                Text =
                    "Select the target GPO, configuration scope, and printer preference type.",
                TextWrapping =
                    TextWrapping.Wrap,
                Margin =
                    new Thickness(4, 4, 4, 10)
            });

        var grid = new Grid();

        for (var i = 0;
             i < 3;
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
                    new GridLength(145)
            });

        grid.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width =
                    new GridLength(
                        1,
                        GridUnitType.Star)
            });

        _gpoCombo =
            new ComboBox
            {
                ItemsSource =
                    gpos.OrderBy(
                            gpo =>
                                gpo.DisplayName)
                        .ToArray(),
                DisplayMemberPath =
                    nameof(
                        GpoInfo.DisplayName),
                IsTextSearchEnabled =
                    true
            };

        _gpoCombo.SelectedIndex =
            _gpoCombo.Items.Count > 0
                ? 0
                : -1;

        _scopeCombo =
            new ComboBox
            {
                ItemsSource =
                    new[]
                    {
                        "Computer",
                        "User"
                    },
                SelectedIndex =
                    0
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
                SelectedIndex =
                    0
            };

        AddRow(
            grid,
            0,
            "Target GPO:",
            _gpoCombo);

        AddRow(
            grid,
            1,
            "Scope:",
            _scopeCombo);

        AddRow(
            grid,
            2,
            "Printer type:",
            _kindCombo);

        panel.Children.Add(
            grid);

        root.Children.Add(
            footer);

        root.Children.Add(
            panel);

        Content =
            root;
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

        DialogResult =
            true;
    }

    private static void AddRow(
        Grid grid,
        int row,
        string label,
        FrameworkElement editor)
    {
        var caption =
            new TextBlock
            {
                Text =
                    label,
                VerticalAlignment =
                    VerticalAlignment.Center,
                Margin =
                    new Thickness(4, 8, 8, 8)
            };

        editor.Margin =
            new Thickness(4, 6, 4, 6);

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
}

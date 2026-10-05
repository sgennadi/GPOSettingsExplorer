using System.Windows;
using System.Windows.Controls;
using GPOSettingsExplorer.Models;

namespace GPOSettingsExplorer;

public sealed class PolicyTargetPickerWindow : Window
{
    private readonly ComboBox _gpoCombo;
    private readonly ComboBox _scopeCombo;

    public GpoInfo? SelectedGpo => _gpoCombo.SelectedItem as GpoInfo;
    public string SelectedScope => Convert.ToString(_scopeCombo.SelectedItem) ?? "Computer";

    public PolicyTargetPickerWindow(
        IEnumerable<GpoInfo> gpos,
        AdmxPolicyDefinition definition)
    {
        Title = "Configure policy in GPO";
        Width = 720;
        Height = 250;
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
        var configure = new Button { Content = "Configure", IsDefault = true };
        configure.Click += (_, _) =>
        {
            if (SelectedGpo is null)
            {
                MessageBox.Show(this, "Select a GPO.", "Configure policy",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            DialogResult = true;
        };

        buttons.Children.Add(cancel);
        buttons.Children.Add(configure);

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var policyLabel = new TextBlock
        {
            Text = "Policy:",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4)
        };
        var policyValue = new TextBlock
        {
            Text = definition.DisplayName,
            TextWrapping = TextWrapping.Wrap,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(4)
        };

        var gpoLabel = new TextBlock
        {
            Text = "Target GPO:",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4)
        };
        _gpoCombo = new ComboBox
        {
            ItemsSource = gpos.OrderBy(g => g.DisplayName).ToArray(),
            DisplayMemberPath = nameof(GpoInfo.DisplayName),
            IsTextSearchEnabled = true,
            Margin = new Thickness(4)
        };
        _gpoCombo.SelectedIndex = _gpoCombo.Items.Count > 0 ? 0 : -1;

        var scopeLabel = new TextBlock
        {
            Text = "Scope:",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4)
        };
        _scopeCombo = new ComboBox { Margin = new Thickness(4) };

        if (definition.Scope.Equals("Both", StringComparison.OrdinalIgnoreCase))
        {
            _scopeCombo.ItemsSource = new[] { "Computer", "User" };
            _scopeCombo.SelectedIndex = 0;
        }
        else
        {
            _scopeCombo.ItemsSource = new[] { definition.Scope };
            _scopeCombo.SelectedIndex = 0;
            _scopeCombo.IsEnabled = false;
        }

        Grid.SetRow(policyLabel, 0); Grid.SetColumn(policyLabel, 0);
        Grid.SetRow(policyValue, 0); Grid.SetColumn(policyValue, 1);
        Grid.SetRow(gpoLabel, 1); Grid.SetColumn(gpoLabel, 0);
        Grid.SetRow(_gpoCombo, 1); Grid.SetColumn(_gpoCombo, 1);
        Grid.SetRow(scopeLabel, 2); Grid.SetColumn(scopeLabel, 0);
        Grid.SetRow(_scopeCombo, 2); Grid.SetColumn(_scopeCombo, 1);

        grid.Children.Add(policyLabel);
        grid.Children.Add(policyValue);
        grid.Children.Add(gpoLabel);
        grid.Children.Add(_gpoCombo);
        grid.Children.Add(scopeLabel);
        grid.Children.Add(_scopeCombo);

        root.Children.Add(buttons);
        root.Children.Add(grid);
        Content = root;
    }
}

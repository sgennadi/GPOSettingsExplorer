using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using GPOSettingsExplorer.Models;

namespace GPOSettingsExplorer;

public sealed class GpoLinkEditorWindow : Window
{
    private readonly ComboBox _gpoCombo;
    private readonly ComboBox _targetCombo;
    private readonly CheckBox _enabledCheck;
    private readonly CheckBox _enforcedCheck;
    private readonly TextBox _orderBox;

    public GpoInfo? SelectedGpo => _gpoCombo.SelectedItem as GpoInfo;
    public GpoLinkTarget? SelectedTarget => _targetCombo.SelectedItem as GpoLinkTarget;
    public bool LinkEnabled => _enabledCheck.IsChecked == true;
    public bool Enforced => _enforcedCheck.IsChecked == true;
    public int Order { get; private set; } = 1;

    public GpoLinkEditorWindow(
        IEnumerable<GpoInfo> gpos,
        IEnumerable<GpoLinkTarget> targets,
        GpoLinkInfo? existing = null)
    {
        Title = existing is null ? "New GPO Link" : "Edit GPO Link";
        Width = 760;
        Height = 340;
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
        var save = new Button { Content = existing is null ? "Create link" : "Save", IsDefault = true };
        save.Click += Save_Click;
        buttons.Children.Add(cancel);
        buttons.Children.Add(save);

        var grid = new Grid();
        for (var i = 0; i < 5; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }

        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(155) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        _gpoCombo = new ComboBox
        {
            ItemsSource = gpos.OrderBy(g => g.DisplayName).ToArray(),
            DisplayMemberPath = nameof(GpoInfo.DisplayName),
            IsTextSearchEnabled = true
        };

        _targetCombo = new ComboBox
        {
            ItemsSource = targets.OrderBy(t => t.TargetType).ThenBy(t => t.Name).ToArray(),
            DisplayMemberPath = nameof(GpoLinkTarget.DisplayName),
            IsTextSearchEnabled = true
        };

        _enabledCheck = new CheckBox
        {
            Content = "Enabled",
            IsChecked = existing?.Enabled ?? true,
            VerticalAlignment = VerticalAlignment.Center
        };

        _enforcedCheck = new CheckBox
        {
            Content = "Enforced",
            IsChecked = existing?.Enforced ?? false,
            VerticalAlignment = VerticalAlignment.Center
        };

        _orderBox = new TextBox
        {
            Text = (existing?.Order ?? 1).ToString(CultureInfo.InvariantCulture),
            Width = 120,
            HorizontalAlignment = HorizontalAlignment.Left
        };

        AddRow(grid, 0, "GPO:", _gpoCombo);
        AddRow(grid, 1, "Target:", _targetCombo);

        var options = new WrapPanel { Orientation = Orientation.Horizontal };
        options.Children.Add(_enabledCheck);
        options.Children.Add(_enforcedCheck);
        AddRow(grid, 2, "Link options:", options);

        AddRow(grid, 3, "Link order:", _orderBox);

        var note = new TextBlock
        {
            Text = "Order 1 has the highest precedence on the selected target. The link can be enabled/disabled and enforced independently.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = System.Windows.Media.Brushes.DimGray,
            Margin = new Thickness(4, 10, 4, 4)
        };
        Grid.SetRow(note, 4);
        Grid.SetColumn(note, 0);
        Grid.SetColumnSpan(note, 2);
        grid.Children.Add(note);

        if (existing is not null)
        {
            _gpoCombo.SelectedItem = _gpoCombo.Items
                .Cast<GpoInfo>()
                .FirstOrDefault(g => g.Id == existing.GpoId);

            _targetCombo.SelectedItem = _targetCombo.Items
                .Cast<GpoLinkTarget>()
                .FirstOrDefault(t =>
                    t.DistinguishedName.Equals(existing.TargetDn, StringComparison.OrdinalIgnoreCase));

            _gpoCombo.IsEnabled = false;
            _targetCombo.IsEnabled = false;
        }
        else
        {
            _gpoCombo.SelectedIndex = _gpoCombo.Items.Count > 0 ? 0 : -1;
            _targetCombo.SelectedIndex = _targetCombo.Items.Count > 0 ? 0 : -1;
        }

        root.Children.Add(buttons);
        root.Children.Add(grid);
        Content = root;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedGpo is null || SelectedTarget is null)
        {
            MessageBox.Show(this, "Select both a GPO and a target.", "GPO Link",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!int.TryParse(_orderBox.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var order) ||
            order < 1)
        {
            MessageBox.Show(this, "Link order must be a positive integer.", "GPO Link",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            _orderBox.Focus();
            return;
        }

        Order = order;
        DialogResult = true;
    }

    private static void AddRow(Grid grid, int row, string label, UIElement editor)
    {
        var text = new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 8, 8, 8)
        };

        if (editor is FrameworkElement element)
        {
            element.Margin = new Thickness(4, 6, 4, 6);
        }

        Grid.SetRow(text, row);
        Grid.SetColumn(text, 0);
        Grid.SetRow(editor, row);
        Grid.SetColumn(editor, 1);

        grid.Children.Add(text);
        grid.Children.Add(editor);
    }
}

using System.Windows;
using System.Windows.Controls;
using GPOSettingsExplorer.Models;

namespace GPOSettingsExplorer;

public sealed class GpoPermissionEditorWindow : Window
{
    private readonly TextBox _trusteeBox;
    private readonly ComboBox _permissionCombo;

    public string Trustee => _trusteeBox.Text.Trim();
    public GpoPermissionLevel SelectedLevel =>
        _permissionCombo.SelectedItem is PermissionChoice choice
            ? choice.Level
            : GpoPermissionLevel.Read;

    public GpoPermissionEditorWindow(
        string title,
        string trustee,
        IEnumerable<GpoPermissionLevel> allowedLevels,
        GpoPermissionLevel selectedLevel,
        bool trusteeReadOnly)
    {
        Title = title;
        Width = 650;
        Height = 250;
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
        var save = new Button { Content = "OK", IsDefault = true };
        save.Click += Save_Click;
        buttons.Children.Add(cancel);
        buttons.Children.Add(save);

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        _trusteeBox = new TextBox
        {
            Text = trustee,
            IsReadOnly = trusteeReadOnly,
            ToolTip = @"Use DOMAIN\user, DOMAIN\group, or a SID."
        };

        var choices = allowedLevels
            .Distinct()
            .Select(level => new PermissionChoice(level, DisplayPermission(level)))
            .ToArray();

        _permissionCombo = new ComboBox
        {
            ItemsSource = choices,
            DisplayMemberPath = nameof(PermissionChoice.DisplayName)
        };
        _permissionCombo.SelectedItem =
            choices.FirstOrDefault(choice => choice.Level == selectedLevel)
            ?? choices.FirstOrDefault();

        AddRow(grid, 0, "Trustee:", _trusteeBox);
        AddRow(grid, 1, "Permission:", _permissionCombo);

        var hint = new TextBlock
        {
            Text = "Trustee can be a SAM name such as DOMAIN\\Group or a security identifier (SID). Changes are written through the GPMC security API after an automatic GPO backup.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = System.Windows.Media.Brushes.DimGray,
            Margin = new Thickness(4, 12, 4, 4)
        };
        Grid.SetRow(hint, 2);
        Grid.SetColumnSpan(hint, 2);
        grid.Children.Add(hint);

        root.Children.Add(buttons);
        root.Children.Add(grid);
        Content = root;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(Trustee))
        {
            MessageBox.Show(this,
                "Enter a trustee in DOMAIN\\name format or a SID.",
                "GPO Permission",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            _trusteeBox.Focus();
            return;
        }

        if (_permissionCombo.SelectedItem is null)
        {
            MessageBox.Show(this,
                "Select a permission level.",
                "GPO Permission",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        DialogResult = true;
    }

    private static string DisplayPermission(GpoPermissionLevel level) => level switch
    {
        GpoPermissionLevel.Apply => "Apply group policy (Security Filtering)",
        GpoPermissionLevel.Read => "Read",
        GpoPermissionLevel.Edit => "Edit settings",
        GpoPermissionLevel.FullControl => "Edit settings, delete, modify security",
        _ => "Custom"
    };

    private static void AddRow(Grid grid, int row, string label, FrameworkElement editor)
    {
        var text = new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 8, 8, 8)
        };

        editor.Margin = new Thickness(4, 6, 4, 6);
        Grid.SetRow(text, row);
        Grid.SetColumn(text, 0);
        Grid.SetRow(editor, row);
        Grid.SetColumn(editor, 1);
        grid.Children.Add(text);
        grid.Children.Add(editor);
    }

    private sealed record PermissionChoice(
        GpoPermissionLevel Level,
        string DisplayName);
}

using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using GPOSettingsExplorer.Services;

namespace GPOSettingsExplorer;

/// <summary>Choose exactly one vetted value; no batch restore.</summary>
public sealed class GpoSelectiveRecoveryWindow : Window
{
    public GpoSelectiveRecoveryCandidate? SelectedCandidate { get; private set; }

    public GpoSelectiveRecoveryWindow(GpoSelectiveRecoveryPlan plan)
    {
        Title = "Selective Recovery - One Existing Security Setting";
        Width = 940;
        Height = 580;
        MinWidth = 600;
        MinHeight = 400;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        UiStyle.ApplyWindowDefaults(this);

        var outer = new DockPanel { Margin = new Thickness(12) };
        var head = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        head.Children.Add(new TextBlock
        {
            Text = plan.Target.DisplayName + " | " + plan.Backup.Timestamp,
            FontSize = UiStyle.HeadingFontSize,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap
        });
        head.Children.Add(new TextBlock
        {
            Text = plan.Summary,
            TextWrapping = TextWrapping.Wrap
        });
        head.Children.Add(new TextBlock
        {
            Text = plan.Limitations,
            TextWrapping = TextWrapping.Wrap,
            Foreground = UiStyle.WarningBrush,
            Margin = new Thickness(0, 8, 0, 0)
        });
        DockPanel.SetDock(head, Dock.Top);
        outer.Children.Add(head);

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 10, 0, 0)
        };
        var cancel = new Button { Content = "Cancel", MinWidth = 95 };
        var choose = new Button
        {
            Content = "Review selected restore...", MinWidth = 175,
            IsEnabled = false
        };
        actions.Children.Add(cancel);
        actions.Children.Add(choose);
        DockPanel.SetDock(actions, Dock.Bottom);
        outer.Children.Add(actions);
        cancel.Click += (_, _) => DialogResult = false;

        var grid = new DataGrid
        {
            AutoGenerateColumns = false,
            IsReadOnly = true, CanUserAddRows = false,
            ItemsSource = plan.Candidates,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "Setting",
            Binding = new Binding(nameof(GpoSelectiveRecoveryCandidate.DisplayName)),
            Width = new DataGridLength(2, DataGridLengthUnitType.Star)
        });
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "Current",
            Binding = new Binding(nameof(GpoSelectiveRecoveryCandidate.CurrentValue)),
            Width = new DataGridLength(100)
        });
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "Backup",
            Binding = new Binding(nameof(GpoSelectiveRecoveryCandidate.BackupValue)),
            Width = new DataGridLength(100)
        });
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "Caution",
            Binding = new Binding(nameof(GpoSelectiveRecoveryCandidate.Caution)),
            Width = new DataGridLength(3, DataGridLengthUnitType.Star)
        });
        UiStyle.ApplyDataGridDefaults(grid);
        grid.SelectionChanged += (_, _) =>
            choose.IsEnabled = grid.SelectedItem is GpoSelectiveRecoveryCandidate;
        choose.Click += (_, _) =>
        {
            SelectedCandidate = grid.SelectedItem as GpoSelectiveRecoveryCandidate;
            if (SelectedCandidate is not null)
                DialogResult = true;
        };
        outer.Children.Add(grid);
        Content = outer;
    }
}

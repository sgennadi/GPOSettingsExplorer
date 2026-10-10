using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using GPOSettingsExplorer.Services;
using Microsoft.Win32;

namespace GPOSettingsExplorer;

/// <summary>Inspectable, exportable read-only health findings.</summary>
public sealed class GpoConsistencyWindow : Window
{
    public GpoConsistencyWindow(GpoConsistencyReport report)
    {
        Title = "GPO Health Check - AD / SYSVOL (READ ONLY)";
        Width = 1000;
        Height = 650;
        MinWidth = 640;
        MinHeight = 400;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        UiStyle.ApplyWindowDefaults(this);

        var outer = new DockPanel { Margin = new Thickness(12) };
        var info = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        info.Children.Add(new TextBlock
        {
            Text = report.GpoName,
            FontWeight = FontWeights.SemiBold,
            FontSize = UiStyle.HeadingFontSize,
            TextWrapping = TextWrapping.Wrap
        });
        info.Children.Add(new TextBlock
        {
            Text = report.Summary,
            Foreground = report.HasBlockingIssues ? UiStyle.WarningBrush : UiStyle.SuccessBrush,
            TextWrapping = TextWrapping.Wrap
        });
        info.Children.Add(new TextBlock
        {
            Text = "Single pinned DC; no repair actions and no claim of domain-wide replication or RSoP.",
            Foreground = UiStyle.MutedBrush,
            TextWrapping = TextWrapping.Wrap
        });
        DockPanel.SetDock(info, Dock.Top);
        outer.Children.Add(info);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 10, 0, 0)
        };
        var copy = new Button { Content = "Copy report", MinWidth = 115 };
        var export = new Button { Content = "Export TXT...", MinWidth = 115 };
        var close = new Button { Content = "Close", MinWidth = 85 };
        buttons.Children.Add(copy);
        buttons.Children.Add(export);
        buttons.Children.Add(close);
        copy.Click += (_, _) => Clipboard.SetText(report.ToText());
        export.Click += (_, _) =>
        {
            var dialog = new SaveFileDialog
            {
                Filter = "Text report (*.txt)|*.txt",
                FileName = $"GPO-Health-{report.GpoId:N}.txt",
                AddExtension = true
            };
            if (dialog.ShowDialog(this) == true)
                File.WriteAllText(dialog.FileName, report.ToText());
        };
        close.Click += (_, _) => Close();
        DockPanel.SetDock(buttons, Dock.Bottom);
        outer.Children.Add(buttons);

        var grid = new DataGrid
        {
            IsReadOnly = true,
            AutoGenerateColumns = false,
            ItemsSource = report.Findings,
            CanUserAddRows = false,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "Check", Binding = new Binding(nameof(GpoConsistencyFinding.Check)),
            Width = new DataGridLength(1.2, DataGridLengthUnitType.Star)
        });
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "Status", Binding = new Binding(nameof(GpoConsistencyFinding.Status)),
            Width = new DataGridLength(120)
        });
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "Evidence / diagnostic",
            Binding = new Binding(nameof(GpoConsistencyFinding.Details)),
            Width = new DataGridLength(3.5, DataGridLengthUnitType.Star)
        });
        UiStyle.ApplyDataGridDefaults(grid);
        outer.Children.Add(grid);
        Content = outer;
    }
}

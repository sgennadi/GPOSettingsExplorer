using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using GPOSettingsExplorer.Services;
using Microsoft.Win32;

namespace GPOSettingsExplorer;

/// <summary>
/// Compact evidence-first diagnostic grid. Status colors come from UiStyle;
/// unknown observations are never visually presented as successful checks.
/// </summary>
public sealed class GpoExplanationWindow : Window
{
    private readonly GpoExplanationReport _report;

    public GpoExplanationWindow(GpoExplanationReport report)
    {
        _report = report ?? throw new ArgumentNullException(nameof(report));
        Title = "Explain Why GPO - read-only evidence";
        Width = 1060;
        Height = 685;
        MinWidth = 520;
        MinHeight = 325;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        UiStyle.ApplyWindowDefaults(this);

        var main = new Grid { Margin = new Thickness(10) };
        main.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        main.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        main.RowDefinitions.Add(new RowDefinition
        {
            Height = new GridLength(1, GridUnitType.Star)
        });
        main.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var title = new TextBlock
        {
            Text = report.GpoName + " | " + report.Computer +
                " | " + report.Scope,
            FontSize = UiStyle.HeadingFontSize,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8)
        };
        Grid.SetRow(title, 0);
        main.Children.Add(title);

        var summary = new TextBlock
        {
            Text = report.Overall,
            TextWrapping = TextWrapping.Wrap,
            Foreground = report.Overall.StartsWith(
                "GPO APPLIED IN LAST LOGGED", StringComparison.Ordinal)
                ? UiStyle.SuccessBrush
                : report.Overall.StartsWith(
                    "CURRENT GPO SECTION DISABLED", StringComparison.Ordinal)
                    ? UiStyle.ErrorBrush : UiStyle.WarningBrush,
            Margin = new Thickness(0, 0, 0, 10)
        };
        Grid.SetRow(summary, 1);
        main.Children.Add(summary);

        var grid = new DataGrid
        {
            AutoGenerateColumns = false,
            IsReadOnly = true,
            CanUserAddRows = false,
            CanUserDeleteRows = false,
            IsTextSearchEnabled = true,
            ItemsSource = report.Checks,
            RowHeight = double.NaN,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            MinHeight = 170
        };
        grid.Columns.Add(Column("Check", nameof(GpoExplanationCheck.Area),
            new DataGridLength(1.3, DataGridLengthUnitType.Star)));
        var status = Column("Evidence level", nameof(GpoExplanationCheck.Level),
            new DataGridLength(1.1, DataGridLengthUnitType.Star));
        var statusStyle = WrapStyle();
        AddColor(statusStyle, "Current blocker", UiStyle.ErrorBrush);
        AddColor(statusStyle, "Client errors observed", UiStyle.ErrorBrush);
        AddColor(statusStyle, "Observed applied", UiStyle.SuccessBrush);
        AddColor(statusStyle, "Path candidate", UiStyle.AccentBrush);
        AddColor(statusStyle, "Configured enabled", UiStyle.SuccessBrush);
        foreach (var value in new[]
        {
            "Unknown", "Not evaluated", "No enabled path candidate",
            "Client warnings observed", "Observed excluded"
        })
            AddColor(statusStyle, value, UiStyle.WarningBrush);
        status.ElementStyle = statusStyle;
        grid.Columns.Add(status);
        grid.Columns.Add(Column("Observed evidence",
            nameof(GpoExplanationCheck.Finding),
            new DataGridLength(2.8, DataGridLengthUnitType.Star)));
        grid.Columns.Add(Column("Next diagnostic action",
            nameof(GpoExplanationCheck.NextAction),
            new DataGridLength(2.5, DataGridLengthUnitType.Star)));
        UiStyle.ApplyDataGridDefaults(grid);
        Grid.SetRow(grid, 2);
        main.Children.Add(grid);

        var footer = new DockPanel
        {
            Margin = new Thickness(0, 10, 0, 0),
            LastChildFill = true
        };
        var actions = new WrapPanel
        {
            HorizontalAlignment = HorizontalAlignment.Right
        };
        var copy = ActionButton("Copy report");
        copy.Click += (_, _) => Clipboard.SetText(_report.ToText());
        var export = ActionButton("Export local TXT...");
        export.Click += (_, _) =>
        {
            var save = new SaveFileDialog
            {
                Title = "Export confidential Explain Why evidence",
                Filter = "Text report (*.txt)|*.txt",
                FileName = "GPO-ExplainWhy-" +
                    DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt",
                AddExtension = true
            };
            if (save.ShowDialog(this) == true)
                File.WriteAllText(save.FileName, _report.ToText());
        };
        var close = ActionButton("Close");
        close.Click += (_, _) => Close();
        actions.Children.Add(copy);
        actions.Children.Add(export);
        actions.Children.Add(close);
        DockPanel.SetDock(actions, Dock.Right);
        footer.Children.Add(actions);
        footer.Children.Add(new TextBlock
        {
            Text = "Read-only evidence. Unknown does not mean blocked.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = UiStyle.MutedBrush,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 4, 8, 4)
        });
        Grid.SetRow(footer, 3);
        main.Children.Add(footer);
        Content = main;
    }

    private static DataGridTextColumn Column(
        string title, string property, DataGridLength width) =>
        new()
        {
            Header = title,
            Binding = new Binding(property),
            Width = width,
            ElementStyle = WrapStyle()
        };

    private static Style WrapStyle()
    {
        var style = new Style(typeof(TextBlock));
        style.Setters.Add(new Setter(
            TextBlock.TextWrappingProperty, TextWrapping.Wrap));
        style.Setters.Add(new Setter(
            FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center));
        style.Setters.Add(new Setter(
            FrameworkElement.MarginProperty, new Thickness(4, 3, 4, 3)));
        return style;
    }

    private static void AddColor(Style style, string level, Brush brush)
    {
        var trigger = new DataTrigger
        {
            Binding = new Binding(nameof(GpoExplanationCheck.Level)),
            Value = level
        };
        trigger.Setters.Add(new Setter(
            TextBlock.ForegroundProperty, brush));
        style.Triggers.Add(trigger);
    }

    private static Button ActionButton(string label) => new()
    {
        Content = label,
        MinWidth = 110,
        MinHeight = 32,
        Margin = new Thickness(3, 0, 3, 3)
    };
}

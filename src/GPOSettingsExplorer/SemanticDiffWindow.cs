using System.Windows;
using System.Windows.Controls;
using GPOSettingsExplorer.Models;

namespace GPOSettingsExplorer;

public sealed class SemanticDiffWindow : Window
{
    private readonly IReadOnlyList<SemanticDiffRow> _allRows;
    private readonly DataGrid _grid;
    private readonly CheckBox _differencesOnly;
    private readonly TextBox _searchBox;
    private readonly TextBlock _count;

    public SemanticDiffWindow(
        string title,
        IReadOnlyList<SemanticDiffRow> rows,
        string leftLabel,
        string rightLabel)
    {
        _allRows =
            rows;

        Title =
            title;

        Width =
            1200;

        Height =
            760;

        MinWidth =
            820;

        MinHeight =
            520;

        WindowStartupLocation =
            WindowStartupLocation.CenterOwner;

        var root =
            new DockPanel
            {
                Margin =
                    new Thickness(12)
            };

        var toolbar =
            new WrapPanel();

        DockPanel.SetDock(
            toolbar,
            Dock.Top);

        _differencesOnly =
            new CheckBox
            {
                Content =
                    "Differences only",
                IsChecked =
                    true,
                VerticalAlignment =
                    VerticalAlignment.Center,
                Margin =
                    new Thickness(
                        4)
            };

        _differencesOnly.Checked +=
            (_, _) =>
                ApplyFilter();

        _differencesOnly.Unchecked +=
            (_, _) =>
                ApplyFilter();

        _searchBox =
            new TextBox
            {
                Width =
                    280,
                Margin =
                    new Thickness(4)
            };

        _searchBox.TextChanged +=
            (_, _) =>
                ApplyFilter();

        _count =
            new TextBlock
            {
                Margin =
                    new Thickness(
                        10,
                        0,
                        0,
                        0),
                VerticalAlignment =
                    VerticalAlignment.Center,
                Foreground =
                    System.Windows.Media.Brushes.DimGray
            };

        toolbar.Children.Add(
            _differencesOnly);

        toolbar.Children.Add(
            new TextBlock
            {
                Text =
                    "Search:",
                VerticalAlignment =
                    VerticalAlignment.Center,
                Margin =
                    new Thickness(
                        8,
                        0,
                        0,
                        0)
            });

        toolbar.Children.Add(
            _searchBox);

        toolbar.Children.Add(
            _count);

        _grid =
            new DataGrid
            {
                AutoGenerateColumns =
                    false,
                IsReadOnly =
                    true
            };

        _grid.Columns.Add(
            new DataGridTextColumn
            {
                Header =
                    "Status",
                Binding =
                    new System.Windows.Data.Binding(
                        nameof(SemanticDiffRow.Status)),
                Width =
                    105
            });

        _grid.Columns.Add(
            new DataGridTextColumn
            {
                Header =
                    "Setting path",
                Binding =
                    new System.Windows.Data.Binding(
                        nameof(SemanticDiffRow.Path)),
                Width =
                    new DataGridLength(
                        2,
                        DataGridLengthUnitType.Star)
            });

        _grid.Columns.Add(
            new DataGridTextColumn
            {
                Header =
                    leftLabel,
                Binding =
                    new System.Windows.Data.Binding(
                        nameof(SemanticDiffRow.LeftValue)),
                Width =
                    new DataGridLength(
                        1.5,
                        DataGridLengthUnitType.Star)
            });

        _grid.Columns.Add(
            new DataGridTextColumn
            {
                Header =
                    rightLabel,
                Binding =
                    new System.Windows.Data.Binding(
                        nameof(SemanticDiffRow.RightValue)),
                Width =
                    new DataGridLength(
                        1.5,
                        DataGridLengthUnitType.Star)
            });

        var close =
            new Button
            {
                Content =
                    "Close",
                IsCancel =
                    true,
                HorizontalAlignment =
                    HorizontalAlignment.Right
            };

        DockPanel.SetDock(
            close,
            Dock.Bottom);

        root.Children.Add(
            toolbar);

        root.Children.Add(
            close);

        root.Children.Add(
            _grid);

        Content =
            root;

        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var query =
            _searchBox.Text.Trim();

        IEnumerable<SemanticDiffRow> rows =
            _allRows;

        if (_differencesOnly.IsChecked ==
            true)
        {
            rows =
                rows.Where(
                    row =>
                        row.IsDifferent);
        }

        if (!string.IsNullOrWhiteSpace(
                query))
        {
            rows =
                rows.Where(
                    row =>
                        row.SearchText.Contains(
                            query,
                            StringComparison.CurrentCultureIgnoreCase));
        }

        var shown =
            rows.ToArray();

        _grid.ItemsSource =
            shown;

        _count.Text =
            $"{shown.Length:N0} shown / {_allRows.Count:N0} total";
    }
}

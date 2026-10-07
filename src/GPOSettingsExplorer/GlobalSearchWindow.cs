using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using GPOSettingsExplorer.Services;

namespace GPOSettingsExplorer;

public sealed class GlobalSearchWindow : Window
{
    private readonly GlobalSearchService _service;
    private readonly object _searchOwner;
    private readonly Action<object> _navigate;
    private readonly TextBox _searchBox;
    private readonly DataGrid _grid;
    private readonly TextBlock _count;

    public GlobalSearchWindow(
        GlobalSearchService service,
        object searchOwner,
        string initialQuery,
        Action<object> navigate)
    {
        _service =
            service;

        _searchOwner =
            searchOwner;

        _navigate =
            navigate;

        Title =
            "Global Search";

        Width =
            1100;

        Height =
            700;

        MinWidth =
            760;

        MinHeight =
            480;

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

        toolbar.Children.Add(
            new TextBlock
            {
                Text =
                    "Search:",
                VerticalAlignment =
                    VerticalAlignment.Center
            });

        _searchBox =
            new TextBox
            {
                Text =
                    initialQuery,
                Width =
                    440
            };

        _searchBox.KeyDown +=
            async (_, e) =>
            {
                if (e.Key ==
                    Key.Enter)
                {
                    e.Handled =
                        true;

                    await RunSearchAsync();
                }
            };

        toolbar.Children.Add(
            _searchBox);

        var search =
            new Button
            {
                Content =
                    "Search",
                IsDefault =
                    true
            };

        search.Click +=
            async (_, _) =>
                await RunSearchAsync();

        toolbar.Children.Add(
            search);

        var open =
            new Button
            {
                Content =
                    "Show in source tab"
            };

        open.Click +=
            (_, _) =>
                NavigateSelected();

        toolbar.Children.Add(
            open);

        _count =
            new TextBlock
            {
                Margin =
                    new Thickness(
                        12,
                        0,
                        0,
                        0),
                VerticalAlignment =
                    VerticalAlignment.Center,
                Foreground =
                    System.Windows.Media.Brushes.DimGray
            };

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
                    "Type",
                Binding =
                    new System.Windows.Data.Binding(
                        nameof(GlobalSearchResult.Category)),
                Width =
                    150
            });

        _grid.Columns.Add(
            new DataGridTextColumn
            {
                Header =
                    "Item",
                Binding =
                    new System.Windows.Data.Binding(
                        nameof(GlobalSearchResult.Title)),
                Width =
                    new DataGridLength(
                        1.4,
                        DataGridLengthUnitType.Star)
            });

        _grid.Columns.Add(
            new DataGridTextColumn
            {
                Header =
                    "Details",
                Binding =
                    new System.Windows.Data.Binding(
                        nameof(GlobalSearchResult.Details)),
                Width =
                    new DataGridLength(
                        3,
                        DataGridLengthUnitType.Star)
            });

        _grid.MouseDoubleClick +=
            (_, _) =>
                NavigateSelected();

        root.Children.Add(
            toolbar);

        root.Children.Add(
            _grid);

        Content =
            root;

        Loaded +=
            async (_, _) =>
            {
                _searchBox.Focus();
                _searchBox.SelectAll();

                await RunSearchAsync();
            };
    }

    private async Task RunSearchAsync()
    {
        var query =
            _searchBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(
                query))
        {
            _grid.ItemsSource =
                Array.Empty<GlobalSearchResult>();

            _count.Text =
                "0 result(s)";

            return;
        }

        _searchBox.IsEnabled =
            false;

        _count.Text =
            "Searching...";

        try
        {
            var snapshot =
                _service.CaptureItems(
                    _searchOwner);

            var results =
                await Task.Run(
                    () =>
                        _service.Search(
                            snapshot,
                            query));

            _grid.ItemsSource =
                results;

            _count.Text =
                $"{results.Count:N0} result(s)";
        }
        catch (Exception ex)
        {
            _count.Text =
                "Search failed";

            MessageBox.Show(
                this,
                ex.Message,
                "Global Search",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            _searchBox.IsEnabled =
                true;

            _searchBox.Focus();
        }
    }

    private void NavigateSelected()
    {
        if (_grid.SelectedItem
            is not GlobalSearchResult result)
        {
            return;
        }

        _navigate(
            result.Source);
    }
}

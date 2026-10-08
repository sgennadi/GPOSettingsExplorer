using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using GPOSettingsExplorer.Models;
using GPOSettingsExplorer.Services;

namespace GPOSettingsExplorer;

public partial class MainWindow
{
    private readonly WorkspaceStateService _workspaceStateService =
        new();

    private WorkspaceState _workspaceState =
        new();

    private readonly HashSet<Guid> _favoriteGpos =
        new();

    private readonly List<Guid> _recentGpos =
        new();

    private bool _workspaceInitialized;
    private bool _workspaceApplied;

    private readonly HashSet<string> _restoredWorkspaceControls =
        new(
            StringComparer.OrdinalIgnoreCase);

    private readonly HashSet<string> _restoredGridWidths =
        new(
            StringComparer.OrdinalIgnoreCase);

    private readonly HashSet<string> _restoredGridSorts =
        new(
            StringComparer.OrdinalIgnoreCase);

    private void InitializeWorkspaceUi()
    {
        _workspaceState =
            _workspaceStateService.Load();

        _favoriteGpos.Clear();

        foreach (var id in _workspaceState.FavoriteGpoIds)
        {
            _favoriteGpos.Add(
                id);
        }

        _recentGpos.Clear();

        foreach (var id in _workspaceState.RecentGpoIds
                     .Distinct()
                     .Take(20))
        {
            _recentGpos.Add(
                id);
        }

        GpoQuickFilterCombo.ItemsSource =
            new[]
            {
                "All",
                "Favorites",
                "Recent"
            };

        GpoQuickFilterCombo.SelectedItem =
            new[]
            {
                "All",
                "Favorites",
                "Recent"
            }.Contains(
                _workspaceState.GpoQuickFilter,
                StringComparer.OrdinalIgnoreCase)
                ? _workspaceState.GpoQuickFilter
                : "All";

        GpoSearchBox.Text =
            _workspaceState.GpoSearch;

        SettingsSearchBox.Text =
            _workspaceState.SettingsSearch;

        GlobalSearchBox.Text =
            _workspaceState.GlobalSearch;

        if (_workspaceState.WindowWidth >=
            MinWidth)
        {
            Width =
                _workspaceState.WindowWidth;
        }

        if (_workspaceState.WindowHeight >=
            MinHeight)
        {
            Height =
                _workspaceState.WindowHeight;
        }

        if (!double.IsNaN(
                _workspaceState.WindowLeft) &&
            !double.IsNaN(
                _workspaceState.WindowTop))
        {
            WindowStartupLocation =
                WindowStartupLocation.Manual;

            Left =
                _workspaceState.WindowLeft;

            Top =
                _workspaceState.WindowTop;
        }

        if (_workspaceState.WindowMaximized)
        {
            WindowState =
                WindowState.Maximized;
        }

        _workspaceInitialized =
            true;
    }

    private void RestoreWorkspaceAfterRefresh()
    {
        if (!_workspaceInitialized)
        {
            return;
        }

        ApplyGpoMarkers();

        if (_workspaceApplied)
        {
            return;
        }

        _workspaceApplied =
            true;

        if (_workspaceState.SelectedGpoId
            is Guid selectedId)
        {
            var selected =
                _gpos.FirstOrDefault(
                    item =>
                        item.Id ==
                        selectedId);

            if (selected is not null)
            {
                GpoGrid.SelectedItem =
                    selected;

                GpoGrid.ScrollIntoView(
                    selected);
            }
        }

        if (_workspaceState.SelectedTabIndex >= 0 &&
            _workspaceState.SelectedTabIndex <
            MainTabs.Items.Count)
        {
            MainTabs.SelectedIndex =
                _workspaceState.SelectedTabIndex;
        }

        RestoreGridLayout();
        RestoreNamedControlState(
            this);
    }

    private void MainWindow_Closing(
        object? sender,
        CancelEventArgs e)
    {
        SaveWorkspaceState();

        if (!string.IsNullOrWhiteSpace(
                _pendingUpdatePackage))
        {
            try
            {
                UpdateService.StageInstallerAndRestart(
                    _pendingUpdatePackage);

                _pendingUpdatePackage =
                    null;
            }
            catch (Exception ex)
            {
                ErrorDialog.Show(
                    this,
                    "Install Update",
                    "The update could not be staged while closing.",
                    ex);
            }
        }
    }

    private void SaveWorkspaceState()
    {
        if (!_workspaceInitialized)
        {
            return;
        }

        var bounds =
            WindowState ==
            WindowState.Normal
                ? new Rect(
                    Left,
                    Top,
                    Width,
                    Height)
                : RestoreBounds;

        var state =
            new WorkspaceState
            {
                WindowLeft =
                    bounds.Left,
                WindowTop =
                    bounds.Top,
                WindowWidth =
                    bounds.Width,
                WindowHeight =
                    bounds.Height,
                WindowMaximized =
                    WindowState ==
                    WindowState.Maximized,
                SelectedTabIndex =
                    MainTabs.SelectedIndex,
                SelectedGpoId =
                    (GpoGrid.SelectedItem as GpoInfo)
                    ?.Id,
                GpoSearch =
                    GpoSearchBox.Text,
                SettingsSearch =
                    SettingsSearchBox.Text,
                GlobalSearch =
                    GlobalSearchBox.Text,
                GpoQuickFilter =
                    Convert.ToString(
                        GpoQuickFilterCombo.SelectedItem)
                    ?? "All",
                FavoriteGpoIds =
                    _favoriteGpos
                        .OrderBy(
                            id =>
                                id)
                        .ToList(),
                RecentGpoIds =
                    _recentGpos
                        .Take(20)
                        .ToList(),
                GridColumnWidths =
                    CaptureGridWidths(),
                GridSorts =
                    CaptureGridSorts(),
                TextValues =
                    CaptureTextValues(),
                ComboValues =
                    CaptureComboValues()
            };

        _workspaceState =
            state;

        _workspaceStateService.Save(
            state);
    }

    private void ToggleFavoriteGpo_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (GpoGrid.SelectedItem
            is not GpoInfo gpo)
        {
            return;
        }

        if (!_favoriteGpos.Add(
                gpo.Id))
        {
            _favoriteGpos.Remove(
                gpo.Id);
        }

        ApplyGpoMarkers();
        _gpoView.Refresh();

        FavoriteGpoButton.Content =
            gpo.IsFavorite
                ? "★ Unfavorite"
                : "★ Favorite";

        SaveWorkspaceState();
    }

    private void GpoQuickFilterCombo_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        _gpoView?.Refresh();
    }

    private void MarkGpoRecent(
        Guid gpoId)
    {
        _recentGpos.Remove(
            gpoId);

        _recentGpos.Insert(
            0,
            gpoId);

        if (_recentGpos.Count >
            20)
        {
            _recentGpos.RemoveRange(
                20,
                _recentGpos.Count -
                20);
        }

        ApplyGpoMarkers();
        _gpoView.Refresh();
    }

    private void ApplyGpoMarkers()
    {
        var recentRanks =
            _recentGpos
                .Select(
                    (id, index) =>
                        new
                        {
                            id,
                            rank =
                                index + 1
                        })
                .ToDictionary(
                    item =>
                        item.id,
                    item =>
                        item.rank);

        foreach (var gpo in _gpos)
        {
            gpo.IsFavorite =
                _favoriteGpos.Contains(
                    gpo.Id);

            gpo.RecentRank =
                recentRanks.TryGetValue(
                    gpo.Id,
                    out var rank)
                    ? rank
                    : int.MaxValue;
        }

        if (GpoGrid.SelectedItem
            is GpoInfo selected)
        {
            FavoriteGpoButton.Content =
                selected.IsFavorite
                    ? "★ Unfavorite"
                    : "★ Favorite";
        }
    }

    private bool MatchesGpoQuickFilter(
        GpoInfo gpo)
    {
        var filter =
            Convert.ToString(
                GpoQuickFilterCombo?
                    .SelectedItem)
            ?? "All";

        return filter switch
        {
            "Favorites" =>
                gpo.IsFavorite,
            "Recent" =>
                gpo.RecentRank !=
                int.MaxValue,
            _ =>
                true
        };
    }

    private Dictionary<string, List<double>> CaptureGridWidths()
    {
        var result =
            _workspaceState.GridColumnWidths
                .ToDictionary(
                    pair =>
                        pair.Key,
                    pair =>
                        pair.Value.ToList(),
                    StringComparer.OrdinalIgnoreCase);

        foreach (var grid in EnumerateNamedDataGrids(
                     this))
        {
            ApplyReadableGridColumnMinimums(
                grid);

            // Hidden TabItem contents can report the framework minimum width
            // rather than their real measured width. Never overwrite a good
            // saved layout with those transient measurements.
            if (!grid.IsLoaded ||
                !grid.IsVisible ||
                grid.ActualWidth <
                320 ||
                grid.Columns.Count ==
                0)
            {
                continue;
            }

            var widths =
                grid.Columns
                    .Select(
                        column =>
                            Math.Max(
                                column.MinWidth,
                                column.ActualWidth))
                    .ToList();

            if (widths.Any(
                    width =>
                        width <
                        36))
            {
                continue;
            }

            result[
                grid.Name] =
                widths;
        }

        return result;
    }

    private Dictionary<string, List<WorkspaceSortDescription>> CaptureGridSorts()
    {
        var result =
            new Dictionary<string, List<WorkspaceSortDescription>>(
                StringComparer.OrdinalIgnoreCase);

        foreach (var grid in EnumerateNamedDataGrids(
                     this))
        {
            if (grid.ItemsSource is null)
            {
                continue;
            }

            try
            {
                var view =
                    CollectionViewSource.GetDefaultView(
                        grid.ItemsSource);

                if (view.SortDescriptions.Count ==
                    0)
                {
                    continue;
                }

                result[
                    grid.Name] =
                    view.SortDescriptions
                        .Select(
                            sort =>
                                new WorkspaceSortDescription(
                                    sort.PropertyName,
                                    sort.Direction.ToString()))
                        .ToList();
            }
            catch
            {
            }
        }

        return result;
    }

    private Dictionary<string, string> CaptureTextValues()
    {
        var result =
            new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase);

        foreach (var textBox in EnumerateNamedControls<TextBox>(
                     this))
        {
            result[
                textBox.Name] =
                textBox.Text;
        }

        return result;
    }

    private Dictionary<string, string> CaptureComboValues()
    {
        var result =
            new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase);

        foreach (var combo in EnumerateNamedControls<ComboBox>(
                     this))
        {
            if (combo.SelectedItem
                    is string value &&
                !string.IsNullOrWhiteSpace(
                    value))
            {
                result[
                    combo.Name] =
                    value;
            }
        }

        return result;
    }

    private void ScheduleWorkspaceControlRestore()
    {
        if (!_workspaceInitialized)
        {
            return;
        }

        _ =
            Dispatcher.BeginInvoke(
                new Action(
                    () =>
                    {
                        var root =
                            MainTabs.SelectedContent
                            as DependencyObject
                            ?? MainTabs;

                        RestoreNamedControlState(
                            root);

                        RestoreGridLayout();
                    }),
                System.Windows.Threading.DispatcherPriority.ContextIdle);
    }

    private void RestoreNamedControlState(
        DependencyObject root)
    {
        foreach (var textBox in EnumerateNamedControls<TextBox>(
                     root))
        {
            if (_restoredWorkspaceControls.Contains(
                    textBox.Name) ||
                !_workspaceState.TextValues.TryGetValue(
                    textBox.Name,
                    out var value))
            {
                continue;
            }

            textBox.Text =
                value;

            _restoredWorkspaceControls.Add(
                textBox.Name);
        }

        foreach (var combo in EnumerateNamedControls<ComboBox>(
                     root))
        {
            if (_restoredWorkspaceControls.Contains(
                    combo.Name) ||
                !_workspaceState.ComboValues.TryGetValue(
                    combo.Name,
                    out var value))
            {
                continue;
            }

            var match =
                combo.Items
                    .Cast<object>()
                    .FirstOrDefault(
                        item =>
                            item is string text &&
                            text.Equals(
                                value,
                                StringComparison.CurrentCultureIgnoreCase));

            if (match is null)
            {
                continue;
            }

            combo.SelectedItem =
                match;

            _restoredWorkspaceControls.Add(
                combo.Name);
        }
    }

    private void RestoreGridLayout()
    {
        foreach (var grid in EnumerateNamedDataGrids(
                     this))
        {
            ApplyReadableGridColumnMinimums(
                grid);

            if (!_restoredGridWidths.Contains(
                    grid.Name) &&
                _workspaceState.GridColumnWidths.TryGetValue(
                    grid.Name,
                    out var widths))
            {
                for (var index = 0;
                     index < widths.Count &&
                     index < grid.Columns.Count;
                     index++)
                {
                    if (double.IsFinite(
                            widths[index]) &&
                        widths[index] >=
                        36 &&
                        widths[index] <=
                        2400)
                    {
                        grid.Columns[index].Width =
                            new DataGridLength(
                                Math.Max(
                                    grid.Columns[index].MinWidth,
                                    widths[index]));
                    }
                }

                _restoredGridWidths.Add(
                    grid.Name);
            }

            if (_restoredGridSorts.Contains(
                    grid.Name) ||
                grid.ItemsSource is null ||
                !_workspaceState.GridSorts.TryGetValue(
                    grid.Name,
                    out var sorts))
            {
                continue;
            }

            try
            {
                var view =
                    CollectionViewSource.GetDefaultView(
                        grid.ItemsSource);

                view.SortDescriptions.Clear();

                foreach (var sort in sorts)
                {
                    if (!Enum.TryParse<ListSortDirection>(
                            sort.Direction,
                            ignoreCase:
                                true,
                            out var direction))
                    {
                        continue;
                    }

                    view.SortDescriptions.Add(
                        new SortDescription(
                            sort.Property,
                            direction));
                }

                _restoredGridSorts.Add(
                    grid.Name);
            }
            catch
            {
            }
        }
    }

    private static void ApplyReadableGridColumnMinimums(
        DataGrid grid)
    {
        grid.MinColumnWidth =
            Math.Max(
                grid.MinColumnWidth,
                72);

        foreach (var column in grid.Columns)
        {
            var minimum =
                GetReadableColumnMinimum(
                    column);

            if (column.MinWidth <
                minimum)
            {
                column.MinWidth =
                    minimum;
            }

            if (column.Width.IsAbsolute &&
                column.Width.Value <
                minimum)
            {
                column.Width =
                    new DataGridLength(
                        minimum);
            }
        }
    }

    private static double GetReadableColumnMinimum(
        DataGridColumn column)
    {
        var header =
            Convert.ToString(
                column.Header)
            ?.Trim()
            ?? string.Empty;

        if (header.Equals(
                "★",
                StringComparison.Ordinal))
        {
            return 42;
        }

        if (header.Equals(
                "Scope",
                StringComparison.CurrentCultureIgnoreCase))
        {
            return 84;
        }

        if (header.Equals(
                "State",
                StringComparison.CurrentCultureIgnoreCase) ||
            header.Equals(
                "Action",
                StringComparison.CurrentCultureIgnoreCase) ||
            header.Equals(
                "Type",
                StringComparison.CurrentCultureIgnoreCase) ||
            header.Equals(
                "Order",
                StringComparison.CurrentCultureIgnoreCase))
        {
            return 88;
        }

        if (header.Contains(
                "Setting",
                StringComparison.CurrentCultureIgnoreCase) ||
            header.Contains(
                "Category",
                StringComparison.CurrentCultureIgnoreCase) ||
            header.Contains(
                "Description",
                StringComparison.CurrentCultureIgnoreCase) ||
            header.Contains(
                "Value",
                StringComparison.CurrentCultureIgnoreCase) ||
            header.Contains(
                "Path",
                StringComparison.CurrentCultureIgnoreCase) ||
            header.Contains(
                "Target",
                StringComparison.CurrentCultureIgnoreCase) ||
            header.Contains(
                "Command",
                StringComparison.CurrentCultureIgnoreCase) ||
            header.Contains(
                "Arguments",
                StringComparison.CurrentCultureIgnoreCase))
        {
            return 150;
        }

        if (header.Equals(
                "GPO",
                StringComparison.CurrentCultureIgnoreCase) ||
            header.Contains(
                "Name",
                StringComparison.CurrentCultureIgnoreCase) ||
            header.Contains(
                "Registry",
                StringComparison.CurrentCultureIgnoreCase) ||
            header.Contains(
                "File",
                StringComparison.CurrentCultureIgnoreCase))
        {
            return 120;
        }

        if (column is DataGridCheckBoxColumn)
        {
            return Math.Clamp(
                60 +
                header.Length *
                4.5,
                78,
                145);
        }

        return Math.Clamp(
            56 +
            header.Length *
            5.5,
            78,
            170);
    }

    private static IEnumerable<T> EnumerateNamedControls<T>(
        DependencyObject root)
        where T : FrameworkElement
    {
        if (root is T control &&
            !string.IsNullOrWhiteSpace(
                control.Name))
        {
            yield return control;
        }

        foreach (var child in LogicalTreeHelper.GetChildren(
                     root))
        {
            if (child is not DependencyObject dependency)
            {
                continue;
            }

            foreach (var nested in EnumerateNamedControls<T>(
                         dependency))
            {
                yield return nested;
            }
        }
    }

    private static IEnumerable<DataGrid> EnumerateNamedDataGrids(
        DependencyObject root)
    {
        if (root is DataGrid grid &&
            !string.IsNullOrWhiteSpace(
                grid.Name))
        {
            yield return grid;
        }

        foreach (var child in LogicalTreeHelper.GetChildren(
                     root))
        {
            if (child is not DependencyObject dependency)
            {
                continue;
            }

            foreach (var nested in EnumerateNamedDataGrids(
                         dependency))
            {
                yield return nested;
            }
        }
    }
}

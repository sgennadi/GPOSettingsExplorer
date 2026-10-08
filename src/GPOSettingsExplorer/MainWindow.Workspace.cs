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
                    CaptureGridSorts()
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
            new Dictionary<string, List<double>>(
                StringComparer.OrdinalIgnoreCase);

        foreach (var grid in EnumerateNamedDataGrids(
                     this))
        {
            result[
                grid.Name] =
                grid.Columns
                    .Select(
                        column =>
                            Math.Max(
                                20,
                                column.ActualWidth))
                    .ToList();
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

    private void RestoreGridLayout()
    {
        foreach (var grid in EnumerateNamedDataGrids(
                     this))
        {
            if (_workspaceState.GridColumnWidths.TryGetValue(
                    grid.Name,
                    out var widths))
            {
                for (var index = 0;
                     index < widths.Count &&
                     index < grid.Columns.Count;
                     index++)
                {
                    if (widths[index] >=
                        20)
                    {
                        grid.Columns[index].Width =
                            new DataGridLength(
                                widths[index]);
                    }
                }
            }

            if (grid.ItemsSource is null ||
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
            }
            catch
            {
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

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

        if (_workspaceState.NavigationLayoutVersion == 0)
        {
            // Pre-0.7.0 top-level ADMX was index 3; sixteen GPP editors
            // occupied indexes 4-19, and the remaining tabs 20-25.
            var oldIndex = _workspaceState.SelectedTabIndex;
            if (oldIndex == 3)
            {
                AdvancedSourcesExpander.IsExpanded = true;
                AllSettingsSubTabs.SelectedItem = AdmxCatalogTab;
            }
            else if (oldIndex >= 4 && oldIndex <= 19)
            {
                GppPreferencesTabs.SelectedIndex = oldIndex - 4;
            }

            _workspaceState.SelectedTabIndex = oldIndex switch
            {
                3 => 1,
                >= 4 and <= 19 => 3,
                >= 20 and <= 25 => oldIndex - 16,
                _ => oldIndex
            };
            _workspaceState.NavigationLayoutVersion = 1;
        }

        if (_workspaceState.SelectedTabIndex >= 0 &&
            _workspaceState.SelectedTabIndex < MainTabs.Items.Count)
            MainTabs.SelectedIndex = _workspaceState.SelectedTabIndex;

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
                NavigationLayoutVersion = 1,
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
                    if (widths[index] >=
                        20)
                    {
                        grid.Columns[index].Width =
                            new DataGridLength(
                                widths[index]);
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

using GPOSettingsExplorer.Models;
using GPOSettingsExplorer.Services;

namespace GPOSettingsExplorer;

public partial class MainWindow
{
    private readonly SettingsIndexCacheService _settingsIndexCacheService = new();
    private readonly SemaphoreSlim _settingsIndexGate = new(1, 1);

    private SettingsIndexCacheSnapshot? _settingsIndexCache;
    private bool _settingsCacheLoaded;

    private async Task EnsureSettingsIndexAsync(
        bool forceRebuild)
    {
        if (_domainContext is null ||
            !_gpmService.IsAvailable ||
            _gpos.Count == 0)
        {
            return;
        }

        if (forceRebuild)
        {
            _indexCancellation?.Cancel();
            await _settingsIndexGate.WaitAsync();
        }
        else
        {
            if (!await _settingsIndexGate.WaitAsync(0))
            {
                return;
            }
        }

        _indexCancellation =
            new CancellationTokenSource();

        var cancellationToken =
            _indexCancellation.Token;

        var showBusyProgress =
            false;

        try
        {
            var domainName =
                _domainContext.DomainName;

            var currentGpos =
                _gpos.ToArray();

            if (!_settingsCacheLoaded ||
                _settingsIndexCache is null ||
                !_settingsIndexCache.DomainName.Equals(
                    domainName,
                    StringComparison.OrdinalIgnoreCase))
            {
                _settingsIndexCache =
                    await Task.Run(
                        () => _settingsIndexCacheService.Load(
                            domainName),
                        cancellationToken);

                _settingsCacheLoaded =
                    true;

                var cachedSettings =
                    _settingsIndexCacheService.GetCurrentSettings(
                        _settingsIndexCache,
                        currentGpos);

                if (cachedSettings.Count > 0)
                {
                    ReplaceCollection(
                        _settings,
                        cachedSettings);

                    _settingsView.Refresh();

                    SettingsCountText.Text =
                        $"{_settings.Count:N0} configured settings (cached)";

                    StatusText.Text =
                        $"Loaded cached settings index from {_settingsIndexCache.GeneratedUtc.ToLocalTime():yyyy-MM-dd HH:mm}";
                }
            }

            var snapshot =
                _settingsIndexCache ??
                SettingsIndexCacheSnapshot.Empty(
                    domainName);

            var changedGpos =
                _settingsIndexCacheService.GetChangedGpos(
                    snapshot,
                    currentGpos,
                    forceRebuild);

            var currentIds =
                currentGpos
                    .Select(gpo =>
                        gpo.Id)
                    .ToHashSet();

            var removedCount =
                snapshot.Entries.Count(entry =>
                    !currentIds.Contains(
                        entry.GpoId));

            if (changedGpos.Count == 0 &&
                removedCount == 0)
            {
                SettingsCountText.Text =
                    $"{_settings.Count:N0} configured settings";

                StatusText.Text =
                    "Settings index is up to date";

                return;
            }

            showBusyProgress =
                true;

            BusyProgress.Visibility =
                Visibility.Visible;

            var cachedCount =
                _settings.Count;

            SettingsCountText.Text =
                cachedCount > 0
                    ? $"{cachedCount:N0} cached | updating {changedGpos.Count:N0} GPO(s)..."
                    : $"Indexing {changedGpos.Count:N0} GPO(s)...";

            var progress =
                new Progress<string>(message =>
                {
                    StatusText.Text =
                        message;

                    HeaderStatusText.Text =
                        message;
                });

            var rebuiltSettings =
                await Task.Run(
                    () => _gpmService.BuildSettingsIndex(
                        domainName,
                        changedGpos,
                        progress,
                        cancellationToken),
                    cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            var merged =
                _settingsIndexCacheService.Merge(
                    domainName,
                    snapshot,
                    currentGpos,
                    changedGpos,
                    rebuiltSettings);

            await Task.Run(
                () => _settingsIndexCacheService.SaveSnapshot(
                    merged),
                cancellationToken);

            _settingsIndexCache =
                merged;

            var finalSettings =
                _settingsIndexCacheService.GetCurrentSettings(
                    merged,
                    currentGpos);

            ReplaceCollection(
                _settings,
                finalSettings);

            _settingsView.Refresh();

            SettingsCountText.Text =
                $"{_settings.Count:N0} configured settings";

            HeaderStatusText.Text =
                $"{_gpos.Count:N0} GPOs | {_settings.Count:N0} settings | {_wmiFilters.Count:N0} WMI filters";

            StatusText.Text =
                forceRebuild
                    ? "Settings index rebuilt and cached"
                    : changedGpos.Count > 0
                        ? $"Settings index updated: {changedGpos.Count:N0} changed/new GPO(s)"
                        : "Settings index cache cleaned";
        }
        catch (OperationCanceledException)
        {
            StatusText.Text =
                "Settings indexing canceled";
        }
        catch (Exception ex)
        {
            StatusText.Text =
                "Settings indexing failed";

            MessageBox.Show(
                this,
                ex.Message,
                forceRebuild
                    ? "Rebuild Settings Index"
                    : "Update Settings Index",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            if (showBusyProgress)
            {
                BusyProgress.Visibility =
                    Visibility.Collapsed;
            }

            _settingsIndexGate.Release();
        }
    }

    private async Task PersistCurrentSettingsCacheAsync()
    {
        if (_domainContext is null ||
            !_settingsCacheLoaded ||
            _settings.Count == 0)
        {
            return;
        }

        try
        {
            await Task.Run(() =>
                _settingsIndexCacheService.Save(
                    _domainContext.DomainName,
                    _gpos,
                    _settings));

            _settingsIndexCache =
                await Task.Run(() =>
                    _settingsIndexCacheService.Load(
                        _domainContext.DomainName));
        }
        catch
        {
            // Cache persistence must never turn a successful policy edit into
            // a visible failure. The next All Settings refresh will rebuild
            // stale entries from GPO ModificationTime.
        }
    }
}

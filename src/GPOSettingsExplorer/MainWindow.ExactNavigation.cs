using System.Windows;
using GPOSettingsExplorer.Models;
using GPOSettingsExplorer.Services;

namespace GPOSettingsExplorer;

public partial class MainWindow
{
    /// <summary>
    /// One GPMC entry -> one observed MMC route, with a one-time manual
    /// review fallback. This runs only after user action: never at startup
    /// and never all GPOs. No GPO/SYSVOL writes or blind MMC dialog clicks.
    /// </summary>
    private async Task<bool> DiscoverAndOpenExactMmcRouteAsync(
        GpoInfo gpo, PolicySettingInfo setting, Window dialogOwner,
        IProgress<string>? progress)
    {
        if (_domainContext is null || !_gpos.Any(item => item.Id == gpo.Id) ||
            setting.GpoId != gpo.Id)
        {
            progress?.Report("GPO or domain session is no longer available.");
            return false;
        }

        if (SecurityXmlEntryClassifier.IsTechnicalDetail(setting))
        {
            progress?.Report("This is a nested GPMC XML detail, not an independently editable MMC policy.");
            return false;
        }

        var configured = _settings.Where(row => row.GpoId == gpo.Id).ToArray();
        if (!configured.Any(row => ReferenceEquals(row, setting) ||
            (row.Scope.Equals(setting.Scope, StringComparison.OrdinalIgnoreCase) &&
             row.Extension.Equals(setting.Extension, StringComparison.OrdinalIgnoreCase) &&
             row.SettingName.Equals(setting.SettingName, StringComparison.OrdinalIgnoreCase) &&
             row.Category.Equals(setting.Category, StringComparison.OrdinalIgnoreCase) &&
             row.Value.Equals(setting.Value, StringComparison.Ordinal))))
        {
            progress?.Report("The source GPMC record is not in the current selected-GPO index. " +
                "Refresh the GPMC index before attempting exact navigation.");
            return false;
        }

        if (!HasCurrentMmcInventoryFor(gpo))
        {
            progress?.Report("An observed route is required: starting the ONE selected-GPO " +
                "read-only MMC inventory only with explicit confirmation.");
            if (!await ScanMmcInventoryAsync(gpo, dialogOwner, progress))
            {
                progress?.Report("MMC scan canceled/failed. No exact native route can be claimed.");
                return false;
            }
        }

        if (!HasCurrentMmcInventoryFor(gpo) || _domainContext is null)
        {
            progress?.Report("MMC snapshot is stale or from another DC/GPO. Re-scan before opening.");
            return false;
        }

        var resolution = GpoExactMmcRouteResolver.Resolve(
            setting, configured, _mmcInventoryRows, _admxPolicies);
        if (resolution.CanOpen)
        {
            progress?.Report("Source-aware exact path: " +
                string.Join(" > ", resolution.Entry!.TreeSegments) + " > " +
                resolution.Entry.SettingName + ". Rechecking in a NEW MMC editor...");
            var opened = await _mmcInventoryNavigator.OpenInventoryEntryAsync(
                gpo, _domainContext.DomainDistinguishedName, resolution.Entry, progress);
            progress?.Report(opened
                ? "Exact native row was selected and Properties were requested. " +
                  "Check the MMC dialog; this is NOT a verified stored value or RSoP."
                : "The observed route could not be reopened exactly. " +
                  "No alternative row was selected.");
            return opened;
        }

        progress?.Report(resolution.Status + ": " + resolution.Explanation);
        if (resolution.Status is GpoExactMmcRouteResolver.SourceAmbiguous or
            GpoExactMmcRouteResolver.Unsupported)
            return false;

        var reviewable = GpoExactMmcRouteResolver.ReviewCandidates(
            setting, _mmcInventoryRows);
        if (reviewable.Count == 0)
        {
            progress?.Report("No uniquely selectable native MMC policy rows were found " +
                "for this GPO/scope. Use the dedicated GPO source editor or inspect " +
                "the incomplete/unsafe MMC sections.");
            return false;
        }

        var answer = MessageBox.Show(dialogOwner,
            resolution.Status + ": " + resolution.Explanation +
            "\n\nAutomatic mapping is not proven. " +
            "You can search and independently review one native row from the same GPO/scope. " +
            "This will only confirm a ONE-TIME NAVIGATION choice, NOT the stored value " +
            "or effective policy. Do not guess a route based on similar names.\n\n" +
            "Review the observed rows?",
            "Exact MMC path needs human review",
            MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes)
            return false;

        var picker = new GpoExactMmcRouteReviewWindow(setting, reviewable)
        {
            Owner = dialogOwner
        };
        if (picker.ShowDialog() != true ||
            picker.SelectedEntry is not MmcInventoryEntry selected ||
            !GpoExactMmcRouteResolver.MayReview(setting, configured,
                selected, _mmcInventoryRows))
        {
            progress?.Report("Manual route review canceled or no longer uniquely bound.");
            return false;
        }

        if (!HasCurrentMmcInventoryFor(gpo) || _domainContext is null)
        {
            progress?.Report("Domain or pinned DC changed during route review. No MMC row opened.");
            return false;
        }

        progress?.Report("Operator-reviewed (NOT value verified): " +
            string.Join(" > ", selected.TreeSegments) + " > " + selected.SettingName +
            ". Verifying current native row again...");
        var success = await _mmcInventoryNavigator.OpenInventoryEntryAsync(
            gpo, _domainContext.DomainDistinguishedName, selected, progress);
        progress?.Report(success
            ? "Reviewed route selected in a new MMC editor. Confirm actual properties window. " +
              "Source/value equality and RSoP are NOT verified."
            : "New MMC editor could not revalidate the manually reviewed row.");
        return success;
    }

    private GpoExactPathsWindow? _exactPathsWindow;

    private async void OpenExactPaths_Click(object sender, RoutedEventArgs e)
    {
        if (_domainContext is null || !_gpmService.IsAvailable)
        {
            MessageBox.Show(this,
                "Connect to an AD domain with GPMC/RSAT before scanning settings.",
                "Exact MMC Paths", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // Requesting the full index is explicit here, unlike the optional
        // user-controlled background indexing at startup.
        if (_settings.Count == 0)
            await EnsureSettingsIndexAsync(forceRebuild: false);

        if (_settings.Count == 0)
        {
            MessageBox.Show(this,
                "No GPMC configured settings have been indexed. " +
                "Build the GPMC settings index before reviewing exact paths.",
                "Exact MMC Paths", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (_exactPathsWindow is { IsLoaded: true } current)
        {
            current.RefreshRows();
            current.Activate();
            return;
        }

        _exactPathsWindow = new GpoExactPathsWindow(BuildExactMmcPathCoverage,
            async (setting, window, progress) =>
            {
                var gpo = _gpos.FirstOrDefault(item => item.Id == setting.GpoId);
                if (gpo is null)
                {
                    progress.Report("GPO not available in the current domain connection.");
                    return false;
                }
                return await DiscoverAndOpenExactMmcRouteAsync(
                    gpo, setting, window, progress);
            })
        {
            Owner = this
        };
        _exactPathsWindow.Closed += (_, _) => _exactPathsWindow = null;
        _exactPathsWindow.Show();
    }

    private IReadOnlyList<GpoExactPathsWindow.RouteRow> BuildExactMmcPathCoverage()
    {
        var indexed = _settings.ToArray();
        var byGpo = indexed.GroupBy(setting => setting.GpoId)
            .ToDictionary(group => group.Key, group => group.ToArray());
        var availableGpos = _gpos.ToDictionary(gpo => gpo.Id);
        var observedGpo = _mmcInventoryScannedGpoId is Guid id &&
                          availableGpos.TryGetValue(id, out var observed) &&
                          HasCurrentMmcInventoryFor(observed)
            ? id : Guid.Empty;

        var result = new List<GpoExactPathsWindow.RouteRow>(indexed.Length);
        foreach (var setting in indexed)
        {
            IReadOnlyList<MmcInventoryEntry>? snapshot =
                setting.GpoId == observedGpo ? _mmcInventoryRows : null;

            var decision = GpoExactMmcRouteResolver.Resolve(
                setting, byGpo[setting.GpoId], snapshot, _admxPolicies);
            var hint = string.Join(" > ", GpoPolicySectionRoutes.Resolve(setting));
            if (hint.Length == 0)
                hint = "(not mapped; needs an observed MMC source-specific route)";
            var observedPath = decision.Entry is null
                ? string.Empty
                : string.Join(" > ", decision.Entry.TreeSegments) +
                    " > " + decision.Entry.SettingName;

            result.Add(new GpoExactPathsWindow.RouteRow(setting,
                setting.GpoName, setting.Scope, setting.Extension,
                setting.SettingName, hint, decision.Status,
                observedPath, decision.Explanation));
        }
        return result;
    }

}

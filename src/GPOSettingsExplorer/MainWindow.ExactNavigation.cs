using System.Windows;
using GPOSettingsExplorer.Models;
using GPOSettingsExplorer.Services;

namespace GPOSettingsExplorer;

public partial class MainWindow
{
    /// <summary>
    /// One GPMC entry -> one previously captured, non-aborted MMC route,
    /// with a one-time manual review fallback. This NEVER initiates a
    /// broad native MMC inventory from an individual setting's value dialog.
    /// No GPO/SYSVOL writes or blind MMC dialog clicks.
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
            progress?.Report("No existing same-GPO/DC MMC snapshot. Use the explicit " +
                "single-GPO Scan MMC action in MMC Full Settings Inventory if needed. " +
                "No risky native inventory was launched from this setting. " +
                "You can open the selected GPO editor manually now.");
            return false;
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
    private RealSettingsScanResult? _kerberosSourceSnapshot;
    private bool _kerberosSourceBusy;
    private RealSettingsScanResult? _srpSourceSnapshot;
    private bool _srpSourceBusy;
    private GpoGppRegistrySourceEvidenceService.Snapshot? _gppRegistrySourceSnapshot;
    private bool _gppRegistrySourceBusy;
    private RealSettingsScanResult? _advancedAuditSourceSnapshot;
    private bool _advancedAuditSourceBusy;

    /// <summary>
    /// Explicit one-GPO, one-source read only. This never walks the domain or
    /// launches MMC and is distinct from the GPMC index / exact-row status.
    /// </summary>
    private async Task<bool> CheckSecurityTemplateSourceAsync(
        PolicySettingInfo setting, Window dialogOwner, IProgress<string>? progress)
    {
        if (_kerberosSourceBusy || _domainContext is null ||
            !(KerberosPolicyMetadataService.IsKnownKerberosSetting(setting) ||
              GpoKnownAccountPolicyService.TryDescribe(setting, out _)))
            return false;
        var gpo = _gpos.FirstOrDefault(item => item.Id == setting.GpoId);
        var domain = _domainContext.DomainName;
        var dc = DomainConnectionState.GetServerFor(domain);
        if (gpo is null || string.IsNullOrWhiteSpace(dc) ||
            !gpo.DomainName.Equals(domain, StringComparison.OrdinalIgnoreCase))
        {
            progress?.Report("A same-GPO connected domain and pinned DC are required.");
            return false;
        }

        _kerberosSourceBusy = true;
        try
        {
            progress?.Report("Reading only GptTmpl.inf of " + gpo.DisplayName +
                " from pinned DC " + dc + " (read-only, no MMC and no GPO writes)...");
            var captured = await Task.Run(() =>
                new RealSettingsSourceService().Scan(gpo, securityOnly: true));
            if (_domainContext is null ||
                !captured.Matches(_domainContext.DomainName,
                    DomainConnectionState.GetServerFor(_domainContext.DomainName)) ||
                captured.GpoId != gpo.Id || !captured.Matches(domain, dc))
                throw new InvalidOperationException(
                    "Connection changed during the source check. Snapshot discarded.");

            _kerberosSourceSnapshot = captured;
            progress?.Report("GptTmpl.inf capture finished. Typed Kerberos or [System Access] " +
                "values can now be compared against exact SHA-256-bound GPMC records. " +
                "Stored agreement is NOT native MMC or effective client RSoP.");
            return true;
        }
        catch (Exception ex)
        {
            CrashLogService.Write("Kerberos pinned security source", ex);
            progress?.Report("Security template source read failed: " + ex.Message);
            return false;
        }
        finally
        {
            _kerberosSourceBusy = false;
        }
    }

    /// <summary>
    /// One explicit GPO/scope-specific SRP Registry.pol read, never the
    /// user's actual HKCU/HKLM or another GPO. A parsed source may confirm
    /// only path and rule security level, not full client precedence/RSoP.
    /// </summary>
    private async Task<bool> CheckSrpSourceAsync(
        PolicySettingInfo setting, Window dialogOwner, IProgress<string>? progress)
    {
        if (_srpSourceBusy || _domainContext is null ||
            !SoftwareRestrictionMmcRouteService.IsPathRule(setting))
            return false;

        var gpo = _gpos.FirstOrDefault(g => g.Id == setting.GpoId);
        var domain = _domainContext.DomainName;
        var dc = DomainConnectionState.GetServerFor(domain);
        if (gpo is null || string.IsNullOrWhiteSpace(dc) ||
            !gpo.DomainName.Equals(domain, StringComparison.OrdinalIgnoreCase))
        {
            progress?.Report("A selected same-domain GPO and pinned DC are required.");
            return false;
        }

        _srpSourceBusy = true;
        try
        {
            progress?.Report("Reading only " + setting.Scope + " Registry.pol for " +
                gpo.DisplayName + " on pinned DC " + dc +
                " (read-only; no local registry, MMC, or policy writes)...");
            var captured = await Task.Run(() =>
                new RealSettingsSourceService().Scan(
                    gpo, registryOnly: true, registryScope: setting.Scope));
            if (_domainContext is null ||
                !captured.Matches(domain, dc) ||
                !captured.Matches(_domainContext.DomainName,
                    DomainConnectionState.GetServerFor(_domainContext.DomainName)) ||
                captured.GpoId != gpo.Id)
                throw new InvalidOperationException(
                    "GPO/domain/pinned DC changed during the SRP source read. " +
                    "The snapshot was discarded.");

            _srpSourceSnapshot = captured;
            progress?.Report("SRP source capture completed. Exactly one saved " +
                "ItemData PATH and Safer rule security level will be compared; " +
                "other SRP flags, MMC values, other GPOs and client RSoP are NOT verified.");
            return true;
        }
        catch (Exception ex)
        {
            CrashLogService.Write("SRP pinned Registry.pol read", ex);
            progress?.Report("SRP Registry.pol read failed: " + ex.Message);
            return false;
        }
        finally
        {
            _srpSourceBusy = false;
        }
    }

    /// <summary>
    /// One selected GPP Registry.xml only, parsed into metadata without
    /// storing the original value=, filter payload or private attributes.
    /// This is not a client-registry read or an effective GPP CSE check.
    /// </summary>
    private async Task<bool> CheckGppRegistrySourceAsync(
        PolicySettingInfo setting, Window dialogOwner, IProgress<string>? progress)
    {
        if (_gppRegistrySourceBusy || _domainContext is null ||
            !setting.Extension.Equals("GppRegistry", StringComparison.OrdinalIgnoreCase))
            return false;

        var gpo = _gpos.FirstOrDefault(item => item.Id == setting.GpoId);
        var domain = _domainContext.DomainName;
        var dc = DomainConnectionState.GetServerFor(domain);
        if (gpo is null || string.IsNullOrWhiteSpace(dc) ||
            !gpo.DomainName.Equals(domain, StringComparison.OrdinalIgnoreCase))
        {
            progress?.Report("A same-GPO connected domain and pinned DC are required.");
            return false;
        }

        _gppRegistrySourceBusy = true;
        try
        {
            progress?.Report("Reading only " + setting.Scope + " Preferences/Registry/Registry.xml " +
                "on pinned DC (no raw value data retained, no GPO writes)...");
            var captured = await Task.Run(() =>
                GpoGppRegistrySourceEvidenceService.Capture(gpo, setting.Scope));

            if (_domainContext is null ||
                !captured.Domain.Equals(domain, StringComparison.OrdinalIgnoreCase) ||
                !captured.DomainController.Equals(dc, StringComparison.OrdinalIgnoreCase) ||
                captured.GpoId != gpo.Id ||
                !DomainConnectionState.GetServerFor(_domainContext.DomainName)
                    .Equals(dc, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    "Connection changed during GPP source capture; snapshot discarded.");

            // Keep one per-scope snapshot only. Previous GPO/scope mismatches
            // remain blocked until the corresponding source is reread.
            _gppRegistrySourceSnapshot = captured;
            progress?.Report(captured.Status == "Read"
                ? "Pinned GPP Registry.xml metadata captured with SHA-256. " +
                  "Stored value data and client registry/RSoP were NOT compared."
                : "GPP Registry.xml was not captured completely (" + captured.Status +
                  "). No verified GPP metadata match is possible.");
            return captured.Status == "Read";
        }
        catch (Exception ex)
        {
            CrashLogService.Write("Pinned GPP Registry.xml source", ex);
            progress?.Report("GPP Registry source capture blocked: " + ex.Message);
            return false;
        }
        finally
        {
            _gppRegistrySourceBusy = false;
        }
    }

    /// <summary>
    /// Read only selected Computer Advanced Audit/audit.csv on one pinned DC.
    /// Never starts MMC, client auditpol, an all-GPO walk or SYSVOL writes.
    /// </summary>
    private async Task<bool> CheckAdvancedAuditSourceAsync(
        PolicySettingInfo setting, Window dialogOwner, IProgress<string>? progress)
    {
        if (_advancedAuditSourceBusy || _domainContext is null ||
            !GpoAdvancedAuditStoredSourceEvidenceService.IsSupported(setting))
            return false;

        var gpo = _gpos.FirstOrDefault(item => item.Id == setting.GpoId);
        var domain = _domainContext.DomainName;
        var dc = DomainConnectionState.GetServerFor(domain);
        if (gpo is null || string.IsNullOrWhiteSpace(dc) ||
            !gpo.DomainName.Equals(domain, StringComparison.OrdinalIgnoreCase))
        {
            progress?.Report("Same-domain GPO and pinned DC are required.");
            return false;
        }

        _advancedAuditSourceBusy = true;
        try
        {
            progress?.Report("Reading only selected GPO's Machine Audit/audit.csv " +
                "on pinned DC (read-only; no MMC or client auditpol)...");
            var captured = await Task.Run(() =>
                new RealSettingsSourceService().Scan(gpo, auditOnly: true));
            if (_domainContext is null || captured.GpoId != gpo.Id ||
                !captured.Matches(domain, dc) ||
                !captured.Matches(_domainContext.DomainName,
                    DomainConnectionState.GetServerFor(_domainContext.DomainName)))
                throw new InvalidOperationException(
                    "Domain/pinned DC changed during audit.csv capture; snapshot discarded.");

            _advancedAuditSourceSnapshot = captured;
            progress?.Report("Audit source captured. A unique System subcategory " +
                "GUID and canonical 0..4 value can now be compared with GPMC. " +
                "MMC, client auditpol, replication and effective RSoP remain unverified.");
            return true;
        }
        catch (Exception ex)
        {
            CrashLogService.Write("Pinned Advanced Audit CSV source", ex);
            progress?.Report("Advanced Audit source read failed: " + ex.Message);
            return false;
        }
        finally
        {
            _advancedAuditSourceBusy = false;
        }
    }

    private Task<bool> CheckSupportedStoredSourceAsync(
        PolicySettingInfo setting, Window dialogOwner, IProgress<string> progress) =>
        GpoAdvancedAuditStoredSourceEvidenceService.IsSupported(setting)
            ? CheckAdvancedAuditSourceAsync(setting, dialogOwner, progress)
            : setting.Extension.Equals("GppRegistry", StringComparison.OrdinalIgnoreCase)
                ? CheckGppRegistrySourceAsync(setting, dialogOwner, progress)
            : SoftwareRestrictionMmcRouteService.IsPathRule(setting)
                ? CheckSrpSourceAsync(setting, dialogOwner, progress)
                : CheckSecurityTemplateSourceAsync(setting, dialogOwner, progress);

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
            },
            (setting, window, progress) =>
                CheckSupportedStoredSourceAsync(setting, window, progress),
            setting =>
            {
                var target = _gpos.FirstOrDefault(item => item.Id == setting.GpoId);
                var context = DomainConnectionState.Context;
                if (target is null || _domainContext is null || context is null ||
                    !target.DomainName.Equals(_domainContext.DomainName,
                        StringComparison.OrdinalIgnoreCase) ||
                    !context.DomainName.Equals(target.DomainName,
                        StringComparison.OrdinalIgnoreCase) ||
                    !context.ConnectedServer.Equals(_domainContext.ConnectedServer,
                        StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException(
                        "Selected GPO or pinned DC changed. Reconnect before opening MMC.");
                _gpmService.OpenEditor(target, _domainContext.DomainDistinguishedName);
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

        var domain = _domainContext?.DomainName ?? string.Empty;
        var dc = DomainConnectionState.GetServerFor(domain);
        var validSources = new[] {
                _kerberosSourceSnapshot, _srpSourceSnapshot,
                _advancedAuditSourceSnapshot, _realSourceSnapshot
            }
            .Where(snapshot => snapshot is not null &&
                snapshot.Matches(domain, dc))
            .OrderByDescending(snapshot => snapshot!.CapturedAt)
            .ToArray();

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

            var isSrp = SoftwareRestrictionMmcRouteService.IsPathRule(setting);
            var isKerberos = KerberosPolicyMetadataService.IsKnownKerberosSetting(setting);
            var isAccount = GpoKnownAccountPolicyService.TryDescribe(setting, out _);
            var isGppRegistry = setting.Extension.Equals(
                "GppRegistry", StringComparison.OrdinalIgnoreCase);
            var isAdvancedAudit =
                GpoAdvancedAuditStoredSourceEvidenceService.IsSupported(setting);
            var scopeFolder = setting.Scope.Equals("User", StringComparison.OrdinalIgnoreCase)
                ? "User" : "Machine";

            // A newer Kerberos-only source capture must not hide an older
            // scope-specific Registry.pol capture (or the reverse). Choose
            // only snapshots that actually include the required source path.
            var storedSource = validSources.FirstOrDefault(source =>
                source!.GpoId == setting.GpoId &&
                source.Files.Any(file => isSrp
                    ? file.SourceFile.Replace('/', '\\').EndsWith(
                        "\\" + scopeFolder + "\\Registry.pol",
                        StringComparison.OrdinalIgnoreCase)
                    : isAdvancedAudit
                        ? file.SourceFile.Replace('/', '\\').EndsWith(
                            "\\Audit\\audit.csv", StringComparison.OrdinalIgnoreCase)
                        : (isKerberos || isAccount) &&
                          file.SourceFile.Replace('/', '\\').EndsWith(
                              "\\SecEdit\\GptTmpl.inf",
                              StringComparison.OrdinalIgnoreCase)));

            var sourceStatus = string.Empty;
            var sourceExplanation = string.Empty;
            if (isSrp)
            {
                var srp = GpoSoftwareRestrictionStoredSourceEvidenceService.Compare(
                    setting, byGpo[setting.GpoId], storedSource, domain, dc);
                sourceStatus = srp.Status;
                sourceExplanation = srp.Explanation;
            }
            else if (isKerberos)
            {
                var kerberos = GpoKerberosStoredSourceEvidenceService.Compare(
                    setting, byGpo[setting.GpoId], storedSource, domain, dc);
                sourceStatus = kerberos.Status;
                sourceExplanation = kerberos.Explanation;
            }
            else if (isAccount)
            {
                var account = GpoAccountStoredSourceEvidenceService.Compare(
                    setting, byGpo[setting.GpoId], storedSource, domain, dc);
                sourceStatus = account.Status;
                sourceExplanation = account.Explanation;
            }
            else if (isGppRegistry)
            {
                var gpp = GpoGppRegistrySourceEvidenceService.Compare(
                    setting, byGpo[setting.GpoId],
                    _gppRegistrySourceSnapshot, domain, dc);
                sourceStatus = gpp.Status;
                sourceExplanation = gpp.Explanation;
            }
            else if (isAdvancedAudit)
            {
                var audit = GpoAdvancedAuditStoredSourceEvidenceService.Compare(
                    setting, byGpo[setting.GpoId], storedSource, domain, dc);
                sourceStatus = audit.Status;
                sourceExplanation = audit.Explanation;
            }

            result.Add(new GpoExactPathsWindow.RouteRow(setting,
                setting.GpoName, setting.Scope, setting.Extension,
                setting.SettingName, hint, decision.Status,
                observedPath, decision.Explanation,
                sourceStatus, sourceExplanation));
        }
        return result;
    }

}

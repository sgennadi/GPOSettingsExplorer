using GPOSettingsExplorer.Models;
using GPOSettingsExplorer.Services;

namespace GPOSettingsExplorer;

public partial class MainWindow
{
    /// <summary>
    /// On-demand, read-only comparison of the currently indexed Kerberos GPMC
    /// record with a newly captured SecEdit GptTmpl.inf on the pinned DC.
    /// Never auto-runs at startup and never treats a stored-source agreement
    /// as MMC row identity or effective client RSoP.
    /// </summary>
    private async Task<GpoKerberosStoredSourceEvidenceService.Result>
        VerifyKerberosStoredSourceAsync(
            GpoInfo gpo, PolicySettingInfo setting,
            IProgress<string> progress, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var session = DomainConnectionState.Context;
        var domain = _domainContext?.DomainName ?? string.Empty;
        var dc = session?.ConnectedServer ?? string.Empty;
        if (session is null ||
            string.IsNullOrWhiteSpace(dc) ||
            !session.DomainName.Equals(domain, StringComparison.OrdinalIgnoreCase) ||
            !gpo.DomainName.Equals(domain, StringComparison.OrdinalIgnoreCase) ||
            setting.GpoId != gpo.Id ||
            !_gpos.Any(x => x.Id == gpo.Id) ||
            !KerberosPolicyMetadataService.IsKnownKerberosSetting(setting))
        {
            return new GpoKerberosStoredSourceEvidenceService.Result(
                GpoKerberosStoredSourceEvidenceService.WrongSnapshot,
                "A matching selected GPO, typed Kerberos setting, domain and " +
                "session-pinned DC are required. No source read was performed.");
        }

        // Freeze the already indexed GPMC data for the same GPO. This is a
        // cached report snapshot, not a live GPMC fetch or client-side RSoP.
        var gpmcRows = _settings.Where(row => row.GpoId == gpo.Id).ToArray();
        progress.Report("Reading only the selected GPO's SecEdit GptTmpl.inf from " +
            "the pinned DC (read-only, no MMC, no writes)...");
        var fresh = await Task.Run(() =>
            new RealSettingsSourceService().Scan(gpo,
                cancellationToken, securityOnly: true), cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        // A UI connection switch must never silently combine the original
        // GPMC identity with a new DC's security-template snapshot.
        session = DomainConnectionState.Context;
        if (_domainContext is null || session is null ||
            !session.DomainName.Equals(domain, StringComparison.OrdinalIgnoreCase) ||
            !session.ConnectedServer.Equals(dc, StringComparison.OrdinalIgnoreCase) ||
            !_domainContext.DomainName.Equals(domain, StringComparison.OrdinalIgnoreCase) ||
            !_domainContext.ConnectedServer.Equals(dc, StringComparison.OrdinalIgnoreCase))
        {
            return new GpoKerberosStoredSourceEvidenceService.Result(
                GpoKerberosStoredSourceEvidenceService.WrongSnapshot,
                "The domain or pinned DC changed while the source was being read. " +
                "The captured data was discarded.");
        }

        progress.Report("Checking exact GPO/source identities, typed values " +
            "and full GptTmpl.inf SHA-256...");
        var result = GpoKerberosStoredSourceEvidenceService.Compare(
            setting, gpmcRows, fresh, domain, dc);

        // Both the per-setting value window and Exact MMC Paths refer to
        // the same one-GPO, one-DC source snapshot. Keep their read-only
        // statuses consistent without opening another MMC editor.
        _kerberosSourceSnapshot = fresh;
        if (_exactPathsWindow is { IsLoaded: true })
            _exactPathsWindow.RefreshRows();
        return result;
    }
}

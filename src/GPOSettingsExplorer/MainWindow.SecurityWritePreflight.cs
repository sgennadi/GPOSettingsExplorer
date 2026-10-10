using GPOSettingsExplorer.Models;
using GPOSettingsExplorer.Services;

namespace GPOSettingsExplorer;

public partial class MainWindow
{
    /// <summary>
    /// Conservative preflight before ANY direct GptTmpl.inf edit. Does not
    /// replace the Windows authorization enforced at the actual AD/SYSVOL write.
    /// Re-reads GPMC AD permission evidence and tests the pinned SYSVOL GPT.INI.
    /// No write occurs when either source is missing/negative/unknown.
    /// </summary>
    private async Task EnsureSecurityWritePreflightAsync(GpoInfo gpo)
    {
        EditingGuard.EnsureEnabled("Edit security policy");
        var context = _domainContext;
        var pinned = DomainConnectionState.Context;
        if (context is null || pinned is null ||
            !context.DomainName.Equals(gpo.DomainName, StringComparison.OrdinalIgnoreCase) ||
            !pinned.DomainName.Equals(context.DomainName, StringComparison.OrdinalIgnoreCase) ||
            !pinned.ConnectedServer.Equals(context.ConnectedServer, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "AD connection or pinned DC changed; reconnect before Security Settings editing.");

        var evidence = await EvaluateGpoCapabilityAsync(gpo);
        EditingGuard.EnsureEnabled("Edit security policy");
        pinned = DomainConnectionState.Context;
        if (pinned is null ||
            !pinned.DomainName.Equals(context.DomainName, StringComparison.OrdinalIgnoreCase) ||
            !pinned.ConnectedServer.Equals(context.ConnectedServer, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "AD connection changed during access checks; no write was attempted.");

        if (!evidence.CanEditSettings)
            throw new UnauthorizedAccessException(
                "Conservative Security Settings preflight blocked the edit: " +
                "both matching-token AD GPO Edit permission AND a writable handle " +
                "to the selected DC's SYSVOL are required. These checks are " +
                "not full Windows AuthZ; the actual write still enforces access. " +
                "Diagnostics: " + evidence.Details);
    }
}

using System.Windows;
using System.Windows.Controls;
using GPOSettingsExplorer.Models;
using GPOSettingsExplorer.Services;
using Microsoft.Win32;

namespace GPOSettingsExplorer;

/// <summary>
/// Unified 2.0 read-only entry point. Independent service results are cached
/// only for one currently loaded GPO and invalidated on each source switch.
/// </summary>
public sealed partial class GpoAdvancedAnalysisWindow
{
    private GpoConsistencyReport? _unifiedHealth;
    private GpoImpactPreview? _unifiedImpact;
    private GpoUnifiedPlatformReport? _unifiedReport;

    private TabItem SetupUnifiedTab()
    {
        var tab = Tab("Unified overview", out var panel);
        AddAction(panel, "Read current GPO source", CaptureCurrentAsync);
        AddAction(panel, "Open offline GPMC backup...", OpenBackupAsync);
        AddAction(panel, "Scan source security", SecurityScanAsync);
        AddAction(panel, "Check pinned DC health...", CheckUnifiedHealthAsync);
        AddAction(panel, "Inspect direct GPO links...", InspectUnifiedLinksAsync);
        AddAction(panel, "Build unified overview", BuildUnifiedOverviewAsync);
        AddAction(panel, "Export anonymized JSON...", ExportUnifiedSummaryAsync);
        AddAction(panel, "Optional local AI triage...", UnifiedLocalAiAsync);

        panel.Children.Add(new TextBlock
        {
            Text = "All modules remain READ ONLY. Capture the intended GPO/backup first; " +
                "security, pinned-DC health and direct-link inventory are separate " +
                "operator-invoked observations. Missing data is UNKNOWN. " +
                "The AI model is configured under Optional cloud & local AI, uses " +
                "127.0.0.1 only, and receives aggregate counts after explicit consent.",
            MinWidth = 280,
            MaxWidth = 950,
            Margin = new Thickness(7, 7, 7, 5),
            TextWrapping = TextWrapping.Wrap,
            Foreground = UiStyle.MutedBrush
        });
        return tab;
    }

    private void InvalidateUnifiedObservations()
    {
        _lastSecurity = null;
        _unifiedHealth = null;
        _unifiedImpact = null;
        _unifiedReport = null;
        _lastGitOpsReview = null;
    }

    private (GpoInfo Gpo, DomainContext Context) RequirePinnedLiveSource()
    {
        var source = Source();
        var gpo = Target();
        var context = DomainConnectionState.Context ??
            throw new InvalidOperationException(
                "A connected, session-pinned domain controller is required. " +
                "Offline backups can still be analyzed without AD health/link reads.");
        if (source.GpoId != gpo.Id ||
            !source.Domain.Equals(context.DomainName, StringComparison.OrdinalIgnoreCase) ||
            !source.DomainController.Equals(context.ConnectedServer,
                StringComparison.OrdinalIgnoreCase) ||
            !gpo.DomainName.Equals(context.DomainName,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "The currently loaded GPO source is not from the session-pinned DC. " +
                "Recapture the selected live GPO before mixing any AD/SYSVOL data.");
        return (gpo, context);
    }

    private async Task<string> CheckUnifiedHealthAsync()
    {
        var (gpo, _) = RequirePinnedLiveSource();
        var health = await Task.Run(() =>
            new GpoConsistencyService().Inspect(gpo, _lifetime.Token),
            _lifetime.Token);
        // Check after the task too: the UI cannot switch source during RunAsync,
        // but never retain a result for a changed connection.
        var source = Source();
        if (health.GpoId != source.GpoId ||
            !health.Domain.Equals(source.Domain, StringComparison.OrdinalIgnoreCase) ||
            !health.DomainController.Equals(source.DomainController,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "Health result belongs to a changed GPO/domain/DC.");
        _unifiedHealth = health;
        _unifiedReport = null;
        return health.ToText();
    }

    private async Task<string> InspectUnifiedLinksAsync()
    {
        var (gpo, context) = RequirePinnedLiveSource();
        var preview = await Task.Run(() =>
        {
            _lifetime.Token.ThrowIfCancellationRequested();
            var links = new GpoLinkService();
            var targets = links.LoadTargets(
                context.DomainDistinguishedName, context.ConfigurationNamingContext);
            _lifetime.Token.ThrowIfCancellationRequested();
            var observed = links.LoadLinks(targets, new[] { gpo });
            return GpoImpactPreviewService.Build(gpo, observed,
                context.DomainName, context.ConnectedServer, true);
        }, _lifetime.Token);
        var source = Source();
        if (preview.GpoId != source.GpoId ||
            !preview.Domain.Equals(source.Domain, StringComparison.OrdinalIgnoreCase) ||
            !preview.PinnedDc.Equals(source.DomainController,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Link evidence belongs to another source.");
        _unifiedImpact = preview;
        _unifiedReport = null;
        return preview.ToText();
    }

    private Task<string> BuildUnifiedOverviewAsync()
    {
        _unifiedReport = GpoUnifiedPlatformService.Build(
            Source(), _lastSecurity, _unifiedHealth, _unifiedImpact, _mapping);
        return Task.FromResult(_unifiedReport.ToText());
    }

    private Task<string> ExportUnifiedSummaryAsync()
    {
        var report = _unifiedReport ??
            throw new InvalidOperationException(
                "Build unified overview from current source before exporting.");
        var save = new SaveFileDialog
        {
            Title = "Export anonymized Unified Platform report (local)",
            Filter = "Anonymized platform JSON (*.json)|*.json",
            FileName = "GPO-Unified-aggregate-only.json",
            AddExtension = true
        };
        if (save.ShowDialog(this) != true)
            return Task.FromResult("Anonymized report export canceled.");
        GpoUnifiedPlatformService.ExportSafeJson(save.FileName, report);
        return Task.FromResult("AGGREGATE-ONLY REPORT EXPORTED LOCALLY\n" +
            save.FileName + "\nContains only counters, statuses and timestamps. " +
            "No raw source values, names, paths, user identities or credentials. " +
            "Review destination access permissions before sharing; no upload occurred.");
    }

    private async Task<string> UnifiedLocalAiAsync()
    {
        var report = _unifiedReport ??
            throw new InvalidOperationException(
                "Run Build unified overview after your desired source and health checks first.");
        if (MessageBox.Show(this,
                "Send ONLY anonymous aggregate counters to an already installed " +
                "Ollama model at http://127.0.0.1:11434?\n\n" +
                "No domain, GPO, setting, source value, OU, SID, script or credential " +
                "is included. No DNS, proxy, redirects, model download, cloud " +
                "connection or GPO write is used. AI conclusions are unverified.\n\n" +
                "Continue?",
                "Explicit LOCAL AI triage consent",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
            return "Local AI triage canceled; no network request was made.";
        return await GpoLocalAiService.ExplainUnifiedAsync(
            report, _aiModel.Text.Trim(), _lifetime.Token);
    }
}

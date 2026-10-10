using System.Windows;
using GPOSettingsExplorer.Models;
using GPOSettingsExplorer.Services;
using Microsoft.Win32;

namespace GPOSettingsExplorer;

public partial class MainWindow
{
    private async void ExportFullEvidence_Click(object sender, RoutedEventArgs e)
    {
        if (_domainContext is null)
            return;

        var gpo = UnifiedGpoFilterCombo.SelectedItem as GpoInfo ??
                  GpoGrid.SelectedItem as GpoInfo;
        if (gpo is null)
        {
            MessageBox.Show(this, "Select a specific GPO first.",
                "Export evidence bundle", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (MessageBox.Show(this,
                "The local evidence ZIP includes raw GPO source values and possibly " +
                "sensitive paths, SIDs and GPMC XML. It is NOT redacted and will NOT " +
                "be uploaded automatically. Handle it as confidential domain data.\n\n" +
                "Generate a read-only evidence archive for this GPO?",
                "Export comprehensive evidence", MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        var save = new SaveFileDialog
        {
            Title = "Export GPO evidence locally (ZIP)",
            Filter = "Evidence archives (*.zip)|*.zip",
            FileName = $"GPO-Evidence-{gpo.Id:N}.zip",
            AddExtension = true
        };
        if (save.ShowDialog(this) != true)
            return;

        SetBusy(true, "Capturing GPO source, health and impact evidence...");
        try
        {
            var context = _domainContext;
            var allGpos = _gpos.ToArray();
            var sources = await Task.Run(() => new RealSettingsSourceService().Scan(gpo));
            var health = await Task.Run(() => new GpoConsistencyService().Inspect(gpo));

            GpoImpactPreview impact;
            try
            {
                impact = await Task.Run(() =>
                {
                    var targets = _gpoLinkService.LoadTargets(
                        context.DomainDistinguishedName, context.ConfigurationNamingContext);
                    var links = _gpoLinkService.LoadLinks(targets, allGpos);
                    return GpoImpactPreviewService.Build(gpo, links,
                        context.DomainName, context.ConnectedServer, true);
                });
            }
            catch (Exception ex)
            {
                CrashLogService.Write("Evidence archive link inventory", ex);
                impact = GpoImpactPreviewService.Build(gpo,
                    Array.Empty<GpoLinkInfo>(), context.DomainName,
                    context.ConnectedServer, false);
            }

            string? gpmcXml = null;
            try
            {
                gpmcXml = await Task.Run(() =>
                    _gpmService.GenerateXmlReport(context.DomainName, gpo.Id));
            }
            catch (Exception ex)
            {
                CrashLogService.Write("Evidence archive GPMC XML", ex);
            }

            var target = save.FileName;
            await Task.Run(() => GpoEvidenceArchiveService.Export(
                target, gpo, sources, health, impact, gpmcXml));
            StatusText.Text = "Local evidence ZIP saved: " + target +
                ((sources.IsPartial || health.HasBlockingIssues || !impact.LinkInventoryComplete ||
                  string.IsNullOrWhiteSpace(gpmcXml)) ? " (partial coverage)" : "");
            MessageBox.Show(this,
                "Evidence ZIP saved locally:\n" + target +
                "\n\nThis is a collection of source evidence, not an atomic snapshot " +
                "or validated client effective RSoP. Do not publicly share the archive.",
                "Export complete", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            CrashLogService.Write("Export full GPO evidence", ex);
            ErrorDialog.Show(this, "Export GPO evidence",
                "Cannot create a complete local evidence ZIP.", ex);
            StatusText.Text = "GPO evidence archive failed";
        }
        finally
        {
            SetBusy(false);
        }
    }
}

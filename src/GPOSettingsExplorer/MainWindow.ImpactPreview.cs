using System.Windows;
using GPOSettingsExplorer.Models;
using GPOSettingsExplorer.Services;

namespace GPOSettingsExplorer;

public partial class MainWindow
{
    private async void GpoImpactPreview_Click(object sender, RoutedEventArgs e)
    {
        if (_domainContext is null)
            return;
        var selected = UnifiedGpoFilterCombo.SelectedItem as GpoInfo ??
                       GpoGrid.SelectedItem as GpoInfo;
        if (selected is null)
        {
            MessageBox.Show(this, "Select a specific GPO first.", "GPO Impact Preview",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        ImpactPreviewButton.IsEnabled = false;
        SetBusy(true, "Loading current domain link evidence...");
        try
        {
            // Snapshot WPF collections before background execution.
            var allGpos = _gpos.ToArray();
            var context = _domainContext;
            var preview = await Task.Run(() =>
            {
                var targets = _gpoLinkService.LoadTargets(
                    context.DomainDistinguishedName, context.ConfigurationNamingContext);
                var links = _gpoLinkService.LoadLinks(targets, allGpos);
                return GpoImpactPreviewService.Build(selected, links,
                    context.DomainName, context.ConnectedServer, inventoryComplete: true);
            });
            new GpoImpactPreviewWindow(preview, selected) { Owner = this }.ShowDialog();
            StatusText.Text = "Read-only impact preview: " + preview.Summary;
        }
        catch (Exception ex)
        {
            CrashLogService.Write("GPO impact preview", ex);
            ErrorDialog.Show(this, "GPO Impact Preview",
                "GPO link inventory could not be completed. No scope was inferred.", ex);
        }
        finally
        {
            ImpactPreviewButton.IsEnabled = true;
            SetBusy(false);
        }
    }
}

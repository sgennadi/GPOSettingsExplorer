using System.Windows;
using GPOSettingsExplorer.Models;

namespace GPOSettingsExplorer;

public partial class MainWindow
{
    private void CrossDcVersions_Click(object sender, RoutedEventArgs e)
    {
        if (_domainContext is null)
            return;
        var gpo = UnifiedGpoFilterCombo.SelectedItem as GpoInfo ??
                  GpoGrid.SelectedItem as GpoInfo;
        if (gpo is null)
        {
            MessageBox.Show(this, "Select a specific GPO first.",
                "Compare DC versions", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        new GpoCrossDcConsistencyWindow(gpo,
            _domainContext.DomainDistinguishedName,
            _domainContext.ConnectedServer)
        {
            Owner = this
        }.ShowDialog();
    }
}

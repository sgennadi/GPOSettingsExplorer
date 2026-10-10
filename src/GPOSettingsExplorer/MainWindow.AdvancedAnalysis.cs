using System.Windows;
using GPOSettingsExplorer.Models;

namespace GPOSettingsExplorer;

public partial class MainWindow
{
    private void AdvancedAnalysis_Click(object sender, RoutedEventArgs e)
    {
        var selected = UnifiedGpoFilterCombo.SelectedItem as GpoInfo ??
                       GpoGrid.SelectedItem as GpoInfo;
        var source = selected is not null && _realSourceSnapshot is not null &&
                     _realSourceSnapshot.GpoId == selected.Id &&
                     _realSourceSnapshot.Matches(selected.DomainName,
                         Services.DomainConnectionState.GetServerFor(
                             selected.DomainName))
            ? _realSourceSnapshot : null;

        new GpoAdvancedAnalysisWindow(selected, source)
        {
            Owner = this
        }.ShowDialog();
    }
}
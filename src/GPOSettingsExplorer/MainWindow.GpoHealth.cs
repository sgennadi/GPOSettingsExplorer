using System.Windows;
using GPOSettingsExplorer.Models;
using GPOSettingsExplorer.Services;

namespace GPOSettingsExplorer;

public partial class MainWindow
{
    private async void GpoHealth_Click(object sender, RoutedEventArgs e)
    {
        if (_domainContext is null)
            return;
        var selected = UnifiedGpoFilterCombo.SelectedItem as GpoInfo ??
                       GpoGrid.SelectedItem as GpoInfo;
        if (selected is null)
        {
            MessageBox.Show(this, "Select a specific GPO first.", "GPO Health Check",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        GpoHealthButton.IsEnabled = false;
        SetBusy(true, "Checking selected GPO on the connected DC (read-only)...");
        try
        {
            var report = await Task.Run(() => new GpoConsistencyService().Inspect(selected));
            new GpoConsistencyWindow(report) { Owner = this }.ShowDialog();
            StatusText.Text = "GPO Health Check: " + report.Summary;
        }
        catch (Exception ex)
        {
            CrashLogService.Write("GPO Health Check", ex);
            ErrorDialog.Show(this, "GPO Health Check",
                "Cannot finish the read-only GPO integrity check.", ex);
        }
        finally
        {
            GpoHealthButton.IsEnabled = true;
            SetBusy(false);
        }
    }
}

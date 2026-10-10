using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using GPOSettingsExplorer.Models;
using GPOSettingsExplorer.Services;

namespace GPOSettingsExplorer;

public partial class MainWindow
{
    private void GpoGrid_PreviewMouseRightButtonDown(
        object sender, MouseButtonEventArgs e)
    {
        // WPF ContextMenu has a detached visual tree. Select the clicked
        // DataGrid row BEFORE the menu opens; never act on a stale selection.
        var current = e.OriginalSource as DependencyObject;
        while (current is not null && current is not DataGridRow)
        {
            current = current is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(current)
                : LogicalTreeHelper.GetParent(current);
        }
        if (current is DataGridRow { Item: GpoInfo gpo })
            GpoGrid.SelectedItem = gpo;
        else
            GpoGrid.SelectedItem = null; // Empty-space context click cannot act on stale GPO.
    }

    private async void OpenGpoSettingsReport_Click(
        object sender, RoutedEventArgs e) =>
        await OpenGpoSettingsReportAsync();

    private async Task OpenGpoSettingsReportAsync()
    {
        if (_domainContext is null || GpoGrid.SelectedItem is not GpoInfo selected)
        {
            MessageBox.Show(this, "Select a GPO row first.",
                "GPO Settings", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var context = _domainContext;
        SetBusy(true, "Reading a GPMC XML report for the selected GPO...");
        try
        {
            MarkGpoRecent(selected.Id);
            var xml = await Task.Run(() =>
                _gpmService.GenerateXmlReport(context.DomainName, selected.Id));
            var report = await Task.Run(() =>
                GpoSettingsReportService.Parse(selected, xml));

            var viewer = new GpoSettingsReportWindow(selected, report, () =>
                _gpmService.OpenEditor(selected, context.DomainDistinguishedName))
            {
                Owner = this
            };
            SetBusy(false);
            viewer.ShowDialog();
            StatusText.Text = "Read-only GPO Settings report displayed: " + selected.DisplayName;
        }
        catch (Exception ex)
        {
            CrashLogService.Write("GPO Settings report viewer", ex);
            ErrorDialog.Show(this, "GPO Settings",
                "Cannot display the selected GPO's GPMC XML report. " +
                "The domain GPO was not modified.", ex);
            StatusText.Text = "Could not open GPO Settings report";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void GpoContextScripts_Click(object sender, RoutedEventArgs e) =>
        MainTabs.SelectedItem = GpoScriptsTab;

    private void GpoContextSecurity_Click(object sender, RoutedEventArgs e) =>
        MainTabs.SelectedItem = SecurityTab;
}

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

        await OpenGpoSettingsReportForGpoAsync(selected, this);
    }

    // A detail window may describe a different GPO than the main grid's
    // current selection. Never silently reuse GpoGrid.SelectedItem here.
    private async Task OpenGpoSettingsReportForGpoAsync(
        GpoInfo selected, Window owner, string? initialSearch = null,
        PolicySettingInfo? sourceSetting = null)
    {
        if (_domainContext is null)
            return;
        if (sourceSetting is not null && sourceSetting.GpoId != selected.Id)
            throw new InvalidOperationException(
                "Selected GPMC setting and the requested report belong to different GPOs.");
        var context = _domainContext;
        SetBusy(true, "Reading a GPMC XML report for the selected GPO...");
        try
        {
            MarkGpoRecent(selected.Id);
            var xml = await Task.Run(() =>
                _gpmService.GenerateXmlReport(context.DomainName, selected.Id,
                    GpoSettingsReportService.MaxXmlCharacters * 2 + 4096));
            var report = await Task.Run(() =>
                GpoSettingsReportService.Parse(selected, xml));

            GpmcRegistryReportLocator.Reference? exactPreference = null;
            var isRegistrySource = sourceSetting is not null &&
                GpoSettingSourceProvenance.IsGppRegistrySource(sourceSetting);
            if (isRegistrySource &&
                GpmcRegistryReportLocator.TryCreate(sourceSetting,
                    out var validReference))
                exactPreference = validReference;

            var viewer = new GpoSettingsReportWindow(selected, report, () =>
                _gpmService.OpenEditor(selected, context.DomainDistinguishedName),
                isRegistrySource ? null : initialSearch, exactPreference,
                requestedGppRegistryLookup: isRegistrySource)
            {
                Owner = owner
            };
            SetBusy(false);
            viewer.ShowDialog();
            StatusText.Text = "Read-only GPO Settings report displayed: " + selected.DisplayName;
        }
        catch (Exception ex)
        {
            CrashLogService.Write("GPO Settings report viewer", ex);
            ErrorDialog.Show(owner, "GPO Settings",
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

using System.Windows;
using System.Windows.Controls;
using GPOSettingsExplorer.Models;
using GPOSettingsExplorer.Services;

namespace GPOSettingsExplorer;

public partial class MainWindow
{
    private NativePolicyScanResult? _nativeSourceResult;
    private CancellationTokenSource? _nativeReadCancellation;

    private async void ReadGpoNativeSources_Click(object sender, RoutedEventArgs e)
    {
        if (_nativeReadCancellation is not null || _domainContext is null)
            return;

        // Explicit reference GPO, never silently scan all 185 policies.
        var gpo = NativePolicyGpoCombo.SelectedItem as GpoInfo ??
                  UnifiedGpoFilterCombo.SelectedItem as GpoInfo ??
                  GpoGrid.SelectedItem as GpoInfo;
        if (gpo is null)
        {
            MessageBox.Show(this,
                "Select a reference GPO in All Settings or on the GPOs tab first. " +
                "The source-file reader runs only for that one GPO.",
                "Select a GPO", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        NativePolicyGpoCombo.SelectedItem = gpo;
        AdvancedSourcesExpander.IsExpanded = true;
        AllSettingsSubTabs.SelectedItem = NativePolicySourcesTab;

        using var cancellation = new CancellationTokenSource();
        _nativeReadCancellation = cancellation;
        NativePolicyScanButton.IsEnabled = false;
        NativePolicyCancelButton.IsEnabled = true;
        NativePolicyCoverageText.Text = "Reading GPO source files from the pinned SYSVOL DC...";
        SetBusy(true, "Reading registry.pol and security template (read-only)...");

        try
        {
            var result = await Task.Run(
                () => GpoNativeSourceReader.Scan(gpo, cancellation.Token),
                cancellation.Token);

            if (cancellation.IsCancellationRequested)
                return;

            _nativeSourceResult = result;
            NativePolicyGrid.ItemsSource = result.Rows;
            NativePolicyStatusGrid.ItemsSource = result.Sources;
            NativePolicyValueText.Text = "";
            NativePolicyCoverageText.Text =
                "Pinned DC: " + result.PinnedServer +
                " | " + result.Coverage;
            StatusText.Text = "GPO source files: " + result.Coverage;
            await RefreshUnifiedCatalogAsync();
        }
        catch (OperationCanceledException)
        {
            NativePolicyCoverageText.Text =
                "Source read canceled. Previous results, if any, are retained.";
            StatusText.Text = "GPO source inspection canceled";
        }
        catch (Exception ex)
        {
            NativePolicyCoverageText.Text =
                "Unable to read the reference GPO's pinned source files: " + ex.Message;
            CrashLogService.Write("Read GPO native source files", ex);
            ErrorDialog.Show(this, "GPO source reader",
                "The read-only native source scan could not complete.", ex);
        }
        finally
        {
            _nativeReadCancellation = null;
            NativePolicyScanButton.IsEnabled = true;
            NativePolicyCancelButton.IsEnabled = false;
            SetBusy(false);
        }
    }

    private void CancelGpoNativeSources_Click(object sender, RoutedEventArgs e) =>
        _nativeReadCancellation?.Cancel();

    private void NativePolicyGrid_SelectionChanged(
        object sender, SelectionChangedEventArgs e)
    {
        var row = NativePolicyGrid.SelectedItem as NativePolicyEvidence;
        NativePolicyValueText.Text = row is null ? "" :
            "GPO: " + row.GpoName + Environment.NewLine +
            "Scope: " + row.Scope + Environment.NewLine +
            "File: " + row.SourceLocation + Environment.NewLine +
            "Category: " + row.Category + Environment.NewLine +
            "Key: " + row.RegistryKey + Environment.NewLine +
            "Value name: " + row.SettingName + Environment.NewLine +
            "Stored value: " + row.Value + Environment.NewLine +
            "This is not proof of effective application or ADMX Enabled/Disabled.";
    }

    private void ShowNativeSourceRow(NativePolicyEvidence observed)
    {
        // Data is from a completed explicit file read, not an old MMC handle.
        AdvancedSourcesExpander.IsExpanded = true;
        AllSettingsSubTabs.SelectedItem = NativePolicySourcesTab;
        if (!ReferenceEquals(NativePolicyGrid.ItemsSource, _nativeSourceResult?.Rows))
            NativePolicyGrid.ItemsSource = _nativeSourceResult?.Rows;

        NativePolicyGrid.SelectedItem = observed;
        NativePolicyGrid.ScrollIntoView(observed);
        NativePolicyGrid.Focus();
    }
}

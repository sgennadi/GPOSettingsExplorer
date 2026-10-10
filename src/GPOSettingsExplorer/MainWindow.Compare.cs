using System.Windows;
using System.Windows.Input;
using GPOSettingsExplorer.Models;
using GPOSettingsExplorer.Services;

namespace GPOSettingsExplorer;

public partial class MainWindow
{
    private IReadOnlyList<GpoComparisonRow> _comparisonRows = Array.Empty<GpoComparisonRow>();
    private IReadOnlyList<GpoConflictInfo> _conflictRows = Array.Empty<GpoConflictInfo>();

    private void CompareGpos_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureSettingsIndexAvailable())
            return;

        if (CompareLeftGpoCombo.SelectedItem is not GpoInfo left ||
            CompareRightGpoCombo.SelectedItem is not GpoInfo right)
        {
            MessageBox.Show(this, "Select both GPOs to compare.", "Compare GPOs",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (left.Id == right.Id)
        {
            MessageBox.Show(this, "Select two different GPOs.", "Compare GPOs",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _comparisonRows = GpoSettingsComparisonService.Compare(
            _settings.Where(s => s.GpoId == left.Id),
            _settings.Where(s => s.GpoId == right.Id));
        ApplyCompareFilter();
        CompareResultTabs.SelectedIndex = 0;
        StatusText.Text = $"Compared '{left.DisplayName}' with '{right.DisplayName}'";
    }

    private async void FindConflicts_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureSettingsIndexAvailable())
            return;

        // Link evidence matters. If the link index has never been loaded,
        // try AD once; an unavailable link index remains explicitly Unknown.
        if (_linkTargets.Count == 0 && _domainContext is not null)
            await LoadLinksAsync();

        _conflictRows = GPOSettingsExplorer.Services.GpoConflictAnalysisService.Analyze(
            _settings.ToArray(), _links.ToArray(), _gpos.ToArray());
        ApplyConflictFilter();
        CompareResultTabs.SelectedIndex = 1;
        var duplicates = _conflictRows.Count(r => r.Kind == "Duplicate");
        var ambiguous = _conflictRows.Count(r => r.Kind == "Ambiguous index");
        var differences = _conflictRows.Count(r => r.Kind == "Different values");
        StatusText.Text =
            $"{differences:N0} differing-value candidates; {duplicates:N0} identical-value candidates; " +
            $"{ambiguous:N0} ambiguous index identities. " +
            "Technical GPMC XML details are excluded. Scope overlap is indicative, not effective RSoP.";
    }

    private void CompareSearchBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        ApplyCompareFilter();
        ApplyConflictFilter();
    }

    private void CompareFilter_Changed(object sender, RoutedEventArgs e) => ApplyCompareFilter();

    private void ApplyCompareFilter()
    {
        if (CompareGrid is null)
            return;

        var search = CompareSearchBox?.Text?.Trim();
        IEnumerable<GpoComparisonRow> source = _comparisonRows;

        if (CompareDifferencesOnlyCheck?.IsChecked == true)
            source = source.Where(row => row.IsDifferent);

        if (!string.IsNullOrWhiteSpace(search))
            source = source.Where(row =>
                row.SearchText.Contains(search, StringComparison.CurrentCultureIgnoreCase));

        var rows = source.ToArray();
        CompareGrid.ItemsSource = rows;
        CompareCountText.Text = $"{rows.Length:N0} shown / {_comparisonRows.Count:N0} total";
    }

    private void ApplyConflictFilter()
    {
        if (ConflictGrid is null)
            return;

        var search = CompareSearchBox?.Text?.Trim();
        IEnumerable<GpoConflictInfo> source = _conflictRows;

        if (!string.IsNullOrWhiteSpace(search))
            source = source.Where(row =>
                row.SearchText.Contains(search, StringComparison.CurrentCultureIgnoreCase));

        var rows = source.ToArray();
        ConflictGrid.ItemsSource = rows;
        ConflictCountText.Text =
            $"{rows.Length:N0} shown / {_conflictRows.Count:N0} differences and duplicates (verify each sample before cleanup)";
    }

    private void ConflictGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e) =>
        ShowConflictDetails();

    private void ConflictDetails_Click(object sender, RoutedEventArgs e) =>
        ShowConflictDetails();

    private void ShowConflictDetails()
    {
        if (ConflictGrid.SelectedItem is not GpoConflictInfo conflict)
            return;

        // Snapshot UI collections on the dispatcher before starting any remote checks.
        var gpos = _gpos.ToArray();
        var filters = _wmiFilters.ToArray();
        var domainDn = _domainContext?.DomainDistinguishedName ?? string.Empty;
        var details = new GpoConflictDetailWindow(
            conflict,
            (computer, user) => Task.Run(() =>
                new GpoApplicabilityVerificationService().Verify(
                    conflict, gpos, filters, domainDn, computer, user)))
        {
            Owner = this
        };
        var action = details.ShowDialog();
        // Detail checks update a finding in memory. Refresh grid bindings after the dialog closes.
        ApplyConflictFilter();
        if (action != true)
            return;

        if (details.ReviewLinks)
        {
            // Leave link editing to the existing safety-guarded UI.
            var link = _links.FirstOrDefault(l => conflict.GpoIds.Contains(l.GpoId));
            if (link is not null)
                LinksGrid.SelectedItem = link;
            MainTabs.SelectedItem = GpoLinksTab;
            StatusText.Text = "Review GPO Links and filter/WMI conditions before changing Link Order.";
        }
        else
        {
            MainTabs.SelectedItem = AllSettingsTab;
            UnifiedAllGposCheckBox.IsChecked = true;
            UnifiedSourceCombo.SelectedIndex = 0;
            UnifiedStateCombo.SelectedIndex = 0;
            UnifiedSearchBox.Text = conflict.SettingName;
            _ = EnsureUnifiedCatalogReadyAsync();
            StatusText.Text = $"Unified Settings: searching for {conflict.SettingName}";
        }
    }

    private bool EnsureSettingsIndexAvailable()
    {
        if (_settings.Count > 0)
            return true;

        MessageBox.Show(this,
            "Build the settings index first. Use 'Build settings index' on the GPOs tab.",
            "Settings Index Required",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
        return false;
    }

}

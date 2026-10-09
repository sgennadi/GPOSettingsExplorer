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

        var leftMap = BuildPolicyMap(_settings.Where(s => s.GpoId == left.Id));
        var rightMap = BuildPolicyMap(_settings.Where(s => s.GpoId == right.Id));

        var identities = leftMap.Keys
            .Union(rightMap.Keys, StringComparer.OrdinalIgnoreCase)
            .OrderBy(key => key, StringComparer.CurrentCultureIgnoreCase);

        var rows = new List<GpoComparisonRow>();

        foreach (var identity in identities)
        {
            leftMap.TryGetValue(identity, out var leftSetting);
            rightMap.TryGetValue(identity, out var rightSetting);

            var sample = leftSetting ?? rightSetting;
            if (sample is null)
                continue;

            var status = leftSetting is null
                ? "Right only"
                : rightSetting is null
                    ? "Left only"
                    : Equivalent(leftSetting, rightSetting)
                        ? "Same"
                        : "Different";

            rows.Add(new GpoComparisonRow
            {
                Identity = identity,
                Scope = sample.Scope,
                SettingName = sample.SettingName,
                Category = sample.Category,
                RegistryKey = sample.RegistryKey,
                RegistryValue = sample.RegistryValue,
                LeftState = leftSetting?.State ?? "<Not configured>",
                LeftValue = leftSetting?.Value ?? string.Empty,
                RightState = rightSetting?.State ?? "<Not configured>",
                RightValue = rightSetting?.Value ?? string.Empty,
                Status = status
            });
        }

        _comparisonRows = rows;
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
        var differences = _conflictRows.Count - duplicates;
        StatusText.Text =
            $"{differences:N0} differing-value candidates; {duplicates:N0} identical-value duplicates. " +
            "Scope overlap is indicative, not effective RSoP. Open a finding to verify RSoP/WMI/security on a computer.";
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
        if (details.ShowDialog() != true)
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
            SettingsSearchBox.Text = conflict.SettingName;
            MainTabs.SelectedIndex = 1;
            _settingsView.Refresh();
            StatusText.Text = $"Showing all occurrences of: {conflict.SettingName}";
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

    private static Dictionary<string, PolicySettingInfo> BuildPolicyMap(IEnumerable<PolicySettingInfo> settings) =>
        settings.GroupBy(PolicyIdentity, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

    private static string PolicyIdentity(PolicySettingInfo setting)
    {
        if (!string.IsNullOrWhiteSpace(setting.RegistryKey) ||
            !string.IsNullOrWhiteSpace(setting.RegistryValue))
            return $"{setting.Scope}|REG|{setting.RegistryKey}|{setting.RegistryValue}";

        return $"{setting.Scope}|NAME|{setting.Category}|{setting.SettingName}";
    }

    private static string NormalizeVariant(PolicySettingInfo setting) =>
        $"{setting.State.Trim()}|{setting.Value.Trim()}";

    private static bool Equivalent(PolicySettingInfo left, PolicySettingInfo right) =>
        left.State.Equals(right.State, StringComparison.OrdinalIgnoreCase) &&
        left.Value.Equals(right.Value, StringComparison.OrdinalIgnoreCase);
}

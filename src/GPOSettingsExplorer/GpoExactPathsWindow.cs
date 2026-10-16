using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using GPOSettingsExplorer.Models;
using GPOSettingsExplorer.Services;

namespace GPOSettingsExplorer;

/// <summary>
/// Operator's route coverage view over ALL indexed GPMC settings. A known
/// section is not a verified native row; the scope/GPO path and the current
/// observed MMC row are shown separately. Opening is always explicit.
/// </summary>
public sealed class GpoExactPathsWindow : Window
{
    public sealed record RouteRow(
        PolicySettingInfo Source,
        string Gpo, string Scope, string Extension, string Setting,
        string SectionHint, string Status, string ObservedExactPath,
        string Reason, string SourceStatus = "", string SourceExplanation = "")
    {
        public string SearchText => string.Join(" ", new[]
        {
            Gpo, Scope, Extension, Setting, SectionHint,
            Status, ObservedExactPath, Reason, SourceStatus, SourceExplanation
        });
    }

    private readonly Func<IReadOnlyList<RouteRow>> _refresh;
    private readonly Func<PolicySettingInfo, Window, IProgress<string>, Task<bool>> _open;
    private readonly Func<PolicySettingInfo, Window, IProgress<string>, Task<bool>> _checkSource;
    private readonly DataGrid _grid = new()
    {
        AutoGenerateColumns = false,
        IsReadOnly = true,
        CanUserAddRows = false,
        SelectionMode = DataGridSelectionMode.Single,
        EnableRowVirtualization = true,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto
    };
    private readonly TextBox _search = new() { MinWidth = 250 };
    private readonly TextBlock _summary = new()
    {
        TextWrapping = TextWrapping.Wrap,
        Foreground = UiStyle.MutedBrush
    };
    private readonly TextBlock _detail = new()
    {
        TextWrapping = TextWrapping.Wrap,
        Foreground = UiStyle.WarningBrush
    };
    private readonly Button _openSelected = new()
    {
        Content = "Find / open selected exact route...",
        IsEnabled = false,
        MinWidth = 220,
        Padding = new Thickness(12, 7, 12, 7)
    };
    private readonly Button _securitySource = new()
    {
        Content = "Check selected stored source...",
        IsEnabled = false,
        MinWidth = 205,
        Padding = new Thickness(12, 7, 12, 7),
        ToolTip = "Read-only, single-GPO pinned source: Kerberos GptTmpl.inf " +
                  "Account/Kerberos GptTmpl.inf, SRP Registry.pol or GPP Registry.xml. " +
                  "GPP comparison is metadata only; NOT MMC or client RSoP."
    };
    private readonly Button _refreshButton = new()
    {
        Content = "Refresh statuses",
        Padding = new Thickness(12, 7, 12, 7)
    };
    private ICollectionView? _view;
    private IReadOnlyList<RouteRow> _rows = Array.Empty<RouteRow>();

    public GpoExactPathsWindow(Func<IReadOnlyList<RouteRow>> refresh,
        Func<PolicySettingInfo, Window, IProgress<string>, Task<bool>> open,
        Func<PolicySettingInfo, Window, IProgress<string>, Task<bool>> checkSource)
    {
        _refresh = refresh ?? throw new ArgumentNullException(nameof(refresh));
        _open = open ?? throw new ArgumentNullException(nameof(open));
        _checkSource = checkSource ?? throw new ArgumentNullException(nameof(checkSource));

        Title = "Exact MMC Paths - indexed GPMC policy coverage";
        Width = 1400;
        Height = 760;
        MinWidth = 950;
        MinHeight = 470;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.CanResize;
        UiStyle.ApplyWindowDefaults(this);

        var root = new DockPanel { Margin = new Thickness(12) };
        Content = root;

        var header = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Margin = new Thickness(0, 0, 0, 8)
        };
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        header.Children.Add(new TextBlock
        {
            Text = "Source-aware exact MMC paths",
            FontWeight = FontWeights.SemiBold,
            FontSize = UiStyle.HeadingFontSize,
            TextWrapping = TextWrapping.Wrap
        });
        header.Children.Add(new TextBlock
        {
            Text = "All indexed GPMC records are listed. Known folders are SECTION hints. " +
                "An EXACT_ROW_OBSERVED result requires an explicit, unique same-GPO " +
                "MMC row from a read-only scan. Unsupported snap-ins, descriptive " +
                "XML children, custom views and unscanned GPOs are not silently " +
                "marked verified. Selected-GPO checks compare typed Kerberos and Account " +
                "Password/Lockout GptTmpl.inf, SRP Registry.pol path/security level, " +
                "or GPP Registry.xml item METADATA (not value data) against GPMC. " +
                "Neither a route nor a match proves effective client RSoP.",
            Foreground = UiStyle.WarningBrush,
            TextWrapping = TextWrapping.Wrap
        });

        var controls = new WrapPanel { Margin = new Thickness(0, 9, 0, 6) };
        header.Children.Add(controls);
        controls.Children.Add(new TextBlock
        {
            Text = "Filter:",
            VerticalAlignment = VerticalAlignment.Center
        });
        controls.Children.Add(_search);
        controls.Children.Add(_refreshButton);
        controls.Children.Add(_openSelected);
        controls.Children.Add(_securitySource);
        _search.TextChanged += (_, _) =>
        {
            _view?.Refresh();
            UpdateSummary();
        };
        _refreshButton.Click += (_, _) => RefreshRows();
        _grid.SelectionChanged += (_, _) => UpdateSelection();
        _grid.MouseDoubleClick += async (_, _) => await OpenSelectedAsync();
        _openSelected.Click += async (_, _) => await OpenSelectedAsync();
        _securitySource.Click += async (_, _) => await CheckSourceAsync();
        header.Children.Add(_summary);

        var footer = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);
        footer.Children.Add(_detail);

        void Column(string caption, string binding, double weight)
        {
            _grid.Columns.Add(new DataGridTextColumn
            {
                Header = caption, Binding = new Binding(binding),
                IsReadOnly = true,
                Width = new DataGridLength(weight, DataGridLengthUnitType.Star)
            });
        }
        Column("GPO", nameof(RouteRow.Gpo), 1.45);
        Column("Scope", nameof(RouteRow.Scope), 0.55);
        Column("GPMC extension", nameof(RouteRow.Extension), 1.15);
        Column("Configured setting", nameof(RouteRow.Setting), 2);
        Column("Candidate section", nameof(RouteRow.SectionHint), 2.1);
        Column("Route status", nameof(RouteRow.Status), 1.1);
        Column("Stored source evidence", nameof(RouteRow.SourceStatus), 1.3);
        Column("Observed exact MMC path", nameof(RouteRow.ObservedExactPath), 2.7);
        UiStyle.ApplyDataGridDefaults(_grid);
        root.Children.Add(_grid);

        RefreshRows();
    }

    public void RefreshRows()
    {
        var selected = (_grid.SelectedItem as RouteRow)?.Source;
        var rows = _refresh().ToArray();
        _rows = rows;
        _view = CollectionViewSource.GetDefaultView(rows);
        _view.Filter = row => row is RouteRow route &&
            (string.IsNullOrWhiteSpace(_search.Text) ||
             route.SearchText.Contains(_search.Text.Trim(),
                 StringComparison.CurrentCultureIgnoreCase));
        _grid.ItemsSource = _view;
        _grid.SelectedItem = rows.FirstOrDefault(row =>
            ReferenceEquals(row.Source, selected));
        UpdateSummary();
        UpdateSelection();
    }

    private void UpdateSummary()
    {
        if (_view is null)
            return;
        var all = _rows;
        var observed = all.Count(row => row.Status == GpoExactMmcRouteResolver.Ready);
        var sourceMatches = all.Count(row =>
            row.SourceStatus == GpoKerberosStoredSourceEvidenceService.Match);
        var sourceDifferences = all.Count(row =>
            row.SourceStatus == GpoKerberosStoredSourceEvidenceService.Mismatch);
        var srpMatches = all.Count(row =>
            row.SourceStatus == GpoSoftwareRestrictionStoredSourceEvidenceService.Match);
        var srpDifferences = all.Count(row =>
            row.SourceStatus == GpoSoftwareRestrictionStoredSourceEvidenceService.Mismatch);
        var accountMatches = all.Count(row =>
            row.SourceStatus == GpoAccountStoredSourceEvidenceService.Match);
        var accountDifferences = all.Count(row =>
            row.SourceStatus == GpoAccountStoredSourceEvidenceService.Mismatch);
        var gppMatches = all.Count(row =>
            row.SourceStatus == GpoGppRegistrySourceEvidenceService.Match);
        var gppDifferences = all.Count(row =>
            row.SourceStatus == GpoGppRegistrySourceEvidenceService.Mismatch);
        var ambiguous = all.Count(row =>
            row.Status is GpoExactMmcRouteResolver.Ambiguous or
                GpoExactMmcRouteResolver.SourceAmbiguous);
        _summary.Text = $"{_view.Cast<object>().Count():N0} shown / " +
            $"{all.Count:N0} indexed GPMC entries | " +
            $"{observed:N0} uniquely observed native rows | " +
            $"{ambiguous:N0} ambiguous source/row identities | " +
            $"{sourceMatches:N0} Kerberos stored agreements / {sourceDifferences:N0} differences | " +
            $"{srpMatches:N0} SRP saved path+level agreements / {srpDifferences:N0} differences | " +
            $"{accountMatches:N0} Password/Lockout agreements / {accountDifferences:N0} differences | " +
            $"{gppMatches:N0} GPP metadata agreements / {gppDifferences:N0} differences. " +
            "GPP data, native MMC and effective client RSoP remain NOT VERIFIED.";
    }

    private void UpdateSelection()
    {
        var row = _grid.SelectedItem as RouteRow;
        _openSelected.IsEnabled = row is not null;
        var isKerberos = row is not null &&
            KerberosPolicyMetadataService.IsKnownKerberosSetting(row.Source);
        var isSrp = row is not null &&
            SoftwareRestrictionMmcRouteService.IsPathRule(row.Source);
        var isAccount = row is not null &&
            GpoKnownAccountPolicyService.TryDescribe(row.Source, out _);
        var isGpp = row is not null &&
            row.Source.Extension.Equals("GppRegistry", StringComparison.OrdinalIgnoreCase);
        _securitySource.IsEnabled = isKerberos || isSrp || isAccount || isGpp;
        _securitySource.Content = isSrp ? "Check SRP Registry.pol..."
            : isGpp ? "Check GPP Registry.xml metadata..."
            : isAccount ? "Check Password/Lockout GptTmpl.inf..."
            : "Check Kerberos GptTmpl.inf...";
        _detail.Text = row is null
            ? "Select a setting, then find its exact native MMC location " +
              "with an optional SINGLE-GPO scan (requires confirmation)."
            : row.Status + " — " + row.Reason +
              (string.IsNullOrWhiteSpace(row.ObservedExactPath)
                  ? "" : "\nObserved: " + row.ObservedExactPath) +
              (string.IsNullOrEmpty(row.SourceStatus)
                  ? "" : "\nStored source: " + row.SourceStatus +
                    " — " + row.SourceExplanation);
    }

    private async Task CheckSourceAsync()
    {
        if (_grid.SelectedItem is not RouteRow selected)
            return;
        var srp = SoftwareRestrictionMmcRouteService.IsPathRule(selected.Source);
        var isAccount = GpoKnownAccountPolicyService.TryDescribe(selected.Source, out _);
        var isGpp = selected.Source.Extension.Equals(
            "GppRegistry", StringComparison.OrdinalIgnoreCase);
        if (!srp && !isAccount && !isGpp &&
            !KerberosPolicyMetadataService.IsKnownKerberosSetting(selected.Source))
            return;

        _securitySource.IsEnabled = false;
        _openSelected.IsEnabled = false;
        _refreshButton.IsEnabled = false;
        _detail.Text = srp
            ? "Reading scope-specific SRP Registry.pol on pinned DC, read-only..."
            : isGpp ? "Reading only selected scope Registry.xml metadata; no value data retained..."
            : isAccount ? "Reading [System Access] Password/Lockout GptTmpl.inf on pinned DC..."
            : "Reading typed Kerberos GptTmpl.inf on pinned DC...";
        try
        {
            var progress = new Progress<string>(text => _detail.Text = text);
            var read = await _checkSource(selected.Source, this, progress);
            RefreshRows();
            if (!read)
                _detail.Text = "The selected pinned source file could not be captured. " +
                    "No stored-value agreement was claimed. Reconnect and retry.";
            // On success RefreshRows displays the distinct stored-value and
            // MMC navigation statuses without claiming live or client RSoP.
        }
        catch (Exception ex)
        {
            _detail.Text = "Selected GPO stored-source check failed: " + ex.Message;
            CrashLogService.Write("GPO stored source check", ex);
        }
        finally
        {
            if (IsLoaded)
            {
                _refreshButton.IsEnabled = true;
                UpdateSelection();
            }
        }
    }

    private async Task OpenSelectedAsync()
    {
        if (_grid.SelectedItem is not RouteRow selected)
            return;
        _openSelected.IsEnabled = false;
        _refreshButton.IsEnabled = false;
        _securitySource.IsEnabled = false;
        _detail.Text = "Checking source, selected GPO and MMC snapshot...";
        try
        {
            var progress = new Progress<string>(text => _detail.Text = text);
            var success = await _open(selected.Source, this, progress);
            RefreshRows();
            _detail.Text = success
                ? "Exact native MMC row was selected and Properties requested. " +
                  "Confirm its actual dialog. This does NOT attest values or RSoP."
                : "No exact native policy row could safely be opened. Review " +
                  "the status, source identity and MMC skipped sections.";
        }
        catch (Exception ex)
        {
            _detail.Text = "Exact route failed: " + ex.Message;
            CrashLogService.Write("Exact MMC routes view", ex);
        }
        finally
        {
            if (IsLoaded)
            {
                _openSelected.IsEnabled = _grid.SelectedItem is RouteRow;
                _securitySource.IsEnabled = _grid.SelectedItem is RouteRow row &&
                    (KerberosPolicyMetadataService.IsKnownKerberosSetting(row.Source) ||
                     SoftwareRestrictionMmcRouteService.IsPathRule(row.Source) ||
                     GpoKnownAccountPolicyService.TryDescribe(row.Source, out _) ||
                     row.Source.Extension.Equals("GppRegistry",
                         StringComparison.OrdinalIgnoreCase));
                _refreshButton.IsEnabled = true;
            }
        }
    }
}

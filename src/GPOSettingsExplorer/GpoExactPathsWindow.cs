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
        string Reason, string SourceStatus = "", string SourceExplanation = "",
        string AdmxDeclaration = "")
    {
        public string SourcePathLabel
        {
            get
            {
                var source = GpoSourcePathPresentationService.Describe(Source);
                return source.GpoRelativeFile + " | " + source.ReportedTarget +
                    (string.IsNullOrWhiteSpace(AdmxDeclaration)
                        ? "" : " | " + AdmxDeclaration);
            }
        }

        public string SearchText => string.Join(" ", new[]
        {
            Gpo, Scope, Extension, Setting, SectionHint,
            Status, ObservedExactPath, Reason, SourceStatus, SourceExplanation,
            GpoSourcePathPresentationService.Describe(Source).SearchText,
            AdmxDeclaration
        });
    }

    private readonly Func<IReadOnlyList<RouteRow>> _refresh;
    private readonly Func<PolicySettingInfo, Window, IProgress<string>, Task<bool>> _open;
    private readonly Func<PolicySettingInfo, Window, IProgress<string>, Task<bool>> _checkSource;
    private readonly Action<PolicySettingInfo> _openManual;
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
                  "Account/Kerberos GptTmpl.inf, SRP Registry.pol, Audit/audit.csv or " +
                  "GPP Registry.xml. GPP is metadata only; NOT MMC or client RSoP."
    };
    private readonly Button _manualEditor = new()
    {
        Content = "Open GPO editor (manual)",
        IsEnabled = false,
        Padding = new Thickness(12, 7, 12, 7),
        ToolTip = "Opens selected GPO at its root with NO MMC inventory scan. " +
                  "Manual edits in MMC can change this GPO."
    };
    private readonly Button _copyLocation = new()
    {
        Content = "Copy source / registry path",
        IsEnabled = false,
        Padding = new Thickness(12, 7, 12, 7),
        ToolTip = "Copies only the reported source/target metadata, not raw setting values. " +
                  "The clipboard may include confidential GPO names and paths."
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
        Func<PolicySettingInfo, Window, IProgress<string>, Task<bool>> checkSource,
        Action<PolicySettingInfo> openManual)
    {
        _refresh = refresh ?? throw new ArgumentNullException(nameof(refresh));
        _open = open ?? throw new ArgumentNullException(nameof(open));
        _checkSource = checkSource ?? throw new ArgumentNullException(nameof(checkSource));
        _openManual = openManual ?? throw new ArgumentNullException(nameof(openManual));

        Title = "Exact MMC Paths - indexed GPMC policy coverage";
        Width = 1240;
        Height = 760;
        MinWidth = 700;
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
                "Advanced Audit System subcategory audit.csv or GPP Registry.xml item " +
                "METADATA (not value data) against GPMC. " +
                "Open GPO editor (manual) never performs a native MMC scan. " +
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
        controls.Children.Add(_manualEditor);
        controls.Children.Add(_securitySource);
        controls.Children.Add(_copyLocation);
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
        _manualEditor.Click += (_, _) => OpenManualEditor();
        _copyLocation.Click += (_, _) => CopyLocation();
        header.Children.Add(_summary);

        var footer = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);
        footer.Children.Add(new ScrollViewer
        {
            Content = _detail,
            MaxHeight = 165,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        });

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
        Column("Source file / registry path", nameof(RouteRow.SourcePathLabel), 2.5);
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
        var auditMatches = all.Count(row =>
            row.SourceStatus == GpoAdvancedAuditStoredSourceEvidenceService.Match);
        var auditDifferences = all.Count(row =>
            row.SourceStatus == GpoAdvancedAuditStoredSourceEvidenceService.Mismatch);
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
            $"{gppMatches:N0} GPP metadata agreements / {gppDifferences:N0} differences | " +
            $"{auditMatches:N0} Advanced Audit saved agreements / {auditDifferences:N0} differences. " +
            "GPP data, native MMC and effective client RSoP remain NOT VERIFIED.";
    }

    private void UpdateSelection()
    {
        var row = _grid.SelectedItem as RouteRow;
        _openSelected.IsEnabled = row is not null;
        _manualEditor.IsEnabled = row is not null;
        _copyLocation.IsEnabled = row is not null;
        var isKerberos = row is not null &&
            KerberosPolicyMetadataService.IsKnownKerberosSetting(row.Source);
        var isSrp = row is not null &&
            SoftwareRestrictionMmcRouteService.IsPathRule(row.Source);
        var isAccount = row is not null &&
            GpoKnownAccountPolicyService.TryDescribe(row.Source, out _);
        var isGpp = row is not null &&
            row.Source.Extension.Equals("GppRegistry", StringComparison.OrdinalIgnoreCase);
        var isAudit = row is not null &&
            GpoAdvancedAuditStoredSourceEvidenceService.IsSupported(row.Source);
        _securitySource.IsEnabled = isKerberos || isSrp || isAccount || isGpp || isAudit;
        _securitySource.Content = isSrp ? "Check SRP Registry.pol..."
            : isAudit ? "Check Advanced Audit audit.csv..."
            : isGpp ? "Check GPP Registry.xml metadata..."
            : isAccount ? "Check Password/Lockout GptTmpl.inf..."
            : "Check Kerberos GptTmpl.inf...";
        _detail.Text = row is null
            ? "Select a setting. Open GPO editor (manual) works without any scan; " +
              "exact row navigation requires separately captured MMC evidence."
            : row.Status + " — " + row.Reason +
              "\nGPMC source: " + GpoSourcePathPresentationService.Describe(row.Source).Family +
              "\nGPO-relative file: " + GpoSourcePathPresentationService.Describe(row.Source).GpoRelativeFile +
              "\nGPMC target: " + GpoSourcePathPresentationService.Describe(row.Source).ReportedTarget +
              "\nADMX target: " + (string.IsNullOrWhiteSpace(row.AdmxDeclaration)
                  ? "(not uniquely resolved)" : row.AdmxDeclaration) +
              "\nLimits: " + GpoSourcePathPresentationService.Describe(row.Source).Limitations +
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
        var isAudit = GpoAdvancedAuditStoredSourceEvidenceService.IsSupported(selected.Source);
        if (!srp && !isAccount && !isGpp && !isAudit &&
            !KerberosPolicyMetadataService.IsKnownKerberosSetting(selected.Source))
            return;

        _securitySource.IsEnabled = false;
        _openSelected.IsEnabled = false;
        _manualEditor.IsEnabled = false;
        _copyLocation.IsEnabled = false;
        _refreshButton.IsEnabled = false;
        _detail.Text = srp
            ? "Reading scope-specific SRP Registry.pol on pinned DC, read-only..."
            : isGpp ? "Reading only selected scope Registry.xml metadata; no value data retained..."
            : isAudit ? "Reading System Advanced Audit audit.csv on pinned DC..."
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

    private void OpenManualEditor()
    {
        if (_grid.SelectedItem is not RouteRow row)
            return;
        try
        {
            _openManual(row.Source);
            _detail.Text = "Opened selected GPO in its native MMC editor at the root. " +
                "No exact row/value, client RSoP or policy save was verified. " +
                "Manual changes in MMC may change the GPO.";
        }
        catch (Exception ex)
        {
            _detail.Text = "Manual GPO editor could not be opened: " + ex.Message;
            CrashLogService.Write("Open manual GPO editor from Exact MMC Paths", ex);
        }
    }

    private void CopyLocation()
    {
        if (_grid.SelectedItem is not RouteRow row)
            return;
        try
        {
            Clipboard.SetText(GpoSourcePathPresentationService.CopyLocationText(row.Source, row.AdmxDeclaration));
            _detail.Text = "Copied reported GPO source/target metadata without raw setting " +
                "values. The clipboard may contain confidential GPO names and paths.";
        }
        catch (Exception ex)
        {
            _detail.Text = "Source location could not be copied: " + ex.Message;
            CrashLogService.Write("Copy source location from Exact MMC Paths", ex);
        }
    }

    private async Task OpenSelectedAsync()
    {
        if (_grid.SelectedItem is not RouteRow selected)
            return;
        _openSelected.IsEnabled = false;
        _manualEditor.IsEnabled = false;
        _copyLocation.IsEnabled = false;
        _refreshButton.IsEnabled = false;
        _securitySource.IsEnabled = false;
        _detail.Text = "Checking previously observed same-GPO native MMC row...";
        try
        {
            var progress = new Progress<string>(text => _detail.Text = text);
            var success = await _open(selected.Source, this, progress);
            RefreshRows();
            _detail.Text = success
                ? "Exact native MMC row was selected and Properties requested. " +
                  "Confirm its actual dialog. This does NOT attest values or RSoP."
                : "No safely observed native MMC policy row was opened. " +
                  "Use Open GPO editor (manual) without any scan, or separately " +
                  "capture one GPO with MMC Full Settings Inventory.";
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
                _manualEditor.IsEnabled = _grid.SelectedItem is RouteRow;
                _copyLocation.IsEnabled = _grid.SelectedItem is RouteRow;
                _securitySource.IsEnabled = _grid.SelectedItem is RouteRow row &&
                    (KerberosPolicyMetadataService.IsKnownKerberosSetting(row.Source) ||
                     SoftwareRestrictionMmcRouteService.IsPathRule(row.Source) ||
                     GpoKnownAccountPolicyService.TryDescribe(row.Source, out _) ||
                     GpoAdvancedAuditStoredSourceEvidenceService.IsSupported(row.Source) ||
                     row.Source.Extension.Equals("GppRegistry",
                         StringComparison.OrdinalIgnoreCase));
                _refreshButton.IsEnabled = true;
            }
        }
    }
}

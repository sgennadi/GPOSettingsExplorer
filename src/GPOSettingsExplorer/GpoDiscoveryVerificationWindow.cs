using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using GPOSettingsExplorer.Models;
using GPOSettingsExplorer.Services;
using Microsoft.Win32;

namespace GPOSettingsExplorer;

/// <summary>
/// Operator-controlled review of previously captured GPO evidence. This window
/// cannot scan DCs, modify SYSVOL/GPO/AD or write an approval back to the domain.
/// Each unit approval lasts only for this dialog and its pinned source snapshot.
/// </summary>
public sealed class GpoDiscoveryVerificationWindow : Window
{
    private readonly IReadOnlyList<GpoDiscoveryEvidenceService.Evidence> _sourceRows;
    private readonly IReadOnlyList<AdmxPolicyDefinition>? _catalog;
    private readonly string _definitionsDirectory;
    private readonly string _language;
    private readonly string _gpoName;
    private readonly string _domainAndDc;
    private readonly string _coverage;
    private readonly Dictionary<string, GpoDiscoveryEvidenceService.VerifiedUnitMapping> _approvals =
        new(StringComparer.Ordinal);
    private readonly DataGrid _grid = new()
    {
        AutoGenerateColumns = false,
        IsReadOnly = true,
        CanUserAddRows = false,
        SelectionMode = DataGridSelectionMode.Single,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto
    };
    private readonly TextBox _search = new() { MinWidth = 170, Width = 300 };
    private readonly ComboBox _filter = new() { Width = 235, MinWidth = 160 };
    private readonly TextBox _details = new()
    {
        IsReadOnly = true,
        AcceptsReturn = true,
        TextWrapping = TextWrapping.Wrap,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
        FontFamily = UiStyle.MonospaceFontFamily,
        FontSize = UiStyle.MonospaceFontSize,
        Padding = new Thickness(10)
    };
    private readonly TextBlock _summary = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _selectedStatus = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Button _review = new()
    {
        Content = "Review minutes / hours...",
        MinWidth = 155,
        ToolTip = "Requires independent confirmation of the exact ADMX element and MMC display units."
    };
    private readonly List<DisplayRow> _rows = new();
    private ICollectionView? _view;

    public GpoDiscoveryVerificationWindow(
        string gpoName, string domain, string domainController,
        IReadOnlyList<GpoDiscoveryEvidenceService.Evidence> evidence,
        IReadOnlyList<AdmxPolicyDefinition>? catalog,
        string definitionsDirectory, string language, string coverage)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        _gpoName = gpoName;
        _domainAndDc = domain + " / pinned DC: " +
            (string.IsNullOrWhiteSpace(domainController) ? "UNKNOWN" : domainController);
        _sourceRows = evidence.ToArray();
        _catalog = catalog;
        _definitionsDirectory = definitionsDirectory;
        _language = language;
        _coverage = coverage;

        Title = "Discovery & Verification - READ ONLY";
        Width = 1230;
        Height = 800;
        MinWidth = 670;
        MinHeight = 450;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        UiStyle.ApplyWindowDefaults(this);
        BuildLayout();
        RefreshRows();
    }

    private void BuildLayout()
    {
        var root = new DockPanel { Margin = new Thickness(12) };

        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        header.Children.Add(new TextBlock
        {
            Text = _gpoName,
            FontWeight = FontWeights.SemiBold,
            FontSize = UiStyle.HeadingFontSize,
            TextWrapping = TextWrapping.Wrap
        });
        header.Children.Add(new TextBlock
        {
            Text = _domainAndDc + " | " + _coverage,
            TextWrapping = TextWrapping.Wrap,
            Foreground = UiStyle.MutedBrush
        });
        header.Children.Add(new TextBlock
        {
            Text = "Captured evidence only. No automatic unit inference, GPO writes, " +
                "effective RSoP claims or cross-DC verification. " +
                "Manual approvals are session-only and bound to SHA-256 fingerprints.",
            Foreground = UiStyle.WarningBrush,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 5, 0, 0)
        });
        header.Children.Add(_summary);
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        var toolbar = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        toolbar.Children.Add(new TextBlock
        {
            Text = "Search:", VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0)
        });
        toolbar.Children.Add(_search);
        _filter.ItemsSource = new[]
        {
            "All evidence", "Ready for review", "Verified conversion",
            "Unverified / blocked", "Mismatches"
        };
        _filter.SelectedIndex = 0;
        toolbar.Children.Add(_filter);
        _search.TextChanged += (_, _) => _view?.Refresh();
        _filter.SelectionChanged += (_, _) => _view?.Refresh();
        var export = new Button { Content = "Export review TSV...", MinWidth = 135 };
        var reset = new Button { Content = "Clear session approvals", MinWidth = 135 };
        var close = new Button { Content = "Close", MinWidth = 75 };
        toolbar.Children.Add(_review);
        toolbar.Children.Add(export);
        toolbar.Children.Add(reset);
        toolbar.Children.Add(close);
        _review.Click += (_, _) => ReviewSelected();
        export.Click += (_, _) => ExportReview();
        reset.Click += (_, _) =>
        {
            _approvals.Clear();
            RefreshRows();
        };
        close.Click += (_, _) => Close();
        DockPanel.SetDock(toolbar, Dock.Top);
        root.Children.Add(toolbar);

        AddColumn("Setting / native row", nameof(DisplayRow.Setting), 2.0);
        AddColumn("Scope", nameof(DisplayRow.Scope), 0.7);
        AddColumn("MMC value", nameof(DisplayRow.MmcValue), 1.0);
        AddColumn("Stored value", nameof(DisplayRow.StoredValue), 1.0);
        AddColumn("Source type", nameof(DisplayRow.SourceType), 0.9);
        AddColumn("Review eligibility", nameof(DisplayRow.ReviewStatus), 1.4);
        AddColumn("Value verification", nameof(DisplayRow.Verification), 1.6);
        AddColumn("Registry target", nameof(DisplayRow.RegistryTarget), 2.3);
        UiStyle.ApplyDataGridDefaults(_grid);
        _grid.SelectionChanged += (_, _) => UpdateSelection();

        var tabs = new TabControl();
        tabs.Items.Add(new TabItem { Header = "Discovered policy evidence", Content = _grid });

        var detailPanel = new DockPanel { Margin = new Thickness(8) };
        var info = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
        info.Children.Add(new TextBlock
        {
            Text = "Selected source, MMC route, provenance and verification reason",
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap
        });
        info.Children.Add(_selectedStatus);
        DockPanel.SetDock(info, Dock.Top);
        detailPanel.Children.Add(info);
        detailPanel.Children.Add(_details);
        tabs.Items.Add(new TabItem { Header = "Selected row / diagnostics", Content = detailPanel });
        root.Children.Add(tabs);
        Content = root;
    }

    private void AddColumn(string title, string property, double star)
    {
        _grid.Columns.Add(new DataGridTextColumn
        {
            Header = title,
            Binding = new Binding(property),
            Width = new DataGridLength(star, DataGridLengthUnitType.Star),
            IsReadOnly = true
        });
    }

    private sealed class DisplayRow
    {
        public required GpoDiscoveryEvidenceService.Evidence Evidence { get; init; }
        public required GpoDiscoveryUnitReviewService.ReviewCandidate Candidate { get; init; }
        public required string ReviewStatus { get; init; }
        public string Setting => Evidence.SettingName;
        public string Scope => Evidence.Scope;
        public string MmcValue => Evidence.MmcObservedValue;
        public string StoredValue => Evidence.SourceStoredValue;
        public string SourceType => Evidence.SourceStoredType;
        public string Verification => Evidence.ValueVerificationStatus;
        public string RegistryTarget => Evidence.RegistryTarget;
        public string SearchText => string.Join(" ", Setting, Scope, MmcValue, StoredValue,
            SourceType, ReviewStatus, Verification, RegistryTarget, Evidence.MmcObservedSection);
    }

    private void RefreshRows()
    {
        var selectedHash = (_grid.SelectedItem as DisplayRow)?.Evidence.IdentityHash;
        _rows.Clear();
        foreach (var original in _sourceRows)
        {
            var candidate = GpoDiscoveryUnitReviewService.Inspect(original, _catalog);
            var result = original;
            string reviewStatus = candidate.Status;
            if (_approvals.TryGetValue(original.IdentityHash, out var approved))
            {
                try
                {
                    if (!candidate.CanReview)
                        throw new InvalidOperationException("Eligibility changed: " + candidate.Status);
                    var live = GpoDiscoveryUnitReviewService.CaptureDefinition(candidate,
                        _definitionsDirectory, _language);
                    if (live.DefinitionSha256 != approved.DefinitionEvidenceSha256 ||
                        live.PolicyIdentitySha256 != approved.IdentitySha256 ||
                        live.SourceFileSha256 != approved.SourceFileSha256)
                        throw new InvalidOperationException("Review fingerprint or source changed.");
                    result = GpoDiscoveryEvidenceService.ApplyVerifiedUnitMappings(
                        new[] { original }, new[] { approved })[0];
                    result = GpoDiscoveryEvidenceService.VerifyLiteralRegistryValues(
                        new[] { result })[0];
                    reviewStatus = result.UnitMappingStatus;
                }
                catch (Exception ex)
                {
                    reviewStatus = "STALE_OR_UNVERIFIED_RULE";
                    result = original with
                    {
                        UnitMappingStatus = "UNVERIFIED_RULE",
                        SourceUnit = string.Empty,
                        UnitMappingEvidence = null,
                        ValueVerificationStatus = "SOURCE_RECORD_LOCATED_VALUE_NOT_COMPARED",
                        Notes = original.Notes + " Reviewed unit mapping invalid: " + ex.Message
                    };
                }
            }
            _rows.Add(new DisplayRow
            {
                Evidence = result,
                Candidate = candidate,
                ReviewStatus = reviewStatus
            });
        }

        _view = CollectionViewSource.GetDefaultView(_rows.ToArray());
        _view.Filter = MatchesFilter;
        _grid.ItemsSource = _view;
        var observed = _rows.Count(r => r.Evidence.NavigationStatus == "MMC_ROW_OBSERVED");
        var ready = _rows.Count(r => r.Candidate.CanReview && r.ReviewStatus != "VERIFIED_EXPLICIT");
        var verified = _rows.Count(r => r.ReviewStatus == "VERIFIED_EXPLICIT");
        var mismatches = _rows.Count(r => r.Verification.StartsWith("MISMATCH_", StringComparison.Ordinal));
        _summary.Text = string.Format(CultureInfo.InvariantCulture,
            "{0:N0} indexed | {1:N0} native MMC rows | {2:N0} review candidates | {3:N0} session approvals | {4:N0} mismatches",
            _rows.Count, observed, ready, verified, mismatches);
        _grid.SelectedItem = _rows.FirstOrDefault(r => r.Evidence.IdentityHash == selectedHash);
        UpdateSelection();
    }

    private bool MatchesFilter(object input)
    {
        if (input is not DisplayRow row)
            return false;
        var search = _search.Text?.Trim();
        if (!string.IsNullOrWhiteSpace(search) &&
            !row.SearchText.Contains(search, StringComparison.CurrentCultureIgnoreCase))
            return false;
        return _filter.SelectedIndex switch
        {
            1 => row.Candidate.CanReview && row.ReviewStatus != "VERIFIED_EXPLICIT",
            2 => row.ReviewStatus == "VERIFIED_EXPLICIT",
            3 => row.ReviewStatus != "VERIFIED_EXPLICIT" &&
                 !row.Verification.StartsWith("MATCH_", StringComparison.Ordinal),
            4 => row.Verification.StartsWith("MISMATCH_", StringComparison.Ordinal),
            _ => true
        };
    }

    private void UpdateSelection()
    {
        if (_grid.SelectedItem is not DisplayRow row)
        {
            _details.Text = "Select a discovered setting to inspect its source evidence.";
            _selectedStatus.Text = "No setting selected.";
            _review.IsEnabled = false;
            return;
        }
        _review.IsEnabled = row.Candidate.CanReview && row.ReviewStatus != "VERIFIED_EXPLICIT";
        _selectedStatus.Text = row.ReviewStatus + " | " + row.Candidate.Reason;
        _selectedStatus.Foreground = _review.IsEnabled
            ? UiStyle.AccentBrush : UiStyle.WarningBrush;
        var e = row.Evidence;
        _details.Text = string.Join("\n", new[]
        {
            "GPO: " + e.GpoId.ToString("D"), "Scope: " + e.Scope,
            "Extension: " + e.Extension, "Setting: " + e.SettingName,
            "Candidate MMC section: " + e.MmcSection,
            "Observed MMC section: " + e.MmcObservedSection,
            "MMC navigation: " + e.MmcRowNavigation,
            "MMC state: " + e.MmcObservedState, "MMC value: " + e.MmcObservedValue,
            "Source: " + e.SourceType, "Source status: " + e.SourceStatus,
            "Registry target: " + e.RegistryTarget,
            "Source type: " + e.SourceStoredType, "Stored value: " + e.SourceStoredValue,
            "Source file SHA-256: " + e.SourceFileHash,
            "Policy identity SHA-256: " + e.IdentityHash,
            "Navigation status: " + e.NavigationStatus,
            "Value verification: " + e.ValueVerificationStatus,
            "Unit review: " + row.ReviewStatus,
            "Review reference: " + (e.UnitMappingEvidence?.ReviewReference ?? "(none)"),
            "ADMX / ADML source: " + (row.Candidate.Policy?.AdmxFile ?? "(unmapped)"),
            "Decimal element: " + (row.Candidate.Element?.Id ?? "(unmapped)"),
            "Reason: " + row.Candidate.Reason,
            "Notes: " + e.Notes,
            "",
            "Important: observed MMC text is not effective client RSoP."
        });
    }

    private void ReviewSelected()
    {
        if (_grid.SelectedItem is not DisplayRow selected || !selected.Candidate.CanReview)
            return;

        try
        {
            var fingerprint = GpoDiscoveryUnitReviewService.CaptureDefinition(
                selected.Candidate, _definitionsDirectory, _language);
            var dialog = new GpoDiscoveryUnitReviewDialog(selected.Candidate, fingerprint)
            {
                Owner = this
            };
            if (dialog.ShowDialog() != true || dialog.ApprovedReference is null)
                return;

            // Re-read the pinned ADMX/ADML files after the operator's review.
            var fresh = GpoDiscoveryUnitReviewService.CaptureDefinition(
                selected.Candidate, _definitionsDirectory, _language);
            if (fresh != fingerprint)
                throw new InvalidOperationException("ADMX/ADML changed while the review was open.");

            var rule = GpoDiscoveryUnitReviewService.Approve(
                selected.Candidate, fresh, dialog.ApprovedReference);
            _approvals[selected.Evidence.IdentityHash] = rule;
            RefreshRows();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Unit review blocked",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ExportReview()
    {
        // Revalidate all session approvals immediately before writing the TSV.
        RefreshRows();
        if (MessageBox.Show(this,
            "The TSV includes unredacted registry paths and Group Policy values. " +
            "Keep it local and confidential. This does not attest effective client RSoP.\n\nExport?",
            "Sensitive GPO evidence", MessageBoxButton.YesNo,
            MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        var save = new SaveFileDialog
        {
            Title = "Export reviewed discovery evidence (local only)",
            FileName = "Discovery-Verification-" + DateTime.Now.ToString("yyyyMMdd-HHmmss",
                CultureInfo.InvariantCulture) + ".tsv",
            Filter = "Tab-separated evidence (*.tsv)|*.tsv",
            DefaultExt = ".tsv",
            AddExtension = true
        };
        if (save.ShowDialog(this) != true)
            return;

        try
        {
            File.WriteAllText(save.FileName,
                GpoDiscoveryEvidenceService.ExportTsv(_rows.Select(row => row.Evidence)),
                new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            MessageBox.Show(this, "Local read-only evidence exported:\n" + save.FileName,
                "Export completed", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Export failed",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}

/// <summary>One deliberate operator approval, never a policy write.</summary>
internal sealed class GpoDiscoveryUnitReviewDialog : Window
{
    public string? ApprovedReference { get; private set; }

    public GpoDiscoveryUnitReviewDialog(
        GpoDiscoveryUnitReviewService.ReviewCandidate candidate,
        GpoDiscoveryUnitReviewService.DefinitionFingerprint fingerprint)
    {
        Title = "Review explicit minutes-to-hours mapping";
        Width = 870;
        Height = 710;
        MinWidth = 570;
        MinHeight = 430;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        UiStyle.ApplyWindowDefaults(this);

        var root = new DockPanel { Margin = new Thickness(12) };
        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        header.Children.Add(new TextBlock
        {
            Text = "Manual, source-specific unit verification",
            FontSize = UiStyle.HeadingFontSize,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap
        });
        header.Children.Add(new TextBlock
        {
            Text = "The ADMX/ADML decimal element does not, by itself, establish minutes. " +
                "Check the policy's official meaning and the native MMC representation independently.",
            Foreground = UiStyle.WarningBrush,
            TextWrapping = TextWrapping.Wrap
        });
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        var buttons = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        var checkbox = new CheckBox
        {
            Content = new TextBlock
            {
                Text = "I independently confirmed that this exact ADMX element stores minutes " +
                    "and this native MMC row displays hours (not inferred from a label or numeric range).",
                TextWrapping = TextWrapping.Wrap
            },
            IsThreeState = false
        };
        buttons.Children.Add(checkbox);
        var referenceLabel = new TextBlock
        {
            Text = "Review / change-ticket reference (6-128 characters):",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 7, 0, 0)
        };
        buttons.Children.Add(referenceLabel);
        var reviewReference = new TextBox { MinWidth = 210, MaxLength = 128 };
        buttons.Children.Add(reviewReference);

        var actionRow = new WrapPanel
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 8, 0, 0)
        };
        var approve = new Button { Content = "Approve for this session", MinWidth = 145 };
        var cancel = new Button { Content = "Cancel", MinWidth = 90 };
        actionRow.Children.Add(approve);
        actionRow.Children.Add(cancel);
        buttons.Children.Add(actionRow);
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);

        var e = candidate.Evidence;
        var content = new TextBox
        {
            AcceptsReturn = true,
            IsReadOnly = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            FontFamily = UiStyle.MonospaceFontFamily,
            FontSize = UiStyle.MonospaceFontSize,
            Padding = new Thickness(10),
            Text = string.Join("\n", new[]
            {
                "Setting: " + e.SettingName,
                "GPO: " + e.GpoId.ToString("D"),
                "Scope: " + e.Scope,
                "Exact Registry.pol target: " + e.RegistryTarget,
                "ADMX policy: " + candidate.Policy?.Name,
                "ADMX category: " + candidate.Policy?.Category,
                "ADMX decimal element ID: " + candidate.Element?.Id,
                "ADMX element label (NOT unit proof): " + candidate.Element?.Label,
                "Native MMC section: " + e.MmcObservedSection,
                "Native MMC value: " + e.MmcObservedValue,
                "Registry DWORD value: " + e.SourceStoredValue,
                "Policy identity SHA-256: " + fingerprint.PolicyIdentitySha256,
                "Registry.pol SHA-256: " + fingerprint.SourceFileSha256,
                "ADMX SHA-256: " + fingerprint.AdmxSha256,
                "ADML SHA-256: " + fingerprint.AdmlSha256,
                "Reviewed definition digest: " + fingerprint.DefinitionSha256,
                "Definition files: " + fingerprint.DefinitionLocation,
                "",
                "This approval is temporary and cannot change a GPO.",
                "Verify exact units with independent documentation / native MMC.",
                "No inference from ADMX minValue, maxValue, valueName or display label."
            })
        };
        root.Children.Add(content);
        Content = root;
        cancel.Click += (_, _) => { DialogResult = false; Close(); };
        approve.Click += (_, _) =>
        {
            if (checkbox.IsChecked != true)
            {
                MessageBox.Show(this, "Confirm the independent unit check.",
                    "Review incomplete", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            var review = reviewReference.Text.Trim();
            if (review.Length is < 6 or > 128 || review.Any(char.IsControl))
            {
                MessageBox.Show(this, "Enter a valid review reference (6-128 printable characters).",
                    "Review incomplete", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (MessageBox.Show(this,
                "Confirm reviewed minutes -> hours conversion for this exact GPO and source snapshot?\n\n" +
                "No domain data will be changed. This is not effective RSoP.",
                "Confirm session-only unit review",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;
            ApprovedReference = review;
            DialogResult = true;
            Close();
        };
    }
}

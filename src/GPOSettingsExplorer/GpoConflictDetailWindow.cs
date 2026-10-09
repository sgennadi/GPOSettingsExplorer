using System.Windows;
using System.Windows.Controls;
using GPOSettingsExplorer.Models;
using GPOSettingsExplorer.Services;

namespace GPOSettingsExplorer;

/// <summary>
/// Actionable, read-only conflict explanation. The existing GPO editor and
/// guarded GPO Links UI must perform any actual writes after preview/backup.
/// </summary>
public sealed class GpoConflictDetailWindow : Window
{
    public bool ReviewLinks { get; private set; }

    private readonly TextBlock _verificationStatus = new();
    private readonly TextBlock _rsopEvidence = new();
    private readonly TextBlock _wmiEvidence = new();
    private readonly TextBlock _securityEvidence = new();
    private readonly TextBlock _recommendation = new();

    public GpoConflictDetailWindow(
        GpoConflictInfo finding,
        Func<string, string, Task<GpoApplicabilityVerification>>? verifyOnComputer = null)
    {
        Title = $"GPO Conflict Analysis - {finding.Kind}";
        Width = 1080;
        Height = 740;
        MinWidth = 640;
        MinHeight = 450;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var root = new DockPanel { Margin = new Thickness(12) };
        Content = root;

        var heading = new StackPanel { Margin = new Thickness(0, 0, 0, 9) };
        DockPanel.SetDock(heading, Dock.Top);
        heading.Children.Add(new TextBlock
        {
            Text = finding.SettingName,
            FontSize = UiStyle.HeadingFontSize,
            Foreground = UiStyle.AccentBrush,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap
        });
        heading.Children.Add(new TextBlock
        {
            Text = $"{finding.Scope} | {finding.Category} | {finding.Kind}",
            Foreground = finding.Kind == "Different values"
                ? UiStyle.ErrorBrush : UiStyle.WarningBrush,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 5, 0, 0)
        });
        heading.Children.Add(new TextBlock
        {
            Text = "Applicability: " + finding.OverlapStatus,
            Foreground = finding.IsPotentialOverlap
                ? UiStyle.WarningBrush : UiStyle.MutedBrush,
            Margin = new Thickness(0, 4, 0, 0)
        });

        var footer = new WrapPanel
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 7, 0, 0)
        };
        DockPanel.SetDock(footer, Dock.Bottom);
        var copy = new Button { Content = "Copy resolution plan" };
        copy.Click += (_, _) =>
        {
            Clipboard.SetText(BuildPlan(finding));
        };
        footer.Children.Add(copy);
        var verify = new Button
        {
            Content = "Verify RSoP / WMI / Security...",
            IsEnabled = verifyOnComputer is not null,
            Style = (Style)FindResource("UiPrimaryButton"),
            ToolTip = "Read-only checks on one specified computer. A passing sample does NOT prove domain-wide applicability."
        };
        verify.Click += async (_, _) =>
        {
            if (verifyOnComputer is null)
                return;

            var computer = new InputDialog(
                "RSoP verification target", "Computer name (RSoP and WMI):",
                Environment.MachineName) { Owner = this };
            if (computer.ShowDialog() != true || string.IsNullOrWhiteSpace(computer.Value))
                return;

            var targetUser = "";
            if (finding.Scope.Equals("User", StringComparison.OrdinalIgnoreCase))
            {
                var user = new InputDialog("User RSoP", "DOMAIN\\user (must have RSoP data):")
                {
                    Owner = this
                };
                if (user.ShowDialog() != true || string.IsNullOrWhiteSpace(user.Value))
                    return;
                targetUser = user.Value;
            }

            verify.IsEnabled = false;
            _verificationStatus.Text = "Verification running (read-only)...";
            try
            {
                var result = await verifyOnComputer(computer.Value, targetUser);
                finding.VerificationStatus = result.Status;
                finding.RsopEvidence = result.RsopEvidence;
                finding.WmiEvidence = result.WmiEvidence;
                finding.SecurityEvidence = result.SecurityEvidence;
                finding.Recommendation = result.Recommendation;
                RefreshEvidence(finding);
            }
            catch (Exception ex)
            {
                finding.VerificationStatus = "Incomplete - verification failed";
                finding.RsopEvidence = ex.Message;
                finding.Recommendation = "NO CONSOLIDATION: verification failed. " +
                    "Review the error and rerun the read-only checks.";
                RefreshEvidence(finding);
                CrashLogService.Write("GPO applicability verification", ex);
            }
            finally
            {
                verify.IsEnabled = true;
            }
        };
        footer.Children.Add(verify);
        var openSettings = new Button
        {
            Content = "Review settings...",
            ToolTip = "Switch to All Settings to inspect all occurrences; does not change GPO data.",
            Style = (Style)FindResource("UiPrimaryButton")
        };
        openSettings.Click += (_, _) =>
        {
            ReviewLinks = false;
            DialogResult = true;
        };
        footer.Children.Add(openSettings);
        var openLinks = new Button
        {
            Content = "Review link order...",
            ToolTip = "Switch to the existing GPO Links editor; any write still requires WRITE ENABLED and confirmation."
        };
        openLinks.Click += (_, _) =>
        {
            ReviewLinks = true;
            DialogResult = true;
        };
        footer.Children.Add(openLinks);
        footer.Children.Add(new Button { Content = "Close", IsCancel = true });

        var body = new Grid();
        body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1.1, GridUnitType.Star) });
        body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var participants = new GroupBox { Header = "Configured values across GPOs" };
        var grid = new DataGrid
        {
            ItemsSource = finding.Participants, AutoGenerateColumns = false,
            IsReadOnly = true
        };
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "GPO", Binding = new System.Windows.Data.Binding(nameof(PolicySettingInfo.GpoName)),
            Width = new DataGridLength(1.8, DataGridLengthUnitType.Star)
        });
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "Scope", Binding = new System.Windows.Data.Binding(nameof(PolicySettingInfo.Scope)),
            Width = new DataGridLength(85)
        });
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "State", Binding = new System.Windows.Data.Binding(nameof(PolicySettingInfo.State)),
            Width = new DataGridLength(110)
        });
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "Configured value", Binding = new System.Windows.Data.Binding(nameof(PolicySettingInfo.Value)),
            Width = new DataGridLength(2.4, DataGridLengthUnitType.Star)
        });
        participants.Content = grid;
        Grid.SetRow(participants, 0);
        body.Children.Add(participants);

        var evidence = new GroupBox { Header = "Scope evidence and recommended resolution" };
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var panel = new StackPanel { Margin = new Thickness(7) };
        panel.Children.Add(new TextBlock
        {
            Text = finding.LinkEvidence,
            Foreground = UiStyle.AccentBrush,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 1, 0, 9)
        });
        _verificationStatus.TextWrapping = TextWrapping.Wrap;
        _verificationStatus.Margin = new Thickness(0, 0, 0, 8);
        panel.Children.Add(_verificationStatus);

        _rsopEvidence.TextWrapping = TextWrapping.Wrap;
        _rsopEvidence.Margin = new Thickness(0, 0, 0, 6);
        panel.Children.Add(_rsopEvidence);

        _wmiEvidence.TextWrapping = TextWrapping.Wrap;
        _wmiEvidence.Margin = new Thickness(0, 0, 0, 6);
        panel.Children.Add(_wmiEvidence);

        _securityEvidence.TextWrapping = TextWrapping.Wrap;
        _securityEvidence.Margin = new Thickness(0, 0, 0, 10);
        panel.Children.Add(_securityEvidence);

        _recommendation.TextWrapping = TextWrapping.Wrap;
        _recommendation.FontWeight = FontWeights.SemiBold;
        _recommendation.Margin = new Thickness(0, 0, 0, 12);
        panel.Children.Add(_recommendation);
        panel.Children.Add(new TextBlock
        {
            Text = "Priority rules: " + finding.PriorityNote,
            Foreground = UiStyle.WarningBrush,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8)
        });
        panel.Children.Add(new TextBlock
        {
            Text = "Safety: No automatic merge, deletion or GPO unlink is performed. " +
                "Check security filtering, WMI filters, inheritance, loopback and gpresult on a representative object. " +
                "For each affected GPO take a separate backup before modifying its settings.",
            Foreground = UiStyle.MutedBrush,
            TextWrapping = TextWrapping.Wrap
        });
        scroll.Content = panel;
        evidence.Content = scroll;
        Grid.SetRow(evidence, 1);
        body.Children.Add(evidence);

        root.Children.Add(heading);
        root.Children.Add(footer);
        root.Children.Add(body);
        RefreshEvidence(finding);
    }

    private void RefreshEvidence(GpoConflictInfo finding)
    {
        _verificationStatus.Text = "Applicability check: " + finding.VerificationStatus;
        _verificationStatus.Foreground = finding.VerificationStatus.StartsWith("Sample", StringComparison.OrdinalIgnoreCase)
            ? UiStyle.SuccessBrush
            : finding.VerificationStatus.StartsWith("Blocked", StringComparison.OrdinalIgnoreCase)
                ? UiStyle.ErrorBrush
                : UiStyle.WarningBrush;
        _rsopEvidence.Text = "RSoP: " + finding.RsopEvidence;
        _wmiEvidence.Text = "WMI: " + finding.WmiEvidence;
        _securityEvidence.Text = "Security Filtering: " + finding.SecurityEvidence;
        _recommendation.Text = "Recommended action: " + finding.Recommendation;
    }

    public static string BuildPlan(GpoConflictInfo finding) =>
        "GPO Settings Explorer - proposed conflict resolution\n" +
        $"Finding: {finding.Kind}\nSetting: {finding.SettingName}\nScope: {finding.Scope}\n" +
        $"GPOs: {finding.Gpos}\nValues: {finding.Variants}\n" +
        $"Overlap assessment: {finding.OverlapStatus}\n" +
        $"Link evidence: {finding.LinkEvidence}\n" +
        $"Verification status: {finding.VerificationStatus}\n" +
        $"RSoP: {finding.RsopEvidence}\n" +
        $"WMI: {finding.WmiEvidence}\n" +
        $"Security filtering: {finding.SecurityEvidence}\n" +
        $"Recommendation: {finding.Recommendation}\n" +
        $"Caution: {finding.PriorityNote}\n" +
        "Before modification: inspect gpresult/RSoP and filter/loopback status; back up every target GPO.\n";
}

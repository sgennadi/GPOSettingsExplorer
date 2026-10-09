using System.Net.Http;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using GPOSettingsExplorer.Services;

namespace GPOSettingsExplorer;

/// <summary>
/// User-controlled preview and upload. Never transmits anything in the
/// constructor, Loaded event, timer or background scan.
/// </summary>
public sealed class DiagnosticSubmissionWindow : Window
{
    private readonly IReadOnlyList<PendingDiagnosticLog> _pending;
    private readonly DiagnosticLogQueueService _queue;
    private readonly TextBox _preview;
    private readonly PasswordBox _tokenInput;
    private readonly CheckBox _rememberToken;
    private readonly CheckBox _includeExcerpts;
    private readonly Button _sendButton;
    private readonly TextBlock _status;
    private readonly bool _hasStoredToken;

    public DiagnosticSubmissionWindow(
        DiagnosticLogQueueService queue, IReadOnlyList<PendingDiagnosticLog> pending)
    {
        _queue = queue;
        _pending = pending;
        _hasStoredToken = !string.IsNullOrWhiteSpace(
            GitHubDiagnosticReportService.LoadToken());

        Title = $"Diagnostics Review - {_pending.Count:N0} pending logs";
        Width = 1060;
        Height = 760;
        MinWidth = 650;
        MinHeight = 470;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var root = new DockPanel { Margin = new Thickness(12) };
        Content = root;

        var bottom = new WrapPanel
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 8, 0, 0)
        };
        DockPanel.SetDock(bottom, Dock.Bottom);

        var exportButton = new Button
        {
            Content = "Export local report",
            ToolTip = "Save sanitized text on this computer. Nothing is sent to the Internet."
        };
        exportButton.Click += (_, _) => ExportReport();
        var copyButton = new Button { Content = "Copy report" };
        copyButton.Click += (_, _) => Clipboard.SetText(_preview.Text);
        var cancelButton = new Button { Content = "Close", IsCancel = true };

        _sendButton = new Button
        {
            Content = "Send to GitHub...",
            Style = (Style)FindResource("UiPrimaryButton"),
            IsEnabled = false,
            ToolTip = "Available only if GitHub is reachable; requires explicit confirmation."
        };
        _sendButton.Click += async (_, _) => await SendAsync();

        bottom.Children.Add(exportButton);
        bottom.Children.Add(copyButton);
        bottom.Children.Add(cancelButton);
        bottom.Children.Add(_sendButton);

        var intro = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
        DockPanel.SetDock(intro, Dock.Top);
        intro.Children.Add(new TextBlock
        {
            Text = $"Pending: {_pending.Count:N0} log(s) ({_pending.Count(x => x.Kind == "Error")} errors, {_pending.Count(x => x.Kind != "Error")} tree/audit reports)",
            FontSize = UiStyle.HeadingFontSize,
            FontWeight = FontWeights.SemiBold,
            Foreground = UiStyle.AccentBrush,
            TextWrapping = TextWrapping.Wrap
        });
        intro.Children.Add(new TextBlock
        {
            Text = "WARNING: GitHub Issues in this repository are PUBLIC. " +
                   "By default only technical fingerprints, event counts and application versions are shared. " +
                   "Review every line before submitting. No logs are uploaded without your approval.",
            Foreground = UiStyle.WarningBrush,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 4)
        });

        _includeExcerpts = new CheckBox
        {
            Content = "Include redacted log excerpts (additional privacy risk)",
            IsChecked = false,
            ToolTip = "Optional. Allows reviewing redacted snippets before uploading; OFF by default."
        };
        _includeExcerpts.Checked += (_, _) => RegenerateReport();
        _includeExcerpts.Unchecked += (_, _) => RegenerateReport();
        intro.Children.Add(_includeExcerpts);

        var auth = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        auth.Children.Add(new TextBlock
        {
            Text = "GitHub Issues token",
            VerticalAlignment = VerticalAlignment.Center
        });
        _tokenInput = new PasswordBox
        {
            Width = 270,
            ToolTip = "Fine-grained GitHub token: repository Issues read/write only. Leave empty to use encrypted saved token."
        };
        auth.Children.Add(_tokenInput);
        _rememberToken = new CheckBox
        {
            Content = "Remember with Windows DPAPI (this user)",
            IsChecked = false,
            VerticalAlignment = VerticalAlignment.Center
        };
        auth.Children.Add(_rememberToken);
        var forget = new Button { Content = "Forget saved token" };
        forget.Click += (_, _) =>
        {
            GitHubDiagnosticReportService.ClearToken();
            _tokenInput.Clear();
            _status.Text = "Saved token cleared.";
        };
        auth.Children.Add(forget);
        intro.Children.Add(auth);

        _status = new TextBlock
        {
            Text = _hasStoredToken
                ? "A token is saved for this Windows account. Checking GitHub availability..."
                : "No saved token. Checking GitHub availability...",
            Foreground = UiStyle.MutedBrush,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(3, 5, 3, 0)
        };
        intro.Children.Add(_status);

        _preview = new TextBox
        {
            Text = GitHubDiagnosticReportService.BuildReport(_pending),
            AcceptsReturn = true,
            AcceptsTab = false,
            IsReadOnly = false,
            TextWrapping = TextWrapping.NoWrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            MinHeight = 190,
            ToolTip = "Editable preview: remove any information you do not want to publish."
        };

        root.Children.Add(bottom);
        root.Children.Add(intro);
        root.Children.Add(_preview);

        Loaded += async (_, _) => await ProbeOnlineAsync();
    }

    private void RegenerateReport()
    {
        if (_preview is null)
            return;
        _preview.Text = GitHubDiagnosticReportService.BuildReport(
            _pending, _includeExcerpts.IsChecked == true);
    }

    private async Task ProbeOnlineAsync()
    {
        _sendButton.IsEnabled = false;
        var online = await GitHubConnectivityService.IsAvailableAsync(force: true);
        _sendButton.IsEnabled = online;
        _status.Text = online
            ? "GitHub is reachable. Local files will be archived only after a confirmed issue is created."
            : "Offline: sending is disabled, no Internet upload is attempted. Local export remains available.";
        _status.Foreground = online ? UiStyle.SuccessBrush : UiStyle.WarningBrush;
    }

    private void ExportReport()
    {
        var dialog = new SaveFileDialog
        {
            Title = "Export sanitized diagnostic report locally",
            FileName = "GPOSettingsExplorer-diagnostics-" +
                       DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt",
            Filter = "Text report (*.txt)|*.txt|All files (*.*)|*.*",
            InitialDirectory = Environment.GetFolderPath(
                Environment.SpecialFolder.MyDocuments)
        };
        if (dialog.ShowDialog(this) != true)
            return;
        try
        {
            File.WriteAllText(dialog.FileName, _preview.Text);
            _status.Text = "Local report exported. Pending log queue unchanged.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, ex.Message, "Export diagnostics",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task SendAsync()
    {
        // Repeat the connectivity check just before any authenticated request.
        if (!await GitHubConnectivityService.IsAvailableAsync(force: true))
        {
            _sendButton.IsEnabled = false;
            _status.Text = "Offline: nothing was sent. Logs remain pending.";
            _status.Foreground = UiStyle.WarningBrush;
            return;
        }

        var token = string.IsNullOrWhiteSpace(_tokenInput.Password)
            ? GitHubDiagnosticReportService.LoadToken()
            : _tokenInput.Password;
        if (string.IsNullOrWhiteSpace(token))
        {
            _status.Text =
                "Enter a fine-grained GitHub token with Issues: Read and write permission.";
            _status.Foreground = UiStyle.WarningBrush;
            _tokenInput.Focus();
            return;
        }

        if (MessageBox.Show(this,
                $"Publish the exact text in the preview to the PUBLIC GitHub repository?\n\n" +
                $"On successful upload, {_pending.Count:N0} matching local log(s) will move into a " +
                "14-day local archive. Any change or failure leaves logs intact.\n\n" +
                "Verify the report does not contain personal, domain or sensitive data.",
                "Confirm public diagnostic submission",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        _sendButton.IsEnabled = false;
        _status.Text = "Submitting approved report to GitHub...";
        try
        {
            var url = await GitHubDiagnosticReportService.CreateIssueAsync(
                token, _preview.Text);
            if (_rememberToken.IsChecked == true &&
                !string.IsNullOrWhiteSpace(_tokenInput.Password))
                GitHubDiagnosticReportService.SaveToken(_tokenInput.Password);

            string cleanup = "Submitted logs were archived locally.";
            try
            {
                _queue.MarkSubmitted(_pending, url);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // GitHub already accepted the issue. Do not automatically
                // resubmit it, and explain the incomplete local cleanup.
                cleanup = "GitHub accepted the report, but local cleanup failed: " + ex.Message;
            }

            Clipboard.SetText(url);
            MessageBox.Show(this,
                $"Diagnostic Issue created:\n{url}\n\n{cleanup}\n" +
                "The link was copied. You do not need to remember its number.",
                "Diagnostics sent",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            DialogResult = true;
        }
        catch (Exception ex) when (ex is IOException or HttpRequestException
                                       or InvalidOperationException
                                       or TaskCanceledException)
        {
            _status.Text = "Send failed: " + ex.Message +
                " No local logs were deleted. Retry later.";
            _status.Foreground = UiStyle.ErrorBrush;
            _sendButton.IsEnabled = true;
        }
    }
}

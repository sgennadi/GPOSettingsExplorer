using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using GPOSettingsExplorer.Services;

namespace GPOSettingsExplorer;

public partial class MainWindow
{
    private readonly DiagnosticLogQueueService _diagnosticQueue = new();
    private DispatcherTimer? _diagnosticQueueTimer;
    private bool _checkingPendingDiagnostics;

    private void StartDiagnosticQueueMonitor()
    {
        if (_diagnosticQueueTimer is not null)
            return;

        // Do not block the application's GPO/AD startup path.
        _diagnosticQueueTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMinutes(30)
        };
        _diagnosticQueueTimer.Tick += async (_, _) =>
            await CheckPendingDiagnosticsAsync();
        _diagnosticQueueTimer.Start();
        _ = CheckPendingDiagnosticsAsync();
    }

    private void StopDiagnosticQueueMonitor()
    {
        _diagnosticQueueTimer?.Stop();
        _diagnosticQueueTimer = null;
    }

    private async Task CheckPendingDiagnosticsAsync()
    {
        if (_checkingPendingDiagnostics)
            return;

        _checkingPendingDiagnostics = true;
        try
        {
            var items = await Task.Run(() =>
            {
                // Only acknowledged files in SentArchive may expire offline.
                // Never delete pending error reports without a confirmed issue.
                _diagnosticQueue.CleanupSubmittedArchive();
                return _diagnosticQueue.GetPending();
            });
            if (items.Count < DiagnosticLogQueueService.AutomaticOfferThreshold ||
                !_diagnosticQueue.ShouldOffer(items.Count, DateTime.UtcNow))
            {
                PendingDiagnosticsButton.Visibility = Visibility.Collapsed;
                return;
            }

            // No notification when GitHub is inaccessible, even if the
            // domain and SYSVOL work correctly. No background upload.
            if (!await GitHubConnectivityService.IsAvailableAsync())
            {
                PendingDiagnosticsButton.Visibility = Visibility.Collapsed;
                return;
            }

            PendingDiagnosticsButton.Content = $"{items.Count:N0} logs · Review";
            PendingDiagnosticsButton.ToolTip =
                $"{items.Count:N0} unsubmitted diagnostics. Click to inspect a local " +
                "preview; uploading is optional and requires another confirmation.";
            PendingDiagnosticsButton.Visibility = Visibility.Visible;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Diagnostics must not interfere with the application.
            PendingDiagnosticsButton.Visibility = Visibility.Collapsed;
        }
        finally
        {
            _checkingPendingDiagnostics = false;
        }
    }

    private async void PendingDiagnostics_Click(object sender, RoutedEventArgs e)
    {
        PendingDiagnosticsButton.Visibility = Visibility.Collapsed;
        var pending = await Task.Run(_diagnosticQueue.GetPending);
        if (pending.Count == 0)
            return;

        // Avoid repeatedly proposing the same batch at every timer tick.
        _diagnosticQueue.MarkOffered(DateTime.UtcNow);
        var review = new DiagnosticSubmissionWindow(_diagnosticQueue, pending)
        {
            Owner = this
        };
        review.ShowDialog();
        await CheckPendingDiagnosticsAsync();
    }
}

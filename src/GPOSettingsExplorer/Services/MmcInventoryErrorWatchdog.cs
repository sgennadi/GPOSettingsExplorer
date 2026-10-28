using System.Threading;

namespace GPOSettingsExplorer.Services;

/// <summary>
/// Best-effort, noninteractive Win32 watchdog. UI Automation or an MMC snap-in
/// may block the scanner thread inside a COM call while a modal error is
/// shown; checking only between tree nodes is too late. This separate timer
/// detects the modal and cancels the scan independently. It never presses a
/// dialog button, changes settings, or terminates MMC.
/// </summary>
public sealed class MmcInventoryErrorWatchdog : IDisposable
{
    private readonly int _mmcPid;
    private readonly CancellationTokenSource _cancelSource;
    private readonly IProgress<string>? _progress;
    private readonly Timer _timer;
    private int _detected;
    private int _disposed;
    private string? _diagnostic;

    public bool DialogDetected => Volatile.Read(ref _detected) != 0;
    public string Diagnostic =>
        Volatile.Read(ref _diagnostic) ?? "MMC modal dialog appeared.";

    public MmcInventoryErrorWatchdog(
        int mmcProcessId, CancellationTokenSource cancellation,
        IProgress<string>? progress = null, int intervalMs = 175)
    {
        ArgumentNullException.ThrowIfNull(cancellation);
        if (mmcProcessId <= 0)
            throw new ArgumentOutOfRangeException(nameof(mmcProcessId));
        if (intervalMs < 50 || intervalMs > 5000)
            throw new ArgumentOutOfRangeException(nameof(intervalMs));

        _mmcPid = mmcProcessId;
        _cancelSource = cancellation;
        _progress = progress;
        _timer = new Timer(CheckForDialog, null, 0, intervalMs);
    }

    private void CheckForDialog(object? state)
    {
        if (Volatile.Read(ref _disposed) != 0 ||
            Volatile.Read(ref _detected) != 0)
            return;

        try
        {
            if (_cancelSource.IsCancellationRequested)
                return;

            var dialog = MmcInventoryDialogGuard.FindVisibleDialog(_mmcPid);
            if (dialog is null ||
                Interlocked.CompareExchange(ref _detected, 1, 0) != 0)
                return;

            Volatile.Write(ref _diagnostic, dialog);
            _progress?.Report(
                "STOPPED: MMC displayed a modal snap-in dialog. " +
                "The read-only inventory is now PARTIAL. Do not select " +
                "'always ignore errors'; close or inspect MMC manually.");
            _cancelSource.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Scan completed while a timer callback was already executing.
        }
        catch (Exception)
        {
            // The watchdog is best-effort: never let background Win32
            // inspection kill the application or automatically touch MMC.
        }
    }

    public void Dispose()
    {
        Interlocked.Exchange(ref _disposed, 1);
        _timer.Dispose();
    }
}

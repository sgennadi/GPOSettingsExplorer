using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace GPOSettingsExplorer.Services;

/// <summary>
/// Read-only Win32 detection of a visible MMC modal error/property dialog.
/// We never click, close, suppress or acknowledge an MMC dialog automatically:
/// a modal popup means the unattended inventory must stop as PARTIAL.
/// </summary>
public static class MmcInventoryDialogGuard
{
    private delegate bool EnumWindowCallback(IntPtr window, IntPtr userData);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowCallback callback, IntPtr userData);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(
        IntPtr window, StringBuilder className, int capacity);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(
        IntPtr window, StringBuilder text, int capacity);

    public static string? FindVisibleDialog(int mmcProcessId)
    {
        string? dialog = null;
        EnumWindows((handle, _) =>
        {
            if (!IsWindowVisible(handle))
                return true;

            GetWindowThreadProcessId(handle, out var ownerProcessId);
            if (ownerProcessId != unchecked((uint)mmcProcessId))
                return true;

            var className = new StringBuilder(128);
            GetClassName(handle, className, className.Capacity);

            // MMC's snap-in error message uses the standard dialog class.
            // Other modal dialogs should also block blind automation.
            if (!className.ToString().Equals("#32770", StringComparison.Ordinal))
                return true;

            var caption = new StringBuilder(256);
            GetWindowText(handle, caption, caption.Capacity);
            dialog = string.IsNullOrWhiteSpace(caption.ToString())
                ? "an untitled MMC modal dialog"
                : "MMC modal dialog: " + caption.ToString().Trim();
            return false;
        }, IntPtr.Zero);
        return dialog;
    }

    public static void ThrowIfDialogOpen(Process process)
    {
        var dialog = FindVisibleDialog(process.Id);
        if (dialog is not null)
            throw new InvalidOperationException(
                dialog + ". Inventory aborted as PARTIAL; close or investigate " +
                "the MMC session manually. No automatic dismissal was attempted.");
    }
}

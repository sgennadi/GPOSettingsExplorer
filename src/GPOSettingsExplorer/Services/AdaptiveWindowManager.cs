using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace GPOSettingsExplorer.Services;

internal static class AdaptiveWindowManager
{
    private const uint MonitorDefaultToNearest = 2;

    public static void Register()
    {
        EventManager.RegisterClassHandler(
            typeof(Window),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnWindowLoaded));
    }

    private static void OnWindowLoaded(
        object sender,
        RoutedEventArgs e)
    {
        if (sender is not Window window)
            return;

        ConstrainToCurrentMonitor(window);
    }

    private static void ConstrainToCurrentMonitor(
        Window window)
    {
        var handle =
            new WindowInteropHelper(window).Handle;

        if (handle == nint.Zero)
            return;

        var monitor =
            MonitorFromWindow(
                handle,
                MonitorDefaultToNearest);

        if (monitor == nint.Zero)
            return;

        var info =
            new MonitorInfo
            {
                Size =
                    Marshal.SizeOf<MonitorInfo>()
            };

        if (!GetMonitorInfo(
                monitor,
                ref info))
            return;

        var dpi =
            GetDpiForWindow(handle);

        if (dpi == 0)
            dpi = 96;

        var scale =
            dpi / 96.0;

        var workWidth =
            (info.Work.Right -
             info.Work.Left) /
            scale;

        var workHeight =
            (info.Work.Bottom -
             info.Work.Top) /
            scale;

        const double margin = 12.0;

        var maxWidth =
            Math.Max(
                320,
                workWidth - margin);

        var maxHeight =
            Math.Max(
                240,
                workHeight - margin);

        window.MaxWidth =
            Math.Min(
                window.MaxWidth,
                maxWidth);

        window.MaxHeight =
            Math.Min(
                window.MaxHeight,
                maxHeight);

        if (window.WindowState !=
            WindowState.Normal)
            return;

        if (!double.IsNaN(window.Width) &&
            window.Width > maxWidth)
        {
            window.Width =
                maxWidth;
        }

        if (!double.IsNaN(window.Height) &&
            window.Height > maxHeight)
        {
            window.Height =
                maxHeight;
        }

        if (window.MinWidth >
            maxWidth)
        {
            window.MinWidth =
                Math.Min(
                    640,
                    maxWidth);
        }

        if (window.MinHeight >
            maxHeight)
        {
            window.MinHeight =
                Math.Min(
                    420,
                    maxHeight);
        }
    }

    [DllImport("user32.dll")]
    private static extern nint MonitorFromWindow(
        nint hwnd,
        uint flags);

    [DllImport(
        "user32.dll",
        CharSet = CharSet.Auto,
        SetLastError = true)]
    private static extern bool GetMonitorInfo(
        nint monitor,
        ref MonitorInfo info);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(
        nint hwnd);

    [StructLayout(
        LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public RectPx Monitor;
        public RectPx Work;
        public uint Flags;
    }

    [StructLayout(
        LayoutKind.Sequential)]
    private struct RectPx
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}

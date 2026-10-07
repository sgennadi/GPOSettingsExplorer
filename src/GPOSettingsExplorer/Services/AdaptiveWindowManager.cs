using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace GPOSettingsExplorer.Services;

internal static class AdaptiveWindowManager
{
    private const uint MonitorDefaultToNearest = 2;

    private static readonly ConditionalWeakTable<Window, OriginalLimits>
        OriginalWindowLimits = new();

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

        UiStyle.ApplyWindowDefaults(window);

        if (!OriginalWindowLimits.TryGetValue(
                window,
                out _))
        {
            OriginalWindowLimits.Add(
                window,
                new OriginalLimits(
                    window.MinWidth,
                    window.MinHeight,
                    window.MaxWidth,
                    window.MaxHeight));

            window.LocationChanged +=
                Window_LocationChanged;

            window.StateChanged +=
                Window_StateChanged;

            window.DpiChanged +=
                Window_DpiChanged;
        }

        ConstrainToCurrentMonitor(
            window);
    }

    private static void Window_LocationChanged(
        object? sender,
        EventArgs e)
    {
        if (sender is Window window)
        {
            ConstrainToCurrentMonitor(
                window);
        }
    }

    private static void Window_StateChanged(
        object? sender,
        EventArgs e)
    {
        if (sender is Window window)
        {
            ConstrainToCurrentMonitor(
                window);
        }
    }

    private static void Window_DpiChanged(
        object sender,
        DpiChangedEventArgs e)
    {
        if (sender is Window window)
        {
            ConstrainToCurrentMonitor(
                window);
        }
    }

    private static void ConstrainToCurrentMonitor(
        Window window)
    {
        var handle =
            new WindowInteropHelper(
                window).Handle;

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
            GetDpiForWindow(
                handle);

        if (dpi == 0)
            dpi = 96;

        var scale =
            dpi / 96.0;

        var workLeft =
            info.Work.Left /
            scale;

        var workTop =
            info.Work.Top /
            scale;

        var workWidth =
            (info.Work.Right -
             info.Work.Left) /
            scale;

        var workHeight =
            (info.Work.Bottom -
             info.Work.Top) /
            scale;

        const double outerMargin =
            12.0;

        var monitorMaxWidth =
            Math.Max(
                320,
                workWidth -
                outerMargin);

        var monitorMaxHeight =
            Math.Max(
                240,
                workHeight -
                outerMargin);

        var original =
            OriginalWindowLimits.GetValue(
                window,
                current =>
                    new OriginalLimits(
                        current.MinWidth,
                        current.MinHeight,
                        current.MaxWidth,
                        current.MaxHeight));

        window.MaxWidth =
            double.IsPositiveInfinity(
                original.MaxWidth)
                ? monitorMaxWidth
                : Math.Min(
                    original.MaxWidth,
                    monitorMaxWidth);

        window.MaxHeight =
            double.IsPositiveInfinity(
                original.MaxHeight)
                ? monitorMaxHeight
                : Math.Min(
                    original.MaxHeight,
                    monitorMaxHeight);

        window.MinWidth =
            Math.Min(
                original.MinWidth,
                window.MaxWidth);

        window.MinHeight =
            Math.Min(
                original.MinHeight,
                window.MaxHeight);

        if (window.WindowState !=
            WindowState.Normal)
            return;

        if (!double.IsNaN(
                window.Width) &&
            window.Width >
            window.MaxWidth)
        {
            window.Width =
                window.MaxWidth;
        }

        if (!double.IsNaN(
                window.Height) &&
            window.Height >
            window.MaxHeight)
        {
            window.Height =
                window.MaxHeight;
        }

        var right =
            workLeft +
            workWidth;

        var bottom =
            workTop +
            workHeight;

        if (window.Left <
            workLeft)
        {
            window.Left =
                workLeft;
        }

        if (window.Top <
            workTop)
        {
            window.Top =
                workTop;
        }

        if (window.Left +
            window.ActualWidth >
            right)
        {
            window.Left =
                Math.Max(
                    workLeft,
                    right -
                    window.ActualWidth);
        }

        if (window.Top +
            window.ActualHeight >
            bottom)
        {
            window.Top =
                Math.Max(
                    workTop,
                    bottom -
                    window.ActualHeight);
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

    private sealed record OriginalLimits(
        double MinWidth,
        double MinHeight,
        double MaxWidth,
        double MaxHeight);
}

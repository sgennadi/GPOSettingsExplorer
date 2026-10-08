using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace GPOSettingsExplorer;

internal static class UiStyle
{
    private static readonly FontFamily CodeFont =
        new("Consolas");

    private static Brush ThemeBrush(string key, Brush fallback) =>
        Application.Current?.TryFindResource(key) as Brush ?? fallback;

    public static Brush AccentBrush => ThemeBrush("UiAccentBrush", SystemColors.HighlightBrush);
    public static Brush SuccessBrush => ThemeBrush("UiSuccessBrush", Brushes.ForestGreen);
    public static Brush WarningBrush => ThemeBrush("UiWarningBrush", Brushes.DarkOrange);
    public static Brush ErrorBrush => ThemeBrush("UiErrorBrush", Brushes.Firebrick);
    public static Brush MutedBrush => ThemeBrush("UiMutedBrush", SystemColors.GrayTextBrush);
    public static Brush FileBatchBrush => ThemeBrush("UiFileBatchBrush", AccentBrush);
    public static Brush FileCmdBrush => ThemeBrush("UiFileCmdBrush", AccentBrush);
    public static Brush FilePowerShellBrush => ThemeBrush("UiFilePowerShellBrush", AccentBrush);
    public static Brush FileOtherBrush => ThemeBrush("UiFileOtherBrush", MutedBrush);
    public static Brush ScriptCommentBrush => ThemeBrush("UiScriptCommentBrush", SuccessBrush);
    public static Brush ScriptStringBrush => ThemeBrush("UiScriptStringBrush", WarningBrush);
    public static Brush ScriptKeywordBrush => ThemeBrush("UiScriptKeywordBrush", AccentBrush);
    public static Brush ScriptVariableBrush => ThemeBrush("UiScriptVariableBrush", AccentBrush);
    public static Brush ScriptNumberBrush => ThemeBrush("UiScriptNumberBrush", AccentBrush);

    public static string ToRgbHex(Brush brush)
    {
        var color = (brush as SolidColorBrush)?.Color ?? Colors.Black;
        return $"#{color.R:X2}{color.G:X2}{color.B:X2}";
    }


    public static FontFamily MonospaceFontFamily =>
        CodeFont;

    public static double MonospaceFontSize =>
        SystemFonts.MessageFontSize;

    public static double HeadingFontSize =>
        Math.Max(
            SystemFonts.MessageFontSize * 1.45,
            SystemFonts.MessageFontSize + 3.0);

    public static void ApplyDataGridDefaults(DataGrid grid)
    {
        // Every grid, including GPP preferences and dialogs, gets a minimum
        // visible header width. Star columns can scroll horizontally instead
        // of crushing their labels into one letter per line.
        var typeface = new Typeface(
            grid.FontFamily,
            grid.FontStyle,
            grid.FontWeight,
            grid.FontStretch);
        var pixelsPerDip = VisualTreeHelper.GetDpi(grid).PixelsPerDip;

        foreach (var column in grid.Columns)
        {
            var title = Convert.ToString(column.Header, CultureInfo.CurrentCulture);
            if (string.IsNullOrWhiteSpace(title))
                continue;

            var measure = new FormattedText(
                title,
                CultureInfo.CurrentUICulture,
                grid.FlowDirection,
                typeface,
                grid.FontSize,
                grid.Foreground,
                pixelsPerDip);
            column.MinWidth = Math.Max(
                column.MinWidth,
                Math.Max(90, Math.Ceiling(measure.WidthIncludingTrailingWhitespace + 28)));
        }
    }

    public static void ApplyWindowDefaults(Window window)
    {
        window.UseLayoutRounding = true;
        window.SnapsToDevicePixels = true;

        TextOptions.SetTextFormattingMode(
            window,
            TextFormattingMode.Display);

        TextOptions.SetTextRenderingMode(
            window,
            TextRenderingMode.ClearType);

        // Fixed-size dialogs are the most common source of clipped controls
        // when the system font or per-monitor DPI changes. Keep the initial
        // dimensions, but always let the user and Windows expand the window.
        if (window.ResizeMode == ResizeMode.NoResize)
        {
            window.ResizeMode =
                ResizeMode.CanResizeWithGrip;
        }
    }
}

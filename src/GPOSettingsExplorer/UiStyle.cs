using System.Windows;
using System.Windows.Media;

namespace GPOSettingsExplorer;

internal static class UiStyle
{
    private static readonly FontFamily CodeFont =
        new("Consolas");

    public static FontFamily MonospaceFontFamily =>
        CodeFont;

    public static double MonospaceFontSize =>
        SystemFonts.MessageFontSize;

    public static double HeadingFontSize =>
        Math.Max(
            SystemFonts.MessageFontSize * 1.45,
            SystemFonts.MessageFontSize + 3.0);

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
    }
}

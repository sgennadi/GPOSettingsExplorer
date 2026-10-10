using System.Windows;
using System.Xml.Linq;

namespace GPOSettingsExplorer.Services;

public sealed record ChangePreviewRequest(
    string Title,
    string Target,
    string Before,
    string After,
    string Details = "",
    string ActionText = "Apply");

public static class ChangePreviewGuard
{
    private static int _enabled =
        1;

    public static bool IsEnabled
    {
        get =>
            Volatile.Read(
                ref _enabled) != 0;

        set =>
            Interlocked.Exchange(
                ref _enabled,
                value ? 1 : 0);
    }

    public static void Confirm(ChangePreviewRequest request) =>
        ConfirmCore(request, required: false);

    /// <summary>Security and restore operations always require a visible approval.</summary>
    public static void ConfirmRequired(ChangePreviewRequest request) =>
        ConfirmCore(request, required: true);

    private static void ConfirmCore(ChangePreviewRequest request, bool required)
    {
        EditingGuard.EnsureEnabled(
            request.Title);

        if (!required && !IsEnabled)
            return;

        var application =
            Application.Current;

        if (application is null)
            throw new InvalidOperationException(
                "Interactive change preview is unavailable. Editing is blocked.");

        bool approved =
            false;

        void Show()
        {
            // During in-place script editing the editor is itself modal.
            // Parent the confirmation to the active dialog so the approval
            // stays visible and cannot be hidden behind a disabled main window.
            var owner =
                application.Windows.OfType<Window>()
                    .Where(candidate => candidate.IsVisible && candidate.IsActive)
                    .FirstOrDefault()
                ?? application.MainWindow;

            var window =
                new ChangePreviewWindow(request)
                {
                    Owner = owner
                };

            approved =
                window.ShowDialog() ==
                true &&
                window.Approved;
        }

        if (application.Dispatcher.CheckAccess())
        {
            Show();
        }
        else
        {
            application.Dispatcher.Invoke(
                Show);
        }

        if (!approved)
        {
            throw new OperationCanceledException(
                "The change was canceled in the preview window.");
        }
    }

    public static string NormalizeXmlForPreview(
        string? xml)
    {
        if (string.IsNullOrWhiteSpace(
                xml))
        {
            return "<none>";
        }

        try
        {
            return XDocument.Parse(
                    xml,
                    LoadOptions.PreserveWhitespace)
                .ToString();
        }
        catch
        {
            return xml;
        }
    }
}

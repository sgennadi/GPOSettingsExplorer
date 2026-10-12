using System.Windows;
using System.Windows.Input;
using System.Xml.Linq;

namespace GPOSettingsExplorer.Services;

public sealed record ChangePreviewRequest(
    string Title,
    string Target,
    string Before,
    string After,
    string Details = "",
    string ActionText = "Apply");

/// <summary>
/// Explicit, user-requested Cancel/close in the interactive change preview.
/// No SYSVOL/GPO data has been committed. Callers may show a neutral status
/// instead of presenting this expected choice as a failed disk operation.
/// </summary>
public sealed class ChangePreviewCanceledException : OperationCanceledException
{
    public ChangePreviewCanceledException()
        : base("The change was canceled in the preview window.") { }
}

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

            // Some callers mark the main window busy while creating backups.
            // Mouse.OverrideCursor is global to the WPF UI thread: if left as
            // Wait, the Preview remains clickable yet falsely looks frozen.
            // Keep the pointer interactive for the entire modal approval.
            var previousOverride = Mouse.OverrideCursor;
            try
            {
                Mouse.OverrideCursor = null;
                approved = window.ShowDialog() == true && window.Approved;
            }
            finally
            {
                Mouse.OverrideCursor = previousOverride;
            }
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

        RequireApprovedResult(approved);
    }

    /// <summary>
    /// Distinguish a normal decline from IO, access and verification faults.
    /// The preview was displayed before any protected writer was allowed to
    /// continue. Never turn Cancel into a successful save.
    /// </summary>
    public static void RequireApprovedResult(bool approved)
    {
        if (!approved)
            throw new ChangePreviewCanceledException();
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

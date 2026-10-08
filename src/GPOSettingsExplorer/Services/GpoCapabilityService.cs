using System.Security.Principal;
using GPOSettingsExplorer.Models;

namespace GPOSettingsExplorer.Services;

public sealed record GpoCapability(
    bool CanRead,
    bool CanEditSettings,
    bool CanEditSecurity,
    string Summary,
    string Details);

public sealed class GpoCapabilityService
{
    public GpoCapability Evaluate(
        IReadOnlyList<GpoPermissionInfo> permissions,
        DiagnosticItem? sysvolWrite = null)
    {
        var tokenSids =
            CurrentTokenSids();

        var matching =
            permissions
                .Where(
                    permission =>
                        !string.IsNullOrWhiteSpace(
                            permission.TrusteeSid) &&
                        tokenSids.Contains(
                            permission.TrusteeSid))
                .ToArray();

        var denied =
            matching
                .Where(
                    item =>
                        item.Denied)
                .ToArray();

        var allowed =
            matching
                .Where(
                    item =>
                        !item.Denied)
                .ToArray();

        var canRead =
            allowed.Any(
                item =>
                    item.Level is
                        GpoPermissionLevel.Read or
                        GpoPermissionLevel.Edit or
                        GpoPermissionLevel.FullControl or
                        GpoPermissionLevel.Apply);

        var canEdit =
            allowed.Any(
                item =>
                    item.Level is
                        GpoPermissionLevel.Edit or
                        GpoPermissionLevel.FullControl);

        var canSecurity =
            allowed.Any(
                item =>
                    item.Level ==
                    GpoPermissionLevel.FullControl);

        if (denied.Length > 0)
        {
            if (denied.Any(
                    item =>
                        item.Level is
                            GpoPermissionLevel.Read or
                            GpoPermissionLevel.Edit or
                            GpoPermissionLevel.FullControl))
            {
                canRead =
                    false;
            }

            if (denied.Any(
                    item =>
                        item.Level is
                            GpoPermissionLevel.Edit or
                            GpoPermissionLevel.FullControl))
            {
                canEdit =
                    false;
            }

            if (denied.Any(
                    item =>
                        item.Level ==
                        GpoPermissionLevel.FullControl))
            {
                canSecurity =
                    false;
            }
        }

        if (sysvolWrite?.Success ==
            true)
        {
            canRead =
                true;

            canEdit =
                true;
        }

        if (matching.Length == 0 &&
            sysvolWrite?.Success !=
            true)
        {
            return new GpoCapability(
                canRead,
                canEdit,
                canSecurity,
                "Access: not resolved",
                "No matching current-user/group SID was found in the simplified GPMC permission list. Effective rights may still be granted through Active Directory permissions.");
        }

        var summary =
            $"Access: Read {(canRead ? "✓" : "✗")} | Edit {(canEdit ? "✓" : "✗")} | Security {(canSecurity ? "✓" : "✗")}";

        var details =
            matching.Length == 0
                ? "SYSVOL write access confirmed."
                : string.Join(
                    " | ",
                    matching.Select(
                        item =>
                            $"{item.TrusteeDisplay}: {item.PermissionDisplay}" +
                            (item.Denied
                                ? " (Deny)"
                                : string.Empty)));

        return new GpoCapability(
            canRead,
            canEdit,
            canSecurity,
            summary,
            details);
    }

    private static HashSet<string> CurrentTokenSids()
    {
        var result =
            new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);

        try
        {
            using var identity =
                WindowsIdentity.GetCurrent();

            if (identity.User is not null)
            {
                result.Add(
                    identity.User.Value);
            }

            if (identity.Groups is not null)
            {
                foreach (var group in identity.Groups)
                {
                    result.Add(
                        group.Value);
                }
            }
        }
        catch
        {
        }

        return result;
    }
}

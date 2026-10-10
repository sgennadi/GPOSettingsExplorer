using System.Security.Principal;
using GPOSettingsExplorer.Models;

namespace GPOSettingsExplorer.Services;

public sealed record GpoCapability(
    bool CanRead,
    bool CanEditSettings,
    bool CanEditSecurity,
    string Summary,
    string Details);

/// <summary>
/// Fail-closed UI hints. GPMC permissions are a simplified interpretation
/// of ACL entries and are not a Windows AuthZ access-check. In particular,
/// SYSVOL write access NEVER supplies a missing AD Edit GPO permission.
/// All mutation services must keep their own authorization and safe-mode
/// checks; this class does not grant access.
/// </summary>
public sealed class GpoCapabilityService
{
    public GpoCapability Evaluate(
        IReadOnlyList<GpoPermissionInfo> permissions,
        DiagnosticItem? sysvolWrite = null) =>
        EvaluateForToken(permissions, CurrentTokenSids(), sysvolWrite);

    /// <summary>Exposes deterministic SID matching for regression tests.</summary>
    public static GpoCapability EvaluateForToken(
        IReadOnlyList<GpoPermissionInfo> permissions,
        IEnumerable<string> currentTokenSids,
        DiagnosticItem? sysvolWrite = null)
    {
        ArgumentNullException.ThrowIfNull(permissions);
        ArgumentNullException.ThrowIfNull(currentTokenSids);

        var sids = currentTokenSids
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var matching = permissions.Where(p =>
                !string.IsNullOrWhiteSpace(p.TrusteeSid) &&
                sids.Contains(p.TrusteeSid.Trim()))
            .ToArray();

        var allows = matching.Where(p => !p.Denied).ToArray();
        var denies = matching.Where(p => p.Denied).ToArray();

        // GPMC aggregates grants into descriptive levels; unknown custom
        // access rules are NOT proof of a higher or lower permission.
        var adRead = allows.Any(p => p.Level is
            GpoPermissionLevel.Read or GpoPermissionLevel.Apply or
            GpoPermissionLevel.Edit or GpoPermissionLevel.FullControl) &&
            !denies.Any(p => p.Level is
                GpoPermissionLevel.Read or GpoPermissionLevel.Apply or
                GpoPermissionLevel.Edit or GpoPermissionLevel.FullControl or
                GpoPermissionLevel.Custom);

        var adEdit = allows.Any(p => p.Level is
            GpoPermissionLevel.Edit or GpoPermissionLevel.FullControl) &&
            !denies.Any(p => p.Level is
                GpoPermissionLevel.Edit or GpoPermissionLevel.FullControl or
                GpoPermissionLevel.Custom);

        var adSecurity = allows.Any(p =>
            p.Level == GpoPermissionLevel.FullControl) &&
            !denies.Any(p => p.Level is
                GpoPermissionLevel.FullControl or GpoPermissionLevel.Custom);

        // GPO settings generally span both AD (GPC) and SYSVOL (GPT).
        // No positive SYSVOL result may turn an AD denial/unknown into Allow.
        var sysvolConfirmed = sysvolWrite?.Success == true;
        var editConfirmed = adEdit && sysvolConfirmed;

        var adEvidence = matching.Length == 0
            ? "AD: no matching token SID in the simplified GPMC permission list (unknown)."
            : "AD (simplified permissions): " + string.Join("; ",
                matching.Select(p =>
                    $"{p.TrusteeDisplay} [{p.TrusteeSid}] {p.PermissionDisplay}" +
                    (p.Denied ? " DENY" : " allow")));

        var fsEvidence = sysvolWrite?.Success switch
        {
            true => "SYSVOL GPT.INI: writable handle opened (not an AD access check).",
            false => "SYSVOL GPT.INI: write not confirmed (" +
                (sysvolWrite.Value.Length > 0 ? sysvolWrite.Value : "failed") + ").",
            _ => "SYSVOL GPT.INI: not tested (unknown)."
        };

        var state = matching.Length == 0
            ? "AD access unknown"
            : "AD access inferred from GPMC entries (not an AuthZ evaluation)";

        var summary =
            $"Access: Read {(adRead ? "AD evidence" : "unconfirmed")} | " +
            $"Edit {(editConfirmed ? "AD + SYSVOL evidence" : "unconfirmed")} | " +
            $"Security {(adSecurity ? "AD evidence" : "unconfirmed")}";

        return new GpoCapability(
            adRead, editConfirmed, adSecurity, summary,
            state + " | " + adEvidence + " | " + fsEvidence +
            " | Always recheck effective rights at the protected write operation.");
    }

    private static HashSet<string> CurrentTokenSids()
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            if (identity.User is not null)
                result.Add(identity.User.Value);

            if (identity.Groups is not null)
                foreach (var group in identity.Groups)
                    result.Add(group.Value);
        }
        catch
        {
            // An unreadable Windows token never becomes permission evidence.
        }

        return result;
    }
}

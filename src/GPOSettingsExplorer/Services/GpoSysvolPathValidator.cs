using System.Text.RegularExpressions;

namespace GPOSettingsExplorer.Services;

/// <summary>
/// Validates AD's advertised gPCFileSysPath without accessing that UNC path.
/// This is an identity/structure check, NOT proof that the advertised host is
/// an authentic domain controller or that the user's token can access SYSVOL.
/// All actual file reads must use the separately session-pinned DC.
/// </summary>
public static class GpoSysvolPathValidator
{
    private static readonly Regex Hostname = new(
        @"^[A-Za-z0-9](?:[A-Za-z0-9.-]{0,251}[A-Za-z0-9])?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static bool MatchesGpo(
        string? advertisedPath, string domainName, Guid gpoId, string? pinnedDc)
    {
        if (string.IsNullOrWhiteSpace(advertisedPath) ||
            string.IsNullOrWhiteSpace(domainName) ||
            advertisedPath.Length > 2048 ||
            !advertisedPath.StartsWith(@"\\", StringComparison.Ordinal) ||
            advertisedPath.Contains('/') ||
            !advertisedPath.Equals(advertisedPath.Trim(), StringComparison.Ordinal))
            return false;

        // Canonical AD GPC path: \\domain\SYSVOL\domain\Policies\{GUID}.
        // Reject extra segments, alternate shares, traversal and wrong domains.
        var segments = advertisedPath[2..].TrimEnd('\\').Split('\\');
        if (segments.Length != 5 ||
            segments.Any(part => part.Length == 0 || part is "." or "..") ||
            !segments[1].Equals("SYSVOL", StringComparison.OrdinalIgnoreCase) ||
            !segments[2].Equals(domainName, StringComparison.OrdinalIgnoreCase) ||
            !segments[3].Equals("Policies", StringComparison.OrdinalIgnoreCase) ||
            !segments[4].Equals(gpoId.ToString("B"), StringComparison.OrdinalIgnoreCase))
            return false;

        var host = segments[0];
        if (!Hostname.IsMatch(host) || host.Contains("..", StringComparison.Ordinal))
            return false;

        if (host.Equals(domainName, StringComparison.OrdinalIgnoreCase))
            return true; // The conventional domain-wide SYSVOL alias.

        if (!string.IsNullOrWhiteSpace(pinnedDc) &&
            (host.Equals(pinnedDc, StringComparison.OrdinalIgnoreCase) ||
             host.Equals(pinnedDc.Split('.')[0], StringComparison.OrdinalIgnoreCase)))
            return true;

        // A single DC label followed by the precise domain DNS suffix is
        // plausible. Arbitrary external UNC servers are not silently accepted.
        return host.EndsWith("." + domainName, StringComparison.OrdinalIgnoreCase) &&
               host.Split('.').Length == domainName.Split('.').Length + 1;
    }
}

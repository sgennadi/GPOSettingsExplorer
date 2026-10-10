using System.DirectoryServices;
using System.Globalization;
using System.Text.RegularExpressions;
using GPOSettingsExplorer.Models;

namespace GPOSettingsExplorer.Services;

/// <summary>
/// Checks domain/OU ancestry for ONE computer using the session-pinned DC.
/// Site links, target security token, WMI and client-side processing cannot
/// be determined from this directory traversal. Never writes to AD/SYSVOL.
/// </summary>
public sealed record GpoClientScopeLink(
    string ContainerDn, int Depth, int LinkOrder,
    bool Enabled, bool Enforced, string Status);

public sealed record GpoClientScopeReport(
    Guid GpoId, string Computer, string Domain, string PinnedDc,
    string ComputerDn, bool Complete, string Summary,
    IReadOnlyList<GpoClientScopeLink> Links)
{
    public string ToText() =>
        "COMPUTER AD CONTAINER PATH (READ ONLY)\n" +
        "Computer: " + Computer + "\nDomain: " + Domain +
        "\nPinned DC: " + PinnedDc +
        "\nComputer DN: " + ComputerDn +
        "\nEvidence: " + (Complete ? "Complete for domain/OU ancestor path" :
                            "INCOMPLETE / UNKNOWN") +
        "\n" + Summary + "\n\n" +
        string.Join("\n", Links.Select(l =>
            "[" + l.Status + "] " + l.ContainerDn +
            " | order " + l.LinkOrder +
            " | " + (l.Enforced ? "enforced" : "not enforced") +
            " | depth " + l.Depth)) +
        "\n\nSite links, security group membership, GPO ACL/NTFS rights, WMI " +
        "and client RSoP are NOT evaluated by this AD path scan.";
}

public sealed record GpoClientScopeContainer(
    string Dn, string GpLink, bool BlockInheritance);

public static class GpoClientScopeProbeService
{
    private const int MaxAncestry = 40;
    private const int MaxLinkBytes = 128 * 1024;
    private static readonly Regex ComputerLabel = new(
        @"^[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex LinkEntry = new(
        @"\[(?<path>LDAP://[^\[\];]+);(?<flags>[0-9]+)\]",
        RegexOptions.Compiled | RegexOptions.IgnoreCase |
        RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200));
    private static readonly Regex GpoPath = new(
        @"^LDAP://CN=\{(?<guid>[0-9a-fA-F-]{36})\},CN=Policies,CN=System,(?<domain>.+)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase |
        RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200));

    /// <summary>Allows only a local computer name or host within this DNS domain.</summary>
    public static string ValidateComputer(string input, string domain)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(input);
        ArgumentException.ThrowIfNullOrWhiteSpace(domain);
        var name = input.Trim();
        if (name == "." || name.Equals("localhost", StringComparison.OrdinalIgnoreCase))
            name = Environment.MachineName;
        if (name.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase))
            name = name[..^(domain.Length + 1)];
        if (!ComputerLabel.IsMatch(name))
            throw new ArgumentException(
                "Use a single computer name or FQDN in the selected AD DNS domain.");
        return name;
    }

    /// <summary>
    /// Pure link-path classifier for regression tests and for collected AD
    /// containers. Ordered nearest computer parent first, domain root last.
    /// Unknown syntax fails closed rather than silently omitting GPO links.
    /// </summary>
    public static GpoClientScopeReport Evaluate(
        Guid gpoId, string computer, string domain, string dc,
        string computerDn, IReadOnlyList<GpoClientScopeContainer> path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var links = new List<GpoClientScopeLink>();
        var complete = path.Count is >= 1 and <= MaxAncestry;
        var candidates = 0;
        var seen = 0;
        for (var depth = 0; depth < path.Count; depth++)
        {
            var container = path[depth];
            var raw = container.GpLink ?? "";
            if (raw.Length > MaxLinkBytes)
            {
                complete = false;
                continue;
            }
            MatchCollection matches;
            try { matches = LinkEntry.Matches(raw); }
            catch (RegexMatchTimeoutException)
            {
                complete = false;
                continue;
            }
            if (!string.Concat(matches.Cast<Match>().Select(x => x.Value))
                    .Equals(raw, StringComparison.Ordinal))
            {
                complete = false;
                continue;
            }

            for (var i = 0; i < matches.Count; i++)
            {
                var parsed = GpoPath.Match(matches[i].Groups["path"].Value);
                if (!parsed.Success || !Guid.TryParse(parsed.Groups["guid"].Value, out var id) ||
                    !parsed.Groups["domain"].Value.Equals(
                        string.Join(",", domain.Split('.').Select(label => "DC=" + label)),
                        StringComparison.OrdinalIgnoreCase) ||
                    !int.TryParse(matches[i].Groups["flags"].Value,
                        NumberStyles.None, CultureInfo.InvariantCulture, out var flags) ||
                    (flags & ~3) != 0)
                {
                    complete = false;
                    continue;
                }

                if (id != gpoId)
                    continue;
                seen++;
                var enabled = (flags & 1) == 0;
                var enforced = (flags & 2) != 0;
                var blockedByChild = !enforced &&
                    path.Take(depth).Any(c => c.BlockInheritance);
                var status = !enabled ? "Disabled direct link" :
                    blockedByChild ? "Blocked inherited link" : "Enabled path candidate";
                if (status == "Enabled path candidate")
                    candidates++;
                links.Add(new GpoClientScopeLink(
                    container.Dn, depth,
                    matches.Count - i, enabled, enforced, status));
            }
        }

        var summary = !complete
            ? "One or more required AD containers/link entries were not verified. " +
              "Do not conclude that this policy is absent or applied."
            : candidates > 0
                ? candidates + " enabled domain/OU link candidate(s) found. " +
                  "This is NOT proof of actual application."
                : seen > 0
                    ? "All observed direct/ancestor links were disabled or blocked. " +
                      "Other paths and site scope are not ruled out."
                    : "No link for this GPO on the inspected domain/OU path. " +
                      "Site links, cross-domain targeting and past RSoP remain unknown.";
        return new GpoClientScopeReport(
            gpoId, computer, domain, dc, computerDn,
            complete, summary, links);
    }

    public static GpoClientScopeReport Inspect(
        GpoInfo gpo, string computer, CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(gpo);
        var context = DomainConnectionState.Context;
        if (context is null ||
            !gpo.DomainName.Equals(context.DomainName, StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(context.ConnectedServer) ||
            string.IsNullOrWhiteSpace(context.DomainDistinguishedName))
            throw new InvalidOperationException(
                "A pinned DC in the GPO domain is required for an AD scope probe.");

        var host = ValidateComputer(computer, context.DomainName);
        cancellation.ThrowIfCancellationRequested();
        using var root = new DirectoryEntry(
            DomainConnectionState.BuildLdapPath(context.DomainDistinguishedName));
        using var search = new DirectorySearcher(root)
        {
            SearchScope = SearchScope.Subtree,
            Filter = "(&(objectCategory=computer)(sAMAccountName=" +
                     host + "$))",
            CacheResults = false, SizeLimit = 2,
            ClientTimeout = TimeSpan.FromSeconds(15),
            ServerTimeLimit = TimeSpan.FromSeconds(15)
        };
        search.PropertiesToLoad.Add("distinguishedName");
        using var records = search.FindAll();
        if (records.Count != 1 ||
            records[0].Properties["distinguishedname"].Count != 1)
            throw new InvalidOperationException(
                "Computer lookup returned zero or ambiguous AD objects on the pinned DC. " +
                "The domain/OU path is unknown.");
        var dn = Convert.ToString(records[0].Properties["distinguishedname"][0]) ?? "";
        if (!dn.EndsWith("," + context.DomainDistinguishedName,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(
                "Computer DN is outside the expected domain naming context.");
        var nodes = new List<GpoClientScopeContainer>();
        var current = GetParentDn(dn);
        var reachedDomain = false;
        for (var depth = 0; depth < MaxAncestry; depth++)
        {
            cancellation.ThrowIfCancellationRequested();
            using var entry = new DirectoryEntry(
                DomainConnectionState.BuildLdapPath(current));
            entry.RefreshCache(new[] { "gPLink", "gPOptions" });
            var raw = Convert.ToString(entry.Properties["gPLink"].Value) ?? "";
            if (raw.Length > MaxLinkBytes)
                throw new InvalidDataException("AD gPLink exceeds bounded size.");
            var options = entry.Properties["gPOptions"].Value is { } opt
                ? Convert.ToInt32(opt, CultureInfo.InvariantCulture) : 0;
            nodes.Add(new GpoClientScopeContainer(current, raw, (options & 1) == 1));
            if (current.Equals(context.DomainDistinguishedName,
                    StringComparison.OrdinalIgnoreCase))
            {
                reachedDomain = true;
                break;
            }
            var parent = GetParentDn(current);
            if (parent.Equals(current, StringComparison.OrdinalIgnoreCase) ||
                !parent.EndsWith(context.DomainDistinguishedName,
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("AD ancestry could not reach domain root.");
            current = parent;
        }

        if (!reachedDomain)
            throw new InvalidDataException("AD ancestry exceeded bounded depth.");
        return Evaluate(gpo.Id, host, context.DomainName,
            context.ConnectedServer, dn, nodes);
    }

    private static string GetParentDn(string dn)
    {
        for (var i = 0; i < dn.Length; i++)
        {
            if (dn[i] == '\\')
            {
                i++;
                continue;
            }
            if (dn[i] == ',' && i + 1 < dn.Length)
                return dn[(i + 1)..];
        }
        throw new InvalidDataException("Malformed AD distinguished name.");
    }
}

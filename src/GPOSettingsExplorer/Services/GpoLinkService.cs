using System.DirectoryServices;
using System.Text.RegularExpressions;
using GPOSettingsExplorer.Models;

namespace GPOSettingsExplorer.Services;

public sealed class GpoLinkService
{
    private static readonly Regex LinkRegex = new(
        @"\[(?<path>LDAP://.*?);(?<options>\d+)\]",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex GpoIdRegex = new(
        @"CN=\{(?<id>[0-9A-Fa-f-]{36})\},CN=Policies,CN=System",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public IReadOnlyList<GpoLinkTarget> LoadTargets(
        string domainDistinguishedName,
        string configurationNamingContext)
    {
        var targets = new List<GpoLinkTarget>();

        using (var domain = new DirectoryEntry($"LDAP://{domainDistinguishedName}"))
        {
            domain.RefreshCache(new[] { "distinguishedName", "name", "gPOptions" });

            targets.Add(new GpoLinkTarget
            {
                Name = Convert.ToString(domain.Properties["name"].Value) ?? domainDistinguishedName,
                DistinguishedName = domainDistinguishedName,
                TargetType = "Domain",
                BlockInheritance = ReadBlockInheritance(domain)
            });

            using var searcher = new DirectorySearcher(domain)
            {
                Filter = "(objectClass=organizationalUnit)",
                SearchScope = SearchScope.Subtree,
                PageSize = 1000
            };
            searcher.PropertiesToLoad.Add("distinguishedName");
            searcher.PropertiesToLoad.Add("name");
            searcher.PropertiesToLoad.Add("gPOptions");

            using var results = searcher.FindAll();
            foreach (SearchResult result in results)
            {
                var dn = GetProperty(result, "distinguishedName");
                if (string.IsNullOrWhiteSpace(dn))
                {
                    continue;
                }

                targets.Add(new GpoLinkTarget
                {
                    Name = GetProperty(result, "name"),
                    DistinguishedName = dn,
                    TargetType = "OU",
                    BlockInheritance = GetIntProperty(result, "gPOptions") == 1
                });
            }
        }

        if (!string.IsNullOrWhiteSpace(configurationNamingContext))
        {
            var sitesDn = $"CN=Sites,{configurationNamingContext}";
            using var sites = new DirectoryEntry($"LDAP://{sitesDn}");
            using var searcher = new DirectorySearcher(sites)
            {
                Filter = "(objectClass=site)",
                SearchScope = SearchScope.OneLevel,
                PageSize = 1000
            };
            searcher.PropertiesToLoad.Add("distinguishedName");
            searcher.PropertiesToLoad.Add("name");

            using var results = searcher.FindAll();
            foreach (SearchResult result in results)
            {
                var dn = GetProperty(result, "distinguishedName");
                if (string.IsNullOrWhiteSpace(dn))
                {
                    continue;
                }

                targets.Add(new GpoLinkTarget
                {
                    Name = GetProperty(result, "name"),
                    DistinguishedName = dn,
                    TargetType = "Site",
                    BlockInheritance = false
                });
            }
        }

        return targets
            .OrderBy(t => t.TargetType, StringComparer.OrdinalIgnoreCase)
            .ThenBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    public IReadOnlyList<GpoLinkInfo> LoadLinks(
        IEnumerable<GpoLinkTarget> targets,
        IEnumerable<GpoInfo> gpos)
    {
        var names = gpos.ToDictionary(g => g.Id, g => g.DisplayName);
        var links = new List<GpoLinkInfo>();

        foreach (var target in targets)
        {
            using var entry = new DirectoryEntry($"LDAP://{target.DistinguishedName}");
            entry.RefreshCache(new[] { "gPLink" });

            var raw = Convert.ToString(entry.Properties["gPLink"].Value) ?? string.Empty;
            var parsed = ParseLinks(raw);

            for (var i = 0; i < parsed.Count; i++)
            {
                var link = parsed[i];
                if (!TryGetGpoId(link.Path, out var id))
                {
                    continue;
                }

                links.Add(new GpoLinkInfo
                {
                    GpoId = id,
                    GpoName = names.TryGetValue(id, out var name) ? name : id.ToString("B"),
                    TargetName = target.Name,
                    TargetDn = target.DistinguishedName,
                    TargetType = target.TargetType,
                    Order = i + 1,
                    Enabled = (link.Options & 0x1) == 0,
                    Enforced = (link.Options & 0x2) != 0,
                    BlockInheritance = target.BlockInheritance
                });
            }
        }

        return links
            .OrderBy(l => l.TargetType, StringComparer.OrdinalIgnoreCase)
            .ThenBy(l => l.TargetName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(l => l.Order)
            .ToArray();
    }

    public void UpsertLink(
        string targetDn,
        Guid gpoId,
        bool enabled,
        bool enforced,
        int order)
    {
        EditingGuard.EnsureEnabled(
            "Change GPO link");
        using var entry = new DirectoryEntry($"LDAP://{targetDn}");
        entry.RefreshCache(new[] { "gPLink" });

        var raw = Convert.ToString(entry.Properties["gPLink"].Value) ?? string.Empty;
        var links = ParseLinks(raw);

        links.RemoveAll(link => TryGetGpoId(link.Path, out var id) && id == gpoId);

        var options = (enabled ? 0 : 0x1) | (enforced ? 0x2 : 0);
        var domainDn = ExtractDomainDn(targetDn);
        var path = $"LDAP://CN={{{gpoId:D}}},CN=Policies,CN=System,{domainDn}";

        var targetIndex = Math.Clamp(order <= 0 ? links.Count + 1 : order, 1, links.Count + 1) - 1;
        links.Insert(targetIndex, new LinkRecord(path, options));

        WriteLinks(entry, links);
    }

    public void RemoveLink(string targetDn, Guid gpoId)
    {
        EditingGuard.EnsureEnabled(
            "Remove GPO link");
        using var entry = new DirectoryEntry($"LDAP://{targetDn}");
        entry.RefreshCache(new[] { "gPLink" });

        var raw = Convert.ToString(entry.Properties["gPLink"].Value) ?? string.Empty;
        var links = ParseLinks(raw);
        links.RemoveAll(link => TryGetGpoId(link.Path, out var id) && id == gpoId);

        WriteLinks(entry, links);
    }

    public void SetBlockInheritance(string targetDn, bool block)
    {
        EditingGuard.EnsureEnabled(
            "Change block inheritance");
        using var entry = new DirectoryEntry($"LDAP://{targetDn}");
        entry.Properties["gPOptions"].Value = block ? 1 : 0;
        entry.CommitChanges();
    }

    private static List<LinkRecord> ParseLinks(string raw)
    {
        var result = new List<LinkRecord>();

        foreach (Match match in LinkRegex.Matches(raw))
        {
            var path = match.Groups["path"].Value;
            var optionsText = match.Groups["options"].Value;

            if (!int.TryParse(optionsText, out var options))
            {
                options = 0;
            }

            result.Add(new LinkRecord(path, options));
        }

        return result;
    }

    private static void WriteLinks(DirectoryEntry entry, IReadOnlyList<LinkRecord> links)
    {
        var value = string.Concat(links.Select(link => $"[{link.Path};{link.Options}]"));

        if (string.IsNullOrEmpty(value))
        {
            entry.Properties["gPLink"].Clear();
        }
        else
        {
            entry.Properties["gPLink"].Value = value;
        }

        entry.CommitChanges();
    }

    private static bool TryGetGpoId(string path, out Guid id)
    {
        id = Guid.Empty;
        var match = GpoIdRegex.Match(path);
        return match.Success && Guid.TryParse(match.Groups["id"].Value, out id);
    }

    private static string ExtractDomainDn(string distinguishedName)
    {
        var parts = distinguishedName
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(part => part.StartsWith("DC=", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (parts.Length == 0)
        {
            throw new InvalidOperationException(
                $"Cannot determine domain DN from target '{distinguishedName}'.");
        }

        return string.Join(",", parts);
    }

    private static bool ReadBlockInheritance(DirectoryEntry entry)
    {
        var value = entry.Properties["gPOptions"].Value;
        return value is not null && Convert.ToInt32(value) == 1;
    }

    private static string GetProperty(SearchResult result, string name)
    {
        return result.Properties.Contains(name) && result.Properties[name].Count > 0
            ? Convert.ToString(result.Properties[name][0]) ?? string.Empty
            : string.Empty;
    }

    private static int GetIntProperty(SearchResult result, string name)
    {
        var value = GetProperty(result, name);
        return int.TryParse(value, out var number) ? number : 0;
    }

    private sealed record LinkRecord(string Path, int Options);
}

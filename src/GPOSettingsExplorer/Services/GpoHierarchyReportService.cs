using System.Text;
using GPOSettingsExplorer.Models;

namespace GPOSettingsExplorer.Services;

/// <summary>
/// Reports the configured GPO links and their container-local priority, not
/// a user's effective RSOP. Actual applicability requires security/WMI filters,
/// site and OU ancestry, loopback, slow-link rules and CSE processing.
/// </summary>
public static class GpoHierarchyReportService
{
    public static string Save(
        IReadOnlyList<GpoLinkTarget> targets, IReadOnlyList<GpoLinkInfo> links)
    {
        var sb = new StringBuilder();
        sb.AppendLine("GPO SETTINGS EXPLORER - HIERARCHY AND LINK ORDER (READ ONLY)");
        sb.AppendLine("Generated-UTC: " + DateTimeOffset.UtcNow.ToString("O"));
        sb.AppendLine($"Targets: {targets.Count}; links: {links.Count}");
        sb.AppendLine("Order 1 has highest link precedence WITHIN the same container.");
        sb.AppendLine("This is not a user's or computer's resultant set of policy (RSoP).");
        sb.AppendLine("Security/WMI filtering, Enforced, inheritance, loopback, site and client-side extension rules may change the effective result.");
        sb.AppendLine();

        foreach (var target in targets.OrderBy(t => t.TargetType, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(t => t.DistinguishedName, StringComparer.OrdinalIgnoreCase))
        {
            sb.AppendLine($"[{target.TargetType}] {target.DistinguishedName}");
            sb.AppendLine("  Block inheritance: " + target.BlockInheritance);
            foreach (var link in links.Where(l =>
                             l.TargetDn.Equals(target.DistinguishedName,
                                 StringComparison.OrdinalIgnoreCase))
                         .OrderBy(l => l.Order))
            {
                sb.AppendLine($"  Link Order {link.Order}: {link.GpoName} {link.GpoId:B}" +
                    $" | Link enabled={link.Enabled} | Enforced={link.Enforced}");
            }
        }

        var dir = StoragePaths.Audit;
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir,
            "GpoHierarchy-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt");
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        return path;
    }
}

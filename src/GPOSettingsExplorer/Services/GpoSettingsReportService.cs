using System.Text;
using System.Xml;
using System.Xml.Linq;
using GPOSettingsExplorer.Models;

namespace GPOSettingsExplorer.Services;

/// <summary>
/// Safe, read-only native tree for GPMC report XML (no browser / HTML scripts).
/// The tree is informational; it never infers effective client RSoP.
/// XML entities/DTDs are forbidden and tree node counts are bounded.
/// </summary>
public sealed record GpoReportNode(
    string Label, string Details, IReadOnlyList<GpoReportNode> Children);

public sealed record GpoSettingsReport(
    string GpoName, DateTimeOffset CapturedUtc,
    IReadOnlyList<GpoReportNode> Sections, bool Limited, int XmlNodes);

public static class GpoSettingsReportService
{
    public const int MaxXmlCharacters = 16 * 1024 * 1024;
    public const int MaxXmlNodes = 30000;
    private const int MaxRenderedNodes = 3200;
    private const int MaxDepth = 15;

    public static GpoSettingsReport Parse(GpoInfo gpo, string xml)
    {
        ArgumentNullException.ThrowIfNull(gpo);
        if (string.IsNullOrWhiteSpace(xml) || xml.Length > MaxXmlCharacters)
            throw new InvalidDataException("GPMC XML report is absent or exceeds the 16 MiB display cap.");

        using var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = MaxXmlCharacters,
            MaxCharactersFromEntities = 0
        });
        var document = XDocument.Load(reader, LoadOptions.None);
        var root = document.Root ??
            throw new InvalidDataException("No GPO root node was found in GPMC XML.");
        if (!root.Name.LocalName.Equals("GPO", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Selected XML is not a GPMC GPO report.");

        var count = root.DescendantsAndSelf().Take(MaxXmlNodes + 1).Count();
        if (count > MaxXmlNodes)
            throw new InvalidDataException(
                "GPMC report exceeds the 30,000-element display cap. Open it in GPMC.");

        var remaining = MaxRenderedNodes;
        var limited = false;
        var sections = new List<GpoReportNode>();
        sections.Add(General(gpo, root, ref remaining, ref limited));
        sections.Add(Scope(gpo, root, "Computer", ref remaining, ref limited));
        sections.Add(Scope(gpo, root, "User", ref remaining, ref limited));
        return new GpoSettingsReport(
            gpo.DisplayName, DateTimeOffset.UtcNow, sections, limited, count);
    }

    private static GpoReportNode General(
        GpoInfo gpo, XElement root, ref int remaining, ref bool limited)
    {
        var details = "GPO: " + gpo.DisplayName +
            "\nDomain: " + gpo.DomainName +
            "\nID: " + gpo.Id.ToString("B") +
            "\nCreated: " + (gpo.CreationTime?.ToString("O") ?? "not reported") +
            "\nModified: " + (gpo.ModificationTime?.ToString("O") ?? "not reported") +
            "\nComputer: " + (gpo.ComputerEnabled ? "Enabled" : "Disabled") +
            "\nUser: " + (gpo.UserEnabled ? "Enabled" : "Disabled") +
            "\nGPMC report is read only. Links, filters, delegation and " +
            "effective RSoP must be independently verified before a change.";
        var children = new List<GpoReportNode>
        {
            new("Details", details, Array.Empty<GpoReportNode>())
        };
        var links = root.Elements().Where(e => Eq(e, "LinksTo")).ToArray();
        children.Add(new("Links",
            links.Length == 0
                ? "GPMC XML reports no direct LinksTo items. This is not a domain-wide absence check."
                : "Links observed by the GPMC report at capture time.",
            links.Select(l => XmlNode(l, ref remaining, ref limited, 0)).ToArray()));
        children.Add(new("WMI Filtering",
            string.IsNullOrWhiteSpace(gpo.WmiFilterName)
                ? "No assigned WMI filter reported by current GPO inventory."
                : "Assigned WMI filter: " + gpo.WmiFilterName,
            Array.Empty<GpoReportNode>()));
        var descriptor = root.Elements().FirstOrDefault(e => Eq(e, "SecurityDescriptor"));
        var trustees = descriptor?.Descendants().Where(e => Eq(e, "TrusteePermissions"))
            .Take(200).ToArray() ?? Array.Empty<XElement>();
        var trusteeNodes = trustees.Select(t =>
            XmlNode(t, ref remaining, ref limited, 0)).ToArray();
        children.Add(new("Security Filtering",
            "Potential permission evidence from GPMC XML. Apply Group Policy " +
            "and Read access must be evaluated against actual AD and SYSVOL ACL; " +
            "listed trustees are NOT proof of effective filtering.",
            trusteeNodes));
        children.Add(new("Delegation",
            "GPMC permission descriptors (may include users/groups/SIDs). " +
            "This does not calculate a client access token.",
            trusteeNodes));
        return new GpoReportNode("General", details, children);
    }

    private static GpoReportNode Scope(GpoInfo gpo, XElement root, string scopeName,
        ref int remaining, ref bool limited)
    {
        var enabled = scopeName == "Computer" ? gpo.ComputerEnabled : gpo.UserEnabled;
        var title = scopeName + " Configuration (" + (enabled ? "Enabled" : "Disabled") + ")";
        var scope = root.Elements().FirstOrDefault(e => Eq(e, scopeName));
        if (scope is null)
            return new GpoReportNode(title, "This GPMC report has no " +
                scopeName + " element. Do not infer Not Configured.",
                Array.Empty<GpoReportNode>());

        var policyBuckets = new SortedDictionary<string, List<GpoReportNode>>(
            StringComparer.OrdinalIgnoreCase);
        var preferenceBuckets = new SortedDictionary<string, List<GpoReportNode>>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var ext in scope.Descendants().Where(e => Eq(e, "Extension") &&
                     e.Parent is not null && Eq(e.Parent, "ExtensionData")))
        {
            if (remaining <= 0) { limited = true; break; }
            var type = ext.Attributes().FirstOrDefault(a =>
                a.Name.LocalName.Equals("type", StringComparison.OrdinalIgnoreCase))?.Value ?? "";
            if (type.Contains(':'))
                type = type[(type.LastIndexOf(':') + 1)..];
            if (type.Length == 0)
                type = ext.Name.NamespaceName.Split('/').LastOrDefault() ?? "Other";
            var group = ExtensionSection(type);
            var bucket = IsPreference(type) ? preferenceBuckets : policyBuckets;
            if (!bucket.TryGetValue(group, out var sections))
                bucket[group] = sections = new List<GpoReportNode>();

            if (type.Equals("SecuritySettings", StringComparison.OrdinalIgnoreCase))
                sections.Add(SecurityExtension(ext, ref remaining, ref limited));
            else
                sections.Add(new GpoReportNode(Display(type),
                    "GPMC source extension: " + type + ". Values are stored GPO source, not RSoP.",
                    ext.Elements().Take(200).Select(node =>
                        XmlNode(node, ref remaining, ref limited, 0)).ToArray()));
        }

        static IReadOnlyList<GpoReportNode> Assemble(
            SortedDictionary<string, List<GpoReportNode>> buckets) =>
            buckets.Select(entry =>
                new GpoReportNode(entry.Key, "GPMC XML report section (read only).",
                    entry.Value.ToArray())).ToArray();
        return new GpoReportNode(title,
            "Captured stored source extensions: " + 
            policyBuckets.Values.Sum(x => x.Count) + " policy, " +
            preferenceBuckets.Values.Sum(x => x.Count) + " preference.",
            new GpoReportNode[]
            {
                new("Policies", "Stored Group Policy settings (not applied RSoP).",
                    Assemble(policyBuckets)),
                new("Preferences (GPP)", "Stored Group Policy Preferences.",
                    Assemble(preferenceBuckets))
            });
    }

    private static GpoReportNode SecurityExtension(
        XElement ext, ref int remaining, ref bool limited)
    {
        var known = ext.Descendants().Where(e =>
            KerberosPolicyMetadataService.TryDescribeGpmcNode(e, out _)).ToArray();
        var other = ext.Elements().Where(e =>
            !KerberosPolicyMetadataService.TryDescribeGpmcNode(e, out _)).ToArray();
        var items = new List<GpoReportNode>();
        if (known.Length > 0)
        {
            items.Add(new GpoReportNode("Account Policies",
                "Security policy source, not Registry.pol.",
                new[]
                {
                    new GpoReportNode("Kerberos Policy",
                        "Known GPMC Kerberos account entries; verify effective " +
                        "values using GPMC and client diagnostics.",
                        known.Select(e =>
                        {
                            KerberosPolicyMetadataService.TryDescribeGpmcNode(e, out var policy);
                            return new GpoReportNode(policy.DisplayName,
                                "Internal GPMC name: " + policy.InternalName + "\n" +
                                XmlDetails(e), Array.Empty<GpoReportNode>());
                        }).ToArray())
                }));
        }
        if (other.Length > 0)
            items.Add(new GpoReportNode("Other Security Settings",
                "Other extension values are grouped without inventing their MMC section.",
                other.Take(200).Select(e =>
                    XmlNode(e, ref remaining, ref limited, 0)).ToArray()));
        return new GpoReportNode("Security Settings",
            "Policies > Windows Settings > Security Settings. " +
            "Native GPMC security data is not a Registry source.",
            items);
    }

    private static GpoReportNode XmlNode(
        XElement element, ref int remaining, ref bool limited, int depth)
    {
        if (remaining-- <= 0 || depth >= MaxDepth)
        {
            limited = true;
            return new GpoReportNode("[additional nested data omitted]",
                "Read-only display limit reached; inspect the original GPMC report.",
                Array.Empty<GpoReportNode>());
        }
        if (KerberosPolicyMetadataService.TryDescribeGpmcNode(element, out var kerberos))
            return new GpoReportNode(kerberos.DisplayName, XmlDetails(element),
                Array.Empty<GpoReportNode>());
        var name = element.Name.LocalName;
        var label = Display(name);
        var nameAttr = element.Attributes().FirstOrDefault(a =>
            a.Name.LocalName.Equals("name", StringComparison.OrdinalIgnoreCase));
        if (nameAttr is not null && !string.IsNullOrWhiteSpace(nameAttr.Value))
            label += ": " + Cut(nameAttr.Value, 130);
        var children = element.Elements().Take(180).Select(e =>
            XmlNode(e, ref remaining, ref limited, depth + 1)).ToArray();
        if (element.Elements().Skip(180).Any())
            limited = true;
        return new GpoReportNode(label, XmlDetails(element), children);
    }

    private static string XmlDetails(XElement element)
    {
        var parts = new List<string> { "GPMC source element: " + element.Name.LocalName };
        foreach (var a in element.Attributes().Where(a => !a.IsNamespaceDeclaration).Take(24))
        {
            var value = IsSensitive(a.Name.LocalName) ? "[redacted]" : Cut(a.Value, 400);
            parts.Add(a.Name.LocalName + ": " + value);
        }
        if (!element.HasElements && !string.IsNullOrWhiteSpace(element.Value))
        {
            parts.Add("Value: " + (IsSensitive(element.Name.LocalName)
                ? "[redacted]" : Cut(element.Value.Trim(), 5000)));
        }
        return string.Join("\n", parts);
    }

    private static bool IsSensitive(string name) =>
        name.Contains("password", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("cpassword", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("secret", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("recoverykey", StringComparison.OrdinalIgnoreCase);

    private static string Cut(string value, int limit) =>
        value.Length > limit ? value[..limit] + " [truncated]" : value;

    private static bool Eq(XElement e, string local) =>
        e.Name.LocalName.Equals(local, StringComparison.OrdinalIgnoreCase);

    private static bool IsPreference(string type) =>
        type.Contains("Preference", StringComparison.OrdinalIgnoreCase) ||
        type is "Drives" or "Folders" or "Files" or "Shortcuts" or
            "Printers" or "ScheduledTasks" or "Services" or "EnvironmentVariables" or
            "LocalUsersAndGroups";

    private static string ExtensionSection(string type) => type switch
    {
        "RegistrySettings" => "Administrative Templates",
        "SoftwareInstallationSettings" => "Software Settings",
        "SecuritySettings" or "AuditSettings" => "Windows Settings > Security Settings",
        "Scripts" or "Script" => "Windows Settings > Scripts",
        "NrptSettings" => "Windows Settings > Name Resolution Policy",
        _ => IsPreference(type) ? "Preferences" : "Windows Settings / Other"
    };

    private static string Display(string value)
    {
        var result = new StringBuilder(value.Length + 8);
        for (var i = 0; i < value.Length; i++)
        {
            var ch = value[i];
            if (i > 0 && char.IsUpper(ch) && char.IsLower(value[i - 1]))
                result.Append(' ');
            result.Append(ch);
        }
        return result.ToString();
    }
}

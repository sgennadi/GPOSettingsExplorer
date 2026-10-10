using System.Xml;
using System.Xml.Linq;
using GPOSettingsExplorer.Models;

namespace GPOSettingsExplorer.Services;

/// <summary>
/// Safe GPP XML read-only item projection. Never evaluates Item Level
/// Targeting, CSE precedence or target-side application. Sensitive XML
/// attributes such as cpassword are redacted from UI and exports.
/// </summary>
public static class GppXmlSourceReader
{
    public const int MaxFileBytes = 16 * 1024 * 1024;
    private const int MaxItems = 20_000;
    private const int MaxSummaryChars = 6_000;

    public static SourceParseResult Parse(
        byte[] bytes, Guid gpoId, string gpoName,
        string scope, string sourceFile, string sourceSha256)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (bytes.Length == 0 || bytes.Length > MaxFileBytes)
            return Fail("GPP XML is empty or exceeds the 16 MiB read limit.");
        if (scope is not ("Computer" or "User"))
            return Fail("GPP source scope must be Computer or User.");

        XDocument doc;
        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            using var xml = XmlReader.Create(stream, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = MaxFileBytes,
                MaxCharactersFromEntities = 0
            });
            doc = XDocument.Load(xml, LoadOptions.SetLineInfo);
        }
        catch (XmlException ex)
        {
            return Fail("GPP XML rejected: " + ex.Message);
        }

        if (doc.Root is null)
            return Fail("GPP XML has no root.");
        var rootName = doc.Root.Name.LocalName;
        var rows = new List<RealSettingRecord>();
        var issues = new List<string>();
        var index = 0;
        foreach (var item in doc.Root.Elements())
        {
            if (++index > MaxItems)
            {
                issues.Add("GPP XML item limit reached; additional items not projected.");
                break;
            }

            var properties = item.Elements().FirstOrDefault(element =>
                element.Name.LocalName.Equals("Properties", StringComparison.OrdinalIgnoreCase));
            var name = (string?)item.Attribute("name") ??
                (string?)properties?.Attribute("name") ??
                (string?)properties?.Attribute("userName") ??
                $"{item.Name.LocalName} #{index}";
            var action = (string?)properties?.Attribute("action") ?? "";
            var disabled = ((string?)item.Attribute("disabled") ?? "")
                .Equals("1", StringComparison.OrdinalIgnoreCase) ||
                ((string?)item.Attribute("disabled") ?? "")
                .Equals("true", StringComparison.OrdinalIgnoreCase);

            var attributes = new List<string>();
            void AppendAttributes(XElement node, string prefix)
            {
                foreach (var attribute in node.Attributes())
                {
                    var field = attribute.Name.LocalName;
                    var sensitive = field.Contains("password", StringComparison.OrdinalIgnoreCase) ||
                        field.Contains("secret", StringComparison.OrdinalIgnoreCase) ||
                        field.Contains("credential", StringComparison.OrdinalIgnoreCase);
                    attributes.Add(prefix + field + "=" +
                        (sensitive ? "[REDACTED IN EVIDENCE]" : attribute.Value));
                    if (attributes.Count > 100)
                    {
                        issues.Add("Item has too many projected XML attributes.");
                        break;
                    }
                }
            }
            AppendAttributes(item, "item.");
            if (properties is not null)
                AppendAttributes(properties, "properties.");
            var summary = string.Join("; ", attributes);
            if (summary.Length > MaxSummaryChars)
            {
                summary = summary[..MaxSummaryChars] + " [truncated; inspect original file]";
                issues.Add("Item XML attribute projection exceeded display limit.");
            }
            var hasFilters = item.Elements().Any(element =>
                element.Name.LocalName.Equals("Filters", StringComparison.OrdinalIgnoreCase));
            var info = item as IXmlLineInfo;
            rows.Add(new RealSettingRecord
            {
                GpoId = gpoId,
                GpoName = gpoName,
                Scope = scope,
                Category = "GPP XML source > " + rootName,
                SettingName = name,
                State = disabled ? "Stored disabled preference item" :
                    "Stored preference item",
                Value = summary,
                ValueType = "GPP XML attributes (partial projection)",
                SourceFile = sourceFile,
                SourceSha256 = sourceSha256,
                Evidence = $"GPP XML root '{rootName}', item '{item.Name.LocalName}', " +
                    $"line {(info?.HasLineInfo() == true ? info.LineNumber : 0)}, " +
                    $"action '{action}', Item Level Targeting " +
                    (hasFilters ? "present (NOT evaluated)" : "not observed in this item") +
                    ". Stored source attributes only; nested settings are not fully projected, " +
                    "sensitive attributes redacted, CSE/RSoP not evaluated."
            });
        }
        return new SourceParseResult(rows, issues);

        static SourceParseResult Fail(string error) =>
            new(Array.Empty<RealSettingRecord>(), new[] { error });
    }
}

using System.Xml.Linq;
using GPOSettingsExplorer.Models;

namespace GPOSettingsExplorer.Services;

public sealed class SemanticXmlDiffService
{
    private static readonly HashSet<string> IgnoredNames =
        new(
            new[]
            {
                "GeneratedTime",
                "ReadTime",
                "ModifiedTime",
                "CreationTime",
                "BackupTime"
            },
            StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<SemanticDiffRow> Compare(
        string leftXml,
        string rightXml) =>
        CompareDocuments(
            XDocument.Load(
                leftXml,
                LoadOptions.None),
            XDocument.Load(
                rightXml,
                LoadOptions.None));

    public IReadOnlyList<SemanticDiffRow> CompareText(
        string leftXml,
        string rightXml) =>
        CompareDocuments(
            XDocument.Parse(
                leftXml,
                LoadOptions.None),
            XDocument.Parse(
                rightXml,
                LoadOptions.None));

    private static IReadOnlyList<SemanticDiffRow> CompareDocuments(
        XDocument leftDocument,
        XDocument rightDocument)
    {
        var left =
            Flatten(
                leftDocument);

        var right =
            Flatten(
                rightDocument);

        return left.Keys
            .Union(
                right.Keys,
                StringComparer.OrdinalIgnoreCase)
            .OrderBy(
                key =>
                    key,
                StringComparer.OrdinalIgnoreCase)
            .Select(
                key =>
                {
                    left.TryGetValue(
                        key,
                        out var leftValue);

                    right.TryGetValue(
                        key,
                        out var rightValue);

                    var status =
                        leftValue is null
                            ? "Backup only"
                            : rightValue is null
                                ? "Current only"
                                : leftValue.Equals(
                                    rightValue,
                                    StringComparison.Ordinal)
                                    ? "Same"
                                    : "Different";

                    return new SemanticDiffRow
                    {
                        Path =
                            key,
                        LeftValue =
                            leftValue
                            ?? string.Empty,
                        RightValue =
                            rightValue
                            ?? string.Empty,
                        Status =
                            status
                    };
                })
            .ToArray();
    }

    private static Dictionary<string, string> Flatten(
        XDocument document)
    {
        var result =
            new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase);

        if (document.Root is null)
        {
            return result;
        }

        Visit(
            document.Root,
            "/" +
            document.Root.Name.LocalName,
            result);

        return result;
    }

    private static void Visit(
        XElement element,
        string path,
        IDictionary<string, string> result)
    {
        if (IgnoredNames.Contains(
                element.Name.LocalName))
        {
            return;
        }

        var identity =
            Identity(
                element);

        var currentPath =
            string.IsNullOrWhiteSpace(
                identity)
                ? path
                : $"{path}[{identity}]";

        foreach (var attribute in element.Attributes()
                     .Where(
                         attribute =>
                             !attribute.IsNamespaceDeclaration &&
                             !IgnoredNames.Contains(
                                 attribute.Name.LocalName))
                     .OrderBy(
                         attribute =>
                             attribute.Name.LocalName,
                         StringComparer.OrdinalIgnoreCase))
        {
            result[
                $"{currentPath}/@{attribute.Name.LocalName}"] =
                attribute.Value.Trim();
        }

        var children =
            element.Elements()
                .ToArray();

        if (children.Length == 0)
        {
            var value =
                element.Value.Trim();

            if (!string.IsNullOrWhiteSpace(
                    value))
            {
                result[
                    currentPath] =
                    value;
            }

            return;
        }

        var counters =
            new Dictionary<string, int>(
                StringComparer.OrdinalIgnoreCase);

        foreach (var child in children)
        {
            var name =
                child.Name.LocalName;

            counters.TryGetValue(
                name,
                out var index);

            counters[
                name] =
                index + 1;

            var identity =
                Identity(
                    child);

            var childPath =
                string.IsNullOrWhiteSpace(
                    identity)
                    ? $"{currentPath}/{name}#{index + 1}"
                    : $"{currentPath}/{name}";

            Visit(
                child,
                childPath,
                result);
        }
    }

    private static string Identity(
        XElement element)
    {
        foreach (var name in new[]
                 {
                     "name",
                     "Name",
                     "id",
                     "ID",
                     "key",
                     "Key",
                     "valueName",
                     "ValueName"
                 })
        {
            var value =
                element.Attribute(
                    name)
                ?.Value;

            if (!string.IsNullOrWhiteSpace(
                    value))
            {
                return
                    $"{name}={value.Trim()}";
            }
        }

        return string.Empty;
    }
}

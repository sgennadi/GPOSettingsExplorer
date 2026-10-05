using System.Globalization;
using System.Xml.Linq;
using GPOSettingsExplorer.Models;

namespace GPOSettingsExplorer.Services;

public sealed class GppShortcutService
{
    private const string RootClsid = "{872ECB34-B2EC-401B-A585-D32574AA90EE}";
    private const string ItemClsid = "{4F2F7C55-2790-433E-8127-0739D1CFA327}";

    private readonly GppDocumentService _documents;

    public GppShortcutService(GppDocumentService documents)
    {
        _documents = documents;
    }

    public IReadOnlyList<GppShortcutItemInfo> Load(
        IEnumerable<GpoInfo> gpos,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var result = new List<GppShortcutItemInfo>();
        var list = gpos.ToList();
        var type = GetShortcutType();

        for (var index = 0; index < list.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var gpo = list[index];
            progress?.Report($"Shortcuts {index + 1}/{list.Count}: {gpo.DisplayName}");

            foreach (var scope in new[] { "Computer", "User" })
            {
                var target = _documents.BuildTarget(gpo, scope, type);
                if (!File.Exists(target.XmlPath))
                    continue;

                try
                {
                    var document = XDocument.Load(
                        target.XmlPath,
                        LoadOptions.PreserveWhitespace);

                    var ordinal = 0;

                    foreach (var shortcut in document
                                 .Descendants()
                                 .Where(element => element.Name.LocalName == "Shortcut"))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        ordinal++;

                        var properties = shortcut.Elements()
                            .FirstOrDefault(element => element.Name.LocalName == "Properties");

                        if (properties is null)
                            continue;

                        var filters = shortcut.Elements()
                            .FirstOrDefault(element => element.Name.LocalName == "Filters");

                        var shortcutPath = Attr(properties, "shortcutPath");
                        var displayName = FirstNonEmpty(
                            Attr(shortcut, "name"),
                            Attr(shortcut, "status"),
                            Path.GetFileNameWithoutExtension(
                                shortcutPath.Replace(
                                    '%',
                                    '_')));

                        result.Add(new GppShortcutItemInfo
                        {
                            GpoId = gpo.Id,
                            GpoName = gpo.DisplayName,
                            DomainName = gpo.DomainName,
                            Scope = scope,
                            XmlPath = target.XmlPath,
                            Uid = Attr(shortcut, "uid"),
                            Ordinal = ordinal,
                            DisplayName = displayName,
                            Action = FirstNonEmpty(Attr(properties, "action"), "U"),
                            ShortcutPath = shortcutPath,
                            TargetType = NormalizeTargetType(
                                FirstNonEmpty(
                                    Attr(properties, "targetType"),
                                    "FILESYSTEM")),
                            TargetPath = Attr(properties, "targetPath"),
                            Arguments = Attr(properties, "arguments"),
                            StartIn = Attr(properties, "startIn"),
                            ShortcutKey = FirstNonEmpty(
                                Attr(properties, "shortcutKey"),
                                "0"),
                            Window = Attr(properties, "window"),
                            Comment = Attr(properties, "comment"),
                            IconPath = Attr(properties, "iconPath"),
                            IconIndex = FirstNonEmpty(
                                Attr(properties, "iconIndex"),
                                "0"),
                            Pidl = Attr(properties, "pidl"),
                            Disabled = IsTrue(Attr(shortcut, "disabled")),
                            BypassErrors = IsTrue(Attr(shortcut, "bypassErrors")),
                            RemoveWhenNoLongerApplied = IsTrue(Attr(shortcut, "removePolicy")),
                            RunInUserContext = IsTrue(Attr(shortcut, "userContext")),
                            FiltersXml = filters?.ToString(SaveOptions.DisableFormatting)
                                         ?? string.Empty
                        });
                    }
                }
                catch
                {
                    // Malformed documents remain available in the raw GPP XML editor.
                }
            }
        }

        return result
            .OrderBy(item => item.GpoName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(item => item.Scope, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.ShortcutPath, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(item => item.Ordinal)
            .ToArray();
    }

    public GppShortcutItemInfo CreateNew(
        GpoInfo gpo,
        string scope) =>
        new()
        {
            GpoId = gpo.Id,
            GpoName = gpo.DisplayName,
            DomainName = gpo.DomainName,
            Scope = scope.Equals("User", StringComparison.OrdinalIgnoreCase)
                ? "User"
                : "Computer",
            XmlPath = _documents.BuildTarget(
                gpo,
                scope.Equals("User", StringComparison.OrdinalIgnoreCase)
                    ? "User"
                    : "Computer",
                GetShortcutType()).XmlPath,
            Uid = Guid.NewGuid().ToString("B").ToUpperInvariant(),
            DisplayName = "New Shortcut",
            Action = "U",
            TargetType = "FILESYSTEM",
            ShortcutKey = "0",
            IconIndex = "0"
        };

    public void Save(
        GpoInfo gpo,
        string domainDistinguishedName,
        GppShortcutItemInfo item)
    {
        Validate(item);

        var type = GetShortcutType();
        var target = _documents.BuildTarget(
            gpo,
            item.Scope,
            type);

        XDocument document;

        if (File.Exists(target.XmlPath))
        {
            document = XDocument.Load(
                target.XmlPath,
                LoadOptions.PreserveWhitespace);

            if (document.Root is null ||
                !document.Root.Name.LocalName.Equals(
                    "Shortcuts",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "The existing Shortcuts.xml root element is not <Shortcuts>. " +
                    "Use the raw GPP XML editor to repair it.");
            }
        }
        else
        {
            document = new XDocument(
                new XDeclaration("1.0", "utf-8", null),
                new XElement(
                    "Shortcuts",
                    new XAttribute("clsid", RootClsid)));
        }

        var shortcuts = document
            .Descendants()
            .Where(element => element.Name.LocalName == "Shortcut")
            .ToArray();

        var shortcut = FindShortcut(shortcuts, item);

        if (shortcut is null)
        {
            shortcut = new XElement(
                "Shortcut",
                new XAttribute("clsid", ItemClsid));

            document.Root!.Add(shortcut);
        }

        UpdateShortcut(shortcut, item);

        _documents.SaveXml(
            gpo,
            domainDistinguishedName,
            target,
            document.ToString());
    }

    public void Delete(
        GpoInfo gpo,
        string domainDistinguishedName,
        GppShortcutItemInfo item)
    {
        var type = GetShortcutType();
        var target = _documents.BuildTarget(
            gpo,
            item.Scope,
            type);

        if (!File.Exists(target.XmlPath))
            return;

        var document = XDocument.Load(
            target.XmlPath,
            LoadOptions.PreserveWhitespace);

        var shortcuts = document
            .Descendants()
            .Where(element => element.Name.LocalName == "Shortcut")
            .ToArray();

        var shortcut = FindShortcut(shortcuts, item)
            ?? throw new InvalidOperationException(
                "The selected Shortcut preference no longer exists. Refresh the list.");

        shortcut.Remove();

        if (!document.Descendants()
                .Any(element => element.Name.LocalName == "Shortcut"))
        {
            _documents.Delete(
                gpo,
                domainDistinguishedName,
                target);
            return;
        }

        _documents.SaveXml(
            gpo,
            domainDistinguishedName,
            target,
            document.ToString());
    }

    public GppShortcutItemInfo CopyEditableValues(
        GppShortcutItemInfo source,
        GppShortcutItemInfo target)
    {
        target.DisplayName = source.DisplayName;
        target.Action = source.Action;
        target.ShortcutPath = source.ShortcutPath;
        target.TargetType = source.TargetType;
        target.TargetPath = source.TargetPath;
        target.Arguments = source.Arguments;
        target.StartIn = source.StartIn;
        target.ShortcutKey = source.ShortcutKey;
        target.Window = source.Window;
        target.Comment = source.Comment;
        target.IconPath = source.IconPath;
        target.IconIndex = source.IconIndex;
        target.Pidl = source.Pidl;

        target.Disabled = source.Disabled;
        target.BypassErrors = source.BypassErrors;
        target.RemoveWhenNoLongerApplied = source.RemoveWhenNoLongerApplied;
        target.RunInUserContext = source.RunInUserContext;
        target.FiltersXml = source.FiltersXml;

        return target;
    }

    private static void UpdateShortcut(
        XElement shortcut,
        GppShortcutItemInfo item)
    {
        var display = string.IsNullOrWhiteSpace(item.DisplayName)
            ? item.ShortcutPath.Trim()
            : item.DisplayName.Trim();

        SetAttr(shortcut, "clsid", ItemClsid);
        SetAttr(shortcut, "name", display);
        SetAttr(shortcut, "status", display);
        SetAttr(shortcut, "image", "2");
        SetAttr(
            shortcut,
            "changed",
            DateTime.UtcNow.ToString(
                "yyyy-MM-dd HH:mm:ss",
                CultureInfo.InvariantCulture));

        if (string.IsNullOrWhiteSpace(item.Uid))
            item.Uid = Guid.NewGuid().ToString("B").ToUpperInvariant();

        SetAttr(shortcut, "uid", item.Uid);

        SetOptionalBool(shortcut, "disabled", item.Disabled);
        SetOptionalBool(shortcut, "bypassErrors", item.BypassErrors);
        SetOptionalBool(shortcut, "removePolicy", item.RemoveWhenNoLongerApplied);
        SetOptionalBool(shortcut, "userContext", item.RunInUserContext);

        var properties = shortcut.Elements()
            .FirstOrDefault(element => element.Name.LocalName == "Properties");

        if (properties is null)
        {
            properties = new XElement("Properties");
            shortcut.AddFirst(properties);
        }

        SetAttr(properties, "pidl", item.Pidl);
        SetAttr(properties, "targetType", NormalizeTargetType(item.TargetType));
        SetAttr(properties, "action", NormalizeAction(item.Action));
        SetAttr(properties, "comment", item.Comment.Trim());
        SetAttr(properties, "shortcutKey", item.ShortcutKey.Trim());
        SetAttr(properties, "startIn", item.StartIn.Trim());
        SetAttr(properties, "arguments", item.Arguments);
        SetAttr(properties, "iconIndex", NormalizeIconIndex(item.IconIndex));
        SetAttr(properties, "targetPath", item.TargetPath.Trim());
        SetAttr(properties, "iconPath", item.IconPath.Trim());
        SetAttr(properties, "window", item.Window.Trim());
        SetAttr(properties, "shortcutPath", item.ShortcutPath.Trim());

        ApplyFilters(shortcut, item.FiltersXml);
    }

    private static void ApplyFilters(
        XElement shortcut,
        string filtersXml)
    {
        var existingFilters = shortcut.Elements()
            .FirstOrDefault(element => element.Name.LocalName == "Filters");

        if (string.IsNullOrWhiteSpace(filtersXml))
        {
            existingFilters?.Remove();
            return;
        }

        XElement filters;

        try
        {
            filters = XElement.Parse(
                filtersXml,
                LoadOptions.PreserveWhitespace);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Item-level targeting XML is invalid: {ex.Message}",
                ex);
        }

        if (!filters.Name.LocalName.Equals(
                "Filters",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Item-level targeting XML must have a <Filters> root element.");
        }

        if (existingFilters is null)
            shortcut.Add(filters);
        else
            existingFilters.ReplaceWith(filters);
    }

    private static XElement? FindShortcut(
        IReadOnlyList<XElement> shortcuts,
        GppShortcutItemInfo item)
    {
        if (!string.IsNullOrWhiteSpace(item.Uid))
        {
            var byUid = shortcuts.FirstOrDefault(element =>
                Attr(element, "uid").Equals(
                    item.Uid,
                    StringComparison.OrdinalIgnoreCase));

            if (byUid is not null)
                return byUid;
        }

        if (item.Ordinal > 0 &&
            item.Ordinal <= shortcuts.Count)
        {
            return shortcuts[item.Ordinal - 1];
        }

        return null;
    }

    private static void Validate(GppShortcutItemInfo item)
    {
        var action = NormalizeAction(item.Action);
        var targetType = NormalizeTargetType(item.TargetType);

        if (string.IsNullOrWhiteSpace(item.ShortcutPath))
        {
            throw new InvalidOperationException(
                "Shortcut path cannot be empty.");
        }

        if (action != "D" &&
            string.IsNullOrWhiteSpace(item.TargetPath))
        {
            throw new InvalidOperationException(
                "Target path cannot be empty for Create, Replace, or Update.");
        }

        if (targetType == "URL" &&
            action != "D" &&
            !Uri.TryCreate(
                item.TargetPath.Trim(),
                UriKind.Absolute,
                out _))
        {
            throw new InvalidOperationException(
                "URL shortcut target must be an absolute URL.");
        }

        _ = NormalizeIconIndex(item.IconIndex);

        if (!string.IsNullOrWhiteSpace(item.FiltersXml))
        {
            var filters = XElement.Parse(item.FiltersXml);
            if (!filters.Name.LocalName.Equals(
                    "Filters",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "Item-level targeting XML must have a <Filters> root element.");
            }
        }
    }

    private GppDocumentTypeInfo GetShortcutType() =>
        _documents.GetKnownTypes().First(type =>
            type.Name.Equals(
                "Shortcuts",
                StringComparison.OrdinalIgnoreCase));

    private static string NormalizeAction(string action) =>
        action.Trim().ToUpperInvariant() switch
        {
            "C" or "CREATE" => "C",
            "D" or "DELETE" => "D",
            "R" or "REPLACE" => "R",
            _ => "U"
        };

    private static string NormalizeTargetType(string value) =>
        value.Trim().ToUpperInvariant() switch
        {
            "URL" => "URL",
            "SHELL" => "SHELL",
            _ => "FILESYSTEM"
        };

    private static string NormalizeIconIndex(string value)
    {
        var text = string.IsNullOrWhiteSpace(value)
            ? "0"
            : value.Trim();

        if (!int.TryParse(
                text,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out _))
        {
            throw new InvalidOperationException(
                "Icon index must be an integer.");
        }

        return text;
    }

    private static string Attr(
        XElement element,
        string name) =>
        element.Attributes()
            .FirstOrDefault(attribute =>
                attribute.Name.LocalName.Equals(
                    name,
                    StringComparison.OrdinalIgnoreCase))?
            .Value ?? string.Empty;

    private static bool IsTrue(string value) =>
        value == "1" ||
        value.Equals(
            "true",
            StringComparison.OrdinalIgnoreCase);

    private static string FirstNonEmpty(params string[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))
        ?? string.Empty;

    private static void SetAttr(
        XElement element,
        string name,
        string value) =>
        element.SetAttributeValue(
            name,
            value ?? string.Empty);

    private static void SetOptionalBool(
        XElement element,
        string name,
        bool value)
    {
        if (value)
            element.SetAttributeValue(name, "1");
        else
            element.SetAttributeValue(name, null);
    }
}

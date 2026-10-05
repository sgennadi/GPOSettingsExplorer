using System.Globalization;
using System.Xml.Linq;
using GPOSettingsExplorer.Models;

namespace GPOSettingsExplorer.Services;

public sealed class GppIniFileService
{
    private const string RootClsid =
        "{694C651A-08F2-47FA-A427-34C4F62BA207}";

    private const string ItemClsid =
        "{EEFACE84-D3D8-4680-8D4B-BF103E759448}";

    private readonly GppDocumentService _documents;

    public GppIniFileService(
        GppDocumentService documents)
    {
        _documents = documents;
    }

    public IReadOnlyList<GppIniFileItemInfo> Load(
        IEnumerable<GpoInfo> gpos,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var result =
            new List<GppIniFileItemInfo>();

        var list =
            gpos.ToList();

        var type =
            GetIniType();

        for (var index = 0;
             index < list.Count;
             index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var gpo =
                list[index];

            progress?.Report(
                $"INI Files {index + 1}/{list.Count}: {gpo.DisplayName}");

            foreach (var scope in new[]
                     {
                         "Computer",
                         "User"
                     })
            {
                var target =
                    _documents.BuildTarget(
                        gpo,
                        scope,
                        type);

                if (!File.Exists(
                        target.XmlPath))
                    continue;

                try
                {
                    var document =
                        XDocument.Load(
                            target.XmlPath,
                            LoadOptions.PreserveWhitespace);

                    var ordinal =
                        0;

                    foreach (var element
                             in document
                                 .Descendants()
                                 .Where(
                                     child =>
                                         child.Name.LocalName ==
                                         "Ini"))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        ordinal++;

                        var properties =
                            element.Elements()
                                .FirstOrDefault(
                                    child =>
                                        child.Name.LocalName ==
                                        "Properties");

                        if (properties is null)
                            continue;

                        var filters =
                            element.Elements()
                                .FirstOrDefault(
                                    child =>
                                        child.Name.LocalName ==
                                        "Filters");

                        var path =
                            Attr(
                                properties,
                                "path");

                        var section =
                            Attr(
                                properties,
                                "section");

                        var property =
                            Attr(
                                properties,
                                "property");

                        result.Add(
                            new GppIniFileItemInfo
                            {
                                GpoId =
                                    gpo.Id,
                                GpoName =
                                    gpo.DisplayName,
                                DomainName =
                                    gpo.DomainName,
                                Scope =
                                    scope,
                                XmlPath =
                                    target.XmlPath,
                                Uid =
                                    Attr(
                                        element,
                                        "uid"),
                                Ordinal =
                                    ordinal,
                                DisplayName =
                                    FirstNonEmpty(
                                        Attr(
                                            element,
                                            "name"),
                                        Attr(
                                            element,
                                            "status"),
                                        property,
                                        section,
                                        path,
                                        "INI File"),
                                Description =
                                    FirstNonEmpty(
                                        Attr(
                                            element,
                                            "desc"),
                                        Attr(
                                            element,
                                            "descr")),
                                Action =
                                    FirstNonEmpty(
                                        Attr(
                                            properties,
                                            "action"),
                                        "U"),
                                Path =
                                    path,
                                Section =
                                    section,
                                Property =
                                    property,
                                Value =
                                    Attr(
                                        properties,
                                        "value"),
                                Disabled =
                                    IsTrue(
                                        Attr(
                                            element,
                                            "disabled")),
                                BypassErrors =
                                    IsTrue(
                                        Attr(
                                            element,
                                            "bypassErrors")),
                                RemoveWhenNoLongerApplied =
                                    IsTrue(
                                        Attr(
                                            element,
                                            "removePolicy")),
                                RunInUserContext =
                                    IsTrue(
                                        Attr(
                                            element,
                                            "userContext")),
                                FiltersXml =
                                    filters?.ToString(
                                        SaveOptions.DisableFormatting)
                                    ?? string.Empty
                            });
                    }
                }
                catch
                {
                    // Malformed IniFiles.xml stays available in the raw GPP XML editor.
                }
            }
        }

        return result
            .OrderBy(
                item =>
                    item.GpoName,
                StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(
                item =>
                    item.Scope,
                StringComparer.OrdinalIgnoreCase)
            .ThenBy(
                item =>
                    item.Path,
                StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(
                item =>
                    item.Section,
                StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(
                item =>
                    item.Property,
                StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(
                item =>
                    item.Ordinal)
            .ToArray();
    }

    public GppIniFileItemInfo CreateNew(
        GpoInfo gpo,
        string scope)
    {
        var normalizedScope =
            NormalizeScope(
                scope);

        return new GppIniFileItemInfo
        {
            GpoId =
                gpo.Id,
            GpoName =
                gpo.DisplayName,
            DomainName =
                gpo.DomainName,
            Scope =
                normalizedScope,
            XmlPath =
                _documents.BuildTarget(
                    gpo,
                    normalizedScope,
                    GetIniType()).XmlPath,
            Uid =
                Guid.NewGuid()
                    .ToString("B")
                    .ToUpperInvariant(),
            DisplayName =
                "New INI File Preference",
            Action =
                "U"
        };
    }

    public void Save(
        GpoInfo gpo,
        string domainDistinguishedName,
        GppIniFileItemInfo item)
    {
        Validate(
            item);

        var target =
            _documents.BuildTarget(
                gpo,
                item.Scope,
                GetIniType());

        XDocument document;

        if (File.Exists(
                target.XmlPath))
        {
            document =
                XDocument.Load(
                    target.XmlPath,
                    LoadOptions.PreserveWhitespace);

            if (document.Root is null ||
                !document.Root.Name.LocalName.Equals(
                    "IniFiles",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "The existing IniFiles.xml root element is not <IniFiles>. " +
                    "Use the raw GPP XML editor to repair it.");
            }
        }
        else
        {
            document =
                new XDocument(
                    new XDeclaration(
                        "1.0",
                        "utf-8",
                        null),
                    new XElement(
                        "IniFiles",
                        new XAttribute(
                            "clsid",
                            RootClsid)));
        }

        var items =
            document
                .Descendants()
                .Where(
                    element =>
                        element.Name.LocalName ==
                        "Ini")
                .ToArray();

        var selected =
            FindItem(
                items,
                item);

        if (selected is null)
        {
            selected =
                new XElement(
                    "Ini",
                    new XAttribute(
                        "clsid",
                        ItemClsid));

            document.Root!.Add(
                selected);
        }

        UpdateItem(
            selected,
            item);

        _documents.SaveXml(
            gpo,
            domainDistinguishedName,
            target,
            document.ToString());
    }

    public void Delete(
        GpoInfo gpo,
        string domainDistinguishedName,
        GppIniFileItemInfo item)
    {
        var target =
            _documents.BuildTarget(
                gpo,
                item.Scope,
                GetIniType());

        if (!File.Exists(
                target.XmlPath))
            return;

        var document =
            XDocument.Load(
                target.XmlPath,
                LoadOptions.PreserveWhitespace);

        var items =
            document
                .Descendants()
                .Where(
                    element =>
                        element.Name.LocalName ==
                        "Ini")
                .ToArray();

        var selected =
            FindItem(
                items,
                item)
            ?? throw new InvalidOperationException(
                "The selected INI File preference no longer exists. Refresh the list.");

        selected.Remove();

        if (!document
                .Descendants()
                .Any(
                    element =>
                        element.Name.LocalName ==
                        "Ini"))
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

    public void CopyEditableValues(
        GppIniFileItemInfo source,
        GppIniFileItemInfo target)
    {
        target.DisplayName =
            source.DisplayName;
        target.Description =
            source.Description;
        target.Action =
            source.Action;
        target.Path =
            source.Path;
        target.Section =
            source.Section;
        target.Property =
            source.Property;
        target.Value =
            source.Value;
        target.Disabled =
            source.Disabled;
        target.BypassErrors =
            source.BypassErrors;
        target.RemoveWhenNoLongerApplied =
            source.RemoveWhenNoLongerApplied;
        target.RunInUserContext =
            source.RunInUserContext;
        target.FiltersXml =
            source.FiltersXml;
    }

    private static void UpdateItem(
        XElement element,
        GppIniFileItemInfo item)
    {
        var display =
            string.IsNullOrWhiteSpace(
                item.DisplayName)
                ? FirstNonEmpty(
                    item.Property,
                    item.Section,
                    item.Path,
                    "INI File")
                : item.DisplayName.Trim();

        SetAttr(
            element,
            "clsid",
            ItemClsid);

        SetAttr(
            element,
            "name",
            display);

        SetAttr(
            element,
            "status",
            display);

        SetAttr(
            element,
            "image",
            "2");

        SetAttr(
            element,
            "changed",
            DateTime.UtcNow.ToString(
                "yyyy-MM-dd HH:mm:ss",
                CultureInfo.InvariantCulture));

        if (string.IsNullOrWhiteSpace(
                item.Uid))
        {
            item.Uid =
                Guid.NewGuid()
                    .ToString("B")
                    .ToUpperInvariant();
        }

        SetAttr(
            element,
            "uid",
            item.Uid);

        if (string.IsNullOrWhiteSpace(
                item.Description))
        {
            element.SetAttributeValue(
                "desc",
                null);
        }
        else
        {
            SetAttr(
                element,
                "desc",
                item.Description);
        }

        SetOptionalBool(
            element,
            "disabled",
            item.Disabled);

        SetOptionalBool(
            element,
            "bypassErrors",
            item.BypassErrors);

        SetOptionalBool(
            element,
            "removePolicy",
            item.RemoveWhenNoLongerApplied);

        SetOptionalBool(
            element,
            "userContext",
            item.RunInUserContext);

        var properties =
            element.Elements()
                .FirstOrDefault(
                    child =>
                        child.Name.LocalName ==
                        "Properties");

        if (properties is null)
        {
            properties =
                new XElement(
                    "Properties");

            element.AddFirst(
                properties);
        }

        SetAttr(
            properties,
            "action",
            NormalizeAction(
                item.Action));

        SetAttr(
            properties,
            "path",
            item.Path.Trim());

        SetAttr(
            properties,
            "section",
            item.Section.Trim());

        SetAttr(
            properties,
            "property",
            item.Property.Trim());

        SetAttr(
            properties,
            "value",
            item.Value ?? string.Empty);

        ApplyFilters(
            element,
            item.FiltersXml);
    }

    private static XElement? FindItem(
        IReadOnlyList<XElement> items,
        GppIniFileItemInfo item)
    {
        if (!string.IsNullOrWhiteSpace(
                item.Uid))
        {
            var byUid =
                items.FirstOrDefault(
                    element =>
                        Attr(
                            element,
                            "uid").Equals(
                            item.Uid,
                            StringComparison.OrdinalIgnoreCase));

            if (byUid is not null)
                return byUid;
        }

        if (item.Ordinal > 0 &&
            item.Ordinal <= items.Count)
        {
            return items[
                item.Ordinal - 1];
        }

        return null;
    }

    private static void Validate(
        GppIniFileItemInfo item)
    {
        if (string.IsNullOrWhiteSpace(
                item.Path))
        {
            throw new InvalidOperationException(
                "INI/INF file path cannot be empty.");
        }

        var action =
            NormalizeAction(
                item.Action);

        if (action != "D")
        {
            if (string.IsNullOrWhiteSpace(
                    item.Section))
            {
                throw new InvalidOperationException(
                    "Section name is required for Create, Replace, or Update.");
            }

            if (string.IsNullOrWhiteSpace(
                    item.Property))
            {
                throw new InvalidOperationException(
                    "Property name is required for Create, Replace, or Update.");
            }
        }

        if (!string.IsNullOrWhiteSpace(
                item.FiltersXml))
        {
            var filters =
                XElement.Parse(
                    item.FiltersXml);

            if (!filters.Name.LocalName.Equals(
                    "Filters",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "Item-level targeting XML must have a <Filters> root element.");
            }
        }
    }

    private static void ApplyFilters(
        XElement item,
        string filtersXml)
    {
        var existing =
            item.Elements()
                .FirstOrDefault(
                    element =>
                        element.Name.LocalName ==
                        "Filters");

        if (string.IsNullOrWhiteSpace(
                filtersXml))
        {
            existing?.Remove();
            return;
        }

        XElement filters;

        try
        {
            filters =
                XElement.Parse(
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

        if (existing is null)
            item.AddFirst(filters);
        else
            existing.ReplaceWith(filters);
    }

    private GppDocumentTypeInfo GetIniType() =>
        _documents.GetKnownTypes().First(
            type =>
                type.Name.Equals(
                    "INI Files",
                    StringComparison.OrdinalIgnoreCase));

    private static string NormalizeScope(
        string scope) =>
        scope.Equals(
            "User",
            StringComparison.OrdinalIgnoreCase)
            ? "User"
            : "Computer";

    private static string NormalizeAction(
        string action) =>
        action.Trim()
            .ToUpperInvariant() switch
        {
            "C" or "CREATE" => "C",
            "D" or "DELETE" => "D",
            "R" or "REPLACE" => "R",
            _ => "U"
        };

    private static string Attr(
        XElement element,
        string name) =>
        element.Attributes()
            .FirstOrDefault(
                attribute =>
                    attribute.Name.LocalName.Equals(
                        name,
                        StringComparison.OrdinalIgnoreCase))?
            .Value
        ?? string.Empty;

    private static bool IsTrue(
        string value) =>
        value == "1" ||
        value.Equals(
            "true",
            StringComparison.OrdinalIgnoreCase);

    private static string FirstNonEmpty(
        params string[] values) =>
        values.FirstOrDefault(
            value =>
                !string.IsNullOrWhiteSpace(
                    value))
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
        {
            element.SetAttributeValue(
                name,
                "1");
        }
        else
        {
            element.SetAttributeValue(
                name,
                null);
        }
    }
}

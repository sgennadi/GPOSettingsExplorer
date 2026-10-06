using System.Globalization;
using System.Xml.Linq;
using GPOSettingsExplorer.Models;

namespace GPOSettingsExplorer.Services;

public sealed class GppFileAssociationService
{
    private const string RootClsid =
        "{8AB5F5D7-F676-48AB-A94E-1186E120EFDC}";
    private const string OpenWithClsid =
        "{100B9C09-906A-4F5A-9C41-1BD98B6CA022}";
    private const string FileTypeClsid =
        "{580C4D3B-7A89-44D0-92D2-C105702C7BD0}";

    private readonly GppDocumentService _documents;

    public GppFileAssociationService(GppDocumentService documents)
    {
        _documents = documents;
    }

    public IReadOnlyList<GppFileAssociationItemInfo> Load(
        IEnumerable<GpoInfo> gpos,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var result = new List<GppFileAssociationItemInfo>();
        var list = gpos.ToList();
        var type = GetTypeInfo();

        for (var index = 0; index < list.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var gpo = list[index];

            progress?.Report(
                $"File Associations {index + 1}/{list.Count}: {gpo.DisplayName}");

            foreach (var scope in new[] { "User", "Computer" })
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
                    foreach (var element in document.Root?.Elements()
                                 ?? Enumerable.Empty<XElement>())
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        if (element.Name.LocalName is not ("OpenWith" or "FileType"))
                            continue;

                        ordinal++;
                        result.Add(ReadItem(
                            gpo,
                            scope,
                            target.XmlPath,
                            ordinal,
                            element));
                    }
                }
                catch
                {
                    // Damaged FolderOptions.xml remains editable via raw GPP XML mode.
                }
            }
        }

        return result
            .OrderBy(item => item.GpoName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(item => item.Scope, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.KindDisplay, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(item => item.FileExtension, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    public GppFileAssociationItemInfo CreateNew(
        GpoInfo gpo,
        string kind)
    {
        var isFileType = kind.Equals(
            "FileType",
            StringComparison.OrdinalIgnoreCase);

        var scope = isFileType ? "Computer" : "User";

        return new GppFileAssociationItemInfo
        {
            GpoId = gpo.Id,
            GpoName = gpo.DisplayName,
            DomainName = gpo.DomainName,
            Scope = scope,
            XmlPath = _documents.BuildTarget(
                gpo,
                scope,
                GetTypeInfo()).XmlPath,
            Uid = Guid.NewGuid().ToString("B").ToUpperInvariant(),
            ItemKind = isFileType ? "FileType" : "OpenWith",
            DisplayName = isFileType ? "New File Type" : "New Open With",
            Action = "U",
            RunInUserContext = !isFileType
        };
    }

    public void Save(
        GpoInfo gpo,
        string domainDistinguishedName,
        GppFileAssociationItemInfo item)
    {
        Validate(item);

        var target = _documents.BuildTarget(
            gpo,
            item.Scope,
            GetTypeInfo());

        XDocument document;

        if (File.Exists(target.XmlPath))
        {
            document = XDocument.Load(
                target.XmlPath,
                LoadOptions.PreserveWhitespace);

            if (document.Root is null ||
                !document.Root.Name.LocalName.Equals(
                    "FolderOptions",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "The existing FolderOptions.xml root is not <FolderOptions>. Use raw XML editing to repair it.");
            }
        }
        else
        {
            document = new XDocument(
                new XDeclaration("1.0", "utf-8", null),
                new XElement(
                    "FolderOptions",
                    new XAttribute("clsid", RootClsid)));
        }

        var element = FindItem(document, item);

        if (element is null)
        {
            element = new XElement(
                item.ItemKind,
                new XAttribute(
                    "clsid",
                    item.IsOpenWith ? OpenWithClsid : FileTypeClsid));

            document.Root!.Add(element);
        }

        UpdateItem(element, item);

        _documents.SaveXml(
            gpo,
            domainDistinguishedName,
            target,
            document.ToString());
    }

    public void Delete(
        GpoInfo gpo,
        string domainDistinguishedName,
        GppFileAssociationItemInfo item)
    {
        var target = _documents.BuildTarget(
            gpo,
            item.Scope,
            GetTypeInfo());

        if (!File.Exists(target.XmlPath))
            return;

        var document = XDocument.Load(
            target.XmlPath,
            LoadOptions.PreserveWhitespace);

        var element = FindItem(document, item)
            ?? throw new InvalidOperationException(
                "The selected file-association preference no longer exists. Refresh the list.");

        element.Remove();

        if (!(document.Root?.Elements().Any() ?? false))
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
        GppFileAssociationItemInfo source,
        GppFileAssociationItemInfo target)
    {
        target.ItemKind = source.ItemKind;
        target.DisplayName = source.DisplayName;
        target.Description = source.Description;
        target.Action = source.Action;
        target.FileExtension = source.FileExtension;
        target.ApplicationPath = source.ApplicationPath;
        target.DefaultApplication = source.DefaultApplication;
        target.Application = source.Application;
        target.ApplicationProgId = source.ApplicationProgId;
        target.ConfigureActions = source.ConfigureActions;
        target.Disabled = source.Disabled;
        target.BypassErrors = source.BypassErrors;
        target.RemoveWhenNoLongerApplied = source.RemoveWhenNoLongerApplied;
        target.RunInUserContext = source.RunInUserContext;
        target.FiltersXml = source.FiltersXml;
    }

    private static GppFileAssociationItemInfo ReadItem(
        GpoInfo gpo,
        string scope,
        string xmlPath,
        int ordinal,
        XElement element)
    {
        var properties = element.Elements()
            .FirstOrDefault(child => child.Name.LocalName == "Properties");

        var filters = element.Elements()
            .FirstOrDefault(child => child.Name.LocalName == "Filters");

        var kind = element.Name.LocalName;

        return new GppFileAssociationItemInfo
        {
            GpoId = gpo.Id,
            GpoName = gpo.DisplayName,
            DomainName = gpo.DomainName,
            Scope = scope,
            XmlPath = xmlPath,
            Uid = Attr(element, "uid"),
            Ordinal = ordinal,
            ItemKind = kind,
            DisplayName = FirstNonEmpty(
                Attr(element, "name"),
                Attr(properties, kind == "OpenWith" ? "fileExtension" : "fileExt"),
                kind),
            Description = Attr(element, "desc"),
            Action = FirstNonEmpty(Attr(properties, "action"), "U"),
            FileExtension = FirstNonEmpty(
                Attr(properties, "fileExtension"),
                Attr(properties, "fileExt")),
            ApplicationPath = Attr(properties, "applicationPath"),
            DefaultApplication = IsTrue(Attr(properties, "default")),
            Application = Attr(properties, "application"),
            ApplicationProgId = Attr(properties, "appProgID"),
            ConfigureActions = IsTrue(Attr(properties, "configActions")),
            Disabled = IsTrue(Attr(element, "disabled")),
            BypassErrors = IsTrue(Attr(element, "bypassErrors")),
            RemoveWhenNoLongerApplied = IsTrue(Attr(element, "removePolicy")),
            RunInUserContext = IsTrue(Attr(element, "userContext")),
            FiltersXml = filters?.ToString(SaveOptions.DisableFormatting)
                ?? string.Empty
        };
    }

    private static void UpdateItem(
        XElement element,
        GppFileAssociationItemInfo item)
    {
        SetAttr(
            element,
            "clsid",
            item.IsOpenWith ? OpenWithClsid : FileTypeClsid);

        SetAttr(
            element,
            "name",
            FirstNonEmpty(
                item.DisplayName.Trim(),
                NormalizeExtension(item.FileExtension)));

        SetAttr(element, "image", "2");
        SetAttr(
            element,
            "changed",
            DateTime.UtcNow.ToString(
                "yyyy-MM-dd HH:mm:ss",
                CultureInfo.InvariantCulture));

        if (string.IsNullOrWhiteSpace(item.Uid))
            item.Uid = Guid.NewGuid().ToString("B").ToUpperInvariant();

        SetAttr(element, "uid", item.Uid);
        SetOptionalAttr(element, "desc", item.Description);
        SetOptionalBool(element, "disabled", item.Disabled);
        SetOptionalBool(element, "bypassErrors", item.BypassErrors);
        SetOptionalBool(element, "removePolicy", item.RemoveWhenNoLongerApplied);
        SetOptionalBool(element, "userContext", item.RunInUserContext);

        var properties = element.Elements()
            .FirstOrDefault(child => child.Name.LocalName == "Properties");

        if (properties is null)
        {
            properties = new XElement("Properties");
            element.AddFirst(properties);
        }

        SetAttr(properties, "action", item.Action.ToUpperInvariant());

        if (item.IsOpenWith)
        {
            SetAttr(properties, "fileExtension", NormalizeExtension(item.FileExtension));
            SetAttr(properties, "applicationPath", item.ApplicationPath.Trim());
            SetAttr(properties, "default", item.DefaultApplication ? "1" : "0");

            properties.SetAttributeValue("fileExt", null);
            properties.SetAttributeValue("application", null);
            properties.SetAttributeValue("appProgID", null);
            properties.SetAttributeValue("configActions", null);
        }
        else
        {
            SetAttr(properties, "fileExt", NormalizeExtension(item.FileExtension));
            SetOptionalAttr(properties, "application", item.Application);
            SetOptionalAttr(properties, "appProgID", item.ApplicationProgId);
            SetAttr(properties, "configActions", item.ConfigureActions ? "1" : "0");

            properties.SetAttributeValue("fileExtension", null);
            properties.SetAttributeValue("applicationPath", null);
            properties.SetAttributeValue("default", null);
        }

        var oldFilters = element.Elements()
            .FirstOrDefault(child => child.Name.LocalName == "Filters");
        oldFilters?.Remove();

        if (!string.IsNullOrWhiteSpace(item.FiltersXml))
        {
            var filters = XElement.Parse(item.FiltersXml);
            if (!filters.Name.LocalName.Equals(
                    "Filters",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "Item-level targeting root element must be <Filters>.");
            }

            element.Add(filters);
        }
    }

    private static XElement? FindItem(
        XDocument document,
        GppFileAssociationItemInfo item)
    {
        var candidates = document.Root?.Elements()
            .Where(element =>
                element.Name.LocalName is "OpenWith" or "FileType")
            .ToArray()
            ?? Array.Empty<XElement>();

        if (!string.IsNullOrWhiteSpace(item.Uid))
        {
            var byUid = candidates.FirstOrDefault(element =>
                Attr(element, "uid").Equals(
                    item.Uid,
                    StringComparison.OrdinalIgnoreCase));

            if (byUid is not null)
                return byUid;
        }

        var sameKind = candidates
            .Where(element =>
                element.Name.LocalName.Equals(
                    item.ItemKind,
                    StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (item.Ordinal > 0 && item.Ordinal <= candidates.Length)
            return candidates[item.Ordinal - 1];

        var extension = NormalizeExtension(item.FileExtension);

        return sameKind.FirstOrDefault(element =>
        {
            var properties = element.Elements()
                .FirstOrDefault(child => child.Name.LocalName == "Properties");

            var current = FirstNonEmpty(
                Attr(properties, "fileExtension"),
                Attr(properties, "fileExt"));

            return NormalizeExtension(current).Equals(
                extension,
                StringComparison.OrdinalIgnoreCase);
        });
    }

    private static void Validate(GppFileAssociationItemInfo item)
    {
        if (item.IsOpenWith &&
            !item.Scope.Equals("User", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Open With preferences are user-policy settings.");
        }

        if (item.IsFileType &&
            !item.Scope.Equals("Computer", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "File Type preferences are computer-policy settings.");
        }

        if (string.IsNullOrWhiteSpace(NormalizeExtension(item.FileExtension)))
            throw new InvalidOperationException("File extension cannot be empty.");

        if (item.Action.ToUpperInvariant() is not ("C" or "U" or "R" or "D"))
            throw new InvalidOperationException("Invalid file-association action.");

        if (item.IsOpenWith &&
            !item.Action.Equals("D", StringComparison.OrdinalIgnoreCase) &&
            string.IsNullOrWhiteSpace(item.ApplicationPath))
        {
            throw new InvalidOperationException(
                "Application path cannot be empty unless the action is Delete.");
        }

        if (item.IsFileType &&
            !item.Action.Equals("D", StringComparison.OrdinalIgnoreCase) &&
            string.IsNullOrWhiteSpace(item.Application) &&
            string.IsNullOrWhiteSpace(item.ApplicationProgId))
        {
            throw new InvalidOperationException(
                "Specify an application name or ProgID unless the action is Delete.");
        }
    }

    private GppDocumentTypeInfo GetTypeInfo() =>
        _documents.GetKnownTypes().First(type =>
            type.Name.Equals(
                "Folder Options",
                StringComparison.OrdinalIgnoreCase));

    private static string NormalizeExtension(string value) =>
        value.Trim().TrimStart('.');

    private static string Attr(XElement? element, string name) =>
        element?.Attributes()
            .FirstOrDefault(attribute =>
                attribute.Name.LocalName.Equals(
                    name,
                    StringComparison.OrdinalIgnoreCase))?
            .Value.Trim() ?? string.Empty;

    private static string FirstNonEmpty(params string[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))
        ?? string.Empty;

    private static bool IsTrue(string value) =>
        value.Equals("1", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("yes", StringComparison.OrdinalIgnoreCase);

    private static void SetAttr(
        XElement element,
        string name,
        string value) =>
        element.SetAttributeValue(name, value);

    private static void SetOptionalAttr(
        XElement element,
        string name,
        string value) =>
        element.SetAttributeValue(
            name,
            string.IsNullOrWhiteSpace(value) ? null : value.Trim());

    private static void SetOptionalBool(
        XElement element,
        string name,
        bool value) =>
        element.SetAttributeValue(name, value ? "1" : null);
}

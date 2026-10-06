using System.Globalization;
using System.Xml.Linq;
using GPOSettingsExplorer.Models;

namespace GPOSettingsExplorer.Services;

public sealed class GppFolderOptionsService
{
    private const string RootClsid =
        "{8AB5F5D7-F676-48AB-A94E-1186E120EFDC}";
    private const string VistaClsid =
        "{DBF1E3CD-4CA2-407C-BE84-5F67D3BE754D}";

    private readonly GppDocumentService _documents;

    public GppFolderOptionsService(GppDocumentService documents)
    {
        _documents = documents;
    }

    public IReadOnlyList<GppFolderOptionsItemInfo> Load(
        IEnumerable<GpoInfo> gpos,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var result = new List<GppFolderOptionsItemInfo>();
        var list = gpos.ToList();
        var type = GetTypeInfo();

        for (var index = 0; index < list.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var gpo = list[index];
            progress?.Report(
                $"Folder Options {index + 1}/{list.Count}: {gpo.DisplayName}");

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
                        if (element.Name.LocalName
                            is not ("GlobalFolderOptionsVista" or "GlobalFolderOptions" or "OpenWith" or "FileType"))
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
                    // Damaged FolderOptions.xml remains accessible in raw GPP XML mode.
                }
            }
        }

        return result
            .OrderBy(item => item.GpoName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(item => item.Scope, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.KindDisplay, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(item => item.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    public GppFolderOptionsItemInfo CreateNew(GpoInfo gpo)
    {
        return new GppFolderOptionsItemInfo
        {
            GpoId = gpo.Id,
            GpoName = gpo.DisplayName,
            DomainName = gpo.DomainName,
            Scope = "User",
            XmlPath = _documents.BuildTarget(
                gpo,
                "User",
                GetTypeInfo()).XmlPath,
            Uid = Guid.NewGuid().ToString("B").ToUpperInvariant()
        };
    }

    public void Save(
        GpoInfo gpo,
        string domainDistinguishedName,
        GppFolderOptionsItemInfo item)
    {
        if (!item.SupportsStructuredEditing)
            throw new InvalidOperationException(
                "Only Vista-and-later global Folder Options are written by the structured editor. Use raw XML for legacy/file-association items.");

        if (!item.Scope.Equals("User", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "Global Folder Options are user-policy preferences.");

        var target = _documents.BuildTarget(
            gpo,
            "User",
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
                "GlobalFolderOptionsVista",
                new XAttribute("clsid", VistaClsid));
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
        GppFolderOptionsItemInfo item)
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
                "The selected Folder Options item no longer exists. Refresh the list.");

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
        GppFolderOptionsItemInfo source,
        GppFolderOptionsItemInfo target)
    {
        target.DisplayName = source.DisplayName;
        target.Description = source.Description;
        target.ShowDriveLetter = source.ShowDriveLetter;
        target.ShowPreviewHandlers = source.ShowPreviewHandlers;
        target.UseCheckBoxes = source.UseCheckBoxes;
        target.UseSharingWizard = source.UseSharingWizard;
        target.AlwaysShowIcons = source.AlwaysShowIcons;
        target.AlwaysShowMenus = source.AlwaysShowMenus;
        target.HiddenFiles = source.HiddenFiles;
        target.DisplayIconThumb = source.DisplayIconThumb;
        target.DisplayFileSize = source.DisplayFileSize;
        target.HideFileExtensions = source.HideFileExtensions;
        target.DisplaySimpleFolders = source.DisplaySimpleFolders;
        target.ListViewTyping = source.ListViewTyping;
        target.SeparateProcess = source.SeparateProcess;
        target.ShowSuperHidden = source.ShowSuperHidden;
        target.ClassicViewState = source.ClassicViewState;
        target.PersistBrowsers = source.PersistBrowsers;
        target.ShowCompressedColor = source.ShowCompressedColor;
        target.ShowInfoTips = source.ShowInfoTips;
        target.FullPath = source.FullPath;
        target.Disabled = source.Disabled;
        target.BypassErrors = source.BypassErrors;
        target.RemoveWhenNoLongerApplied = source.RemoveWhenNoLongerApplied;
        target.RunInUserContext = source.RunInUserContext;
        target.FiltersXml = source.FiltersXml;
    }

    private static GppFolderOptionsItemInfo ReadItem(
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

        return new GppFolderOptionsItemInfo
        {
            GpoId = gpo.Id,
            GpoName = gpo.DisplayName,
            DomainName = gpo.DomainName,
            Scope = scope,
            XmlPath = xmlPath,
            Uid = Attr(element, "uid"),
            Ordinal = ordinal,
            ItemKind = element.Name.LocalName,
            DisplayName = FirstNonEmpty(
                Attr(element, "name"),
                element.Name.LocalName),
            Description = Attr(element, "desc"),
            ShowDriveLetter = BoolAttr(properties, "showDriveLetter", true),
            ShowPreviewHandlers = BoolAttr(properties, "showPreviewHandlers", true),
            UseCheckBoxes = BoolAttr(properties, "useCheckBoxes", false),
            UseSharingWizard = BoolAttr(properties, "useSharingWizard", true),
            AlwaysShowIcons = BoolAttr(properties, "alwaysShowIcons", false),
            AlwaysShowMenus = BoolAttr(properties, "alwaysShowMenus", false),
            HiddenFiles = FirstNonEmpty(Attr(properties, "hidden"), "HIDE"),
            DisplayIconThumb = BoolAttr(properties, "displayIconThumb", true),
            DisplayFileSize = BoolAttr(properties, "displayFileSize", true),
            HideFileExtensions = BoolAttr(properties, "hideFileExt", true),
            DisplaySimpleFolders = BoolAttr(properties, "displaySimpleFolders", true),
            ListViewTyping = FirstNonEmpty(Attr(properties, "listViewTyping"), "SELECT"),
            SeparateProcess = BoolAttr(properties, "separateProcess", false),
            ShowSuperHidden = BoolAttr(properties, "showSuperHidden", false),
            ClassicViewState = BoolAttr(properties, "classicViewState", false),
            PersistBrowsers = BoolAttr(properties, "persistBrowsers", false),
            ShowCompressedColor = BoolAttr(properties, "showCompColor", true),
            ShowInfoTips = BoolAttr(properties, "showInfoTip", true),
            FullPath = BoolAttr(properties, "fullPath", false),
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
        GppFolderOptionsItemInfo item)
    {
        SetAttr(element, "clsid", VistaClsid);
        SetAttr(element, "name", item.DisplayName.Trim());
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

        SetBool(properties, "showDriveLetter", item.ShowDriveLetter);
        SetBool(properties, "showPreviewHandlers", item.ShowPreviewHandlers);
        SetBool(properties, "useCheckBoxes", item.UseCheckBoxes);
        SetBool(properties, "useSharingWizard", item.UseSharingWizard);
        SetBool(properties, "alwaysShowIcons", item.AlwaysShowIcons);
        SetBool(properties, "alwaysShowMenus", item.AlwaysShowMenus);
        SetAttr(properties, "hidden",
            item.HiddenFiles.Equals("SHOW", StringComparison.OrdinalIgnoreCase)
                ? "SHOW"
                : "HIDE");
        SetBool(properties, "displayIconThumb", item.DisplayIconThumb);
        SetBool(properties, "displayFileSize", item.DisplayFileSize);
        SetBool(properties, "hideFileExt", item.HideFileExtensions);
        SetBool(properties, "displaySimpleFolders", item.DisplaySimpleFolders);
        SetAttr(properties, "listViewTyping",
            item.ListViewTyping.Equals("AUTO", StringComparison.OrdinalIgnoreCase)
                ? "AUTO"
                : "SELECT");
        SetBool(properties, "separateProcess", item.SeparateProcess);
        SetBool(properties, "showSuperHidden", item.ShowSuperHidden);
        SetBool(properties, "classicViewState", item.ClassicViewState);
        SetBool(properties, "persistBrowsers", item.PersistBrowsers);
        SetBool(properties, "showCompColor", item.ShowCompressedColor);
        SetBool(properties, "showInfoTip", item.ShowInfoTips);
        SetBool(properties, "fullPath", item.FullPath);

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
        GppFolderOptionsItemInfo item)
    {
        var candidates = document.Root?.Elements().ToArray()
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

        if (item.Ordinal > 0 && item.Ordinal <= candidates.Length)
            return candidates[item.Ordinal - 1];

        return candidates.FirstOrDefault(element =>
            element.Name.LocalName.Equals(
                item.ItemKind,
                StringComparison.OrdinalIgnoreCase) &&
            Attr(element, "name").Equals(
                item.DisplayName,
                StringComparison.CurrentCultureIgnoreCase));
    }

    private GppDocumentTypeInfo GetTypeInfo() =>
        _documents.GetKnownTypes().First(type =>
            type.Name.Equals(
                "Folder Options",
                StringComparison.OrdinalIgnoreCase));

    private static string Attr(XElement? element, string name) =>
        element?.Attributes()
            .FirstOrDefault(attribute =>
                attribute.Name.LocalName.Equals(
                    name,
                    StringComparison.OrdinalIgnoreCase))?
            .Value.Trim() ?? string.Empty;

    private static bool BoolAttr(
        XElement? element,
        string name,
        bool defaultValue)
    {
        var value = Attr(element, name);
        return string.IsNullOrWhiteSpace(value)
            ? defaultValue
            : IsTrue(value);
    }

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

    private static void SetBool(
        XElement element,
        string name,
        bool value) =>
        element.SetAttributeValue(name, value ? "1" : "0");

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

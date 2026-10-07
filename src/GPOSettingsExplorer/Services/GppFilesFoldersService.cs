using System.Globalization;
using System.Xml.Linq;
using GPOSettingsExplorer.Models;

namespace GPOSettingsExplorer.Services;

public sealed class GppFilesFoldersService
{
    private const string FilesRootClsid = "{215B2E53-57CE-475C-80FE-9EEC14635851}";
    private const string FileItemClsid = "{50BE44C8-567A-4ED1-B1D0-9234FE1F38AF}";
    private const string FoldersRootClsid = "{77CC39E7-3D16-4F8F-AF86-EC0BBEE2C861}";
    private const string FolderItemClsid = "{07DA02F5-F9CD-4397-A550-4AE21B6B4BD3}";

    private readonly GppDocumentService _documents;

    public GppFilesFoldersService(GppDocumentService documents)
    {
        _documents = documents;
    }

    public IReadOnlyList<GppFileItemInfo> LoadFiles(
        IEnumerable<GpoInfo> gpos,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var result = new List<GppFileItemInfo>();
        var list = gpos.ToList();
        var type = GetTypeInfo("Files");

        for (var index = 0; index < list.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var gpo = list[index];
            progress?.Report($"Files {index + 1}/{list.Count}: {gpo.DisplayName}");

            foreach (var scope in new[] { "Computer", "User" })
            {
                var target = _documents.BuildTarget(gpo, scope, type);
                if (!File.Exists(target.XmlPath))
                    continue;

                try
                {
                    var document = GppXmlCacheService.Load(
                        target.XmlPath,
                        LoadOptions.PreserveWhitespace);

                    var ordinal = 0;

                    foreach (var file in document
                                 .Descendants()
                                 .Where(element => element.Name.LocalName == "File"))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        ordinal++;

                        var properties = file.Elements()
                            .FirstOrDefault(element => element.Name.LocalName == "Properties");

                        if (properties is null)
                            continue;

                        var filters = file.Elements()
                            .FirstOrDefault(element => element.Name.LocalName == "Filters");

                        var targetPath = Attr(properties, "targetPath");

                        result.Add(new GppFileItemInfo
                        {
                            GpoId = gpo.Id,
                            GpoName = gpo.DisplayName,
                            DomainName = gpo.DomainName,
                            Scope = scope,
                            XmlPath = target.XmlPath,
                            Uid = Attr(file, "uid"),
                            Ordinal = ordinal,
                            DisplayName = FirstNonEmpty(
                                Attr(file, "name"),
                                Attr(file, "status"),
                                targetPath),
                            Action = FirstNonEmpty(Attr(properties, "action"), "U"),
                            FromPath = Attr(properties, "fromPath"),
                            TargetPath = targetPath,
                            SuppressErrors = IsTrue(Attr(properties, "suppress")),
                            ReadOnly = IsTrue(Attr(properties, "readOnly")),
                            Archive = IsTrue(Attr(properties, "archive")),
                            Hidden = IsTrue(Attr(properties, "hidden")),
                            Disabled = IsTrue(Attr(file, "disabled")),
                            BypassErrors = IsTrue(Attr(file, "bypassErrors")),
                            RemoveWhenNoLongerApplied = IsTrue(Attr(file, "removePolicy")),
                            RunInUserContext = IsTrue(Attr(file, "userContext")),
                            FiltersXml = filters?.ToString(SaveOptions.DisableFormatting)
                                         ?? string.Empty
                        });
                    }
                }
                catch
                {
                    // The raw GPP XML editor remains available for malformed documents.
                }
            }
        }

        return result
            .OrderBy(item => item.GpoName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(item => item.Scope, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.TargetPath, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(item => item.Ordinal)
            .ToArray();
    }

    public IReadOnlyList<GppFolderItemInfo> LoadFolders(
        IEnumerable<GpoInfo> gpos,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var result = new List<GppFolderItemInfo>();
        var list = gpos.ToList();
        var type = GetTypeInfo("Folders");

        for (var index = 0; index < list.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var gpo = list[index];
            progress?.Report($"Folders {index + 1}/{list.Count}: {gpo.DisplayName}");

            foreach (var scope in new[] { "Computer", "User" })
            {
                var target = _documents.BuildTarget(gpo, scope, type);
                if (!File.Exists(target.XmlPath))
                    continue;

                try
                {
                    var document = GppXmlCacheService.Load(
                        target.XmlPath,
                        LoadOptions.PreserveWhitespace);

                    var ordinal = 0;

                    foreach (var folder in document
                                 .Descendants()
                                 .Where(element => element.Name.LocalName == "Folder"))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        ordinal++;

                        var properties = folder.Elements()
                            .FirstOrDefault(element => element.Name.LocalName == "Properties");

                        if (properties is null)
                            continue;

                        var filters = folder.Elements()
                            .FirstOrDefault(element => element.Name.LocalName == "Filters");

                        var path = Attr(properties, "path");

                        result.Add(new GppFolderItemInfo
                        {
                            GpoId = gpo.Id,
                            GpoName = gpo.DisplayName,
                            DomainName = gpo.DomainName,
                            Scope = scope,
                            XmlPath = target.XmlPath,
                            Uid = Attr(folder, "uid"),
                            Ordinal = ordinal,
                            DisplayName = FirstNonEmpty(
                                Attr(folder, "name"),
                                Attr(folder, "status"),
                                path),
                            Action = FirstNonEmpty(Attr(properties, "action"), "U"),
                            Path = path,
                            ReadOnly = IsTrue(Attr(properties, "readOnly")),
                            Archive = IsTrue(Attr(properties, "archive")),
                            Hidden = IsTrue(Attr(properties, "hidden")),
                            DeleteIgnoreErrors = IsTrue(Attr(properties, "deleteIgnoreErrors")),
                            DeleteReadOnly = IsTrue(Attr(properties, "deleteReadOnly")),
                            DeleteFiles = IsTrue(Attr(properties, "deleteFiles")),
                            DeleteSubFolders = IsTrue(Attr(properties, "deleteSubFolders")),
                            DeleteFolder = IsTrue(Attr(properties, "deleteFolder")),
                            Disabled = IsTrue(Attr(folder, "disabled")),
                            BypassErrors = IsTrue(Attr(folder, "bypassErrors")),
                            RemoveWhenNoLongerApplied = IsTrue(Attr(folder, "removePolicy")),
                            RunInUserContext = IsTrue(Attr(folder, "userContext")),
                            FiltersXml = filters?.ToString(SaveOptions.DisableFormatting)
                                         ?? string.Empty
                        });
                    }
                }
                catch
                {
                    // The raw GPP XML editor remains available for malformed documents.
                }
            }
        }

        return result
            .OrderBy(item => item.GpoName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(item => item.Scope, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Path, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(item => item.Ordinal)
            .ToArray();
    }

    public GppFileItemInfo CreateNewFile(
        GpoInfo gpo,
        string scope) =>
        new()
        {
            GpoId = gpo.Id,
            GpoName = gpo.DisplayName,
            DomainName = gpo.DomainName,
            Scope = NormalizeScope(scope),
            XmlPath = _documents.BuildTarget(
                gpo,
                NormalizeScope(scope),
                GetTypeInfo("Files")).XmlPath,
            Uid = Guid.NewGuid().ToString("B").ToUpperInvariant(),
            DisplayName = "New File Preference",
            Action = "U"
        };

    public GppFolderItemInfo CreateNewFolder(
        GpoInfo gpo,
        string scope) =>
        new()
        {
            GpoId = gpo.Id,
            GpoName = gpo.DisplayName,
            DomainName = gpo.DomainName,
            Scope = NormalizeScope(scope),
            XmlPath = _documents.BuildTarget(
                gpo,
                NormalizeScope(scope),
                GetTypeInfo("Folders")).XmlPath,
            Uid = Guid.NewGuid().ToString("B").ToUpperInvariant(),
            DisplayName = "New Folder Preference",
            Action = "U"
        };

    public void SaveFile(
        GpoInfo gpo,
        string domainDistinguishedName,
        GppFileItemInfo item)
    {
        ValidateFile(item);

        var target = _documents.BuildTarget(
            gpo,
            item.Scope,
            GetTypeInfo("Files"));

        var document = LoadOrCreate(
            target.XmlPath,
            "Files",
            FilesRootClsid);

        var items = document
            .Descendants()
            .Where(element => element.Name.LocalName == "File")
            .ToArray();

        var file = FindItem(
            items,
            item.Uid,
            item.Ordinal);

        if (file is null)
        {
            file = new XElement(
                "File",
                new XAttribute("clsid", FileItemClsid));

            document.Root!.Add(file);
        }

        UpdateFile(file, item);

        _documents.SaveXml(
            gpo,
            domainDistinguishedName,
            target,
            document.ToString());
    }

    public void SaveFolder(
        GpoInfo gpo,
        string domainDistinguishedName,
        GppFolderItemInfo item)
    {
        ValidateFolder(item);

        var target = _documents.BuildTarget(
            gpo,
            item.Scope,
            GetTypeInfo("Folders"));

        var document = LoadOrCreate(
            target.XmlPath,
            "Folders",
            FoldersRootClsid);

        var items = document
            .Descendants()
            .Where(element => element.Name.LocalName == "Folder")
            .ToArray();

        var folder = FindItem(
            items,
            item.Uid,
            item.Ordinal);

        if (folder is null)
        {
            folder = new XElement(
                "Folder",
                new XAttribute("clsid", FolderItemClsid));

            document.Root!.Add(folder);
        }

        UpdateFolder(folder, item);

        _documents.SaveXml(
            gpo,
            domainDistinguishedName,
            target,
            document.ToString());
    }

    public void DeleteFile(
        GpoInfo gpo,
        string domainDistinguishedName,
        GppFileItemInfo item)
    {
        DeleteItem(
            gpo,
            domainDistinguishedName,
            item.Scope,
            GetTypeInfo("Files"),
            "File",
            item.Uid,
            item.Ordinal);
    }

    public void DeleteFolder(
        GpoInfo gpo,
        string domainDistinguishedName,
        GppFolderItemInfo item)
    {
        DeleteItem(
            gpo,
            domainDistinguishedName,
            item.Scope,
            GetTypeInfo("Folders"),
            "Folder",
            item.Uid,
            item.Ordinal);
    }

    public void CopyFileValues(
        GppFileItemInfo source,
        GppFileItemInfo target)
    {
        target.DisplayName = source.DisplayName;
        target.Action = source.Action;
        target.FromPath = source.FromPath;
        target.TargetPath = source.TargetPath;
        target.SuppressErrors = source.SuppressErrors;
        target.ReadOnly = source.ReadOnly;
        target.Archive = source.Archive;
        target.Hidden = source.Hidden;
        target.Disabled = source.Disabled;
        target.BypassErrors = source.BypassErrors;
        target.RemoveWhenNoLongerApplied = source.RemoveWhenNoLongerApplied;
        target.RunInUserContext = source.RunInUserContext;
        target.FiltersXml = source.FiltersXml;
    }

    public void CopyFolderValues(
        GppFolderItemInfo source,
        GppFolderItemInfo target)
    {
        target.DisplayName = source.DisplayName;
        target.Action = source.Action;
        target.Path = source.Path;
        target.ReadOnly = source.ReadOnly;
        target.Archive = source.Archive;
        target.Hidden = source.Hidden;
        target.DeleteIgnoreErrors = source.DeleteIgnoreErrors;
        target.DeleteReadOnly = source.DeleteReadOnly;
        target.DeleteFiles = source.DeleteFiles;
        target.DeleteSubFolders = source.DeleteSubFolders;
        target.DeleteFolder = source.DeleteFolder;
        target.Disabled = source.Disabled;
        target.BypassErrors = source.BypassErrors;
        target.RemoveWhenNoLongerApplied = source.RemoveWhenNoLongerApplied;
        target.RunInUserContext = source.RunInUserContext;
        target.FiltersXml = source.FiltersXml;
    }

    private void DeleteItem(
        GpoInfo gpo,
        string domainDistinguishedName,
        string scope,
        GppDocumentTypeInfo type,
        string itemName,
        string uid,
        int ordinal)
    {
        var target = _documents.BuildTarget(
            gpo,
            scope,
            type);

        if (!File.Exists(target.XmlPath))
            return;

        var document = GppXmlCacheService.Load(
            target.XmlPath,
            LoadOptions.PreserveWhitespace);

        var items = document
            .Descendants()
            .Where(element => element.Name.LocalName == itemName)
            .ToArray();

        var selected = FindItem(
            items,
            uid,
            ordinal)
            ?? throw new InvalidOperationException(
                $"The selected {itemName} preference no longer exists. Refresh the list.");

        selected.Remove();

        if (!document.Descendants()
                .Any(element => element.Name.LocalName == itemName))
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

    private static XDocument LoadOrCreate(
        string path,
        string rootName,
        string rootClsid)
    {
        if (File.Exists(path))
        {
            var document = GppXmlCacheService.Load(
                path,
                LoadOptions.PreserveWhitespace);

            if (document.Root is null ||
                !document.Root.Name.LocalName.Equals(
                    rootName,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"The existing document root is not <{rootName}>. " +
                    "Use the raw GPP XML editor to repair it.");
            }

            return document;
        }

        return new XDocument(
            new XDeclaration("1.0", "utf-8", null),
            new XElement(
                rootName,
                new XAttribute("clsid", rootClsid)));
    }

    private static void UpdateFile(
        XElement file,
        GppFileItemInfo item)
    {
        var display = string.IsNullOrWhiteSpace(item.DisplayName)
            ? item.TargetPath.Trim()
            : item.DisplayName.Trim();

        SetCommonItemAttributes(
            file,
            FileItemClsid,
            display,
            item.Uid,
            item.Disabled,
            item.BypassErrors,
            item.RemoveWhenNoLongerApplied,
            item.RunInUserContext,
            out var uid);

        item.Uid = uid;

        var properties = EnsureProperties(file);

        SetAttr(properties, "action", NormalizeAction(item.Action));
        SetAttr(properties, "fromPath", item.FromPath.Trim());
        SetAttr(properties, "targetPath", item.TargetPath.Trim());
        SetAttr(properties, "readOnly", Bool(item.ReadOnly));
        SetAttr(properties, "archive", Bool(item.Archive));
        SetAttr(properties, "hidden", Bool(item.Hidden));
        SetAttr(properties, "suppress", Bool(item.SuppressErrors));

        ApplyFilters(file, item.FiltersXml);
    }

    private static void UpdateFolder(
        XElement folder,
        GppFolderItemInfo item)
    {
        var display = string.IsNullOrWhiteSpace(item.DisplayName)
            ? item.Path.Trim()
            : item.DisplayName.Trim();

        SetCommonItemAttributes(
            folder,
            FolderItemClsid,
            display,
            item.Uid,
            item.Disabled,
            item.BypassErrors,
            item.RemoveWhenNoLongerApplied,
            item.RunInUserContext,
            out var uid);

        item.Uid = uid;

        var properties = EnsureProperties(folder);

        SetAttr(properties, "action", NormalizeAction(item.Action));
        SetAttr(properties, "path", item.Path.Trim());
        SetAttr(properties, "readOnly", Bool(item.ReadOnly));
        SetAttr(properties, "archive", Bool(item.Archive));
        SetAttr(properties, "hidden", Bool(item.Hidden));
        SetAttr(properties, "deleteIgnoreErrors", Bool(item.DeleteIgnoreErrors));
        SetAttr(properties, "deleteReadOnly", Bool(item.DeleteReadOnly));
        SetAttr(properties, "deleteFiles", Bool(item.DeleteFiles));
        SetAttr(properties, "deleteSubFolders", Bool(item.DeleteSubFolders));
        SetAttr(properties, "deleteFolder", Bool(item.DeleteFolder));

        ApplyFilters(folder, item.FiltersXml);
    }

    private static void SetCommonItemAttributes(
        XElement element,
        string clsid,
        string display,
        string uid,
        bool disabled,
        bool bypassErrors,
        bool removePolicy,
        bool userContext,
        out string normalizedUid)
    {
        SetAttr(element, "clsid", clsid);
        SetAttr(element, "name", display);
        SetAttr(element, "status", display);
        SetAttr(element, "image", "2");
        SetAttr(
            element,
            "changed",
            DateTime.UtcNow.ToString(
                "yyyy-MM-dd HH:mm:ss",
                CultureInfo.InvariantCulture));

        normalizedUid = string.IsNullOrWhiteSpace(uid)
            ? Guid.NewGuid().ToString("B").ToUpperInvariant()
            : uid;

        SetAttr(element, "uid", normalizedUid);

        SetOptionalBool(element, "disabled", disabled);
        SetOptionalBool(element, "bypassErrors", bypassErrors);
        SetOptionalBool(element, "removePolicy", removePolicy);
        SetOptionalBool(element, "userContext", userContext);
    }

    private static XElement EnsureProperties(XElement element)
    {
        var properties = element.Elements()
            .FirstOrDefault(child =>
                child.Name.LocalName == "Properties");

        if (properties is not null)
            return properties;

        properties = new XElement("Properties");
        element.AddFirst(properties);
        return properties;
    }

    private static void ApplyFilters(
        XElement item,
        string filtersXml)
    {
        var existing = item.Elements()
            .FirstOrDefault(element =>
                element.Name.LocalName == "Filters");

        if (string.IsNullOrWhiteSpace(filtersXml))
        {
            existing?.Remove();
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

        if (existing is null)
            item.Add(filters);
        else
            existing.ReplaceWith(filters);
    }

    private static XElement? FindItem(
        IReadOnlyList<XElement> items,
        string uid,
        int ordinal)
    {
        if (!string.IsNullOrWhiteSpace(uid))
        {
            var byUid = items.FirstOrDefault(element =>
                Attr(element, "uid").Equals(
                    uid,
                    StringComparison.OrdinalIgnoreCase));

            if (byUid is not null)
                return byUid;
        }

        if (ordinal > 0 &&
            ordinal <= items.Count)
            return items[ordinal - 1];

        return null;
    }

    private static void ValidateFile(
        GppFileItemInfo item)
    {
        var action = NormalizeAction(item.Action);

        if (string.IsNullOrWhiteSpace(item.TargetPath))
        {
            throw new InvalidOperationException(
                "Target path cannot be empty.");
        }

        if (action != "D" &&
            string.IsNullOrWhiteSpace(item.FromPath))
        {
            throw new InvalidOperationException(
                "Source path cannot be empty for Create, Replace, or Update.");
        }
    }

    private static void ValidateFolder(
        GppFolderItemInfo item)
    {
        if (string.IsNullOrWhiteSpace(item.Path))
        {
            throw new InvalidOperationException(
                "Folder path cannot be empty.");
        }

        if (item.Path.Trim().EndsWith(
                "\\",
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Folder path must not end with a backslash.");
        }
    }

    private GppDocumentTypeInfo GetTypeInfo(string name) =>
        _documents.GetKnownTypes().First(type =>
            type.Name.Equals(
                name,
                StringComparison.OrdinalIgnoreCase));

    private static string NormalizeScope(string scope) =>
        scope.Equals(
            "User",
            StringComparison.OrdinalIgnoreCase)
            ? "User"
            : "Computer";

    private static string NormalizeAction(string action) =>
        action.Trim().ToUpperInvariant() switch
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

    private static string Bool(bool value) =>
        value ? "1" : "0";

    private static string FirstNonEmpty(params string[] values) =>
        values.FirstOrDefault(value =>
            !string.IsNullOrWhiteSpace(value))
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

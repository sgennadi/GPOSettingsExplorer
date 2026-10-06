using System.Globalization;
using System.Xml.Linq;
using GPOSettingsExplorer.Models;

namespace GPOSettingsExplorer.Services;

public sealed class GppDataSourceService
{
    private const string RootClsid =
        "{380F820F-F21B-41AC-A3CC-24D4F80F067B}";

    private const string ItemClsid =
        "{5C209626-D820-4D69-8D50-1FACD6214488}";

    private readonly GppDocumentService _documents;

    public GppDataSourceService(GppDocumentService documents)
    {
        _documents = documents;
    }

    public IReadOnlyList<GppDataSourceItemInfo> Load(
        IEnumerable<GpoInfo> gpos,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var result = new List<GppDataSourceItemInfo>();
        var list = gpos.ToList();
        var type = GetDataSourcesType();

        for (var index = 0; index < list.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var gpo = list[index];
            progress?.Report(
                $"Data Sources {index + 1}/{list.Count}: {gpo.DisplayName}");

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

                    foreach (var element in document
                                 .Descendants()
                                 .Where(child => child.Name.LocalName == "DataSource"))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        ordinal++;

                        var properties = element.Elements()
                            .FirstOrDefault(child =>
                                child.Name.LocalName == "Properties");

                        if (properties is null)
                            continue;

                        var filters = element.Elements()
                            .FirstOrDefault(child =>
                                child.Name.LocalName == "Filters");

                        result.Add(ReadItem(
                            gpo,
                            scope,
                            target.XmlPath,
                            ordinal,
                            element,
                            properties,
                            filters));
                    }
                }
                catch
                {
                    // Malformed DataSources.xml remains available through raw GPP XML editing.
                }
            }
        }

        return result
            .OrderBy(item => item.GpoName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(item => item.Scope, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Dsn, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(item => item.Ordinal)
            .ToArray();
    }

    public GppDataSourceItemInfo CreateNew(
        GpoInfo gpo,
        string scope)
    {
        var normalizedScope = NormalizeScope(scope);

        return new GppDataSourceItemInfo
        {
            GpoId = gpo.Id,
            GpoName = gpo.DisplayName,
            DomainName = gpo.DomainName,
            Scope = normalizedScope,
            XmlPath = _documents.BuildTarget(
                gpo,
                normalizedScope,
                GetDataSourcesType()).XmlPath,
            Uid = Guid.NewGuid().ToString("B").ToUpperInvariant(),
            DisplayName = "New Data Source",
            Dsn = "NewDataSource",
            Action = "U",
            UserDsn = normalizedScope.Equals(
                "User",
                StringComparison.OrdinalIgnoreCase),
            RunInUserContext = normalizedScope.Equals(
                "User",
                StringComparison.OrdinalIgnoreCase)
        };
    }

    public void Save(
        GpoInfo gpo,
        string domainDistinguishedName,
        GppDataSourceItemInfo item)
    {
        Validate(item);

        var target = _documents.BuildTarget(
            gpo,
            item.Scope,
            GetDataSourcesType());

        XDocument document;

        if (File.Exists(target.XmlPath))
        {
            document = XDocument.Load(
                target.XmlPath,
                LoadOptions.PreserveWhitespace);

            if (document.Root is null ||
                !document.Root.Name.LocalName.Equals(
                    "DataSources",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "The existing DataSources.xml root is not <DataSources>. Use the raw GPP XML editor to repair it.");
            }
        }
        else
        {
            document = new XDocument(
                new XDeclaration("1.0", "utf-8", null),
                new XElement(
                    "DataSources",
                    new XAttribute("clsid", RootClsid)));
        }

        var dataSource = FindItem(document, item);

        if (dataSource is null)
        {
            dataSource = new XElement(
                "DataSource",
                new XAttribute("clsid", ItemClsid));

            document.Root!.Add(dataSource);
        }

        UpdateItem(dataSource, item);

        _documents.SaveXml(
            gpo,
            domainDistinguishedName,
            target,
            document.ToString());
    }

    public void Delete(
        GpoInfo gpo,
        string domainDistinguishedName,
        GppDataSourceItemInfo item)
    {
        var target = _documents.BuildTarget(
            gpo,
            item.Scope,
            GetDataSourcesType());

        if (!File.Exists(target.XmlPath))
            return;

        var document = XDocument.Load(
            target.XmlPath,
            LoadOptions.PreserveWhitespace);

        var selected = FindItem(document, item)
            ?? throw new InvalidOperationException(
                "The selected Data Source preference no longer exists. Refresh the list.");

        selected.Remove();

        if (!(document.Root?.Elements()
                .Any(element => element.Name.LocalName == "DataSource") ?? false))
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
        GppDataSourceItemInfo source,
        GppDataSourceItemInfo target,
        bool preserveCredential)
    {
        target.DisplayName = source.DisplayName;
        target.Description = source.Description;
        target.Action = source.Action;
        target.UserDsn = source.UserDsn;
        target.Dsn = source.Dsn;
        target.Driver = source.Driver;
        target.DsnDescription = source.DsnDescription;
        target.UserName = source.UserName;
        target.Attributes = new System.Collections.ObjectModel.ObservableCollection<GppDataSourceAttributeInfo>(
            source.Attributes.Select(item => new GppDataSourceAttributeInfo
            {
                Name = item.Name,
                Value = item.Value
            }));
        target.Disabled = source.Disabled;
        target.BypassErrors = source.BypassErrors;
        target.RemoveWhenNoLongerApplied = source.RemoveWhenNoLongerApplied;
        target.RunInUserContext = source.RunInUserContext;
        target.FiltersXml = source.FiltersXml;
        target.OpaqueCredential = preserveCredential
            ? source.OpaqueCredential
            : string.Empty;
        target.ClearStoredCredential = false;
    }

    private static GppDataSourceItemInfo ReadItem(
        GpoInfo gpo,
        string scope,
        string xmlPath,
        int ordinal,
        XElement element,
        XElement properties,
        XElement? filters)
    {
        var attributes = properties.Elements()
            .FirstOrDefault(child => child.Name.LocalName == "Attributes")?
            .Elements()
            .Where(child => child.Name.LocalName == "Attribute")
            .Select(child => new GppDataSourceAttributeInfo
            {
                Name = Attr(child, "name"),
                Value = Attr(child, "value")
            })
            .ToArray()
            ?? Array.Empty<GppDataSourceAttributeInfo>();

        var dsn = Attr(properties, "dsn");

        return new GppDataSourceItemInfo
        {
            GpoId = gpo.Id,
            GpoName = gpo.DisplayName,
            DomainName = gpo.DomainName,
            Scope = scope,
            XmlPath = xmlPath,
            Uid = Attr(element, "uid"),
            Ordinal = ordinal,
            DisplayName = FirstNonEmpty(
                Attr(element, "name"),
                Attr(element, "status"),
                dsn,
                "Data Source"),
            Description = Attr(element, "desc"),
            Action = FirstNonEmpty(Attr(properties, "action"), "U"),
            UserDsn = IsTrue(Attr(properties, "userDSN")),
            Dsn = dsn,
            Driver = Attr(properties, "driver"),
            DsnDescription = Attr(properties, "description"),
            UserName = Attr(properties, "username"),
            Attributes = new System.Collections.ObjectModel.ObservableCollection<GppDataSourceAttributeInfo>(
                attributes),
            Disabled = IsTrue(Attr(element, "disabled")),
            BypassErrors = IsTrue(Attr(element, "bypassErrors")),
            RemoveWhenNoLongerApplied = IsTrue(Attr(element, "removePolicy")),
            RunInUserContext = IsTrue(Attr(element, "userContext")),
            FiltersXml = filters?.ToString(SaveOptions.DisableFormatting)
                ?? string.Empty,
            OpaqueCredential = Attr(properties, "cpassword")
        };
    }

    private static void UpdateItem(
        XElement dataSource,
        GppDataSourceItemInfo item)
    {
        var display = string.IsNullOrWhiteSpace(item.DisplayName)
            ? item.Dsn.Trim()
            : item.DisplayName.Trim();

        SetAttr(dataSource, "clsid", ItemClsid);
        SetAttr(dataSource, "name", display);
        SetAttr(dataSource, "status", display);
        SetAttr(dataSource, "image", item.UserDsn ? "1" : "2");
        SetAttr(
            dataSource,
            "changed",
            DateTime.UtcNow.ToString(
                "yyyy-MM-dd HH:mm:ss",
                CultureInfo.InvariantCulture));

        if (string.IsNullOrWhiteSpace(item.Uid))
            item.Uid = Guid.NewGuid().ToString("B").ToUpperInvariant();

        SetAttr(dataSource, "uid", item.Uid);
        SetOptionalAttr(dataSource, "desc", item.Description);
        SetOptionalBool(dataSource, "disabled", item.Disabled);
        SetOptionalBool(dataSource, "bypassErrors", item.BypassErrors);
        SetOptionalBool(dataSource, "removePolicy", item.RemoveWhenNoLongerApplied);
        SetOptionalBool(dataSource, "userContext", item.RunInUserContext);

        var properties = dataSource.Elements()
            .FirstOrDefault(child => child.Name.LocalName == "Properties");

        if (properties is null)
        {
            properties = new XElement("Properties");
            dataSource.AddFirst(properties);
        }

        SetAttr(properties, "action", item.Action.ToUpperInvariant());
        SetAttr(properties, "userDSN", item.UserDsn ? "1" : "0");
        SetAttr(properties, "dsn", item.Dsn.Trim());
        SetAttr(properties, "driver", item.Driver.Trim());
        SetOptionalAttr(properties, "description", item.DsnDescription);
        SetOptionalAttr(properties, "username", item.UserName);

        if (item.ClearStoredCredential)
        {
            properties.SetAttributeValue("cpassword", null);
            item.OpaqueCredential = string.Empty;
        }
        else if (!string.IsNullOrWhiteSpace(item.OpaqueCredential))
        {
            SetAttr(properties, "cpassword", item.OpaqueCredential);
        }
        else
        {
            properties.SetAttributeValue("cpassword", null);
        }

        var attributesNode = properties.Elements()
            .FirstOrDefault(child => child.Name.LocalName == "Attributes");

        if (attributesNode is null)
        {
            attributesNode = new XElement("Attributes");
            properties.Add(attributesNode);
        }

        attributesNode.RemoveNodes();

        foreach (var attribute in item.Attributes
                     .Where(attribute =>
                         !string.IsNullOrWhiteSpace(attribute.Name)))
        {
            attributesNode.Add(
                new XElement(
                    "Attribute",
                    new XAttribute("name", attribute.Name.Trim()),
                    new XAttribute("value", attribute.Value ?? string.Empty)));
        }

        var existingFilters = dataSource.Elements()
            .FirstOrDefault(child => child.Name.LocalName == "Filters");

        existingFilters?.Remove();

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

            dataSource.Add(filters);
        }
    }

    private static XElement? FindItem(
        XDocument document,
        GppDataSourceItemInfo item)
    {
        var candidates = document
            .Descendants()
            .Where(element =>
                element.Name.LocalName == "DataSource")
            .ToArray();

        if (!string.IsNullOrWhiteSpace(item.Uid))
        {
            var byUid = candidates.FirstOrDefault(element =>
                Attr(element, "uid")
                    .Equals(item.Uid, StringComparison.OrdinalIgnoreCase));

            if (byUid is not null)
                return byUid;
        }

        if (item.Ordinal > 0 &&
            item.Ordinal <= candidates.Length)
        {
            return candidates[item.Ordinal - 1];
        }

        return candidates.FirstOrDefault(element =>
        {
            var properties = element.Elements()
                .FirstOrDefault(child =>
                    child.Name.LocalName == "Properties");

            return Attr(properties, "dsn")
                .Equals(
                    item.Dsn,
                    StringComparison.CurrentCultureIgnoreCase);
        });
    }

    private GppDocumentTypeInfo GetDataSourcesType() =>
        _documents.GetKnownTypes()
            .First(type =>
                type.Name.Equals(
                    "Data Sources",
                    StringComparison.OrdinalIgnoreCase));

    private static void Validate(
        GppDataSourceItemInfo item)
    {
        if (string.IsNullOrWhiteSpace(item.Dsn))
            throw new InvalidOperationException("DSN name cannot be empty.");

        if (string.IsNullOrWhiteSpace(item.Driver) &&
            !item.Action.Equals("D", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "ODBC driver name cannot be empty unless the action is Delete.");
        }

        if (item.Action.ToUpperInvariant()
            is not ("C" or "U" or "R" or "D"))
        {
            throw new InvalidOperationException(
                "Invalid Data Source preference action.");
        }

        var duplicateAttributes = item.Attributes
            .Where(attribute =>
                !string.IsNullOrWhiteSpace(attribute.Name))
            .GroupBy(
                attribute => attribute.Name.Trim(),
                StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);

        if (duplicateAttributes is not null)
        {
            throw new InvalidOperationException(
                $"Duplicate ODBC attribute: {duplicateAttributes.Key}");
        }
    }

    private static string NormalizeScope(string value) =>
        value.Equals(
            "User",
            StringComparison.OrdinalIgnoreCase)
            ? "User"
            : "Computer";

    private static string Attr(
        XElement? element,
        string name) =>
        element?.Attributes()
            .FirstOrDefault(attribute =>
                attribute.Name.LocalName.Equals(
                    name,
                    StringComparison.OrdinalIgnoreCase))?
            .Value.Trim()
        ?? string.Empty;

    private static string FirstNonEmpty(
        params string[] values) =>
        values.FirstOrDefault(value =>
            !string.IsNullOrWhiteSpace(value))
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
        string value)
    {
        element.SetAttributeValue(
            name,
            string.IsNullOrWhiteSpace(value)
                ? null
                : value.Trim());
    }

    private static void SetOptionalBool(
        XElement element,
        string name,
        bool value)
    {
        element.SetAttributeValue(
            name,
            value ? "1" : null);
    }
}

using System.Globalization;
using System.Xml.Linq;
using GPOSettingsExplorer.Models;

namespace GPOSettingsExplorer.Services;

public sealed class GppEnvironmentService
{
    private const string RootClsid = "{BF141A63-327B-438A-B9BF-2C188F13B7AD}";
    private const string ItemClsid = "{78570023-8373-4A19-BA80-2F150738EA19}";

    private readonly GppDocumentService _documents;

    public GppEnvironmentService(GppDocumentService documents)
    {
        _documents = documents;
    }

    public IReadOnlyList<GppEnvironmentVariableInfo> Load(
        IEnumerable<GpoInfo> gpos,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var result = new List<GppEnvironmentVariableInfo>();
        var list = gpos.ToList();
        var type = GetEnvironmentType();

        for (var index = 0; index < list.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var gpo = list[index];
            progress?.Report(
                $"Environment Variables {index + 1}/{list.Count}: {gpo.DisplayName}");

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

                    foreach (var element in document
                                 .Descendants()
                                 .Where(child =>
                                     child.Name.LocalName == "EnvironmentVariable"))
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

                        var name = Attr(properties, "name");
                        var value = Attr(properties, "value");

                        result.Add(new GppEnvironmentVariableInfo
                        {
                            GpoId = gpo.Id,
                            GpoName = gpo.DisplayName,
                            DomainName = gpo.DomainName,
                            Scope = scope,
                            XmlPath = target.XmlPath,
                            Uid = Attr(element, "uid"),
                            Ordinal = ordinal,
                            DisplayName = FirstNonEmpty(
                                Attr(element, "name"),
                                Attr(element, "status"),
                                name),
                            Description = FirstNonEmpty(
                                Attr(element, "desc"),
                                Attr(element, "descr")),
                            Action = FirstNonEmpty(
                                Attr(properties, "action"),
                                "U"),
                            Name = name,
                            Value = value,
                            UserVariable = IsTrue(
                                Attr(properties, "user")),
                            PartialPath = IsTrue(
                                Attr(properties, "partial")),
                            Disabled = IsTrue(
                                Attr(element, "disabled")),
                            BypassErrors = IsTrue(
                                Attr(element, "bypassErrors")),
                            RemoveWhenNoLongerApplied = IsTrue(
                                Attr(element, "removePolicy")),
                            RunInUserContext = IsTrue(
                                Attr(element, "userContext")),
                            FiltersXml =
                                filters?.ToString(
                                    SaveOptions.DisableFormatting)
                                ?? string.Empty
                        });
                    }
                }
                catch
                {
                    // The generic GPP XML editor remains available for malformed documents.
                }
            }
        }

        return result
            .OrderBy(
                item => item.GpoName,
                StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(
                item => item.Scope,
                StringComparer.OrdinalIgnoreCase)
            .ThenBy(
                item => item.Name,
                StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(item => item.Ordinal)
            .ToArray();
    }

    public GppEnvironmentVariableInfo CreateNew(
        GpoInfo gpo,
        string scope)
    {
        var normalizedScope = NormalizeScope(scope);

        return new GppEnvironmentVariableInfo
        {
            GpoId = gpo.Id,
            GpoName = gpo.DisplayName,
            DomainName = gpo.DomainName,
            Scope = normalizedScope,
            XmlPath = _documents.BuildTarget(
                gpo,
                normalizedScope,
                GetEnvironmentType()).XmlPath,
            Uid = Guid.NewGuid()
                .ToString("B")
                .ToUpperInvariant(),
            DisplayName = "New Environment Variable",
            Action = "U",
            UserVariable = normalizedScope == "User"
        };
    }

    public void Save(
        GpoInfo gpo,
        string domainDistinguishedName,
        GppEnvironmentVariableInfo item)
    {
        Validate(item);

        var target = _documents.BuildTarget(
            gpo,
            item.Scope,
            GetEnvironmentType());

        XDocument document;

        if (File.Exists(target.XmlPath))
        {
            document = GppXmlCacheService.Load(
                target.XmlPath,
                LoadOptions.PreserveWhitespace);

            if (document.Root is null ||
                !document.Root.Name.LocalName.Equals(
                    "EnvironmentVariables",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "The existing EnvironmentVariables.xml root element is not <EnvironmentVariables>. " +
                    "Use the raw GPP XML editor to repair it.");
            }
        }
        else
        {
            document = new XDocument(
                new XDeclaration(
                    "1.0",
                    "utf-8",
                    null),
                new XElement(
                    "EnvironmentVariables",
                    new XAttribute(
                        "clsid",
                        RootClsid)));
        }

        var items = document
            .Descendants()
            .Where(element =>
                element.Name.LocalName == "EnvironmentVariable")
            .ToArray();

        var environmentVariable = FindItem(
            items,
            item);

        if (environmentVariable is null)
        {
            environmentVariable = new XElement(
                "EnvironmentVariable",
                new XAttribute(
                    "clsid",
                    ItemClsid));

            document.Root!.Add(environmentVariable);
        }

        UpdateItem(
            environmentVariable,
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
        GppEnvironmentVariableInfo item)
    {
        var target = _documents.BuildTarget(
            gpo,
            item.Scope,
            GetEnvironmentType());

        if (!File.Exists(target.XmlPath))
            return;

        var document = GppXmlCacheService.Load(
            target.XmlPath,
            LoadOptions.PreserveWhitespace);

        var items = document
            .Descendants()
            .Where(element =>
                element.Name.LocalName == "EnvironmentVariable")
            .ToArray();

        var selected = FindItem(
            items,
            item)
            ?? throw new InvalidOperationException(
                "The selected Environment Variable preference no longer exists. Refresh the list.");

        selected.Remove();

        if (!document
                .Descendants()
                .Any(element =>
                    element.Name.LocalName == "EnvironmentVariable"))
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
        GppEnvironmentVariableInfo source,
        GppEnvironmentVariableInfo target)
    {
        target.DisplayName = source.DisplayName;
        target.Description = source.Description;
        target.Action = source.Action;
        target.Name = source.Name;
        target.Value = source.Value;
        target.UserVariable = source.UserVariable;
        target.PartialPath = source.PartialPath;
        target.Disabled = source.Disabled;
        target.BypassErrors = source.BypassErrors;
        target.RemoveWhenNoLongerApplied =
            source.RemoveWhenNoLongerApplied;
        target.RunInUserContext =
            source.RunInUserContext;
        target.FiltersXml =
            source.FiltersXml;
    }

    private static void UpdateItem(
        XElement element,
        GppEnvironmentVariableInfo item)
    {
        var display = string.IsNullOrWhiteSpace(
            item.DisplayName)
            ? $"{item.Name} = {item.Value}"
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
            $"{item.Name} = {item.Value}");
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

        if (string.IsNullOrWhiteSpace(item.Uid))
        {
            item.Uid = Guid.NewGuid()
                .ToString("B")
                .ToUpperInvariant();
        }

        SetAttr(
            element,
            "uid",
            item.Uid);

        if (string.IsNullOrWhiteSpace(item.Description))
            element.SetAttributeValue("desc", null);
        else
            SetAttr(
                element,
                "desc",
                item.Description);

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

        var properties = element.Elements()
            .FirstOrDefault(child =>
                child.Name.LocalName == "Properties");

        if (properties is null)
        {
            properties = new XElement("Properties");
            element.AddFirst(properties);
        }

        SetAttr(
            properties,
            "action",
            NormalizeAction(item.Action));
        SetAttr(
            properties,
            "name",
            item.Name.Trim());
        SetAttr(
            properties,
            "value",
            item.Value ?? string.Empty);
        SetAttr(
            properties,
            "user",
            Bool(item.UserVariable));
        SetAttr(
            properties,
            "partial",
            Bool(item.PartialPath));

        ApplyFilters(
            element,
            item.FiltersXml);
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
        GppEnvironmentVariableInfo item)
    {
        if (!string.IsNullOrWhiteSpace(item.Uid))
        {
            var byUid = items.FirstOrDefault(
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
            return items[item.Ordinal - 1];
        }

        return null;
    }

    private static void Validate(
        GppEnvironmentVariableInfo item)
    {
        if (string.IsNullOrWhiteSpace(item.Name))
        {
            throw new InvalidOperationException(
                "Environment variable name cannot be empty.");
        }

        if (item.Name.Contains(
                '=',
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Environment variable name cannot contain '='.");
        }

        if (item.PartialPath &&
            (item.UserVariable ||
             !item.Name.Equals(
                 "PATH",
                 StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(
                "Partial PATH mode is valid only for the system PATH variable.");
        }

        if (!string.IsNullOrWhiteSpace(
                item.FiltersXml))
        {
            var filters = XElement.Parse(
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

    private GppDocumentTypeInfo GetEnvironmentType() =>
        _documents.GetKnownTypes().First(
            type => type.Name.Equals(
                "Environment Variables",
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

    private static string FirstNonEmpty(
        params string[] values) =>
        values.FirstOrDefault(
            value =>
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
            element.SetAttributeValue(
                name,
                "1");
        else
            element.SetAttributeValue(
                name,
                null);
    }
}

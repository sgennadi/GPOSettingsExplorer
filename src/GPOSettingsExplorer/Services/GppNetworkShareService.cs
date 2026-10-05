using System.Globalization;
using System.Xml.Linq;
using GPOSettingsExplorer.Models;

namespace GPOSettingsExplorer.Services;

public sealed class GppNetworkShareService
{
    private const string RootClsid =
        "{520870D8-A6E7-47E8-A8D8-E6A4E76EAEC2}";

    private const string ItemClsid =
        "{2888C5E7-94FC-4739-90AA-2C1536D68BC0}";

    private readonly GppDocumentService _documents;

    public GppNetworkShareService(
        GppDocumentService documents)
    {
        _documents = documents;
    }

    public IReadOnlyList<GppNetworkShareInfo> Load(
        IEnumerable<GpoInfo> gpos,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var result =
            new List<GppNetworkShareInfo>();

        var list =
            gpos.ToList();

        var type =
            GetNetworkShareType();

        for (var index = 0;
             index < list.Count;
             index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var gpo =
                list[index];

            progress?.Report(
                $"Network Shares {index + 1}/{list.Count}: {gpo.DisplayName}");

            var target =
                _documents.BuildTarget(
                    gpo,
                    "Computer",
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
                                     "NetShare"))
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

                    var shareName =
                        Attr(
                            properties,
                            "name");

                    result.Add(
                        new GppNetworkShareInfo
                        {
                            GpoId =
                                gpo.Id,
                            GpoName =
                                gpo.DisplayName,
                            DomainName =
                                gpo.DomainName,
                            Scope =
                                "Computer",
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
                                    shareName,
                                    "Network Share"),
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
                            ShareName =
                                shareName,
                            Path =
                                Attr(
                                    properties,
                                    "path"),
                            Comment =
                                Attr(
                                    properties,
                                    "comment"),
                            AllRegular =
                                IsTrue(
                                    Attr(
                                        properties,
                                        "allRegular")),
                            AllHidden =
                                IsTrue(
                                    Attr(
                                        properties,
                                        "allHidden")),
                            AllAdminDrive =
                                IsTrue(
                                    Attr(
                                        properties,
                                        "allAdminDrive")),
                            LimitUsersMode =
                                FirstNonEmpty(
                                    Attr(
                                        properties,
                                        "limitUsers"),
                                    "NO_CHANGE"),
                            UserLimit =
                                Attr(
                                    properties,
                                    "userLimit"),
                            AbeMode =
                                FirstNonEmpty(
                                    Attr(
                                        properties,
                                        "abe"),
                                    "NO_CHANGE"),
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
                // Malformed NetworkShares.xml stays available in the raw GPP XML editor.
            }
        }

        return result
            .OrderBy(
                item =>
                    item.GpoName,
                StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(
                item =>
                    item.ShareName,
                StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(
                item =>
                    item.Ordinal)
            .ToArray();
    }

    public GppNetworkShareInfo CreateNew(
        GpoInfo gpo) =>
        new()
        {
            GpoId =
                gpo.Id,
            GpoName =
                gpo.DisplayName,
            DomainName =
                gpo.DomainName,
            Scope =
                "Computer",
            XmlPath =
                _documents.BuildTarget(
                    gpo,
                    "Computer",
                    GetNetworkShareType()).XmlPath,
            Uid =
                Guid.NewGuid()
                    .ToString("B")
                    .ToUpperInvariant(),
            DisplayName =
                "New Network Share",
            Action =
                "U",
            LimitUsersMode =
                "NO_CHANGE",
            AbeMode =
                "NO_CHANGE"
        };

    public void Save(
        GpoInfo gpo,
        string domainDistinguishedName,
        GppNetworkShareInfo item)
    {
        Validate(
            item);

        var target =
            _documents.BuildTarget(
                gpo,
                "Computer",
                GetNetworkShareType());

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
                    "NetworkShareSettings",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "The existing NetworkShares.xml root element is not <NetworkShareSettings>. " +
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
                        "NetworkShareSettings",
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
                        "NetShare")
                .ToArray();

        var selected =
            FindItem(
                items,
                item);

        if (selected is null)
        {
            selected =
                new XElement(
                    "NetShare",
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
        GppNetworkShareInfo item)
    {
        var target =
            _documents.BuildTarget(
                gpo,
                "Computer",
                GetNetworkShareType());

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
                        "NetShare")
                .ToArray();

        var selected =
            FindItem(
                items,
                item)
            ?? throw new InvalidOperationException(
                "The selected Network Share preference no longer exists. Refresh the list.");

        selected.Remove();

        if (!document
                .Descendants()
                .Any(
                    element =>
                        element.Name.LocalName ==
                        "NetShare"))
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
        GppNetworkShareInfo source,
        GppNetworkShareInfo target)
    {
        target.DisplayName =
            source.DisplayName;
        target.Description =
            source.Description;
        target.Action =
            source.Action;
        target.ShareName =
            source.ShareName;
        target.Path =
            source.Path;
        target.Comment =
            source.Comment;
        target.AllRegular =
            source.AllRegular;
        target.AllHidden =
            source.AllHidden;
        target.AllAdminDrive =
            source.AllAdminDrive;
        target.LimitUsersMode =
            source.LimitUsersMode;
        target.UserLimit =
            source.UserLimit;
        target.AbeMode =
            source.AbeMode;
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
        GppNetworkShareInfo item)
    {
        var display =
            string.IsNullOrWhiteSpace(
                item.DisplayName)
                ? item.ShareName.Trim()
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
            item.ShareName.Trim());

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
            "name",
            item.ShareName.Trim());

        SetAttr(
            properties,
            "path",
            item.Path.Trim());

        SetAttr(
            properties,
            "comment",
            item.Comment);

        SetAttr(
            properties,
            "allRegular",
            Bool(
                item.AllRegular));

        SetAttr(
            properties,
            "allHidden",
            Bool(
                item.AllHidden));

        SetAttr(
            properties,
            "allAdminDrive",
            Bool(
                item.AllAdminDrive));

        SetAttr(
            properties,
            "limitUsers",
            NormalizeLimitUsersMode(
                item.LimitUsersMode));

        if (NormalizeLimitUsersMode(
                item.LimitUsersMode) ==
            "SET_LIMIT")
        {
            SetAttr(
                properties,
                "userLimit",
                item.UserLimit.Trim());
        }
        else
        {
            properties.SetAttributeValue(
                "userLimit",
                null);
        }

        SetAttr(
            properties,
            "abe",
            NormalizeAbeMode(
                item.AbeMode));

        ApplyFilters(
            element,
            item.FiltersXml);
    }

    private static XElement? FindItem(
        IReadOnlyList<XElement> items,
        GppNetworkShareInfo item)
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
        GppNetworkShareInfo item)
    {
        var action =
            NormalizeAction(
                item.Action);

        var bulk =
            item.AllRegular ||
            item.AllHidden ||
            item.AllAdminDrive;

        if (!bulk &&
            string.IsNullOrWhiteSpace(
                item.ShareName))
        {
            throw new InvalidOperationException(
                "Share name cannot be empty unless a bulk share option is selected.");
        }

        if (action != "D" &&
            !bulk &&
            string.IsNullOrWhiteSpace(
                item.Path))
        {
            throw new InvalidOperationException(
                "Share path cannot be empty for Create, Replace, or Update.");
        }

        if (NormalizeLimitUsersMode(
                item.LimitUsersMode) ==
            "SET_LIMIT")
        {
            if (!uint.TryParse(
                    item.UserLimit,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var limit) ||
                limit == 0)
            {
                throw new InvalidOperationException(
                    "User limit must be a positive whole number when 'Set limit' is selected.");
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

    private GppDocumentTypeInfo GetNetworkShareType() =>
        _documents.GetKnownTypes().First(
            type =>
                type.Name.Equals(
                    "Network Shares",
                    StringComparison.OrdinalIgnoreCase));

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

    private static string NormalizeLimitUsersMode(
        string value) =>
        value.Trim()
            .ToUpperInvariant() switch
        {
            "SET_LIMIT" or "SET LIMIT" =>
                "SET_LIMIT",
            "MAX_ALLOWED" or "MAXIMUM ALLOWED" =>
                "MAX_ALLOWED",
            _ =>
                "NO_CHANGE"
        };

    private static string NormalizeAbeMode(
        string value) =>
        value.Trim()
            .ToUpperInvariant() switch
        {
            "ENABLE" or "ENABLED" =>
                "ENABLE",
            "DISABLE" or "DISABLED" =>
                "DISABLE",
            _ =>
                "NO_CHANGE"
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

    private static string Bool(
        bool value) =>
        value
            ? "1"
            : "0";

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

using System.Globalization;
using System.Xml.Linq;
using GPOSettingsExplorer.Models;

namespace GPOSettingsExplorer.Services;

public sealed class GppPrinterService
{
    private const string RootClsid =
        "{1F577D12-3D1B-471E-A1B7-060317597B9C}";

    private const string SharedPrinterClsid =
        "{9A5E9697-9095-436D-A0EE-4D128FDFBCE5}";

    private const string PortPrinterClsid =
        "{C3A739D2-4A44-401E-9F9D-88E5E77DFB3E}";

    private const string LocalPrinterClsid =
        "{F08996D5-568B-45F5-BB7A-D3FB1E370B0A}";

    private readonly GppDocumentService _documents;

    public GppPrinterService(
        GppDocumentService documents)
    {
        _documents = documents;
    }

    public IReadOnlyList<GppPrinterItemInfo> Load(
        IEnumerable<GpoInfo> gpos,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var result =
            new List<GppPrinterItemInfo>();

        var list =
            gpos.ToList();

        var type =
            GetPrinterType();

        for (var index = 0;
             index < list.Count;
             index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var gpo =
                list[index];

            progress?.Report(
                $"Printers {index + 1}/{list.Count}: {gpo.DisplayName}");

            foreach (var scope
                     in new[]
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
                        GppXmlCacheService.Load(
                            target.XmlPath,
                            LoadOptions.PreserveWhitespace);

                    foreach (var kind
                             in new[]
                             {
                                 "SharedPrinter",
                                 "PortPrinter",
                                 "LocalPrinter"
                             })
                    {
                        var ordinal =
                            0;

                        foreach (var element
                                 in document
                                     .Descendants()
                                     .Where(
                                         child =>
                                             child.Name.LocalName ==
                                             kind))
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

                            result.Add(
                                ReadItem(
                                    gpo,
                                    scope,
                                    target.XmlPath,
                                    kind,
                                    ordinal,
                                    element,
                                    properties,
                                    filters));
                        }
                    }
                }
                catch
                {
                    // Malformed Printers.xml remains accessible through the raw GPP XML editor.
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
                    item.PrinterKindDisplay,
                StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(
                item =>
                    item.DisplayName,
                StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(
                item =>
                    item.Ordinal)
            .ToArray();
    }

    public GppPrinterItemInfo CreateNew(
        GpoInfo gpo,
        string scope,
        string printerKind)
    {
        var normalizedScope =
            NormalizeScope(
                scope);

        var normalizedKind =
            NormalizeKind(
                printerKind);

        return new GppPrinterItemInfo
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
                    GetPrinterType()).XmlPath,
            Uid =
                Guid.NewGuid()
                    .ToString("B")
                    .ToUpperInvariant(),
            PrinterKind =
                normalizedKind switch
                {
                    "PORT" => "TCP/IP",
                    "LOCAL" => "Local",
                    _ => "Shared"
                },
            DisplayName =
                "New Printer",
            Action =
                "U",
            Protocol =
                "PROTOCOL_RAWTCP_TYPE",
            PortNumber =
                "9100",
            SnmpDevIndex =
                "1"
        };
    }

    public void Save(
        GpoInfo gpo,
        string domainDistinguishedName,
        GppPrinterItemInfo item)
    {
        Validate(
            item);

        var target =
            _documents.BuildTarget(
                gpo,
                item.Scope,
                GetPrinterType());

        XDocument document;

        if (File.Exists(
                target.XmlPath))
        {
            document =
                GppXmlCacheService.Load(
                    target.XmlPath,
                    LoadOptions.PreserveWhitespace);

            if (document.Root is null ||
                !document.Root.Name.LocalName.Equals(
                    "Printers",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "The existing Printers.xml root element is not <Printers>. " +
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
                        "Printers",
                        new XAttribute(
                            "clsid",
                            RootClsid)));
        }

        var elementName =
            ElementName(
                item.PrinterKind);

        var items =
            document
                .Descendants()
                .Where(
                    element =>
                        element.Name.LocalName ==
                        elementName)
                .ToArray();

        var printer =
            FindItem(
                items,
                item);

        if (printer is null)
        {
            printer =
                new XElement(
                    elementName,
                    new XAttribute(
                        "clsid",
                        ItemClsid(
                            item.PrinterKind)));

            document.Root!.Add(
                printer);
        }

        UpdatePrinter(
            printer,
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
        GppPrinterItemInfo item)
    {
        var target =
            _documents.BuildTarget(
                gpo,
                item.Scope,
                GetPrinterType());

        if (!File.Exists(
                target.XmlPath))
            return;

        var document =
            GppXmlCacheService.Load(
                target.XmlPath,
                LoadOptions.PreserveWhitespace);

        var elementName =
            ElementName(
                item.PrinterKind);

        var items =
            document
                .Descendants()
                .Where(
                    element =>
                        element.Name.LocalName ==
                        elementName)
                .ToArray();

        var selected =
            FindItem(
                items,
                item)
            ?? throw new InvalidOperationException(
                "The selected Printer preference no longer exists. Refresh the list.");

        selected.Remove();

        var hasPrinterItems =
            document
                .Descendants()
                .Any(
                    element =>
                        element.Name.LocalName is
                            "SharedPrinter" or
                            "PortPrinter" or
                            "LocalPrinter");

        if (!hasPrinterItems)
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
        GppPrinterItemInfo source,
        GppPrinterItemInfo target,
        bool preserveCredential)
    {
        target.PrinterKind =
            source.PrinterKind;
        target.DisplayName =
            source.DisplayName;
        target.Description =
            source.Description;
        target.Action =
            source.Action;
        target.PrinterName =
            source.PrinterName;
        target.Path =
            source.Path;
        target.Port =
            source.Port;
        target.Location =
            source.Location;
        target.Comment =
            source.Comment;
        target.DefaultPrinter =
            source.DefaultPrinter;
        target.SkipLocal =
            source.SkipLocal;
        target.DeleteAll =
            source.DeleteAll;
        target.Persistent =
            source.Persistent;
        target.DeleteMaps =
            source.DeleteMaps;
        target.UserName =
            source.UserName;
        target.OpaqueCredential =
            preserveCredential
                ? source.OpaqueCredential
                : string.Empty;
        target.ClearStoredCredential =
            false;
        target.IpAddress =
            source.IpAddress;
        target.UseDns =
            source.UseDns;
        target.LocalName =
            source.LocalName;
        target.LprQueue =
            source.LprQueue;
        target.SnmpCommunity =
            source.SnmpCommunity;
        target.Protocol =
            source.Protocol;
        target.PortNumber =
            source.PortNumber;
        target.DoubleSpool =
            source.DoubleSpool;
        target.SnmpEnabled =
            source.SnmpEnabled;
        target.SnmpDevIndex =
            source.SnmpDevIndex;
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

    private static GppPrinterItemInfo ReadItem(
        GpoInfo gpo,
        string scope,
        string xmlPath,
        string elementName,
        int ordinal,
        XElement element,
        XElement properties,
        XElement? filters)
    {
        var kind =
            elementName switch
            {
                "PortPrinter" => "TCP/IP",
                "LocalPrinter" => "Local",
                _ => "Shared"
            };

        var displayName =
            FirstNonEmpty(
                Attr(
                    element,
                    "name"),
                Attr(
                    element,
                    "status"),
                kind == "TCP/IP"
                    ? Attr(
                        properties,
                        "localName")
                    : kind == "Local"
                        ? Attr(
                            properties,
                            "name")
                        : Attr(
                            properties,
                            "path"),
                "Printer");

        return new GppPrinterItemInfo
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
                xmlPath,
            Uid =
                Attr(
                    element,
                    "uid"),
            Ordinal =
                ordinal,
            PrinterKind =
                kind,
            DisplayName =
                displayName,
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
            PrinterName =
                Attr(
                    properties,
                    "name"),
            Path =
                Attr(
                    properties,
                    "path"),
            Port =
                Attr(
                    properties,
                    "port"),
            Location =
                Attr(
                    properties,
                    "location"),
            Comment =
                Attr(
                    properties,
                    "comment"),
            DefaultPrinter =
                IsTrue(
                    Attr(
                        properties,
                        "default")),
            SkipLocal =
                IsTrue(
                    Attr(
                        properties,
                        "skipLocal")),
            DeleteAll =
                IsTrue(
                    Attr(
                        properties,
                        "deleteAll")),
            Persistent =
                IsTrue(
                    Attr(
                        properties,
                        "persistent")),
            DeleteMaps =
                IsTrue(
                    Attr(
                        properties,
                        "deleteMaps")),
            UserName =
                Attr(
                    properties,
                    "username"),
            OpaqueCredential =
                Attr(
                    properties,
                    "cpassword"),
            IpAddress =
                Attr(
                    properties,
                    "ipAddress"),
            UseDns =
                IsTrue(
                    Attr(
                        properties,
                        "useDNS")),
            LocalName =
                Attr(
                    properties,
                    "localName"),
            LprQueue =
                Attr(
                    properties,
                    "lprQueue"),
            SnmpCommunity =
                Attr(
                    properties,
                    "snmpCommunity"),
            Protocol =
                FirstNonEmpty(
                    Attr(
                        properties,
                        "protocol"),
                    "PROTOCOL_RAWTCP_TYPE"),
            PortNumber =
                FirstNonEmpty(
                    Attr(
                        properties,
                        "portNumber"),
                    "9100"),
            DoubleSpool =
                IsTrue(
                    Attr(
                        properties,
                        "doubleSpool")),
            SnmpEnabled =
                IsTrue(
                    Attr(
                        properties,
                        "snmpEnabled")),
            SnmpDevIndex =
                FirstNonEmpty(
                    Attr(
                        properties,
                        "snmpDevIndex"),
                    "1"),
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
        };
    }

    private static void UpdatePrinter(
        XElement printer,
        GppPrinterItemInfo item)
    {
        var kind =
            NormalizeKind(
                item.PrinterKind);

        var display =
            string.IsNullOrWhiteSpace(
                item.DisplayName)
                ? item.TargetDisplay
                : item.DisplayName.Trim();

        SetAttr(
            printer,
            "clsid",
            ItemClsid(
                item.PrinterKind));

        SetAttr(
            printer,
            "name",
            display);

        SetAttr(
            printer,
            "status",
            display);

        SetAttr(
            printer,
            "image",
            "2");

        SetAttr(
            printer,
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
            printer,
            "uid",
            item.Uid);

        if (string.IsNullOrWhiteSpace(
                item.Description))
        {
            printer.SetAttributeValue(
                "desc",
                null);
        }
        else
        {
            SetAttr(
                printer,
                "desc",
                item.Description);
        }

        SetOptionalBool(
            printer,
            "disabled",
            item.Disabled);

        SetOptionalBool(
            printer,
            "bypassErrors",
            item.BypassErrors);

        SetOptionalBool(
            printer,
            "removePolicy",
            item.RemoveWhenNoLongerApplied);

        SetOptionalBool(
            printer,
            "userContext",
            item.RunInUserContext);

        var properties =
            printer.Elements()
                .FirstOrDefault(
                    element =>
                        element.Name.LocalName ==
                        "Properties");

        if (properties is null)
        {
            properties =
                new XElement(
                    "Properties");

            printer.AddFirst(
                properties);
        }

        // Rebuild only documented printer properties so a preference cannot
        // accidentally carry attributes belonging to another printer type.
        properties.RemoveAttributes();

        SetAttr(
            properties,
            "action",
            NormalizeAction(
                item.Action));

        if (kind == "SHARED")
        {
            SetAttr(
                properties,
                "comment",
                item.Comment);
            SetAttr(
                properties,
                "path",
                item.Path.Trim());
            SetAttr(
                properties,
                "location",
                item.Location);
            SetAttr(
                properties,
                "default",
                Bool(
                    item.DefaultPrinter));
            SetAttr(
                properties,
                "skipLocal",
                Bool(
                    item.SkipLocal));
            SetAttr(
                properties,
                "deleteAll",
                Bool(
                    item.DeleteAll));
            SetAttr(
                properties,
                "persistent",
                Bool(
                    item.Persistent));
            SetAttr(
                properties,
                "deleteMaps",
                Bool(
                    item.DeleteMaps));
            SetAttr(
                properties,
                "port",
                item.Port.Trim());

            if (string.IsNullOrWhiteSpace(
                    item.UserName))
            {
                properties.SetAttributeValue(
                    "username",
                    null);
            }
            else
            {
                SetAttr(
                    properties,
                    "username",
                    item.UserName.Trim());
            }

            if (item.ClearStoredCredential)
            {
                properties.SetAttributeValue(
                    "cpassword",
                    null);
            }
            else if (!string.IsNullOrWhiteSpace(
                         item.OpaqueCredential))
            {
                SetAttr(
                    properties,
                    "cpassword",
                    item.OpaqueCredential);
            }
            else
            {
                properties.SetAttributeValue(
                    "cpassword",
                    null);
            }
        }
        else if (kind == "PORT")
        {
            SetAttr(
                properties,
                "ipAddress",
                item.IpAddress.Trim());
            SetAttr(
                properties,
                "useDNS",
                Bool(
                    item.UseDns));
            SetAttr(
                properties,
                "localName",
                item.LocalName.Trim());
            SetAttr(
                properties,
                "path",
                item.Path.Trim());
            SetAttr(
                properties,
                "default",
                Bool(
                    item.DefaultPrinter));
            SetAttr(
                properties,
                "skipLocal",
                Bool(
                    item.SkipLocal));
            SetAttr(
                properties,
                "deleteAll",
                Bool(
                    item.DeleteAll));
            SetAttr(
                properties,
                "location",
                item.Location);
            SetAttr(
                properties,
                "comment",
                item.Comment);
            SetAttr(
                properties,
                "lprQueue",
                item.LprQueue.Trim());
            SetAttr(
                properties,
                "snmpCommunity",
                item.SnmpCommunity.Trim());
            SetAttr(
                properties,
                "protocol",
                NormalizeProtocol(
                    item.Protocol));
            SetAttr(
                properties,
                "portNumber",
                NormalizePositiveInteger(
                    item.PortNumber,
                    "TCP/IP printer port number"));
            SetAttr(
                properties,
                "doubleSpool",
                Bool(
                    item.DoubleSpool));
            SetAttr(
                properties,
                "snmpEnabled",
                Bool(
                    item.SnmpEnabled));
            SetAttr(
                properties,
                "snmpDevIndex",
                NormalizePositiveInteger(
                    item.SnmpDevIndex,
                    "SNMP device index"));
        }
        else
        {
            SetAttr(
                properties,
                "name",
                item.PrinterName.Trim());
            SetAttr(
                properties,
                "port",
                item.Port.Trim());
            SetAttr(
                properties,
                "path",
                item.Path.Trim());
            SetAttr(
                properties,
                "default",
                Bool(
                    item.DefaultPrinter));
            SetAttr(
                properties,
                "deleteAll",
                Bool(
                    item.DeleteAll));
            SetAttr(
                properties,
                "location",
                item.Location);
            SetAttr(
                properties,
                "comment",
                item.Comment);
        }

        ApplyFilters(
            printer,
            item.FiltersXml);
    }

    private static void Validate(
        GppPrinterItemInfo item)
    {
        var kind =
            NormalizeKind(
                item.PrinterKind);

        var action =
            NormalizeAction(
                item.Action);

        if (kind == "SHARED")
        {
            if (action != "D" &&
                string.IsNullOrWhiteSpace(
                    item.Path))
            {
                throw new InvalidOperationException(
                    "Shared printer path cannot be empty.");
            }

            if (!string.IsNullOrWhiteSpace(
                    item.Path) &&
                !item.Path.StartsWith(
                    @"\\",
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Shared printer path must be a UNC path beginning with \\.");
            }
        }
        else if (kind == "PORT")
        {
            if (action != "D" &&
                string.IsNullOrWhiteSpace(
                    item.IpAddress))
            {
                throw new InvalidOperationException(
                    "TCP/IP printer IP address or DNS name cannot be empty.");
            }

            if (action != "D" &&
                string.IsNullOrWhiteSpace(
                    item.Path))
            {
                throw new InvalidOperationException(
                    "TCP/IP printer driver source path cannot be empty.");
            }

            if (!string.IsNullOrWhiteSpace(
                    item.Path) &&
                !item.Path.StartsWith(
                    @"\\",
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "TCP/IP printer driver source must be a UNC printer path beginning with \\.");
            }

            _ =
                NormalizePositiveInteger(
                    item.PortNumber,
                    "TCP/IP printer port number");

            _ =
                NormalizePositiveInteger(
                    item.SnmpDevIndex,
                    "SNMP device index");

            var protocol =
                NormalizeProtocol(
                    item.Protocol);

            if (protocol == "PROTOCOL_LPR_TYPE" &&
                item.PortNumber.Trim() != "515")
            {
                throw new InvalidOperationException(
                    "LPR protocol requires TCP port 515.");
            }
        }
        else
        {
            if (string.IsNullOrWhiteSpace(
                    item.PrinterName))
            {
                throw new InvalidOperationException(
                    "Local printer name cannot be empty.");
            }

            if (action != "D" &&
                string.IsNullOrWhiteSpace(
                    item.Port))
            {
                throw new InvalidOperationException(
                    "Local printer port cannot be empty.");
            }

            if (action != "D" &&
                string.IsNullOrWhiteSpace(
                    item.Path))
            {
                throw new InvalidOperationException(
                    "Local printer driver source path cannot be empty.");
            }

            if (!string.IsNullOrWhiteSpace(
                    item.Path) &&
                !item.Path.StartsWith(
                    @"\\",
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Local printer driver source must be a UNC printer path beginning with \\.");
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

    private static XElement? FindItem(
        IReadOnlyList<XElement> items,
        GppPrinterItemInfo item)
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
            item.Add(
                filters);
        else
            existing.ReplaceWith(
                filters);
    }

    private GppDocumentTypeInfo GetPrinterType() =>
        _documents.GetKnownTypes().First(
            type =>
                type.Name.Equals(
                    "Printers",
                    StringComparison.OrdinalIgnoreCase));

    private static string NormalizeScope(
        string scope) =>
        scope.Equals(
            "User",
            StringComparison.OrdinalIgnoreCase)
            ? "User"
            : "Computer";

    private static string NormalizeKind(
        string value) =>
        value.Trim()
            .ToUpperInvariant() switch
        {
            "TCP/IP" or "TCPIP" or "PORT" => "PORT",
            "LOCAL" => "LOCAL",
            _ => "SHARED"
        };

    private static string ElementName(
        string printerKind) =>
        NormalizeKind(
            printerKind) switch
        {
            "PORT" => "PortPrinter",
            "LOCAL" => "LocalPrinter",
            _ => "SharedPrinter"
        };

    private static string ItemClsid(
        string printerKind) =>
        NormalizeKind(
            printerKind) switch
        {
            "PORT" => PortPrinterClsid,
            "LOCAL" => LocalPrinterClsid,
            _ => SharedPrinterClsid
        };

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

    private static string NormalizeProtocol(
        string protocol) =>
        protocol.Trim()
            .ToUpperInvariant() switch
        {
            "PROTOCOL_LPR_TYPE" or "LPR" =>
                "PROTOCOL_LPR_TYPE",
            _ =>
                "PROTOCOL_RAWTCP_TYPE"
        };

    private static string NormalizePositiveInteger(
        string value,
        string fieldName)
    {
        var text =
            value.Trim();

        if (!int.TryParse(
                text,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var number) ||
            number < 0)
        {
            throw new InvalidOperationException(
                $"{fieldName} must be a non-negative integer.");
        }

        return number.ToString(
            CultureInfo.InvariantCulture);
    }

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

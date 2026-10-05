using System.Globalization;
using System.Xml.Linq;
using GPOSettingsExplorer.Models;

namespace GPOSettingsExplorer.Services;

public sealed class GppServicePreferenceService
{
    private const string RootClsid = "{2CFB484A-4E96-4B5D-A0B6-093D2F91E6AE}";
    private const string ItemClsid = "{AB6F0B67-341F-4E51-92F9-005FBFBA1A43}";

    private readonly GppDocumentService _documents;

    public GppServicePreferenceService(GppDocumentService documents)
    {
        _documents = documents;
    }

    public IReadOnlyList<GppServiceItemInfo> Load(
        IEnumerable<GpoInfo> gpos,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var result = new List<GppServiceItemInfo>();
        var list = gpos.ToList();
        var type = GetServiceType();

        for (var index = 0; index < list.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var gpo = list[index];
            progress?.Report($"Services {index + 1}/{list.Count}: {gpo.DisplayName}");

            var target = _documents.BuildTarget(gpo, "Computer", type);
            if (!File.Exists(target.XmlPath))
                continue;

            try
            {
                var document = XDocument.Load(
                    target.XmlPath,
                    LoadOptions.PreserveWhitespace);

                var ordinal = 0;

                foreach (var service in document
                             .Descendants()
                             .Where(element => element.Name.LocalName == "NTService"))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    ordinal++;

                    var properties = service.Elements()
                        .FirstOrDefault(element => element.Name.LocalName == "Properties");

                    if (properties is null)
                        continue;

                    var filters = service.Elements()
                        .FirstOrDefault(element => element.Name.LocalName == "Filters");

                    var serviceName = Attr(properties, "serviceName");

                    result.Add(new GppServiceItemInfo
                    {
                        GpoId = gpo.Id,
                        GpoName = gpo.DisplayName,
                        DomainName = gpo.DomainName,
                        XmlPath = target.XmlPath,
                        Uid = Attr(service, "uid"),
                        Ordinal = ordinal,
                        DisplayName = FirstNonEmpty(
                            Attr(service, "name"),
                            Attr(service, "status"),
                            serviceName),
                        ServiceName = serviceName,
                        ServiceAction = NormalizeServiceAction(Attr(properties, "serviceAction")),
                        StartupType = NormalizeStartupType(Attr(properties, "startupType")),
                        Timeout = FirstNonEmpty(Attr(properties, "timeout"), "30"),
                        AccountName = Attr(properties, "accountName"),
                        InteractWithDesktop = IsTrue(Attr(properties, "interact")),
                        FirstFailure = NormalizeFailureAction(Attr(properties, "firstFailure")),
                        SecondFailure = NormalizeFailureAction(Attr(properties, "secondFailure")),
                        ThirdFailure = NormalizeFailureAction(Attr(properties, "thirdFailure")),
                        ResetFailCountDelay = FirstNonEmpty(Attr(properties, "resetFailCountDelay"), "0"),
                        RestartServiceDelay = FirstNonEmpty(Attr(properties, "restartServiceDelay"), "0"),
                        RestartComputerDelay = FirstNonEmpty(Attr(properties, "restartComputerDelay"), "0"),
                        RestartMessage = Attr(properties, "restartMessage"),
                        Program = Attr(properties, "program"),
                        Arguments = Attr(properties, "args"),
                        AppendArguments = Attr(properties, "append"),
                        Disabled = IsTrue(Attr(service, "disabled")),
                        BypassErrors = IsTrue(Attr(service, "bypassErrors")),
                        RemoveWhenNoLongerApplied = IsTrue(Attr(service, "removePolicy")),
                        RunInUserContext = IsTrue(Attr(service, "userContext")),
                        FiltersXml = filters?.ToString(SaveOptions.DisableFormatting) ?? string.Empty,
                        OpaqueCredential = FirstNonEmpty(
                            Attr(properties, "cPassword"),
                            Attr(properties, "cpassword"))
                    });
                }
            }
            catch
            {
                // Malformed documents remain repairable through the raw GPP XML editor.
            }
        }

        return result
            .OrderBy(item => item.GpoName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(item => item.ServiceName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(item => item.Ordinal)
            .ToArray();
    }

    public GppServiceItemInfo CreateNew(GpoInfo gpo) =>
        new()
        {
            GpoId = gpo.Id,
            GpoName = gpo.DisplayName,
            DomainName = gpo.DomainName,
            XmlPath = _documents.BuildTarget(
                gpo,
                "Computer",
                GetServiceType()).XmlPath,
            Uid = Guid.NewGuid().ToString("B").ToUpperInvariant(),
            DisplayName = "New Service Preference",
            ServiceAction = "NOCHANGE",
            StartupType = "NOCHANGE",
            Timeout = "30",
            FirstFailure = "NOACTION",
            SecondFailure = "NOACTION",
            ThirdFailure = "NOACTION",
            ResetFailCountDelay = "0",
            RestartServiceDelay = "0",
            RestartComputerDelay = "0"
        };

    public void Save(
        GpoInfo gpo,
        string domainDistinguishedName,
        GppServiceItemInfo item)
    {
        Validate(item);

        var type = GetServiceType();
        var target = _documents.BuildTarget(gpo, "Computer", type);

        XDocument document;

        if (File.Exists(target.XmlPath))
        {
            document = XDocument.Load(
                target.XmlPath,
                LoadOptions.PreserveWhitespace);

            if (document.Root is null ||
                !document.Root.Name.LocalName.Equals(
                    "NTServices",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "The existing Services.xml root element is not <NTServices>. Use the raw GPP XML editor to repair it.");
            }
        }
        else
        {
            document = new XDocument(
                new XDeclaration("1.0", "utf-8", null),
                new XElement(
                    "NTServices",
                    new XAttribute("clsid", RootClsid)));
        }

        var services = document
            .Descendants()
            .Where(element => element.Name.LocalName == "NTService")
            .ToArray();

        var service = FindService(services, item);

        if (service is null)
        {
            service = new XElement(
                "NTService",
                new XAttribute("clsid", ItemClsid));

            document.Root!.Add(service);
        }

        UpdateService(service, item);

        _documents.SaveXml(
            gpo,
            domainDistinguishedName,
            target,
            document.ToString());
    }

    public void Delete(
        GpoInfo gpo,
        string domainDistinguishedName,
        GppServiceItemInfo item)
    {
        var type = GetServiceType();
        var target = _documents.BuildTarget(gpo, "Computer", type);

        if (!File.Exists(target.XmlPath))
            return;

        var document = XDocument.Load(
            target.XmlPath,
            LoadOptions.PreserveWhitespace);

        var services = document
            .Descendants()
            .Where(element => element.Name.LocalName == "NTService")
            .ToArray();

        var service = FindService(services, item)
            ?? throw new InvalidOperationException(
                "The selected Services preference no longer exists. Refresh the list.");

        service.Remove();

        if (!document.Descendants().Any(
                element => element.Name.LocalName == "NTService"))
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

    private static void UpdateService(
        XElement service,
        GppServiceItemInfo item)
    {
        var displayName = string.IsNullOrWhiteSpace(item.DisplayName)
            ? item.ServiceName.Trim()
            : item.DisplayName.Trim();

        SetAttr(service, "clsid", ItemClsid);
        SetAttr(service, "name", displayName);
        SetAttr(service, "status", displayName);
        SetAttr(service, "image", "0");
        SetAttr(
            service,
            "changed",
            DateTime.UtcNow.ToString(
                "yyyy-MM-dd HH:mm:ss",
                CultureInfo.InvariantCulture));

        if (string.IsNullOrWhiteSpace(item.Uid))
            item.Uid = Guid.NewGuid().ToString("B").ToUpperInvariant();

        SetAttr(service, "uid", item.Uid);

        SetOptionalBool(service, "disabled", item.Disabled);
        SetOptionalBool(service, "bypassErrors", item.BypassErrors);
        SetOptionalBool(service, "removePolicy", item.RemoveWhenNoLongerApplied);
        SetOptionalBool(service, "userContext", item.RunInUserContext);

        var properties = service.Elements()
            .FirstOrDefault(element => element.Name.LocalName == "Properties");

        if (properties is null)
        {
            properties = new XElement("Properties");
            service.AddFirst(properties);
        }

        SetAttr(
            properties,
            "serviceAction",
            NormalizeServiceAction(item.ServiceAction));
        SetAttr(
            properties,
            "startupType",
            NormalizeStartupType(item.StartupType));
        SetAttr(properties, "serviceName", item.ServiceName.Trim());
        SetAttr(properties, "timeout", item.Timeout.Trim());
        SetAttr(properties, "accountName", item.AccountName.Trim());
        SetAttr(properties, "interact", item.InteractWithDesktop ? "1" : "0");
        SetAttr(properties, "firstFailure", NormalizeFailureAction(item.FirstFailure));
        SetAttr(properties, "secondFailure", NormalizeFailureAction(item.SecondFailure));
        SetAttr(properties, "thirdFailure", NormalizeFailureAction(item.ThirdFailure));
        SetAttr(properties, "resetFailCountDelay", item.ResetFailCountDelay.Trim());
        SetAttr(properties, "restartServiceDelay", item.RestartServiceDelay.Trim());
        SetAttr(properties, "restartComputerDelay", item.RestartComputerDelay.Trim());
        SetAttr(properties, "restartMessage", item.RestartMessage ?? string.Empty);
        SetAttr(properties, "program", item.Program.Trim());
        SetAttr(properties, "args", item.Arguments ?? string.Empty);
        SetAttr(properties, "append", item.AppendArguments ?? string.Empty);

        if (item.ClearStoredCredential)
        {
            properties.SetAttributeValue("cPassword", null);
            properties.SetAttributeValue("cpassword", null);
        }
        else if (!string.IsNullOrWhiteSpace(item.OpaqueCredential))
        {
            SetAttr(properties, "cPassword", item.OpaqueCredential);
            properties.SetAttributeValue("cpassword", null);
        }

        ReplaceFilters(service, item.FiltersXml);
    }

    private static XElement? FindService(
        IReadOnlyList<XElement> services,
        GppServiceItemInfo item)
    {
        if (!string.IsNullOrWhiteSpace(item.Uid))
        {
            var byUid = services.FirstOrDefault(element =>
                Attr(element, "uid").Equals(
                    item.Uid,
                    StringComparison.OrdinalIgnoreCase));

            if (byUid is not null)
                return byUid;
        }

        if (item.Ordinal > 0 && item.Ordinal <= services.Count)
            return services[item.Ordinal - 1];

        return null;
    }

    private static void Validate(GppServiceItemInfo item)
    {
        if (string.IsNullOrWhiteSpace(item.ServiceName))
            throw new InvalidOperationException("Service name cannot be empty.");

        ValidateUnsignedNumber(item.Timeout, "Timeout");
        ValidateUnsignedNumber(item.ResetFailCountDelay, "Reset fail count delay");
        ValidateUnsignedNumber(item.RestartServiceDelay, "Restart service delay");
        ValidateUnsignedNumber(item.RestartComputerDelay, "Restart computer delay");

        _ = NormalizeServiceAction(item.ServiceAction);
        _ = NormalizeStartupType(item.StartupType);
        _ = NormalizeFailureAction(item.FirstFailure);
        _ = NormalizeFailureAction(item.SecondFailure);
        _ = NormalizeFailureAction(item.ThirdFailure);
    }

    private static void ValidateUnsignedNumber(
        string value,
        string fieldName)
    {
        if (!uint.TryParse(
                value.Trim(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out _))
        {
            throw new InvalidOperationException(
                $"{fieldName} must be a non-negative integer.");
        }
    }

    private static void ReplaceFilters(
        XElement item,
        string filtersXml)
    {
        var existing = item.Elements()
            .FirstOrDefault(element => element.Name.LocalName == "Filters");

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

    private GppDocumentTypeInfo GetServiceType() =>
        _documents.GetKnownTypes().First(type =>
            type.Name.Equals("Services", StringComparison.OrdinalIgnoreCase));

    private static string NormalizeServiceAction(string value) =>
        value.Trim().ToUpperInvariant() switch
        {
            "START" => "START",
            "STOP" => "STOP",
            "RESTART" => "RESTART",
            "RESTART_IF_REQUIRED" => "RESTART_IF_REQUIRED",
            _ => "NOCHANGE"
        };

    private static string NormalizeStartupType(string value) =>
        value.Trim().ToUpperInvariant() switch
        {
            "AUTOMATIC" => "AUTOMATIC",
            "BOOT" => "BOOT",
            "DISABLED" => "DISABLED",
            "MANUAL" => "MANUAL",
            "SYSTEM" => "SYSTEM",
            _ => "NOCHANGE"
        };

    private static string NormalizeFailureAction(string value) =>
        value.Trim().ToUpperInvariant() switch
        {
            "START" => "START",
            "STOP" => "STOP",
            "RESTART" => "RESTART",
            "RESTART_IF_REQUIRED" => "RESTART_IF_REQUIRED",
            _ => "NOACTION"
        };

    private static string Attr(XElement element, string name) =>
        element.Attributes()
            .FirstOrDefault(attribute =>
                attribute.Name.LocalName.Equals(
                    name,
                    StringComparison.OrdinalIgnoreCase))?
            .Value ?? string.Empty;

    private static bool IsTrue(string value) =>
        value == "1" ||
        value.Equals("true", StringComparison.OrdinalIgnoreCase);

    private static string FirstNonEmpty(params string[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))
        ?? string.Empty;

    private static void SetAttr(
        XElement element,
        string name,
        string value) =>
        element.SetAttributeValue(name, value ?? string.Empty);

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

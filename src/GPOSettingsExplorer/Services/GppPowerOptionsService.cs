using System.Globalization;
using System.Xml.Linq;
using GPOSettingsExplorer.Models;

namespace GPOSettingsExplorer.Services;

public sealed class GppPowerOptionsService
{
    private const string RootClsid =
        "{7B0F9381-C3B8-4525-8167-87349B671D94}";

    private const string PowerV2Clsid =
        "{2B130A62-FC14-4572-91C3-5435C6A0C3FC}";

    private readonly GppDocumentService _documents;

    public GppPowerOptionsService(GppDocumentService documents)
    {
        _documents = documents;
    }

    public IReadOnlyList<GppPowerOptionItemInfo> Load(
        IEnumerable<GpoInfo> gpos,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var result = new List<GppPowerOptionItemInfo>();
        var list = gpos.ToList();
        var type = GetPowerOptionsType();

        for (var index = 0; index < list.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var gpo = list[index];
            progress?.Report(
                $"Power Options {index + 1}/{list.Count}: {gpo.DisplayName}");

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

                    foreach (var element in document.Root?.Elements()
                                 ?? Enumerable.Empty<XElement>())
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        if (element.Name.LocalName
                            is not ("GlobalPowerOptionsV2" or "GlobalPowerOptions" or "PowerScheme"))
                        {
                            continue;
                        }

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
                    // Malformed PowerOptions.xml remains accessible in raw GPP XML mode.
                }
            }
        }

        return result
            .OrderBy(item => item.GpoName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(item => item.Scope, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.KindDisplay, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(item => item.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(item => item.Ordinal)
            .ToArray();
    }

    public GppPowerOptionItemInfo CreateNew(
        GpoInfo gpo,
        string scope)
    {
        var normalizedScope = NormalizeScope(scope);

        return new GppPowerOptionItemInfo
        {
            GpoId = gpo.Id,
            GpoName = gpo.DisplayName,
            DomainName = gpo.DomainName,
            Scope = normalizedScope,
            XmlPath = _documents.BuildTarget(
                gpo,
                normalizedScope,
                GetPowerOptionsType()).XmlPath,
            Uid = Guid.NewGuid().ToString("B").ToUpperInvariant(),
            ItemKind = "GlobalPowerOptionsV2",
            DisplayName = "Power Plan",
            Action = "U",
            RunInUserContext = normalizedScope.Equals(
                "User",
                StringComparison.OrdinalIgnoreCase)
        };
    }

    public void Save(
        GpoInfo gpo,
        string domainDistinguishedName,
        GppPowerOptionItemInfo item)
    {
        Validate(item);

        if (!item.SupportsStructuredEditing)
        {
            throw new InvalidOperationException(
                "Legacy Power Options items are read-only in the structured editor. Use raw GPP XML editing.");
        }

        var target = _documents.BuildTarget(
            gpo,
            item.Scope,
            GetPowerOptionsType());

        XDocument document;

        if (File.Exists(target.XmlPath))
        {
            document = GppXmlCacheService.Load(
                target.XmlPath,
                LoadOptions.PreserveWhitespace);

            if (document.Root is null ||
                !document.Root.Name.LocalName.Equals(
                    "PowerOptions",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "The existing PowerOptions.xml root is not <PowerOptions>. Use raw GPP XML editing to repair it.");
            }
        }
        else
        {
            document = new XDocument(
                new XDeclaration("1.0", "utf-8", null),
                new XElement(
                    "PowerOptions",
                    new XAttribute("clsid", RootClsid)));
        }

        var powerItem = FindItem(document, item);

        if (powerItem is null)
        {
            powerItem = new XElement(
                "GlobalPowerOptionsV2",
                new XAttribute("clsid", PowerV2Clsid));

            document.Root!.Add(powerItem);
        }

        UpdateItem(powerItem, item);

        _documents.SaveXml(
            gpo,
            domainDistinguishedName,
            target,
            document.ToString());
    }

    public void Delete(
        GpoInfo gpo,
        string domainDistinguishedName,
        GppPowerOptionItemInfo item)
    {
        var target = _documents.BuildTarget(
            gpo,
            item.Scope,
            GetPowerOptionsType());

        if (!File.Exists(target.XmlPath))
            return;

        var document = GppXmlCacheService.Load(
            target.XmlPath,
            LoadOptions.PreserveWhitespace);

        var selected = FindItem(document, item)
            ?? throw new InvalidOperationException(
                "The selected Power Options preference no longer exists. Refresh the list.");

        selected.Remove();

        if (!(document.Root?.Elements()
                .Any(element =>
                    element.Name.LocalName
                    is "GlobalPowerOptionsV2" or "GlobalPowerOptions" or "PowerScheme")
              ?? false))
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
        GppPowerOptionItemInfo source,
        GppPowerOptionItemInfo target)
    {
        target.ItemKind = source.ItemKind;
        target.DisplayName = source.DisplayName;
        target.Description = source.Description;
        target.Action = source.Action;
        target.PlanGuid = source.PlanGuid;
        target.SetAsDefault = source.SetAsDefault;
        target.RequireWakePasswordAc = source.RequireWakePasswordAc;
        target.RequireWakePasswordDc = source.RequireWakePasswordDc;
        target.TurnOffHardDiskAc = source.TurnOffHardDiskAc;
        target.TurnOffHardDiskDc = source.TurnOffHardDiskDc;
        target.SleepAfterAc = source.SleepAfterAc;
        target.SleepAfterDc = source.SleepAfterDc;
        target.AllowHybridSleepAc = source.AllowHybridSleepAc;
        target.AllowHybridSleepDc = source.AllowHybridSleepDc;
        target.HibernateAfterAc = source.HibernateAfterAc;
        target.HibernateAfterDc = source.HibernateAfterDc;
        target.LidCloseAc = source.LidCloseAc;
        target.LidCloseDc = source.LidCloseDc;
        target.PowerButtonAc = source.PowerButtonAc;
        target.PowerButtonDc = source.PowerButtonDc;
        target.StartMenuPowerAc = source.StartMenuPowerAc;
        target.StartMenuPowerDc = source.StartMenuPowerDc;
        target.LinkPowerManagementAc = source.LinkPowerManagementAc;
        target.LinkPowerManagementDc = source.LinkPowerManagementDc;
        target.ProcessorMinAc = source.ProcessorMinAc;
        target.ProcessorMinDc = source.ProcessorMinDc;
        target.ProcessorMaxAc = source.ProcessorMaxAc;
        target.ProcessorMaxDc = source.ProcessorMaxDc;
        target.DisplayOffAc = source.DisplayOffAc;
        target.DisplayOffDc = source.DisplayOffDc;
        target.AdaptiveDisplayAc = source.AdaptiveDisplayAc;
        target.AdaptiveDisplayDc = source.AdaptiveDisplayDc;
        target.CriticalBatteryActionAc = source.CriticalBatteryActionAc;
        target.CriticalBatteryActionDc = source.CriticalBatteryActionDc;
        target.LowBatteryLevelAc = source.LowBatteryLevelAc;
        target.LowBatteryLevelDc = source.LowBatteryLevelDc;
        target.CriticalBatteryLevelAc = source.CriticalBatteryLevelAc;
        target.CriticalBatteryLevelDc = source.CriticalBatteryLevelDc;
        target.LowBatteryNotificationAc = source.LowBatteryNotificationAc;
        target.LowBatteryNotificationDc = source.LowBatteryNotificationDc;
        target.LowBatteryActionAc = source.LowBatteryActionAc;
        target.LowBatteryActionDc = source.LowBatteryActionDc;
        target.Disabled = source.Disabled;
        target.BypassErrors = source.BypassErrors;
        target.RemoveWhenNoLongerApplied = source.RemoveWhenNoLongerApplied;
        target.RunInUserContext = source.RunInUserContext;
        target.FiltersXml = source.FiltersXml;
    }

    private static GppPowerOptionItemInfo ReadItem(
        GpoInfo gpo,
        string scope,
        string xmlPath,
        int ordinal,
        XElement element)
    {
        var properties = element.Elements()
            .FirstOrDefault(child =>
                child.Name.LocalName == "Properties");

        var filters = element.Elements()
            .FirstOrDefault(child =>
                child.Name.LocalName == "Filters");

        var kind = element.Name.LocalName;

        return new GppPowerOptionItemInfo
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
                Attr(properties, "name"),
                kind),
            Description = Attr(element, "desc"),
            Action = FirstNonEmpty(Attr(properties, "action"), "U"),
            PlanGuid = Attr(properties, "nameGuid"),
            SetAsDefault = IsTrue(Attr(properties, "default")),
            RequireWakePasswordAc = FirstNonEmpty(Attr(properties, "requireWakePwdAC"), "NO"),
            RequireWakePasswordDc = FirstNonEmpty(Attr(properties, "requireWakePwdDC"), "NO"),
            TurnOffHardDiskAc = FirstNonEmpty(Attr(properties, "turnOffHDAC"), "0"),
            TurnOffHardDiskDc = FirstNonEmpty(Attr(properties, "turnOffHDDC"), "0"),
            SleepAfterAc = FirstNonEmpty(Attr(properties, "sleepAfterAC"), "0"),
            SleepAfterDc = FirstNonEmpty(Attr(properties, "sleepAfterDC"), "0"),
            AllowHybridSleepAc = FirstNonEmpty(Attr(properties, "allowHybridSleepAC"), "OFF"),
            AllowHybridSleepDc = FirstNonEmpty(Attr(properties, "allowHybridSleepDC"), "OFF"),
            HibernateAfterAc = FirstNonEmpty(Attr(properties, "hibernateAC"), "0"),
            HibernateAfterDc = FirstNonEmpty(Attr(properties, "hibernateDC"), "0"),
            LidCloseAc = FirstNonEmpty(Attr(properties, "lidCloseAC"), "DO_NOTHING"),
            LidCloseDc = FirstNonEmpty(Attr(properties, "lidCloseDC"), "DO_NOTHING"),
            PowerButtonAc = FirstNonEmpty(Attr(properties, "pbActionAC"), "DO_NOTHING"),
            PowerButtonDc = FirstNonEmpty(Attr(properties, "pbActionDC"), "DO_NOTHING"),
            StartMenuPowerAc = FirstNonEmpty(Attr(properties, "strtMenuActionAC"), "DO_NOTHING"),
            StartMenuPowerDc = FirstNonEmpty(Attr(properties, "strtMenuActionDC"), "DO_NOTHING"),
            LinkPowerManagementAc = FirstNonEmpty(Attr(properties, "linkPwrMgmtAC"), "OFF"),
            LinkPowerManagementDc = FirstNonEmpty(Attr(properties, "linkPwrMgmtDC"), "OFF"),
            ProcessorMinAc = FirstNonEmpty(Attr(properties, "procStateMinAC"), "5"),
            ProcessorMinDc = FirstNonEmpty(Attr(properties, "procStateMinDC"), "5"),
            ProcessorMaxAc = FirstNonEmpty(Attr(properties, "procStateMaxAC"), "100"),
            ProcessorMaxDc = FirstNonEmpty(Attr(properties, "procStateMaxDC"), "100"),
            DisplayOffAc = FirstNonEmpty(Attr(properties, "displayOffAC"), "0"),
            DisplayOffDc = FirstNonEmpty(Attr(properties, "displayOffDC"), "0"),
            AdaptiveDisplayAc = FirstNonEmpty(Attr(properties, "adaptiveAC"), "OFF"),
            AdaptiveDisplayDc = FirstNonEmpty(Attr(properties, "adaptiveDC"), "OFF"),
            CriticalBatteryActionAc = FirstNonEmpty(Attr(properties, "critBatActionAC"), "DO_NOTHING"),
            CriticalBatteryActionDc = FirstNonEmpty(Attr(properties, "critBatActionDC"), "HIBERNATE"),
            LowBatteryLevelAc = FirstNonEmpty(Attr(properties, "lowBatteryLvlAC"), "10"),
            LowBatteryLevelDc = FirstNonEmpty(Attr(properties, "lowBatteryLvlDC"), "10"),
            CriticalBatteryLevelAc = FirstNonEmpty(Attr(properties, "critBatteryLvlAC"), "5"),
            CriticalBatteryLevelDc = FirstNonEmpty(Attr(properties, "critBatteryLvlDC"), "5"),
            LowBatteryNotificationAc = FirstNonEmpty(Attr(properties, "lowBatteryNotAC"), "OFF"),
            LowBatteryNotificationDc = FirstNonEmpty(Attr(properties, "lowBatteryNotDC"), "ON"),
            LowBatteryActionAc = FirstNonEmpty(Attr(properties, "lowBatteryActionAC"), "DO_NOTHING"),
            LowBatteryActionDc = FirstNonEmpty(Attr(properties, "lowBatteryActionDC"), "DO_NOTHING"),
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
        GppPowerOptionItemInfo item)
    {
        SetAttr(element, "clsid", PowerV2Clsid);
        SetAttr(element, "name", item.DisplayName.Trim());
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
            .FirstOrDefault(child =>
                child.Name.LocalName == "Properties");

        if (properties is null)
        {
            properties = new XElement("Properties");
            element.AddFirst(properties);
        }

        SetAttr(properties, "action", item.Action.ToUpperInvariant());

        if (item.Action.Equals("C", StringComparison.OrdinalIgnoreCase))
        {
            properties.SetAttributeValue("nameGuid", null);
        }
        else
        {
            SetAttr(properties, "nameGuid", NormalizeGuid(item.PlanGuid));
        }

        SetAttr(properties, "default", item.SetAsDefault ? "1" : "0");
        SetAttr(properties, "requireWakePwdAC", NormalizeYesNo(item.RequireWakePasswordAc));
        SetAttr(properties, "requireWakePwdDC", NormalizeYesNo(item.RequireWakePasswordDc));
        SetAttr(properties, "turnOffHDAC", NormalizeByte(item.TurnOffHardDiskAc, "Hard disk AC"));
        SetAttr(properties, "turnOffHDDC", NormalizeByte(item.TurnOffHardDiskDc, "Hard disk DC"));
        SetAttr(properties, "sleepAfterAC", NormalizeByte(item.SleepAfterAc, "Sleep AC"));
        SetAttr(properties, "sleepAfterDC", NormalizeByte(item.SleepAfterDc, "Sleep DC"));
        SetAttr(properties, "allowHybridSleepAC", NormalizeOnOff(item.AllowHybridSleepAc));
        SetAttr(properties, "allowHybridSleepDC", NormalizeOnOff(item.AllowHybridSleepDc));
        SetAttr(properties, "hibernateAC", NormalizeByte(item.HibernateAfterAc, "Hibernate AC"));
        SetAttr(properties, "hibernateDC", NormalizeByte(item.HibernateAfterDc, "Hibernate DC"));
        SetAttr(properties, "lidCloseAC", NormalizePowerAction(item.LidCloseAc));
        SetAttr(properties, "lidCloseDC", NormalizePowerAction(item.LidCloseDc));
        SetAttr(properties, "pbActionAC", NormalizePowerAction(item.PowerButtonAc));
        SetAttr(properties, "pbActionDC", NormalizePowerAction(item.PowerButtonDc));
        SetAttr(properties, "strtMenuActionAC", NormalizePowerAction(item.StartMenuPowerAc));
        SetAttr(properties, "strtMenuActionDC", NormalizePowerAction(item.StartMenuPowerDc));
        SetAttr(properties, "linkPwrMgmtAC", NormalizeOnOff(item.LinkPowerManagementAc));
        SetAttr(properties, "linkPwrMgmtDC", NormalizeOnOff(item.LinkPowerManagementDc));
        SetAttr(properties, "procStateMinAC", NormalizePercent(item.ProcessorMinAc, "Processor minimum AC"));
        SetAttr(properties, "procStateMinDC", NormalizePercent(item.ProcessorMinDc, "Processor minimum DC"));
        SetAttr(properties, "procStateMaxAC", NormalizePercent(item.ProcessorMaxAc, "Processor maximum AC"));
        SetAttr(properties, "procStateMaxDC", NormalizePercent(item.ProcessorMaxDc, "Processor maximum DC"));
        SetAttr(properties, "displayOffAC", NormalizeByte(item.DisplayOffAc, "Display timeout AC"));
        SetAttr(properties, "displayOffDC", NormalizeByte(item.DisplayOffDc, "Display timeout DC"));
        SetAttr(properties, "adaptiveAC", NormalizeOnOff(item.AdaptiveDisplayAc));
        SetAttr(properties, "adaptiveDC", NormalizeOnOff(item.AdaptiveDisplayDc));
        SetAttr(properties, "critBatActionAC", NormalizePowerAction(item.CriticalBatteryActionAc));
        SetAttr(properties, "critBatActionDC", NormalizePowerAction(item.CriticalBatteryActionDc));
        SetAttr(properties, "lowBatteryLvlAC", NormalizePercent(item.LowBatteryLevelAc, "Low battery AC"));
        SetAttr(properties, "lowBatteryLvlDC", NormalizePercent(item.LowBatteryLevelDc, "Low battery DC"));
        SetAttr(properties, "critBatteryLvlAC", NormalizePercent(item.CriticalBatteryLevelAc, "Critical battery AC"));
        SetAttr(properties, "critBatteryLvlDC", NormalizePercent(item.CriticalBatteryLevelDc, "Critical battery DC"));
        SetAttr(properties, "lowBatteryNotAC", NormalizeOnOff(item.LowBatteryNotificationAc));
        SetAttr(properties, "lowBatteryNotDC", NormalizeOnOff(item.LowBatteryNotificationDc));
        SetAttr(properties, "lowBatteryActionAC", NormalizePowerAction(item.LowBatteryActionAc));
        SetAttr(properties, "lowBatteryActionDC", NormalizePowerAction(item.LowBatteryActionDc));

        var existingFilters = element.Elements()
            .FirstOrDefault(child =>
                child.Name.LocalName == "Filters");

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

            element.Add(filters);
        }
    }

    private static XElement? FindItem(
        XDocument document,
        GppPowerOptionItemInfo item)
    {
        var candidates = document.Root?.Elements()
            .Where(element =>
                element.Name.LocalName
                is "GlobalPowerOptionsV2" or "GlobalPowerOptions" or "PowerScheme")
            .ToArray()
            ?? Array.Empty<XElement>();

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
            Attr(element, "name")
                .Equals(
                    item.DisplayName,
                    StringComparison.CurrentCultureIgnoreCase));
    }

    private GppDocumentTypeInfo GetPowerOptionsType() =>
        _documents.GetKnownTypes()
            .First(type =>
                type.Name.Equals(
                    "Power Options",
                    StringComparison.OrdinalIgnoreCase));

    private static void Validate(GppPowerOptionItemInfo item)
    {
        if (!item.SupportsStructuredEditing)
        {
            throw new InvalidOperationException(
                "Legacy Power Options items are read-only in the structured editor.");
        }

        if (string.IsNullOrWhiteSpace(item.DisplayName))
            throw new InvalidOperationException("Display name cannot be empty.");

        if (item.Action.ToUpperInvariant()
            is not ("C" or "U" or "R" or "D"))
        {
            throw new InvalidOperationException(
                "Invalid Power Options preference action.");
        }

        if (!item.Action.Equals("C", StringComparison.OrdinalIgnoreCase))
        {
            _ = NormalizeGuid(item.PlanGuid);
        }

        foreach (var pair in new[]
        {
            ("Hard disk AC", item.TurnOffHardDiskAc),
            ("Hard disk DC", item.TurnOffHardDiskDc),
            ("Sleep AC", item.SleepAfterAc),
            ("Sleep DC", item.SleepAfterDc),
            ("Hibernate AC", item.HibernateAfterAc),
            ("Hibernate DC", item.HibernateAfterDc),
            ("Display timeout AC", item.DisplayOffAc),
            ("Display timeout DC", item.DisplayOffDc)
        })
        {
            _ = NormalizeByte(pair.Item2, pair.Item1);
        }

        foreach (var pair in new[]
        {
            ("Processor minimum AC", item.ProcessorMinAc),
            ("Processor minimum DC", item.ProcessorMinDc),
            ("Processor maximum AC", item.ProcessorMaxAc),
            ("Processor maximum DC", item.ProcessorMaxDc),
            ("Low battery AC", item.LowBatteryLevelAc),
            ("Low battery DC", item.LowBatteryLevelDc),
            ("Critical battery AC", item.CriticalBatteryLevelAc),
            ("Critical battery DC", item.CriticalBatteryLevelDc)
        })
        {
            _ = NormalizePercent(pair.Item2, pair.Item1);
        }

        if (ParseInt(item.ProcessorMinAc) > ParseInt(item.ProcessorMaxAc) ||
            ParseInt(item.ProcessorMinDc) > ParseInt(item.ProcessorMaxDc))
        {
            throw new InvalidOperationException(
                "Processor minimum state cannot be greater than maximum state.");
        }

        if (ParseInt(item.CriticalBatteryLevelAc) > ParseInt(item.LowBatteryLevelAc) ||
            ParseInt(item.CriticalBatteryLevelDc) > ParseInt(item.LowBatteryLevelDc))
        {
            throw new InvalidOperationException(
                "Critical battery level cannot be greater than low battery level.");
        }
    }

    private static string NormalizeGuid(string value)
    {
        if (!Guid.TryParse(value, out var guid))
        {
            throw new InvalidOperationException(
                "Power plan GUID must be a valid GUID for Update, Replace, or Delete.");
        }

        return guid.ToString("B").ToUpperInvariant();
    }

    private static string NormalizeByte(string value, string label)
    {
        if (!byte.TryParse(
                value,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var number))
        {
            throw new InvalidOperationException(
                $"{label} must be an integer from 0 to 255.");
        }

        return number.ToString(CultureInfo.InvariantCulture);
    }

    private static string NormalizePercent(string value, string label)
    {
        if (!byte.TryParse(
                value,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var number) ||
            number > 100)
        {
            throw new InvalidOperationException(
                $"{label} must be an integer from 0 to 100.");
        }

        return number.ToString(CultureInfo.InvariantCulture);
    }

    private static int ParseInt(string value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
            ? number
            : 0;

    private static string NormalizeYesNo(string value) =>
        value.Equals("YES", StringComparison.OrdinalIgnoreCase)
            ? "YES"
            : "NO";

    private static string NormalizeOnOff(string value) =>
        value.Equals("ON", StringComparison.OrdinalIgnoreCase)
            ? "ON"
            : "OFF";

    private static string NormalizePowerAction(string value)
    {
        var normalized = value.ToUpperInvariant();

        return normalized is "DO_NOTHING" or "SLEEP" or "HIBERNATE" or "SHUT_DOWN"
            ? normalized
            : "DO_NOTHING";
    }

    private static string NormalizeScope(string value) =>
        value.Equals(
            "User",
            StringComparison.OrdinalIgnoreCase)
            ? "User"
            : "Computer";

    private static string Attr(XElement? element, string name) =>
        element?.Attributes()
            .FirstOrDefault(attribute =>
                attribute.Name.LocalName.Equals(
                    name,
                    StringComparison.OrdinalIgnoreCase))?
            .Value.Trim()
        ?? string.Empty;

    private static string FirstNonEmpty(params string[] values) =>
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

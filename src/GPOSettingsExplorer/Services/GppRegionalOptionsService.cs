using System.Globalization;
using System.Xml.Linq;
using GPOSettingsExplorer.Models;

namespace GPOSettingsExplorer.Services;

public sealed class GppRegionalOptionsService
{
    private const string RootClsid =
        "{BDBA23C2-DE02-434E-8D89-13E53CB6710B}";

    private const string ItemClsid =
        "{C126A328-BECF-4ACC-BA8D-C9C7F6B84E49}";

    private readonly GppDocumentService _documents;

    public GppRegionalOptionsService(GppDocumentService documents)
    {
        _documents = documents;
    }

    public IReadOnlyList<GppRegionalOptionsItemInfo> Load(
        IEnumerable<GpoInfo> gpos,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var result = new List<GppRegionalOptionsItemInfo>();
        var list = gpos.ToList();
        var type = GetTypeInfo();

        for (var index = 0; index < list.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var gpo = list[index];

            progress?.Report(
                $"Regional Options {index + 1}/{list.Count}: {gpo.DisplayName}");

            var target = _documents.BuildTarget(gpo, "User", type);
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
                                 child.Name.LocalName == "RegionalOptions"))
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
                        target.XmlPath,
                        ordinal,
                        element,
                        properties,
                        filters));
                }
            }
            catch
            {
                // Malformed RegionalOptions.xml remains accessible through raw GPP XML mode.
            }
        }

        return result
            .OrderBy(item => item.GpoName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(item => item.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(item => item.Ordinal)
            .ToArray();
    }

    public GppRegionalOptionsItemInfo CreateNew(GpoInfo gpo)
    {
        var culture = CultureInfo.CurrentCulture;

        return new GppRegionalOptionsItemInfo
        {
            GpoId = gpo.Id,
            GpoName = gpo.DisplayName,
            DomainName = gpo.DomainName,
            Scope = "User",
            XmlPath = _documents.BuildTarget(
                gpo,
                "User",
                GetTypeInfo()).XmlPath,
            Uid = Guid.NewGuid().ToString("B").ToUpperInvariant(),
            DisplayName = culture.DisplayName,
            LocaleId = culture.LCID.ToString(CultureInfo.InvariantCulture),
            LocaleName = culture.DisplayName,
            NumberDecimalSymbol = culture.NumberFormat.NumberDecimalSeparator,
            NumberDecimals = culture.NumberFormat.NumberDecimalDigits.ToString(CultureInfo.InvariantCulture),
            NumberGroupSymbol = culture.NumberFormat.NumberGroupSeparator,
            NumberGrouping = Grouping(culture.NumberFormat.NumberGroupSizes),
            NumberNegativeSymbol = culture.NumberFormat.NegativeSign,
            ListSeparator = culture.TextInfo.ListSeparator,
            CurrencySymbol = culture.NumberFormat.CurrencySymbol,
            CurrencyDecimalSymbol = culture.NumberFormat.CurrencyDecimalSeparator,
            CurrencyDecimals = culture.NumberFormat.CurrencyDecimalDigits.ToString(CultureInfo.InvariantCulture),
            CurrencyGroupSymbol = culture.NumberFormat.CurrencyGroupSeparator,
            CurrencyGrouping = Grouping(culture.NumberFormat.CurrencyGroupSizes),
            TimeFormat = culture.DateTimeFormat.LongTimePattern,
            TimeSeparator = culture.DateTimeFormat.TimeSeparator,
            AmSymbol = culture.DateTimeFormat.AMDesignator,
            PmSymbol = culture.DateTimeFormat.PMDesignator,
            ShortDateFormat = culture.DateTimeFormat.ShortDatePattern,
            DateSeparator = culture.DateTimeFormat.DateSeparator,
            LongDateFormat = culture.DateTimeFormat.LongDatePattern,
            RunInUserContext = true
        };
    }

    public void Save(
        GpoInfo gpo,
        string domainDistinguishedName,
        GppRegionalOptionsItemInfo item)
    {
        Validate(item);

        var target = _documents.BuildTarget(
            gpo,
            "User",
            GetTypeInfo());

        XDocument document;

        if (File.Exists(target.XmlPath))
        {
            document = GppXmlCacheService.Load(
                target.XmlPath,
                LoadOptions.PreserveWhitespace);

            if (document.Root is null ||
                !document.Root.Name.LocalName.Equals(
                    "Regional",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "The existing RegionalOptions.xml root is not <Regional>. Use raw GPP XML editing to repair it.");
            }
        }
        else
        {
            document = new XDocument(
                new XDeclaration("1.0", "utf-8", null),
                new XElement(
                    "Regional",
                    new XAttribute("clsid", RootClsid)));
        }

        var regional = FindItem(document, item);

        if (regional is null)
        {
            regional = new XElement(
                "RegionalOptions",
                new XAttribute("clsid", ItemClsid));

            document.Root!.Add(regional);
        }

        UpdateItem(regional, item);

        _documents.SaveXml(
            gpo,
            domainDistinguishedName,
            target,
            document.ToString());
    }

    public void Delete(
        GpoInfo gpo,
        string domainDistinguishedName,
        GppRegionalOptionsItemInfo item)
    {
        var target = _documents.BuildTarget(
            gpo,
            "User",
            GetTypeInfo());

        if (!File.Exists(target.XmlPath))
            return;

        var document = GppXmlCacheService.Load(
            target.XmlPath,
            LoadOptions.PreserveWhitespace);

        var selected = FindItem(document, item)
            ?? throw new InvalidOperationException(
                "The selected Regional Options preference no longer exists. Refresh the list.");

        selected.Remove();

        if (!(document.Root?.Elements()
                .Any(element =>
                    element.Name.LocalName == "RegionalOptions") ?? false))
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
        GppRegionalOptionsItemInfo source,
        GppRegionalOptionsItemInfo target)
    {
        target.DisplayName = source.DisplayName;
        target.LocaleId = source.LocaleId;
        target.LocaleName = source.LocaleName;
        target.NumberDecimalSymbol = source.NumberDecimalSymbol;
        target.NumberDecimals = source.NumberDecimals;
        target.NumberGroupSymbol = source.NumberGroupSymbol;
        target.NumberGrouping = source.NumberGrouping;
        target.NumberNegativeSymbol = source.NumberNegativeSymbol;
        target.NumberNegativeFormat = source.NumberNegativeFormat;
        target.NumberLeadingZeros = source.NumberLeadingZeros;
        target.ListSeparator = source.ListSeparator;
        target.MeasurementSystem = source.MeasurementSystem;
        target.CurrencySymbol = source.CurrencySymbol;
        target.CurrencyPositiveFormat = source.CurrencyPositiveFormat;
        target.CurrencyNegativeFormat = source.CurrencyNegativeFormat;
        target.CurrencyDecimalSymbol = source.CurrencyDecimalSymbol;
        target.CurrencyDecimals = source.CurrencyDecimals;
        target.CurrencyGroupSymbol = source.CurrencyGroupSymbol;
        target.CurrencyGrouping = source.CurrencyGrouping;
        target.TimeFormat = source.TimeFormat;
        target.TimeSeparator = source.TimeSeparator;
        target.AmSymbol = source.AmSymbol;
        target.PmSymbol = source.PmSymbol;
        target.InterpretYearMax = source.InterpretYearMax;
        target.ShortDateFormat = source.ShortDateFormat;
        target.DateSeparator = source.DateSeparator;
        target.LongDateFormat = source.LongDateFormat;
        target.Disabled = source.Disabled;
        target.BypassErrors = source.BypassErrors;
        target.RemoveWhenNoLongerApplied = source.RemoveWhenNoLongerApplied;
        target.RunInUserContext = source.RunInUserContext;
        target.FiltersXml = source.FiltersXml;
    }

    private static GppRegionalOptionsItemInfo ReadItem(
        GpoInfo gpo,
        string xmlPath,
        int ordinal,
        XElement element,
        XElement properties,
        XElement? filters)
    {
        return new GppRegionalOptionsItemInfo
        {
            GpoId = gpo.Id,
            GpoName = gpo.DisplayName,
            DomainName = gpo.DomainName,
            Scope = "User",
            XmlPath = xmlPath,
            Uid = Attr(element, "uid"),
            Ordinal = ordinal,
            DisplayName = FirstNonEmpty(
                Attr(element, "name"),
                Attr(properties, "localeName"),
                "Regional Options"),
            LocaleId = Attr(properties, "localeId"),
            LocaleName = Attr(properties, "localeName"),
            NumberDecimalSymbol = Attr(properties, "numDeciSymbol"),
            NumberDecimals = Attr(properties, "numNumDecimals"),
            NumberGroupSymbol = Attr(properties, "numGrpSymbol"),
            NumberGrouping = Attr(properties, "numDigitGrpFmt"),
            NumberNegativeSymbol = Attr(properties, "numNegSymbol"),
            NumberNegativeFormat = Attr(properties, "numNegFormat"),
            NumberLeadingZeros = Attr(properties, "numLeadingZeros"),
            ListSeparator = Attr(properties, "numListSeparator"),
            MeasurementSystem = Attr(properties, "numMeasurement"),
            CurrencySymbol = Attr(properties, "currSymbol"),
            CurrencyPositiveFormat = Attr(properties, "currPosFormat"),
            CurrencyNegativeFormat = Attr(properties, "currNegFormat"),
            CurrencyDecimalSymbol = Attr(properties, "currDeciSymbol"),
            CurrencyDecimals = Attr(properties, "currNumDecimals"),
            CurrencyGroupSymbol = Attr(properties, "currGrpSymbol"),
            CurrencyGrouping = Attr(properties, "currDigitGrpFmt"),
            TimeFormat = Attr(properties, "timeFormat"),
            TimeSeparator = Attr(properties, "timeSeparator"),
            AmSymbol = Attr(properties, "timeAmSymbol"),
            PmSymbol = Attr(properties, "timePmSymbol"),
            InterpretYearMax = Attr(properties, "dateInterpretYearMax"),
            ShortDateFormat = Attr(properties, "dateShortFormat"),
            DateSeparator = Attr(properties, "dateSeparator"),
            LongDateFormat = Attr(properties, "dateLongFormat"),
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
        GppRegionalOptionsItemInfo item)
    {
        SetAttr(element, "clsid", ItemClsid);
        SetAttr(element, "name", FirstNonEmpty(
            item.DisplayName.Trim(),
            item.LocaleName.Trim()));
        SetAttr(element, "image", "0");
        SetAttr(
            element,
            "changed",
            DateTime.UtcNow.ToString(
                "yyyy-MM-dd HH:mm:ss",
                CultureInfo.InvariantCulture));

        if (string.IsNullOrWhiteSpace(item.Uid))
            item.Uid = Guid.NewGuid().ToString("B").ToUpperInvariant();

        SetAttr(element, "uid", item.Uid);
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

        SetAttr(properties, "localeId", item.LocaleId.Trim());
        SetAttr(properties, "localeName", item.LocaleName.Trim());
        SetAttr(properties, "numDeciSymbol", item.NumberDecimalSymbol);
        SetAttr(properties, "numNumDecimals", Numeric(item.NumberDecimals, 0, 9, "Number decimals"));
        SetAttr(properties, "numGrpSymbol", item.NumberGroupSymbol);
        SetAttr(properties, "numDigitGrpFmt", item.NumberGrouping);
        SetAttr(properties, "numNegSymbol", item.NumberNegativeSymbol);
        SetAttr(properties, "numNegFormat", Numeric(item.NumberNegativeFormat, 0, 4, "Negative number format"));
        SetAttr(properties, "numLeadingZeros", Numeric(item.NumberLeadingZeros, 0, 1, "Leading zero"));
        SetAttr(properties, "numListSeparator", item.ListSeparator);
        SetAttr(properties, "numMeasurement", Numeric(item.MeasurementSystem, 0, 1, "Measurement system"));

        SetAttr(properties, "currSymbol", item.CurrencySymbol);
        SetAttr(properties, "currPosFormat", Numeric(item.CurrencyPositiveFormat, 0, 3, "Positive currency format"));
        SetAttr(properties, "currNegFormat", Numeric(item.CurrencyNegativeFormat, 0, 15, "Negative currency format"));
        SetAttr(properties, "currDeciSymbol", item.CurrencyDecimalSymbol);
        SetAttr(properties, "currNumDecimals", Numeric(item.CurrencyDecimals, 0, 9, "Currency decimals"));
        SetAttr(properties, "currGrpSymbol", item.CurrencyGroupSymbol);
        SetAttr(properties, "currDigitGrpFmt", item.CurrencyGrouping);

        SetAttr(properties, "timeFormat", item.TimeFormat);
        SetAttr(properties, "timeSeparator", item.TimeSeparator);
        SetAttr(properties, "timeAmSymbol", item.AmSymbol);
        SetAttr(properties, "timePmSymbol", item.PmSymbol);

        SetAttr(properties, "dateInterpretYearMax",
            Numeric(item.InterpretYearMax, 99, 9999, "Interpret year max"));
        SetAttr(properties, "dateShortFormat", item.ShortDateFormat);
        SetAttr(properties, "dateSeparator", item.DateSeparator);
        SetAttr(properties, "dateLongFormat", item.LongDateFormat);

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
        GppRegionalOptionsItemInfo item)
    {
        var candidates = document
            .Descendants()
            .Where(element =>
                element.Name.LocalName == "RegionalOptions")
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
            Attr(element, "name").Equals(
                item.DisplayName,
                StringComparison.CurrentCultureIgnoreCase));
    }

    private GppDocumentTypeInfo GetTypeInfo() =>
        _documents.GetKnownTypes()
            .First(type =>
                type.Name.Equals(
                    "Regional Options",
                    StringComparison.OrdinalIgnoreCase));

    private static void Validate(
        GppRegionalOptionsItemInfo item)
    {
        if (!item.Scope.Equals(
                "User",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Regional Options preferences are user-policy settings.");
        }

        if (!int.TryParse(
                item.LocaleId,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var localeId) ||
            localeId <= 0)
        {
            throw new InvalidOperationException(
                "Locale ID must be a positive decimal LCID.");
        }

        if (string.IsNullOrWhiteSpace(item.LocaleName))
            throw new InvalidOperationException("Locale name cannot be empty.");

        _ = Numeric(item.NumberDecimals, 0, 9, "Number decimals");
        _ = Numeric(item.NumberNegativeFormat, 0, 4, "Negative number format");
        _ = Numeric(item.NumberLeadingZeros, 0, 1, "Leading zero");
        _ = Numeric(item.MeasurementSystem, 0, 1, "Measurement system");
        _ = Numeric(item.CurrencyPositiveFormat, 0, 3, "Positive currency format");
        _ = Numeric(item.CurrencyNegativeFormat, 0, 15, "Negative currency format");
        _ = Numeric(item.CurrencyDecimals, 0, 9, "Currency decimals");
        _ = Numeric(item.InterpretYearMax, 99, 9999, "Interpret year max");
    }

    private static string Numeric(
        string value,
        int minimum,
        int maximum,
        string label)
    {
        if (!int.TryParse(
                value,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var number) ||
            number < minimum ||
            number > maximum)
        {
            throw new InvalidOperationException(
                $"{label} must be an integer from {minimum} to {maximum}.");
        }

        return number.ToString(CultureInfo.InvariantCulture);
    }

    private static string Grouping(int[] sizes)
    {
        var values = sizes
            .Where(size => size >= 0)
            .Select(size => size.ToString(CultureInfo.InvariantCulture))
            .ToList();

        if (values.Count == 0 || values[^1] != "0")
            values.Add("0");

        return string.Join(";", values);
    }

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

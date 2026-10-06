namespace GPOSettingsExplorer.Models;

public sealed class GppRegionalOptionsItemInfo
{
    public Guid GpoId { get; init; }
    public string GpoName { get; init; } = string.Empty;
    public string DomainName { get; init; } = string.Empty;
    public string Scope { get; set; } = "User";
    public string XmlPath { get; init; } = string.Empty;
    public string Uid { get; set; } = string.Empty;
    public int Ordinal { get; init; }

    public string DisplayName { get; set; } = string.Empty;
    public string LocaleId { get; set; } = string.Empty;
    public string LocaleName { get; set; } = string.Empty;

    public string NumberDecimalSymbol { get; set; } = ".";
    public string NumberDecimals { get; set; } = "2";
    public string NumberGroupSymbol { get; set; } = ",";
    public string NumberGrouping { get; set; } = "3;0";
    public string NumberNegativeSymbol { get; set; } = "-";
    public string NumberNegativeFormat { get; set; } = "1";
    public string NumberLeadingZeros { get; set; } = "1";
    public string ListSeparator { get; set; } = ",";
    public string MeasurementSystem { get; set; } = "1";

    public string CurrencySymbol { get; set; } = "$";
    public string CurrencyPositiveFormat { get; set; } = "0";
    public string CurrencyNegativeFormat { get; set; } = "0";
    public string CurrencyDecimalSymbol { get; set; } = ".";
    public string CurrencyDecimals { get; set; } = "2";
    public string CurrencyGroupSymbol { get; set; } = ",";
    public string CurrencyGrouping { get; set; } = "3;0";

    public string TimeFormat { get; set; } = "HH:mm:ss";
    public string TimeSeparator { get; set; } = ":";
    public string AmSymbol { get; set; } = "AM";
    public string PmSymbol { get; set; } = "PM";

    public string InterpretYearMax { get; set; } = "2029";
    public string ShortDateFormat { get; set; } = "dd/MM/yyyy";
    public string DateSeparator { get; set; } = "/";
    public string LongDateFormat { get; set; } = "dddd, d MMMM yyyy";

    public bool Disabled { get; set; }
    public bool BypassErrors { get; set; }
    public bool RemoveWhenNoLongerApplied { get; set; }
    public bool RunInUserContext { get; set; } = true;
    public string FiltersXml { get; set; } = string.Empty;

    public bool HasFilters => !string.IsNullOrWhiteSpace(FiltersXml);

    public string SearchText =>
        $"{GpoName} {DisplayName} {LocaleId} {LocaleName} {NumberDecimalSymbol} " +
        $"{CurrencySymbol} {TimeFormat} {ShortDateFormat} {LongDateFormat}";
}

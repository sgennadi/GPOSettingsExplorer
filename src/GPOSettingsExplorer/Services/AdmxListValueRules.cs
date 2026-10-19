using System.Globalization;

namespace GPOSettingsExplorer.Services;

/// <summary>
/// ADMX list fields with an explicit valuePrefix generate names such as
/// "Server1", "Server2" (or "1", "2" for an explicitly empty prefix).
/// Do not enumerate/delete arbitrary values in the list registry key:
/// other policies and settings may share it.
/// </summary>
public static class AdmxListValueRules
{
    public static bool IsGeneratedName(string? name, string? valuePrefix)
    {
        if (string.IsNullOrEmpty(name))
            return false;

        var prefix = valuePrefix ?? string.Empty;
        if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return false;

        var suffix = name.AsSpan(prefix.Length);
        if (suffix.IsEmpty || suffix[0] == '0' || suffix.Length > 10)
            return false;

        foreach (var character in suffix)
            if (character is < '0' or > '9')
                return false;

        return int.TryParse(suffix, NumberStyles.None,
                   CultureInfo.InvariantCulture, out var number) &&
               number > 0 &&
               suffix.SequenceEqual(number.ToString(CultureInfo.InvariantCulture));
    }

    public static string NameForIndex(int oneBasedIndex, string? valuePrefix)
    {
        if (oneBasedIndex < 1)
            throw new ArgumentOutOfRangeException(nameof(oneBasedIndex));
        return (valuePrefix ?? string.Empty) +
               oneBasedIndex.ToString(CultureInfo.InvariantCulture);
    }

    public static bool PrefixesMayOverlap(string? first, string? second) =>
        (first ?? string.Empty).StartsWith(second ?? string.Empty,
            StringComparison.OrdinalIgnoreCase) ||
        (second ?? string.Empty).StartsWith(first ?? string.Empty,
            StringComparison.OrdinalIgnoreCase);
}

namespace GPOSettingsExplorer.Services;

/// <summary>
/// Compares *tree section labels*, never policy setting names. GPMC decorates
/// Administrative Templates with the ADMX store description. That suffix is
/// presentation text, not an additional tree node. Do not accept arbitrary
/// prefixes or fuzzy matches in order to avoid opening unrelated MMC sections.
/// </summary>
public static class MmcTreePathMatcher
{
    public const int NotFound = -1;
    public const int Ambiguous = -2;

    public static bool SectionNameMatches(string? displayed, string? requested)
    {
        var actual = Normalize(displayed);
        var expected = Normalize(requested);

        if (actual.Length == 0 || expected.Length == 0)
            return false;

        if (actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
            return true;

        // The observed MMC English UI uses
        // "Administrative Templates: Policy definitions (ADMX files)
        //  retrieved from the central store".
        // Built-in/local template stores can produce a different suffix.
        // Only a recognized *ADMX description* may be ignored; never use a
        // broad StartsWith("Administrative Templates") or a prefix test.
        if (!expected.Equals("Administrative Templates",
                StringComparison.OrdinalIgnoreCase))
            return false;

        const string prefix =
            "Administrative Templates: Policy definitions (ADMX files) retrieved from ";
        if (!actual.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return false;

        var source = actual[prefix.Length..].Trim().TrimEnd('.');
        return source.Equals("the central store", StringComparison.OrdinalIgnoreCase) ||
               source.Equals("the local computer", StringComparison.OrdinalIgnoreCase) ||
               source.Equals("local computer", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Resolve a single child by name, preserving actual MMC node identity.
    /// Exact matches win over the special ADMX alias, but duplicate exact or
    /// duplicate alias matches are always an error, not an arbitrary choice.
    /// </summary>
    public static int FindUniqueIndex(
        IReadOnlyList<string> displayedNames, string expected)
    {
        var normalizedExpected = Normalize(expected);
        var exact = new List<int>();

        for (var i = 0; i < displayedNames.Count; i++)
        {
            if (Normalize(displayedNames[i]).Equals(
                    normalizedExpected, StringComparison.OrdinalIgnoreCase))
                exact.Add(i);
        }

        if (exact.Count > 1)
            return Ambiguous;
        if (exact.Count == 1)
            return exact[0];

        var aliases = new List<int>();
        for (var i = 0; i < displayedNames.Count; i++)
        {
            if (SectionNameMatches(displayedNames[i], expected))
                aliases.Add(i);
        }

        return aliases.Count switch
        {
            0 => NotFound,
            1 => aliases[0],
            _ => Ambiguous
        };
    }

    private static string Normalize(string? name) =>
        string.Join(" ", (name ?? "").Trim().Split(
            (char[]?)null,
            StringSplitOptions.RemoveEmptyEntries |
            StringSplitOptions.TrimEntries));
}

namespace GPOSettingsExplorer.Services;

/// <summary>
/// Resolves an MMC policy label without accepting broad prefix/fuzzy matches.
/// The native list view exposes complete names even when the visible column is narrow.
/// </summary>
public static class MmcPolicyNameMatcher
{
    public static string Normalize(string? text)
    {
        var value = (text ?? string.Empty)
            .Replace("&", string.Empty, StringComparison.Ordinal)
            .Replace("…", "...", StringComparison.Ordinal);

        return string.Join(" ", value.Split((char[]?)null,
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    public static bool Exact(string? actual, string? target)
    {
        var left = Normalize(actual);
        var right = Normalize(target);
        return left.Length != 0 && right.Length != 0 &&
            left.Equals(right, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Native list view labels have priority. A truncated UI label is accepted
    /// only if there is precisely one matching row, and only when the UI
    /// actually contains an ellipsis (rather than a coincidental shared prefix).
    /// -1 means no match or an ambiguous match; never guess for GPO settings.
    /// </summary>
    public static int FindUniqueMatch(IReadOnlyList<string> labels, string target)
    {
        var expected = Normalize(target);
        if (expected.Length == 0)
            return -1;

        var exactIndex = -1;
        for (var i = 0; i < labels.Count; i++)
        {
            if (!Exact(labels[i], expected))
                continue;
            if (exactIndex >= 0)
                return -1;
            exactIndex = i;
        }

        if (exactIndex >= 0)
            return exactIndex;

        var shortenedIndex = -1;
        for (var i = 0; i < labels.Count; i++)
        {
            var label = Normalize(labels[i]);
            if (!label.EndsWith("...", StringComparison.Ordinal))
                continue;

            var prefix = label[..^3].TrimEnd();
            if (prefix.Length < 28 ||
                !expected.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                continue;

            if (shortenedIndex >= 0)
                return -1;
            shortenedIndex = i;
        }

        return shortenedIndex;
    }
}

namespace GPOSettingsExplorer.Services;

public static class ScriptTextSanitizer
{
    public static string StripOuterMarkdownFence(
        string text,
        out bool removed)
    {
        removed = false;

        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        var newline =
            text.Contains(
                "\r\n",
                StringComparison.Ordinal)
                ? "\r\n"
                : text.Contains(
                    '\n')
                    ? "\n"
                    : text.Contains(
                        '\r')
                        ? "\r"
                        : Environment.NewLine;

        var normalized =
            text
                .Replace(
                    "\r\n",
                    "\n",
                    StringComparison.Ordinal)
                .Replace(
                    "\r",
                    "\n",
                    StringComparison.Ordinal);

        var lines =
            normalized
                .Split(
                    '\n',
                    StringSplitOptions.None)
                .ToList();

        var first =
            lines.FindIndex(line =>
                !string.IsNullOrWhiteSpace(
                    line));

        var last =
            lines.FindLastIndex(line =>
                !string.IsNullOrWhiteSpace(
                    line));

        if (first < 0 ||
            last <= first)
        {
            return text;
        }

        var opening =
            lines[first].Trim();

        var closing =
            lines[last].Trim();

        if (!TryGetFenceCharacter(
                opening,
                out var fenceCharacter) ||
            !IsSupportedLanguageFence(
                opening) ||
            !closing.Equals(
                new string(
                    fenceCharacter,
                    3),
                StringComparison.Ordinal))
        {
            return text;
        }

        lines.RemoveAt(
            last);

        lines.RemoveAt(
            first);

        removed = true;

        return string.Join(
            newline,
            lines);
    }

    private static bool TryGetFenceCharacter(
        string line,
        out char fenceCharacter)
    {
        if (line.StartsWith(
                "```",
                StringComparison.Ordinal))
        {
            fenceCharacter = '`';
            return true;
        }

        if (line.StartsWith(
                "~~~",
                StringComparison.Ordinal))
        {
            fenceCharacter = '~';
            return true;
        }

        fenceCharacter = default;
        return false;
    }

    private static bool IsSupportedLanguageFence(
        string line)
    {
        var suffix =
            line[3..]
                .Trim();

        if (suffix.Length == 0)
        {
            return true;
        }

        return suffix.Equals(
                   "bat",
                   StringComparison.OrdinalIgnoreCase) ||
               suffix.Equals(
                   "batch",
                   StringComparison.OrdinalIgnoreCase) ||
               suffix.Equals(
                   "cmd",
                   StringComparison.OrdinalIgnoreCase) ||
               suffix.Equals(
                   "powershell",
                   StringComparison.OrdinalIgnoreCase) ||
               suffix.Equals(
                   "ps1",
                   StringComparison.OrdinalIgnoreCase) ||
               suffix.Equals(
                   "vbs",
                   StringComparison.OrdinalIgnoreCase) ||
               suffix.Equals(
                   "vbscript",
                   StringComparison.OrdinalIgnoreCase) ||
               suffix.Equals(
                   "js",
                   StringComparison.OrdinalIgnoreCase) ||
               suffix.Equals(
                   "javascript",
                   StringComparison.OrdinalIgnoreCase) ||
               suffix.Equals(
                   "wsf",
                   StringComparison.OrdinalIgnoreCase) ||
               suffix.Equals(
                   "hta",
                   StringComparison.OrdinalIgnoreCase);
    }
}

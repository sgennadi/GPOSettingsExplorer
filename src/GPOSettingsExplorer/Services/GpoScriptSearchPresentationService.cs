using GPOSettingsExplorer.Models;

namespace GPOSettingsExplorer.Services;

/// <summary>
/// Separates the file-content-deduplicated search engine from what a user
/// actually sees: one independently editable physical script PER GPO/path.
/// The view can be switched without reading any GPO scripts from SYSVOL again.
/// </summary>
public enum GpoScriptSearchViewMode
{
    UniqueFilesPerGpo,
    EveryMatchingLine
}

public sealed record GpoScriptSearchOccurrence(
    int LineNumber, string MatchType, string Text)
{
    public string LineDisplay => LineNumber > 0
        ? LineNumber.ToString(System.Globalization.CultureInfo.CurrentCulture)
        : "File / path";
}

public sealed record GpoScriptSearchDisplayRow(
    GpoScriptInfo Script,
    int LineNumber,
    string MatchType,
    string Preview,
    IReadOnlyList<GpoScriptSearchOccurrence> Occurrences,
    int ContentLineCount,
    int MetadataMatchCount)
{
    public string GpoName => Script.GpoName;
    public Guid GpoId => Script.GpoId;
    public string GpoHeader => $"GPO: {Script.GpoName}  [{Script.GpoId:D}]";
    public string FileHeader => "File: " + Script.FullPath;
    public string FileName => Script.FileName;
    public string FilePath => Script.FullPath;
    public string Scope => Script.Scope;
    public string Assignment => Script.Assignment;
    public string Type => Script.Type;
    public string LineDisplay => LineNumber > 0
        ? LineNumber.ToString(System.Globalization.CultureInfo.CurrentCulture) : "—";
    public int MatchCount => ContentLineCount + MetadataMatchCount;
    public bool HasMatches => Occurrences.Count > 0;
    public string MatchesLabel => ContentLineCount switch
    {
        0 => MetadataMatchCount == 1 ? "File / path" : $"{MetadataMatchCount:N0} metadata",
        1 => MetadataMatchCount > 0 ? "1 line + file" : "1 line",
        _ => MetadataMatchCount > 0
            ? $"{ContentLineCount:N0} lines + file"
            : $"{ContentLineCount:N0} lines"
    };
    public string LineNumbers => ContentLineCount == 0 ? "File / path" :
        string.Join(", ", Occurrences.Where(item => item.LineNumber > 0)
            .Select(item => item.LineNumber.ToString(
                System.Globalization.CultureInfo.CurrentCulture)));
}

public sealed record GpoScriptSearchPresentation(
    IReadOnlyList<GpoScriptSearchDisplayRow> Rows,
    int GpoCount, int UniqueFileCount, int ContentLineCount,
    int MetadataMatchCount)
{
    public int TotalMatches => ContentLineCount + MetadataMatchCount;
}

/// <summary>
/// The search engine returns one row per matching line of identical content
/// and attaches ALL GPO copies to that hash. This projector expands every
/// hash hit to a distinct (GPO ID, physical full path) before rendering. It
/// does NOT combine independent GPOs, even if two scripts have identical
/// bytes or file names.
/// </summary>
public static class GpoScriptSearchPresentationService
{
    private readonly record struct FileKey(Guid GpoId, string Path);

    private sealed class FileKeyComparer : IEqualityComparer<FileKey>
    {
        public bool Equals(FileKey x, FileKey y) =>
            x.GpoId == y.GpoId &&
            StringComparer.OrdinalIgnoreCase.Equals(x.Path, y.Path);

        public int GetHashCode(FileKey obj) =>
            HashCode.Combine(obj.GpoId,
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Path));
    }

    private sealed class FileMatches
    {
        public GpoScriptInfo Script { get; private set; }
        public readonly List<GpoScriptSearchOccurrence> Occurrences = new();

        public FileMatches(GpoScriptInfo script) => Script = script;

        public void AddScript(GpoScriptInfo script)
        {
            // Multiple startup/logon assignments may reference the SAME
            // physical file; prefer an active named assignment in the UI.
            if ((!Script.Referenced && script.Referenced) ||
                (!Script.Exists && script.Exists))
                Script = script;
        }

        public void AddMatch(GpoScriptSearchOccurrence entry)
        {
            if (Occurrences.Any(existing =>
                    existing.LineNumber == entry.LineNumber &&
                    existing.MatchType.Equals(entry.MatchType,
                        StringComparison.OrdinalIgnoreCase)))
                return;
            Occurrences.Add(entry);
        }
    }

    public static GpoScriptSearchPresentation Project(
        IEnumerable<GpoScriptSearchResult> rawMatches,
        GpoScriptSearchViewMode viewMode)
    {
        ArgumentNullException.ThrowIfNull(rawMatches);
        if (!Enum.IsDefined(viewMode))
            throw new ArgumentOutOfRangeException(nameof(viewMode));

        var byFile = new Dictionary<FileKey, FileMatches>(new FileKeyComparer());

        foreach (var match in rawMatches)
        {
            var occurrence = new GpoScriptSearchOccurrence(
                match.LineNumber, match.MatchType, match.LineText);

            foreach (var script in match.Scripts)
            {
                if (script is null || string.IsNullOrWhiteSpace(script.FullPath))
                    continue;

                var key = new FileKey(script.GpoId, script.FullPath);
                if (!byFile.TryGetValue(key, out var file))
                {
                    file = new FileMatches(script);
                    byFile.Add(key, file);
                }
                else
                    file.AddScript(script);

                file.AddMatch(occurrence);
            }
        }

        var rows = new List<GpoScriptSearchDisplayRow>();
        var contentLines = 0;
        var metadata = 0;

        foreach (var file in byFile.Values
            .OrderBy(x => x.Script.GpoName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(x => x.Script.GpoId)
            .ThenBy(x => x.Script.FileName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(x => x.Script.FullPath, StringComparer.OrdinalIgnoreCase))
        {
            // Content matches are distinct LINE numbers, not substring
            // occurrence counts inside a line. Metadata matches count once.
            var occurrences = file.Occurrences
                .OrderBy(x => x.LineNumber <= 0 ? int.MaxValue : x.LineNumber)
                .ThenBy(x => x.MatchType, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var lineCount = occurrences.Count(x => x.LineNumber > 0);
            var metadataCount = occurrences.Length - lineCount;
            contentLines += lineCount;
            metadata += metadataCount;

            if (viewMode == GpoScriptSearchViewMode.UniqueFilesPerGpo)
            {
                var first = occurrences.FirstOrDefault(x => x.LineNumber > 0)
                    ?? occurrences.First();
                rows.Add(new GpoScriptSearchDisplayRow(
                    file.Script, first.LineNumber,
                    lineCount > 0 && metadataCount > 0 ? "Content + file/path" :
                    lineCount > 0 ? "Content" : "File / path",
                    first.Text, occurrences, lineCount, metadataCount));
            }
            else
            {
                foreach (var match in occurrences)
                    rows.Add(new GpoScriptSearchDisplayRow(
                        file.Script, match.LineNumber, match.MatchType, match.Text,
                        occurrences, lineCount, metadataCount));
            }
        }

        return new GpoScriptSearchPresentation(rows,
            byFile.Keys.Select(key => key.GpoId).Distinct().Count(),
            byFile.Count, contentLines, metadata);
    }
}

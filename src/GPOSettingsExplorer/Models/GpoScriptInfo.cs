namespace GPOSettingsExplorer.Models;

public sealed class GpoScriptInfo
{
    public Guid GpoId { get; init; }
    public string GpoName { get; init; } = string.Empty;
    public string DomainName { get; init; } = string.Empty;
    public string Scope { get; init; } = string.Empty;
    public string EventName { get; init; } = string.Empty;
    public int Order { get; init; }
    public string FileName { get; init; } = string.Empty;
    public string Parameters { get; init; } = string.Empty;
    public string FullPath { get; init; } = string.Empty;
    public string SourceIni { get; init; } = string.Empty;
    public bool Referenced { get; init; }
    public bool Exists { get; init; }
    public long Size { get; init; }
    public DateTime? Modified { get; init; }

    public string Type =>
        Path.GetExtension(FileName).TrimStart('.').ToUpperInvariant();

    public string Assignment =>
        string.IsNullOrWhiteSpace(EventName)
            ? (Referenced ? "Referenced" : "Unassigned file")
            : $"{Scope} / {EventName}";

    public string OrderText =>
        Referenced ? Order.ToString() : string.Empty;
}

public enum GpoScriptSearchMode
{
    ContentOnly,
    FileNamesAndPaths,
    Both
}

public sealed class GpoScriptSearchResult
{
    public IReadOnlyList<GpoScriptInfo> Scripts { get; init; } =
        Array.Empty<GpoScriptInfo>();

    public string Identity { get; init; } = string.Empty;
    public int LineNumber { get; init; }
    public string LineText { get; init; } = string.Empty;
    public string MatchType { get; init; } = string.Empty;

    public GpoScriptInfo Script =>
        Scripts.FirstOrDefault() ?? new GpoScriptInfo();

    public string FileName => Script.FileName;
    public string FullPath => Script.FullPath;

    public int CopyCount =>
        Scripts
            .Select(item => item.FullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

    public int GpoReferenceCount =>
        Scripts
            .Select(item => item.GpoId)
            .Distinct()
            .Count();
}

public sealed class GpoScriptDocument
{
    public string Text { get; set; } = string.Empty;
    public int CodePage { get; init; }
    public bool EmitBom { get; init; }
    public string OriginalSha256 { get; init; } = string.Empty;
}

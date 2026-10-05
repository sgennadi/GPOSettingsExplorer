namespace GPOSettingsExplorer.Models;

public sealed class GppDocumentInfo
{
    public Guid GpoId { get; init; }
    public string GpoName { get; init; } = string.Empty;
    public string DomainName { get; init; } = string.Empty;
    public string Scope { get; init; } = "Computer";
    public string PreferenceType { get; init; } = string.Empty;
    public string RelativePath { get; init; } = string.Empty;
    public string XmlPath { get; init; } = string.Empty;
    public Guid CseGuid { get; init; }
    public Guid ToolGuid { get; init; }
    public string RootElement { get; init; } = string.Empty;
    public int ItemCount { get; init; }
    public long SizeBytes { get; init; }
    public DateTime? LastWriteTime { get; init; }

    public string CseGuidText => CseGuid.ToString("B").ToUpperInvariant();
    public string ToolGuidText => ToolGuid.ToString("B").ToUpperInvariant();

    public string SearchText =>
        $"{GpoName} {Scope} {PreferenceType} {RelativePath} {RootElement} {XmlPath} {CseGuidText} {ToolGuidText}";
}

public sealed class GppDocumentTypeInfo
{
    public string Name { get; init; } = string.Empty;
    public string RelativePath { get; init; } = string.Empty;
    public Guid CseGuid { get; init; }
    public Guid ToolGuid { get; init; }
}

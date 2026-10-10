namespace GPOSettingsExplorer.Models;

/// <summary>
/// A stored GPO payload, not a resolved RSoP setting or proof of an ADMX
/// enabled/disabled state. No write capability is attached to these rows.
/// </summary>
public sealed record StoredGpoSetting
{
    public Guid GpoId { get; init; }
    public string GpoName { get; init; } = "";
    public string Scope { get; init; } = "";
    public string Source { get; init; } = "";
    public string Section { get; init; } = "";
    public string Key { get; init; } = "";
    public string ValueName { get; init; } = "";
    public string Type { get; init; } = "";
    public string Value { get; init; } = "";
    public int DataBytes { get; init; }
    public bool IsInstruction { get; init; }
    public string FilePath { get; init; } = "";
    public string DisplayName =>
        string.IsNullOrWhiteSpace(ValueName) ? "(key/default)" : ValueName;
    public string SearchText => string.Join(" ",
        new[] { GpoName, Scope, Source, Section, Key, ValueName, Type, Value });
}

public sealed record StoredGpoSourceStatus(
    string Source,
    string Scope,
    string Status,
    string Details,
    string FilePath,
    int EntryCount);

public sealed record StoredGpoScanResult(
    Guid GpoId,
    string GpoName,
    IReadOnlyList<StoredGpoSetting> Rows,
    IReadOnlyList<StoredGpoSourceStatus> Sources,
    DateTimeOffset CollectedAt)
{
    // A complete *core-source read* never means complete enumeration of
    // optional third-party CSE files, registry extensions or applied policy.
    public bool CoreSourcesReadable => Sources.Count == 3 &&
        Sources.All(s => s.Status is "Read" or "Not present");
    public string Coverage =>
        $"{Rows.Count:N0} stored rows from {Sources.Count} core source files; " +
        (CoreSourcesReadable ? "core files inspected" : "PARTIAL / source error") +
        ". Other CSE files not inspected; no RSoP or effective state inferred.";
}

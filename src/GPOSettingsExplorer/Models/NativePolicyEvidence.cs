namespace GPOSettingsExplorer.Models;

/// <summary>
/// A value observed in a GPO template on the pinned SYSVOL server. The value
/// is NOT proof of effective RSoP on any target and is not an ADMX state.
/// </summary>
public sealed record NativePolicyEvidence
{
    public Guid GpoId { get; init; }
    public string GpoName { get; init; } = string.Empty;
    public string Scope { get; init; } = string.Empty;
    public string Source { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public string SettingName { get; init; } = string.Empty;
    public string State { get; init; } = "Stored source value";
    public string Value { get; init; } = string.Empty;
    public string DataType { get; init; } = string.Empty;
    public string RegistryKey { get; init; } = string.Empty;
    public string RegistryValue { get; init; } = string.Empty;
    public string SourceLocation { get; init; } = string.Empty;
    public int Ordinal { get; init; }
    public string SearchText => string.Join(" ", new[]
    {
        GpoName, Scope, Source, Category, SettingName, State,
        Value, DataType, RegistryKey, RegistryValue, SourceLocation
    });
}

public sealed record NativePolicySourceStatus(
    string Source, string Status, int RowCount, string Details);

public sealed record NativePolicyScanResult(
    Guid GpoId,
    string GpoName,
    string PinnedServer,
    IReadOnlyList<NativePolicyEvidence> Rows,
    IReadOnlyList<NativePolicySourceStatus> Sources,
    DateTimeOffset CollectedAt)
{
    public bool HasErrors => Sources.Any(source => source.Status is
        "Read error" or "Parse error" or "Changed during read" or "Limit exceeded");

    public bool IsComplete => !HasErrors &&
        Sources.All(source => source.Status == "Read successfully");

    public string Coverage =>
        $"{Rows.Count:N0} stored source values | " +
        $"{Sources.Count(source => source.Status == "Read successfully")} / " +
        $"{Sources.Count} source files read | " +
        $"{Sources.Count(source => source.Status == "Not present")} absent | " +
        $"{Sources.Count(source => source.Status is
            "Read error" or "Parse error" or "Changed during read" or "Limit exceeded")} errors | " +
        (IsComplete ? "all listed sources parsed" :
            "PARTIAL: not every optional source was readable/present") +
        "; never infer Not Configured or effective RSoP from missing files.";
}

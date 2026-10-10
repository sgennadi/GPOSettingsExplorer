namespace GPOSettingsExplorer.Models;

/// <summary>
/// Evidence from a source file of one specific GPO on one pinned domain
/// controller. Never equate presence in a file with effective client RSoP.
/// These are only read-only saved values, not inferred ADMX states.
/// </summary>
public sealed record RealSettingRecord
{
    public Guid GpoId { get; init; }
    public string GpoName { get; init; } = string.Empty;
    public string Scope { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public string SettingName { get; init; } = string.Empty;
    public string RegistryKey { get; init; } = string.Empty;
    public string RegistryValue { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;
    public string ValueType { get; init; } = string.Empty;
    public string SourceFile { get; init; } = string.Empty;
    public string SourceSha256 { get; init; } = string.Empty;
    public string State { get; init; } = "Stored in GPO source";
    public string Evidence { get; init; } = string.Empty;
    public string SearchText => string.Join(" ", new[]
    {
        GpoName, Scope, Category, SettingName, RegistryKey,
        RegistryValue, Value, ValueType, SourceFile, State, Evidence
    });
}

public sealed record RealSettingsFileEvidence(
    string SourceFile,
    string Status,
    int Records,
    string Sha256,
    string Details)
{
    public bool HasError => Status is "Invalid" or "Unreadable" or "Partial";
}

public sealed record RealSettingsScanResult(
    Guid GpoId,
    string GpoName,
    string Domain,
    string DomainController,
    DateTimeOffset CapturedAt,
    IReadOnlyList<RealSettingRecord> Rows,
    IReadOnlyList<RealSettingsFileEvidence> Files)
{
    public bool IsPartial => Files.Count == 0 || Files.Any(f => f.HasError);

    public string Coverage =>
        $"{Rows.Count:N0} stored source entries across " +
        $"{Files.Count(f => f.Status == "Read"):N0} read file(s), " +
        $"{Files.Count(f => f.Status == "Absent"):N0} absent optional file(s), " +
        $"{Files.Count(f => f.HasError):N0} issue(s); " +
        (IsPartial ? "PARTIAL" : "read of available files completed") +
        $"; pinned DC: {DomainController}";

    public bool Matches(string domain, string dc) =>
        Domain.Equals(domain, StringComparison.OrdinalIgnoreCase) &&
        DomainController.Equals(dc, StringComparison.OrdinalIgnoreCase);
}

public sealed record SourceParseResult(
    IReadOnlyList<RealSettingRecord> Rows,
    IReadOnlyList<string> Issues)
{
    public bool IsComplete => Issues.Count == 0;
}

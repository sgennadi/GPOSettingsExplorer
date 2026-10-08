namespace GPOSettingsExplorer.Models;

public sealed class SemanticDiffRow
{
    public string Path { get; init; } = string.Empty;
    public string LeftValue { get; init; } = string.Empty;
    public string RightValue { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;

    public bool IsDifferent =>
        !Status.Equals(
            "Same",
            StringComparison.OrdinalIgnoreCase);

    public string SearchText =>
        $"{Status} {Path} {LeftValue} {RightValue}";
}

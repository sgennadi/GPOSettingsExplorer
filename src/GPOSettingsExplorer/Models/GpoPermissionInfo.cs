namespace GPOSettingsExplorer.Models;

public enum GpoPermissionLevel
{
    Apply,
    Read,
    Edit,
    FullControl,
    Custom
}

public sealed class GpoPermissionInfo
{
    public Guid GpoId { get; init; }
    public string GpoName { get; init; } = string.Empty;
    public string TrusteeName { get; init; } = string.Empty;
    public string TrusteeDomain { get; init; } = string.Empty;
    public string TrusteeSid { get; init; } = string.Empty;
    public string TrusteeDsPath { get; init; } = string.Empty;
    public int TrusteeType { get; init; }
    public GpoPermissionLevel Level { get; init; }
    public int RawPermission { get; init; }
    public bool Denied { get; init; }
    public bool Inherited { get; init; }
    public bool Inheritable { get; init; }

    public string TrusteeDisplay =>
        string.IsNullOrWhiteSpace(TrusteeDomain)
            ? TrusteeName
            : $"{TrusteeDomain}\\{TrusteeName}";

    public string PermissionDisplay => Level switch
    {
        GpoPermissionLevel.Apply => "Apply group policy",
        GpoPermissionLevel.Read => "Read",
        GpoPermissionLevel.Edit => "Edit settings",
        GpoPermissionLevel.FullControl => "Edit settings, delete, modify security",
        _ => $"Custom (0x{RawPermission:X})"
    };

    public string Category =>
        Level == GpoPermissionLevel.Apply ? "Security Filtering" : "Delegation";
}

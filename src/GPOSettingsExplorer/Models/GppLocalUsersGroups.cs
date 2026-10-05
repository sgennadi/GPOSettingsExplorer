namespace GPOSettingsExplorer.Models;

public sealed class GppLocalUserInfo
{
    public Guid GpoId { get; init; }
    public string GpoName { get; init; } = string.Empty;
    public string DomainName { get; init; } = string.Empty;
    public string Scope { get; init; } = "Computer";
    public string XmlPath { get; init; } = string.Empty;
    public string Uid { get; set; } = string.Empty;
    public int Ordinal { get; init; }

    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Action { get; set; } = "U";
    public string UserName { get; set; } = string.Empty;
    public string NewName { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string AccountDescription { get; set; } = string.Empty;
    public bool ChangePasswordAtLogon { get; set; }
    public bool UserCannotChangePassword { get; set; }
    public bool PasswordNeverExpires { get; set; }
    public bool AccountDisabled { get; set; }
    public string Expires { get; set; } = string.Empty;

    // Legacy GPP cpassword data is intentionally opaque. The application never
    // decrypts, displays, creates, or clones it.
    public string OpaqueCredential { get; set; } = string.Empty;
    public bool ClearStoredCredential { get; set; }

    public bool Disabled { get; set; }
    public bool BypassErrors { get; set; }
    public bool RemoveWhenNoLongerApplied { get; set; }
    public bool RunInUserContext { get; set; }
    public string FiltersXml { get; set; } = string.Empty;

    public bool HasStoredCredential =>
        !string.IsNullOrWhiteSpace(OpaqueCredential);

    public bool HasFilters =>
        !string.IsNullOrWhiteSpace(FiltersXml);

    public string ActionDisplay => Action.ToUpperInvariant() switch
    {
        "C" => "Create",
        "D" => "Delete",
        "R" => "Replace",
        _ => "Update"
    };

    public string SearchText =>
        $"{GpoName} {Scope} {DisplayName} {Description} {ActionDisplay} " +
        $"{UserName} {NewName} {FullName} {AccountDescription}";
}

public sealed class GppLocalGroupMemberInfo
{
    public string Name { get; set; } = string.Empty;
    public string Action { get; set; } = "ADD";
    public string Sid { get; set; } = string.Empty;

    public string ActionDisplay =>
        Action.Equals("REMOVE", StringComparison.OrdinalIgnoreCase)
            ? "Remove"
            : "Add";

    public GppLocalGroupMemberInfo Clone() =>
        new()
        {
            Name = Name,
            Action = Action,
            Sid = Sid
        };
}

public sealed class GppLocalGroupInfo
{
    public Guid GpoId { get; init; }
    public string GpoName { get; init; } = string.Empty;
    public string DomainName { get; init; } = string.Empty;
    public string Scope { get; init; } = "Computer";
    public string XmlPath { get; init; } = string.Empty;
    public string Uid { get; set; } = string.Empty;
    public int Ordinal { get; init; }

    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Action { get; set; } = "U";
    public string GroupName { get; set; } = string.Empty;
    public string GroupSid { get; set; } = string.Empty;
    public string NewName { get; set; } = string.Empty;
    public string GroupDescription { get; set; } = string.Empty;
    public string CurrentUserAction { get; set; } = string.Empty;
    public bool DeleteAllUsers { get; set; }
    public bool DeleteAllGroups { get; set; }
    public bool RemoveAccounts { get; set; }
    public bool PropertiesDisabled { get; set; }

    public List<GppLocalGroupMemberInfo> Members { get; set; } = new();

    public bool Disabled { get; set; }
    public bool BypassErrors { get; set; }
    public bool RemoveWhenNoLongerApplied { get; set; }
    public bool RunInUserContext { get; set; }
    public string FiltersXml { get; set; } = string.Empty;

    public bool HasFilters =>
        !string.IsNullOrWhiteSpace(FiltersXml);

    public string ActionDisplay => Action.ToUpperInvariant() switch
    {
        "C" => "Create",
        "D" => "Delete",
        "R" => "Replace",
        _ => "Update"
    };

    public string MembersPreview =>
        string.Join(
            " | ",
            Members.Take(4).Select(
                member =>
                    $"{member.ActionDisplay}: {member.Name}")) +
        (Members.Count > 4
            ? $" | +{Members.Count - 4} more"
            : string.Empty);

    public string SearchText =>
        $"{GpoName} {Scope} {DisplayName} {Description} {ActionDisplay} " +
        $"{GroupName} {GroupSid} {NewName} {GroupDescription} {MembersPreview}";
}

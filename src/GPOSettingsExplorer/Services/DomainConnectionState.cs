namespace GPOSettingsExplorer.Services;

public enum CredentialPersistenceScope
{
    None,
    CurrentUser,
    LocalMachine
}

public sealed record DomainConnectionProfile(
    string DomainName,
    string DomainController,
    string UserName,
    bool UseCurrentCredentials,
    CredentialPersistenceScope PersistenceScope)
{
    public static DomainConnectionProfile CurrentSession(
        string domainName = "",
        string domainController = "") =>
        new(
            domainName,
            domainController,
            string.Empty,
            true,
            CredentialPersistenceScope.None);
}

public static class DomainConnectionState
{
    private static readonly object SyncRoot = new();

    private static DomainConnectionProfile _profile =
        DomainConnectionProfile.CurrentSession();

    private static DomainContext? _context;

    public static DomainConnectionProfile Profile
    {
        get
        {
            lock (SyncRoot)
                return _profile;
        }
    }

    public static DomainContext? Context
    {
        get
        {
            lock (SyncRoot)
                return _context;
        }
    }

    public static void SetProfile(
        DomainConnectionProfile profile)
    {
        lock (SyncRoot)
        {
            _profile =
                profile;

            _context =
                null;
        }
    }

    public static void SetContext(
        DomainContext context)
    {
        lock (SyncRoot)
        {
            _context =
                context;

            _profile =
                _profile with
                {
                    DomainName =
                        context.DomainName,
                    DomainController =
                        context.ConnectedServer
                };
        }
    }

    public static string GetServerFor(
        string domainName)
    {
        lock (SyncRoot)
        {
            if (_context is not null &&
                _context.DomainName.Equals(
                    domainName,
                    StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(
                    _context.ConnectedServer))
            {
                return _context.ConnectedServer;
            }

            if (_profile.DomainName.Equals(
                    domainName,
                    StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(
                    _profile.DomainController))
            {
                return _profile.DomainController;
            }

            return string.Empty;
        }
    }

    public static string BuildLdapPath(
        string distinguishedName)
    {
        var server =
            Context?.ConnectedServer;

        return string.IsNullOrWhiteSpace(
                server)
            ? $"LDAP://{distinguishedName}"
            : $"LDAP://{server}/{distinguishedName}";
    }

    public static string BuildRootDsePath()
    {
        var server =
            Profile.DomainController;

        if (string.IsNullOrWhiteSpace(
                server))
        {
            server =
                Profile.DomainName;
        }

        return string.IsNullOrWhiteSpace(
                server)
            ? "LDAP://RootDSE"
            : $"LDAP://{server}/RootDSE";
    }

    public static string BuildSysvolRoot(
        string domainName)
    {
        var server =
            GetServerFor(
                domainName);

        if (string.IsNullOrWhiteSpace(
                server))
        {
            server =
                domainName;
        }

        return $@"\\{server}\SYSVOL\{domainName}";
    }
}

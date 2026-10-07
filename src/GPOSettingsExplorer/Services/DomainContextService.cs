using System.DirectoryServices;

namespace GPOSettingsExplorer.Services;

public sealed record DomainContext(
    string DomainName,
    string DomainDistinguishedName,
    string ConfigurationNamingContext,
    string ConnectedServer);

public sealed class DomainContextService
{
    public DomainContext Detect()
    {
        return Detect(
            DomainConnectionState.Profile,
            password: null);
    }

    public DomainContext Detect(
        DomainConnectionProfile profile,
        string? password)
    {
        var path =
            BuildRootDsePath(
                profile);

        using var rootDse =
            profile.UseCurrentCredentials
                ? new DirectoryEntry(
                    path)
                : new DirectoryEntry(
                    path,
                    profile.UserName,
                    password ?? string.Empty,
                    AuthenticationTypes.Secure);

        // Force the bind now so connection/credential failures are reported
        // by the connection screen instead of much later in a feature tab.
        rootDse.RefreshCache(
            new[]
            {
                "defaultNamingContext",
                "configurationNamingContext",
                "dnsHostName"
            });

        var defaultNamingContext =
            Convert.ToString(
                rootDse.Properties[
                    "defaultNamingContext"].Value)
            ?? throw new InvalidOperationException(
                "The Active Directory default naming context is unavailable.");

        var configurationNamingContext =
            Convert.ToString(
                rootDse.Properties[
                    "configurationNamingContext"].Value)
            ?? string.Empty;

        var dnsHostName =
            Convert.ToString(
                rootDse.Properties[
                    "dnsHostName"].Value);

        var domainName =
            DistinguishedNameToDns(
                defaultNamingContext);

        if (string.IsNullOrWhiteSpace(
                domainName))
        {
            domainName =
                profile.DomainName;
        }

        var connectedServer =
            FirstNonEmpty(
                dnsHostName,
                profile.DomainController,
                profile.DomainName,
                domainName,
                Environment.MachineName);

        return new DomainContext(
            domainName,
            defaultNamingContext,
            configurationNamingContext,
            connectedServer);
    }

    private static string BuildRootDsePath(
        DomainConnectionProfile profile)
    {
        var target =
            FirstNonEmpty(
                profile.DomainController,
                profile.DomainName);

        return string.IsNullOrWhiteSpace(
                target)
            ? "LDAP://RootDSE"
            : $"LDAP://{target}/RootDSE";
    }

    private static string DistinguishedNameToDns(
        string distinguishedName)
    {
        return string.Join(
            ".",
            distinguishedName
                .Split(
                    ',',
                    StringSplitOptions.RemoveEmptyEntries |
                    StringSplitOptions.TrimEntries)
                .Where(
                    part =>
                        part.StartsWith(
                            "DC=",
                            StringComparison.OrdinalIgnoreCase))
                .Select(
                    part =>
                        part[3..]));
    }

    private static string FirstNonEmpty(
        params string?[] values) =>
        values.FirstOrDefault(
            value =>
                !string.IsNullOrWhiteSpace(
                    value))
        ?? string.Empty;
}

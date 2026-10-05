using System.DirectoryServices;

namespace GPOSettingsExplorer.Services;

public sealed record DomainContext(string DomainName, string DomainDistinguishedName, string ConnectedServer);

public sealed class DomainContextService
{
    public DomainContext Detect()
    {
        using var rootDse = new DirectoryEntry("LDAP://RootDSE");

        var defaultNamingContext = Convert.ToString(rootDse.Properties["defaultNamingContext"].Value)
            ?? throw new InvalidOperationException("The current computer is not connected to an Active Directory domain.");

        var dnsHostName = Convert.ToString(rootDse.Properties["dnsHostName"].Value) ?? Environment.MachineName;
        var domainName = DistinguishedNameToDns(defaultNamingContext);

        return new DomainContext(domainName, defaultNamingContext, dnsHostName);
    }

    private static string DistinguishedNameToDns(string distinguishedName)
    {
        return string.Join(".",
            distinguishedName
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(p => p.StartsWith("DC=", StringComparison.OrdinalIgnoreCase))
                .Select(p => p[3..]));
    }
}

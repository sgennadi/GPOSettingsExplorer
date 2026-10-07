using System.Text.Json;

namespace GPOSettingsExplorer.Services;

public sealed record StoredConnectionProfile(
    string DomainName,
    string DomainController,
    string UserName,
    bool UseCurrentCredentials,
    CredentialPersistenceScope PersistenceScope,
    string ProtectedPassword);

public sealed class ConnectionProfileService
{
    private static readonly JsonSerializerOptions JsonOptions =
        new()
        {
            WriteIndented =
                true
        };

    private static string ProfilePath =>
        Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "GPOSettingsExplorer",
            "connection.json");

    public StoredConnectionProfile? Load()
    {
        try
        {
            if (!File.Exists(
                    ProfilePath))
            {
                return null;
            }

            return JsonSerializer.Deserialize<StoredConnectionProfile>(
                File.ReadAllText(
                    ProfilePath),
                JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    public string LoadPassword(
        StoredConnectionProfile profile)
    {
        if (profile.UseCurrentCredentials ||
            profile.PersistenceScope ==
            CredentialPersistenceScope.None)
        {
            return string.Empty;
        }

        try
        {
            return DpapiCredentialProtector.Unprotect(
                profile.ProtectedPassword,
                profile.PersistenceScope);
        }
        catch
        {
            return string.Empty;
        }
    }

    public void Save(
        DomainConnectionProfile profile,
        string password)
    {
        var directory =
            Path.GetDirectoryName(
                ProfilePath)!;

        Directory.CreateDirectory(
            directory);

        var stored =
            new StoredConnectionProfile(
                profile.DomainName,
                profile.DomainController,
                profile.UserName,
                profile.UseCurrentCredentials,
                profile.PersistenceScope,
                profile.UseCurrentCredentials ||
                profile.PersistenceScope ==
                CredentialPersistenceScope.None
                    ? string.Empty
                    : DpapiCredentialProtector.Protect(
                        password,
                        profile.PersistenceScope));

        File.WriteAllText(
            ProfilePath,
            JsonSerializer.Serialize(
                stored,
                JsonOptions));
    }

    public void Delete()
    {
        try
        {
            File.Delete(
                ProfilePath);
        }
        catch
        {
        }
    }
}

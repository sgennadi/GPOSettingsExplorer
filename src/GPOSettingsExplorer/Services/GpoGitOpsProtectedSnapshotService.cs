using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GPOSettingsExplorer.Models;

namespace GPOSettingsExplorer.Services;

/// <summary>
/// Explicit local-only encrypted stored-source snapshot. The envelope contains
/// ONLY schema and DPAPI ciphertext; never commit a .gposesvault file to Git.
/// Protection is current-Windows-user + machine scoped, not portable to
/// another user's account. No policy write, backup restore or AD connection.
/// </summary>
public static class GpoGitOpsProtectedSnapshotService
{
    public const string Schema = "gposes-protected-source-v1";
    public const int MaxPlaintextBytes = 16 * 1024 * 1024;
    public const int MaxVaultBytes = 24 * 1024 * 1024;

    private sealed record Envelope(string Schema, string Ciphertext);

    public static void Export(string destination, RealSettingsScanResult source)
    {
        ArgumentNullException.ThrowIfNull(source);
        ValidateSource(source);
        var raw = JsonSerializer.SerializeToUtf8Bytes(source);
        if (raw.Length > MaxPlaintextBytes)
            throw new InvalidDataException("Source snapshot exceeds 16 MiB; export refused.");
        string? protectedText = null;
        try
        {
            protectedText = DpapiCredentialProtector.Protect(
                Encoding.UTF8.GetString(raw), CredentialPersistenceScope.CurrentUser);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(raw);
        }
        var encoded = JsonSerializer.Serialize(new Envelope(Schema, protectedText));
        var bytes = new UTF8Encoding(false).GetBytes(encoded);
        if (bytes.Length > MaxVaultBytes)
            throw new InvalidDataException("Encrypted GPO evidence exceeds size limit.");
        var path = Path.GetFullPath(destination);
        var folder = Path.GetDirectoryName(path) ??
            throw new IOException("Invalid protected export destination.");
        Directory.CreateDirectory(folder);
        var staging = Path.Combine(folder, "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var file = new FileStream(staging, FileMode.CreateNew, FileAccess.Write,
                       FileShare.None))
            {
                file.Write(bytes);
                file.Flush(flushToDisk: true);
            }
            File.Move(staging, path, overwrite: true);
        }
        finally
        {
            try { if (File.Exists(staging)) File.Delete(staging); } catch (IOException) { }
        }
    }

    public static RealSettingsScanResult Import(string path)
    {
        var raw = OfflineGpoSourceService.ReadBounded(path, MaxVaultBytes);
        var envelope = JsonSerializer.Deserialize<Envelope>(raw) ??
            throw new InvalidDataException("The protected snapshot is empty.");
        if (envelope.Schema != Schema || string.IsNullOrWhiteSpace(envelope.Ciphertext) ||
            envelope.Ciphertext.Length > MaxVaultBytes)
            throw new InvalidDataException("Invalid protected snapshot envelope.");
        var plaintext = DpapiCredentialProtector.Unprotect(
            envelope.Ciphertext, CredentialPersistenceScope.CurrentUser);
        if (Encoding.UTF8.GetByteCount(plaintext) > MaxPlaintextBytes)
            throw new InvalidDataException("Protected snapshot plaintext is too large.");
        var source = JsonSerializer.Deserialize<RealSettingsScanResult>(plaintext) ??
            throw new InvalidDataException("Protected snapshot is malformed.");
        ValidateSource(source);
        return source;
    }

    private static void ValidateSource(RealSettingsScanResult source)
    {
        if (source.GpoId == Guid.Empty ||
            string.IsNullOrWhiteSpace(source.Domain) || source.Domain.Length > 1024 ||
            string.IsNullOrWhiteSpace(source.GpoName) || source.GpoName.Length > 2048 ||
            source.Rows is null || source.Rows.Count > GpoGitOpsReviewService.MaxEntries ||
            source.Files is null || source.Files.Count > 4000 ||
            source.Rows.Any(r => r is null || r.GpoId != source.GpoId))
            throw new InvalidDataException("Protected snapshot has invalid scope or row counts.");
    }
}

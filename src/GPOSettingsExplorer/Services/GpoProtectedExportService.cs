using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GPOSettingsExplorer.Models;

namespace GPOSettingsExplorer.Services;

/// <summary>
/// Confidential, offline, per-Windows-user source export. The payload contains
/// raw stored GPO evidence and MUST NEVER be committed to Git or uploaded.
/// The envelope holds DPAPI ciphertext only; it is not cross-user portable.
/// </summary>
public sealed record GpoProtectedSourceEnvelope(
    string Schema, DateTimeOffset ExportedUtc, string Protection,
    string ProtectedPayloadBase64);

internal sealed record GpoProtectedSourcePayload(
    string Schema, string Sha256, string SourceJson);

public static class GpoProtectedExportService
{
    public const string EnvelopeSchema = "gposes-dpapi-source-v1";
    private const string PayloadSchema = "gposes-dpapi-source-payload-v1";
    private const int MaxPlainBytes = 12 * 1024 * 1024;
    private const int MaxEnvelopeBytes = 32 * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public static void Export(string destination, RealSettingsScanResult scan)
    {
        ArgumentNullException.ThrowIfNull(scan);
        if (scan.GpoId == Guid.Empty || scan.Rows is null || scan.Files is null ||
            scan.Rows.Count > GpoGitOpsReviewService.MaxEntries ||
            scan.Rows.Any(row => row.GpoId != scan.GpoId))
            throw new InvalidDataException("Source contains invalid or mixed GPO identities.");

        // No unencrypted staging file is ever written.
        var raw = JsonSerializer.Serialize(scan);
        var bytes = Encoding.UTF8.GetBytes(raw);
        try
        {
            if (bytes.Length > MaxPlainBytes)
                throw new InvalidDataException("Confidential source exceeds 12 MiB.");
            var inner = new GpoProtectedSourcePayload(PayloadSchema,
                Convert.ToHexString(SHA256.HashData(bytes)), raw);
            var cipher = DpapiCredentialProtector.Protect(
                JsonSerializer.Serialize(inner), CredentialPersistenceScope.CurrentUser);
            var outer = new GpoProtectedSourceEnvelope(
                EnvelopeSchema, DateTimeOffset.UtcNow,
                "Windows DPAPI CurrentUser (not portable to another user or machine)",
                cipher);
            var output = JsonSerializer.Serialize(outer, JsonOptions);
            if (Encoding.UTF8.GetByteCount(output) > MaxEnvelopeBytes)
                throw new InvalidDataException("Encrypted envelope exceeds 32 MiB.");
            WriteAtomic(destination, output);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    public static RealSettingsScanResult Import(string filename)
    {
        var bytes = OfflineGpoSourceService.ReadBounded(filename, MaxEnvelopeBytes);
        var envelope = JsonSerializer.Deserialize<GpoProtectedSourceEnvelope>(bytes) ??
            throw new InvalidDataException("Protected source envelope is empty.");
        if (envelope.Schema != EnvelopeSchema ||
            envelope.Protection !=
                "Windows DPAPI CurrentUser (not portable to another user or machine)" ||
            string.IsNullOrWhiteSpace(envelope.ProtectedPayloadBase64) ||
            envelope.ProtectedPayloadBase64.Length > MaxEnvelopeBytes)
            throw new InvalidDataException("Invalid protected source envelope.");

        var plaintext = DpapiCredentialProtector.Unprotect(
            envelope.ProtectedPayloadBase64, CredentialPersistenceScope.CurrentUser);
        if (Encoding.UTF8.GetByteCount(plaintext) > MaxPlainBytes + 4096)
            throw new InvalidDataException("Decrypted source exceeds the approved size.");
        var payload = JsonSerializer.Deserialize<GpoProtectedSourcePayload>(plaintext) ??
            throw new InvalidDataException("Protected source payload is empty.");
        if (payload.Schema != PayloadSchema ||
            payload.SourceJson is null ||
            Encoding.UTF8.GetByteCount(payload.SourceJson) > MaxPlainBytes)
            throw new InvalidDataException("Protected source payload has invalid metadata.");
        var data = Encoding.UTF8.GetBytes(payload.SourceJson);
        try
        {
            if (!Convert.ToHexString(SHA256.HashData(data)).Equals(
                    payload.Sha256, StringComparison.Ordinal))
                throw new CryptographicException("Decrypted source integrity mismatch.");
            var scan = JsonSerializer.Deserialize<RealSettingsScanResult>(data) ??
                throw new InvalidDataException("Decrypted GPO source is empty.");
            if (scan.GpoId == Guid.Empty || scan.Rows is null || scan.Files is null ||
                scan.Rows.Count > GpoGitOpsReviewService.MaxEntries ||
                scan.Rows.Any(r => r.GpoId != scan.GpoId))
                throw new InvalidDataException("Decrypted GPO source identity is inconsistent.");
            return scan;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(data);
        }
    }

    private static void WriteAtomic(string path, string output)
    {
        var full = Path.GetFullPath(path);
        var dir = Path.GetDirectoryName(full) ??
            throw new IOException("Invalid confidential export destination.");
        Directory.CreateDirectory(dir);
        var staging = Path.Combine(dir,
            "." + Path.GetFileName(full) + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var file = new FileStream(staging, FileMode.CreateNew,
                       FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(file, new UTF8Encoding(false)))
            {
                writer.Write(output);
                writer.Flush();
                file.Flush(flushToDisk: true);
            }
            File.Move(staging, full, overwrite: true);
        }
        finally
        {
            try { if (File.Exists(staging)) File.Delete(staging); }
            catch (IOException) { }
        }
    }
}

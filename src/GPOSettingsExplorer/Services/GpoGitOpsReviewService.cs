using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using GPOSettingsExplorer.Models;

namespace GPOSettingsExplorer.Services;

/// <summary>
/// Confidentiality-preserving Git review workflow. Only HMAC-derived opaque
/// identities are represented in Git-ready artifacts. Approval is certificate
/// evidence for an exact request, never authorization to modify a GPO.
/// </summary>
public sealed record GpoGitOpsChange(string PolicyKeyHmacSha256, string Kind);

public sealed record GpoGitOpsReviewRequest(
    string Schema, DateTimeOffset CreatedUtc, string KeyId,
    string GpoIdentityHmacSha256, string DomainIdentityHmacSha256,
    string BaselineSha256, string CandidateSha256,
    bool BaselinePartial, bool CandidatePartial,
    int BaselineRecords, int CandidateRecords,
    IReadOnlyList<GpoGitOpsChange> Changes);

public sealed record GpoGitOpsSignedDecision(
    string Schema, string RequestSha256, string Decision,
    DateTimeOffset SignedUtc, string CertificateSha256,
    string CertificateDerBase64, string SignatureBase64);

public static class GpoGitOpsReviewService
{
    public const string RequestSchema = "gposes-gitops-review-v1";
    public const string DecisionSchema = "gposes-gitops-approval-v1";
    public const int MaxDocumentBytes = 12 * 1024 * 1024;
    public const int MaxEntries = 30000;

    private static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };

    public static void ValidateManifest(GpoGitOpsManifest document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.Schema != "gposes-gitops-fingerprint-v2" ||
            !Hex(document.KeyId, 16) ||
            !Hex(document.GpoIdentityHmacSha256, 64) ||
            !Hex(document.DomainIdentityHmacSha256, 64) ||
            document.SourceRecords is < 0 or > MaxEntries ||
            document.Entries is null || document.Entries.Count > MaxEntries ||
            document.Entries.Any(x => x is null ||
                !Hex(x.PolicyKeyHmacSha256, 64) ||
                !Hex(x.StoredValueHmacSha256, 64) ||
                x.DuplicateCount is < 1 or > MaxEntries) ||
            document.Entries.Select(x => x.PolicyKeyHmacSha256)
                .Distinct(StringComparer.Ordinal).Count() != document.Entries.Count ||
            document.Entries.Sum(x => (long)x.DuplicateCount) != document.SourceRecords)
            throw new InvalidDataException("Invalid or inconsistent GitOps fingerprint manifest.");
    }

    public static GpoGitOpsManifest LoadManifest(string path)
    {
        var bytes = OfflineGpoSourceService.ReadBounded(path, MaxDocumentBytes);
        var document = JsonSerializer.Deserialize<GpoGitOpsManifest>(bytes) ??
            throw new InvalidDataException("GitOps manifest is empty.");
        ValidateManifest(document);
        return document;
    }

    public static GpoGitOpsReviewRequest CreateRequest(
        GpoGitOpsManifest baseline, GpoGitOpsManifest candidate)
    {
        ValidateManifest(baseline);
        ValidateManifest(candidate);
        if (!string.Equals(baseline.KeyId, candidate.KeyId, StringComparison.Ordinal) ||
            !string.Equals(baseline.GpoIdentityHmacSha256,
                candidate.GpoIdentityHmacSha256, StringComparison.Ordinal) ||
            !string.Equals(baseline.DomainIdentityHmacSha256,
                candidate.DomainIdentityHmacSha256, StringComparison.Ordinal))
            throw new InvalidOperationException(
                "GitOps comparison denied: different DPAPI HMAC key, GPO or domain. " +
                "Separate Windows users/computers do not share the GitOps HMAC key.");

        var before = baseline.Entries.ToDictionary(
            x => x.PolicyKeyHmacSha256, StringComparer.Ordinal);
        var after = candidate.Entries.ToDictionary(
            x => x.PolicyKeyHmacSha256, StringComparer.Ordinal);
        var changes = new List<GpoGitOpsChange>();
        foreach (var key in before.Keys.Union(after.Keys, StringComparer.Ordinal)
                     .OrderBy(x => x, StringComparer.Ordinal))
        {
            if (!before.TryGetValue(key, out var left))
                changes.Add(new(key, "Added"));
            else if (!after.TryGetValue(key, out var right))
                changes.Add(new(key, "Removed"));
            else if (left.StoredValueHmacSha256 != right.StoredValueHmacSha256 ||
                     left.DuplicateCount != right.DuplicateCount)
                changes.Add(new(key, "Changed"));
        }

        return new GpoGitOpsReviewRequest(RequestSchema, DateTimeOffset.UtcNow,
            candidate.KeyId, candidate.GpoIdentityHmacSha256,
            candidate.DomainIdentityHmacSha256,
            DigestManifest(baseline), DigestManifest(candidate),
            baseline.PartialCoverage, candidate.PartialCoverage,
            baseline.SourceRecords, candidate.SourceRecords, changes);
    }

    public static bool CanApprove(GpoGitOpsReviewRequest request) =>
        request is not null &&
        !request.BaselinePartial && !request.CandidatePartial &&
        request.BaselineRecords > 0 && request.CandidateRecords > 0 &&
        request.Changes.Count > 0;

    public static string ReviewSummary(GpoGitOpsReviewRequest request)
    {
        ValidateRequest(request);
        return "GITOPS REDACTED REVIEW (NO GPO WRITE)\n" +
            "Request digest: " + RequestSha256(request) + "\n" +
            "Key: " + request.KeyId + "\n" +
            "Baseline entries: " + request.BaselineRecords +
            " | Candidate entries: " + request.CandidateRecords + "\n" +
            "Added: " + request.Changes.Count(x => x.Kind == "Added") +
            " | Removed: " + request.Changes.Count(x => x.Kind == "Removed") +
            " | Changed: " + request.Changes.Count(x => x.Kind == "Changed") + "\n" +
            "Approval eligibility: " +
            (CanApprove(request) ? "REQUIRES HUMAN REVIEW" :
                "BLOCKED (partial/no evidence or no observed changes)") + "\n" +
            "Unlisted GPO source families, RSoP, AD links, security ACL and WMI are " +
            "not validated by this fingerprint comparison. A signed decision " +
            "does not make GPO writes or deployments safe.\n\n" +
            string.Join("\n", request.Changes.Take(3000).Select(x =>
                "[" + x.Kind + "] " + x.PolicyKeyHmacSha256)) +
            (request.Changes.Count > 3000 ? "\n[Output limited to 3000 entries]" : "");
    }

    public static string RequestSha256(GpoGitOpsReviewRequest request)
    {
        ValidateRequest(request);
        return Convert.ToHexString(SHA256.HashData(
            JsonSerializer.SerializeToUtf8Bytes(request)));
    }

    public static void SaveJson(string path, GpoGitOpsManifest manifest)
    {
        ValidateManifest(manifest);
        WriteAtomic(path, GpoGitOpsExportService.ToJson(manifest));
    }

    public static void SaveJson(string path, GpoGitOpsReviewRequest request)
    {
        ValidateRequest(request);
        WriteAtomic(path, JsonSerializer.Serialize(request, Pretty));
    }

    public static void SaveJson(string path, GpoGitOpsSignedDecision decision)
    {
        ValidateDecision(decision);
        WriteAtomic(path, JsonSerializer.Serialize(decision, Pretty));
    }

    public static GpoGitOpsReviewRequest LoadRequest(string path)
    {
        var data = OfflineGpoSourceService.ReadBounded(path, MaxDocumentBytes);
        var request = JsonSerializer.Deserialize<GpoGitOpsReviewRequest>(data) ??
            throw new InvalidDataException("Empty GitOps review request.");
        ValidateRequest(request);
        return request;
    }

    public static GpoGitOpsSignedDecision LoadDecision(string path)
    {
        var data = OfflineGpoSourceService.ReadBounded(path, 256 * 1024);
        var decision = JsonSerializer.Deserialize<GpoGitOpsSignedDecision>(data) ??
            throw new InvalidDataException("Empty approval receipt.");
        ValidateDecision(decision);
        return decision;
    }

    /// <summary>
    /// Windows CurrentUser/My certificate key is never exported. The resulting
    /// certificate is public; reviewers must obtain trusted SHA-256 pins via an
    /// independently controlled channel. Does not change AD or SYSVOL.
    /// </summary>
    public static GpoGitOpsSignedDecision Sign(
        GpoGitOpsReviewRequest request, string certificateSha256, bool approve)
    {
        ValidateRequest(request);
        if (approve && !CanApprove(request))
            throw new InvalidOperationException(
                "Cannot approve a partial, empty or unchanged GitOps review.");
        if (!Hex(certificateSha256, 64))
            throw new ArgumentException("Enter the 64-digit SHA-256 of the reviewer certificate.");

        using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
        store.Open(OpenFlags.ReadOnly);
        var cert = store.Certificates.OfType<X509Certificate2>()
            .FirstOrDefault(x => CertificateId(x)
                .Equals(certificateSha256, StringComparison.OrdinalIgnoreCase));
        if (cert is null || !cert.HasPrivateKey)
            throw new InvalidOperationException(
                "Reviewer signing certificate with a private key was not found in CurrentUser/My.");
        using (cert)
        {
            CheckSigningCertificate(cert);
            var timestamp = DateTimeOffset.UtcNow;
            var decision = approve ? "Approve" : "Reject";
            var digest = RequestSha256(request);
            var message = SignedBytes(digest, decision, timestamp);
            byte[] signature;
            using (var rsa = cert.GetRSAPrivateKey())
            using (var ecdsa = rsa is null ? cert.GetECDsaPrivateKey() : null)
                signature = rsa is not null
                    ? rsa.SignData(message, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)
                    : ecdsa is not null
                        ? ecdsa.SignData(message, HashAlgorithmName.SHA256)
                        : throw new InvalidOperationException(
                            "Reviewer key must be RSA or ECDSA.");

            return new GpoGitOpsSignedDecision(
                DecisionSchema, digest, decision, timestamp,
                CertificateId(cert), Convert.ToBase64String(cert.RawData),
                Convert.ToBase64String(signature));
        }
    }

    public static string Verify(
        GpoGitOpsReviewRequest request, GpoGitOpsSignedDecision receipt,
        string trustedCertificateSha256)
    {
        ValidateRequest(request);
        ValidateDecision(receipt);
        if (!Hex(trustedCertificateSha256, 64) ||
            !string.Equals(receipt.CertificateSha256, trustedCertificateSha256,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "The reviewer certificate is not on the independently supplied trust pin.");
        if (!string.Equals(receipt.RequestSha256, RequestSha256(request),
                StringComparison.Ordinal))
            throw new InvalidDataException(
                "Approval receipt belongs to a different or modified review request.");
        if (receipt.Decision == "Approve" && !CanApprove(request))
            throw new InvalidDataException(
                "Approval is prohibited for partial, empty or unchanged evidence.");
        var der = Convert.FromBase64String(receipt.CertificateDerBase64);
        using var cert = new X509Certificate2(der);
        if (!string.Equals(CertificateId(cert), trustedCertificateSha256,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Reviewer certificate content does not match its SHA-256.");
        CheckSigningCertificate(cert, receipt.SignedUtc);
        var message = SignedBytes(receipt.RequestSha256, receipt.Decision, receipt.SignedUtc);
        var sig = Convert.FromBase64String(receipt.SignatureBase64);
        using var rsa = cert.GetRSAPublicKey();
        using var ecdsa = rsa is null ? cert.GetECDsaPublicKey() : null;
        var valid = rsa is not null
            ? rsa.VerifyData(message, sig, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)
            : ecdsa?.VerifyData(message, sig, HashAlgorithmName.SHA256) == true;
        if (!valid)
            throw new CryptographicException("Reviewer signature is invalid.");
        return "Cryptographic signature: VERIFIED\n" +
            "Reviewer certificate SHA-256: " + receipt.CertificateSha256 + "\n" +
            "Decision: " + receipt.Decision + "\nSigned UTC: " +
            receipt.SignedUtc.ToString("O") + "\nReview SHA-256: " +
            receipt.RequestSha256 + "\n\n" +
            "This confirms a pinned certificate signed this exact review. " +
            "It does NOT prove the identity/authority of a reviewer or validate " +
            "certificate chain/revocation; manage certificate pin distribution, " +
            "person separation and Git branch protection out of band. " +
            "No GPO is applied or changed.";
    }

    private static string DigestManifest(GpoGitOpsManifest manifest) =>
        Convert.ToHexString(SHA256.HashData(
            JsonSerializer.SerializeToUtf8Bytes(manifest)));

    private static byte[] SignedBytes(string digest, string decision,
        DateTimeOffset signedUtc) =>
        Encoding.UTF8.GetBytes("gposes-gitops-approval-v1\n" + digest + "\n" +
            decision + "\n" + signedUtc.ToUniversalTime().ToString("O"));

    private static void CheckSigningCertificate(
        X509Certificate2 cert, DateTimeOffset? signedUtc = null)
    {
        var time = (signedUtc ?? DateTimeOffset.UtcNow).UtcDateTime;
        if (cert.NotBefore.ToUniversalTime() > time ||
            cert.NotAfter.ToUniversalTime() < time)
            throw new CryptographicException("Reviewer certificate is not valid at signing time.");
        foreach (var usage in cert.Extensions.OfType<X509KeyUsageExtension>())
            if ((usage.KeyUsages & X509KeyUsageFlags.DigitalSignature) == 0)
                throw new CryptographicException(
                    "Certificate is not permitted for digital signatures.");
    }

    private static string CertificateId(X509Certificate2 cert) =>
        Convert.ToHexString(SHA256.HashData(cert.RawData));

    private static void ValidateRequest(GpoGitOpsReviewRequest r)
    {
        if (r is null || r.Schema != RequestSchema ||
            r.CreatedUtc == default ||
            !Hex(r.KeyId, 16) ||
            !Hex(r.GpoIdentityHmacSha256, 64) ||
            !Hex(r.DomainIdentityHmacSha256, 64) ||
            !Hex(r.BaselineSha256, 64) || !Hex(r.CandidateSha256, 64) ||
            r.BaselineRecords is < 0 or > MaxEntries ||
            r.CandidateRecords is < 0 or > MaxEntries ||
            r.Changes is null || r.Changes.Count > MaxEntries ||
            r.Changes.Any(c => c is null || !Hex(c.PolicyKeyHmacSha256, 64) ||
                c.Kind is not ("Added" or "Removed" or "Changed")) ||
            r.Changes.Select(c => c.PolicyKeyHmacSha256)
                .Distinct(StringComparer.Ordinal).Count() != r.Changes.Count)
            throw new InvalidDataException("Malformed GitOps review request.");
    }

    private static void ValidateDecision(GpoGitOpsSignedDecision d)
    {
        if (d is null || d.Schema != DecisionSchema ||
            d.Decision is not ("Approve" or "Reject") ||
            d.SignedUtc == default || !Hex(d.RequestSha256, 64) ||
            !Hex(d.CertificateSha256, 64) ||
            d.CertificateDerBase64 is null or { Length: > 32768 } ||
            d.SignatureBase64 is null or { Length: > 16384 })
            throw new InvalidDataException("Malformed GitOps signing receipt.");
    }

    private static bool Hex(string? value, int length) =>
        value is not null && value.Length == length &&
        value.All(c => c is >= '0' and <= '9' or >= 'A' and <= 'F' or >= 'a' and <= 'f');

    private static void WriteAtomic(string path, string json)
    {
        var output = Path.GetFullPath(path);
        var folder = Path.GetDirectoryName(output) ??
            throw new IOException("Invalid GitOps export destination.");
        Directory.CreateDirectory(folder);
        var tmp = Path.Combine(folder,
            "." + Path.GetFileName(output) + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            var bytes = new UTF8Encoding(false).GetBytes(json);
            if (bytes.Length > MaxDocumentBytes)
                throw new InvalidDataException("GitOps export exceeds 12 MiB safety limit.");
            using (var stream = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write,
                       FileShare.None))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            File.Move(tmp, output, overwrite: true);
        }
        finally
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch (IOException) { }
        }
    }
}

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GPOSettingsExplorer.Models;

namespace GPOSettingsExplorer.Services;

/// <summary>
/// Portable privacy-conscious GitOps review manifest. No raw registry/INF/GPP
/// value or domain name is sent to Git. This is source fingerprint evidence,
/// not an executable policy deployment specification.
/// </summary>
public sealed record GpoGitOpsFingerprint(
    string PolicyKeySha256, string StoredValueSha256, int DuplicateCount);
public sealed record GpoGitOpsManifest(
    string Schema, DateTimeOffset CapturedUtc,
    string GpoIdentitySha256, string DomainIdentitySha256,
    bool PartialCoverage, int SourceRecords,
    IReadOnlyList<GpoGitOpsFingerprint> Entries,
    string Caveat);

public static class GpoGitOpsExportService
{
    public static GpoGitOpsManifest Capture(RealSettingsScanResult source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var timeline = GpoTimelineService.Capture(source);
        return new GpoGitOpsManifest(
            "gposes-gitops-fingerprint-v1", timeline.CapturedUtc,
            Hash(source.GpoId.ToString("N")), Hash(source.Domain.ToLowerInvariant()),
            source.IsPartial, source.Rows.Count,
            timeline.Entries.Select(e => new GpoGitOpsFingerprint(
                Hash(e.Key), e.Fingerprint, e.Count)).ToArray(),
            "Read-only hashes of stored source values, not effective client policy. " +
            "Not a reversible GPO/Intune deployment file. Do not use for automated writes.");
    }

    public static string ToJson(GpoGitOpsManifest report) =>
        JsonSerializer.Serialize(report, new JsonSerializerOptions
        {
            WriteIndented = true
        });

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
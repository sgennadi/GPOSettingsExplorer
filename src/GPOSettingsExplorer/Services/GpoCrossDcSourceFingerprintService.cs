using System.Security.Cryptography;
using GPOSettingsExplorer.Models;

namespace GPOSettingsExplorer.Services;

/// <summary>
/// Bounded byte fingerprints on each explicitly named DC. Distinguishes
/// missing files from access errors. File agreement is not DFSR convergence.
/// </summary>
public sealed record GpoDcSourceFingerprint(
    string Dc, string Source, string Status, string Sha256, string Details);
public sealed record GpoCrossDcSourceReport(
    Guid GpoId, string Domain, DateTimeOffset CapturedUtc,
    IReadOnlyList<GpoDcSourceFingerprint> Files)
{
    public string ToText()
    {
        var result = new List<string>
        {
            "GPO CROSS-DC FILE FINGERPRINTS (READ ONLY)",
            "Captured UTC: " + CapturedUtc.ToString("O"),
            "Domain: " + Domain + "  GPO: " + GpoId.ToString("B"),
            "Matching content hashes do not prove DFSR replication convergence.",
            ""
        };
        foreach (var group in Files.GroupBy(f => f.Source))
        {
            var read = group.Where(f => f.Status == "Read").ToArray();
            var status = group.Any(f => f.Status == "Unknown") ? "UNKNOWN" :
                read.Length == 0 ? "ABSENT ON SELECTED DCs" :
                read.Length != group.Count() ? "INCOMPLETE / DIFFERENT PRESENCE" :
                read.Select(f => f.Sha256).Distinct(StringComparer.Ordinal).Count() == 1
                    ? "SHA-256 MATCH" : "MISMATCH";
            result.Add(group.Key + " — " + status);
            result.AddRange(group.Select(f => "  " + f.Dc + ": " + f.Status +
                " | " + f.Sha256 + " | " + f.Details));
        }
        return string.Join("\n", result);
    }
}

public static class GpoCrossDcSourceFingerprintService
{
    private static readonly (string Relative, int MaxBytes)[] Sources =
    {
        ("GPT.INI", GptIniVersionParser.MaxBytes),
        (@"Machine\Microsoft\Windows NT\Audit\audit.csv",
            AdvancedAuditSourceReader.MaxFileBytes),
        (@"Machine\Registry.pol", RegistryPolReader.MaxFileBytes),
        (@"User\Registry.pol", RegistryPolReader.MaxFileBytes),
        (@"Machine\Microsoft\Windows NT\SecEdit\GptTmpl.inf",
            SecurityTemplateSourceReader.MaxFileBytes),
        (@"Machine\Preferences\Registry\Registry.xml",
            GppXmlSourceReader.MaxFileBytes),
        (@"User\Preferences\Registry\Registry.xml",
            GppXmlSourceReader.MaxFileBytes)
    };

    public static GpoCrossDcSourceReport Compare(
        GpoInfo gpo, string controllerNames, CancellationToken cancellation = default)
    {
        var context = DomainConnectionState.Context;
        if (context is null ||
            !context.DomainName.Equals(gpo.DomainName, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "A matching connected domain/DC is required.");
        var hosts = GpoCrossDcConsistencyService.ValidateControllers(
            controllerNames, gpo.DomainName);
        var files = new List<GpoDcSourceFingerprint>();
        foreach (var spec in Sources)
        {
            foreach (var dc in hosts)
            {
                cancellation.ThrowIfCancellationRequested();
                var root = $@"\\{dc}\SYSVOL\{gpo.DomainName}\Policies\{gpo.Id:B}";
                var path = Path.Combine(root, spec.Relative);
                if (!File.Exists(path))
                {
                    // File.Exists can return false for access denied: do not
                    // claim proven absence from an unchecked probe.
                    files.Add(new(dc, spec.Relative, "Unknown", "",
                        "File missing OR inaccessible; cannot distinguish without a read."));
                    continue;
                }
                try
                {
                    var bytes = OfflineGpoSourceService.ReadBounded(
                        path, spec.MaxBytes, cancellation);
                    var sha = Convert.ToHexString(SHA256.HashData(bytes));
                    files.Add(new(dc, spec.Relative, "Read", sha,
                        bytes.Length + " byte(s)"));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                                           System.Security.SecurityException)
                {
                    files.Add(new(dc, spec.Relative, "Unknown", "",
                        ex.GetType().Name + ": read not verified"));
                }
            }
        }
        return new GpoCrossDcSourceReport(
            gpo.Id, gpo.DomainName, DateTimeOffset.UtcNow, files);
    }
}
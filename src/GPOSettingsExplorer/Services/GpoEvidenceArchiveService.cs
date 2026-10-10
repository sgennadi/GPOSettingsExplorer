using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GPOSettingsExplorer.Models;

namespace GPOSettingsExplorer.Services;

/// <summary>
/// Local-only, operator-requested evidence ZIP. No cloud upload, no AD writes.
/// A report is a multi-source observation, NOT an atomic DC snapshot or RSoP.
/// </summary>
public static class GpoEvidenceArchiveService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public static void Export(
        string destination, GpoInfo gpo,
        RealSettingsScanResult sources, GpoConsistencyReport health,
        GpoImpactPreview impact, string? gpmcXml)
    {
        ArgumentNullException.ThrowIfNull(gpo);
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(health);
        ArgumentNullException.ThrowIfNull(impact);
        if (gpo.Id != sources.GpoId || gpo.Id != health.GpoId ||
            gpo.Id != impact.GpoId ||
            !sources.Domain.Equals(health.Domain, StringComparison.OrdinalIgnoreCase) ||
            !sources.Domain.Equals(impact.Domain, StringComparison.OrdinalIgnoreCase) ||
            !sources.DomainController.Equals(health.DomainController,
                StringComparison.OrdinalIgnoreCase) ||
            !sources.DomainController.Equals(impact.PinnedDc,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "Evidence snapshots refer to different GPOs, domains or DCs. Export refused.");

        var fullPath = Path.GetFullPath(destination);
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new IOException("Invalid evidence archive destination.");
        Directory.CreateDirectory(directory);
        var staging = Path.Combine(directory,
            "." + Path.GetFileName(fullPath) + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            var hashes = new SortedDictionary<string, string>(StringComparer.Ordinal);
            using (var output = new FileStream(staging, FileMode.CreateNew,
                       FileAccess.Write, FileShare.None))
            using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: false))
            {
                void Add(string filename, string text)
                {
                    var bytes = new UTF8Encoding(false).GetBytes(text);
                    var entry = zip.CreateEntry(filename, CompressionLevel.Optimal);
                    using (var writer = entry.Open())
                        writer.Write(bytes, 0, bytes.Length);
                    hashes[filename] = Convert.ToHexString(SHA256.HashData(bytes));
                }

                Add("README.txt",
                    "GPO Settings Explorer - sensitive local GPO evidence bundle\n" +
                    "This archive may contain domain identifiers, computer/user policy content, " +
                    "paths and registry values. Do NOT publish or upload it automatically.\n" +
                    "One operator-requested snapshot from a pinned DC; reads across AD, SYSVOL " +
                    "and GPMC are not atomic. Check individual capture times and coverage.\n" +
                    "Stored policy values are not effective policy. RSoP/client execution, " +
                    "security tokens, WMI, link inheritance, loopback and other DCs " +
                    "are NOT fully evaluated. No backup, restore or repair is performed.\n" +
                    "SHA-256 entries in sha256sums.txt verify the individual ZIP entry content.\n");
                Add("health.txt", health.ToText());
                Add("impact.txt", impact.ToText());
                Add("sources.json", JsonSerializer.Serialize(sources, JsonOptions));
                Add("settings.csv", ToCsv(sources.Rows));
                if (!string.IsNullOrWhiteSpace(gpmcXml))
                    Add("gpmc-report.xml", gpmcXml);

                var manifest = new
                {
                    Schema = "gposes-evidence-v1",
                    GeneratedUtc = DateTimeOffset.UtcNow,
                    Gpo = new { gpo.Id, gpo.DisplayName, gpo.DomainName },
                    CapturedDomain = sources.Domain,
                    PinnedDc = sources.DomainController,
                    SourceCapturedAt = sources.CapturedAt,
                    HealthCapturedAt = health.CapturedAt,
                    ImpactCapturedAt = impact.CapturedAt,
                    SourceCoverage = sources.Coverage,
                    HealthSummary = health.Summary,
                    ImpactSummary = impact.Summary,
                    PartialEvidence = sources.IsPartial || health.HasBlockingIssues ||
                        !impact.LinkInventoryComplete || string.IsNullOrWhiteSpace(gpmcXml),
                    GpmcXmlIncluded = !string.IsNullOrWhiteSpace(gpmcXml),
                    CompleteDomainEffectiveRsop = false,
                    IsAtomicSnapshot = false,
                    ContentSha256 = hashes
                };
                var digestList = string.Join("\n", hashes.Select(kv =>
                    kv.Value + "  " + kv.Key)) + "\n";
                Add("sha256sums.txt", digestList);
                // Manifest itself is not in sha256sums.txt (prevents recursive checksums).
                var manifestEntry = zip.CreateEntry("manifest.json", CompressionLevel.Optimal);
                using (var stream = manifestEntry.Open())
                using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
                    writer.Write(JsonSerializer.Serialize(manifest, JsonOptions));
            }
            // Only a complete archive is put at the requested destination.
            File.Move(staging, fullPath, overwrite: true);
        }
        finally
        {
            try { if (File.Exists(staging)) File.Delete(staging); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    public static string ToCsv(IReadOnlyList<RealSettingRecord> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine("GPO,Scope,Category,Setting,State,Type,Value,Registry key,Registry value,Source file,Source SHA-256,Evidence");
        foreach (var row in rows)
        {
            sb.AppendLine(string.Join(",", new[]
            {
                row.GpoName, row.Scope, row.Category, row.SettingName, row.State,
                row.ValueType, row.Value, row.RegistryKey, row.RegistryValue,
                row.SourceFile, row.SourceSha256, row.Evidence
            }.Select(Csv)));
        }
        return sb.ToString();
    }

    private static string Csv(string? value)
    {
        var text = value ?? "";
        // Prevent spreadsheet formula execution when CSV is opened in Excel.
        if (text.Length > 0 && (text[0] is '=' or '+' or '-' or '@' ||
            text[0] == '\t' || text[0] == '\r'))
            text = "'" + text;
        return "\"" + text.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }
}

using System.DirectoryServices;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using GPOSettingsExplorer.Models;

namespace GPOSettingsExplorer.Services;

/// <summary>
/// Explicit multi-DC read-only comparison of the same GPO's AD GPC version
/// and each named DC's own SYSVOL GPT.INI version.
/// Version equality is necessary evidence, NOT proof of full DFSR replication.
/// </summary>
public sealed record GpoDcVersionEvidence(
    string Dc, uint? AdVersion, uint? GptVersion,
    string GptIniSha256, string AdEvidence, string SysvolEvidence)
{
    public string Status =>
        AdVersion is null || GptVersion is null ? "Unknown" :
        AdVersion.Value == GptVersion.Value ? "Match" : "Mismatch";
    public string Versions =>
        $"AD={AdVersion?.ToString() ?? "unknown"} | GPT.INI={GptVersion?.ToString() ?? "unknown"}";
}

public sealed record GpoCrossDcConsistencyReport(
    Guid GpoId, string GpoName, string Domain,
    DateTimeOffset CapturedAt, IReadOnlyList<GpoDcVersionEvidence> Controllers)
{
    public string Summary => GpoCrossDcConsistencyService.Assess(Controllers);
    public string ToText() =>
        $"GPO CROSS-DC VERSION COMPARISON (READ ONLY)\nCaptured: {CapturedAt:O}\n" +
        $"Domain: {Domain}\nGPO: {GpoName} ({GpoId:B})\n{Summary}\n" +
        "Version equality does NOT prove SYSVOL file replication, Security Filtering, " +
        "DFSR backlog, CSE correctness or effective client RSoP.\n\n" +
        string.Join("\n", Controllers.Select(row =>
            $"[{row.Status}] {row.Dc}: {row.Versions} | SHA-256: {row.GptIniSha256} | " +
            $"AD: {row.AdEvidence} | SYSVOL: {row.SysvolEvidence}"));
}

public static class GpoCrossDcConsistencyService
{
    private static readonly Regex DcName = new(
        @"^[a-zA-Z0-9](?:[a-zA-Z0-9.-]{0,251}[a-zA-Z0-9])?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static string[] ValidateControllers(string input, string domain)
    {
        var values = input.Split(new[] { ',', ';', '\r', '\n', '\t', ' ' },
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (values.Length == 0 || values.Length > 16)
            throw new ArgumentException("Specify from 1 to 16 explicit DC hostnames.");
        foreach (var host in values)
            if (!DcName.IsMatch(host) || host.Contains("..", StringComparison.Ordinal) ||
                host.Equals(domain, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException(
                    "Use actual DC hostnames/FQDNs, not a domain DFS alias, UNC path or IP range: " + host);
        return values;
    }

    public static string Assess(IReadOnlyList<GpoDcVersionEvidence> controllers)
    {
        if (controllers.Count == 0)
            return "UNKNOWN: no domain controllers inspected.";
        if (controllers.Any(item => item.Status == "Mismatch"))
            return "MISMATCH: AD and GPT.INI version differ on at least one DC.";
        if (controllers.Any(item => item.Status == "Unknown"))
            return "INCOMPLETE: one or more DCs could not provide both version values.";
        if (controllers.Count == 1)
            return "SINGLE DC: AD and GPT.INI versions match locally; no cross-DC conclusion.";

        var distinct = controllers.Select(item => (item.AdVersion, item.GptVersion))
            .Distinct().Count();
        if (distinct != 1)
            return "CROSS-DC VERSION DRIFT: values disagree between controllers.";
        var distinctHashes = controllers.Select(item => item.GptIniSha256)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase).Count();
        if (distinctHashes > 1)
            return "VERSION MATCH, DIFFERENT GPT.INI BYTES: review differing SHA-256 " +
                "across DCs even though the version values agree.";
        return $"VERSION MATCH: {controllers.Count} selected DCs agree on version numbers; " +
            "this is NOT a full replication or policy-content proof.";
    }

    public static GpoCrossDcConsistencyReport Compare(
        GpoInfo gpo, string domainDn, IReadOnlyList<string> hosts,
        CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(gpo);
        if (hosts.Count is < 1 or > 16)
            throw new ArgumentException("Use 1 to 16 domain controllers.");
        var context = DomainConnectionState.Context;
        if (context is null || !context.DomainName.Equals(gpo.DomainName,
                StringComparison.OrdinalIgnoreCase) ||
            context.DomainDistinguishedName != domainDn)
            throw new InvalidOperationException("Current AD connection has changed.");
        var validated = ValidateControllers(string.Join("\n", hosts), gpo.DomainName);
        var result = new List<GpoDcVersionEvidence>();
        var id = gpo.Id.ToString("B").ToUpperInvariant();
        var dn = $"CN={id},CN=Policies,CN=System,{domainDn}";

        foreach (var dc in validated)
        {
            cancellation.ThrowIfCancellationRequested();
            uint? ad = null;
            uint? gpt = null;
            var hash = "";
            var adDetails = "";
            var fileDetails = "";
            try
            {
                using var entry = new DirectoryEntry($"LDAP://{dc}/{dn}");
                entry.RefreshCache(new[] { "versionNumber", "gPCFileSysPath" });
                if (entry.Properties["versionNumber"].Value is { } raw)
                {
                    ad = unchecked((uint)Convert.ToInt32(raw, CultureInfo.InvariantCulture));
                    adDetails = "AD versionNumber read from explicit DC.";
                }
                else
                    adDetails = "Missing GPC versionNumber.";

                var path = Convert.ToString(entry.Properties["gPCFileSysPath"].Value) ?? "";
                if (!path.Replace('/', '\\').TrimEnd('\\')
                    .EndsWith(@"\Policies\" + id, StringComparison.OrdinalIgnoreCase))
                {
                    ad = null;
                    adDetails = "GPC path missing or wrong GPO GUID; comparison blocked.";
                }
            }
            catch (Exception ex)
            {
                adDetails = $"{ex.GetType().Name}: {ex.Message}";
            }

            cancellation.ThrowIfCancellationRequested();
            var ini = $@"\\{dc}\SYSVOL\{gpo.DomainName}\Policies\{id}\GPT.INI";
            try
            {
                if (!File.Exists(ini))
                    fileDetails = "GPT.INI absent or inaccessible on explicit DC.";
                else
                {
                    var info = new FileInfo(ini);
                    if (info.Length > GptIniVersionParser.MaxBytes)
                        fileDetails = "GPT.INI exceeds bounded read size.";
                    else
                    {
                        var before = info.LastWriteTimeUtc;
                        var bytes = File.ReadAllBytes(ini);
                        var parsed = GptIniVersionParser.Parse(bytes);
                        if (!parsed.Valid || File.GetLastWriteTimeUtc(ini) != before)
                            fileDetails = parsed.Valid
                                ? "GPT.INI timestamp changed during read; retry."
                                : parsed.Error;
                        else
                        {
                            gpt = parsed.Version;
                            hash = Convert.ToHexString(SHA256.HashData(bytes));
                            fileDetails = "Version read from DC-specific SYSVOL UNC; SHA-256 captured.";
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                fileDetails = $"{ex.GetType().Name}: {ex.Message}";
            }
            result.Add(new GpoDcVersionEvidence(
                dc, ad, gpt, hash, adDetails, fileDetails));
        }
        return new GpoCrossDcConsistencyReport(gpo.Id, gpo.DisplayName,
            gpo.DomainName, DateTimeOffset.Now, result);
    }
}

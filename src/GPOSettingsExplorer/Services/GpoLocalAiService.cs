using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GPOSettingsExplorer.Services;

/// <summary>
/// Optional LOCAL inference via explicitly installed loopback-only Ollama.
/// Sends ONLY aggregate severity/type counts, never names, paths, SID, script
/// bodies, domain name, registry values, passwords or credentials. No server
/// selection, no public DNS, no automatic model downloads, no policy writes.
/// </summary>
public static class GpoLocalAiService
{
    private static readonly Uri LocalEndpoint =
        new("http://127.0.0.1:11434/api/generate");
    private static readonly Regex ModelName = new(
        @"^[A-Za-z0-9_.:/-]{1,100}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static string BuildRedactedPrompt(GpoSecurityScan report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var categories = report.Findings
            .GroupBy(f => (f.Severity, f.Category))
            .OrderBy(g => g.Key.Severity, StringComparer.Ordinal)
            .ThenBy(g => g.Key.Category, StringComparer.Ordinal)
            .Take(25)
            .Select(g =>
            {
                // Only these built-in scanner identifiers may enter a prompt.
                var category = g.Key.Category switch
                {
                    "Legacy GPP cpassword" => "Legacy GPP credential metadata",
                    "Script content pattern" => "Script review markers",
                    "Unreadable GPP XML" => "Unreadable preference XML",
                    "Unreadable script" => "Unreadable scripts",
                    "Scan limit" => "Unscanned files beyond bounded cap",
                    _ => "Other / unknown"
                };
                var severity = g.Key.Severity switch
                {
                    "Critical" => "Critical",
                    "Review" => "Review",
                    _ => "Unknown"
                };
                return severity + " " + category + ": " + g.Count();
            });
        return "You are a local advisory security assistant. Provide a short " +
               "plain-language diagnostic checklist in Russian. Do not claim " +
               "these heuristic findings are confirmed compromise. Never " +
               "generate scripts that change GPO or disable security. " +
               "Never infer hidden domain identities from aggregates.\n" +
               "Read-only scan complete: " + report.Complete +
               "\nFinding counts:\n" + string.Join("\n", categories);
    }

    /// <summary>
    /// An allowlisted, count-only prompt. It cannot contain GPO names, OU
    /// paths, DC hostnames, SIDs, raw Registry.pol, group members or scripts.
    /// Neither the full report nor a user-entered prompt is sent to the model.
    /// </summary>
    public static string BuildRedactedUnifiedPrompt(GpoUnifiedPlatformReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        return "You are an optional local Group Policy triage advisor. " +
            "Write a concise Russian diagnostic checklist based ONLY on " +
            "bounded aggregate observations. Unknown evidence is not a PASS. " +
            "Do not infer GPO identities or advise policy writes, disabling " +
            "security, performing repairs, or generating executable scripts. " +
            "Do not claim client RSoP, domain replication or compliance verified.\n" +
            "Stored source records: " + report.StoredRows + "\n" +
            "Exact projected values: " + report.ExactRows + "\n" +
            "Source partial: " + report.SourcePartial + "\n" +
            "Unreadable/unavailable files: " + report.UnavailableFiles + "\n" +
            "Security checked: " + report.SecurityReviewed + "\n" +
            "Security scan complete: " + report.SecurityComplete + "\n" +
            "Critical/review/unknown findings: " +
            report.CriticalSecurityFindings + "/" +
            report.ReviewSecurityFindings + "/" +
            report.UnknownSecurityFindings + "\n" +
            "AD/SYSVOL health checked: " + report.HealthReviewed + "\n" +
            "Health errors/warnings/unknown: " +
            report.HealthErrors + "/" + report.HealthWarnings + "/" +
            report.HealthUnknowns + "\n" +
            "Direct links checked: " + report.LinksReviewed + "\n" +
            "Link inventory complete: " + report.LinkInventoryComplete + "\n" +
            "Direct/enabled link counts: " +
            report.DirectLinks + "/" + report.EnabledLinks + "\n" +
            "Intune mapping supplied: " + report.IntuneMappingLoaded + "\n" +
            "Exact eligible Registry.pol rows and explicit CSP candidates: " +
            report.EligibleRegistryRows + "/" + report.MappedIntuneCandidates +
            "\nAll data is count-only, non-atomic stored evidence; effective " +
            "policy, link inheritance and WMI/ACL processing are unknown.";
    }

    public static Task<string> ExplainUnifiedAsync(
        GpoUnifiedPlatformReport report, string model,
        CancellationToken cancellation = default) =>
        SendRedactedAsync(BuildRedactedUnifiedPrompt(report), model, cancellation);

    public static Task<string> ExplainAsync(
        GpoSecurityScan report, string model,
        CancellationToken cancellation = default) =>
        SendRedactedAsync(BuildRedactedPrompt(report), model, cancellation);

    private static async Task<string> SendRedactedAsync(
        string prompt, string model, CancellationToken cancellation)
    {
        if (string.IsNullOrWhiteSpace(model) || !ModelName.IsMatch(model))
            throw new ArgumentException("Enter the exact name of an installed LOCAL model.");
        if (prompt.Length > 32_000)
            throw new InvalidDataException("Anonymized prompt is unexpectedly large.");

        // No proxy, no HTTP redirects, no alternate host and no DNS resolution.
        // Loopback-only endpoint cannot be replaced from user settings, and
        // automatic pulling/installing models is never attempted.
        using var handler = new HttpClientHandler
        {
            AllowAutoRedirect = false,
            UseProxy = false,
            UseCookies = false
        };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(95) };
        using var request = new HttpRequestMessage(HttpMethod.Post, LocalEndpoint)
        {
            Content = JsonContent.Create(new
            {
                model,
                prompt,
                stream = false,
                options = new { num_predict = 700 }
            })
        };
        using var response = await client.SendAsync(request,
            HttpCompletionOption.ResponseHeadersRead, cancellation);
        if (!response.IsSuccessStatusCode)
            throw new IOException("Local inference did not succeed (HTTP " +
                (int)response.StatusCode + "). Install/start Ollama and the model " +
                "manually. No request was redirected to another endpoint.");

        const int cap = 256 * 1024;
        if (response.Content.Headers.ContentLength > cap)
            throw new InvalidDataException("Local model output exceeds 256 KiB.");
        await using var body = await response.Content.ReadAsStreamAsync(cancellation);
        using var bounded = new MemoryStream();
        var chunk = new byte[8192];
        int count;
        while ((count = await body.ReadAsync(chunk, cancellation)) > 0)
        {
            cancellation.ThrowIfCancellationRequested();
            if (bounded.Length + count > cap)
                throw new InvalidDataException("Local model response grew beyond 256 KiB.");
            bounded.Write(chunk, 0, count);
        }
        bounded.Position = 0;
        using var json = JsonDocument.Parse(bounded, new JsonDocumentOptions { MaxDepth = 8 });
        if (!json.RootElement.TryGetProperty("response", out var result) ||
            result.ValueKind != JsonValueKind.String)
            throw new InvalidDataException("Unexpected local model result.");
        var text = result.GetString() ?? "";
        if (text.Length > 40_000)
            text = text[..40_000] + "\n[Local output truncated.]";
        return "LOCAL AI ADVISORY - UNVERIFIED - READ ONLY\n\n" + text +
            "\n\nReview suggestions manually against trusted GPMC source, " +
            "actual client RSoP and authoritative Microsoft documentation. " +
            "No GPO, Intune or AD changes were made.";
    }
}

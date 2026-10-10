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

    public static async Task<string> ExplainAsync(
        GpoSecurityScan report, string model, CancellationToken cancellation = default)
    {
        if (string.IsNullOrWhiteSpace(model) || !ModelName.IsMatch(model))
            throw new ArgumentException("Enter the exact name of a locally installed AI model.");
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(95) };
        using var request = new HttpRequestMessage(HttpMethod.Post, LocalEndpoint)
        {
            Content = JsonContent.Create(new
            {
                model, prompt = BuildRedactedPrompt(report), stream = false,
                options = new { num_predict = 700 }
            })
        };
        using var response = await client.SendAsync(request,
            HttpCompletionOption.ResponseHeadersRead, cancellation);
        if (!response.IsSuccessStatusCode)
            throw new IOException("Local model unavailable (HTTP " + (int)response.StatusCode +
                "). Install/launch Ollama and pull the requested model manually.");
        if (response.Content.Headers.ContentLength > 256 * 1024)
            throw new InvalidDataException("Local model output is oversized.");
        var bytes = await response.Content.ReadAsByteArrayAsync(cancellation);
        if (bytes.Length > 256 * 1024)
            throw new InvalidDataException("Local model output grew beyond cap.");
        using var json = JsonDocument.Parse(bytes);
        if (!json.RootElement.TryGetProperty("response", out var result) ||
            result.ValueKind != JsonValueKind.String)
            throw new InvalidDataException("Unexpected local model result.");
        var text = result.GetString() ?? "";
        if (text.Length > 40_000)
            text = text[..40_000] + "\n[Local output truncated.]";
        return "LOCAL AI DRAFT — NOT VERIFIED, NO AUTOMATIC ACTION\n\n" + text +
               "\n\nRecheck every recommendation against authoritative Microsoft guidance.";
    }
}
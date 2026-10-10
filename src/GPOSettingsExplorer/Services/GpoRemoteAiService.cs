using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GPOSettingsExplorer.Services;

/// <summary>
/// Explicit cloud opt-in. The ONLY transmitted GPO content is an allowlisted
/// aggregate-count prompt; never raw XML, values, paths, SIDs or script bodies.
/// No custom hosts, user-provided URLs, redirects, proxies, key persistence,
/// automatic retries, or policy-changing commands are permitted.
/// </summary>
public enum GpoRemoteAiProvider
{
    OpenAI,
    AzureOpenAI
}

public static class GpoRemoteAiService
{
    private static readonly Uri OfficialOpenAiEndpoint =
        new("https://api.openai.com/v1/chat/completions");
    private static readonly Regex ResourceName = new(
        @"^[a-z0-9][a-z0-9-]{1,61}[a-z0-9]$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex ModelName = new(
        @"^[A-Za-z0-9_.:-]{1,100}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public const int MaxResponseBytes = 256 * 1024;

    public static Uri Endpoint(GpoRemoteAiProvider provider, string? azureResource)
    {
        if (provider == GpoRemoteAiProvider.OpenAI)
            return OfficialOpenAiEndpoint;
        if (provider != GpoRemoteAiProvider.AzureOpenAI)
            throw new ArgumentOutOfRangeException(nameof(provider));
        if (!ResourceName.IsMatch(azureResource ?? ""))
            throw new ArgumentException(
                "Enter an Azure OpenAI resource NAME (3-63 lowercase letters, digits or hyphens), not a URL.");
        // Azure OpenAI v1 uses an HTTPS endpoint under the provider-owned
        // openai.azure.com DNS suffix; resource text can never set a path/port.
        return new Uri("https://" + azureResource +
            ".openai.azure.com/openai/v1/chat/completions");
    }

    public static string RedactedPrompt(GpoUnifiedPlatformReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        // This builder is fixed schema and derived from numeric/boolean
        // observations only; there is NO free-form prompt input or source copy.
        return GpoLocalAiService.BuildRedactedUnifiedPrompt(report);
    }

    public static async Task<string> ExplainAsync(
        GpoUnifiedPlatformReport report,
        GpoRemoteAiProvider provider,
        string? azureResource,
        string model,
        string apiKey,
        CancellationToken cancellation = default)
    {
        if (string.IsNullOrWhiteSpace(model) || !ModelName.IsMatch(model))
            throw new ArgumentException(
                "Enter a supported model/deployment identifier (letters, digits, hyphens and punctuation only).");
        if (string.IsNullOrWhiteSpace(apiKey) ||
            apiKey.Length > 2048 ||
            apiKey.Any(char.IsControl))
            throw new ArgumentException("An API key must be provided for this one request.");
        var endpoint = Endpoint(provider, azureResource);
        var prompt = RedactedPrompt(report);
        if (prompt.Length > 32_000)
            throw new InvalidDataException("Redacted GPO prompt exceeded its safety limit.");

        // Enterprise proxies and 30x redirects are intentionally disabled:
        // a bearer credential is sent only to the selected official origin.
        using var handler = new HttpClientHandler
        {
            UseProxy = false,
            AllowAutoRedirect = false,
            UseCookies = false
        };
        using var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(95)
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        if (provider == GpoRemoteAiProvider.OpenAI)
            request.Headers.Authorization = new AuthenticationHeaderValue(
                "Bearer", apiKey);
        else
            request.Headers.TryAddWithoutValidation("api-key", apiKey);

        request.Content = JsonContent.Create(new
        {
            model,
            messages = new[]
            {
                new
                {
                    role = "system",
                    content = "You are an advisory Group Policy diagnostics assistant. " +
                              "Only analyze anonymized aggregate counts. Missing evidence " +
                              "must remain UNKNOWN. Give a short checklist in Russian. " +
                              "Do not propose actions that apply GPOs, weaken security, " +
                              "expose credentials or run scripts."
                },
                new { role = "user", content = prompt }
            },
            max_completion_tokens = 700,
            stream = false
        });

        using var response = await client.SendAsync(request,
            HttpCompletionOption.ResponseHeadersRead, cancellation);
        if (!response.IsSuccessStatusCode)
            throw new IOException(
                "The remote AI provider returned HTTP " +
                (int)response.StatusCode +
                ". No response/error body was read or logged. " +
                "Check provider access and model/deployment authorization.");
        if (response.Content.Headers.ContentLength > MaxResponseBytes)
            throw new InvalidDataException("Remote AI output exceeds 256 KiB.");

        await using var input = await response.Content.ReadAsStreamAsync(cancellation);
        using var memory = new MemoryStream();
        var chunk = new byte[8192];
        int received;
        while ((received = await input.ReadAsync(chunk, cancellation)) != 0)
        {
            cancellation.ThrowIfCancellationRequested();
            if (memory.Length + received > MaxResponseBytes)
                throw new InvalidDataException("Remote AI response grew beyond 256 KiB.");
            memory.Write(chunk, 0, received);
        }
        memory.Position = 0;
        using var json = JsonDocument.Parse(memory, new JsonDocumentOptions { MaxDepth = 12 });
        var root = json.RootElement;
        if (!root.TryGetProperty("choices", out var choices) ||
            choices.ValueKind != JsonValueKind.Array ||
            choices.GetArrayLength() == 0 ||
            !choices[0].TryGetProperty("message", out var message) ||
            !message.TryGetProperty("content", out var body) ||
            body.ValueKind != JsonValueKind.String)
            throw new InvalidDataException(
                "Remote model response did not contain a supported chat message.");
        var answer = body.GetString() ?? "";
        if (answer.Length > 40000)
            answer = answer[..40000] + "\n[Response truncated.]";
        return "REMOTE AI ADVISORY - UNVERIFIED - NO AUTOMATIC ACTION\n" +
            "Provider: " + provider + "\n\n" + answer +
            "\n\nVerify suggestions using GPMC, actual RSoP and Microsoft documentation. " +
            "Remote service received aggregated counts only; no AD/GPO values were sent " +
            "and no settings were changed.";
    }
}

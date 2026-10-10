using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Identity.Client;

namespace GPOSettingsExplorer.Services;

/// <summary>
/// A bounded snapshot of Intune policy metadata, not effective deployment.
/// Names, assignments and timestamps come from Graph /beta. They are not
/// linked to any GPO or target unless independently verified.
/// </summary>
public sealed record GpoIntuneCloudPolicy(
    string Name, string Platforms, int SettingCount,
    string Id = "", bool? IsAssigned = null,
    string Technologies = "", DateTimeOffset? ModifiedUtc = null);

public sealed record GpoIntuneCloudPreview(
    DateTimeOffset CapturedUtc, IReadOnlyList<GpoIntuneCloudPolicy> Policies,
    bool HasMore, int PagesRead = 1)
{
    public string ToText()
    {
        var lines = new List<string>
        {
            "MICROSOFT INTUNE CONFIGURATION POLICIES (READ ONLY)",
            "Microsoft Graph /beta; bounded, paginated configuration-policy metadata.",
            "Captured UTC: " + CapturedUtc.ToString("O"),
            "Pages fetched: " + PagesRead + "; policies read: " + Policies.Count,
            HasMore
                ? "INCOMPLETE: Graph has further results; page/item cap reached."
                : "No further Graph continuation in the bounded response.",
            "Policy names and inventory metadata may be sensitive: save locally only.",
            "Graph assignment flag is NOT proof that a target device received a policy.",
            "No GPO equivalence, CSP value compatibility, applied RSoP or migration " +
            "success is inferred.",
            ""
        };
        lines.AddRange(Policies.Select(p =>
            p.Name + " | " + p.Platforms + " / " + p.Technologies +
            " | settings " + (p.SettingCount < 0 ? "unknown" : p.SettingCount) +
            " | assigned " + (p.IsAssigned is null ? "unknown" :
                                p.IsAssigned.Value ? "yes" : "no") +
            " | modified UTC " + (p.ModifiedUtc?.ToUniversalTime().ToString("O") ?? "unknown")));
        return string.Join("\n", lines);
    }
}

public static class GpoIntuneGraphReadOnlyService
{
    private const int MaxPageBytes = 2 * 1024 * 1024;
    private const int MaxPages = 10;
    private const int MaxPolicies = 500;
    private const int MaxPagePolicies = 250;
    private const int MaxGraphUrlChars = 8192;
    private static readonly Uri FirstPageUri = new(
        "https://graph.microsoft.com/beta/deviceManagement/configurationPolicies?$top=50");
    private static readonly string[] Scopes =
    {
        "DeviceManagementConfiguration.Read.All"
    };

    public static async Task<GpoIntuneCloudPreview> QueryAsync(
        string tenantId, string publicClientId,
        Func<string, Task> showDeviceCode,
        CancellationToken cancellation = default)
    {
        if (!Guid.TryParse(tenantId?.Trim(), out _) ||
            !Guid.TryParse(publicClientId?.Trim(), out _))
            throw new ArgumentException(
                "Enter the Entra tenant GUID and public-client App Registration GUID. " +
                "Device-code flow and delegated Read permission must be configured.");
        ArgumentNullException.ThrowIfNull(showDeviceCode);

        var app = PublicClientApplicationBuilder.Create(publicClientId.Trim())
            .WithAuthority(AzureCloudInstance.AzurePublic, tenantId.Trim())
            .Build();
        var token = await app.AcquireTokenWithDeviceCode(
            Scopes, code => showDeviceCode(code.Message)).ExecuteAsync(cancellation);

        // Never forward bearer credentials through HTTP redirects or to any
        // arbitrary @odata.nextLink. The URL validator pins all pages to
        // this exact Microsoft Graph collection on the global cloud.
        using var handler = new HttpClientHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            AutomaticDecompression = DecompressionMethods.GZip |
                                     DecompressionMethods.Deflate
        };
        using var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
        return await ReadPagesAsync(async (uri, ct) =>
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Authorization =
                new AuthenticationHeaderValue("Bearer", token.AccessToken);
            request.Headers.Accept.ParseAdd("application/json");
            using var response = await client.SendAsync(request,
                HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException(
                    "Microsoft Graph read-only request failed (HTTP " +
                    (int)response.StatusCode + "). " +
                    "Check Intune license, delegated permission, network and throttling. " +
                    "Redirects are intentionally not followed.");
            if (response.Content.Headers.ContentLength > MaxPageBytes)
                throw new InvalidDataException("Graph page exceeds 2 MiB read cap.");
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var buffer = new MemoryStream();
            var chunk = new byte[16 * 1024];
            int read;
            while ((read = await stream.ReadAsync(chunk.AsMemory(), ct)) > 0)
            {
                if (buffer.Length + read > MaxPageBytes)
                    throw new InvalidDataException("Graph page grew past 2 MiB read cap.");
                buffer.Write(chunk, 0, read);
            }
            return buffer.ToArray();
        }, cancellation);
    }

    /// <summary>
    /// Testable paginated collector. Every continuation is validated before
    /// invoking the caller's reader. No token or HTTP client is handled here.
    /// </summary>
    public static async Task<GpoIntuneCloudPreview> ReadPagesAsync(
        Func<Uri, CancellationToken, Task<byte[]>> fetch,
        CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(fetch);
        var policies = new List<GpoIntuneCloudPolicy>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        Uri? uri = FirstPageUri;
        var pages = 0;
        while (uri is not null && pages < MaxPages && policies.Count < MaxPolicies)
        {
            cancellation.ThrowIfCancellationRequested();
            uri = ValidateContinuation(uri.AbsoluteUri);
            if (!visited.Add(uri.AbsoluteUri))
                throw new InvalidDataException(
                    "Microsoft Graph continuation loop detected. Results discarded.");
            var body = await fetch(uri, cancellation);
            var (page, next) = ParsePageEnvelope(body);
            pages++;
            var capacity = MaxPolicies - policies.Count;
            if (page.Count > capacity)
            {
                policies.AddRange(page.Take(capacity));
                return new GpoIntuneCloudPreview(
                    DateTimeOffset.UtcNow, policies, true, pages);
            }
            policies.AddRange(page);
            uri = next;
        }
        return new GpoIntuneCloudPreview(
            DateTimeOffset.UtcNow, policies, uri is not null, pages);
    }

    /// <summary>Reject off-domain, downgraded, redirected or malformed next links.</summary>
    public static Uri ValidateContinuation(string url)
    {
        if (string.IsNullOrWhiteSpace(url) || url.Length > MaxGraphUrlChars ||
            !Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            !uri.Host.Equals("graph.microsoft.com", StringComparison.OrdinalIgnoreCase) ||
            uri.Port != 443 || !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Fragment) ||
            !uri.GetComponents(UriComponents.Path, UriFormat.Unescaped)
                .Equals("beta/deviceManagement/configurationPolicies",
                    StringComparison.Ordinal))
            throw new InvalidDataException(
                "Untrusted Microsoft Graph nextLink rejected. " +
                "Only the exact HTTPS configurationPolicies collection is permitted.");
        return uri;
    }

    /// <summary>Compatibility parser for a single safe Graph response page.</summary>
    public static GpoIntuneCloudPreview ParsePage(byte[] raw)
    {
        var (items, next) = ParsePageEnvelope(raw);
        return new GpoIntuneCloudPreview(DateTimeOffset.UtcNow, items,
            next is not null);
    }

    private static (IReadOnlyList<GpoIntuneCloudPolicy> Items, Uri? Next)
        ParsePageEnvelope(byte[] raw)
    {
        ArgumentNullException.ThrowIfNull(raw);
        if (raw.Length is 0 or > MaxPageBytes)
            throw new InvalidDataException("Graph policy response exceeds bounded size.");
        using var json = JsonDocument.Parse(raw);
        var root = json.RootElement;
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("value", out var values) ||
            values.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException(
                "Microsoft Graph did not return a configuration-policy list.");
        if (values.GetArrayLength() > MaxPagePolicies)
            throw new InvalidDataException(
                "Graph page exceeds bounded inventory record count.");

        Uri? next = null;
        if (root.TryGetProperty("@odata.nextLink", out var link))
        {
            if (link.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(link.GetString()))
                throw new InvalidDataException("Malformed Graph continuation URL.");
            next = ValidateContinuation(link.GetString()!);
        }

        var list = new List<GpoIntuneCloudPolicy>();
        foreach (var item in values.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("Malformed Graph policy metadata record.");
            string Get(string key, int max)
            {
                if (!item.TryGetProperty(key, out var value) ||
                    value.ValueKind != JsonValueKind.String)
                    return "";
                var rawValue = value.GetString() ?? "";
                return new string(rawValue.Take(max)
                    .Select(c => char.IsControl(c) ? ' ' : c).ToArray());
            }
            var id = Get("id", 100);
            if (id.Length > 0 && !Guid.TryParse(id, out _))
                throw new InvalidDataException("Graph policy ID is invalid.");
            var count = item.TryGetProperty("settingCount", out var number) &&
                        number.ValueKind == JsonValueKind.Number &&
                        number.TryGetInt32(out var parsed) && parsed >= 0
                ? parsed : -1;
            bool? assigned = item.TryGetProperty("isAssigned", out var assignment)
                ? assignment.ValueKind switch
                {
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    _ => null
                } : null;
            DateTimeOffset? modified =
                DateTimeOffset.TryParse(Get("lastModifiedDateTime", 80), out var time)
                    ? time.ToUniversalTime() : null;
            list.Add(new GpoIntuneCloudPolicy(
                Get("name", 256), Get("platforms", 100), count,
                id, assigned, Get("technologies", 100), modified));
        }
        return (list, next);
    }
}

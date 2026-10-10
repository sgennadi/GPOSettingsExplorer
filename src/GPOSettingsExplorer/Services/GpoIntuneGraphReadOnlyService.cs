using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Identity.Client;

namespace GPOSettingsExplorer.Services;

/// <summary>
/// Optional explicit Microsoft Graph preview. Requires operator-provided
/// Entra tenant ID, public client App Registration with device-code flow and
/// delegated Intune configuration READ permission. No writes, token export,
/// silent background login or automatic connection.
/// </summary>
public sealed record GpoIntuneCloudPolicy(string Name, string Platforms, int SettingCount);
public sealed record GpoIntuneCloudPreview(
    DateTimeOffset CapturedUtc, IReadOnlyList<GpoIntuneCloudPolicy> Policies,
    bool HasMore)
{
    public string ToText() =>
        "MICROSOFT INTUNE CONFIGURATION POLICY INVENTORY (READ ONLY)\n" +
        "Microsoft Graph /beta, deviceManagement/configurationPolicies, first page.\n" +
        "Captured UTC: " + CapturedUtc.ToString("O") +
        "\nPolicies loaded: " + Policies.Count +
        (HasMore ? " (pagination exists; report is incomplete)" : "") +
        "\nThe cloud inventory does NOT infer matching GPO values, clients, policy" +
        " assignments or Intune migration readiness.\n\n" +
        string.Join("\n", Policies.Select(p =>
            p.Name + " | " + p.Platforms + " | " +
            p.SettingCount + " setting(s)"));
}

public static class GpoIntuneGraphReadOnlyService
{
    private static readonly string[] Scopes =
    {
        "DeviceManagementConfiguration.Read.All"
    };

    public static async Task<GpoIntuneCloudPreview> QueryAsync(
        string tenantId, string publicClientId, Func<string, Task> showDeviceCode,
        CancellationToken cancellation = default)
    {
        if (!Guid.TryParse(tenantId?.Trim(), out _) ||
            !Guid.TryParse(publicClientId?.Trim(), out _))
            throw new ArgumentException(
                "Enter the tenant GUID and public-client App Registration GUID. " +
                "A preconfigured Entra App Registration and delegated Read permission are required.");
        ArgumentNullException.ThrowIfNull(showDeviceCode);

        var app = PublicClientApplicationBuilder.Create(publicClientId.Trim())
            .WithAuthority(AzureCloudInstance.AzurePublic, tenantId.Trim())
            .Build();
        var token = await app.AcquireTokenWithDeviceCode(
            Scopes, code => showDeviceCode(code.Message)).ExecuteAsync(cancellation);

        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(25) };
        using var request = new HttpRequestMessage(HttpMethod.Get,
            "https://graph.microsoft.com/beta/deviceManagement/configurationPolicies?$top=50");
        request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", token.AccessToken);
        request.Headers.Accept.ParseAdd("application/json");

        using var response = await client.SendAsync(request,
            HttpCompletionOption.ResponseHeadersRead, cancellation);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException(
                "Graph read-only request failed (HTTP " + (int)response.StatusCode +
                "). Verify Intune license and delegated permissions.");
        if (response.Content.Headers.ContentLength > 2 * 1024 * 1024)
            throw new InvalidDataException("Graph response exceeds 2 MiB cap.");
        await using var stream = await response.Content.ReadAsStreamAsync(cancellation);
        using var buffer = new MemoryStream();
        var block = new byte[16384];
        int count;
        while ((count = await stream.ReadAsync(block, cancellation)) > 0)
        {
            if (buffer.Length + count > 2 * 1024 * 1024)
                throw new InvalidDataException("Graph response grew beyond the read cap.");
            buffer.Write(block, 0, count);
        }
        return ParsePage(buffer.ToArray());
    }

    public static GpoIntuneCloudPreview ParsePage(byte[] raw)
    {
        ArgumentNullException.ThrowIfNull(raw);
        if (raw.Length > 2 * 1024 * 1024)
            throw new InvalidDataException("Graph policy response exceeds 2 MiB.");
        using var doc = JsonDocument.Parse(raw);
        var root = doc.RootElement;
        if (!root.TryGetProperty("value", out var values) ||
            values.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("Graph did not return a configuration-policy list.");
        var result = new List<GpoIntuneCloudPolicy>();
        foreach (var item in values.EnumerateArray().Take(50))
        {
            string Get(string key) =>
                item.TryGetProperty(key, out var value) &&
                value.ValueKind == JsonValueKind.String
                    ? value.GetString() ?? "" : "";
            var name = Get("name");
            if (name.Length > 256) name = name[..256];
            var platforms = Get("platforms");
            var settings = item.TryGetProperty("settingCount", out var number) &&
                           number.TryGetInt32(out var parsed) ? parsed : 0;
            result.Add(new(name, platforms, settings));
        }
        return new GpoIntuneCloudPreview(DateTimeOffset.UtcNow, result,
            root.TryGetProperty("@odata.nextLink", out _));
    }
}
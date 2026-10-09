using System.Net.Http;
using System.Net.Http.Headers;

namespace GPOSettingsExplorer.Services;

/// <summary>
/// Checks the actual GitHub API endpoint, not merely the presence of a network
/// adapter or an AD connection. Offline computers do not attempt release lookup
/// or suggest sending logs. A failed probe is silent and cached.
/// </summary>
public static class GitHubConnectivityService
{
    private static readonly HttpClient Client = CreateClient();
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static DateTime _lastCheckUtc = DateTime.MinValue;
    private static bool _lastResult;
    public static readonly TimeSpan CacheInterval = TimeSpan.FromMinutes(15);

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(4) };
        client.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("GPOSettingsExplorer", "1.0"));
        client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }

    public static async Task<bool> IsAvailableAsync(
        bool force = false, CancellationToken cancellationToken = default)
    {
        await Gate.WaitAsync(cancellationToken);
        try
        {
            if (!force && DateTime.UtcNow - _lastCheckUtc < CacheInterval)
                return _lastResult;

            _lastCheckUtc = DateTime.UtcNow;
            _lastResult = false;
            try
            {
                // A small GitHub-specific availability probe only. It does not
                // query release versions and never uploads diagnostic content.
                using var response = await Client.GetAsync(
                    "https://api.github.com/zen",
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);
                _lastResult = response.IsSuccessStatusCode;
            }
            catch (Exception ex) when (
                ex is HttpRequestException or TaskCanceledException
                      or OperationCanceledException or System.IO.IOException)
            {
                if (cancellationToken.IsCancellationRequested)
                    throw;
            }

            return _lastResult;
        }
        finally
        {
            Gate.Release();
        }
    }
}

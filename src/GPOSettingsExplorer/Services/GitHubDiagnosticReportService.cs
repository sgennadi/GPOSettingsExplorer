using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GPOSettingsExplorer.Services;

/// <summary>
/// Explicit opt-in, review-before-send diagnostics. Never uploads a raw ZIP,
/// a credential, or anything in SYSVOL. Issues are created only by a UI click.
/// </summary>
public static class GitHubDiagnosticReportService
{
    public const string Repository = "sgennadi/GPOSettingsExplorer";
    public const string IssuesUrl = "https://github.com/sgennadi/GPOSettingsExplorer/issues";
    private const int MaximumBodyLength = 54000;
    private static readonly Regex Email = new(
        @"(?i)\b[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}\b", RegexOptions.Compiled);
    private static readonly Regex IpV4 = new(
        @"(?<![\d.])(?:\d{1,3}\.){3}\d{1,3}(?![\d.])", RegexOptions.Compiled);
    private static readonly Regex LdapPart = new(
        @"(?i)\b(?:OU|CN|DC)=[^,;\r\n\\]+", RegexOptions.Compiled);
    private static readonly Regex Sid = new(
        @"(?i)\bS-1-\d+(?:-\d+){2,}\b", RegexOptions.Compiled);
    private static readonly Regex Unc = new(
        @"\\\\[A-Za-z0-9_.-]+\\[^\s<>""\r\n]*", RegexOptions.Compiled);
    private static readonly Regex WinUser = new(
        @"(?i)(?:[A-Z]:\\Users\\|C:\\Documents and Settings\\)[^\\\r\n]+", RegexOptions.Compiled);
    private static readonly Regex CredentialLine = new(
        @"(?im)^.*(?:password|recovery.?key|secret|bearer|token|credential|private.?key|authorization)\s*[:=].*$",
        RegexOptions.Compiled);

    private static string TokenPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "GPOSettingsExplorer", "github-issues-token.dpapi");

    public static string? LoadToken()
    {
        if (!File.Exists(TokenPath))
            return null;

        try
        {
            return Encoding.UTF8.GetString(
                ProtectedData.Unprotect(File.ReadAllBytes(TokenPath),
                    Encoding.UTF8.GetBytes("GPOSettingsExplorer GitHub issues v1"),
                    DataProtectionScope.CurrentUser));
        }
        catch (CryptographicException) { return null; }
        catch (IOException) { return null; }
    }

    public static void SaveToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            throw new ArgumentException("GitHub access token is required.", nameof(token));

        Directory.CreateDirectory(Path.GetDirectoryName(TokenPath)!);
        var encrypted = ProtectedData.Protect(Encoding.UTF8.GetBytes(token.Trim()),
            Encoding.UTF8.GetBytes("GPOSettingsExplorer GitHub issues v1"),
            DataProtectionScope.CurrentUser);
        File.WriteAllBytes(TokenPath, encrypted);
    }

    public static void ClearToken()
    {
        if (File.Exists(TokenPath))
            File.Delete(TokenPath);
    }

    public static string Sanitize(string source)
    {
        var value = CredentialLine.Replace(source, "[REDACTED: confidential setting or credential]");
        value = Email.Replace(value, "[REDACTED: email]");
        value = WinUser.Replace(value, @"C:\Users\[REDACTED]");
        value = Unc.Replace(value, @"\\[REDACTED-SERVER]\[REDACTED-PATH]");
        value = LdapPart.Replace(value, match =>
            match.Value[..match.Value.IndexOf('=')] + "=[REDACTED]");
        value = Sid.Replace(value, "[REDACTED: SID]");
        value = IpV4.Replace(value, "[REDACTED: IP]");
        var user = Environment.UserName;
        if (!string.IsNullOrWhiteSpace(user) && user.Length > 2)
            value = value.Replace(user, "[REDACTED: username]", StringComparison.OrdinalIgnoreCase);
        var machine = Environment.MachineName;
        if (!string.IsNullOrWhiteSpace(machine) && machine.Length > 2)
            value = value.Replace(machine, "[REDACTED: host]", StringComparison.OrdinalIgnoreCase);
        return value;
    }

    public static string BuildPreview(string? mmcAuditFile = null, string? hierarchyFile = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine("## GPO Settings Explorer diagnostic report");
        sb.AppendLine();
        sb.AppendLine("Submitted explicitly by a user from the Diagnostics window.");
        sb.AppendLine("User previewed and approved the sanitized text.");
        sb.AppendLine($"Version: {Assembly.GetExecutingAssembly().GetName().Version}");
        sb.AppendLine($"OS: {Environment.OSVersion.VersionString}");
        sb.AppendLine($"Architecture: {System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}");
        sb.AppendLine($"Generated (UTC): {DateTimeOffset.UtcNow:O}");
        sb.AppendLine();
        sb.AppendLine("### MMC route audit");
        AppendFile(sb, mmcAuditFile ?? MostRecent("MmcRouteAudit-*.txt", StoragePaths.Audit), 26000);
        sb.AppendLine();
        sb.AppendLine("### GPO hierarchy / link order");
        AppendFile(sb, hierarchyFile ?? MostRecent("GpoHierarchy-*.txt", StoragePaths.Audit), 8500);
        sb.AppendLine();
        sb.AppendLine("### Recent application errors");
        var recent = Directory.Exists(CrashLogService.LogDirectory)
            ? Directory.EnumerateFiles(CrashLogService.LogDirectory, "error-*.log")
                .OrderByDescending(File.GetLastWriteTimeUtc).Take(5).ToArray()
            : Array.Empty<string>();

        foreach (var file in recent)
        {
            sb.AppendLine($"#### Log recorded at {File.GetLastWriteTimeUtc(file):O}");
            AppendFile(sb, file, 3700);
        }

        if (recent.Length == 0)
            sb.AppendLine("No recent error logs.");

        var report = Sanitize(sb.ToString());
        if (report.Length > MaximumBodyLength)
            report = report[..MaximumBodyLength] + "\n\n[REPORT TRUNCATED: size cap]";
        return report;
    }

    private static string? MostRecent(string pattern, string folder) =>
        Directory.Exists(folder)
            ? Directory.EnumerateFiles(folder, pattern)
                .OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault()
            : null;

    private static void AppendFile(StringBuilder target, string? path, int limit)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            target.AppendLine("Not collected yet. Use MMC Route Audit / Hierarchy Export first.");
            return;
        }

        try
        {
            var text = File.ReadAllText(path);
            if (text.Length > limit)
                text = text[..limit] + "\n[SECTION TRUNCATED]";
            target.AppendLine(new string((char)96, 3) + "text");
            target.AppendLine(text.Replace(new string((char)96, 3), "[code delimiter]", StringComparison.Ordinal));
            target.AppendLine(new string((char)96, 3));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            target.AppendLine("Local diagnostic log could not be read: " + ex.GetType().Name);
        }
    }

    public static async Task<string> CreateIssueAsync(
        string token, string report, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException(
                "A GitHub fine-grained personal access token with Issues: Read and write permission is required.");
        if (string.IsNullOrWhiteSpace(report))
            throw new InvalidOperationException("Report is empty.");
        if (report.Length > 64000)
            throw new InvalidOperationException("Report exceeds the GitHub issue size limit.");

        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(35) };
        client.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("GPOSettingsExplorer", "1.0"));
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token.Trim());
        client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");

        var title = "[Diagnostics] MMC navigation and GPO policy audit";
        using var payload = new StringContent(
            JsonSerializer.Serialize(new { title, body = report }),
            Encoding.UTF8, "application/json");
        using var response = await client.PostAsync(
            $"https://api.github.com/repos/{Repository}/issues", payload, cancellationToken);

        var result = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(
                $"GitHub rejected the diagnostic issue (HTTP {(int)response.StatusCode}). " +
                "Check repository Issues permission, network connection and token validity. " +
                "No log was uploaded.");
        using var json = JsonDocument.Parse(result);
        if (!json.RootElement.TryGetProperty("html_url", out var property))
            throw new IOException("GitHub response did not contain an issue URL.");
        var url = property.GetString();
        if (string.IsNullOrWhiteSpace(url) ||
            !url.StartsWith(IssuesUrl + "/", StringComparison.OrdinalIgnoreCase))
            throw new IOException("GitHub returned an unexpected issue URL.");
        return url;
    }
}

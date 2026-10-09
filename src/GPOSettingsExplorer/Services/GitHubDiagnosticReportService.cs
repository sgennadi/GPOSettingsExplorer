using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GPOSettingsExplorer.Services;

/// <summary>
/// GitHub is public: default reports contain only technical metadata and
/// SHA-256 signatures. Optional redacted excerpts require explicit opt-in,
/// an editable preview, and a second confirmation before any HTTP POST.
/// </summary>
public static class GitHubDiagnosticReportService
{
    public const string Repository = "sgennadi/GPOSettingsExplorer";
    private const int MaximumIssueBody = 54000;

    private static readonly Regex CredentialLine = new(
        @"(?im)^.*(?:password|secret|recovery.?key|credential|bearer|token|authorization|private.?key)\s*[:=].*$",
        RegexOptions.Compiled);
    private static readonly Regex Email = new(
        @"(?i)\b[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}\b", RegexOptions.Compiled);
    private static readonly Regex Unc = new(
        @"\\\\[^\s<>""\r\n]+", RegexOptions.Compiled);
    private static readonly Regex DomainName = new(
        @"(?i)\b(?:[a-z0-9-]+\.)+(?:local|lan|internal|ac\.il|edu|corp)\b",
        RegexOptions.Compiled);
    private static readonly Regex UserPath = new(
        @"(?i)\b[A-Z]:\\Users\\[^\\\r\n]+", RegexOptions.Compiled);
    private static readonly Regex Ldap = new(
        @"(?i)\b(?:OU|CN|DC)=[^,;\r\n\\]+", RegexOptions.Compiled);
    private static readonly Regex IpV4 = new(
        @"(?<![\d.])(?:\d{1,3}\.){3}\d{1,3}(?![\d.])", RegexOptions.Compiled);
    private static readonly Regex Sid = new(
        @"(?i)\bS-1-\d+(?:-\d+){2,}\b", RegexOptions.Compiled);
    private static readonly Regex GuidValue = new(
        @"(?i)\b[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\b",
        RegexOptions.Compiled);
    private static readonly Regex HostUser = new(
        @"(?i)\b[A-Za-z0-9_.-]+\\[A-Za-z0-9_.-]+\b", RegexOptions.Compiled);

    private static string PrivateDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "GPOSettingsExplorer", "DiagnosticQueue");

    private static string TokenPath => Path.Combine(PrivateDirectory, "github-token.dpapi");
    private static string ReporterIdPath => Path.Combine(PrivateDirectory, "reporter-id.txt");
    private static readonly byte[] TokenEntropy =
        Encoding.UTF8.GetBytes("GPOSettingsExplorer.Diagnostics.Issues.v1");

    public static string? LoadToken()
    {
        try
        {
            if (!File.Exists(TokenPath))
                return null;
            return Encoding.UTF8.GetString(ProtectedData.Unprotect(
                File.ReadAllBytes(TokenPath), TokenEntropy, DataProtectionScope.CurrentUser));
        }
        catch (Exception ex) when (ex is IOException or CryptographicException
                                         or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static void SaveToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            throw new ArgumentException("GitHub Issues token is empty.", nameof(token));

        Directory.CreateDirectory(PrivateDirectory);
        var protectedBytes = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(token.Trim()), TokenEntropy, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(TokenPath, protectedBytes);
    }

    public static void ClearToken()
    {
        if (File.Exists(TokenPath))
            File.Delete(TokenPath);
    }

    private static string ReporterId()
    {
        Directory.CreateDirectory(PrivateDirectory);
        if (File.Exists(ReporterIdPath))
        {
            var stored = File.ReadAllText(ReporterIdPath).Trim();
            if (Guid.TryParse(stored, out var parsed))
                return parsed.ToString("D");
        }

        // Random per-installation ID, never a machine name, SID or domain ID.
        var newId = Guid.NewGuid().ToString("D");
        File.WriteAllText(ReporterIdPath, newId);
        return newId;
    }

    public static string Redact(string raw)
    {
        var value = CredentialLine.Replace(raw,
            "[REDACTED: confidential value or credential]");
        value = Email.Replace(value, "[REDACTED: email]");
        value = Unc.Replace(value, "[REDACTED: network path]");
        value = UserPath.Replace(value, @"C:\Users\[REDACTED]");
        value = Ldap.Replace(value, match =>
            match.Value[..match.Value.IndexOf('=')] + "=[REDACTED]");
        value = DomainName.Replace(value, "[REDACTED: domain]");
        value = IpV4.Replace(value, "[REDACTED: IP]");
        value = Sid.Replace(value, "[REDACTED: SID]");
        value = GuidValue.Replace(value, "[REDACTED: object ID]");
        value = HostUser.Replace(value, "[REDACTED: account]");
        foreach (var personal in new[] { Environment.MachineName, Environment.UserName })
        {
            if (personal.Length > 2)
                value = value.Replace(personal, "[REDACTED: identity]",
                    StringComparison.OrdinalIgnoreCase);
        }
        return value;
    }

    public static string BuildReport(
        IReadOnlyList<PendingDiagnosticLog> files, bool includeRedactedExcerpts = false)
    {
        if (files.Count == 0)
            throw new ArgumentException("No pending diagnostic files were selected.");

        var issues = new List<(string Kind, string Fingerprint, string ExceptionType,
            string Version, string FileSha256, string Excerpt)>();

        foreach (var file in files.Take(100))
        {
            string source = "";
            try
            {
                // Read-only, bounded: never package raw SYSVOL files.
                if (File.Exists(file.Path))
                {
                    using var reader = new StreamReader(file.Path);
                    var buffer = new char[14000];
                    var length = reader.Read(buffer, 0, buffer.Length);
                    source = new string(buffer, 0, length);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }

            var type = Regex.Match(source,
                @"(?m)\b(?:System\.)?[A-Za-z][\w.]*(?:Exception|Error)\b")
                .Value;
            if (string.IsNullOrWhiteSpace(type))
                type = "Unclassified";
            var version = Regex.Match(source,
                @"(?im)^Version:\s*(\d+(?:\.\d+){2,3})\s*$").Groups[1].Value;
            if (string.IsNullOrWhiteSpace(version))
                version = "unknown";

            var context = Regex.Match(source,
                @"(?im)^(?:Context|Operation):\s*(.{1,150})$")
                .Groups[1].Value;
            var identity = file.Kind + "|" + type + "|" + context.Trim();
            var signature = Convert.ToHexString(SHA256.HashData(
                Encoding.UTF8.GetBytes(identity.ToLowerInvariant())));
            var excerpt = includeRedactedExcerpts
                ? Redact(source[..Math.Min(source.Length, 2200)])
                : "";
            issues.Add((file.Kind, signature, type, version,
                file.Sha256, excerpt));
        }

        var fingerprints = issues.Select(i => i.Fingerprint)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToArray();
        var batchFingerprint = Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(string.Join("|", fingerprints))));
        var sb = new StringBuilder();
        sb.AppendLine("## GPO Settings Explorer - opt-in diagnostics");
        sb.AppendLine($"App-Version: {Assembly.GetExecutingAssembly().GetName().Version}");
        sb.AppendLine($"Reporter-ID: {ReporterId()}");
        sb.AppendLine($"Diagnostic-Fingerprint: {batchFingerprint}");
        sb.AppendLine($"Created-UTC: {DateTimeOffset.UtcNow:O}");
        sb.AppendLine($"Machine-architecture: {System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}");
        sb.AppendLine($"Diagnostic-files: {files.Count}");
        sb.AppendLine("Repository is PUBLIC. No unredacted SYSVOL, user names or credentials should be submitted.");
        sb.AppendLine();
        sb.AppendLine("### Grouped signatures");
        foreach (var group in issues.GroupBy(i => i.Fingerprint)
                     .OrderByDescending(g => g.Count()))
        {
            var item = group.First();
            sb.AppendLine($"- Type: {item.Kind}; Exception: {item.ExceptionType};" +
                          $" App version: {item.Version}; Count: {group.Count()};" +
                          $" Fingerprint: {group.Key}");
        }

        sb.AppendLine();
        sb.AppendLine("### MMC / GPO path diagnostics");
        foreach (var item in issues.Where(i => i.Kind != "Error"))
            sb.AppendLine($"- {item.Kind}: SHA256 {item.FileSha256[..16]}...");

        if (includeRedactedExcerpts)
        {
            sb.AppendLine();
            sb.AppendLine("### Operator-selected redacted excerpts");
            sb.AppendLine("Check this section and remove anything confidential before clicking Send.");
            foreach (var item in issues.Where(i => !string.IsNullOrWhiteSpace(i.Excerpt)).Take(12))
            {
                sb.AppendLine($"#### {item.Kind} / {item.ExceptionType}");
                sb.AppendLine(item.Excerpt);
            }
        }

        var text = sb.ToString();
        return text.Length <= MaximumIssueBody ? text :
            text[..MaximumIssueBody] + "\n[TRUNCATED]";
    }

    public static async Task<string> CreateIssueAsync(
        string token, string report, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException("GitHub token with Issues: Read and write is required.");
        if (string.IsNullOrWhiteSpace(report) || report.Length > MaximumIssueBody + 100)
            throw new InvalidOperationException("The diagnostic report is empty or too large.");

        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(35) };
        client.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("GPOSettingsExplorer", "1.0"));
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token.Trim());
        client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");

        var sig = Regex.Match(report,
            @"(?im)^Diagnostic-Fingerprint:\s*([A-Fa-f0-9]{16,64})$")
            .Groups[1].Value;
        var title = "[Diagnostics] " +
            (sig.Length >= 12 ? sig[..12] : "manual") + " - GPO Settings Explorer";
        using var payload = new StringContent(
            JsonSerializer.Serialize(new { title, body = report }),
            Encoding.UTF8, "application/json");

        using var response = await client.PostAsync(
            $"https://api.github.com/repos/{Repository}/issues",
            payload, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new IOException($"GitHub issue was not accepted (HTTP {(int)response.StatusCode})." +
                " Check token permissions and repository access. Local logs are unchanged.");

        using var parsed = JsonDocument.Parse(body);
        if (!parsed.RootElement.TryGetProperty("html_url", out var urlValue))
            throw new IOException("GitHub response did not contain a confirmed issue URL.");
        var url = urlValue.GetString() ?? "";
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            uri.Host != "github.com" ||
            !uri.AbsolutePath.StartsWith("/sgennadi/GPOSettingsExplorer/issues/",
                StringComparison.OrdinalIgnoreCase))
            throw new IOException("GitHub returned an unexpected issue URL.");

        return url;
    }
}

using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using GPOSettingsExplorer.Models;

namespace GPOSettingsExplorer.Services;

public sealed record GpoSecurityFinding(
    string Severity, string Category, string RelativeFile, string Evidence);

public sealed record GpoSecurityScan(
    Guid GpoId, DateTimeOffset CapturedUtc, string Root, bool Complete,
    IReadOnlyList<GpoSecurityFinding> Findings)
{
    public string ToText() =>
        "GPO SECURITY SOURCE INSPECTION (READ ONLY)\n" +
        "Captured UTC: " + CapturedUtc.ToString("O") + "\n" +
        "GPO: " + GpoId.ToString("B") + "\n" +
        "Coverage: " + (Complete ? "bounded scan finished" : "INCOMPLETE") + "\n" +
        "Findings indicate review candidates, not confirmed compromise.\n" +
        "No password, secret or script body is copied to this report.\n\n" +
        string.Join("\n", Findings.Select(x =>
            "[" + x.Severity + "] " + x.Category + " | " +
            x.RelativeFile + " | " + x.Evidence));
}

/// <summary>
/// Offline/live-readonly security triage. Inspects specifically supported GPP
/// XML and startup/login scripts, never executes their contents. Avoids any
/// password/body disclosure in findings or exports.
/// </summary>
public static class GpoSecurityScannerService
{
    public const int MaxXmlFiles = 300;
    public const int MaxScripts = 160;
    public const int MaxScriptBytes = 2 * 1024 * 1024;

    public static GpoSecurityScan Scan(
        string gpoRoot, Guid gpoId, CancellationToken cancellation = default)
    {
        var root = Path.GetFullPath(gpoRoot);
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException(root);
        var findings = new List<GpoSecurityFinding>();
        var complete = true;

        foreach (var scope in new[] { "Machine", "User" })
        {
            var prefs = Path.Combine(root, scope, "Preferences");
            if (AvailableSourceDirectory(prefs, "GPP XML", scope, findings, ref complete))
            {
                var found = GpoBoundedDirectoryWalker.Scan(prefs,
                    new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".xml" },
                    maxEntries: 2000, maxMatches: MaxXmlFiles, cancellation);
                if (!found.Complete)
                {
                    complete = false;
                    findings.Add(new("Unknown", "GPP scan coverage", scope,
                        string.Join(" | ", found.Issues.Take(6))));
                }
                foreach (var file in found.Files)
                {
                    cancellation.ThrowIfCancellationRequested();
                    var relative = Path.GetRelativePath(root, file);
                    try
                    {
                        var bytes = OfflineGpoSourceService.ReadBounded(
                            file, GppXmlSourceReader.MaxFileBytes, cancellation);
                        using var input = new MemoryStream(bytes, writable: false);
                        using var xml = XmlReader.Create(input, new XmlReaderSettings
                        {
                            DtdProcessing = DtdProcessing.Prohibit,
                            XmlResolver = null,
                            MaxCharactersInDocument = GppXmlSourceReader.MaxFileBytes
                        });
                        var legacy = 0;
                        while (xml.Read())
                        {
                            if (xml.NodeType != XmlNodeType.Element ||
                                !xml.HasAttributes)
                                continue;
                            while (xml.MoveToNextAttribute())
                            {
                                if (xml.LocalName.Equals(
                                        "cpassword", StringComparison.OrdinalIgnoreCase) &&
                                    !string.IsNullOrWhiteSpace(xml.Value))
                                    legacy++;
                            }
                            xml.MoveToElement();
                        }
                        if (legacy > 0)
                            findings.Add(new("Critical", "Legacy GPP cpassword",
                                relative, legacy + " nonempty cpassword attribute(s). " +
                                "Rotate affected credentials and remove old preference passwords. " +
                                "Values are NOT exposed."));
                    }
                    catch (Exception ex) when (ex is IOException or InvalidDataException or XmlException or
                        UnauthorizedAccessException or System.Security.SecurityException)
                    {
                        complete = false;
                        findings.Add(new("Unknown", "Unreadable GPP XML",
                            relative, ex.GetType().Name +
                            "; no conclusion can be made about secrets."));
                    }
                }
            }

            var scripts = Path.Combine(root, scope, "Scripts");
            if (!AvailableSourceDirectory(scripts, "Scripts", scope, findings, ref complete))
                continue;
            var candidates = GpoBoundedDirectoryWalker.Scan(scripts,
                new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    ".ps1", ".psm1", ".bat", ".cmd", ".vbs",
                    ".js", ".wsf", ".hta"
                },
                maxEntries: 2500, maxMatches: MaxScripts, cancellation);
            if (!candidates.Complete)
            {
                complete = false;
                findings.Add(new("Unknown", "Script scan coverage", scope,
                    string.Join(" | ", candidates.Issues.Take(6))));
            }
            foreach (var file in candidates.Files)
            {
                cancellation.ThrowIfCancellationRequested();
                var relative = Path.GetRelativePath(root, file);
                try
                {
                    var bytes = OfflineGpoSourceService.ReadBounded(
                        file, MaxScriptBytes, cancellation);
                    // Latin1 provides a byte-preserving ASCII search without
                    // guessing the script's OEM/ANSI code page.
                    var text = Encoding.Latin1.GetString(bytes);
                    var suspects = new (string Label, string RegexPattern)[]
                    {
                        ("Encoded PowerShell command",
                            @"(?i)(-encodedcommand\b|-enc\s+[a-z0-9+/]{24,})"),
                        ("PowerShell execution bypass",
                            @"(?i)(-executionpolicy\s+bypass|set-executionpolicy\s+bypass)"),
                        ("Dynamic expression execution",
                            @"(?i)\b(invoke-expression|iex)\s*[\(\s]"),
                        ("Security controls modification",
                            @"(?i)(disableRealtimeMonitoring|set-mppreference\s+-disable)"),
                        ("Remote code retrieval",
                            @"(?i)(downloadstring\s*\(|invoke-webrequest\b)")
                    };
                    foreach (var suspect in suspects)
                        if (Regex.IsMatch(text, suspect.RegexPattern,
                                RegexOptions.CultureInvariant,
                                TimeSpan.FromMilliseconds(150)))
                            findings.Add(new("Review", "Script content pattern",
                                relative, suspect.Label +
                                " found; may be legitimate administration. " +
                                "Review original locally before acting."));
                }
                catch (Exception ex) when (ex is IOException or InvalidDataException or
                            UnauthorizedAccessException or System.Security.SecurityException or
                            RegexMatchTimeoutException)
                {
                    complete = false;
                    findings.Add(new("Unknown", "Unreadable script",
                        relative, ex.GetType().Name + "; inspect manually."));
                }
            }
        }

        return new GpoSecurityScan(gpoId, DateTimeOffset.UtcNow, root, complete, findings);
    }

    private static bool AvailableSourceDirectory(
        string path, string name, string scope,
        List<GpoSecurityFinding> findings, ref bool complete)
    {
        try
        {
            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.Directory) != 0)
                return true;
            complete = false;
            findings.Add(new GpoSecurityFinding("Unknown", "Invalid source directory",
                scope, name + " source path is not a directory; coverage unknown."));
            return false;
        }
        catch (Exception ex) when (ex is DirectoryNotFoundException or
                                   FileNotFoundException)
        {
            // Most GPOs have no preferences or scripts in both scopes.
            // Optional absent directory is normal; not proof of policy absence.
            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                                   System.Security.SecurityException)
        {
            complete = false;
            findings.Add(new GpoSecurityFinding("Unknown", "Unavailable source directory",
                scope, name + " path could not be inspected (" +
                ex.GetType().Name + "); skipped data is unknown."));
            return false;
        }
    }
}
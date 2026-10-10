using System.Text;
using GPOSettingsExplorer.Models;

namespace GPOSettingsExplorer.Services;

/// <summary>
/// Read-only, bounded SecEdit GptTmpl.inf reader. Does not resolve SIDs or
/// interpret security descriptors as effective access; it records exact
/// section/key/value text and the encoding evidence instead.
/// </summary>
public static class SecurityTemplateSourceReader
{
    public const int MaxFileBytes = 32 * 1024 * 1024;
    private const int MaxLines = 200_000;
    private const int MaxValueLength = 16_384;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static SourceParseResult Parse(
        byte[] bytes, Guid gpoId, string gpoName,
        string sourceFile, string sourceSha256)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (bytes.Length > MaxFileBytes)
            return Failed("GptTmpl.inf exceeds the read limit.");

        if (!TryDecode(bytes, out var text, out var encoding, out var decodeProblem))
            return Failed(decodeProblem);

        var rows = new List<RealSettingRecord>();
        var issues = new List<string>();
        var section = "";
        using var reader = new StringReader(text);
        string? line;
        var lineNo = 0;

        while ((line = reader.ReadLine()) is not null)
        {
            lineNo++;
            if (lineNo > MaxLines)
            {
                issues.Add($"Line limit {MaxLines:N0} reached.");
                break;
            }

            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith(';') ||
                trimmed.StartsWith('#'))
                continue;

            if (trimmed.StartsWith('['))
            {
                if (!trimmed.EndsWith(']') || trimmed.Length < 3)
                {
                    issues.Add($"Line {lineNo}: invalid section header.");
                    continue;
                }
                section = trimmed[1..^1].Trim();
                continue;
            }

            var equals = line.IndexOf('=');
            if (equals <= 0)
            {
                issues.Add($"Line {lineNo}: not an INF key=value record.");
                continue;
            }
            var key = line[..equals].Trim();
            var value = line[(equals + 1)..].Trim();
            if (string.IsNullOrWhiteSpace(key))
            {
                issues.Add($"Line {lineNo}: empty INF key.");
                continue;
            }

            if (value.Length > MaxValueLength)
            {
                issues.Add($"Line {lineNo}: oversized security template value not displayed.");
                continue;
            }

            // Security Templates can contain account policy, user-rights,
            // audit, registry ACL, file ACL, restricted groups and service
            // security entries. Preserve the original section exactly.
            var metadata = section.Equals("Unicode", StringComparison.OrdinalIgnoreCase) ||
                           section.Equals("Version", StringComparison.OrdinalIgnoreCase);
            rows.Add(new RealSettingRecord
            {
                GpoId = gpoId,
                GpoName = gpoName,
                Scope = "Computer",
                Category = metadata ? "Security template metadata" :
                    "Security template > " +
                    (section.Length == 0 ? "(missing section)" : section),
                SettingName = key,
                Value = value,
                ValueType = "INF string",
                SourceFile = sourceFile,
                SourceSha256 = sourceSha256,
                State = metadata
                    ? "Template metadata" : "Stored template value",
                Evidence = $"GptTmpl.inf line {lineNo}, {encoding}; " +
                    "raw security template data. SID/ACL/permissions are NOT " +
                    "interpreted as effective target rights."
            });
            if (section.Length == 0)
                issues.Add($"Line {lineNo}: INF key outside a named section.");
        }

        return new SourceParseResult(rows, issues);

        SourceParseResult Failed(string issue) =>
            new(Array.Empty<RealSettingRecord>(), new[] { issue });
    }

    private static bool TryDecode(
        byte[] bytes, out string text, out string sourceEncoding,
        out string issue)
    {
        text = "";
        issue = "";
        sourceEncoding = "";
        try
        {
            if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            {
                text = new UnicodeEncoding(false, true, true).GetString(bytes, 2,
                    bytes.Length - 2);
                sourceEncoding = "UTF-16LE BOM";
                return true;
            }
            if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            {
                text = new UnicodeEncoding(true, true, true).GetString(bytes, 2,
                    bytes.Length - 2);
                sourceEncoding = "UTF-16BE BOM";
                return true;
            }
            if (bytes.Length >= 3 && bytes[0] == 0xEF &&
                bytes[1] == 0xBB && bytes[2] == 0xBF)
            {
                text = StrictUtf8.GetString(bytes, 3, bytes.Length - 3);
                sourceEncoding = "UTF-8 BOM";
                return true;
            }

            // A conventional UTF-16LE security template may lack a BOM.
            // Only use the detectable ASCII+NUL layout for that case.
            var count = Math.Min(bytes.Length - bytes.Length % 2, 200);
            var oddNul = 0;
            for (var index = 1; index < count; index += 2)
                if (bytes[index] == 0)
                    oddNul++;

            if (count > 30 && oddNul > count / 5)
            {
                text = new UnicodeEncoding(false, false, true)
                    .GetString(bytes);
                sourceEncoding = "UTF-16LE no BOM (pattern detected)";
                return true;
            }

            // Strict UTF-8/ASCII only. ANSI with unknown code page is not
            // converted silently into garbled policy/SID values.
            text = StrictUtf8.GetString(bytes);
            sourceEncoding = "UTF-8 / ASCII without BOM";
            return true;
        }
        catch (DecoderFallbackException)
        {
            issue = "Unknown or invalid security template character encoding. " +
                    "No guessed ANSI conversion was used.";
            return false;
        }
    }
}

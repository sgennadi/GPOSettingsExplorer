using System.Text;
using GPOSettingsExplorer.Models;

namespace GPOSettingsExplorer.Services;

/// <summary>
/// Bounded read-only parser for Machine/Microsoft/Windows NT/Audit/audit.csv.
/// This is stored Advanced Audit Policy configuration evidence, NOT auditpol
/// output from any client, CSE precedence, a GPO editor or effective RSoP.
/// </summary>
public static class AdvancedAuditSourceReader
{
    public const int MaxFileBytes = 4 * 1024 * 1024;
    public const int MaxRecords = 20_000;
    private const int MaxFieldChars = 16_384;
    private const int MaxColumns = 32;

    public static SourceParseResult Parse(
        byte[] bytes, Guid gpoId, string gpoName, string path, string sha256)
    {
        if (bytes.Length == 0 || bytes.Length > MaxFileBytes)
            return Fail("Advanced Audit CSV empty or exceeds 4 MiB read limit.");

        string decoded;
        try
        {
            if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
                decoded = new UnicodeEncoding(false, true, true).GetString(
                    bytes, 2, bytes.Length - 2);
            else if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
                decoded = new UnicodeEncoding(true, true, true).GetString(
                    bytes, 2, bytes.Length - 2);
            else if (bytes.Length >= 3 &&
                     bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
                decoded = new UTF8Encoding(false, true).GetString(
                    bytes, 3, bytes.Length - 3);
            else
                decoded = new UTF8Encoding(false, true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return Fail("Invalid CSV text encoding; no unknown ANSI conversion performed.");
        }

        var parsed = new List<string[]>();
        var issues = new List<string>();
        using (var reader = new StringReader(decoded))
        {
            while (reader.Peek() >= 0)
            {
                if (parsed.Count >= MaxRecords)
                {
                    issues.Add("Audit CSV row limit reached.");
                    break;
                }
                if (!TryRecord(reader, out var cells, out var error))
                {
                    issues.Add(error);
                    break;
                }
                if (cells.Length > 1 || cells.Any(x => !string.IsNullOrWhiteSpace(x)))
                    parsed.Add(cells);
            }
        }
        if (parsed.Count == 0)
            return Fail(issues.FirstOrDefault() ?? "Audit CSV has no valid header.");
        var headers = parsed[0].Select(x => x.Trim()).ToArray();
        int Column(string name) => Array.FindIndex(headers, x =>
            x.Equals(name, StringComparison.OrdinalIgnoreCase));
        var targetColumn = Column("Policy Target");
        var nameColumn = Column("Subcategory");
        var guidColumn = Column("Subcategory GUID");
        var valueColumn = Column("Setting Value");
        if (nameColumn < 0 || guidColumn < 0 || valueColumn < 0)
            return Fail("Audit CSV lacks the required Subcategory/GUID/Setting Value columns.");

        var rows = new List<RealSettingRecord>();
        for (var i = 1; i < parsed.Count; i++)
        {
            var row = parsed[i];
            if (row.Length != headers.Length)
            {
                issues.Add("Audit CSV row " + (i + 1) + " has a different column count.");
                continue;
            }
            var setting = row[nameColumn].Trim();
            var guid = row[guidColumn].Trim();
            var settingValue = row[valueColumn].Trim();
            if (string.IsNullOrWhiteSpace(setting) ||
                !Guid.TryParse(guid, out _) ||
                settingValue.Length > MaxFieldChars)
            {
                issues.Add("Audit CSV row " + (i + 1) +
                    " has an empty subcategory, invalid GUID or long value.");
                continue;
            }
            var policyTarget = targetColumn >= 0 ? row[targetColumn].Trim() : "";
            rows.Add(new RealSettingRecord
            {
                GpoId = gpoId, GpoName = gpoName,
                Scope = "Computer",
                Category = "Advanced Audit Policy > " +
                    (policyTarget.Length > 0 ? policyTarget : "Stored policy"),
                SettingName = setting,
                RegistryKey = "Subcategory GUID",
                RegistryValue = guid,
                Value = settingValue,
                ValueType = "CSV string",
                SourceFile = path, SourceSha256 = sha256,
                State = "Stored advanced audit CSV entry",
                Evidence = "Advanced Audit Policy CSV row " + (i + 1) +
                    "; direct source only; use client auditpol/gpresult " +
                    "to verify effective audit behavior."
            });
        }
        return new SourceParseResult(rows, issues);

        SourceParseResult Fail(string message) =>
            new(Array.Empty<RealSettingRecord>(), new[] { message });
    }

    private static bool TryRecord(
        StringReader reader, out string[] fields, out string error)
    {
        var result = new List<string>();
        var builder = new StringBuilder();
        var quoted = false;
        var quoteClosed = false;
        error = "";
        while (reader.Peek() >= 0)
        {
            var ch = (char)reader.Read();
            if (ch == '"')
            {
                if (quoted && reader.Peek() == '"')
                {
                    reader.Read();
                    builder.Append('"');
                }
                else if (quoted)
                {
                    quoted = false;
                    quoteClosed = true;
                }
                else if (builder.Length == 0 && !quoteClosed)
                    quoted = true;
                else
                {
                    fields = Array.Empty<string>();
                    error = "Unexpected quote inside audit CSV field.";
                    return false;
                }
            }
            else if (ch == ',' && !quoted)
            {
                result.Add(builder.ToString());
                builder.Clear();
                quoteClosed = false;
            }
            else if ((ch is '\r' or '\n') && !quoted)
            {
                if (ch == '\r' && reader.Peek() == '\n')
                    reader.Read();
                result.Add(builder.ToString());
                fields = result.ToArray();
                return true;
            }
            else
            {
                if (quoteClosed && !char.IsWhiteSpace(ch))
                {
                    fields = Array.Empty<string>();
                    error = "Unexpected characters after closing audit CSV quote.";
                    return false;
                }
                builder.Append(ch);
            }

            if (result.Count >= MaxColumns || builder.Length > MaxFieldChars)
            {
                fields = Array.Empty<string>();
                error = "Audit CSV field/column count exceeds safety limit.";
                return false;
            }
        }
        if (quoted)
        {
            fields = Array.Empty<string>();
            error = "Unterminated quoted audit CSV field.";
            return false;
        }
        result.Add(builder.ToString());
        fields = result.ToArray();
        return true;
    }
}
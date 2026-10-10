using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using GPOSettingsExplorer.Models;

namespace GPOSettingsExplorer.Services;

/// <summary>
/// Read-only inspection of the original GPO source files on the pinned DC.
/// Does not parse GPMC report XML, launch MMC, change GPO versions or write
/// files. Missing files are not proof of Not Configured or effective RSoP.
/// </summary>
public static class GpoNativeSourceReader
{
    public const int MaxRegistryBytes = 32 * 1024 * 1024;
    public const int MaxSecurityTemplateBytes = 16 * 1024 * 1024;
    public const int MaxRecords = 100000;
    private const int MaxNameChars = 8192;

    private sealed record SourceFile(string Relative, string Scope, string Format, int Limit);

    private static readonly SourceFile[] KnownSources =
    {
        new(@"Machine\Registry.pol", "Computer", "Registry.pol", MaxRegistryBytes),
        new(@"User\Registry.pol", "User", "Registry.pol", MaxRegistryBytes),
        new(@"Machine\Microsoft\Windows NT\SecEdit\GptTmpl.inf",
            "Computer", "Security template", MaxSecurityTemplateBytes)
    };

    public static NativePolicyScanResult Scan(
        GpoInfo gpo, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(gpo);
        if (gpo.Id == Guid.Empty || string.IsNullOrWhiteSpace(gpo.DomainName))
            throw new ArgumentException("A domain-qualified GPO with an ID is required.");

        var server = DomainConnectionState.GetServerFor(gpo.DomainName);
        if (string.IsNullOrWhiteSpace(server))
            throw new InvalidOperationException(
                "No pinned domain controller was resolved. Connect before reading SYSVOL.");

        var basePath = Path.Combine(
            DomainConnectionState.BuildSysvolRoot(gpo.DomainName),
            "Policies", gpo.Id.ToString("B"));
        var observations = new List<NativePolicyEvidence>();
        var statuses = new List<NativePolicySourceStatus>();

        foreach (var source in KnownSources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fullPath = Path.Combine(basePath, source.Relative);
            try
            {
                var bytes = ReadStableBytes(fullPath, source.Limit, cancellationToken);
                var rows = source.Format == "Registry.pol"
                    ? ParseRegistryPol(bytes, gpo.Id, gpo.DisplayName,
                        source.Scope, source.Relative)
                    : ParseSecurityInf(bytes, gpo.Id, gpo.DisplayName,
                        source.Relative);

                observations.AddRange(rows);
                statuses.Add(new NativePolicySourceStatus(
                    source.Relative, "Read successfully", rows.Count,
                    "Parsed a bounded, stable source file on the pinned DC."));
            }
            catch (FileNotFoundException)
            {
                statuses.Add(new NativePolicySourceStatus(
                    source.Relative, "Not present", 0,
                    "Optional GPO source file was not found. No configuration state is inferred."));
            }
            catch (DirectoryNotFoundException)
            {
                statuses.Add(new NativePolicySourceStatus(
                    source.Relative, "Not present", 0,
                    "The source directory does not exist. This is not proof of Not Configured."));
            }
            catch (InvalidDataException ex)
            {
                statuses.Add(new NativePolicySourceStatus(
                    source.Relative, "Parse error", 0, ex.Message));
            }
            catch (DecoderFallbackException ex)
            {
                statuses.Add(new NativePolicySourceStatus(
                    source.Relative, "Parse error", 0,
                    "Invalid source text encoding; no replacement or silent character conversion: " +
                    ex.Message));
            }
            catch (IOException ex)
            {
                statuses.Add(new NativePolicySourceStatus(
                    source.Relative, "Read error", 0, ex.Message));
            }
            catch (UnauthorizedAccessException ex)
            {
                statuses.Add(new NativePolicySourceStatus(
                    source.Relative, "Read error", 0, ex.Message));
            }
        }

        return new NativePolicyScanResult(
            gpo.Id, gpo.DisplayName, server, observations, statuses,
            DateTimeOffset.UtcNow);
    }

    private static byte[] ReadStableBytes(
        string path, int maxBytes, CancellationToken cancellationToken)
    {
        using var stream = new FileStream(
            path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        var before = stream.Length;
        if (before > maxBytes)
            throw new InvalidDataException(
                "Source exceeds the configured " + maxBytes + "-byte read limit.");
        if (before < 0)
            throw new InvalidDataException("Invalid source file length.");

        var dateBefore = File.GetLastWriteTimeUtc(path);
        var content = new byte[checked((int)before)];
        stream.ReadExactly(content);
        cancellationToken.ThrowIfCancellationRequested();
        if (stream.Length != before || File.GetLastWriteTimeUtc(path) != dateBefore)
            throw new InvalidDataException(
                "The source changed during its read. Retry instead of trusting partial evidence.");

        return content;
    }

    public static IReadOnlyList<NativePolicyEvidence> ParseRegistryPol(
        byte[] bytes, Guid id, string gpoName, string scope, string source)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (bytes.Length > MaxRegistryBytes)
            throw new InvalidDataException("Registry.pol exceeds the size limit.");
        if (bytes.Length < 8 ||
            bytes[0] != 0x50 || bytes[1] != 0x52 ||
            bytes[2] != 0x65 || bytes[3] != 0x67)
            throw new InvalidDataException("Invalid Registry.pol PReg header.");
        if (BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4, 4)) != 1)
            throw new InvalidDataException("Unsupported Registry.pol version.");

        var rows = new List<NativePolicyEvidence>();
        var offset = 8;
        while (offset < bytes.Length)
        {
            if (rows.Count >= MaxRecords)
                throw new InvalidDataException("Registry.pol has too many records.");

            ReadChar(bytes, ref offset, '[');
            var key = ReadWideField(bytes, ref offset);
            var valueName = ReadWideField(bytes, ref offset);
            var kind = ReadUInt(bytes, ref offset);
            ReadChar(bytes, ref offset, ';');
            var count = ReadUInt(bytes, ref offset);
            ReadChar(bytes, ref offset, ';');
            if (count > MaxRegistryBytes ||
                count > bytes.Length - offset)
                throw new InvalidDataException(
                    "Registry.pol record has a truncated or excessive data size.");

            var raw = bytes.AsSpan(offset, checked((int)count));
            var value = DecodeRegistryValue(kind, raw);
            offset += checked((int)count);
            ReadChar(bytes, ref offset, ']');

            rows.Add(new NativePolicyEvidence
            {
                GpoId = id, GpoName = gpoName, Scope = scope,
                Source = "Registry.pol",
                SourceLocation = source,
                Category = "Administrative Templates / registry policy source",
                SettingName = string.IsNullOrWhiteSpace(valueName)
                    ? "(Default)" : valueName,
                RegistryKey = key, RegistryValue = valueName,
                DataType = TypeName(kind),
                State = valueName.StartsWith("**", StringComparison.Ordinal)
                    ? "Registry.pol directive" : "Stored source value",
                Value = value,
                Ordinal = rows.Count + 1
            });
        }
        return rows;
    }

    private static uint ReadUInt(byte[] data, ref int index)
    {
        if (index > data.Length - 4)
            throw new InvalidDataException("Truncated Registry.pol integer.");
        var value = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(index, 4));
        index += 4;
        return value;
    }

    private static void ReadChar(byte[] bytes, ref int index, char expected)
    {
        if (index > bytes.Length - 2 ||
            BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(index, 2)) != expected)
            throw new InvalidDataException(
                "Registry.pol invalid record delimiter at offset " + index + ".");
        index += 2;
    }

    private static string ReadWideField(byte[] bytes, ref int index)
    {
        var start = index;
        while (index <= bytes.Length - 2)
        {
            if (index - start > MaxNameChars * 2)
                throw new InvalidDataException("Registry.pol record name is too long.");

            if (BinaryPrimitives.ReadUInt16LittleEndian(
                    bytes.AsSpan(index, 2)) == ';')
            {
                var name = new UnicodeEncoding(false, false, true)
                    .GetString(bytes.AsSpan(start, index - start));
                index += 2;
                return name;
            }
            index += 2;
        }
        throw new InvalidDataException("Registry.pol record name is truncated.");
    }

    private static string DecodeRegistryValue(uint kind, ReadOnlySpan<byte> raw)
    {
        switch (kind)
        {
            case 1:
            case 2:
            {
                if (raw.Length % 2 != 0)
                    throw new InvalidDataException("Registry.pol UTF-16 string has odd length.");
                return new UnicodeEncoding(false, false, true)
                    .GetString(raw).TrimEnd('\0');
            }
            case 4:
                if (raw.Length != 4)
                    throw new InvalidDataException("Invalid DWORD value length.");
                var dword = BinaryPrimitives.ReadUInt32LittleEndian(raw);
                return dword.ToString(CultureInfo.InvariantCulture) +
                    " (0x" + dword.ToString("X8", CultureInfo.InvariantCulture) + ")";
            case 11:
                if (raw.Length != 8)
                    throw new InvalidDataException("Invalid QWORD value length.");
                return BinaryPrimitives.ReadUInt64LittleEndian(raw)
                    .ToString(CultureInfo.InvariantCulture);
            case 7:
            {
                if (raw.Length % 2 != 0)
                    throw new InvalidDataException("Registry.pol MULTI_SZ has odd length.");
                var text = new UnicodeEncoding(false, false, true)
                    .GetString(raw).TrimEnd('\0');
                return string.Join(" | ", text.Split('\0'));
            }
            default:
            {
                if (raw.Length <= 256)
                    return "0x" + Convert.ToHexString(raw);
                return "Binary " + raw.Length + " bytes; SHA256=" +
                    Convert.ToHexString(SHA256.HashData(raw));
            }
        }
    }

    private static string TypeName(uint kind) => kind switch
    {
        0 => "REG_NONE", 1 => "REG_SZ", 2 => "REG_EXPAND_SZ",
        3 => "REG_BINARY", 4 => "REG_DWORD", 7 => "REG_MULTI_SZ",
        11 => "REG_QWORD",
        _ => "REG_TYPE_" + kind
    };

    public static IReadOnlyList<NativePolicyEvidence> ParseSecurityInf(
        byte[] bytes, Guid id, string gpoName, string source)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (bytes.Length > MaxSecurityTemplateBytes)
            throw new InvalidDataException("GptTmpl.inf exceeds the size limit.");

        string text;
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            text = new UnicodeEncoding(false, true, true).GetString(bytes, 2, bytes.Length - 2);
        else if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            text = new UnicodeEncoding(true, true, true).GetString(bytes, 2, bytes.Length - 2);
        else
        {
            try
            {
                var start = bytes.Length >= 3 && bytes[0] == 0xEF &&
                    bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;
                text = new UTF8Encoding(false, true).GetString(
                    bytes, start, bytes.Length - start);
            }
            catch (DecoderFallbackException ex)
            {
                throw new InvalidDataException(
                    "Unsupported GptTmpl.inf encoding. The source was not guessed or altered.", ex);
            }
        }

        var rows = new List<NativePolicyEvidence>();
        var section = "";
        var lineNumber = 0;
        foreach (var raw in text.Replace("\r\n", "\n", StringComparison.Ordinal)
                     .Replace('\r', '\n').Split('\n'))
        {
            lineNumber++;
            if (lineNumber > MaxRecords * 2)
                throw new InvalidDataException("GptTmpl.inf exceeds the line limit.");

            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith(";", StringComparison.Ordinal))
                continue;
            if (line.StartsWith("[", StringComparison.Ordinal))
            {
                if (!line.EndsWith("]", StringComparison.Ordinal) || line.Length > MaxNameChars)
                    throw new InvalidDataException(
                        "Malformed security template section at line " + lineNumber + ".");
                section = line[1..^1].Trim();
                if (section.Length == 0)
                    throw new InvalidDataException("Empty security template section.");
                continue;
            }

            var separator = line.IndexOf('=');
            if (separator <= 0 || section.Length == 0)
                throw new InvalidDataException(
                    "Malformed GptTmpl.inf record at line " + lineNumber + ".");
            if (rows.Count >= MaxRecords)
                throw new InvalidDataException("Too many GptTmpl.inf records.");

            var name = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();
            if (name.Length == 0 || name.Length > MaxNameChars)
                throw new InvalidDataException(
                    "Invalid GptTmpl.inf key at line " + lineNumber + ".");

            rows.Add(new NativePolicyEvidence
            {
                GpoId = id, GpoName = gpoName, Scope = "Computer",
                Source = "GptTmpl.inf", SourceLocation = source,
                Category = SecurityCategory(section),
                SettingName = name,
                State = section.Equals("Version", StringComparison.OrdinalIgnoreCase) ||
                        section.Equals("Unicode", StringComparison.OrdinalIgnoreCase)
                    ? "Source metadata" : "Stored source value",
                Value = value,
                DataType = "[" + section + "]",
                Ordinal = lineNumber
            });
        }
        return rows;
    }

    private static string SecurityCategory(string section)
    {
        if (section.Equals("System Access", StringComparison.OrdinalIgnoreCase))
            return "Security Settings > Account Policies";
        if (section.Equals("Event Audit", StringComparison.OrdinalIgnoreCase))
            return "Security Settings > Local Policies > Audit Policy";
        if (section.Equals("Privilege Rights", StringComparison.OrdinalIgnoreCase))
            return "Security Settings > Local Policies > User Rights Assignment";
        if (section.Equals("Registry Values", StringComparison.OrdinalIgnoreCase))
            return "Security Settings > Local Policies > Security Options";
        if (section.Equals("Group Membership", StringComparison.OrdinalIgnoreCase))
            return "Security Settings > Restricted Groups";
        if (section.Equals("Registry Keys", StringComparison.OrdinalIgnoreCase))
            return "Security Settings > Registry";
        if (section.Equals("File Security", StringComparison.OrdinalIgnoreCase))
            return "Security Settings > File System";
        if (section.Equals("Service General Setting", StringComparison.OrdinalIgnoreCase))
            return "Security Settings > System Services";
        return "Security template [" + section + "]";
    }
}

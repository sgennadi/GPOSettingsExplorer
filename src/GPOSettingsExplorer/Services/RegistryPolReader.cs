using System.Buffers.Binary;
using System.Text;
using GPOSettingsExplorer.Models;

namespace GPOSettingsExplorer.Services;

/// <summary>
/// Strict, bounded reader for the Windows Registry.pol PReg format. Records
/// mix UTF-16LE text and binary integers/data; never read them as text lines.
/// No registry key is created, opened, written or imported by this parser.
/// </summary>
public static class RegistryPolReader
{
    private const uint Signature = 0x67655250; // "PReg"
    public const int MaxFileBytes = 64 * 1024 * 1024;
    private const int MaxValueBytes = 8 * 1024 * 1024;
    private const int MaxEntries = 100_000;
    private const int MaxNameCharacters = 16_384;

    public static SourceParseResult Parse(
        byte[] bytes, Guid gpoId, string gpoName, string scope,
        string sourceFile, string sourceSha256)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        var rows = new List<RealSettingRecord>();
        var issues = new List<string>();

        if (bytes.Length > MaxFileBytes)
            return Failed($"Registry.pol exceeds {MaxFileBytes:N0} byte read limit.");

        if (bytes.Length < 8 ||
            BinaryPrimitives.ReadUInt32LittleEndian(bytes) != Signature)
            return Failed("Not a valid PReg registry policy header.");

        var version = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4, 4));
        if (version != 1)
            return Failed($"Registry.pol version {version} is unsupported; no records were interpreted.");

        var offset = 8;
        while (offset < bytes.Length && rows.Count < MaxEntries)
        {
            var recordNumber = rows.Count + 1;
            try
            {
                ExpectChar(bytes, ref offset, '[');
                var key = ReadField(bytes, ref offset);
                var valueName = ReadField(bytes, ref offset);
                var kind = ReadUInt32(bytes, ref offset);
                ExpectChar(bytes, ref offset, ';');
                var size = ReadUInt32(bytes, ref offset);
                ExpectChar(bytes, ref offset, ';');

                if (size > MaxValueBytes || size > bytes.Length - offset)
                    throw new InvalidDataException(
                        $"Data size {size} is invalid or exceeds the available bytes.");

                var data = bytes.AsSpan(offset, checked((int)size));
                offset += checked((int)size);
                ExpectChar(bytes, ref offset, ']');

                var special = valueName.StartsWith("**", StringComparison.Ordinal);
                var display = FormatValue(kind, data, out var valueType);
                rows.Add(new RealSettingRecord
                {
                    GpoId = gpoId,
                    GpoName = gpoName,
                    Scope = scope,
                    Category = special
                        ? "Registry policy operations"
                        : "Registry policy (source file)",
                    SettingName = string.IsNullOrWhiteSpace(valueName)
                        ? "(Default)" : valueName,
                    RegistryKey = key,
                    RegistryValue = valueName,
                    Value = display,
                    ValueType = valueType,
                    SourceFile = sourceFile,
                    SourceSha256 = sourceSha256,
                    State = special ? "Stored operation" : "Stored registry value",
                    Evidence = $"PReg v{version}, record {recordNumber}, binary type {kind}, length {size} bytes. " +
                        (special
                            ? "Special registry.pol instruction; do not interpret as current effective value."
                            : "Configured source-file data only; effective RSoP not evaluated.")
                });
            }
            catch (Exception ex) when (ex is InvalidDataException or ArgumentOutOfRangeException)
            {
                issues.Add($"Record {recordNumber}, byte offset {offset}: {ex.Message}");
                break;
            }
        }

        if (rows.Count >= MaxEntries && offset < bytes.Length)
            issues.Add($"Record cap {MaxEntries:N0} reached; remaining bytes not interpreted.");

        return new SourceParseResult(rows, issues);

        SourceParseResult Failed(string error) =>
            new(Array.Empty<RealSettingRecord>(), new[] { error });
    }

    private static string ReadField(byte[] bytes, ref int offset)
    {
        var chars = new List<char>();
        while (true)
        {
            if (offset + 2 > bytes.Length)
                throw new InvalidDataException("Unexpected EOF in UTF-16LE key/value field.");

            var c = (char)BinaryPrimitives.ReadUInt16LittleEndian(
                bytes.AsSpan(offset, 2));
            offset += 2;
            if (c == ';')
                return new string(chars.ToArray()).TrimEnd('\0');
            if (chars.Count >= MaxNameCharacters)
                throw new InvalidDataException("Registry policy key/value name is too long.");
            chars.Add(c);
        }
    }

    private static uint ReadUInt32(byte[] bytes, ref int offset)
    {
        if (offset + 4 > bytes.Length)
            throw new InvalidDataException("Unexpected EOF reading DWORD.");
        var number = BinaryPrimitives.ReadUInt32LittleEndian(
            bytes.AsSpan(offset, 4));
        offset += 4;
        return number;
    }

    private static void ExpectChar(byte[] bytes, ref int offset, char expected)
    {
        if (offset + 2 > bytes.Length)
            throw new InvalidDataException($"Missing UTF-16LE delimiter '{expected}'.");
        var actual = BinaryPrimitives.ReadUInt16LittleEndian(
            bytes.AsSpan(offset, 2));
        if (actual != expected)
            throw new InvalidDataException(
                $"Expected UTF-16LE delimiter '{expected}', got 0x{actual:X4}.");
        offset += 2;
    }

    private static string FormatValue(uint kind, ReadOnlySpan<byte> bytes, out string typeName)
    {
        typeName = kind switch
        {
            0 => "REG_NONE",
            1 => "REG_SZ",
            2 => "REG_EXPAND_SZ",
            3 => "REG_BINARY",
            4 => "REG_DWORD",
            5 => "REG_DWORD_BIG_ENDIAN",
            7 => "REG_MULTI_SZ",
            11 => "REG_QWORD",
            _ => $"REG_TYPE_{kind}"
        };

        if (kind is 1 or 2 or 7)
        {
            if (bytes.Length % 2 != 0)
                return "(odd-length UTF-16LE data) 0x" + HexPreview(bytes);
            var text = Encoding.Unicode.GetString(bytes).TrimEnd('\0');
            return kind == 7 ? text.Replace("\0", " | ", StringComparison.Ordinal)
                             : text;
        }
        if (kind == 4 && bytes.Length == 4)
            return BinaryPrimitives.ReadUInt32LittleEndian(bytes).ToString(
                System.Globalization.CultureInfo.InvariantCulture);
        if (kind == 5 && bytes.Length == 4)
            return BinaryPrimitives.ReadUInt32BigEndian(bytes).ToString(
                System.Globalization.CultureInfo.InvariantCulture);
        if (kind == 11 && bytes.Length == 8)
            return BinaryPrimitives.ReadUInt64LittleEndian(bytes).ToString(
                System.Globalization.CultureInfo.InvariantCulture);

        return bytes.Length == 0
            ? "(zero bytes)" : "0x" + HexPreview(bytes);
    }

    private static string HexPreview(ReadOnlySpan<byte> bytes)
    {
        const int maxShown = 48;
        var shown = bytes[..Math.Min(bytes.Length, maxShown)];
        return Convert.ToHexString(shown) +
            (bytes.Length > maxShown ? $"... ({bytes.Length:N0} bytes total)" : "");
    }
}

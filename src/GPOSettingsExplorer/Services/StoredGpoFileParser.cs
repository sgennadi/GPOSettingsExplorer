using System.Buffers.Binary;
using System.Text;
using GPOSettingsExplorer.Models;

namespace GPOSettingsExplorer.Services;

/// <summary>
/// Strict, bounded read-only parser for MS-GPREG / Registry.pol, and a
/// read-only section/key reader for GptTmpl.inf. It never loads a registry
/// hive, applies a policy, mutates a GPO or infers effective RSoP results.
/// </summary>
public static class StoredGpoFileParser
{
    public const int MaxFileBytes = 32 * 1024 * 1024;
    public const int MaxEntries = 100000;
    public const int MaxStringChars = 16384;

    public static IReadOnlyList<StoredGpoSetting> ReadRegistryPol(
        byte[] file, Guid gpoId, string gpoName,
        string scope, string sourcePath)
    {
        ArgumentNullException.ThrowIfNull(file);
        if (file.Length < 8 || file.Length > MaxFileBytes ||
            file[0] != 0x50 || file[1] != 0x52 ||
            file[2] != 0x65 || file[3] != 0x67)
            throw new FormatException("Invalid Registry.pol signature or file size.");

        var pos = 4;
        var version = Dword(file, ref pos);
        if (version != 1)
            throw new FormatException($"Registry.pol version {version} is not supported.");

        if (!scope.Equals("User", StringComparison.OrdinalIgnoreCase) &&
            !scope.Equals("Computer", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Registry scope must be Computer or User.", nameof(scope));

        var output = new List<StoredGpoSetting>();
        while (pos < file.Length)
        {
            if (output.Count == MaxEntries)
                throw new FormatException("Registry.pol entry limit exceeded.");

            Expect(file, ref pos, '[');
            var key = NullTerminatedUnicode(file, ref pos);
            Expect(file, ref pos, ';');
            var name = NullTerminatedUnicode(file, ref pos);
            Expect(file, ref pos, ';');
            var type = Dword(file, ref pos);
            Expect(file, ref pos, ';');
            var size = Dword(file, ref pos);
            Expect(file, ref pos, ';');

            if (size > MaxFileBytes || size > file.Length - pos)
                throw new FormatException("Registry.pol data length exceeds remaining file bounds.");

            var data = file.AsSpan(pos, (int)size);
            var value = DecodeValue(type, data);
            pos += (int)size;
            Expect(file, ref pos, ']');

            var isSpecial = name.StartsWith("**", StringComparison.Ordinal) ||
                            name.Length == 0;
            output.Add(new StoredGpoSetting
            {
                GpoId = gpoId,
                GpoName = gpoName,
                Scope = scope,
                Source = "Registry.pol",
                Section = "Stored registry policy",
                Key = key,
                ValueName = name,
                Type = isSpecial ? "Instruction / " + TypeName(type) : TypeName(type),
                Value = value,
                DataBytes = (int)size,
                IsInstruction = isSpecial,
                FilePath = sourcePath
            });
        }
        return output;
    }

    public static IReadOnlyList<StoredGpoSetting> ReadSecurityInf(
        byte[] file, Guid gpoId, string gpoName, string sourcePath)
    {
        ArgumentNullException.ThrowIfNull(file);
        if (file.Length > MaxFileBytes)
            throw new FormatException("GptTmpl.inf file exceeds safety size limit.");

        var text = DecodeInf(file);
        var section = "";
        var rows = new List<StoredGpoSetting>();
        foreach (var line in text.Replace("\r\n", "\n", StringComparison.Ordinal)
                     .Replace('\r', '\n')
                     .Split('\n'))
        {
            var part = line.Trim();
            if (part.Length == 0 || part.StartsWith(';') || part.StartsWith('#'))
                continue;

            if (part.StartsWith('[') && part.EndsWith(']') && part.Length > 2)
            {
                section = part[1..^1].Trim();
                continue;
            }

            if (section.Length == 0)
                continue;

            // The key/value encoding varies between INF sections. Preserve
            // raw data after the first '=' instead of guessing a value type.
            var equals = part.IndexOf('=');
            if (equals <= 0)
                continue;

            if (rows.Count >= MaxEntries)
                throw new FormatException("GptTmpl.inf entry limit exceeded.");

            var name = part[..equals].Trim();
            var value = part[(equals + 1)..].Trim();
            if (name.Length == 0)
                continue;

            rows.Add(new StoredGpoSetting
            {
                GpoId = gpoId,
                GpoName = gpoName,
                Scope = "Computer",
                Source = "GptTmpl.inf",
                Section = section,
                Key = section,
                ValueName = name,
                Value = value,
                Type = "Security INF raw",
                DataBytes = Encoding.UTF8.GetByteCount(value),
                IsInstruction = section.Equals("Version", StringComparison.OrdinalIgnoreCase) ||
                                section.Equals("Unicode", StringComparison.OrdinalIgnoreCase),
                FilePath = sourcePath
            });
        }
        return rows;
    }

    private static string DecodeInf(byte[] bytes)
    {
        if (bytes.Length >= 2)
        {
            if (bytes[0] == 0xff && bytes[1] == 0xfe)
                return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
            if (bytes[0] == 0xfe && bytes[1] == 0xff)
                return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
        }
        if (bytes.Length >= 3 && bytes[0] == 0xef &&
            bytes[1] == 0xbb && bytes[2] == 0xbf)
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);

        // GptTmpl.inf may be UTF-16LE without BOM.
        var sample = Math.Min(bytes.Length, 160);
        var highZeros = 0;
        for (var i = 1; i < sample; i += 2)
            if (bytes[i] == 0) highZeros++;
        if (highZeros > 12 && bytes.Length % 2 == 0)
            return Encoding.Unicode.GetString(bytes);

        try
        {
            return new UTF8Encoding(false, true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            // Last-resort lossless byte-to-character inspection; the value
            // is not claimed to be correctly decoded ANSI text.
            return Encoding.Latin1.GetString(bytes);
        }
    }

    private static string DecodeValue(uint type, ReadOnlySpan<byte> data)
    {
        switch (type)
        {
            case 1: case 2: // REG_SZ, REG_EXPAND_SZ
                if (data.Length % 2 != 0)
                    throw new FormatException("Registry.pol UTF-16 string has odd byte length.");
                return Encoding.Unicode.GetString(data).TrimEnd('\0');
            case 7: // REG_MULTI_SZ
                if (data.Length % 2 != 0)
                    throw new FormatException("Registry.pol MULTI_SZ has odd byte length.");
                return string.Join(" | ", Encoding.Unicode.GetString(data)
                    .Split('\0', StringSplitOptions.RemoveEmptyEntries));
            case 4:
                if (data.Length != 4) throw new FormatException("REG_DWORD must have four bytes.");
                return BinaryPrimitives.ReadUInt32LittleEndian(data).ToString(
                    System.Globalization.CultureInfo.InvariantCulture);
            case 5:
                if (data.Length != 4) throw new FormatException("REG_DWORD_BIG_ENDIAN must have four bytes.");
                return BinaryPrimitives.ReadUInt32BigEndian(data).ToString(
                    System.Globalization.CultureInfo.InvariantCulture);
            case 11:
                if (data.Length != 8) throw new FormatException("REG_QWORD must have eight bytes.");
                return BinaryPrimitives.ReadUInt64LittleEndian(data).ToString(
                    System.Globalization.CultureInfo.InvariantCulture);
            default:
                // Bound visible raw hex; never log unrestricted binary blobs.
                return data.Length == 0 ? "(empty)" :
                    Convert.ToHexString(data[..Math.Min(64, data.Length)]) +
                    (data.Length > 64 ? $"... ({data.Length:N0} bytes)" : "");
        }
    }

    private static string TypeName(uint t) => t switch
    {
        0 => "REG_NONE",
        1 => "REG_SZ",
        2 => "REG_EXPAND_SZ",
        3 => "REG_BINARY",
        4 => "REG_DWORD",
        5 => "REG_DWORD_BIG_ENDIAN",
        7 => "REG_MULTI_SZ",
        11 => "REG_QWORD",
        _ => "REG_TYPE_" + t
    };

    private static string NullTerminatedUnicode(byte[] bytes, ref int pos)
    {
        var start = pos;
        for (var chars = 0; chars < MaxStringChars; chars++)
        {
            var c = U16(bytes, ref pos);
            if (c == 0)
                return Encoding.Unicode.GetString(bytes, start, pos - start - 2);
        }
        throw new FormatException("Registry.pol key/value exceeded string limit.");
    }

    private static void Expect(byte[] bytes, ref int pos, char expected)
    {
        if (U16(bytes, ref pos) != expected)
            throw new FormatException(
                $"Invalid Registry.pol record delimiter at byte {pos - 2} (expected {expected}).");
    }

    private static ushort U16(byte[] bytes, ref int pos)
    {
        if (bytes.Length - pos < 2)
            throw new FormatException("Unexpected end of Registry.pol UTF-16 field.");
        var v = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(pos, 2));
        pos += 2;
        return v;
    }

    private static uint Dword(byte[] bytes, ref int pos)
    {
        if (bytes.Length - pos < 4)
            throw new FormatException("Truncated Registry.pol 32-bit field.");
        var v = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(pos, 4));
        pos += 4;
        return v;
    }
}

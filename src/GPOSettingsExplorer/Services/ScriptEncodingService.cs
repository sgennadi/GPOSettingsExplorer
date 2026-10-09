using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace GPOSettingsExplorer.Services;

public sealed record ScriptEncodingChoice(int CodePage, string Label)
{
    public override string ToString() => Label;
}

public static class ScriptEncodingService
{
    static ScriptEncodingService() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    [DllImport("kernel32.dll")]
    private static extern uint GetACP();

    [DllImport("kernel32.dll")]
    private static extern uint GetOEMCP();

    public static int SystemAnsiCodePage => OperatingSystem.IsWindows()
        ? checked((int)GetACP()) : Encoding.Default.CodePage;

    public static int SystemOemCodePage => OperatingSystem.IsWindows()
        ? checked((int)GetOEMCP()) : 437;

    public static IReadOnlyList<ScriptEncodingChoice> AvailableCodePages()
    {
        var popular = new[]
        {
            65001, 1200, 1201, 12000, 12001,
            SystemAnsiCodePage, SystemOemCodePage,
            1252, 1250, 1251, 1255, 1256, 1253, 1254, 1257,
            866, 862, 850, 852, 855, 437, 737, 775, 857,
            20866, 21866, 28595, 28598,
            932, 936, 949, 950, 874
        };
        var installed = Encoding.GetEncodings()
            .Select(info => info.CodePage)
            .Concat(popular)
            .Where(cp => cp > 0 && cp != 65000)
            .Distinct()
            .ToArray();
        var recommended = new HashSet<int>(popular);
        return installed
            .Select(cp =>
            {
                try
                {
                    var encoding = Strict(cp);
                    var common = cp == SystemAnsiCodePage ? "System ANSI; " :
                        cp == SystemOemCodePage ? "System OEM; " : "";
                    return new ScriptEncodingChoice(cp,
                        $"{cp} — {common}{encoding.EncodingName} ({encoding.WebName})");
                }
                catch (ArgumentException) { return null; }
                catch (NotSupportedException) { return null; }
            })
            .Where(item => item is not null)
            .Select(item => item!)
            .OrderByDescending(item => recommended.Contains(item.CodePage))
            .ThenBy(item => Array.IndexOf(popular, item.CodePage) < 0
                ? int.MaxValue : Array.IndexOf(popular, item.CodePage))
            .ThenBy(item => item.CodePage)
            .ToArray();
    }

    public static Encoding Strict(int codePage) => codePage switch
    {
        65001 => new UTF8Encoding(false, true),
        1200 => new UnicodeEncoding(false, false, true),
        1201 => new UnicodeEncoding(true, false, true),
        12000 => new UTF32Encoding(false, false, true),
        12001 => new UTF32Encoding(true, false, true),
        65000 => throw new NotSupportedException("UTF-7 is unsafe and not supported."),
        _ => Encoding.GetEncoding(codePage,
            EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback)
    };

    public static bool SupportsBom(int codePage) =>
        codePage is 65001 or 1200 or 1201 or 12000 or 12001;

    public static byte[] BomFor(int codePage) => codePage switch
    {
        65001 => new byte[] { 0xEF, 0xBB, 0xBF },
        1200 => new byte[] { 0xFF, 0xFE },
        1201 => new byte[] { 0xFE, 0xFF },
        12000 => new byte[] { 0xFF, 0xFE, 0x00, 0x00 },
        12001 => new byte[] { 0x00, 0x00, 0xFE, 0xFF },
        _ => Array.Empty<byte>()
    };

    public static (int CodePage, int BomLength) DetectBom(ReadOnlySpan<byte> bytes)
    {
        // UTF-32LE starts with UTF-16LE's BOM; order matters.
        foreach (var cp in new[] { 12000, 12001, 65001, 1200, 1201 })
        {
            var mark = BomFor(cp);
            if (bytes.StartsWith(mark))
                return (cp, mark.Length);
        }
        return (0, 0);
    }

    public static string Decode(byte[] bytes, int codePage, bool skipKnownBom = true)
    {
        var offset = skipKnownBom ? DetectBom(bytes).BomLength : 0;
        return Strict(codePage).GetString(bytes, offset, bytes.Length - offset);
    }

    public static byte[] Encode(string text, int codePage, bool bom)
    {
        if (bom && !SupportsBom(codePage))
            throw new InvalidOperationException(
                $"Code page {codePage} is not Unicode and cannot have a BOM.");

        var payload = Strict(codePage).GetBytes(text);
        if (!bom)
            return payload;

        var mark = BomFor(codePage);
        var bytes = new byte[mark.Length + payload.Length];
        mark.CopyTo(bytes, 0);
        payload.CopyTo(bytes, mark.Length);
        return bytes;
    }

    public static bool CanRoundTrip(string text, int codePage, out string reason)
    {
        try
        {
            var encoded = Strict(codePage).GetBytes(text);
            var restored = Strict(codePage).GetString(encoded);
            var ok = text.Equals(restored, StringComparison.Ordinal);
            reason = ok ? string.Empty : "Text changes during encode/decode round trip.";
            return ok;
        }
        catch (Exception error) when (error is EncoderFallbackException or DecoderFallbackException
                                           or ArgumentException or NotSupportedException)
        {
            reason = error.Message;
            return false;
        }
    }

    public static string DetectNewline(string text)
    {
        var crlf = 0;
        var lf = 0;
        var cr = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\r')
            {
                if (i + 1 < text.Length && text[i + 1] == '\n')
                {
                    crlf++;
                    i++;
                }
                else cr++;
            }
            else if (text[i] == '\n') lf++;
        }

        if (crlf == 0 && lf == 0 && cr == 0)
            return Environment.NewLine;
        return crlf >= lf && crlf >= cr ? "\r\n" : lf >= cr ? "\n" : "\r";
    }

    public static bool HasMixedNewlines(string text)
    {
        var crlf = false;
        var lf = false;
        var cr = false;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\r')
            {
                if (i + 1 < text.Length && text[i + 1] == '\n')
                {
                    crlf = true;
                    i++;
                }
                else cr = true;
            }
            else if (text[i] == '\n') lf = true;
        }
        return (crlf ? 1 : 0) + (lf ? 1 : 0) + (cr ? 1 : 0) > 1;
    }

    public static string NormalizeNewlines(string text, string lineEnding)
    {
        if (lineEnding is not ("\r\n" or "\n" or "\r"))
            throw new ArgumentException("Unsupported line-ending sequence.", nameof(lineEnding));
        var unix = text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal);
        return lineEnding == "\n" ? unix :
            unix.Replace("\n", lineEnding, StringComparison.Ordinal);
    }

    public static string DisplayLineEnding(string value) => value switch
    {
        "\r\n" => "DOS/Windows CRLF",
        "\n" => "Unix LF",
        "\r" => "Classic Mac CR",
        _ => "Unknown"
    };

    public static bool SourceMatches(byte[] bytes, string originalSha256) =>
        Convert.ToHexString(SHA256.HashData(bytes)).Equals(
            originalSha256, StringComparison.OrdinalIgnoreCase);
}

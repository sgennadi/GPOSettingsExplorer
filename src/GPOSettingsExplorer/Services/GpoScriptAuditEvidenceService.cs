using System.Security.Cryptography;
using GPOSettingsExplorer.Models;

namespace GPOSettingsExplorer.Services;

/// <summary>
/// Immutable evidence captured from the original bytes and from the bytes
/// verified after the GPO scripts extension was committed. Never put script
/// source code, command arguments or potential embedded credentials in JSONL.
/// </summary>
public sealed record GpoScriptAuditEvidence(
    bool Changed,
    string Before,
    string After,
    string ChangeSummary);

public static class GpoScriptAuditEvidenceService
{
    public static GpoScriptAuditEvidence Create(
        byte[] originalBytes,
        byte[] committedBytes,
        GpoScriptDocument document)
    {
        ArgumentNullException.ThrowIfNull(originalBytes);
        ArgumentNullException.ThrowIfNull(committedBytes);
        ArgumentNullException.ThrowIfNull(document);

        // ReadDocument already validated the baseline code page. Decode the
        // exact committed bytes rather than the editor buffer: normalization
        // and BOM transformations can change bytes during the save.
        var originalText = ScriptEncodingService.Decode(
            originalBytes, document.OriginalCodePage);
        var committedText = ScriptEncodingService.Decode(
            committedBytes, document.CodePage);

        var changed = !originalBytes.AsSpan().SequenceEqual(committedBytes);
        var before = Describe(
            originalBytes, originalText,
            document.OriginalCodePage, document.OriginalEmitBom);
        var after = Describe(
            committedBytes, committedText,
            document.CodePage, document.EmitBom);
        var summary = DescribeChangedRegion(originalText, committedText, changed);

        return new GpoScriptAuditEvidence(changed, before, after, summary);
    }

    private static string Describe(
        byte[] bytes, string text, int codePage, bool expectedBom)
    {
        var detected = ScriptEncodingService.DetectBom(bytes);
        var bom = detected.BomLength > 0;
        var newline = ScriptEncodingService.HasMixedNewlines(text)
            ? "Mixed"
            : ScriptEncodingService.DisplayLineEnding(
                ScriptEncodingService.DetectNewline(text));
        return "SHA-256: " + Convert.ToHexString(SHA256.HashData(bytes)) +
               "\nBytes: " + bytes.Length +
               "\nLines: " + Lines(text).Length +
               "\nCode page: " + codePage +
               "\nBOM: " + (bom ? "Yes" : "No") +
               "\nLine endings: " + newline;
    }

    private static string DescribeChangedRegion(
        string originalText, string committedText, bool bytesChanged)
    {
        if (!bytesChanged)
            return "No byte change - no script modification was committed.";

        if (originalText.Equals(committedText, StringComparison.Ordinal))
            return "File format changed (encoding / BOM); the decoded script text is identical.";

        var before = Lines(originalText);
        var after = Lines(committedText);
        var commonPrefix = 0;
        while (commonPrefix < Math.Min(before.Length, after.Length) &&
               before[commonPrefix].Equals(after[commonPrefix], StringComparison.Ordinal))
            commonPrefix++;

        var commonSuffix = 0;
        while (commonSuffix < Math.Min(before.Length, after.Length) - commonPrefix &&
               before[before.Length - 1 - commonSuffix].Equals(
                   after[after.Length - 1 - commonSuffix],
                   StringComparison.Ordinal))
            commonSuffix++;

        var beforeCount = before.Length - commonPrefix - commonSuffix;
        var afterCount = after.Length - commonPrefix - commonSuffix;

        // No script text is included. Changed-line counts/ranges are only
        // approximate for a reordering of lines, but fingerprints are exact.
        return $"Changed text region: before lines {Range(commonPrefix, beforeCount)} " +
               $"({beforeCount} line(s)); after lines {Range(commonPrefix, afterCount)} " +
               $"({afterCount} line(s)). Content omitted from audit for credential safety.";
    }

    private static string Range(int prefix, int count) =>
        count == 0 ? "<none>" : $"{prefix + 1}-{prefix + count}";

    private static string[] Lines(string text)
    {
        if (text.Length == 0)
            return Array.Empty<string>();

        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal)
            .Split('\n');
        if (text.EndsWith('\r') || text.EndsWith('\n'))
            return lines[..^1];
        return lines;
    }
}

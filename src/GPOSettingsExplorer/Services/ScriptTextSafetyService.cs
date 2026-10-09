using System.Globalization;
using GPOSettingsExplorer.Models;

namespace GPOSettingsExplorer.Services;

/// <summary>
/// Static examination only. No source code execution, auto-replacement or
/// conversion of Cyrillic/Hebrew words. Diagnostics pinpoint risky codepoints.
/// </summary>
public static class ScriptTextSafetyService
{
    public static IReadOnlyList<ScriptDiagnostic> Analyze(
        string text, string fileName, int codePage, bool emitBom, string newLine)
    {
        var findings = new List<ScriptDiagnostic>();
        var line = 1;
        var column = 1;
        var batch = ScriptSyntaxService.IsBatch(fileName);
        var powershell = ScriptSyntaxService.IsPowerShell(fileName);

        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (ch is '\r' or '\n')
            {
                if (ch == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                line++;
                column = 1;
                continue;
            }

            ScriptDiagnosticSeverity? severity = null;
            string? message = null;
            var cp = ((int)ch).ToString("X4", CultureInfo.InvariantCulture);
            if (ch == '\uFEFF')
            {
                severity = i == 0 ? ScriptDiagnosticSeverity.Warning : ScriptDiagnosticSeverity.Error;
                message = "Embedded Unicode BOM / zero-width no-break space (U+FEFF). The physical BOM is controlled separately.";
            }
            else if (ch == '\0')
            {
                severity = ScriptDiagnosticSeverity.Error;
                message = "NUL (U+0000) can truncate a command or confuse interpreters.";
            }
            else if (char.IsSurrogate(ch))
            {
                if (char.IsHighSurrogate(ch) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                {
                    // Valid supplementary Unicode symbol: do not treat it as an error.
                    i++;
                    column += 2;
                    continue;
                }
                severity = ScriptDiagnosticSeverity.Error;
                message = "Unpaired UTF-16 surrogate: text is invalid Unicode.";
            }
            else if (ch < 0x20 && ch != '\t')
            {
                severity = ScriptDiagnosticSeverity.Error;
                message = $"Control character U+{cp} can break script execution.";
            }
            else if (ch is '\u00A0' or '\u202F' or '\u2007' or '\u3000' ||
                     ch is >= '\u2000' and <= '\u200A')
            {
                severity = ScriptDiagnosticSeverity.Warning;
                message = $"Nonstandard Unicode whitespace U+{cp} looks like an ordinary space but is not a command separator.";
            }
            else if (ch is '\u200B' or '\u200C' or '\u200D' or '\u2060' or '\u00AD')
            {
                severity = ScriptDiagnosticSeverity.Warning;
                message = $"Invisible / zero-width Unicode character U+{cp} can alter command or variable names.";
            }
            else if (ch is >= '\u202A' and <= '\u202E' ||
                     ch is >= '\u2066' and <= '\u2069')
            {
                severity = ScriptDiagnosticSeverity.Error;
                message = $"Unicode bidirectional override/isolate U+{cp} can disguise the displayed order of script text.";
            }
            else if (ch is '\u200E' or '\u200F' or '\u061C')
            {
                severity = ScriptDiagnosticSeverity.Information;
                message = $"Text direction mark U+{cp}; common with Hebrew/Arabic, but can alter visual command order.";
            }
            else if (ch is '\u2018' or '\u2019' or '\u201C' or '\u201D' or
                     '\u2013' or '\u2014' or '\u2212')
            {
                severity = ScriptDiagnosticSeverity.Warning;
                message = $"Typography punctuation U+{cp} resembles ASCII quotes, hyphens or minus signs. Verify syntax if used in commands.";
            }
            else if (ch == '\u007F' || char.GetUnicodeCategory(ch) ==
                     UnicodeCategory.Control)
            {
                severity = ScriptDiagnosticSeverity.Warning;
                message = $"Nonprinting Unicode control character U+{cp}.";
            }

            if (severity is not null && message is not null)
                findings.Add(new ScriptDiagnostic(line, column, severity.Value, message));
            column++;
        }

        if (ScriptEncodingService.HasMixedNewlines(text))
            findings.Add(new ScriptDiagnostic(0, 0,
                ScriptDiagnosticSeverity.Warning,
                "Mixed CRLF/LF/CR line endings detected. Choose one format when converting."));

        if (!ScriptEncodingService.CanRoundTrip(text, codePage, out var error))
            findings.Add(new ScriptDiagnostic(0, 0,
                ScriptDiagnosticSeverity.Error,
                $"Text cannot be saved losslessly as code page {codePage}: {error}"));

        if (batch && ScriptEncodingService.SupportsBom(codePage))
        {
            if (emitBom)
                findings.Add(new ScriptDiagnostic(0, 0,
                    ScriptDiagnosticSeverity.Warning,
                    "BAT/CMD with a Unicode BOM may not run reliably under cmd.exe. Validate target Windows/code page."));
            else if (codePage is 1200 or 1201 or 12000 or 12001)
                findings.Add(new ScriptDiagnostic(0, 0,
                    ScriptDiagnosticSeverity.Warning,
                    "UTF-16/UTF-32 BAT/CMD may not execute in cmd.exe; prefer the correct Windows/DOS code page."));
        }

        if (powershell && codePage == 65001 && !emitBom && text.Any(ch => ch > 0x7F))
            findings.Add(new ScriptDiagnostic(0, 0,
                ScriptDiagnosticSeverity.Warning,
                "Windows PowerShell 5.1 may read UTF-8 without BOM as ANSI. For non-ASCII PS1 consider UTF-8 BOM; PowerShell 7 behaves differently."));

        return findings;
    }
}

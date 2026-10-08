using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace GPOSettingsExplorer.Services;

public enum ScriptDiagnosticSeverity
{
    Error,
    Warning,
    Information
}

public sealed record ScriptDiagnostic(
    int Line,
    int Column,
    ScriptDiagnosticSeverity Severity,
    string Message)
{
    public string Location => Line > 0 ? $"Ln {Line}, Col {Column}" : "General";
}

public static class ScriptSyntaxService
{
    private static readonly Regex BatchLabel = new(
        @"(?im)^[ \t]*:(?!:)(?<name>[a-zA-Z0-9_.-]+)(?:[ \t]|$)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex BatchGoto = new(
        @"(?im)^\s*(?:(?:@?\s*)|(?:if\s+.+?\s+))(?:(?:goto\s+:?)|(?:call\s+:))(?<name>[a-zA-Z0-9_.-]+)\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static bool IsPowerShell(string fileName) =>
        new[] { ".ps1", ".psm1", ".psd1" }.Contains(
            Path.GetExtension(fileName), StringComparer.OrdinalIgnoreCase);

    public static bool IsBatch(string fileName) =>
        new[] { ".bat", ".cmd" }.Contains(
            Path.GetExtension(fileName), StringComparer.OrdinalIgnoreCase);

    public static string CheckDescription(string fileName) =>
        IsPowerShell(fileName)
            ? "PowerShell AST parser (PowerShell source is never executed)"
            : IsBatch(fileName)
                ? "Safe BAT/CMD static checks (label resolution and text format)"
                : "Syntax validation is not available for this extension";

    public static IReadOnlyList<ScriptDiagnostic> Analyze(
        string script,
        string fileName)
    {
        if (IsPowerShell(fileName))
            return CheckPowerShell(script, fileName);
        if (IsBatch(fileName))
            return CheckBatch(script);
        return new[]
        {
            new ScriptDiagnostic(0, 0, ScriptDiagnosticSeverity.Information,
                $"Static validation for {Path.GetExtension(fileName)} is not currently supported.")
        };
    }

    public static IReadOnlyList<ScriptDiagnostic> CheckBatch(string script)
    {
        var problems = new List<ScriptDiagnostic>();
        var lines = script.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal).Split('\n');

        var labels = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < lines.Length; i++)
        {
            if (lines[i].TrimStart().StartsWith("~~~", StringComparison.Ordinal) ||
                lines[i].TrimStart().StartsWith(new string((char)96, 3), StringComparison.Ordinal))
                problems.Add(new ScriptDiagnostic(i + 1, 1,
                    ScriptDiagnosticSeverity.Error,
                    "Markdown code fence is not valid BAT/CMD syntax. Remove the wrapping delimiter line."));

            var label = BatchLabel.Match(lines[i]);
            if (!label.Success)
                continue;

            var value = label.Groups["name"].Value;
            if (labels.TryGetValue(value, out var previousLine))
                problems.Add(new ScriptDiagnostic(i + 1, label.Index + 1,
                    ScriptDiagnosticSeverity.Warning,
                    $"Duplicate label '{value}' (first defined on line {previousLine})."));
            else
                labels[value] = i + 1;
        }

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimStart();
            if (line.StartsWith("rem ", StringComparison.OrdinalIgnoreCase) ||
                line.Equals("rem", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("::", StringComparison.Ordinal) ||
                line.StartsWith(":"))
                continue;

            var match = BatchGoto.Match(lines[i]);
            if (!match.Success)
                continue;

            var name = match.Groups["name"].Value;
            if (name.Equals("EOF", StringComparison.OrdinalIgnoreCase) ||
                labels.ContainsKey(name))
                continue;

            problems.Add(new ScriptDiagnostic(
                i + 1, match.Groups["name"].Index + 1,
                ScriptDiagnosticSeverity.Warning,
                $"The target label '{name}' was not found in this script."));
        }

        return problems;
    }

    private static IReadOnlyList<ScriptDiagnostic> CheckPowerShell(
        string text,
        string fileName)
    {
        var extension = Path.GetExtension(fileName);
        var temp = Path.Combine(Path.GetTempPath(),
            "GPOSettingsExplorer-Syntax-" + Guid.NewGuid().ToString("N") + extension);

        try
        {
            // This is a parser-only operation. The script is stored as data,
            // never passed to Invoke-Expression or executed as a script.
            File.WriteAllText(temp, text, new UTF8Encoding(false));
            var encodedPath = Convert.ToBase64String(Encoding.Unicode.GetBytes(temp));
            var command = $$"""
                $ErrorActionPreference = 'Stop'
                $path = [Text.Encoding]::Unicode.GetString([Convert]::FromBase64String('{{encodedPath}}'))
                $tokens = $null
                $errors = $null
                $null = [System.Management.Automation.Language.Parser]::ParseFile($path,[ref]$tokens,[ref]$errors)
                foreach ($problem in $errors) {
                    $message = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($problem.Message))
                    [Console]::Out.WriteLine(('{0}|{1}|{2}' -f $problem.Extent.StartLineNumber,$problem.Extent.StartColumnNumber,$message))
                }
                """;

            var psPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.System),
                "WindowsPowerShell", "v1.0", "powershell.exe");

            var processInfo = new ProcessStartInfo
            {
                FileName = File.Exists(psPath) ? psPath : "powershell.exe",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            processInfo.ArgumentList.Add("-NoLogo");
            processInfo.ArgumentList.Add("-NoProfile");
            processInfo.ArgumentList.Add("-NonInteractive");
            processInfo.ArgumentList.Add("-EncodedCommand");
            processInfo.ArgumentList.Add(
                Convert.ToBase64String(Encoding.Unicode.GetBytes(command)));

            using var process = Process.Start(processInfo)
                ?? throw new InvalidOperationException("Could not start Windows PowerShell syntax parser.");

            if (!process.WaitForExit(15_000))
            {
                process.Kill(entireProcessTree: true);
                return new[] { new ScriptDiagnostic(0, 0, ScriptDiagnosticSeverity.Warning,
                    "PowerShell parser timed out; syntax validation was not completed.") };
            }

            var output = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();

            if (process.ExitCode != 0)
                return new[] { new ScriptDiagnostic(0, 0, ScriptDiagnosticSeverity.Warning,
                    "PowerShell syntax parser unavailable: " +
                    (string.IsNullOrWhiteSpace(stderr) ? "exit code " + process.ExitCode
                        : stderr.Trim().Split('\n').FirstOrDefault())) };

            var diagnostics = new List<ScriptDiagnostic>();
            foreach (var line in output.Split('\n',
                         StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var fields = line.Split('|', 3);
                if (fields.Length != 3 ||
                    !int.TryParse(fields[0], out var row) ||
                    !int.TryParse(fields[1], out var column))
                    continue;

                try
                {
                    var message = Encoding.UTF8.GetString(Convert.FromBase64String(fields[2]));
                    diagnostics.Add(new ScriptDiagnostic(row, column,
                        ScriptDiagnosticSeverity.Error, message));
                }
                catch (FormatException)
                {
                    // Unexpected messages from the host are not source text.
                }
            }
            return diagnostics;
        }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception
                                       or UnauthorizedAccessException or InvalidOperationException)
        {
            return new[]
            {
                new ScriptDiagnostic(0, 0, ScriptDiagnosticSeverity.Warning,
                    "PowerShell parser could not run: " + ex.Message)
            };
        }
        finally
        {
            try { File.Delete(temp); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}

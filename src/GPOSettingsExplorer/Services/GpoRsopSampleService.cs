using System.Diagnostics;
using System.Text.RegularExpressions;
using GPOSettingsExplorer.Models;

namespace GPOSettingsExplorer.Services;

/// <summary>
/// A single-machine, logged RSoP sample, never a theoretical effective-policy
/// simulation. Refuses to turn absent/ambiguous gpresult evidence into "blocked".
/// </summary>
public sealed record GpoRsopSample(
    string Status, string Computer, string Scope, string Details);

public sealed class GpoRsopSampleService
{
    private static readonly Regex HostName = new(
        @"^[a-zA-Z0-9_.:-]{1,253}$", RegexOptions.Compiled);

    public GpoRsopSample Verify(
        GpoInfo gpo, string computer, string scope, string user = "")
    {
        ArgumentNullException.ThrowIfNull(gpo);
        computer = computer.Trim();
        user = user.Trim();
        if (!HostName.IsMatch(computer))
            throw new ArgumentException("Enter a valid hostname (or '.' for local computer).");
        if (!scope.Equals("Computer", StringComparison.OrdinalIgnoreCase) &&
            !scope.Equals("User", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Scope must be Computer or User.");
        if (scope.Equals("User", StringComparison.OrdinalIgnoreCase) &&
            string.IsNullOrWhiteSpace(user))
            throw new ArgumentException("User scope requires DOMAIN\\user for this RSoP sample.");

        const int limit = 16 * 1024 * 1024;
        var filename = Path.Combine(Path.GetTempPath(),
            "GPOSE-effective-sample-" + Guid.NewGuid().ToString("N") + ".xml");
        try
        {
            var process = new ProcessStartInfo
            {
                FileName = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.System), "gpresult.exe"),
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true
            };
            if (computer != "." &&
                !computer.Equals("localhost", StringComparison.OrdinalIgnoreCase))
            {
                process.ArgumentList.Add("/S");
                process.ArgumentList.Add(computer);
            }
            if (scope.Equals("User", StringComparison.OrdinalIgnoreCase))
            {
                process.ArgumentList.Add("/USER");
                process.ArgumentList.Add(user);
            }
            process.ArgumentList.Add("/SCOPE");
            process.ArgumentList.Add(scope.ToUpperInvariant());
            process.ArgumentList.Add("/X");
            process.ArgumentList.Add(filename);
            process.ArgumentList.Add("/F");

            using var instance = Process.Start(process)
                ?? throw new IOException("gpresult.exe could not start.");
            var stdout = instance.StandardOutput.ReadToEndAsync();
            var stderr = instance.StandardError.ReadToEndAsync();
            if (!instance.WaitForExit(60_000))
            {
                instance.Kill(entireProcessTree: true);
                return Unknown("gpresult timed out after 60 seconds.");
            }
            var output = stdout.GetAwaiter().GetResult();
            var error = stderr.GetAwaiter().GetResult();
            if (instance.ExitCode != 0 || !File.Exists(filename))
                return Unknown("gpresult failed: exit " + instance.ExitCode + ". " +
                    (string.IsNullOrWhiteSpace(error) ? output : error).Trim());
            var info = new FileInfo(filename);
            if (info.Length > limit)
                return Unknown("gpresult XML exceeds the 16 MiB read cap.");

            var xml = File.ReadAllText(filename);
            var status = GpoApplicabilityVerificationService.AssessRsopXml(
                xml, scope, new[] { gpo.Id });
            var text = status.Details + "\n" +
                "Result reflects the last available policy processing on this ONE client, " +
                "not a future change, domain-wide membership, policy precedence or every setting.";
            return new GpoRsopSample(status.AllApplied ? "Applied (logged sample)" :
                status.AnyExcluded ? "Excluded (logged sample)" : "Unknown / incomplete",
                computer, scope, text);
        }
        catch (Exception ex)
        {
            return Unknown("RSoP unavailable (service, permissions, firewall, logged-on user, " +
                "XML structure or policy processing): " + ex.GetType().Name + ": " + ex.Message);
        }
        finally
        {
            try { File.Delete(filename); } catch { }
        }

        GpoRsopSample Unknown(string reason) =>
            new("Unknown / incomplete", computer, scope, reason);
    }
}

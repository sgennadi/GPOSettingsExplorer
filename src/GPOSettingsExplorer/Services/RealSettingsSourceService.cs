using System.Security.Cryptography;
using GPOSettingsExplorer.Models;

namespace GPOSettingsExplorer.Services;

/// <summary>
/// Reads one selected GPO's source files from the session-pinned DC.
/// No cross-domain fallback, process execution, registry access or write.
/// </summary>
public sealed class RealSettingsSourceService
{
    public RealSettingsScanResult Scan(
        GpoInfo gpo, CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(gpo);
        var context = DomainConnectionState.Context;
        if (context is null || string.IsNullOrWhiteSpace(context.ConnectedServer) ||
            !context.DomainName.Equals(gpo.DomainName, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Real Settings source scan requires a connected, pinned domain controller " +
                "matching the selected GPO. No fallback to a different SYSVOL server.");
        }

        var pinnedDc = context.ConnectedServer;
        var domain = context.DomainName;
        var root = Path.Combine(DomainConnectionState.BuildSysvolRoot(domain),
            "Policies", gpo.Id.ToString("B"));
        var sources = new (string RelativePath, string Scope, bool Registry)[]
        {
            (Path.Combine("Machine", "Registry.pol"), "Computer", true),
            (Path.Combine("User", "Registry.pol"), "User", true),
            (Path.Combine("Machine", "Microsoft", "Windows NT",
                "SecEdit", "GptTmpl.inf"), "Computer", false)
        };

        var rows = new List<RealSettingRecord>();
        var files = new List<RealSettingsFileEvidence>();
        foreach (var source in sources)
        {
            cancellation.ThrowIfCancellationRequested();
            var path = Path.Combine(root, source.RelativePath);
            try
            {
                if (!File.Exists(path))
                {
                    files.Add(new RealSettingsFileEvidence(path, "Absent", 0, "",
                        "Optional source file not present. This does NOT prove " +
                        "Not Configured; another source/CSE may hold settings."));
                    continue;
                }

                var stat = new FileInfo(path);
                var limit = source.Registry
                    ? RegistryPolReader.MaxFileBytes
                    : SecurityTemplateSourceReader.MaxFileBytes;
                if (stat.Length > limit)
                {
                    files.Add(new RealSettingsFileEvidence(path, "Invalid", 0, "",
                        $"Size {stat.Length:N0} bytes exceeds {limit:N0} read cap."));
                    continue;
                }

                var timeBefore = stat.LastWriteTimeUtc;
                byte[] bytes;
                // FileShare avoids blocking GPMC and does not request writes.
                using (var stream = new FileStream(path, FileMode.Open,
                           FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    using var memory = new MemoryStream();
                    stream.CopyTo(memory);
                    bytes = memory.ToArray();
                }

                cancellation.ThrowIfCancellationRequested();
                if (bytes.Length > limit)
                {
                    files.Add(new RealSettingsFileEvidence(path, "Invalid", 0, "",
                        "Source grew beyond size cap while reading."));
                    continue;
                }

                var sha = Convert.ToHexString(SHA256.HashData(bytes));
                var parsed = source.Registry
                    ? RegistryPolReader.Parse(bytes, gpo.Id, gpo.DisplayName,
                        source.Scope, path, sha)
                    : SecurityTemplateSourceReader.Parse(bytes,
                        gpo.Id, gpo.DisplayName, path, sha);

                rows.AddRange(parsed.Rows);
                var changedDuringRead = File.GetLastWriteTimeUtc(path) != timeBefore;
                var issues = parsed.Issues.ToList();
                if (changedDuringRead)
                    issues.Add("Source file timestamp changed during scan; data may be stale.");

                files.Add(new RealSettingsFileEvidence(
                    path,
                    issues.Count == 0 ? "Read" : "Partial",
                    parsed.Rows.Count,
                    sha,
                    issues.Count == 0
                        ? $"Read-only snapshot; {bytes.Length:N0} bytes, SHA-256 verified for captured bytes."
                        : string.Join(" | ", issues.Take(12)) +
                          (issues.Count > 12
                              ? $" (+{issues.Count - 12} more issues)" : "")));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                                       System.Security.SecurityException or
                                       InvalidDataException)
            {
                files.Add(new RealSettingsFileEvidence(path, "Unreadable", 0, "",
                    ex.GetType().Name + ": " + ex.Message));
            }
        }

        return new RealSettingsScanResult(gpo.Id, gpo.DisplayName,
            domain, pinnedDc, DateTimeOffset.Now, rows, files);
    }
}

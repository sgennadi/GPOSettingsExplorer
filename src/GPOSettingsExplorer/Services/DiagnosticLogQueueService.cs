using System.Security.Cryptography;
using System.Text.Json;

namespace GPOSettingsExplorer.Services;

public sealed record PendingDiagnosticLog(
    string Path, string Kind, string Sha256, long Size, DateTime LastModifiedUtc);

internal sealed record SubmittedDiagnostic(
    string Sha256, DateTime SubmittedUtc, string IssueUrl);

internal sealed class DiagnosticQueueState
{
    public DateTime LastPromptUtc { get; set; }
    public List<SubmittedDiagnostic> Submitted { get; set; } = new();
}

/// <summary>
/// Stores a per-installation queue of local diagnostics. Offline files remain
/// untouched. After GitHub acknowledges an issue, fingerprints are recorded
/// durably, then submitted files are moved to a short-lived local archive.
/// </summary>
public sealed class DiagnosticLogQueueService
{
    public const int AutomaticOfferThreshold = 3;
    public static readonly TimeSpan PromptCooldown = TimeSpan.FromHours(12);
    public static readonly TimeSpan SentArchiveRetention = TimeSpan.FromDays(14);
    private readonly string _errors;
    private readonly string _audit;
    private readonly string _stateDirectory;
    private readonly object _gate = new();

    public DiagnosticLogQueueService(
        string? errorsDirectory = null,
        string? auditDirectory = null,
        string? stateDirectory = null)
    {
        _errors = errorsDirectory ?? CrashLogService.LogDirectory;
        _audit = auditDirectory ?? StoragePaths.Audit;
        _stateDirectory = stateDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GPOSettingsExplorer", "DiagnosticQueue");
    }

    private string StatePath => Path.Combine(_stateDirectory, "queue.json");
    private string ArchiveDirectory => Path.Combine(_stateDirectory, "SentArchive");

    public IReadOnlyList<PendingDiagnosticLog> GetPending()
    {
        lock (_gate)
        {
            var state = ReadState();
            var sent = state.Submitted.Select(x => x.Sha256)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var files = new List<(string Path, string Kind)>();
            Append(files, _errors, "error-*.log", "Error");
            Append(files, _audit, "MmcRouteAudit-*.txt", "MMC route tree");
            Append(files, _audit, "GpoHierarchy-*.txt", "GPO link tree");

            var result = new List<PendingDiagnosticLog>();
            foreach (var item in files
                         .OrderByDescending(x => File.GetLastWriteTimeUtc(x.Path))
                         .Take(250))
            {
                try
                {
                    var info = new FileInfo(item.Path);
                    // Skip logs that may still be in the middle of a write.
                    if (info.Length <= 0 ||
                        info.LastWriteTimeUtc > DateTime.UtcNow.AddSeconds(-10))
                        continue;

                    using var stream = new FileStream(
                        item.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    var hash = Convert.ToHexString(SHA256.HashData(stream));
                    if (sent.Contains(hash))
                        continue;
                    result.Add(new PendingDiagnosticLog(
                        info.FullName, item.Kind, hash, info.Length, info.LastWriteTimeUtc));
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    // Do not block the user because a log is locked/missing.
                }
            }
            return result.OrderBy(x => x.LastModifiedUtc).ToArray();
        }
    }

    private static void Append(
        List<(string Path, string Kind)> files, string dir, string pattern, string kind)
    {
        try
        {
            if (!Directory.Exists(dir))
                return;
            files.AddRange(Directory.EnumerateFiles(
                dir, pattern, SearchOption.TopDirectoryOnly).Select(path => (path, kind)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    public bool ShouldOffer(int pendingCount, DateTime utcNow)
    {
        if (pendingCount < AutomaticOfferThreshold)
            return false;
        lock (_gate)
        {
            var last = ReadState().LastPromptUtc;
            return last == DateTime.MinValue ||
                   utcNow - last >= PromptCooldown;
        }
    }

    public void MarkOffered(DateTime utcNow)
    {
        lock (_gate)
        {
            var state = ReadState();
            state.LastPromptUtc = utcNow;
            WriteState(state);
        }
    }

    public void MarkSubmitted(
        IEnumerable<PendingDiagnosticLog> submitted, string confirmedIssueUrl)
    {
        if (!Uri.TryCreate(confirmedIssueUrl, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            !uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) ||
            !uri.AbsolutePath.StartsWith(
                "/sgennadi/GPOSettingsExplorer/issues/", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException(
                "A confirmed GitHub issue URL is required before cleaning local logs.",
                nameof(confirmedIssueUrl));

        lock (_gate)
        {
            var state = ReadState();
            var acknowledged = new List<PendingDiagnosticLog>();

            foreach (var log in submitted)
            {
                try
                {
                    if (!File.Exists(log.Path))
                        continue;
                    using var file = new FileStream(
                        log.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    var current = Convert.ToHexString(SHA256.HashData(file));
                    if (current.Equals(log.Sha256, StringComparison.OrdinalIgnoreCase))
                    {
                        acknowledged.Add(log);
                        if (!state.Submitted.Any(x => x.Sha256.Equals(
                                current, StringComparison.OrdinalIgnoreCase)))
                            state.Submitted.Add(new SubmittedDiagnostic(
                                current, DateTime.UtcNow, confirmedIssueUrl));
                    }
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                }
            }

            // Persist acknowledgement *before* moving the original files.
            // If the program closes at this point, retrying will never
            // create the same issue twice.
            WriteState(state);
            foreach (var log in acknowledged)
            {
                try
                {
                    Directory.CreateDirectory(ArchiveDirectory);
                    var archiveFile = Path.Combine(ArchiveDirectory,
                        log.Sha256[..20] + "-" + Path.GetFileName(log.Path));
                    if (File.Exists(archiveFile))
                        File.Delete(log.Path);
                    else
                        File.Move(log.Path, archiveFile);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    // Leave the source untouched if archival failed.
                    // The acknowledged fingerprint prevents a second upload.
                }
            }
            CleanupSubmittedArchive();
        }
    }

    public void CleanupSubmittedArchive()
    {
        try
        {
            if (!Directory.Exists(ArchiveDirectory))
                return;
            var cutoff = DateTime.UtcNow - SentArchiveRetention;
            foreach (var path in Directory.EnumerateFiles(ArchiveDirectory))
            {
                if (File.GetLastWriteTimeUtc(path) < cutoff)
                {
                    try { File.Delete(path); }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    private DiagnosticQueueState ReadState()
    {
        try
        {
            return File.Exists(StatePath)
                ? JsonSerializer.Deserialize<DiagnosticQueueState>(
                      File.ReadAllText(StatePath)) ?? new DiagnosticQueueState()
                : new DiagnosticQueueState();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException
                                       or JsonException)
        {
            return new DiagnosticQueueState();
        }
    }

    private void WriteState(DiagnosticQueueState state)
    {
        Directory.CreateDirectory(_stateDirectory);
        var temp = StatePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var json = JsonSerializer.Serialize(state);
            File.WriteAllText(temp, json);
            File.Move(temp, StatePath, true);
        }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); }
            catch (IOException) { }
        }
    }
}

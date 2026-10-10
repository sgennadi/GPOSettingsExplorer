using System.Security.Cryptography;
using GPOSettingsExplorer.Models;

namespace GPOSettingsExplorer.Services;

/// <summary>
/// Streams and fingerprints bounded GPO SYSVOL directory trees from explicitly
/// named DCs. No DFS aliases, AD writes, file mutation or DFSR repair.
/// Identical files are content evidence, NOT full replication health.
/// </summary>
public sealed record GpoSysvolTreeFile(
    string RelativePath, long Bytes, string Sha256, string Status);

public sealed record GpoSysvolTreeSnapshot(
    string Dc, string Root, bool Complete, long BytesRead,
    IReadOnlyList<GpoSysvolTreeFile> Files, IReadOnlyList<string> Issues);

public sealed record GpoSysvolTreeDifference(
    string RelativePath, string Status, string Evidence);

public sealed record GpoSysvolTreeReport(
    Guid GpoId, string Domain, DateTimeOffset CapturedUtc,
    IReadOnlyList<GpoSysvolTreeSnapshot> Controllers,
    IReadOnlyList<GpoSysvolTreeDifference> Differences)
{
    public bool Complete => Controllers.Count >= 2 &&
        Controllers.All(x => x.Complete);

    public int ChangedCount => Differences.Count(x =>
        x.Status is "Content mismatch" or "Different presence");

    public string ToText()
    {
        var lines = new List<string>
        {
            "GPO FULL SYSVOL FILE-TREE COMPARISON (READ ONLY)",
            "Captured UTC: " + CapturedUtc.ToString("O"),
            "Domain: " + Domain + " | GPO: " + GpoId.ToString("B"),
            "Overall: " + (Complete ? "DIRECTORY SCANS FINISHED" :
                          "INCOMPLETE / UNKNOWN: one or more controllers could not be verified"),
            "Controllers: " + Controllers.Count + " | file deviations: " + ChangedCount,
            "Matching SHA-256 proves only captured bytes, not DFSR health, " +
            "AD replication, client RSoP or absence of concurrent GPO edits.",
            ""
        };
        foreach (var dc in Controllers)
        {
            lines.Add(dc.Dc + ": " + (dc.Complete ? "Complete bounded scan" : "INCOMPLETE") +
                ", files " + dc.Files.Count + ", bytes " + dc.BytesRead);
            lines.AddRange(dc.Issues.Take(12).Select(x => "  [UNKNOWN] " + x));
            if (dc.Issues.Count > 12)
                lines.Add("  [UNKNOWN] Additional issues: " + (dc.Issues.Count - 12));
        }
        lines.Add("");
        lines.Add("Nonmatching, unavailable or partially verified paths:");
        var detailed = Differences.Where(x => x.Status != "Match").Take(600).ToArray();
        if (detailed.Length == 0)
            lines.Add(Complete
                ? "No differences found within this bounded snapshot."
                : "No confirmed differences; some source files were not inspected.");
        else
            lines.AddRange(detailed.Select(x =>
                "[" + x.Status + "] " + x.RelativePath + ": " + x.Evidence));
        var remaining = Differences.Count(x => x.Status != "Match") - detailed.Length;
        if (remaining > 0)
            lines.Add("Additional findings omitted from display: " + remaining +
                      " (do not infer all files matched).");
        return string.Join("\n", lines);
    }
}

public static class GpoSysvolTreeIntegrityService
{
    public const int MaxFilesPerDc = 4000;
    public const int MaxDepth = 30;
    public const long MaxBytesPerFile = 32L * 1024 * 1024;
    public const long MaxTotalBytesPerDc = 256L * 1024 * 1024;
    public const int MaxRelativePathChars = 1024;

    public static GpoSysvolTreeReport Compare(
        GpoInfo gpo, string controllerNames, CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(gpo);
        var session = DomainConnectionState.Context;
        if (session is null || string.IsNullOrWhiteSpace(session.ConnectedServer) ||
            !session.DomainName.Equals(gpo.DomainName, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "A connected, pinned DC for this GPO domain is required.");
        var hosts = GpoCrossDcConsistencyService.ValidateControllers(
            controllerNames, gpo.DomainName);
        if (hosts.Length < 2)
            throw new ArgumentException("Enter at least two actual DC hostnames.");

        var snapshots = new List<GpoSysvolTreeSnapshot>();
        foreach (var dc in hosts)
        {
            cancellation.ThrowIfCancellationRequested();
            var root = $@"\\{dc}\SYSVOL\{gpo.DomainName}\Policies\{gpo.Id:B}";
            snapshots.Add(ScanTree(root, dc, cancellation));
        }
        return CompareSnapshots(gpo.Id, gpo.DomainName, snapshots);
    }

    /// <summary>
    /// Public pure comparison for fixture-based regression tests.
    /// If a controller scan is partial, missing files are UNKNOWN, never
    /// considered a proven delete or successful full replication.
    /// </summary>
    public static GpoSysvolTreeReport CompareSnapshots(
        Guid gpoId, string domain, IReadOnlyList<GpoSysvolTreeSnapshot> snapshots)
    {
        ArgumentNullException.ThrowIfNull(snapshots);
        if (snapshots.Count is < 2 or > 16 ||
            snapshots.Select(x => x.Dc)
                .Distinct(StringComparer.OrdinalIgnoreCase).Count() != snapshots.Count)
            throw new ArgumentException("Use two to sixteen distinct DC snapshots.");

        var allNames = snapshots.SelectMany(s => s.Files)
            .Select(f => f.RelativePath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToArray();
        var maps = new Dictionary<string, GpoSysvolTreeFile>[snapshots.Count];
        for (var i = 0; i < snapshots.Count; i++)
        {
            maps[i] = new Dictionary<string, GpoSysvolTreeFile>(
                StringComparer.OrdinalIgnoreCase);
            foreach (var file in snapshots[i].Files)
                if (!maps[i].TryAdd(file.RelativePath, file))
                    throw new InvalidDataException(
                        "Ambiguous duplicate relative file identity from " +
                        snapshots[i].Dc + ": " + file.RelativePath);
        }

        var differences = new List<GpoSysvolTreeDifference>();
        foreach (var name in allNames)
        {
            var present = Enumerable.Range(0, snapshots.Count)
                .Select(i => (Controller: snapshots[i],
                    File: maps[i].GetValueOrDefault(name))).ToArray();
            var unreadable = present.Any(x =>
                x.File is not null &&
                (x.File.Status != "Read" ||
                 x.File.Sha256.Length != 64 ||
                 !x.File.Sha256.All(Uri.IsHexDigit)));
            var missingOnIncomplete = present.Any(x =>
                x.File is null && !x.Controller.Complete);
            var hashes = present.Where(x => x.File is { Status: "Read" })
                .Select(x => x.File!.Sha256)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var missing = present.Count(x => x.File is null);
            var status = unreadable || missingOnIncomplete
                ? "Unknown"
                : missing > 0
                    ? "Different presence"
                    : hashes.Length > 1 ? "Content mismatch" : "Match";

            // When all scanned DCs completed and a GPO policy was missing
            // from every tree, it cannot appear in the union, which is fine.
            var summary = status switch
            {
                "Unknown" => "Unreadable or incomplete controller inventory; cannot infer absence.",
                "Different presence" => string.Join(", ", present.Select(x =>
                    x.Controller.Dc + "=" +
                    (x.File is null ? "missing" : x.File.Status))),
                "Content mismatch" => "At least two DCs returned different SHA-256 content.",
                _ => "All selected DCs returned matching SHA-256 content."
            };
            differences.Add(new(name, status, summary));
        }

        return new GpoSysvolTreeReport(gpoId, domain, DateTimeOffset.UtcNow,
            snapshots, differences);
    }

    public static GpoSysvolTreeSnapshot ScanTree(
        string directory, string dc, CancellationToken cancellation = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentException.ThrowIfNullOrWhiteSpace(dc);

        var root = Path.GetFullPath(directory);
        var files = new List<GpoSysvolTreeFile>();
        var issues = new List<string>();
        long total = 0;
        var encountered = 0;

        if (!Directory.Exists(root))
            return new(dc, root, false, 0, files,
                new[] { "GPO SYSVOL root unavailable or access denied; absence not verified." });

        try
        {
            var rootInfo = new DirectoryInfo(root);
            if ((rootInfo.Attributes & FileAttributes.ReparsePoint) != 0)
                return new(dc, root, false, 0, files,
                    new[] { "Directory root is a reparse point; refusing uncertain redirection." });

            var pending = new Stack<(DirectoryInfo Directory, int Depth)>();
            pending.Push((rootInfo, 0));
            while (pending.Count > 0)
            {
                cancellation.ThrowIfCancellationRequested();
                var (current, depth) = pending.Pop();
                if (depth > MaxDepth)
                {
                    issues.Add("Maximum nested directory depth reached.");
                    continue;
                }

                try
                {
                    foreach (var entry in current.EnumerateFileSystemInfos())
                    {
                        cancellation.ThrowIfCancellationRequested();
                        if (++encountered > MaxFilesPerDc)
                        {
                            issues.Add("Exceeded the " + MaxFilesPerDc +
                                " directory entry/file cap.");
                            pending.Clear();
                            break;
                        }
                        var relative = Path.GetRelativePath(root, entry.FullName)
                            .Replace(Path.AltDirectorySeparatorChar,
                                Path.DirectorySeparatorChar);
                        if (relative.Length > MaxRelativePathChars ||
                            relative == ".." ||
                            relative.StartsWith(".." + Path.DirectorySeparatorChar,
                                StringComparison.Ordinal))
                        {
                            issues.Add("Unsafe or oversized relative path encountered.");
                            continue;
                        }

                        try
                        {
                            entry.Refresh();
                            if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
                            {
                                issues.Add("Reparse point skipped: " + relative);
                                continue;
                            }
                            if ((entry.Attributes & FileAttributes.Directory) != 0)
                            {
                                if (depth >= MaxDepth)
                                    issues.Add("Maximum depth reached: " + relative);
                                else
                                    pending.Push((new DirectoryInfo(entry.FullName),
                                        depth + 1));
                                continue;
                            }
                            var item = new FileInfo(entry.FullName);
                            if (item.Length > MaxBytesPerFile ||
                                total + item.Length > MaxTotalBytesPerDc)
                            {
                                issues.Add("File/aggregate byte cap prevents hashing: " + relative);
                                files.Add(new(relative, item.Length, "", "Unknown"));
                                continue;
                            }
                            try
                            {
                                var hash = HashFile(item, MaxBytesPerFile,
                                    MaxTotalBytesPerDc - total, cancellation,
                                    out var read);
                                total += read;
                                files.Add(new(relative, read, hash, "Read"));
                            }
                            catch (Exception ex) when (ex is IOException or InvalidDataException or
                                UnauthorizedAccessException or System.Security.SecurityException)
                            {
                                issues.Add("File read was incomplete: " + relative +
                                    " (" + ex.GetType().Name + ")");
                                files.Add(new(relative, 0, "", "Unknown"));
                            }
                        }
                        catch (Exception ex) when (ex is IOException or
                            UnauthorizedAccessException or System.Security.SecurityException)
                        {
                            issues.Add("Cannot inspect: " + relative +
                                " (" + ex.GetType().Name + ")");
                        }
                    }
                }
                catch (Exception ex) when (ex is IOException or
                    UnauthorizedAccessException or System.Security.SecurityException)
                {
                    issues.Add("Cannot enumerate a directory (" +
                        ex.GetType().Name + "): " +
                        Path.GetRelativePath(root, current.FullName));
                }
            }
        }
        catch (Exception ex) when (ex is IOException or
            UnauthorizedAccessException or System.Security.SecurityException)
        {
            issues.Add("Directory tree inspection failed: " +
                ex.GetType().Name);
        }
        return new(dc, root, issues.Count == 0, total,
            files.OrderBy(f => f.RelativePath,
                StringComparer.OrdinalIgnoreCase).ToArray(), issues);
    }

    private static string HashFile(
        FileInfo file, long maxFile, long maxRemaining,
        CancellationToken cancellation, out long total)
    {
        file.Refresh();
        var beforeLength = file.Length;
        var beforeWrite = file.LastWriteTimeUtc;
        if (beforeLength > maxFile || beforeLength > maxRemaining)
            throw new InvalidDataException("Source exceeds bounded read cap.");
        using var stream = new FileStream(file.FullName, FileMode.Open,
            FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (stream.Length > maxFile || stream.Length > maxRemaining)
            throw new InvalidDataException("Source grew beyond bounded read cap.");
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var chunk = new byte[64 * 1024];
        total = 0;
        int count;
        while ((count = stream.Read(chunk, 0, chunk.Length)) > 0)
        {
            cancellation.ThrowIfCancellationRequested();
            total += count;
            if (total > maxFile || total > maxRemaining)
                throw new InvalidDataException("Source grew beyond streaming byte cap.");
            hash.AppendData(chunk, 0, count);
        }
        file.Refresh();
        if (total != beforeLength || stream.Length != beforeLength ||
            file.Length != beforeLength || file.LastWriteTimeUtc != beforeWrite)
            throw new IOException(
                "File changed during snapshot; hash not accepted as stable.");
        return Convert.ToHexString(hash.GetHashAndReset());
    }
}

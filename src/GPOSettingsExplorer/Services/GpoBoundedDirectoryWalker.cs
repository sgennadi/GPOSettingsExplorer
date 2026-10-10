namespace GPOSettingsExplorer.Services;

/// <summary>
/// No-follow directory tree discovery for sensitive GPO source inspection.
/// Limits all directory entries, not only matched script or XML files, and
/// reports incomplete coverage explicitly instead of silently skipping it.
/// </summary>
public sealed record GpoBoundedWalkResult(
    IReadOnlyList<string> Files, IReadOnlyList<string> Issues)
{
    public bool Complete => Issues.Count == 0;
}

public static class GpoBoundedDirectoryWalker
{
    public const int MaxDepth = 24;

    public static GpoBoundedWalkResult Scan(
        string rootDirectory, IReadOnlySet<string> extensions,
        int maxEntries, int maxMatches,
        CancellationToken cancellation = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        ArgumentNullException.ThrowIfNull(extensions);
        if (maxEntries is < 1 or > 100_000 ||
            maxMatches is < 1 or > 10_000)
            throw new ArgumentOutOfRangeException(
                nameof(maxEntries), "Use bounded entry/match limits.");
        var files = new List<string>();
        var issues = new List<string>();
        var root = Path.GetFullPath(rootDirectory);
        var stack = new Stack<(DirectoryInfo Directory, int Depth)>();
        stack.Push((new DirectoryInfo(root), 0));
        var visited = 0;

        while (stack.Count > 0)
        {
            cancellation.ThrowIfCancellationRequested();
            var (directory, depth) = stack.Pop();
            try
            {
                directory.Refresh();
                if ((directory.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    issues.Add("Reparse-point directory skipped: " +
                        Path.GetRelativePath(root, directory.FullName));
                    continue;
                }

                foreach (var entry in directory.EnumerateFileSystemInfos())
                {
                    cancellation.ThrowIfCancellationRequested();
                    if (++visited > maxEntries)
                    {
                        issues.Add("Directory-entry cap reached (" + maxEntries +
                            "); additional files may be uninspected.");
                        stack.Clear();
                        break;
                    }

                    var relative = Path.GetRelativePath(root, entry.FullName);
                    if (relative == ".." ||
                        relative.StartsWith(".." + Path.DirectorySeparatorChar,
                            StringComparison.Ordinal) ||
                        relative.Length > 1024)
                    {
                        issues.Add("Unsafe or long relative path skipped.");
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
                                issues.Add("Depth cap reached: " + relative);
                            else
                                stack.Push((new DirectoryInfo(entry.FullName), depth + 1));
                        }
                        else if (extensions.Contains(Path.GetExtension(entry.Name)))
                        {
                            if (files.Count >= maxMatches)
                            {
                                issues.Add("Matching-file cap reached (" +
                                    maxMatches + "); additional files uninspected.");
                                stack.Clear();
                                break;
                            }
                            files.Add(entry.FullName);
                        }
                    }
                    catch (Exception ex) when (ex is IOException or
                        UnauthorizedAccessException or System.Security.SecurityException)
                    {
                        issues.Add("Entry cannot be inspected: " + relative +
                            " (" + ex.GetType().Name + ").");
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or
                UnauthorizedAccessException or System.Security.SecurityException)
            {
                issues.Add("Directory cannot be enumerated: " +
                    Path.GetRelativePath(root, directory.FullName) +
                    " (" + ex.GetType().Name + ").");
            }
        }

        return new GpoBoundedWalkResult(
            files.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray(),
            issues);
    }
}

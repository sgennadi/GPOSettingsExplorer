using GPOSettingsExplorer.Models;

namespace GPOSettingsExplorer.Services;

/// <summary>
/// A search result represents one content hash, but different GPOs contain
/// independent physical files. Selecting a copy for editing must never
/// change any other GPO just because its file contents are identical.
/// </summary>
public static class GpoScriptCopyResolver
{
    public static IReadOnlyList<GpoScriptInfo> PhysicalCopies(GpoScriptSearchResult result) =>
        result.Scripts
            .Where(item => !string.IsNullOrWhiteSpace(item.FullPath))
            .GroupBy(item => item.FullPath, StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(item => item.Referenced)
                .ThenBy(item => item.GpoName, StringComparer.CurrentCultureIgnoreCase)
                .First())
            .OrderByDescending(item => item.Exists)
            .ThenBy(item => item.ScopeDisabled)
            .ThenByDescending(item => item.Referenced)
            .ThenBy(item => item.GpoName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(item => item.Scope, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.EventName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.FullPath, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public static GpoScriptInfo? PreferredCopy(
        GpoScriptSearchResult result,
        Guid? preferredGpoId = null,
        string? preferredPath = null)
    {
        var copies = PhysicalCopies(result);
        if (copies.Count == 0)
            return null;

        if (!string.IsNullOrWhiteSpace(preferredPath))
        {
            var exact = copies.FirstOrDefault(item =>
                item.FullPath.Equals(preferredPath, StringComparison.OrdinalIgnoreCase) &&
                (!preferredGpoId.HasValue || item.GpoId == preferredGpoId.Value));
            if (exact is not null)
                return exact;
        }

        if (preferredGpoId.HasValue)
        {
            var scoped = copies.FirstOrDefault(item => item.GpoId == preferredGpoId.Value);
            if (scoped is not null)
                return scoped;
        }

        return copies[0];
    }
}

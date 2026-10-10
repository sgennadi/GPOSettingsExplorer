using System.Security.Cryptography;
using GPOSettingsExplorer.Models;

namespace GPOSettingsExplorer.Services;

/// <summary>
/// Curated standard GPP XML locations only, called after the pinned-DC
/// validation in RealSettingsSourceService. Other custom CSEs are NOT covered.
/// </summary>
internal static class GppXmlSourceScanService
{
    private static readonly (string Folder, string Filename)[] Sources =
    {
        ("Drives", "Drives.xml"),
        ("Groups", "Groups.xml"),
        ("Registry", "Registry.xml"),
        ("Files", "Files.xml"),
        ("Folders", "Folders.xml"),
        ("Printers", "Printers.xml"),
        ("ScheduledTasks", "ScheduledTasks.xml"),
        ("Services", "Services.xml"),
        ("Shortcuts", "Shortcuts.xml"),
        ("EnvironmentVariables", "EnvironmentVariables.xml"),
        ("IniFiles", "IniFiles.xml"),
        ("NetworkShares", "NetworkShares.xml"),
        ("PowerOptions", "PowerOptions.xml"),
        ("RegionalOptions", "RegionalOptions.xml"),
        ("DataSources", "DataSources.xml")
    };

    public static void Scan(
        string gpoRoot, GpoInfo gpo,
        List<RealSettingRecord> rows, List<RealSettingsFileEvidence> evidence,
        CancellationToken cancellation)
    {
        foreach (var sourceScope in new[] { "Machine", "User" })
        {
            cancellation.ThrowIfCancellationRequested();
            var prefs = Path.Combine(gpoRoot, sourceScope, "Preferences");
            if (!Directory.Exists(prefs))
            {
                evidence.Add(new RealSettingsFileEvidence(
                    prefs, "Absent", 0, "",
                    "Optional GPP Preferences directory not present or inaccessible. " +
                    "Do not infer policy Not Configured or effective absence."));
                continue;
            }

            foreach (var source in Sources)
            {
                cancellation.ThrowIfCancellationRequested();
                var path = Path.Combine(prefs, source.Folder, source.Filename);
                // Absent optional files are not inferred as Not Configured.
                if (!File.Exists(path))
                    continue;

                try
                {
                    var stat = new FileInfo(path);
                    var cap = GppXmlSourceReader.MaxFileBytes;
                    if (stat.Length > cap)
                    {
                        evidence.Add(new RealSettingsFileEvidence(path, "Invalid", 0, "",
                            $"XML size {stat.Length:N0} exceeds {cap:N0} byte cap."));
                        continue;
                    }

                    var timeBefore = stat.LastWriteTimeUtc;
                    using var input = new FileStream(path, FileMode.Open, FileAccess.Read,
                        FileShare.ReadWrite | FileShare.Delete);
                    if (input.Length > cap)
                        throw new InvalidDataException("GPP XML grew beyond bounded read cap.");
                    using var content = new MemoryStream();
                    var buffer = new byte[32 * 1024];
                    int count;
                    while ((count = input.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        cancellation.ThrowIfCancellationRequested();
                        if (content.Length + count > cap)
                            throw new InvalidDataException("GPP XML grew beyond read cap.");
                        content.Write(buffer, 0, count);
                    }
                    if (input.Length != content.Length)
                        throw new IOException("GPP XML length changed during read.");
                    var bytes = content.ToArray();
                    var hash = Convert.ToHexString(SHA256.HashData(bytes));
                    var result = GppXmlSourceReader.Parse(bytes, gpo.Id,
                        gpo.DisplayName, sourceScope == "Machine" ? "Computer" : "User",
                        path, hash);
                    rows.AddRange(result.Rows);
                    var issues = result.Issues.ToList();
                    if (File.GetLastWriteTimeUtc(path) != timeBefore)
                        issues.Add("XML timestamp changed during capture; data may be stale.");
                    var status = issues.Count == 0 ? "Read" :
                        result.Rows.Count == 0 ? "Invalid" : "Partial";
                    evidence.Add(new RealSettingsFileEvidence(
                        path, status, result.Rows.Count, hash,
                        issues.Count == 0
                            ? $"Bounded GPP XML attribute snapshot; SHA-256 {hash}. " +
                              "Item Level Targeting and CSE execution NOT evaluated."
                            : string.Join(" | ", issues.Take(8))));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                                           InvalidDataException or System.Security.SecurityException)
                {
                    evidence.Add(new RealSettingsFileEvidence(path, "Unreadable", 0, "",
                        ex.GetType().Name + ": " + ex.Message));
                }
            }
        }
    }
}

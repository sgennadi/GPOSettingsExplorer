using System.Text;
using System.Text.Json;

namespace GPOSettingsExplorer.Services;

/// <summary>
/// Read-only point-in-time backup of AD link attributes before changes.
/// Distinct from a GPO-content backup: links live on AD containers, not
/// inside the GPO's registry.pol or SYSVOL files.
/// </summary>
public static class GpoLinkSnapshotService
{
    public static string Save(string targetDn, string rawGpoLinks,
        int? gpoOptions = null)
    {
        if (string.IsNullOrWhiteSpace(targetDn))
            throw new ArgumentException("Target DN is required.", nameof(targetDn));

        var folder = Path.Combine(StoragePaths.GpoBackups, "AD-Link-Snapshots");
        Directory.CreateDirectory(folder);
        var file = Path.Combine(folder,
            "GpoLinks-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") +
            "-" + Guid.NewGuid().ToString("N")[..8] + ".json");
        var snapshot = new
        {
            Format = "GPOSettingsExplorer.GpoLinkSnapshot.v1",
            CapturedUtc = DateTimeOffset.UtcNow,
            DistinguishedName = targetDn,
            GpLinkRaw = rawGpoLinks,
            GpOptions = gpoOptions,
            Note = "AD link-attribute snapshot, not a complete GPO policy backup."
        };
        File.WriteAllText(file,
            JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true }),
            new UTF8Encoding(false));
        return file;
    }
}

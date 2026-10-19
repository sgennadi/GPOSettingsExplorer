using System.Text.Json;

namespace GPOSettingsExplorer.Services;

/// <summary>
/// Per-user opt-in for the cached GPMC XML settings index refresh on startup.
/// Never automatically starts a native MMC editor or scans every GPO's GUI.
/// </summary>
public static class GpoStartupIndexPreferences
{
    private const string FileName = "gpmc-index-startup.json";
    private sealed record Options(int SchemaVersion, bool AutoRefreshGpmcIndex);
    private const int CurrentSchema = 1;

    public static bool Load(string? directory = null)
    {
        try
        {
            var file = Path.Combine(directory ?? StoragePaths.Cache, FileName);
            if (!File.Exists(file))
                return false; // Explicit opt-in: a DC must never be flooded by default.
            var result = JsonSerializer.Deserialize<Options>(
                File.ReadAllText(file));
            return result is { SchemaVersion: CurrentSchema, AutoRefreshGpmcIndex: true };
        }
        catch
        {
            return false;
        }
    }

    public static void Save(bool enabled, string? directory = null)
    {
        var root = directory ?? StoragePaths.Cache;
        Directory.CreateDirectory(root);
        var file = Path.Combine(root, FileName);
        var temp = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temp,
                JsonSerializer.Serialize(new Options(CurrentSchema, enabled)));
            File.Move(temp, file, overwrite: true);
        }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}

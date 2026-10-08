using System.Text.Json;

namespace GPOSettingsExplorer.Services;

public sealed record WorkspaceSortDescription(
    string Property,
    string Direction);

public sealed class WorkspaceState
{
    public int LayoutSchemaVersion { get; set; } =
        WorkspaceStateService.CurrentLayoutSchemaVersion;
    public double WindowLeft { get; set; } = double.NaN;
    public double WindowTop { get; set; } = double.NaN;
    public double WindowWidth { get; set; } = 1400;
    public double WindowHeight { get; set; } = 850;
    public bool WindowMaximized { get; set; }
    public int SelectedTabIndex { get; set; }
    public Guid? SelectedGpoId { get; set; }
    public string GpoSearch { get; set; } = string.Empty;
    public string SettingsSearch { get; set; } = string.Empty;
    public string GlobalSearch { get; set; } = string.Empty;
    public string GpoQuickFilter { get; set; } = "All";
    public List<Guid> FavoriteGpoIds { get; set; } = new();
    public List<Guid> RecentGpoIds { get; set; } = new();
    public Dictionary<string, List<double>> GridColumnWidths { get; set; } =
        new(
            StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, List<WorkspaceSortDescription>> GridSorts { get; set; } =
        new(
            StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, string> TextValues { get; set; } =
        new(
            StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, string> ComboValues { get; set; } =
        new(
            StringComparer.OrdinalIgnoreCase);
}

public sealed class WorkspaceStateService
{
    public const int CurrentLayoutSchemaVersion =
        2;

    private static readonly JsonSerializerOptions JsonOptions =
        new()
        {
            WriteIndented =
                true
        };

    private static string StatePath =>
        Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "GPOSettingsExplorer",
            "workspace.json");

    public WorkspaceState Load()
    {
        try
        {
            if (!File.Exists(
                    StatePath))
            {
                return new WorkspaceState();
            }

            var state =
                JsonSerializer.Deserialize<WorkspaceState>(
                    File.ReadAllText(
                        StatePath),
                    JsonOptions)
                ?? new WorkspaceState();

            if (state.LayoutSchemaVersion <
                CurrentLayoutSchemaVersion)
            {
                // Older releases could persist 20px widths from hidden or
                // unmeasured tab grids. Keep the user's filters/favorites,
                // but discard only the corrupt column-width snapshot.
                state.GridColumnWidths.Clear();

                state.LayoutSchemaVersion =
                    CurrentLayoutSchemaVersion;
            }

            return state;
        }
        catch
        {
            return new WorkspaceState();
        }
    }

    public void Save(
        WorkspaceState state)
    {
        try
        {
            state.LayoutSchemaVersion =
                CurrentLayoutSchemaVersion;
            var directory =
                Path.GetDirectoryName(
                    StatePath)!;

            Directory.CreateDirectory(
                directory);

            var temp =
                StatePath +
                "." +
                Guid.NewGuid().ToString(
                    "N") +
                ".tmp";

            File.WriteAllText(
                temp,
                JsonSerializer.Serialize(
                    state,
                    JsonOptions));

            File.Move(
                temp,
                StatePath,
                overwrite:
                    true);
        }
        catch
        {
        }
    }
}

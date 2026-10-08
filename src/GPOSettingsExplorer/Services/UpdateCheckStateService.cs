using System.Text.Json;

namespace GPOSettingsExplorer.Services;

public sealed record UpdateCheckState(
    DateTime LastAttemptUtc,
    DateTime LastSuccessfulCheckUtc,
    string LastSeenTag);

public sealed class UpdateCheckStateService
{
    public static readonly TimeSpan AutomaticCheckInterval =
        TimeSpan.FromHours(
            24);

    public static readonly TimeSpan FailedCheckRetryInterval =
        TimeSpan.FromHours(
            2);

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
            "update-state.json");

    public UpdateCheckState Load()
    {
        try
        {
            if (!File.Exists(
                    StatePath))
            {
                return Empty();
            }

            var state =
                JsonSerializer.Deserialize<UpdateCheckState>(
                    File.ReadAllText(
                        StatePath),
                    JsonOptions);

            return state
                ?? Empty();
        }
        catch
        {
            return Empty();
        }
    }

    public bool ShouldCheckAutomatically(
        DateTime utcNow)
    {
        return ShouldCheckAutomatically(
            Load(),
            utcNow);
    }

    public static bool ShouldCheckAutomatically(
        UpdateCheckState state,
        DateTime utcNow)
    {
        var now =
            EnsureUtc(
                utcNow);

        var lastSuccess =
            state.LastSuccessfulCheckUtc ==
            DateTime.MinValue
                ? DateTime.MinValue
                : EnsureUtc(
                    state.LastSuccessfulCheckUtc);

        var lastAttempt =
            state.LastAttemptUtc ==
            DateTime.MinValue
                ? DateTime.MinValue
                : EnsureUtc(
                    state.LastAttemptUtc);

        if (lastSuccess !=
                DateTime.MinValue &&
            now -
            lastSuccess <
            AutomaticCheckInterval)
        {
            return false;
        }

        if (lastAttempt !=
                DateTime.MinValue &&
            lastAttempt >
            lastSuccess &&
            now -
            lastAttempt <
            FailedCheckRetryInterval)
        {
            return false;
        }

        return true;
    }

    public void MarkAttempt(
        DateTime utcNow)
    {
        var previous =
            Load();

        Save(
            previous with
            {
                LastAttemptUtc =
                    EnsureUtc(
                        utcNow)
            });
    }

    public void MarkSuccessful(
        DateTime utcNow,
        string lastSeenTag)
    {
        var timestamp =
            EnsureUtc(
                utcNow);

        Save(
            new UpdateCheckState(
                timestamp,
                timestamp,
                lastSeenTag ?? string.Empty));
    }

    private static UpdateCheckState Empty() =>
        new(
            DateTime.MinValue,
            DateTime.MinValue,
            string.Empty);

    private static DateTime EnsureUtc(
        DateTime value) =>
        value.Kind switch
        {
            DateTimeKind.Utc =>
                value,
            DateTimeKind.Local =>
                value.ToUniversalTime(),
            _ =>
                DateTime.SpecifyKind(
                    value,
                    DateTimeKind.Utc)
        };

    private static void Save(
        UpdateCheckState state)
    {
        try
        {
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
            // Update scheduling state must never block the application.
        }
    }
}

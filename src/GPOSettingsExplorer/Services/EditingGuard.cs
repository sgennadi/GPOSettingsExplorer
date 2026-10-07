namespace GPOSettingsExplorer.Services;

public static class EditingGuard
{
    private static int _enabled;

    public static bool IsEnabled =>
        Volatile.Read(
            ref _enabled) != 0;

    public static event EventHandler? Changed;

    public static void SetEnabled(
        bool enabled)
    {
        Interlocked.Exchange(
            ref _enabled,
            enabled ? 1 : 0);

        Changed?.Invoke(
            null,
            EventArgs.Empty);
    }

    public static void EnsureEnabled(
        string operation = "This change")
    {
        if (IsEnabled)
            return;

        throw new InvalidOperationException(
            $"{operation} is blocked because Safe mode is enabled. " +
            "Turn on WRITE ENABLED in the main window before making Group Policy changes.");
    }
}

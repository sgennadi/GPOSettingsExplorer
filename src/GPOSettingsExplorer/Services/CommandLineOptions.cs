namespace GPOSettingsExplorer.Services;

public sealed record CommandLineOptions(
    bool ConnectedSession,
    string DomainName,
    string DomainController,
    bool OfflineAnalysis = false)
{
    public static CommandLineOptions Current { get; private set; } =
        new(
            false,
            string.Empty,
            string.Empty);

    public static void Initialize(
        IEnumerable<string> args)
    {
        var list =
            args.ToArray();

        var connected =
            list.Any(
                value =>
                    value.Equals(
                        "--connected-session",
                        StringComparison.OrdinalIgnoreCase));

        var offline = list.Any(value =>
            value.Equals("--offline", StringComparison.OrdinalIgnoreCase));

        Current =
            new CommandLineOptions(
                connected,
                ReadValue(list, "--domain"),
                ReadValue(list, "--dc"),
                offline);
    }

    private static string ReadValue(
        IReadOnlyList<string> args,
        string name)
    {
        for (var index = 0;
             index < args.Count - 1;
             index++)
        {
            if (args[index].Equals(
                    name,
                    StringComparison.OrdinalIgnoreCase))
            {
                return args[index + 1];
            }
        }

        return string.Empty;
    }
}

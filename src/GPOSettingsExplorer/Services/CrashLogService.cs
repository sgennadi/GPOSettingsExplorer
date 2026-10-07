using System.Text;

namespace GPOSettingsExplorer.Services;

public static class CrashLogService
{
    public static string Write(
        string context,
        Exception exception)
    {
        try
        {
            var directory =
                Path.Combine(
                    Environment.GetFolderPath(
                        Environment.SpecialFolder.LocalApplicationData),
                    "GPOSettingsExplorer",
                    "Logs");

            Directory.CreateDirectory(
                directory);

            var path =
                Path.Combine(
                    directory,
                    $"error-{DateTime.Now:yyyyMMdd-HHmmss-fff}.log");

            var text =
                new StringBuilder()
                    .AppendLine(
                        $"Time: {DateTimeOffset.Now:O}")
                    .AppendLine(
                        $"Context: {context}")
                    .AppendLine(
                        $"Version: {typeof(CrashLogService).Assembly.GetName().Version}")
                    .AppendLine(
                        $"OS: {Environment.OSVersion}")
                    .AppendLine(
                        $"Process architecture: {System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}")
                    .AppendLine()
                    .AppendLine(
                        exception.ToString())
                    .ToString();

            File.WriteAllText(
                path,
                text,
                new UTF8Encoding(
                    encoderShouldEmitUTF8Identifier: false));

            CleanupOldLogs(
                directory);

            return path;
        }
        catch
        {
            return string.Empty;
        }
    }

    public static string Write(
        string context,
        string details)
    {
        return Write(
            context,
            new InvalidOperationException(
                details));
    }

    private static void CleanupOldLogs(
        string directory)
    {
        try
        {
            var keepAfter =
                DateTime.UtcNow.AddDays(
                    -30);

            var files =
                Directory.EnumerateFiles(
                        directory,
                        "error-*.log",
                        SearchOption.TopDirectoryOnly)
                    .Select(
                        path =>
                            new FileInfo(
                                path))
                    .OrderByDescending(
                        file =>
                            file.LastWriteTimeUtc)
                    .ToArray();

            for (var index = 0;
                 index < files.Length;
                 index++)
            {
                if (index < 100 &&
                    files[index].LastWriteTimeUtc >=
                    keepAfter)
                {
                    continue;
                }

                try
                {
                    files[index].Delete();
                }
                catch
                {
                }
            }
        }
        catch
        {
        }
    }
}

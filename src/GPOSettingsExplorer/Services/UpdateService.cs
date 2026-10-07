using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace GPOSettingsExplorer.Services;

public sealed record UpdateAsset(
    string Name,
    string DownloadUrl,
    long Size);

public sealed record UpdateInfo(
    Version CurrentVersion,
    Version LatestVersion,
    string TagName,
    string ReleaseUrl,
    UpdateAsset? Asset)
{
    public bool IsUpdateAvailable =>
        LatestVersion >
        CurrentVersion;
}

public sealed class UpdateService
{
    private static readonly Uri LatestReleaseUri =
        new(
            "https://api.github.com/repos/sgennadi/GPOSettingsExplorer/releases/latest");

    private readonly HttpClient _client;

    public UpdateService()
    {
        _client =
            new HttpClient();

        _client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "GPOSettingsExplorer-update-check");
    }

    public async Task<UpdateInfo> CheckAsync(
        CancellationToken cancellationToken = default)
    {
        using var response =
            await _client.GetAsync(
                LatestReleaseUri,
                cancellationToken);

        response.EnsureSuccessStatusCode();

        await using var stream =
            await response.Content.ReadAsStreamAsync(
                cancellationToken);

        using var document =
            await JsonDocument.ParseAsync(
                stream,
                cancellationToken:
                    cancellationToken);

        var root =
            document.RootElement;

        var tag =
            root.TryGetProperty(
                "tag_name",
                out var tagProperty)
                ? tagProperty.GetString()
                  ?? string.Empty
                : string.Empty;

        var releaseUrl =
            root.TryGetProperty(
                "html_url",
                out var urlProperty)
                ? urlProperty.GetString()
                  ?? string.Empty
                : string.Empty;

        var latest =
            ParseVersion(
                tag);

        var current =
            Assembly.GetExecutingAssembly()
                .GetName()
                .Version
            ?? new Version(
                0,
                0,
                0);

        var expectedAsset =
            RuntimeInformation.ProcessArchitecture ==
            Architecture.Arm64
                ? "GPOSettingsExplorer-win-arm64-portable.zip"
                : "GPOSettingsExplorer-win-x64-portable.zip";

        UpdateAsset? asset =
            null;

        if (root.TryGetProperty(
                "assets",
                out var assets) &&
            assets.ValueKind ==
            JsonValueKind.Array)
        {
            foreach (var item in assets.EnumerateArray())
            {
                var name =
                    item.TryGetProperty(
                        "name",
                        out var nameProperty)
                        ? nameProperty.GetString()
                          ?? string.Empty
                        : string.Empty;

                if (!name.Equals(
                        expectedAsset,
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var download =
                    item.TryGetProperty(
                        "browser_download_url",
                        out var downloadProperty)
                        ? downloadProperty.GetString()
                          ?? string.Empty
                        : string.Empty;

                var size =
                    item.TryGetProperty(
                        "size",
                        out var sizeProperty) &&
                    sizeProperty.TryGetInt64(
                        out var parsedSize)
                        ? parsedSize
                        : 0;

                if (!string.IsNullOrWhiteSpace(
                        download))
                {
                    asset =
                        new UpdateAsset(
                            name,
                            download,
                            size);
                }

                break;
            }
        }

        return new UpdateInfo(
            NormalizeVersion(
                current),
            latest,
            tag,
            releaseUrl,
            asset);
    }

    public async Task<string> DownloadAsync(
        UpdateInfo update,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (update.Asset is null)
        {
            throw new InvalidOperationException(
                "The release does not contain a package for this Windows architecture.");
        }

        var directory =
            Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData),
                "GPOSettingsExplorer",
                "Updates",
                update.TagName);

        Directory.CreateDirectory(
            directory);

        var destination =
            Path.Combine(
                directory,
                update.Asset.Name);

        using var response =
            await _client.GetAsync(
                update.Asset.DownloadUrl,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

        response.EnsureSuccessStatusCode();

        var total =
            response.Content.Headers.ContentLength
            ?? update.Asset.Size;

        await using var source =
            await response.Content.ReadAsStreamAsync(
                cancellationToken);

        await using var target =
            new FileStream(
                destination,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                1024 * 128,
                useAsync:
                    true);

        var buffer =
            new byte[
                1024 * 128];

        long copied =
            0;

        while (true)
        {
            var read =
                await source.ReadAsync(
                    buffer,
                    cancellationToken);

            if (read <= 0)
                break;

            await target.WriteAsync(
                buffer.AsMemory(
                    0,
                    read),
                cancellationToken);

            copied +=
                read;

            if (total > 0)
            {
                progress?.Report(
                    copied /
                    (double)total);
            }
        }

        return destination;
    }

    public static void StageInstallerAndRestart(
        string packagePath)
    {
        if (!File.Exists(
                packagePath))
        {
            throw new FileNotFoundException(
                "The downloaded update package was not found.",
                packagePath);
        }

        var executable =
            Environment.ProcessPath
            ?? throw new InvalidOperationException(
                "The current executable path is unavailable.");

        var updater =
            Path.Combine(
                Path.GetTempPath(),
                $"GPOSettingsExplorer-Updater-{Guid.NewGuid():N}.exe");

        File.Copy(
            executable,
            updater,
            overwrite:
                true);

        var targetDirectory =
            AppContext.BaseDirectory.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);

        var targetExecutable =
            Path.GetFileName(
                executable);

        var arguments =
            string.Join(
                " ",
                "--apply-update",
                Quote(
                    packagePath),
                Quote(
                    targetDirectory),
                Quote(
                    targetExecutable),
                Environment.ProcessId.ToString(
                    System.Globalization.CultureInfo.InvariantCulture));

        Process.Start(
            new ProcessStartInfo
            {
                FileName =
                    updater,
                Arguments =
                    arguments,
                WorkingDirectory =
                    Path.GetTempPath(),
                UseShellExecute =
                    true
            });
    }

    private static string Quote(
        string value) =>
        """ +
        value.Replace(
            """,
            "\"",
            StringComparison.Ordinal) +
        """;

    private static Version ParseVersion(
        string value)
    {
        var text =
            value.Trim();

        if (text.StartsWith(
                'v') ||
            text.StartsWith(
                'V'))
        {
            text =
                text[1..];
        }

        return Version.TryParse(
                   text,
                   out var version)
            ? NormalizeVersion(
                version)
            : new Version(
                0,
                0,
                0);
    }

    private static Version NormalizeVersion(
        Version version) =>
        new(
            Math.Max(
                0,
                version.Major),
            Math.Max(
                0,
                version.Minor),
            Math.Max(
                0,
                version.Build));
}

public static class UpdateInstaller
{
    public static bool TryApply(
        IReadOnlyList<string> args)
    {
        var index =
            args
                .Select(
                    (value, position) =>
                        new
                        {
                            value,
                            position
                        })
                .FirstOrDefault(
                    item =>
                        item.value.Equals(
                            "--apply-update",
                            StringComparison.OrdinalIgnoreCase))
                ?.position
            ?? -1;

        if (index < 0 ||
            index + 4 >=
            args.Count)
        {
            return false;
        }

        var package =
            args[index + 1];

        var targetDirectory =
            args[index + 2];

        var executableName =
            args[index + 3];

        _ =
            int.TryParse(
                args[index + 4],
                out var parentProcessId);

        try
        {
            if (parentProcessId > 0)
            {
                try
                {
                    using var process =
                        Process.GetProcessById(
                            parentProcessId);

                    process.WaitForExit(
                        60000);
                }
                catch
                {
                }
            }

            Directory.CreateDirectory(
                targetDirectory);

            ZipFile.ExtractToDirectory(
                package,
                targetDirectory,
                overwriteFiles:
                    true);

            var executable =
                Path.Combine(
                    targetDirectory,
                    executableName);

            if (!File.Exists(
                    executable))
            {
                throw new FileNotFoundException(
                    "The updated executable was not found after extraction.",
                    executable);
            }

            Process.Start(
                new ProcessStartInfo
                {
                    FileName =
                        executable,
                    WorkingDirectory =
                        targetDirectory,
                    UseShellExecute =
                        true
                });
        }
        catch (Exception ex)
        {
            CrashLogService.Write(
                "Apply update",
                ex);

            try
            {
                System.Windows.MessageBox.Show(
                    ex.Message +
                    "\n\nThe update package was left at:\n" +
                    package,
                    "GPO Settings Explorer Update",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
            }
            catch
            {
            }
        }

        return true;
    }
}

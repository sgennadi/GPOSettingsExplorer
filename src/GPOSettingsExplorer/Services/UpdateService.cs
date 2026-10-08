using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace GPOSettingsExplorer.Services;

public sealed record UpdateAsset(
    string Name,
    string DownloadUrl,
    long Size,
    string Digest);

public sealed record UpdateInfo(
    Version CurrentVersion,
    Version LatestVersion,
    string TagName,
    string ReleaseUrl,
    string ReleaseNotes,
    DateTimeOffset? PublishedAt,
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
            new HttpClient
            {
                Timeout =
                    TimeSpan.FromSeconds(
                        20)
            };

        _client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "GPOSettingsExplorer-update-check");

        _client.DefaultRequestHeaders.Accept.ParseAdd(
            "application/vnd.github+json");
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
            ReadString(
                root,
                "tag_name");

        var releaseUrl =
            ReadString(
                root,
                "html_url");

        var releaseNotes =
            ReadString(
                root,
                "body");

        DateTimeOffset? publishedAt =
            null;

        var publishedText =
            ReadString(
                root,
                "published_at");

        if (DateTimeOffset.TryParse(
                publishedText,
                out var parsedPublished))
        {
            publishedAt =
                parsedPublished;
        }

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
                    ReadString(
                        item,
                        "name");

                if (!name.Equals(
                        expectedAsset,
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var download =
                    ReadString(
                        item,
                        "browser_download_url");

                var size =
                    item.TryGetProperty(
                        "size",
                        out var sizeProperty) &&
                    sizeProperty.TryGetInt64(
                        out var parsedSize)
                        ? parsedSize
                        : 0;

                var digest =
                    ReadString(
                        item,
                        "digest");

                if (!string.IsNullOrWhiteSpace(
                        download))
                {
                    asset =
                        new UpdateAsset(
                            name,
                            download,
                            size,
                            digest);
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
            releaseNotes,
            publishedAt,
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

        using var hash =
            IncrementalHash.CreateHash(
                HashAlgorithmName.SHA256);

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

            hash.AppendData(
                buffer,
                0,
                read);

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

        await target.FlushAsync(
            cancellationToken);

        var computedDigest =
            "sha256:" +
            Convert.ToHexString(
                    hash.GetHashAndReset())
                .ToLowerInvariant();

        if (!string.IsNullOrWhiteSpace(
                update.Asset.Digest) &&
            !computedDigest.Equals(
                update.Asset.Digest.Trim(),
                StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                target.Close();
                File.Delete(
                    destination);
            }
            catch
            {
            }

            throw new InvalidOperationException(
                $"The downloaded update failed SHA-256 verification. Expected {update.Asset.Digest}; received {computedDigest}.");
        }

        ValidatePackage(
            destination,
            update.LatestVersion);

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

    public static string? FindLatestRollbackDirectory()
    {
        var root =
            RollbackRoot;

        if (!Directory.Exists(
                root))
        {
            return null;
        }

        return Directory.EnumerateDirectories(
                    root,
                    "rollback-*",
                    SearchOption.TopDirectoryOnly)
            .OrderByDescending(
                Directory.GetCreationTimeUtc)
            .FirstOrDefault();
    }

    public static void StageRollbackAndRestart(
        string rollbackDirectory)
    {
        if (string.IsNullOrWhiteSpace(
                rollbackDirectory) ||
            !Directory.Exists(
                rollbackDirectory))
        {
            throw new DirectoryNotFoundException(
                "The rollback directory was not found.");
        }

        var executable =
            Environment.ProcessPath
            ?? throw new InvalidOperationException(
                "The current executable path is unavailable.");

        var updater =
            Path.Combine(
                Path.GetTempPath(),
                $"GPOSettingsExplorer-Rollback-{Guid.NewGuid():N}.exe");

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
                "--rollback-update",
                Quote(
                    rollbackDirectory),
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

    private static void ValidatePackage(
        string packagePath,
        Version expectedVersion)
    {
        using var archive =
            ZipFile.OpenRead(
                packagePath);

        var executable =
            archive.Entries.FirstOrDefault(
                entry =>
                    Path.GetFileName(
                            entry.FullName)
                        .Equals(
                            "GPOSettingsExplorer.exe",
                            StringComparison.OrdinalIgnoreCase));

        if (executable is null ||
            executable.Length <= 0)
        {
            throw new InvalidOperationException(
                "The update archive does not contain a valid GPOSettingsExplorer.exe.");
        }

        var suspicious =
            archive.Entries.FirstOrDefault(
                entry =>
                    IsUnsafeArchivePath(
                        entry.FullName));

        if (suspicious is not null)
        {
            throw new InvalidOperationException(
                $"The update archive contains an unsafe path: {suspicious.FullName}");
        }

        if (expectedVersion.Major <= 0)
        {
            throw new InvalidOperationException(
                "The release version is invalid.");
        }
    }

    private static bool IsUnsafeArchivePath(
        string path)
    {
        var normalized =
            path.Replace(
                '\\',
                '/');

        return normalized.StartsWith(
                   "/",
                   StringComparison.Ordinal) ||
               normalized.Contains(
                   "../",
                   StringComparison.Ordinal) ||
               normalized.Contains(
                   ":",
                   StringComparison.Ordinal);
    }

    private static string ReadString(
        JsonElement element,
        string name) =>
        element.TryGetProperty(
            name,
            out var property) &&
        property.ValueKind ==
        JsonValueKind.String
            ? property.GetString()
              ?? string.Empty
            : string.Empty;

    private static string Quote(
        string value) =>
        "\"" +
        value.Replace(
            "\"",
            "\\\"",
            StringComparison.Ordinal) +
        "\"";

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

    private static string RollbackRoot =>
        Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "GPOSettingsExplorer",
            "Updates",
            "Rollback");
}

public static class UpdateInstaller
{
    private sealed record RollbackManifest(
        string TargetDirectory,
        string TargetExecutable,
        List<string> BackedUpFiles,
        List<string> NewFiles);

    public static bool TryApply(
        IReadOnlyList<string> args)
    {
        var applyIndex =
            FindArgument(
                args,
                "--apply-update");

        if (applyIndex >= 0)
        {
            return ApplyUpdate(
                args,
                applyIndex);
        }

        var rollbackIndex =
            FindArgument(
                args,
                "--rollback-update");

        if (rollbackIndex >= 0)
        {
            return ApplyRollback(
                args,
                rollbackIndex);
        }

        return false;
    }

    private static bool ApplyUpdate(
        IReadOnlyList<string> args,
        int index)
    {
        if (index + 4 >=
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

        var staging =
            Path.Combine(
                Path.GetTempPath(),
                "GPOSettingsExplorer-Stage-" +
                Guid.NewGuid().ToString(
                    "N"));

        var rollback =
            Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData),
                "GPOSettingsExplorer",
                "Updates",
                "Rollback",
                $"rollback-{DateTime.Now:yyyyMMdd-HHmmss}");

        try
        {
            WaitForParent(
                parentProcessId);

            Directory.CreateDirectory(
                staging);

            ZipFile.ExtractToDirectory(
                package,
                staging,
                overwriteFiles:
                    true);

            var stagedExecutable =
                Path.Combine(
                    staging,
                    executableName);

            if (!File.Exists(
                    stagedExecutable))
            {
                throw new FileNotFoundException(
                    "The staged update does not contain the application executable.",
                    stagedExecutable);
            }

            Directory.CreateDirectory(
                rollback);

            var backedUp =
                new List<string>();

            var newFiles =
                new List<string>();

            foreach (var source in Directory.EnumerateFiles(
                         staging,
                         "*",
                         SearchOption.AllDirectories))
            {
                var relative =
                    Path.GetRelativePath(
                        staging,
                        source);

                if (relative.StartsWith(
                        "Data" +
                        Path.DirectorySeparatorChar,
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var target =
                    Path.Combine(
                        targetDirectory,
                        relative);

                if (File.Exists(
                        target))
                {
                    var backup =
                        Path.Combine(
                            rollback,
                            "Files",
                            relative);

                    Directory.CreateDirectory(
                        Path.GetDirectoryName(
                            backup)!);

                    File.Copy(
                        target,
                        backup,
                        overwrite:
                            true);

                    backedUp.Add(
                        relative);
                }
                else
                {
                    newFiles.Add(
                        relative);
                }
            }

            var manifest =
                new RollbackManifest(
                    targetDirectory,
                    executableName,
                    backedUp,
                    newFiles);

            File.WriteAllText(
                Path.Combine(
                    rollback,
                    "rollback.json"),
                JsonSerializer.Serialize(
                    manifest,
                    new JsonSerializerOptions
                    {
                        WriteIndented =
                            true
                    }));

            try
            {
                CopyTree(
                    staging,
                    targetDirectory);
            }
            catch
            {
                RestoreRollback(
                    rollback,
                    manifest);

                throw;
            }

            CleanupOldRollbacks(
                Path.GetDirectoryName(
                    rollback)!,
                keep:
                    3);

            var executable =
                Path.Combine(
                    targetDirectory,
                    executableName);

            Process.Start(
                new ProcessStartInfo
                {
                    FileName =
                        executable,
                    WorkingDirectory =
                        targetDirectory,
                    Arguments =
                        "--updated",
                    UseShellExecute =
                        true
                });
        }
        catch (Exception ex)
        {
            CrashLogService.Write(
                "Apply update with rollback",
                ex);

            try
            {
                System.Windows.MessageBox.Show(
                    ex.Message +
                    "\n\nThe previous application files were restored when possible.\nUpdate package:\n" +
                    package,
                    "GPO Settings Explorer Update",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
            }
            catch
            {
            }
        }
        finally
        {
            TryDeleteDirectory(
                staging);
        }

        return true;
    }

    private static bool ApplyRollback(
        IReadOnlyList<string> args,
        int index)
    {
        if (index + 4 >=
            args.Count)
        {
            return false;
        }

        var rollbackDirectory =
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
            WaitForParent(
                parentProcessId);

            var manifestPath =
                Path.Combine(
                    rollbackDirectory,
                    "rollback.json");

            var manifest =
                JsonSerializer.Deserialize<RollbackManifest>(
                    File.ReadAllText(
                        manifestPath))
                ?? throw new InvalidOperationException(
                    "The rollback manifest is invalid.");

            RestoreRollback(
                rollbackDirectory,
                manifest);

            var executable =
                Path.Combine(
                    targetDirectory,
                    executableName);

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
                "Rollback update",
                ex);

            try
            {
                System.Windows.MessageBox.Show(
                    ex.Message,
                    "GPO Settings Explorer Rollback",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
            }
            catch
            {
            }
        }

        return true;
    }

    private static void RestoreRollback(
        string rollbackDirectory,
        RollbackManifest manifest)
    {
        foreach (var relative in manifest.NewFiles)
        {
            try
            {
                File.Delete(
                    Path.Combine(
                        manifest.TargetDirectory,
                        relative));
            }
            catch
            {
            }
        }

        foreach (var relative in manifest.BackedUpFiles)
        {
            var source =
                Path.Combine(
                    rollbackDirectory,
                    "Files",
                    relative);

            var target =
                Path.Combine(
                    manifest.TargetDirectory,
                    relative);

            Directory.CreateDirectory(
                Path.GetDirectoryName(
                    target)!);

            File.Copy(
                source,
                target,
                overwrite:
                    true);
        }
    }

    private static void CopyTree(
        string sourceDirectory,
        string targetDirectory)
    {
        foreach (var source in Directory.EnumerateFiles(
                     sourceDirectory,
                     "*",
                     SearchOption.AllDirectories))
        {
            var relative =
                Path.GetRelativePath(
                    sourceDirectory,
                    source);

            if (relative.StartsWith(
                    "Data" +
                    Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var target =
                Path.Combine(
                    targetDirectory,
                    relative);

            Directory.CreateDirectory(
                Path.GetDirectoryName(
                    target)!);

            File.Copy(
                source,
                target,
                overwrite:
                    true);
        }
    }

    private static int FindArgument(
        IReadOnlyList<string> args,
        string name) =>
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
                        name,
                        StringComparison.OrdinalIgnoreCase))
            ?.position
        ?? -1;

    private static void WaitForParent(
        int processId)
    {
        if (processId <= 0)
        {
            return;
        }

        try
        {
            using var process =
                Process.GetProcessById(
                    processId);

            process.WaitForExit(
                60000);
        }
        catch
        {
        }
    }

    private static void CleanupOldRollbacks(
        string root,
        int keep)
    {
        try
        {
            foreach (var directory in Directory.EnumerateDirectories(
                         root,
                         "rollback-*",
                         SearchOption.TopDirectoryOnly)
                     .OrderByDescending(
                         Directory.GetCreationTimeUtc)
                     .Skip(
                         keep))
            {
                TryDeleteDirectory(
                    directory);
            }
        }
        catch
        {
        }
    }

    private static void TryDeleteDirectory(
        string path)
    {
        try
        {
            if (Directory.Exists(
                    path))
            {
                Directory.Delete(
                    path,
                    recursive:
                        true);
            }
        }
        catch
        {
        }
    }
}

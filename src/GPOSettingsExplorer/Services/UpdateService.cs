using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Sockets;
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
            "https://api.github.com/repos/sgennadi/GPOSettingsExplorer/releases?per_page=30");

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

    public static bool IsExpectedConnectivityFailure(
        Exception exception)
    {
        for (Exception? current = exception;
             current is not null;
             current = current.InnerException)
        {
            if (current is SocketException socket)
            {
                return socket.SocketErrorCode is
                    SocketError.AccessDenied or
                    SocketError.TimedOut or
                    SocketError.ConnectionRefused or
                    SocketError.HostNotFound or
                    SocketError.TryAgain or
                    SocketError.NetworkUnreachable or
                    SocketError.HostUnreachable or
                    SocketError.NetworkDown or
                    SocketError.ConnectionReset;
            }

            if (current is HttpRequestException)
            {
                return true;
            }
        }

        return false;
    }

    public static string BuildConnectivityFailureMessage(
        Exception exception)
    {
        var socket =
            EnumerateExceptionChain(
                    exception)
                .OfType<SocketException>()
                .FirstOrDefault();

        if (socket?.SocketErrorCode ==
            SocketError.AccessDenied)
        {
            return
                "Windows blocked the HTTPS connection to GitHub (socket error 10013). " +
                "GPO Settings Explorer will continue normally. Allow outbound HTTPS to api.github.com and github.com, " +
                "or update the portable build manually from GitHub Releases.";
        }

        return
            "GitHub could not be reached from this computer. GPO Settings Explorer will continue normally. " +
            "Check Internet/proxy/firewall access to api.github.com and github.com, or update the portable build manually.";
    }

    private static IEnumerable<Exception> EnumerateExceptionChain(
        Exception exception)
    {
        for (Exception? current = exception;
             current is not null;
             current = current.InnerException)
        {
            yield return current;
        }
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

        var releases = document.RootElement;
        if (releases.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("Invalid GitHub releases response.");
        var currentTag = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion?.Split('+')[0] ?? string.Empty;
        var previewChannel = currentTag.Contains("-preview.", StringComparison.OrdinalIgnoreCase);
        JsonElement root = default;
        Version best = new(0, 0, 0, 0);
        foreach (var candidate in releases.EnumerateArray())
        {
            if (candidate.TryGetProperty("draft", out var draft) &&
                draft.ValueKind == JsonValueKind.True)
                continue;
            var candidateTag = ReadString(candidate, "tag_name");
            if (candidateTag.Contains("-preview.", StringComparison.OrdinalIgnoreCase) != previewChannel)
                continue;
            var version = ParseVersion(candidateTag);
            if (version <= best)
                continue;
            best = version;
            root = candidate;
        }
        if (root.ValueKind == JsonValueKind.Undefined)
            throw new InvalidOperationException("No release exists for the selected update channel.");

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

        var current = ParseVersion(currentTag);
        if (current.Major == 0)
            current = NormalizeVersion(Assembly.GetExecutingAssembly()
                .GetName().Version ?? new Version(0, 0, 0));

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

        if (!Uri.TryCreate(update.Asset.DownloadUrl, UriKind.Absolute, out var assetUri) ||
            assetUri.Scheme != Uri.UriSchemeHttps ||
            !assetUri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Update asset must be downloaded from GitHub over HTTPS.");

        if (!update.Asset.Digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) ||
            update.Asset.Digest.Length != 71 ||
            !update.Asset.Digest[7..].All(Uri.IsHexDigit))
            throw new InvalidOperationException("GitHub release asset is missing a valid SHA-256 digest. Automatic installation is blocked.");

        using var response =
            await _client.GetAsync(
                assetUri,
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

        if (!computedDigest.Equals(
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

        return string.IsNullOrWhiteSpace(normalized) ||
               normalized.StartsWith("/", StringComparison.Ordinal) ||
               normalized.StartsWith("../", StringComparison.Ordinal) ||
               normalized.Contains("/../", StringComparison.Ordinal) ||
               normalized.EndsWith("/..", StringComparison.Ordinal) ||
               normalized.StartsWith("./", StringComparison.Ordinal) ||
               normalized.Contains(":", StringComparison.Ordinal);
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

    private static Version ParseVersion(string value)
    {
        var text = value.Trim().TrimStart('v', 'V');
        var previewIndex = text.IndexOf("-preview.", StringComparison.OrdinalIgnoreCase);
        var revision = 0;
        if (previewIndex >= 0)
        {
            if (!int.TryParse(text[(previewIndex + "-preview.".Length)..], out revision) || revision < 0)
                return new Version(0, 0, 0, 0);
            text = text[..previewIndex];
        }
        else if (text.Contains('-'))
            return new Version(0, 0, 0, 0);
        if (!Version.TryParse(text, out var version))
            return new Version(0, 0, 0, 0);
        return new Version(Math.Max(0, version.Major), Math.Max(0, version.Minor),
            Math.Max(0, version.Build), previewIndex >= 0 ? revision : Math.Max(0, version.Revision));
    }

    private static Version NormalizeVersion(Version version) =>
        new(Math.Max(0, version.Major), Math.Max(0, version.Minor),
            Math.Max(0, version.Build), Math.Max(0, version.Revision));

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

            var healthMarker = Path.Combine(
                Path.GetTempPath(),
                "GPOSettingsExplorer-Health-" + Guid.NewGuid().ToString("N") + ".ok");
            using var launched = Process.Start(
                new ProcessStartInfo
                {
                    FileName = executable,
                    WorkingDirectory = targetDirectory,
                    Arguments = "--updated --update-health-marker \"" + healthMarker.Replace("\"", "\\\"") + "\"",
                    UseShellExecute = true
                }) ?? throw new InvalidOperationException("Updated application did not start.");

            // The detached updater remains alive until the new GUI reports startup.
            var healthy = false;
            for (var attempt = 0; attempt < 60; attempt++)
            {
                if (File.Exists(healthMarker))
                {
                    healthy = !launched.HasExited;
                    break;
                }
                if (launched.HasExited)
                    break;
                System.Threading.Thread.Sleep(500);
            }
            try { File.Delete(healthMarker); } catch { }
            if (!healthy)
            {
                if (!launched.HasExited)
                {
                    launched.Kill(entireProcessTree: true);
                    launched.WaitForExit(5000);
                }
                RestoreRollback(rollback, manifest);
                throw new InvalidOperationException(
                    "The updated application did not confirm startup within 30 seconds. Previous files were restored.");
            }
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

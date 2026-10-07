using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using GPOSettingsExplorer.Models;

namespace GPOSettingsExplorer.Services;

public sealed record DiagnosticItem(
    string Name,
    string Value,
    bool? Success = null,
    string Details = "");

public sealed class DiagnosticsService
{
    public IReadOnlyList<DiagnosticItem> Collect(
        GpmService gpmService)
    {
        var result =
            new List<DiagnosticItem>();

        var assembly =
            Assembly.GetExecutingAssembly();

        result.Add(
            new DiagnosticItem(
                "Application version",
                assembly.GetName().Version?.ToString()
                ?? "unknown",
                true));

        result.Add(
            new DiagnosticItem(
                "Process architecture",
                RuntimeInformation.ProcessArchitecture.ToString(),
                true));

        result.Add(
            new DiagnosticItem(
                "Operating system",
                RuntimeInformation.OSDescription,
                true));

        result.Add(
            new DiagnosticItem(
                "Windows identity",
                $"{Environment.UserDomainName}\\{Environment.UserName}",
                true));

        result.Add(
            new DiagnosticItem(
                "Safe mode",
                EditingGuard.IsEnabled
                    ? "WRITE ENABLED"
                    : "READ ONLY",
                true));

        var context =
            DomainConnectionState.Context;

        result.Add(
            new DiagnosticItem(
                "Domain",
                context?.DomainName
                ?? "<not connected>",
                context is not null));

        result.Add(
            new DiagnosticItem(
                "Connected DC",
                context?.ConnectedServer
                ?? "<not connected>",
                context is not null));

        result.Add(
            new DiagnosticItem(
                "GPMC / RSAT",
                gpmService.IsAvailable
                    ? "Available"
                    : "Not installed",
                gpmService.IsAvailable));

        if (context is not null)
        {
            var sysvol =
                DomainConnectionState.BuildSysvolRoot(
                    context.DomainName);

            result.Add(
                CheckDirectory(
                    "SYSVOL",
                    sysvol));

            result.Add(
                CheckDirectory(
                    "Policies",
                    Path.Combine(
                        sysvol,
                        "Policies")));

            result.Add(
                CheckDirectory(
                    "Central Store",
                    Path.Combine(
                        sysvol,
                        "Policies",
                        "PolicyDefinitions"),
                    optional:
                        true));
        }

        result.Add(
            CheckDirectory(
                "Cache directory",
                StoragePaths.Cache,
                optional:
                    true));

        result.Add(
            CheckDirectory(
                "Backup directory",
                StoragePaths.GpoBackups,
                optional:
                    true));

        result.Add(
            CheckDirectory(
                "Audit directory",
                StoragePaths.Audit,
                optional:
                    true));

        result.Add(
            CheckDirectory(
                "Crash log directory",
                CrashLogDirectory,
                optional:
                    true));

        return result;
    }

    public DiagnosticItem TestGpoWriteAccess(
        GpoInfo gpo)
    {
        try
        {
            var path =
                Path.Combine(
                    DomainConnectionState.BuildSysvolRoot(
                        gpo.DomainName),
                    "Policies",
                    gpo.Id.ToString("B"),
                    "GPT.INI");

            if (!File.Exists(
                    path))
            {
                return new DiagnosticItem(
                    "Selected GPO write access",
                    "GPT.INI was not found",
                    false,
                    path);
            }

            using var stream =
                new FileStream(
                    path,
                    FileMode.Open,
                    FileAccess.ReadWrite,
                    FileShare.ReadWrite |
                    FileShare.Delete);

            return new DiagnosticItem(
                "Selected GPO write access",
                "Write access available",
                true,
                path);
        }
        catch (Exception ex)
        {
            return new DiagnosticItem(
                "Selected GPO write access",
                "Write access unavailable",
                false,
                ex.Message);
        }
    }

    public string CreateSupportPackage(
        IReadOnlyList<DiagnosticItem> diagnostics)
    {
        var directory =
            Path.Combine(
                StoragePaths.Exports,
                "Support");

        Directory.CreateDirectory(
            directory);

        var zipPath =
            Path.Combine(
                directory,
                $"GPOSettingsExplorer-support-{DateTime.Now:yyyyMMdd-HHmmss}.zip");

        using var archive =
            ZipFile.Open(
                zipPath,
                ZipArchiveMode.Create);

        var diagnosticsEntry =
            archive.CreateEntry(
                "diagnostics.txt",
                CompressionLevel.Optimal);

        using (var writer =
               new StreamWriter(
                   diagnosticsEntry.Open(),
                   new UTF8Encoding(
                       encoderShouldEmitUTF8Identifier: false)))
        {
            writer.WriteLine(
                $"Generated: {DateTimeOffset.Now:O}");

            foreach (var item in diagnostics)
            {
                writer.WriteLine(
                    $"{item.Name}: {item.Value}");

                if (!string.IsNullOrWhiteSpace(
                        item.Details))
                {
                    writer.WriteLine(
                        $"  {item.Details}");
                }
            }
        }

        if (Directory.Exists(
                CrashLogDirectory))
        {
            foreach (var file in Directory.EnumerateFiles(
                         CrashLogDirectory,
                         "*.log",
                         SearchOption.TopDirectoryOnly)
                     .OrderByDescending(
                         File.GetLastWriteTimeUtc)
                     .Take(20))
            {
                try
                {
                    archive.CreateEntryFromFile(
                        file,
                        Path.Combine(
                            "Logs",
                            Path.GetFileName(
                                file)),
                        CompressionLevel.Optimal);
                }
                catch
                {
                }
            }
        }

        return zipPath;
    }

    private static string CrashLogDirectory =>
        Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "GPOSettingsExplorer",
            "Logs");

    private static DiagnosticItem CheckDirectory(
        string name,
        string path,
        bool optional = false)
    {
        try
        {
            var exists =
                Directory.Exists(
                    path);

            return new DiagnosticItem(
                name,
                exists
                    ? path
                    : optional
                        ? "Not present"
                        : "Unavailable",
                optional
                    ? true
                    : exists,
                exists
                    ? string.Empty
                    : path);
        }
        catch (Exception ex)
        {
            return new DiagnosticItem(
                name,
                "Error",
                optional
                    ? true
                    : false,
                ex.Message);
        }
    }
}

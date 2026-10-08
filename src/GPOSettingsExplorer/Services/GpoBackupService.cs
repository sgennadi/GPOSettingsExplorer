using System.Globalization;
using System.Xml.Linq;
using GPOSettingsExplorer.Models;

namespace GPOSettingsExplorer.Services;

public sealed class GpoBackupService
{
    public IReadOnlyList<GpoBackupInfo> LoadBackups(string rootDirectory)
    {
        if (string.IsNullOrWhiteSpace(rootDirectory))
            throw new ArgumentException("Backup directory cannot be empty.", nameof(rootDirectory));

        var fullPath = Path.GetFullPath(
            Environment.ExpandEnvironmentVariables(rootDirectory.Trim()));

        if (!Directory.Exists(fullPath))
            return Array.Empty<GpoBackupInfo>();

        var result = new List<GpoBackupInfo>();

        foreach (var manifestPath in Directory.EnumerateFiles(
                     fullPath,
                     "bkupInfo.xml",
                     SearchOption.AllDirectories))
        {
            try
            {
                var parsed = ParseManifest(manifestPath);
                if (parsed is not null)
                    result.Add(parsed);
            }
            catch
            {
                // One damaged backup must not prevent browsing all other backups.
            }
        }

        return result
            .OrderByDescending(item => item.Timestamp ?? DateTime.MinValue)
            .ThenBy(item => item.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    public void Restore(string currentDomainName, GpoBackupInfo backup)
    {
        EditingGuard.EnsureEnabled(
            "Restore GPO backup");
        if (!string.Equals(
                currentDomainName,
                backup.DomainName,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"This backup belongs to '{backup.DomainName}', but the current domain is '{currentDomainName}'. " +
                "A GPO backup can only be restored to its original domain.");
        }

        if (!Directory.Exists(backup.BackupDirectory))
            throw new DirectoryNotFoundException(
                $"Backup directory does not exist: {backup.BackupDirectory}");

        dynamic gpm = CreateGpm();
        dynamic constants = gpm.GetConstants();
        var server =
            DomainConnectionState.GetServerFor(
                currentDomainName);

        dynamic domain =
            string.IsNullOrWhiteSpace(
                server)
                ? gpm.GetDomain(
                    currentDomainName,
                    string.Empty,
                    constants.UseAnyDC)
                : gpm.GetDomain(
                    currentDomainName,
                    server,
                    0);

        dynamic backupDirectory = gpm.GetBackupDir(backup.BackupDirectory);
        dynamic backupObject = backupDirectory.GetBackup(backup.BackupId.ToString("B"));

        ChangePreviewGuard.Confirm(
            new ChangePreviewRequest(
                "Restore GPO backup",
                string.IsNullOrWhiteSpace(backup.DisplayName)
                    ? backup.GpoId.ToString("B")
                    : backup.DisplayName,
                "Current GPO state",
                $"Backup from {backup.Timestamp:yyyy-MM-dd HH:mm:ss}",
                $"Backup ID: {backup.BackupId:B}\nDirectory: {backup.BackupDirectory}",
                "Restore"));

        dynamic result = domain.RestoreGPO(backupObject, null, null);

        var status = ReadOverallStatus(result);
        if (status < 0)
            System.Runtime.InteropServices.Marshal.ThrowExceptionForHR(status);

        if (status != 0)
            throw new InvalidOperationException(
                $"GPMC RestoreGPO returned status 0x{status:X8}.");
    }

    public void DeleteBackup(GpoBackupInfo backup)
    {
        EditingGuard.EnsureEnabled(
            "Delete GPO backup");
        if (!Directory.Exists(backup.BackupDirectory))
            return;

        dynamic gpm = CreateGpm();
        dynamic backupDirectory = gpm.GetBackupDir(backup.BackupDirectory);
        dynamic backupObject = backupDirectory.GetBackup(backup.BackupId.ToString("B"));

        ChangePreviewGuard.Confirm(
            new ChangePreviewRequest(
                "Delete GPO backup",
                backup.DisplayName,
                $"{backup.BackupId:B} | {backup.Timestamp:yyyy-MM-dd HH:mm:ss}",
                "<deleted>",
                backup.BackupDirectory,
                "Delete"));

        backupObject.Delete();

        TryDeleteEmptyDirectory(backup.BackupDirectory);
    }

    public string GenerateXmlReport(
        GpoBackupInfo backup)
    {
        if (!Directory.Exists(
                backup.BackupDirectory))
        {
            throw new DirectoryNotFoundException(
                $"Backup directory does not exist: {backup.BackupDirectory}");
        }

        dynamic gpm =
            CreateGpm();

        dynamic constants =
            gpm.GetConstants();

        dynamic backupDirectory =
            gpm.GetBackupDir(
                backup.BackupDirectory);

        dynamic backupObject =
            backupDirectory.GetBackup(
                backup.BackupId.ToString(
                    "B"));

        var temp =
            Path.Combine(
                Path.GetTempPath(),
                $"GPOSettingsExplorer-backup-report-{Guid.NewGuid():N}.xml");

        try
        {
            backupObject.GenerateReportToFile(
                constants.ReportXML,
                temp);

            return File.ReadAllText(
                temp);
        }
        finally
        {
            try
            {
                File.Delete(
                    temp);
            }
            catch
            {
            }
        }
    }

    public string GenerateReport(GpoBackupInfo backup)
    {
        if (!Directory.Exists(backup.BackupDirectory))
            throw new DirectoryNotFoundException(
                $"Backup directory does not exist: {backup.BackupDirectory}");

        dynamic gpm = CreateGpm();
        dynamic constants = gpm.GetConstants();
        dynamic backupDirectory = gpm.GetBackupDir(backup.BackupDirectory);
        dynamic backupObject = backupDirectory.GetBackup(backup.BackupId.ToString("B"));

        var reportsDirectory = Path.Combine(StoragePaths.Exports, "BackupReports");
        Directory.CreateDirectory(reportsDirectory);

        var safeName = SanitizeFileName(
            string.IsNullOrWhiteSpace(backup.DisplayName)
                ? backup.GpoId.ToString("B")
                : backup.DisplayName);

        var reportPath = Path.Combine(
            reportsDirectory,
            $"{safeName}_{DateTime.Now:yyyyMMdd-HHmmss}.html");

        backupObject.GenerateReportToFile(constants.ReportHTML, reportPath);
        return reportPath;
    }

    private static GpoBackupInfo? ParseManifest(string manifestPath)
    {
        var document = XDocument.Load(manifestPath, LoadOptions.None);
        var root = document.Root;
        if (root is null)
            return null;

        var backupIdText = Value(root, "ID");
        var gpoIdText = FirstValue(root, "GPOGuid", "GPOID", "GPOId");

        if (!Guid.TryParse(backupIdText, out var backupId) ||
            !Guid.TryParse(gpoIdText, out var gpoId))
        {
            return null;
        }

        var backupInstanceDirectory = Path.GetDirectoryName(manifestPath)
            ?? throw new InvalidOperationException("Invalid backup manifest path.");

        var backupRoot = Directory.GetParent(backupInstanceDirectory)?.FullName
            ?? backupInstanceDirectory;

        return new GpoBackupInfo
        {
            BackupId = backupId,
            GpoId = gpoId,
            DisplayName = FirstValue(root, "GPODisplayName", "DisplayName"),
            DomainName = FirstValue(root, "GPODomain", "Domain"),
            Comment = Value(root, "Comment"),
            Timestamp = ParseTimestamp(FirstValue(root, "BackupTime", "Timestamp", "CreationTime")),
            BackupDirectory = backupRoot
        };
    }

    private static string Value(XElement root, string localName) =>
        root.DescendantsAndSelf()
            .FirstOrDefault(element =>
                element.Name.LocalName.Equals(localName, StringComparison.OrdinalIgnoreCase))?
            .Value.Trim() ?? string.Empty;

    private static string FirstValue(XElement root, params string[] names)
    {
        foreach (var name in names)
        {
            var value = Value(root, name);
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }

        return string.Empty;
    }

    private static DateTime? ParseTimestamp(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        if (DateTime.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeLocal | DateTimeStyles.AllowWhiteSpaces,
                out var invariant))
            return invariant;

        return DateTime.TryParse(value, out var current)
            ? current
            : null;
    }

    private static int ReadOverallStatus(dynamic result)
    {
        try
        {
            return Convert.ToInt32((object?)result.OverallStatus());
        }
        catch
        {
            try
            {
                return Convert.ToInt32((object?)result.OverallStatus);
            }
            catch
            {
                return 0;
            }
        }
    }

    private static dynamic CreateGpm()
    {
        var type = Type.GetTypeFromProgID("GPMgmt.GPM")
            ?? throw new InvalidOperationException(
                "Group Policy Management components (GPMC/RSAT) are not installed.");

        return Activator.CreateInstance(type)
            ?? throw new InvalidOperationException(
                "Unable to create the GPMC automation object.");
    }

    private static void TryDeleteEmptyDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path) &&
                !Directory.EnumerateFileSystemEntries(path).Any())
                Directory.Delete(path);
        }
        catch
        {
        }
    }

    private static string SanitizeFileName(string value)
    {
        foreach (var invalid in Path.GetInvalidFileNameChars())
            value = value.Replace(invalid, '_');

        return value;
    }
}

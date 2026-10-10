using System.Security.Cryptography;
using GPOSettingsExplorer.Models;

namespace GPOSettingsExplorer.Services;

/// <summary>
/// Limited selective recovery: restore ONE previously-existing, reviewed numeric
/// SecEdit setting from an original-domain GPMC backup, never a whole GPO.
/// This intentionally does not restore ACLs, rights, unsupported fields,
/// missing settings, scripts, links, preferences or WMI assignments.
/// </summary>
public sealed record GpoSelectiveRecoveryCandidate(
    string Section, string Key, int CurrentValue, int BackupValue,
    string Caution, RealSettingRecord CurrentSource)
{
    public string DisplayName => $"[{Section}] {Key}";
}

public sealed record GpoSelectiveRecoveryPlan(
    GpoBackupInfo Backup, GpoInfo Target,
    string BackupFile, string BackupSha256,
    DateTimeOffset ExaminedAt,
    IReadOnlyList<GpoSelectiveRecoveryCandidate> Candidates,
    string Limitations)
{
    public string Summary =>
        $"{Candidates.Count} individually recoverable, pre-existing supported " +
        "Security Settings values. NO full or arbitrary GPO restore.";
}

public static class GpoSelectiveRecoveryService
{
    public static GpoSelectiveRecoveryPlan Inspect(
        GpoBackupInfo backup, GpoInfo gpo, RealSettingsScanResult current)
    {
        ArgumentNullException.ThrowIfNull(backup);
        ArgumentNullException.ThrowIfNull(gpo);
        ArgumentNullException.ThrowIfNull(current);
        if (backup.GpoId != gpo.Id ||
            !backup.DomainName.Equals(gpo.DomainName, StringComparison.OrdinalIgnoreCase) ||
            current.GpoId != gpo.Id ||
            !current.Domain.Equals(gpo.DomainName, StringComparison.OrdinalIgnoreCase) ||
            !current.Matches(gpo.DomainName,
                DomainConnectionState.GetServerFor(gpo.DomainName)))
            throw new InvalidOperationException(
                "Backup, target GPO and pinned-DC source snapshot must identify the SAME GPO/domain.");

        var path = Path.Combine(Path.GetFullPath(backup.BackupDirectory),
            backup.BackupId.ToString("B").ToUpperInvariant(),
            "DomainSysvol", "GPO", "Machine", "Microsoft", "Windows NT",
            "SecEdit", "GptTmpl.inf");
        if (!File.Exists(path))
            throw new FileNotFoundException(
                "The backup has no Machine SecEdit GptTmpl.inf. Select a backup with that source.", path);
        var stat = new FileInfo(path);
        if (stat.Length > SecurityTemplateSourceReader.MaxFileBytes)
            throw new InvalidDataException("Backup security template exceeds the safety cap.");
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length > SecurityTemplateSourceReader.MaxFileBytes)
            throw new InvalidDataException("Backup security template grew beyond the read cap.");

        var sha = Convert.ToHexString(SHA256.HashData(bytes));
        var parsed = SecurityTemplateSourceReader.Parse(bytes, gpo.Id,
            gpo.DisplayName, path, sha);
        if (!parsed.IsComplete)
            throw new InvalidDataException(
                "Backup Security Template failed strict parsing: " +
                string.Join(" | ", parsed.Issues.Take(5)));

        // No recovery attempt is permitted from incomplete or unreadable live
        // security-template evidence, even if other GPO source files are fine.
        var liveFiles = current.Files.Where(file =>
            Path.GetFileName(file.SourceFile).Equals("GptTmpl.inf",
                StringComparison.OrdinalIgnoreCase)).ToArray();
        if (liveFiles.Length != 1 || liveFiles[0].Status != "Read")
            throw new InvalidOperationException(
                "The live GptTmpl.inf was not fully read. No selective recovery permitted.");

        var backupRows = parsed.Rows
            .GroupBy(row => row.Category + "\0" + row.SettingName,
                StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() == 1)
            .Select(group => group.Single())
            .ToArray();

        var liveRows = current.Rows
            .GroupBy(row => row.Category + "\0" + row.SettingName,
                StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() == 1)
            .ToDictionary(group => group.Key, group => group.Single(),
                StringComparer.OrdinalIgnoreCase);
        var result = new List<GpoSelectiveRecoveryCandidate>();
        foreach (var previous in backupRows)
        {
            if (!SecurityTemplateEditRules.TryDescribe(previous, out var previousRule) ||
                previousRule is null)
                continue;
            var identity = previous.Category + "\0" + previous.SettingName;
            if (!liveRows.TryGetValue(identity, out var now) ||
                !SecurityTemplateEditRules.TryDescribe(now, out var currentRule) ||
                currentRule is null || currentRule.CurrentValue == previousRule.CurrentValue ||
                !currentRule.Allows(previousRule.CurrentValue))
                continue;
            result.Add(new GpoSelectiveRecoveryCandidate(
                currentRule.Section, currentRule.Key,
                currentRule.CurrentValue, previousRule.CurrentValue,
                currentRule.Warning, now));
        }

        return new GpoSelectiveRecoveryPlan(backup, gpo, path, sha,
            DateTimeOffset.Now,
            result.OrderBy(row => row.Section, StringComparer.Ordinal)
                .ThenBy(row => row.Key, StringComparer.Ordinal).ToArray(),
            "Limited to existing Event Audit and approved System Access numeric entries. " +
            "No absent settings are created. No ACL, SID, Restricted Groups, Rights, " +
            "Security Descriptors, scripts, registry.pol, GPP, links or WMI settings " +
            "are recovered. Recovery of one value is a NEW change and needs a " +
            "fresh GPMC safety backup, SHA-256 checks and approval.");
    }

    public static void EnsureBackupUnchanged(GpoSelectiveRecoveryPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var info = new FileInfo(plan.BackupFile);
        if (!info.Exists || info.Length > SecurityTemplateSourceReader.MaxFileBytes)
            throw new IOException("Backup source missing or oversize since preview.");
        var now = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(plan.BackupFile)));
        if (!now.Equals(plan.BackupSha256, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Backup security template changed since preview; reload.");
    }
}

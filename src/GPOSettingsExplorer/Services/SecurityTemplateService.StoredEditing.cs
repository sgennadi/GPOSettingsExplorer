using System.Security.Cryptography;
using System.Text;
using GPOSettingsExplorer.Models;

namespace GPOSettingsExplorer.Services;

/// <summary>
/// Narrow SecEdit source editor. Only pre-existing, exact, supported numeric
/// entries are modified. All writes require the GPMC backup performed by the
/// caller and a second source-hash comparison immediately before replacement.
/// </summary>
public sealed partial class SecurityTemplateService
{
    public void ApplyStoredNumeric(
        GpoInfo gpo, string domainDistinguishedName,
        RealSettingRecord record, int proposedValue)
    {
        EditingGuard.EnsureEnabled("Edit stored security policy");
        ArgumentNullException.ThrowIfNull(gpo);
        ArgumentNullException.ThrowIfNull(record);
        if (!SecurityTemplateEditRules.TryDescribe(record, out var rule) ||
            rule is null || !rule.Allows(proposedValue))
            throw new InvalidOperationException("Unsupported or malformed Security Settings record.");
        if (proposedValue == rule.CurrentValue)
            return;
        if (record.GpoId != gpo.Id ||
            !record.GpoName.Equals(gpo.DisplayName, StringComparison.Ordinal))
            throw new InvalidOperationException("Source record does not belong to selected GPO.");
        var context = DomainConnectionState.Context;
        if (context is null ||
            !context.DomainName.Equals(gpo.DomainName, StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(context.ConnectedServer) ||
            context.DomainDistinguishedName != domainDistinguishedName)
            throw new InvalidOperationException("Connected DC/domain changed; reload source evidence.");

        var path = Path.Combine(DomainConnectionState.BuildSysvolRoot(gpo.DomainName),
            "Policies", gpo.Id.ToString("B").ToUpperInvariant(), "Machine",
            "Microsoft", "Windows NT", "SecEdit", "GptTmpl.inf");
        if (!Path.GetFullPath(path).Equals(Path.GetFullPath(record.SourceFile),
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "Source path is not the selected GPO on the session-pinned DC.");
        if (!File.Exists(path))
            throw new FileNotFoundException("Source file disappeared; do not recreate it.", path);

        var original = File.ReadAllBytes(path);
        if (original.Length > SecurityTemplateSourceReader.MaxFileBytes)
            throw new InvalidDataException("Security source file exceeds the read limit.");
        var sha = Convert.ToHexString(SHA256.HashData(original));
        if (!sha.Equals(record.SourceSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "GptTmpl.inf changed since inspection. Reload before editing.");

        var parsed = SecurityTemplateSourceReader.Parse(original, gpo.Id, gpo.DisplayName,
            path, sha);
        if (!parsed.IsComplete)
            throw new InvalidDataException("Security template has parsing issues; editing refused: " +
                string.Join(" | ", parsed.Issues.Take(5)));

        var matches = parsed.Rows.Where(row =>
            row.Category.Equals(record.Category, StringComparison.Ordinal) &&
            row.SettingName.Equals(record.SettingName, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (matches.Length != 1 || !matches[0].Value.Equals(record.Value, StringComparison.Ordinal))
            throw new InvalidOperationException(
                "The source key is absent, duplicated or changed. No new setting will be created.");

        // Decode with the same encoding family as the existing SecurityTemplateService.
        // The strict source parser above must succeed before this conversion is allowed.
        var encoding = DetectEncoding(original);
        var text = encoding.GetString(StripPreamble(original, encoding));
        var updated = SecurityTemplateEditRules.ChangeExistingValue(text, rule, proposedValue);
        var output = encoding.GetPreamble().Concat(encoding.GetBytes(updated)).ToArray();

        ChangePreviewGuard.Confirm(new ChangePreviewRequest(
            $"Edit Security Settings: {record.SettingName}", gpo.DisplayName,
            $"{record.Category}\n{record.SettingName} = {rule.CurrentValue}",
            $"{record.Category}\n{record.SettingName} = {proposedValue}",
            $"Pinned DC: {context.ConnectedServer}\n" +
            $"Source: {path}\nSource SHA-256: {sha}\n" +
            $"Category: {record.Category}\n{rule.Warning}\n" +
            "A full GPMC backup must already exist. Confirm your intended clients; " +
            "this is NOT an effective RSoP calculation.", "Apply"));

        // Never overwrite a changed file or leave a rollback in SYSVOL. File.Replace
        // is required: failure is reported, not downgraded to non-atomic File.Copy.
        var staging = path + ".gposes-" + Guid.NewGuid().ToString("N") + ".tmp";
        var localRollbackDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GPOSettingsExplorer", "SecurityRollback");
        Directory.CreateDirectory(localRollbackDir);
        var localRollback = Path.Combine(localRollbackDir,
            $"{gpo.Id:N}-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.inf");
        var replaced = false;
        try
        {
            // Keep a local byte-exact copy in case COM Save subsequently fails.
            File.WriteAllBytes(localRollback, original);
            File.WriteAllBytes(staging, output);
            if (!File.ReadAllBytes(path).AsSpan().SequenceEqual(original))
                throw new IOException("Security template changed during write preparation; re-read GPO.");
            EditingGuard.EnsureEnabled("Edit stored security policy");
            File.Replace(staging, path, null);
            replaced = true;
            using (var policy = new NativeGroupPolicyObject(gpo, domainDistinguishedName))
            {
                var extension = SecurityExtensionGuid;
                var tool = SecurityToolGuid;
                policy.Save(machine: true, add: true, ref extension, ref tool);
            }
            // Drop redundant rollback after a completed GPMC save.
            File.Delete(localRollback);
        }
        catch (Exception ex) when (replaced)
        {
            throw new IOException(
                "GptTmpl.inf was replaced, but GPMC Save did not complete. " +
                "Do not retry blindly. Inspect AD/SYSVOL health and restore via the GPMC " +
                "backup if needed. Byte-exact local source copy: " + localRollback, ex);
        }
        finally
        {
            // A failed atomic replacement leaves the original file untouched.
            try { if (File.Exists(staging)) File.Delete(staging); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            if (!replaced)
            {
                try { if (File.Exists(localRollback)) File.Delete(localRollback); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }
}

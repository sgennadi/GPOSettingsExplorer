using System.IO;
using System.Diagnostics;
using System.DirectoryServices;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Xml.Linq;
using GPOSettingsExplorer.Models;

namespace GPOSettingsExplorer.Services;

public sealed class GpmService
{
    public bool IsAvailable => Type.GetTypeFromProgID("GPMgmt.GPM") is not null;

    public IReadOnlyList<GpoInfo> LoadGpos(string domainName)
    {
        dynamic gpm = CreateGpm();
        dynamic constants = gpm.GetConstants();
        dynamic domain = GetDomain(gpm, constants, domainName);
        dynamic criteria = gpm.CreateSearchCriteria();
        dynamic collection = domain.SearchGPOs(criteria);

        var result = new List<GpoInfo>();
        var count = Convert.ToInt32(collection.Count);

        for (var i = 1; i <= count; i++)
        {
            dynamic gpo = collection.Item(i);
            dynamic? wmiFilter = null;
            try
            {
                wmiFilter = gpo.GetWMIFilter();
            }
            catch
            {
                // A missing filter may surface as either null or a COM status code.
            }

            string idText = Convert.ToString((object?)gpo.ID) ?? string.Empty;
            Guid id;
            if (!Guid.TryParse(idText, out id))
            {
                continue;
            }

            result.Add(new GpoInfo
            {
                Id = id,
                DisplayName = Convert.ToString(gpo.DisplayName) ?? idText,
                DomainName = domainName,
                CreationTime = TryConvertDateTime(gpo.CreationTime),
                ModificationTime = TryConvertDateTime(gpo.ModificationTime),
                ComputerEnabled = SafeBool(() => gpo.IsComputerEnabled()),
                UserEnabled = SafeBool(() => gpo.IsUserEnabled()),
                WmiFilterName = wmiFilter is null ? string.Empty : Convert.ToString(wmiFilter.Name) ?? string.Empty,
                WmiFilterPath = wmiFilter is null ? string.Empty : Convert.ToString(wmiFilter.Path) ?? string.Empty
            });
        }

        return result.OrderBy(g => g.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    public IReadOnlyList<PolicySettingInfo> BuildSettingsIndex(
        string domainName,
        IEnumerable<GpoInfo> gpos,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        dynamic gpm = CreateGpm();
        dynamic constants = gpm.GetConstants();
        dynamic domain = GetDomain(gpm, constants, domainName);

        var settings = new List<PolicySettingInfo>();
        var gpoList = gpos.ToList();

        for (var index = 0; index < gpoList.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var info = gpoList[index];
            progress?.Report($"Indexing {index + 1}/{gpoList.Count}: {info.DisplayName}");

            dynamic gpo = domain.GetGPO(info.Id.ToString("B"));
            var tempFile = Path.Combine(Path.GetTempPath(), $"GPOSettingsExplorer-{Guid.NewGuid():N}.xml");

            try
            {
                gpo.GenerateReportToFile(constants.ReportXML, tempFile);
                settings.AddRange(ParseReport(tempFile, info));
            }
            finally
            {
                try { File.Delete(tempFile); } catch { }
            }
        }

        return settings;
    }

    public string GenerateXmlReport(
        string domainName,
        Guid gpoId)
    {
        dynamic gpm =
            CreateGpm();

        dynamic constants =
            gpm.GetConstants();

        dynamic domain =
            GetDomain(
                gpm,
                constants,
                domainName);

        dynamic gpo =
            domain.GetGPO(
                gpoId.ToString(
                    "B"));

        var temp =
            Path.Combine(
                Path.GetTempPath(),
                $"GPOSettingsExplorer-report-{Guid.NewGuid():N}.xml");

        try
        {
            gpo.GenerateReportToFile(
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

    public void OpenEditor(
        GpoInfo gpo,
        string domainDistinguishedName)
    {
        _ = OpenEditorProcess(
            gpo,
            domainDistinguishedName);
    }

    public Process OpenEditorProcess(
        GpoInfo gpo,
        string domainDistinguishedName)
    {
        var objectPath =
            DomainConnectionState.BuildLdapPath(
                $"CN={gpo.Id:B},CN=Policies,CN=System,{domainDistinguishedName}");

        var arguments =
            $"gpme.msc /gpobject:\"{objectPath}\"";

        var systemDirectory =
            Environment.GetFolderPath(
                Environment.SpecialFolder.System);

        var mmcPath =
            Path.Combine(
                systemDirectory,
                "mmc.exe");

        return Process.Start(
                   new ProcessStartInfo
                   {
                       FileName =
                           mmcPath,
                       Arguments =
                           arguments,
                       WorkingDirectory =
                           systemDirectory,
                       UseShellExecute =
                           true
                   })
               ?? throw new InvalidOperationException(
                   "Unable to start the Group Policy Management Editor.");
    }

    public Guid CreateGpo(string domainName, string displayName)
    {
        EditingGuard.EnsureEnabled(
            "Create GPO");
        if (string.IsNullOrWhiteSpace(displayName))
        {
            throw new ArgumentException("GPO name cannot be empty.", nameof(displayName));
        }

        dynamic gpm = CreateGpm();
        dynamic constants = gpm.GetConstants();
        dynamic domain = GetDomain(gpm, constants, domainName);

        ChangePreviewGuard.Confirm(
            new ChangePreviewRequest(
                "Create GPO",
                domainName,
                "<not present>",
                displayName.Trim(),
                "A new Group Policy Object will be created.",
                "Create"));

        dynamic gpo = domain.CreateGPO();
        gpo.DisplayName = displayName.Trim();

        string idText = Convert.ToString((object?)gpo.ID) ?? string.Empty;
        if (!Guid.TryParse(idText, out Guid id))
        {
            throw new InvalidOperationException("The GPO was created, but its GUID could not be read.");
        }

        return id;
    }

    public Guid CopyGpo(
        string domainName,
        Guid sourceGpoId,
        string newDisplayName,
        bool copyAcl)
    {
        EditingGuard.EnsureEnabled(
            "Copy GPO");
        if (string.IsNullOrWhiteSpace(newDisplayName))
        {
            throw new ArgumentException("GPO name cannot be empty.", nameof(newDisplayName));
        }

        dynamic gpm = CreateGpm();
        dynamic constants = gpm.GetConstants();
        dynamic domain = GetDomain(gpm, constants, domainName);
        dynamic source = domain.GetGPO(sourceGpoId.ToString("B"));

        ChangePreviewGuard.Confirm(
            new ChangePreviewRequest(
                "Copy GPO",
                domainName,
                $"Source: {Convert.ToString((object?)source.DisplayName) ?? sourceGpoId.ToString("B")}",
                $"New GPO: {newDisplayName.Trim()}",
                copyAcl
                    ? "The GPO settings and ACL will be copied."
                    : "The GPO settings will be copied without processing the source ACL.",
                "Copy"));

        var flags = copyAcl
            ? Convert.ToInt32((object?)constants.ProcessSecurity)
            : 0;

        dynamic result = source.CopyTo(
            flags,
            domain,
            newDisplayName.Trim());

        EnsureGpmResultSuccess(result, "Copy GPO");

        dynamic copiedGpo = result.Result;
        string idText = Convert.ToString((object?)copiedGpo.ID) ?? string.Empty;

        if (!Guid.TryParse(idText, out Guid copiedId))
        {
            throw new InvalidOperationException(
                "The GPO copy completed, but the new GPO GUID could not be read.");
        }

        return copiedId;
    }

    public void ImportBackupSettings(
        string domainName,
        Guid targetGpoId,
        GpoBackupInfo backup,
        string? migrationTablePath = null)
    {
        EditingGuard.EnsureEnabled(
            "Import GPO settings");
        if (!Directory.Exists(backup.BackupDirectory))
        {
            throw new DirectoryNotFoundException(
                $"Backup directory does not exist: {backup.BackupDirectory}");
        }

        dynamic gpm = CreateGpm();
        dynamic constants = gpm.GetConstants();
        dynamic domain = GetDomain(gpm, constants, domainName);
        dynamic target = domain.GetGPO(targetGpoId.ToString("B"));

        ChangePreviewGuard.Confirm(
            new ChangePreviewRequest(
                "Import GPO settings",
                Convert.ToString((object?)target.DisplayName) ?? targetGpoId.ToString("B"),
                "Current GPO settings",
                $"Backup: {backup.DisplayName} | {backup.BackupId:B}",
                string.IsNullOrWhiteSpace(migrationTablePath)
                    ? "No migration table will be used."
                    : $"Migration table: {migrationTablePath}",
                "Import"));

        dynamic backupDirectory = gpm.GetBackupDir(backup.BackupDirectory);
        dynamic backupObject = backupDirectory.GetBackup(backup.BackupId.ToString("B"));

        dynamic result;

        if (string.IsNullOrWhiteSpace(migrationTablePath))
        {
            result = target.Import(0, backupObject);
        }
        else
        {
            var fullMigrationPath = Path.GetFullPath(
                Environment.ExpandEnvironmentVariables(migrationTablePath.Trim()));

            if (!File.Exists(fullMigrationPath))
            {
                throw new FileNotFoundException(
                    "Migration table file was not found.",
                    fullMigrationPath);
            }

            dynamic migrationTable = gpm.GetMigrationTable(fullMigrationPath);
            result = target.Import(0, backupObject, migrationTable);
        }

        EnsureGpmResultSuccess(result, "Import GPO settings");
    }

    public void RenameGpo(string domainName, Guid gpoId, string newDisplayName)
    {
        EditingGuard.EnsureEnabled(
            "Rename GPO");
        if (string.IsNullOrWhiteSpace(newDisplayName))
        {
            throw new ArgumentException("GPO name cannot be empty.", nameof(newDisplayName));
        }

        dynamic gpm = CreateGpm();
        dynamic constants = gpm.GetConstants();
        dynamic domain = GetDomain(gpm, constants, domainName);
        dynamic gpo = domain.GetGPO(gpoId.ToString("B"));

        ChangePreviewGuard.Confirm(
            new ChangePreviewRequest(
                "Rename GPO",
                gpoId.ToString("B"),
                Convert.ToString((object?)gpo.DisplayName) ?? string.Empty,
                newDisplayName.Trim(),
                string.Empty,
                "Rename"));

        gpo.DisplayName = newDisplayName.Trim();
    }

    public void DeleteGpo(string domainName, Guid gpoId)
    {
        EditingGuard.EnsureEnabled(
            "Delete GPO");
        dynamic gpm = CreateGpm();
        dynamic constants = gpm.GetConstants();
        dynamic domain = GetDomain(gpm, constants, domainName);
        dynamic gpo = domain.GetGPO(gpoId.ToString("B"));

        ChangePreviewGuard.Confirm(
            new ChangePreviewRequest(
                "Delete GPO",
                Convert.ToString((object?)gpo.DisplayName) ?? gpoId.ToString("B"),
                gpoId.ToString("B"),
                "<deleted>",
                "This deletes the Group Policy Object.",
                "Delete"));

        gpo.Delete();
    }

    public void SetComputerEnabled(string domainName, Guid gpoId, bool enabled)
    {
        EditingGuard.EnsureEnabled(
            "Change Computer Configuration scope");
        dynamic gpm = CreateGpm();
        dynamic constants = gpm.GetConstants();
        dynamic domain = GetDomain(gpm, constants, domainName);
        dynamic gpo = domain.GetGPO(gpoId.ToString("B"));

        var current =
            SafeBool(
                () => gpo.IsComputerEnabled());

        ChangePreviewGuard.Confirm(
            new ChangePreviewRequest(
                "Change Computer Configuration scope",
                Convert.ToString((object?)gpo.DisplayName) ?? gpoId.ToString("B"),
                current ? "Enabled" : "Disabled",
                enabled ? "Enabled" : "Disabled",
                string.Empty,
                "Apply"));

        gpo.SetComputerEnabled(enabled);
    }

    public void SetUserEnabled(string domainName, Guid gpoId, bool enabled)
    {
        EditingGuard.EnsureEnabled(
            "Change User Configuration scope");
        dynamic gpm = CreateGpm();
        dynamic constants = gpm.GetConstants();
        dynamic domain = GetDomain(gpm, constants, domainName);
        dynamic gpo = domain.GetGPO(gpoId.ToString("B"));

        var current =
            SafeBool(
                () => gpo.IsUserEnabled());

        ChangePreviewGuard.Confirm(
            new ChangePreviewRequest(
                "Change User Configuration scope",
                Convert.ToString((object?)gpo.DisplayName) ?? gpoId.ToString("B"),
                current ? "Enabled" : "Disabled",
                enabled ? "Enabled" : "Disabled",
                string.Empty,
                "Apply"));

        gpo.SetUserEnabled(enabled);
    }

    public IReadOnlyList<GpoPermissionInfo> LoadPermissions(string domainName, Guid gpoId)
    {
        try
        {
            return LoadPermissionsWithGpm(
                domainName,
                gpoId);
        }
        catch (Exception gpmException)
            when (!IsFatal(
                gpmException))
        {
            try
            {
                return LoadPermissionsFromDirectory(
                    gpoId);
            }
            catch (Exception directoryException)
                when (!IsFatal(
                    directoryException))
            {
                throw new InvalidOperationException(
                    BuildSecurityLoadDiagnostic(
                        domainName,
                        gpoId,
                        gpmException,
                        directoryException),
                    directoryException);
            }
        }
    }

    private static IReadOnlyList<GpoPermissionInfo> LoadPermissionsWithGpm(
        string domainName,
        Guid gpoId)
    {
        dynamic gpm = CreateGpm();
        dynamic constants = gpm.GetConstants();
        dynamic domain = GetDomain(gpm, constants, domainName);
        dynamic gpo = domain.GetGPO(gpoId.ToString("B"));
        dynamic security = gpo.GetSecurityInfo();

        var gpoName =
            SafeString(() => gpo.DisplayName);

        if (string.IsNullOrWhiteSpace(gpoName))
        {
            gpoName =
                gpoId.ToString("B");
        }

        var result =
            new List<GpoPermissionInfo>();

        var count =
            Convert.ToInt32(
                (object?)security.Count);

        for (var i = 1;
             i <= count;
             i++)
        {
            try
            {
                dynamic permission =
                    security.Item(i);

                dynamic trustee =
                    permission.Trustee;

                var rawPermission =
                    SafeInt(() => permission.Permission);

                var trusteeSid =
                    SafeString(() => trustee.TrusteeSid);

                var trusteeName =
                    SafeString(() => trustee.TrusteeName);

                var trusteeDomain =
                    SafeString(() => trustee.TrusteeDomain);

                if (string.IsNullOrWhiteSpace(trusteeName) &&
                    !string.IsNullOrWhiteSpace(trusteeSid))
                {
                    trusteeName =
                        trusteeSid;
                }

                result.Add(
                    new GpoPermissionInfo
                    {
                        GpoId = gpoId,
                        GpoName = gpoName,
                        TrusteeName = trusteeName,
                        TrusteeDomain = trusteeDomain,
                        TrusteeSid = trusteeSid,
                        TrusteeDsPath =
                            SafeString(() => trustee.TrusteeDSPath),
                        TrusteeType =
                            SafeInt(() => trustee.TrusteeType),
                        Level =
                            MapPermissionLevel(rawPermission),
                        RawPermission = rawPermission,
                        Denied =
                            SafeBool(() => permission.Denied),
                        Inherited =
                            SafeBool(() => permission.Inherited),
                        Inheritable =
                            SafeBool(() => permission.Inheritable)
                    });
            }
            catch (Exception ex)
                when (!IsFatal(
                    ex))
            {
                // A deleted, stale or otherwise unresolvable trustee must not
                // make the complete GPO security page unusable. GPMC can
                // surface these failures as COMException, FileNotFoundException
                // or RuntimeBinderException depending on the trustee property.
            }
        }

        return SortPermissions(
            result);
    }

    private static IReadOnlyList<GpoPermissionInfo> LoadPermissionsFromDirectory(
        Guid gpoId)
    {
        using var rootDse =
            new DirectoryEntry(
                DomainConnectionState.BuildRootDsePath());

        var defaultNamingContext =
            Convert.ToString(
                rootDse.Properties[
                    "defaultNamingContext"].Value)
            ?? throw new InvalidOperationException(
                "The Active Directory default naming context is unavailable.");

        var gpoDn =
            $"CN={gpoId.ToString("B").ToUpperInvariant()},CN=Policies,CN=System,{defaultNamingContext}";

        using var gpoEntry =
            new DirectoryEntry(
                DomainConnectionState.BuildLdapPath(
                    gpoDn));

        gpoEntry.Options.SecurityMasks =
            SecurityMasks.Dacl;

        var displayName =
            Convert.ToString(
                gpoEntry.Properties[
                    "displayName"].Value)
            ?? gpoId.ToString("B");

        var security =
            gpoEntry.ObjectSecurity;

        var rules =
            security
                .GetAccessRules(
                    includeExplicit: true,
                    includeInherited: true,
                    targetType: typeof(SecurityIdentifier))
                .OfType<ActiveDirectoryAccessRule>()
                .ToArray();

        var result =
            new List<GpoPermissionInfo>();

        foreach (var group in rules
                     .GroupBy(rule => new
                     {
                         Sid =
                             ((SecurityIdentifier)rule.IdentityReference).Value,
                         rule.AccessControlType
                     }))
        {
            var groupedRules =
                group.ToArray();

            var level =
                MapDirectoryPermissionLevel(
                    groupedRules);

            if (level is null)
            {
                continue;
            }

            var sid =
                new SecurityIdentifier(
                    group.Key.Sid);

            var account =
                ResolveAccountName(
                    sid);

            var separator =
                account.IndexOf(
                    '\\');

            var trusteeDomain =
                separator > 0
                    ? account[..separator]
                    : string.Empty;

            var trusteeName =
                separator > 0
                    ? account[(separator + 1)..]
                    : account;

            var rawPermission =
                PermissionCodeForLevel(
                    level.Value);

            result.Add(
                new GpoPermissionInfo
                {
                    GpoId = gpoId,
                    GpoName = displayName,
                    TrusteeName = trusteeName,
                    TrusteeDomain = trusteeDomain,
                    TrusteeSid = sid.Value,
                    TrusteeDsPath = string.Empty,
                    TrusteeType = 0,
                    Level = level.Value,
                    RawPermission = rawPermission,
                    Denied =
                        group.Key.AccessControlType ==
                        AccessControlType.Deny,
                    Inherited =
                        groupedRules.All(rule =>
                            rule.IsInherited),
                    Inheritable =
                        groupedRules.Any(rule =>
                            rule.InheritanceType !=
                            ActiveDirectorySecurityInheritance.None)
                });
        }

        return SortPermissions(
            result);
    }

    private static GpoPermissionLevel? MapDirectoryPermissionLevel(
        IReadOnlyCollection<ActiveDirectoryAccessRule> rules)
    {
        var rights =
            rules.Aggregate(
                (ActiveDirectoryRights)0,
                (current, rule) =>
                    current |
                    rule.ActiveDirectoryRights);

        if ((rights &
             ActiveDirectoryRights.GenericAll) != 0 ||
            (rights &
             (ActiveDirectoryRights.WriteDacl |
              ActiveDirectoryRights.WriteOwner |
              ActiveDirectoryRights.Delete)) != 0)
        {
            return GpoPermissionLevel.FullControl;
        }

        if ((rights &
             (ActiveDirectoryRights.GenericWrite |
              ActiveDirectoryRights.WriteProperty |
              ActiveDirectoryRights.CreateChild |
              ActiveDirectoryRights.DeleteChild |
              ActiveDirectoryRights.Self)) != 0)
        {
            return GpoPermissionLevel.Edit;
        }

        var applyGroupPolicyGuid =
            new Guid(
                "edacfd8f-ffb3-11d1-b41d-00a0c968f939");

        if (rules.Any(rule =>
                (rule.ActiveDirectoryRights &
                 ActiveDirectoryRights.ExtendedRight) != 0 &&
                (rule.ObjectType == applyGroupPolicyGuid ||
                 rule.ObjectType == Guid.Empty)))
        {
            return GpoPermissionLevel.Apply;
        }

        if ((rights &
             (ActiveDirectoryRights.GenericRead |
              ActiveDirectoryRights.ReadProperty |
              ActiveDirectoryRights.ReadControl |
              ActiveDirectoryRights.ListChildren |
              ActiveDirectoryRights.ListObject)) != 0)
        {
            return GpoPermissionLevel.Read;
        }

        return null;
    }

    private static int PermissionCodeForLevel(
        GpoPermissionLevel level)
    {
        return level switch
        {
            GpoPermissionLevel.Apply => 0x10000,
            GpoPermissionLevel.Read => 0x10100,
            GpoPermissionLevel.Edit => 0x10101,
            GpoPermissionLevel.FullControl => 0x10102,
            _ => 0
        };
    }

    private static string BuildSecurityLoadDiagnostic(
        string domainName,
        Guid gpoId,
        Exception gpmException,
        Exception directoryException)
    {
        var builder =
            new System.Text.StringBuilder();

        builder.AppendLine(
            "Unable to load GPO security.");
        builder.AppendLine();
        builder.AppendLine(
            $"GPO GUID: {gpoId:B}");
        builder.AppendLine(
            $"Domain: {domainName}");
        builder.AppendLine();
        builder.AppendLine(
            "GPMC stage: GetSecurityInfo()");
        builder.AppendLine(
            $"GPMC error: {gpmException.Message}");
        builder.AppendLine(
            $"GPMC HRESULT: 0x{gpmException.HResult:X8}");

        if (gpmException.HResult ==
            unchecked((int)0x80070002))
        {
            builder.AppendLine(
                "GPMC returned ERROR_FILE_NOT_FOUND, but the COM exception did not include the concrete file name.");
        }

        builder.AppendLine();
        builder.AppendLine(
            "Active Directory fallback error:");
        builder.AppendLine(
            $"{directoryException.GetType().Name}: {directoryException.Message}");

        try
        {
            using var rootDse =
                new DirectoryEntry(
                    DomainConnectionState.BuildRootDsePath());

            var defaultNamingContext =
                Convert.ToString(
                    rootDse.Properties[
                        "defaultNamingContext"].Value)
                ?? string.Empty;

            var gpoDn =
                $"CN={gpoId.ToString("B").ToUpperInvariant()},CN=Policies,CN=System,{defaultNamingContext}";

            builder.AppendLine();
            builder.AppendLine(
                "Paths checked:");
            builder.AppendLine(
                $"AD object: LDAP://{gpoDn}");

            using var gpoEntry =
                new DirectoryEntry(
                    DomainConnectionState.BuildLdapPath(
                        gpoDn));

            var displayName =
                Convert.ToString(
                    gpoEntry.Properties[
                        "displayName"].Value)
                ?? string.Empty;

            if (!string.IsNullOrWhiteSpace(
                    displayName))
            {
                builder.AppendLine(
                    $"GPO name: {displayName}");
            }

            var fileSysPath =
                Convert.ToString(
                    gpoEntry.Properties[
                        "gPCFileSysPath"].Value)
                ?? string.Empty;

            if (string.IsNullOrWhiteSpace(
                    fileSysPath))
            {
                builder.AppendLine(
                    "gPCFileSysPath: <missing in Active Directory>");
            }
            else
            {
                builder.AppendLine(
                    $"gPCFileSysPath: {fileSysPath}");

                var directoryExists =
                    Directory.Exists(
                        fileSysPath);

                builder.AppendLine(
                    $"SYSVOL GPO folder exists: {directoryExists}");

                var gptIniPath =
                    Path.Combine(
                        fileSysPath,
                        "GPT.INI");

                builder.AppendLine(
                    $"GPT.INI: {gptIniPath}");
                builder.AppendLine(
                    $"GPT.INI exists: {File.Exists(gptIniPath)}");

                var machinePath =
                    Path.Combine(
                        fileSysPath,
                        "Machine");

                var userPath =
                    Path.Combine(
                        fileSysPath,
                        "User");

                builder.AppendLine(
                    $"Machine folder: {machinePath} | exists: {Directory.Exists(machinePath)}");
                builder.AppendLine(
                    $"User folder: {userPath} | exists: {Directory.Exists(userPath)}");
            }
        }
        catch (Exception diagnosticException)
        {
            builder.AppendLine();
            builder.AppendLine(
                "Path diagnostics failed:");
            builder.AppendLine(
                $"{diagnosticException.GetType().Name}: {diagnosticException.Message}");
        }

        return builder
            .ToString()
            .TrimEnd();
    }

    private static string ResolveAccountName(
        SecurityIdentifier sid)
    {
        try
        {
            return sid
                .Translate(
                    typeof(NTAccount))
                .Value;
        }
        catch (IdentityNotMappedException)
        {
            return sid.Value;
        }
        catch (SystemException)
        {
            return sid.Value;
        }
    }

    private static IReadOnlyList<GpoPermissionInfo> SortPermissions(
        IEnumerable<GpoPermissionInfo> permissions)
    {
        return permissions
            .OrderBy(
                permission => permission.Category,
                StringComparer.OrdinalIgnoreCase)
            .ThenBy(
                permission => permission.TrusteeDisplay,
                StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(
                permission => permission.RawPermission)
            .ToArray();
    }

    public void AddPermission(
        string domainName,
        Guid gpoId,
        string trustee,
        GpoPermissionLevel level)
    {
        EditingGuard.EnsureEnabled(
            "Change GPO permissions");
        if (string.IsNullOrWhiteSpace(trustee))
        {
            throw new ArgumentException("Trustee cannot be empty.", nameof(trustee));
        }

        if (level == GpoPermissionLevel.Custom)
        {
            throw new InvalidOperationException(
                "Custom permissions can be displayed but are not created by the simplified permission editor.");
        }

        dynamic gpm = CreateGpm();
        dynamic constants = gpm.GetConstants();
        dynamic domain = GetDomain(gpm, constants, domainName);
        dynamic gpo = domain.GetGPO(gpoId.ToString("B"));
        dynamic security = gpo.GetSecurityInfo();

        ChangePreviewGuard.Confirm(
            new ChangePreviewRequest(
                "Add GPO permission",
                Convert.ToString((object?)gpo.DisplayName) ?? gpoId.ToString("B"),
                "<not present>",
                $"{trustee.Trim()} | {level}",
                string.Empty,
                "Add"));

        var permissionCode = GetPermissionConstant(constants, level);
        dynamic newPermission = gpm.CreatePermission(trustee.Trim(), permissionCode, true);
        security.Add(newPermission);
        gpo.SetSecurityInfo(security);
    }

    public void ReplacePermission(
        string domainName,
        Guid gpoId,
        GpoPermissionInfo existing,
        GpoPermissionLevel newLevel)
    {
        EditingGuard.EnsureEnabled(
            "Change GPO permissions");
        if (existing.Inherited)
        {
            throw new InvalidOperationException(
                "Inherited permissions cannot be changed on this GPO. Change them on the parent object.");
        }

        if (newLevel == GpoPermissionLevel.Custom)
        {
            throw new InvalidOperationException(
                "Custom permissions can be displayed but are not created by the simplified permission editor.");
        }

        dynamic gpm = CreateGpm();
        dynamic constants = gpm.GetConstants();
        dynamic domain = GetDomain(gpm, constants, domainName);
        dynamic gpo = domain.GetGPO(gpoId.ToString("B"));
        dynamic security = gpo.GetSecurityInfo();

        dynamic? current = FindPermissionObject(
            security,
            existing.TrusteeSid,
            existing.RawPermission,
            includeDenied: existing.Denied);

        if (current is null)
        {
            throw new InvalidOperationException(
                "The selected permission no longer exists. Refresh the permission list and try again.");
        }

        var trustee = string.IsNullOrWhiteSpace(existing.TrusteeSid)
            ? existing.TrusteeDisplay
            : existing.TrusteeSid;

        ChangePreviewGuard.Confirm(
            new ChangePreviewRequest(
                "Change GPO permission",
                Convert.ToString((object?)gpo.DisplayName) ?? gpoId.ToString("B"),
                $"{existing.TrusteeDisplay} | {existing.Level}",
                $"{existing.TrusteeDisplay} | {newLevel}",
                string.Empty,
                "Apply"));

        security.Remove(current);

        var permissionCode = GetPermissionConstant(constants, newLevel);
        dynamic newPermission = gpm.CreatePermission(trustee, permissionCode, true);
        security.Add(newPermission);
        gpo.SetSecurityInfo(security);
    }

    public void RemovePermission(
        string domainName,
        Guid gpoId,
        GpoPermissionInfo existing)
    {
        EditingGuard.EnsureEnabled(
            "Change GPO permissions");
        if (existing.Inherited)
        {
            throw new InvalidOperationException(
                "Inherited permissions cannot be removed from this GPO. Change them on the parent object.");
        }

        dynamic gpm = CreateGpm();
        dynamic constants = gpm.GetConstants();
        dynamic domain = GetDomain(gpm, constants, domainName);
        dynamic gpo = domain.GetGPO(gpoId.ToString("B"));
        dynamic security = gpo.GetSecurityInfo();

        dynamic? current = FindPermissionObject(
            security,
            existing.TrusteeSid,
            existing.RawPermission,
            includeDenied: existing.Denied);

        if (current is null)
        {
            throw new InvalidOperationException(
                "The selected permission no longer exists. Refresh the permission list and try again.");
        }

        ChangePreviewGuard.Confirm(
            new ChangePreviewRequest(
                "Remove GPO permission",
                Convert.ToString((object?)gpo.DisplayName) ?? gpoId.ToString("B"),
                $"{existing.TrusteeDisplay} | {existing.Level}",
                "<removed>",
                string.Empty,
                "Remove"));

        security.Remove(current);
        gpo.SetSecurityInfo(security);
    }

    public string BackupGpo(string domainName, Guid gpoId, string comment)
    {
        dynamic gpm = CreateGpm();
        dynamic constants = gpm.GetConstants();
        dynamic domain = GetDomain(gpm, constants, domainName);
        dynamic gpo = domain.GetGPO(gpoId.ToString("B"));

        var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var backupDirectory = Path.Combine(
            StoragePaths.GpoBackups,
            $"{SanitizeFileName(Convert.ToString(gpo.DisplayName) ?? gpoId.ToString("B"))}_{timestamp}");

        Directory.CreateDirectory(backupDirectory);

        try
        {
            _ = gpo.Backup(backupDirectory, comment, null, null);
            return backupDirectory;
        }
        catch
        {
            try
            {
                if (Directory.Exists(backupDirectory) &&
                    !Directory.EnumerateFileSystemEntries(backupDirectory).Any())
                {
                    Directory.Delete(backupDirectory, recursive: true);
                }
            }
            catch
            {
            }

            throw;
        }
    }

    public void SetWmiFilter(string domainName, Guid gpoId, WmiFilterInfo? filter)
    {
        EditingGuard.EnsureEnabled(
            "Assign WMI filter");
        dynamic gpm = CreateGpm();
        dynamic constants = gpm.GetConstants();
        dynamic domain = GetDomain(gpm, constants, domainName);
        dynamic gpo = domain.GetGPO(gpoId.ToString("B"));

        var currentFilter =
            string.Empty;

        try
        {
            dynamic existingFilter =
                gpo.GetWMIFilter();

            currentFilter =
                existingFilter is null
                    ? "<none>"
                    : Convert.ToString((object?)existingFilter.Name)
                      ?? "<assigned>";
        }
        catch
        {
            currentFilter =
                "<none>";
        }

        ChangePreviewGuard.Confirm(
            new ChangePreviewRequest(
                "Assign WMI filter",
                Convert.ToString((object?)gpo.DisplayName) ?? gpoId.ToString("B"),
                currentFilter,
                filter?.Name ?? "<none>",
                string.Empty,
                "Apply"));

        if (filter is null)
        {
            gpo.SetWMIFilter(null);
            return;
        }

        dynamic wmiFilter = domain.GetWMIFilter(filter.Path);
        gpo.SetWMIFilter(wmiFilter);
    }

    private static dynamic? FindPermissionObject(
        dynamic security,
        string trusteeSid,
        int rawPermission,
        bool includeDenied)
    {
        var count = Convert.ToInt32((object?)security.Count);

        for (var i = 1; i <= count; i++)
        {
            dynamic item = security.Item(i);
            dynamic trustee = item.Trustee;

            var sid = Convert.ToString((object?)trustee.TrusteeSid) ?? string.Empty;
            var permissionCode = Convert.ToInt32((object?)item.Permission);
            var denied = SafeBool(() => item.Denied);

            if (sid.Equals(trusteeSid, StringComparison.OrdinalIgnoreCase) &&
                permissionCode == rawPermission &&
                denied == includeDenied)
            {
                return item;
            }
        }

        return null;
    }

    private static GpoPermissionLevel MapPermissionLevel(int rawPermission)
    {
        return rawPermission switch
        {
            0x10000 => GpoPermissionLevel.Apply,
            0x10100 => GpoPermissionLevel.Read,
            0x10101 => GpoPermissionLevel.Edit,
            0x10102 => GpoPermissionLevel.FullControl,
            _ => GpoPermissionLevel.Custom
        };
    }

    private static object GetPermissionConstant(dynamic constants, GpoPermissionLevel level)
    {
        return level switch
        {
            GpoPermissionLevel.Apply => constants.PermGPOApply,
            GpoPermissionLevel.Read => constants.PermGPORead,
            GpoPermissionLevel.Edit => constants.PermGPOEdit,
            GpoPermissionLevel.FullControl => constants.PermGPOEditSecurityAndDelete,
            _ => throw new InvalidOperationException("Unsupported GPO permission level.")
        };
    }

    private static void EnsureGpmResultSuccess(
        dynamic result,
        string operation)
    {
        if (result is null)
        {
            throw new InvalidOperationException(
                $"{operation} did not return a GPMC result object.");
        }

        int status;

        try
        {
            status = Convert.ToInt32((object?)result.OverallStatus());
        }
        catch (Microsoft.CSharp.RuntimeBinder.RuntimeBinderException)
        {
            status = Convert.ToInt32((object?)result.OverallStatus);
        }

        if (status < 0)
        {
            Marshal.ThrowExceptionForHR(status);
        }

        if (status != 0)
        {
            throw new InvalidOperationException(
                $"{operation} returned GPMC status 0x{status:X8}.");
        }
    }

    private static string SanitizeFileName(string value)
    {
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            value = value.Replace(invalid, '_');
        }

        return value;
    }

    private static dynamic GetDomain(
        dynamic gpm,
        dynamic constants,
        string domainName)
    {
        var server =
            DomainConnectionState.GetServerFor(
                domainName);

        return string.IsNullOrWhiteSpace(
                server)
            ? gpm.GetDomain(
                domainName,
                string.Empty,
                constants.UseAnyDC)
            : gpm.GetDomain(
                domainName,
                server,
                0);
    }

    private static dynamic CreateGpm()
    {
        var type = Type.GetTypeFromProgID("GPMgmt.GPM")
            ?? throw new InvalidOperationException(
                "Group Policy Management components (GPMC/RSAT) are not installed.");

        return Activator.CreateInstance(type)
            ?? throw new InvalidOperationException("Unable to create the GPMC automation object.");
    }

    private static IEnumerable<PolicySettingInfo> ParseReport(string path, GpoInfo gpo)
    {
        var document = XDocument.Load(path, LoadOptions.None);
        var settings = new List<PolicySettingInfo>();

        foreach (var scopeName in new[] { "Computer", "User" })
        {
            var scope = document.Descendants().FirstOrDefault(e => e.Name.LocalName == scopeName);
            if (scope is null)
            {
                continue;
            }

            foreach (var policy in scope.Descendants().Where(e => e.Name.LocalName == "Policy"))
            {
                var name = ChildValue(policy, "Name");
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                var state = ChildValue(policy, "State");
                var category = ChildValue(policy, "Category");
                var extension = policy.Ancestors()
                    .FirstOrDefault(e => e.Name.LocalName == "Extension")?
                    .Attribute("type")?.Value ?? "Administrative Templates";

                var values = policy.Descendants()
                    .Where(e => !e.HasElements)
                    .Where(e => IsUsefulValueElement(e.Name.LocalName))
                    .Select(e => e.Value.Trim())
                    .Where(v => !string.IsNullOrWhiteSpace(v))
                    .Where(v => !string.Equals(v, name, StringComparison.OrdinalIgnoreCase))
                    .Where(v => !string.Equals(v, state, StringComparison.OrdinalIgnoreCase))
                    .Where(v => !string.Equals(v, category, StringComparison.OrdinalIgnoreCase))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(32)
                    .ToArray();

                var key = DescendantValue(policy, "Key");
                var valueName = DescendantValue(policy, "ValueName");

                settings.Add(new PolicySettingInfo
                {
                    GpoId = gpo.Id,
                    GpoName = gpo.DisplayName,
                    Scope = scopeName,
                    Extension = extension,
                    Category = category,
                    SettingName = name,
                    State = state,
                    Value = string.Join("; ", values),
                    RegistryKey = key,
                    RegistryValue = valueName
                });
            }

            foreach (var extension in scope
                         .Descendants()
                         .Where(e => e.Name.LocalName == "Extension"))
            {
                settings.AddRange(ParseGenericExtensionSettings(
                    extension,
                    scopeName,
                    gpo,
                    settings));
            }
        }

        var securityTargets =
            settings
                .Where(item =>
                    item.Extension.Equals(
                        "SecuritySettings",
                        StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(
                        item.RegistryKey) &&
                    !string.IsNullOrWhiteSpace(
                        item.RegistryValue))
                .ToArray();

        return settings
            .Where(item =>
                !item.Extension.Equals(
                    "RegistrySettings",
                    StringComparison.OrdinalIgnoreCase) ||
                !securityTargets.Any(security =>
                    SameRegistryTarget(
                        security,
                        item)))
            .ToArray();
    }

    private static bool SameRegistryTarget(
        PolicySettingInfo left,
        PolicySettingInfo right)
    {
        return NormalizeRegistryTargetPath(
                   left.RegistryKey)
               .Equals(
                   NormalizeRegistryTargetPath(
                       right.RegistryKey),
                   StringComparison.OrdinalIgnoreCase) &&
               left.RegistryValue
                   .Trim()
                   .Equals(
                       right.RegistryValue.Trim(),
                       StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeRegistryTargetPath(
        string value)
    {
        var normalized =
            (value ?? string.Empty)
            .Trim()
            .Replace(
                '/',
                '\\');

        foreach (var prefix in new[]
                 {
                     "HKEY_LOCAL_MACHINE\\",
                     "HKLM\\",
                     "MACHINE\\",
                     "HKEY_CURRENT_USER\\",
                     "HKCU\\",
                     "USER\\"
                 })
        {
            if (!normalized.StartsWith(
                    prefix,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            normalized =
                normalized[prefix.Length..];

            break;
        }

        return normalized.Trim('\\');
    }

    private static IEnumerable<PolicySettingInfo> ParseGenericExtensionSettings(
        XElement extension,
        string scopeName,
        GpoInfo gpo,
        IReadOnlyCollection<PolicySettingInfo> existing)
    {
        var extensionType = GetExtensionType(extension);
        var rows = new List<PolicySettingInfo>();

        if (extensionType.Equals(
                "SecuritySettings",
                StringComparison.OrdinalIgnoreCase))
        {
            rows.AddRange(
                ParseSecurityOptions(
                    extension,
                    scopeName,
                    gpo));
        }

        if (extensionType.Equals(
                "RegistrySettings",
                StringComparison.OrdinalIgnoreCase))
        {
            rows.AddRange(
                ParseRegistrySettings(
                    extension,
                    scopeName,
                    gpo));

            return rows;
        }

        var candidates = extension
            .Descendants()
            .Where(e =>
                !e.Name.LocalName.Equals(
                    "SecurityOptions",
                    StringComparison.OrdinalIgnoreCase) &&
                !e.Ancestors().Any(a =>
                    a.Name.LocalName.Equals(
                        "SecurityOptions",
                        StringComparison.OrdinalIgnoreCase)))
            .Where(e => !e.Ancestors().Any(a => a.Name.LocalName == "Policy"))
            .Where(e => !e.DescendantsAndSelf().Any(a => a.Name.LocalName == "Policy"))
            .Where(IsGenericSettingCandidate)
            .ToArray();

        foreach (var element in candidates)
        {
            if (element.Ancestors()
                .TakeWhile(a => a != extension)
                .Any(a => IsPreferredOuterSettingNode(a)))
            {
                continue;
            }

            var settingName = GetGenericSettingName(element);
            if (string.IsNullOrWhiteSpace(settingName))
            {
                continue;
            }

            var category = string.IsNullOrWhiteSpace(extensionType)
                ? GetNamespaceTail(element.Name.NamespaceName)
                : extensionType;

            var state = GetDirectOrAttributeValue(element, "State");
            if (string.IsNullOrWhiteSpace(state))
            {
                var action = GetDirectOrAttributeValue(element, "action");
                state = string.IsNullOrWhiteSpace(action)
                    ? "Configured"
                    : $"Configured ({ExpandPreferenceAction(action)})";
            }

            var key = FirstNonEmpty(
                GetDirectOrAttributeValue(element, "Key"),
                GetDirectOrAttributeValue(element, "key"),
                FindNamedValue(element, "Key"),
                FindNamedAttributeValue(element, "Key"),
                FindNamedAttributeValue(element, "key"));

            var valueName = FirstNonEmpty(
                GetDirectOrAttributeValue(element, "ValueName"),
                GetDirectOrAttributeValue(element, "valueName"),
                FindNamedValue(element, "ValueName"),
                FindNamedValue(element, "Name"),
                FindNamedAttributeValue(element, "ValueName"),
                FindNamedAttributeValue(element, "valueName"),
                FindNamedAttributeValue(element, "Name"),
                FindNamedAttributeValue(element, "name"));

            var value = BuildGenericValueSummary(element, settingName, state);

            if (existing.Any(item =>
                    item.GpoId == gpo.Id &&
                    item.Scope.Equals(scopeName, StringComparison.OrdinalIgnoreCase) &&
                    item.SettingName.Equals(settingName, StringComparison.CurrentCultureIgnoreCase) &&
                    item.Category.Equals(category, StringComparison.CurrentCultureIgnoreCase) &&
                    item.Value.Equals(value, StringComparison.CurrentCultureIgnoreCase)) ||
                rows.Any(item =>
                    item.SettingName.Equals(settingName, StringComparison.CurrentCultureIgnoreCase) &&
                    item.Category.Equals(category, StringComparison.CurrentCultureIgnoreCase) &&
                    item.Value.Equals(value, StringComparison.CurrentCultureIgnoreCase)))
            {
                continue;
            }

            rows.Add(new PolicySettingInfo
            {
                GpoId = gpo.Id,
                GpoName = gpo.DisplayName,
                Scope = scopeName,
                Extension = extensionType,
                Category = category,
                SettingName = settingName,
                State = state,
                Value = value,
                RegistryKey = key,
                RegistryValue = valueName
            });
        }

        return rows;
    }

    private static IEnumerable<PolicySettingInfo> ParseRegistrySettings(
        XElement extension,
        string scopeName,
        GpoInfo gpo)
    {
        foreach (var registrySetting in extension
                     .Descendants()
                     .Where(element =>
                         element.Name.LocalName.Equals(
                             "RegistrySetting",
                             StringComparison.OrdinalIgnoreCase)))
        {
            var keyPath =
                FirstNonEmpty(
                    ChildValue(
                        registrySetting,
                        "KeyPath"),
                    DescendantValue(
                        registrySetting,
                        "KeyPath"));

            var admSetting =
                FirstNonEmpty(
                    ChildValue(
                        registrySetting,
                        "AdmSetting"),
                    DescendantValue(
                        registrySetting,
                        "AdmSetting"));

            var values =
                registrySetting
                    .Descendants()
                    .Where(element =>
                        element.Name.LocalName.Equals(
                            "Value",
                            StringComparison.OrdinalIgnoreCase))
                    .ToArray();

            if (values.Length == 0)
            {
                yield return new PolicySettingInfo
                {
                    GpoId = gpo.Id,
                    GpoName = gpo.DisplayName,
                    Scope = scopeName,
                    Extension = "RegistrySettings",
                    Category =
                        IsFalseText(
                            admSetting)
                            ? "Registry > Extra Registry Settings"
                            : "Registry",
                    SettingName =
                        string.IsNullOrWhiteSpace(
                            keyPath)
                            ? "Registry setting"
                            : $"Registry: {keyPath}",
                    State = "Configured",
                    Value =
                        string.IsNullOrWhiteSpace(
                            admSetting)
                            ? string.Empty
                            : $"AdmSetting={admSetting}",
                    RegistryKey = keyPath,
                    RegistryValue = string.Empty
                };

                continue;
            }

            foreach (var valueElement in values)
            {
                var valueName =
                    FirstNonEmpty(
                        ChildValue(
                            valueElement,
                            "Name"),
                        DescendantValue(
                            valueElement,
                            "Name"));

                var valueParts =
                    valueElement
                        .Descendants()
                        .Where(element =>
                            !element.HasElements &&
                            !element.Name.LocalName.Equals(
                                "Name",
                                StringComparison.OrdinalIgnoreCase))
                        .Select(element =>
                            $"{element.Name.LocalName}={element.Value.Trim()}")
                        .Where(part =>
                            !part.EndsWith(
                                "=",
                                StringComparison.Ordinal))
                        .Distinct(
                            StringComparer.CurrentCultureIgnoreCase)
                        .Take(16)
                        .ToArray();

                var summary =
                    new List<string>();

                if (!string.IsNullOrWhiteSpace(
                        admSetting))
                {
                    summary.Add(
                        $"AdmSetting={admSetting}");
                }

                summary.AddRange(
                    valueParts);

                yield return new PolicySettingInfo
                {
                    GpoId = gpo.Id,
                    GpoName = gpo.DisplayName,
                    Scope = scopeName,
                    Extension = "RegistrySettings",
                    Category =
                        IsFalseText(
                            admSetting)
                            ? "Registry > Extra Registry Settings"
                            : "Registry",
                    SettingName =
                        string.IsNullOrWhiteSpace(
                            valueName)
                            ? string.IsNullOrWhiteSpace(
                                keyPath)
                                ? "Registry setting"
                                : $"Registry: {keyPath}"
                            : $"Registry: {valueName}",
                    State = "Configured",
                    Value =
                        string.Join(
                            "; ",
                            summary),
                    RegistryKey = keyPath,
                    RegistryValue = valueName
                };
            }
        }
    }

    private static bool IsFalseText(
        string value) =>
        value.Equals(
            "false",
            StringComparison.OrdinalIgnoreCase) ||
        value.Equals(
            "0",
            StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<PolicySettingInfo> ParseSecurityOptions(
        XElement extension,
        string scopeName,
        GpoInfo gpo)
    {
        foreach (var option in extension
                     .Descendants()
                     .Where(element =>
                         element.Name.LocalName.Equals(
                             "SecurityOptions",
                             StringComparison.OrdinalIgnoreCase)))
        {
            var keyName =
                FindNamedValue(
                    option,
                    "KeyName");

            var display =
                option.Descendants()
                    .FirstOrDefault(element =>
                        element.Name.LocalName.Equals(
                            "Display",
                            StringComparison.OrdinalIgnoreCase));

            var displayName =
                display?.Descendants()
                    .FirstOrDefault(element =>
                        element.Name.LocalName.Equals(
                            "Name",
                            StringComparison.OrdinalIgnoreCase))
                    ?.Value.Trim()
                ?? string.Empty;

            var displayValue =
                FirstNonEmpty(
                    display?.Descendants()
                        .FirstOrDefault(element =>
                            element.Name.LocalName.Equals(
                                "DisplayString",
                                StringComparison.OrdinalIgnoreCase))
                        ?.Value.Trim()
                        ?? string.Empty,
                    display?.Descendants()
                        .FirstOrDefault(element =>
                            element.Name.LocalName.Equals(
                                "DisplayBoolean",
                                StringComparison.OrdinalIgnoreCase))
                        ?.Value.Trim()
                        ?? string.Empty,
                    display?.Descendants()
                        .FirstOrDefault(element =>
                            element.Name.LocalName.Equals(
                                "DisplayNumber",
                                StringComparison.OrdinalIgnoreCase))
                        ?.Value.Trim()
                        ?? string.Empty,
                    FindNamedValue(
                        option,
                        "SettingNumber"));

            var units =
                display?.Descendants()
                    .FirstOrDefault(element =>
                        element.Name.LocalName.Equals(
                            "Units",
                            StringComparison.OrdinalIgnoreCase))
                    ?.Value.Trim()
                ?? string.Empty;

            if (!string.IsNullOrWhiteSpace(units) &&
                !string.IsNullOrWhiteSpace(displayValue))
            {
                displayValue =
                    $"{displayValue} {units}";
            }

            SplitSecurityRegistryTarget(
                keyName,
                out var registryKey,
                out var registryValue);

            yield return new PolicySettingInfo
            {
                GpoId = gpo.Id,
                GpoName = gpo.DisplayName,
                Scope = scopeName,
                Extension = "SecuritySettings",
                Category =
                    "Security Settings > Local Policies > Security Options",
                SettingName =
                    FirstNonEmpty(
                        displayName,
                        registryValue,
                        keyName,
                        "Security option"),
                State = "Configured",
                Value = displayValue,
                RegistryKey = registryKey,
                RegistryValue = registryValue
            };
        }
    }

    private static void SplitSecurityRegistryTarget(
        string keyName,
        out string registryKey,
        out string registryValue)
    {
        registryKey =
            string.Empty;
        registryValue =
            string.Empty;

        if (string.IsNullOrWhiteSpace(keyName))
        {
            return;
        }

        var normalized =
            keyName.Trim();

        foreach (var prefix in new[]
                 {
                     "MACHINE\\",
                     "USER\\"
                 })
        {
            if (!normalized.StartsWith(
                    prefix,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            normalized =
                normalized[prefix.Length..];

            break;
        }

        var separator =
            normalized.LastIndexOf('\\');

        if (separator <= 0 ||
            separator >= normalized.Length - 1)
        {
            registryKey =
                normalized;

            return;
        }

        registryKey =
            normalized[..separator];

        registryValue =
            normalized[(separator + 1)..];
    }

    private static bool IsGenericSettingCandidate(XElement element)
    {
        var localName = element.Name.LocalName;

        if (localName is "Extension" or "ExtensionData" or "Properties" or "Filters" or
            "Filter" or "GPOSettingOrder" or "Policy" or "Name" or "State" or
            "Category" or "Explain" or "Supported")
        {
            return false;
        }

        if (localName.EndsWith("Settings", StringComparison.OrdinalIgnoreCase) &&
            !element.Elements().Any(child => !child.HasElements))
        {
            return false;
        }

        var meaningfulAttributes = element.Attributes()
            .Where(a => !a.IsNamespaceDeclaration)
            .Where(a => a.Name.LocalName is not ("clsid" or "uid" or "changed" or "image"))
            .ToArray();

        var directLeaves = element.Elements()
            .Where(child => !child.HasElements)
            .Where(child => !string.IsNullOrWhiteSpace(child.Value))
            .ToArray();

        var hasPropertiesChild = element.Elements()
            .Any(child => child.Name.LocalName == "Properties");

        var hasIdentityAttribute = element.Attributes()
            .Any(a => a.Name.LocalName.Equals("name", StringComparison.OrdinalIgnoreCase) ||
                      a.Name.LocalName.Equals("status", StringComparison.OrdinalIgnoreCase) ||
                      a.Name.LocalName.Equals("path", StringComparison.OrdinalIgnoreCase) ||
                      a.Name.LocalName.Equals("key", StringComparison.OrdinalIgnoreCase) ||
                      a.Name.LocalName.Equals("valueName", StringComparison.OrdinalIgnoreCase));

        return hasPropertiesChild ||
               hasIdentityAttribute ||
               meaningfulAttributes.Length > 0 ||
               directLeaves.Length >= 2;
    }

    private static bool IsPreferredOuterSettingNode(XElement element)
    {
        if (element.Name.LocalName is "Extension" or "ExtensionData")
        {
            return false;
        }

        return element.Elements().Any(child => child.Name.LocalName == "Properties") ||
               element.Attributes().Any(a =>
                   a.Name.LocalName.Equals("name", StringComparison.OrdinalIgnoreCase) ||
                   a.Name.LocalName.Equals("status", StringComparison.OrdinalIgnoreCase));
    }

    private static string GetGenericSettingName(XElement element)
    {
        var name = FirstNonEmpty(
            GetDirectOrAttributeValue(element, "name"),
            GetDirectOrAttributeValue(element, "Name"),
            GetDirectOrAttributeValue(element, "status"),
            GetDirectOrAttributeValue(element, "displayName"),
            GetDirectOrAttributeValue(element, "label"));

        if (!string.IsNullOrWhiteSpace(name))
        {
            return $"{HumanizeElementName(element.Name.LocalName)}: {name}";
        }

        var key = FindNamedValue(element, "KeyName");
        if (!string.IsNullOrWhiteSpace(key))
        {
            return $"{HumanizeElementName(element.Name.LocalName)}: {key}";
        }

        return HumanizeElementName(element.Name.LocalName);
    }

    private static string BuildGenericValueSummary(
        XElement element,
        string settingName,
        string state)
    {
        var parts = new List<string>();

        foreach (var attribute in element
                     .DescendantsAndSelf()
                     .SelectMany(e => e.Attributes())
                     .Where(a => !a.IsNamespaceDeclaration)
                     .Where(a => a.Name.LocalName is not ("clsid" or "uid" or "image"))
                     .Take(64))
        {
            var value = attribute.Value.Trim();
            if (string.IsNullOrWhiteSpace(value) ||
                value.Equals(settingName, StringComparison.CurrentCultureIgnoreCase) ||
                value.Equals(state, StringComparison.CurrentCultureIgnoreCase))
            {
                continue;
            }

            parts.Add($"{attribute.Name.LocalName}={value}");
        }

        foreach (var leaf in element
                     .Descendants()
                     .Where(e => !e.HasElements)
                     .Where(e => !string.IsNullOrWhiteSpace(e.Value))
                     .Take(64))
        {
            var value = leaf.Value.Trim();
            if (value.Equals(settingName, StringComparison.CurrentCultureIgnoreCase) ||
                value.Equals(state, StringComparison.CurrentCultureIgnoreCase))
            {
                continue;
            }

            parts.Add($"{leaf.Name.LocalName}={value}");
        }

        return string.Join("; ",
            parts.Distinct(StringComparer.CurrentCultureIgnoreCase).Take(40));
    }

    private static string GetExtensionType(XElement extension)
    {
        var type = extension.Attributes()
            .FirstOrDefault(a => a.Name.LocalName == "type")?
            .Value;

        if (!string.IsNullOrWhiteSpace(type))
        {
            var colon = type.LastIndexOf(':');
            return colon >= 0 ? type[(colon + 1)..] : type;
        }

        var first = extension.Elements().FirstOrDefault();
        return first is null
            ? "Other"
            : FirstNonEmpty(
                GetNamespaceTail(first.Name.NamespaceName),
                HumanizeElementName(first.Name.LocalName));
    }

    private static string GetNamespaceTail(string namespaceName)
    {
        if (string.IsNullOrWhiteSpace(namespaceName))
        {
            return string.Empty;
        }

        var trimmed = namespaceName.TrimEnd('/');
        var index = trimmed.LastIndexOf('/');
        return index >= 0 ? trimmed[(index + 1)..] : trimmed;
    }

    private static string GetDirectOrAttributeValue(XElement element, string name)
    {
        var attribute = element.Attributes()
            .FirstOrDefault(a => a.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (attribute is not null)
        {
            return attribute.Value.Trim();
        }

        return element.Elements()
            .FirstOrDefault(e => e.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase))?
            .Value.Trim() ?? string.Empty;
    }

    private static string FindNamedValue(XElement element, string name)
    {
        return element.Descendants()
            .FirstOrDefault(e => e.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase))?
            .Value.Trim() ?? string.Empty;
    }

    private static string FindNamedAttributeValue(
        XElement element,
        string name)
    {
        return element
            .DescendantsAndSelf()
            .SelectMany(
                current =>
                    current.Attributes())
            .FirstOrDefault(
                attribute =>
                    !attribute.IsNamespaceDeclaration &&
                    attribute.Name.LocalName.Equals(
                        name,
                        StringComparison.OrdinalIgnoreCase))
            ?.Value.Trim()
            ?? string.Empty;
    }

    private static string FirstNonEmpty(params string[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;

    private static string ExpandPreferenceAction(string action) =>
        action.ToUpperInvariant() switch
        {
            "C" => "Create",
            "R" => "Replace",
            "U" => "Update",
            "D" => "Delete",
            _ => action
        };

    private static string HumanizeElementName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        var builder = new System.Text.StringBuilder(value.Length + 8);
        for (var i = 0; i < value.Length; i++)
        {
            var ch = value[i];
            if (i > 0 && char.IsUpper(ch) && !char.IsUpper(value[i - 1]))
            {
                builder.Append(' ');
            }

            builder.Append(ch);
        }

        return builder.ToString();
    }

    private static bool IsUsefulValueElement(string localName)
    {
        return localName is not ("Name" or "State" or "Explain" or "Supported" or "Category" or "Presentation");
    }

    private static string ChildValue(XElement element, string localName)
        => element.Elements().FirstOrDefault(e => e.Name.LocalName == localName)?.Value.Trim() ?? string.Empty;

    private static string DescendantValue(XElement element, string localName)
        => element.Descendants().FirstOrDefault(e => e.Name.LocalName == localName)?.Value.Trim() ?? string.Empty;

    private static DateTime? TryConvertDateTime(object? value)
    {
        if (value is null)
        {
            return null;
        }

        try
        {
            return Convert.ToDateTime(value);
        }
        catch
        {
            return null;
        }
    }

    private static string SafeString(Func<object> getter)
    {
        try
        {
            return Convert.ToString(
                       getter())
                   ?? string.Empty;
        }
        catch (Exception ex)
            when (!IsFatal(
                ex))
        {
            return string.Empty;
        }
    }

    private static int SafeInt(Func<object> getter)
    {
        try
        {
            return Convert.ToInt32(getter());
        }
        catch (Exception ex)
            when (!IsFatal(
                ex))
        {
            return 0;
        }
    }

    private static bool SafeBool(Func<object> getter)
    {
        try
        {
            return Convert.ToBoolean(getter());
        }
        catch (Exception ex)
            when (!IsFatal(
                ex))
        {
            return false;
        }
    }

    private static bool IsFatal(
        Exception exception) =>
        exception is OutOfMemoryException or
                     StackOverflowException or
                     AccessViolationException;
}

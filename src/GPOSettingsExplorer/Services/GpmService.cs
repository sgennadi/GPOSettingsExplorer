using System.IO;
using System.Diagnostics;
using System.Runtime.InteropServices;
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
        dynamic domain = gpm.GetDomain(domainName, string.Empty, constants.UseAnyDC);
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
        dynamic domain = gpm.GetDomain(domainName, string.Empty, constants.UseAnyDC);

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

    public void OpenEditor(GpoInfo gpo, string domainDistinguishedName)
    {
        var objectPath = $"LDAP://CN={gpo.Id:B},CN=Policies,CN=System,{domainDistinguishedName}";
        var arguments = $"gpme.msc /gpobject:\"{objectPath}\"";

        Process.Start(new ProcessStartInfo
        {
            FileName = "mmc.exe",
            Arguments = arguments,
            UseShellExecute = true
        });
    }

    public Guid CreateGpo(string domainName, string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            throw new ArgumentException("GPO name cannot be empty.", nameof(displayName));
        }

        dynamic gpm = CreateGpm();
        dynamic constants = gpm.GetConstants();
        dynamic domain = gpm.GetDomain(domainName, string.Empty, constants.UseAnyDC);
        dynamic gpo = domain.CreateGPO();
        gpo.DisplayName = displayName.Trim();

        string idText = Convert.ToString((object?)gpo.ID) ?? string.Empty;
        if (!Guid.TryParse(idText, out Guid id))
        {
            throw new InvalidOperationException("The GPO was created, but its GUID could not be read.");
        }

        return id;
    }

    public void RenameGpo(string domainName, Guid gpoId, string newDisplayName)
    {
        if (string.IsNullOrWhiteSpace(newDisplayName))
        {
            throw new ArgumentException("GPO name cannot be empty.", nameof(newDisplayName));
        }

        dynamic gpm = CreateGpm();
        dynamic constants = gpm.GetConstants();
        dynamic domain = gpm.GetDomain(domainName, string.Empty, constants.UseAnyDC);
        dynamic gpo = domain.GetGPO(gpoId.ToString("B"));
        gpo.DisplayName = newDisplayName.Trim();
    }

    public void DeleteGpo(string domainName, Guid gpoId)
    {
        dynamic gpm = CreateGpm();
        dynamic constants = gpm.GetConstants();
        dynamic domain = gpm.GetDomain(domainName, string.Empty, constants.UseAnyDC);
        dynamic gpo = domain.GetGPO(gpoId.ToString("B"));
        gpo.Delete();
    }

    public void SetComputerEnabled(string domainName, Guid gpoId, bool enabled)
    {
        dynamic gpm = CreateGpm();
        dynamic constants = gpm.GetConstants();
        dynamic domain = gpm.GetDomain(domainName, string.Empty, constants.UseAnyDC);
        dynamic gpo = domain.GetGPO(gpoId.ToString("B"));
        gpo.SetComputerEnabled(enabled);
    }

    public void SetUserEnabled(string domainName, Guid gpoId, bool enabled)
    {
        dynamic gpm = CreateGpm();
        dynamic constants = gpm.GetConstants();
        dynamic domain = gpm.GetDomain(domainName, string.Empty, constants.UseAnyDC);
        dynamic gpo = domain.GetGPO(gpoId.ToString("B"));
        gpo.SetUserEnabled(enabled);
    }

    public IReadOnlyList<GpoPermissionInfo> LoadPermissions(string domainName, Guid gpoId)
    {
        dynamic gpm = CreateGpm();
        dynamic constants = gpm.GetConstants();
        dynamic domain = gpm.GetDomain(domainName, string.Empty, constants.UseAnyDC);
        dynamic gpo = domain.GetGPO(gpoId.ToString("B"));
        dynamic security = gpo.GetSecurityInfo();

        var gpoName = Convert.ToString((object?)gpo.DisplayName) ?? gpoId.ToString("B");
        var result = new List<GpoPermissionInfo>();
        var count = Convert.ToInt32((object?)security.Count);

        for (var i = 1; i <= count; i++)
        {
            dynamic permission = security.Item(i);
            dynamic trustee = permission.Trustee;

            var rawPermission = Convert.ToInt32((object?)permission.Permission);
            result.Add(new GpoPermissionInfo
            {
                GpoId = gpoId,
                GpoName = gpoName,
                TrusteeName = Convert.ToString((object?)trustee.TrusteeName) ?? string.Empty,
                TrusteeDomain = Convert.ToString((object?)trustee.TrusteeDomain) ?? string.Empty,
                TrusteeSid = Convert.ToString((object?)trustee.TrusteeSid) ?? string.Empty,
                TrusteeDsPath = Convert.ToString((object?)trustee.TrusteeDSPath) ?? string.Empty,
                TrusteeType = SafeInt(() => trustee.TrusteeType),
                Level = MapPermissionLevel(rawPermission),
                RawPermission = rawPermission,
                Denied = SafeBool(() => permission.Denied),
                Inherited = SafeBool(() => permission.Inherited),
                Inheritable = SafeBool(() => permission.Inheritable)
            });
        }

        return result
            .OrderBy(p => p.Category, StringComparer.OrdinalIgnoreCase)
            .ThenBy(p => p.TrusteeDisplay, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(p => p.RawPermission)
            .ToArray();
    }

    public void AddPermission(
        string domainName,
        Guid gpoId,
        string trustee,
        GpoPermissionLevel level)
    {
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
        dynamic domain = gpm.GetDomain(domainName, string.Empty, constants.UseAnyDC);
        dynamic gpo = domain.GetGPO(gpoId.ToString("B"));
        dynamic security = gpo.GetSecurityInfo();

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
        dynamic domain = gpm.GetDomain(domainName, string.Empty, constants.UseAnyDC);
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

        security.Remove(current);

        var trustee = string.IsNullOrWhiteSpace(existing.TrusteeSid)
            ? existing.TrusteeDisplay
            : existing.TrusteeSid;

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
        if (existing.Inherited)
        {
            throw new InvalidOperationException(
                "Inherited permissions cannot be removed from this GPO. Change them on the parent object.");
        }

        dynamic gpm = CreateGpm();
        dynamic constants = gpm.GetConstants();
        dynamic domain = gpm.GetDomain(domainName, string.Empty, constants.UseAnyDC);
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

        security.Remove(current);
        gpo.SetSecurityInfo(security);
    }

    public string BackupGpo(string domainName, Guid gpoId, string comment)
    {
        dynamic gpm = CreateGpm();
        dynamic constants = gpm.GetConstants();
        dynamic domain = gpm.GetDomain(domainName, string.Empty, constants.UseAnyDC);
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
        dynamic gpm = CreateGpm();
        dynamic constants = gpm.GetConstants();
        dynamic domain = gpm.GetDomain(domainName, string.Empty, constants.UseAnyDC);
        dynamic gpo = domain.GetGPO(gpoId.ToString("B"));

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

    private static string SanitizeFileName(string value)
    {
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            value = value.Replace(invalid, '_');
        }

        return value;
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
        }

        return settings;
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

    private static int SafeInt(Func<object> getter)
    {
        try
        {
            return Convert.ToInt32(getter());
        }
        catch (COMException)
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
        catch (COMException)
        {
            return false;
        }
    }
}

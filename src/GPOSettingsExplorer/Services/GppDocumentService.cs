using System.Runtime.InteropServices;
using System.Text;
using System.Xml.Linq;
using GPOSettingsExplorer.Models;

namespace GPOSettingsExplorer.Services;

public sealed class GppDocumentService
{
    private const uint GpoOpenLoadRegistry = 0x00000001;

    private static readonly GppDocumentTypeInfo[] KnownTypes =
    {
        CreateTypeInfo("Applications", @"Applications\Applications.xml",
            "F9C77450-3A41-477E-9310-9ACD617BD9E3",
            "0DA274B5-EB93-47A7-AAFB-65BA532D3FE6"),

        CreateTypeInfo("Data Sources", @"DataSources\DataSources.xml",
            "728EE579-943C-4519-9EF7-AB56765798ED",
            "1612B55C-243C-48DD-A449-FFC097B19776"),

        CreateTypeInfo("Devices", @"Devices\Devices.xml",
            "1A6364EB-776B-4120-ADE1-B63A406A76B5",
            "1B767E9A-7BE4-4D35-85C1-2E174A7BA951"),

        CreateTypeInfo("Drive Maps", @"Drives\Drives.xml",
            "5794DAFD-BE60-433F-88A2-1A31939AC01F",
            "2EA1A81B-48E5-45E9-8BB7-A6E3AC170006"),

        CreateTypeInfo("Environment Variables", @"EnvironmentVariables\EnvironmentVariables.xml",
            "0E28E245-9368-4853-AD84-6DA3BA35BB75",
            "35141B6B-498A-4CC7-AD59-CEF93D89B2CE"),

        CreateTypeInfo("Files", @"Files\Files.xml",
            "7150F9BF-48AD-4DA4-A49C-29EF4A8369BA",
            "3BAE7E51-E3F4-41D0-853D-9BB9FD47605F"),

        CreateTypeInfo("Folder Options", @"FolderOptions\FolderOptions.xml",
            "A3F3E39B-5D83-4940-B954-28315B82F0A8",
            "3BFAE46A-7F3A-467B-8CEA-6AA34DC71F53"),

        CreateTypeInfo("Folders", @"Folders\Folders.xml",
            "6232C319-91AC-4931-9385-E70C2B099F0E",
            "3EC4E9D3-714D-471F-88DC-4DD4471AAB47"),

        CreateTypeInfo("INI Files", @"IniFiles\IniFiles.xml",
            "74EE6C03-5363-4554-B161-627540339CAB",
            "516FC620-5D34-4B08-8165-6A06B623EDEB"),

        CreateTypeInfo("Internet Settings", @"InternetSettings\InternetSettings.xml",
            "E47248BA-94CC-49C4-BBB5-9EB7F05183D0",
            "5C935941-A954-4F7C-B507-885941ECE5C4"),

        CreateTypeInfo("Local Users and Groups", @"Groups\Groups.xml",
            "17D89FEC-5C44-4972-B12D-241CAEF74509",
            "79F92669-4224-476C-9C5C-6EFB4D87DF4A"),

        CreateTypeInfo("Network Options", @"NetworkOptions\NetworkOptions.xml",
            "3A0DBA37-F8B2-4356-83DE-3E90BD5C261F",
            "949FB894-E883-42C6-88C1-29169720E8CA"),

        CreateTypeInfo("Network Shares", @"NetworkShares\NetworkShares.xml",
            "6A4C88C6-C502-4F74-8F60-2CB23EDC24E2",
            "BFCBBEB0-9DF4-4C0C-A728-434EA66A0373"),

        CreateTypeInfo("Power Options", @"PowerOptions\PowerOptions.xml",
            "E62688F0-25FD-4C90-BFF5-F508B9D2E31F",
            "9AD2BAFE-63B4-4883-A08C-C3C6196BCAFD"),

        CreateTypeInfo("Printers", @"Printers\Printers.xml",
            "BC75B1ED-5833-4858-9BB8-CBF0B166DF9D",
            "A8C42CEA-CDB8-4388-97F4-5831F933DA84"),

        CreateTypeInfo("Regional Options", @"RegionalOptions\RegionalOptions.xml",
            "E5094040-C46C-4115-B030-04FB2E545B00",
            "B9CCA4DE-E2B9-4CBD-BF7D-11B6EBFBDDF7"),

        CreateTypeInfo("Registry", @"Registry\Registry.xml",
            "B087BE9D-ED37-454F-AF9C-04291E351182",
            "BEE07A6A-EC9F-4659-B8C9-0B1937907C83"),

        CreateTypeInfo("Scheduled Tasks", @"ScheduledTasks\ScheduledTasks.xml",
            "AADCED64-746C-4633-A97C-D61349046527",
            "CAB54552-DEEA-4691-817E-ED4A4D1AFC72"),

        CreateTypeInfo("Services", @"Services\Services.xml",
            "91FBB303-0CD5-4055-BF42-E512A681B325",
            "CC5746A9-9B74-4BE5-AE2E-64379C86E0E4"),

        CreateTypeInfo("Shortcuts", @"Shortcuts\Shortcuts.xml",
            "C418DD9D-0D14-4EFB-8FBF-CFE535C8FAC7",
            "CEFFA6E2-E3BD-421B-852C-6F6A79A59BC1"),

        CreateTypeInfo("Start Menu and Taskbar", @"StartMenuTaskbar\StartMenuTaskbar.xml",
            "E4F48E54-F38D-4884-BFB9-D4D2E5729C18",
            "CF848D48-888D-4F45-B530-6A201E62A605")
    };

    public IReadOnlyList<GppDocumentTypeInfo> GetKnownTypes() => KnownTypes;

    public IReadOnlyList<GppDocumentInfo> Load(
        IEnumerable<GpoInfo> gpos,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var list = gpos.ToList();
        var result = new List<GppDocumentInfo>();

        for (var index = 0; index < list.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var gpo = list[index];
            progress?.Report($"GPP XML {index + 1}/{list.Count}: {gpo.DisplayName}");

            foreach (var scope in new[] { "Computer", "User" })
            {
                foreach (var type in KnownTypes)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var path = GetXmlPath(gpo, scope, type.RelativePath);
                    if (!File.Exists(path))
                        continue;

                    try
                    {
                        result.Add(ReadInfo(gpo, scope, type, path));
                    }
                    catch
                    {
                        // A malformed preference file is still shown so the admin can repair it.
                        var info = new FileInfo(path);
                        result.Add(new GppDocumentInfo
                        {
                            GpoId = gpo.Id,
                            GpoName = gpo.DisplayName,
                            DomainName = gpo.DomainName,
                            Scope = scope,
                            PreferenceType = type.Name,
                            RelativePath = type.RelativePath,
                            XmlPath = path,
                            CseGuid = type.CseGuid,
                            ToolGuid = type.ToolGuid,
                            RootElement = "<invalid XML>",
                            ItemCount = 0,
                            SizeBytes = info.Exists ? info.Length : 0,
                            LastWriteTime = info.Exists ? info.LastWriteTime : null
                        });
                    }
                }
            }
        }

        return result
            .OrderBy(item => item.GpoName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(item => item.Scope, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.PreferenceType, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    public string ReadXml(GppDocumentInfo document)
    {
        if (!File.Exists(document.XmlPath))
            throw new FileNotFoundException(
                "The Group Policy Preferences XML file no longer exists.",
                document.XmlPath);

        return File.ReadAllText(document.XmlPath);
    }

    public GppDocumentInfo BuildTarget(
        GpoInfo gpo,
        string scope,
        GppDocumentTypeInfo type)
    {
        var path = GetXmlPath(gpo, scope, type.RelativePath);

        return new GppDocumentInfo
        {
            GpoId = gpo.Id,
            GpoName = gpo.DisplayName,
            DomainName = gpo.DomainName,
            Scope = scope,
            PreferenceType = type.Name,
            RelativePath = type.RelativePath,
            XmlPath = path,
            CseGuid = type.CseGuid,
            ToolGuid = type.ToolGuid
        };
    }

    public void SaveXml(
        GpoInfo gpo,
        string domainDistinguishedName,
        GppDocumentInfo document,
        string xml)
    {
        if (string.IsNullOrWhiteSpace(xml))
            throw new InvalidOperationException("XML cannot be empty.");

        XDocument parsed;
        try
        {
            parsed = XDocument.Parse(xml, LoadOptions.PreserveWhitespace);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"The XML is invalid: {ex.Message}",
                ex);
        }

        if (parsed.Root is null)
            throw new InvalidOperationException("The XML document has no root element.");

        var path = GetXmlPath(gpo, document.Scope, document.RelativePath);
        var directory = Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException("Invalid Group Policy Preferences path.");

        Directory.CreateDirectory(directory);

        byte[]? original = File.Exists(path)
            ? File.ReadAllBytes(path)
            : null;

        try
        {
            WriteXml(path, parsed);
            CommitExtension(
                gpo,
                domainDistinguishedName,
                document.Scope,
                document.CseGuid,
                document.ToolGuid,
                add: true);
        }
        catch
        {
            RestoreFile(path, original);
            throw;
        }
    }

    public void Delete(
        GpoInfo gpo,
        string domainDistinguishedName,
        GppDocumentInfo document)
    {
        var path = GetXmlPath(gpo, document.Scope, document.RelativePath);
        if (!File.Exists(path))
            return;

        var original = File.ReadAllBytes(path);

        try
        {
            File.Delete(path);
            TryDeleteEmptyParents(path);

            CommitExtension(
                gpo,
                domainDistinguishedName,
                document.Scope,
                document.CseGuid,
                document.ToolGuid,
                add: false);
        }
        catch
        {
            RestoreFile(path, original);
            throw;
        }
    }

    public string Validate(string xml)
    {
        if (string.IsNullOrWhiteSpace(xml))
            return "XML is empty.";

        try
        {
            var document = XDocument.Parse(xml, LoadOptions.PreserveWhitespace);
            if (document.Root is null)
                return "XML has no root element.";

            return $"Valid XML. Root: <{document.Root.Name.LocalName}>; " +
                   $"elements: {document.Descendants().Count():N0}.";
        }
        catch (Exception ex)
        {
            return "Invalid XML: " + ex.Message;
        }
    }

    private static GppDocumentInfo ReadInfo(
        GpoInfo gpo,
        string scope,
        GppDocumentTypeInfo type,
        string path)
    {
        var document = XDocument.Load(path, LoadOptions.PreserveWhitespace);
        var file = new FileInfo(path);

        return new GppDocumentInfo
        {
            GpoId = gpo.Id,
            GpoName = gpo.DisplayName,
            DomainName = gpo.DomainName,
            Scope = scope,
            PreferenceType = type.Name,
            RelativePath = type.RelativePath,
            XmlPath = path,
            CseGuid = type.CseGuid,
            ToolGuid = type.ToolGuid,
            RootElement = document.Root?.Name.LocalName ?? string.Empty,
            ItemCount = document.Root?.Elements().Count() ?? 0,
            SizeBytes = file.Length,
            LastWriteTime = file.LastWriteTime
        };
    }

    private static string GetXmlPath(
        GpoInfo gpo,
        string scope,
        string relativePath)
    {
        var side = scope.Equals(
            "User",
            StringComparison.OrdinalIgnoreCase)
            ? "User"
            : "Machine";

        return Path.Combine(
            $@"\\{gpo.DomainName}\SYSVOL\{gpo.DomainName}\Policies\{gpo.Id:B}",
            side,
            "Preferences",
            relativePath);
    }

    private static void WriteXml(string path, XDocument document)
    {
        var directory = Path.GetDirectoryName(path)!;
        var temp = Path.Combine(
            directory,
            $".GppXml.{Guid.NewGuid():N}.tmp");

        try
        {
            var settings = new System.Xml.XmlWriterSettings
            {
                Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                Indent = true,
                NewLineChars = Environment.NewLine,
                NewLineHandling = System.Xml.NewLineHandling.Replace
            };

            using (var writer = System.Xml.XmlWriter.Create(temp, settings))
                document.Save(writer);

            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            try { File.Delete(temp); } catch { }
        }
    }

    private static void RestoreFile(string path, byte[]? original)
    {
        try
        {
            if (original is null)
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllBytes(path, original);
            }
        }
        catch
        {
        }
    }

    private static void TryDeleteEmptyParents(string filePath)
    {
        try
        {
            var directory = Path.GetDirectoryName(filePath);

            for (var i = 0; i < 2 && !string.IsNullOrWhiteSpace(directory); i++)
            {
                if (!Directory.Exists(directory) ||
                    Directory.EnumerateFileSystemEntries(directory).Any())
                    break;

                Directory.Delete(directory);
                directory = Path.GetDirectoryName(directory);
            }
        }
        catch
        {
        }
    }

    private static void CommitExtension(
        GpoInfo gpo,
        string domainDistinguishedName,
        string scope,
        Guid cseGuid,
        Guid toolGuid,
        bool add)
    {
        var comType = Type.GetTypeFromCLSID(
            new Guid("EA502722-A23D-11D1-A7D3-0000F87571E3"))
            ?? throw new InvalidOperationException(
                "Windows Group Policy API is unavailable.");

        var raw = Activator.CreateInstance(comType)
            ?? throw new InvalidOperationException(
                "Unable to create the Windows Group Policy object.");

        var policyObject = (IGroupPolicyObject)raw;

        try
        {
            var ldapPath =
                $"LDAP://CN={gpo.Id:B},CN=Policies,CN=System,{domainDistinguishedName}";

            ThrowIfFailed(
                policyObject.OpenDSGPO(
                    ldapPath,
                    GpoOpenLoadRegistry));

            var machine = !scope.Equals(
                "User",
                StringComparison.OrdinalIgnoreCase);

            ThrowIfFailed(
                policyObject.Save(
                    machine,
                    add,
                    ref cseGuid,
                    ref toolGuid));
        }
        finally
        {
            Marshal.FinalReleaseComObject(policyObject);
        }
    }

    private static GppDocumentTypeInfo Type(
        string name,
        string relativePath,
        string cse,
        string tool) =>
        new()
        {
            Name = name,
            RelativePath = relativePath,
            CseGuid = Guid.Parse(cse),
            ToolGuid = Guid.Parse(tool)
        };

    private static void ThrowIfFailed(int hresult)
    {
        if (hresult < 0)
            Marshal.ThrowExceptionForHR(hresult);
    }

    [ComImport]
    [Guid("EA502723-A23D-11D1-A7D3-0000F87571E3")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IGroupPolicyObject
    {
        [PreserveSig]
        int New(
            [MarshalAs(UnmanagedType.LPWStr)] string domainName,
            [MarshalAs(UnmanagedType.LPWStr)] string displayName,
            uint flags);

        [PreserveSig]
        int OpenDSGPO(
            [MarshalAs(UnmanagedType.LPWStr)] string path,
            uint flags);

        [PreserveSig]
        int OpenLocalMachineGPO(uint flags);

        [PreserveSig]
        int OpenRemoteMachineGPO(
            [MarshalAs(UnmanagedType.LPWStr)] string computerName,
            uint flags);

        [PreserveSig]
        int Save(
            [MarshalAs(UnmanagedType.Bool)] bool machine,
            [MarshalAs(UnmanagedType.Bool)] bool add,
            ref Guid extensionGuid,
            ref Guid snapInGuid);

        [PreserveSig]
        int Delete();

        [PreserveSig]
        int GetName(
            [MarshalAs(UnmanagedType.LPWStr)] StringBuilder name,
            int maxLength);

        [PreserveSig]
        int GetDisplayName(
            [MarshalAs(UnmanagedType.LPWStr)] StringBuilder name,
            int maxLength);

        [PreserveSig]
        int SetDisplayName(
            [MarshalAs(UnmanagedType.LPWStr)] string name);

        [PreserveSig]
        int GetPath(
            [MarshalAs(UnmanagedType.LPWStr)] StringBuilder path,
            int maxPath);

        [PreserveSig]
        int GetDSPath(
            uint section,
            [MarshalAs(UnmanagedType.LPWStr)] StringBuilder path,
            int maxPath);

        [PreserveSig]
        int GetFileSysPath(
            uint section,
            [MarshalAs(UnmanagedType.LPWStr)] StringBuilder path,
            int maxPath);

        [PreserveSig]
        int GetRegistryKey(uint section, out IntPtr key);

        [PreserveSig]
        int GetOptions(out uint options);

        [PreserveSig]
        int SetOptions(uint options, uint mask);

        [PreserveSig]
        int GetType(out uint gpoType);

        [PreserveSig]
        int GetMachineName(
            [MarshalAs(UnmanagedType.LPWStr)] StringBuilder name,
            int maxLength);

        [PreserveSig]
        int GetPropertySheetPages(out IntPtr pages, out uint pageCount);
    }
}

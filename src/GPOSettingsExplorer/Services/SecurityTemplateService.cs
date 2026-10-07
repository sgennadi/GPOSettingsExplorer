using System.DirectoryServices;
using System.Runtime.InteropServices;
using System.Text;
using GPOSettingsExplorer.Models;

namespace GPOSettingsExplorer.Services;

public sealed class SecurityTemplateService
{
    private static readonly Guid SecurityExtensionGuid =
        new("827D319E-6EAC-11D2-A4EA-00C04F79F83A");

    private static readonly Guid SecurityToolGuid =
        new("803E14A0-B4FB-11D0-A0D0-00A0C90F574B");

    public bool CanEditBoolean(
        PolicySettingInfo setting)
    {
        return setting.Extension.Equals(
                   "SecuritySettings",
                   StringComparison.OrdinalIgnoreCase) &&
               setting.Scope.Equals(
                   "Computer",
                   StringComparison.OrdinalIgnoreCase) &&
               !string.IsNullOrWhiteSpace(
                   setting.RegistryKey) &&
               !string.IsNullOrWhiteSpace(
                   setting.RegistryValue) &&
               bool.TryParse(
                   setting.Value,
                   out _);
    }

    public void ApplyBoolean(
        GpoInfo gpo,
        string domainDistinguishedName,
        PolicySettingInfo setting,
        bool value)
    {
        if (!CanEditBoolean(setting))
        {
            throw new InvalidOperationException(
                "This Security Settings value is not a supported Boolean registry-backed security option.");
        }

        var gpoPath =
            GetGpoFileSystemPath(
                gpo,
                domainDistinguishedName);

        var templatePath =
            Path.Combine(
                gpoPath,
                "Machine",
                "Microsoft",
                "Windows NT",
                "SecEdit",
                "GptTmpl.inf");

        if (!File.Exists(templatePath))
        {
            throw new FileNotFoundException(
                "The GPO security template file was not found.",
                templatePath);
        }

        var original =
            File.ReadAllBytes(
                templatePath);

        var encoding =
            DetectEncoding(
                original);

        var text =
            encoding.GetString(
                    StripPreamble(
                        original,
                        encoding))
                .Replace(
                    "\r\n",
                    "\n",
                    StringComparison.Ordinal)
                .Replace(
                    "\r",
                    "\n",
                    StringComparison.Ordinal);

        var target =
            BuildRegistryTarget(
                setting);

        var lines =
            text.Split(
                    '\n',
                    StringSplitOptions.None)
                .ToList();

        UpdateRegistryValuesSection(
            lines,
            target,
            value);

        var updated =
            string.Join(
                "\r\n",
                lines);

        var tempPath =
            templatePath +
            ".gposes-" +
            Guid.NewGuid().ToString("N") +
            ".tmp";

        WriteText(
            tempPath,
            updated,
            encoding);

        try
        {
            File.Copy(
                tempPath,
                templatePath,
                overwrite: true);

            using var policy =
                new NativeGroupPolicyObject(
                    gpo,
                    domainDistinguishedName);

            var extensionGuid =
                SecurityExtensionGuid;

            var toolGuid =
                SecurityToolGuid;

            policy.Save(
                machine: true,
                add: true,
                ref extensionGuid,
                ref toolGuid);
        }
        catch
        {
            File.WriteAllBytes(
                templatePath,
                original);

            throw;
        }
        finally
        {
            try
            {
                File.Delete(
                    tempPath);
            }
            catch
            {
            }
        }
    }

    private static string GetGpoFileSystemPath(
        GpoInfo gpo,
        string domainDistinguishedName)
    {
        var distinguishedName =
            $"CN={gpo.Id.ToString("B").ToUpperInvariant()},CN=Policies,CN=System,{domainDistinguishedName}";

        using var entry =
            new DirectoryEntry(
                $"LDAP://{distinguishedName}");

        var path =
            Convert.ToString(
                entry.Properties[
                    "gPCFileSysPath"].Value);

        if (!string.IsNullOrWhiteSpace(path))
        {
            return path;
        }

        return
            $@"\\{gpo.DomainName}\SYSVOL\{gpo.DomainName}\Policies\{gpo.Id.ToString("B").ToUpperInvariant()}";
    }

    private static string BuildRegistryTarget(
        PolicySettingInfo setting)
    {
        var key =
            setting.RegistryKey.Trim()
                .TrimStart('\\');

        foreach (var prefix in new[]
                 {
                     "MACHINE\\",
                     "HKEY_LOCAL_MACHINE\\",
                     "HKLM\\"
                 })
        {
            if (!key.StartsWith(
                    prefix,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            key =
                key[prefix.Length..];

            break;
        }

        return
            $"MACHINE\\{key}\\{setting.RegistryValue.Trim()}";
    }

    private static void UpdateRegistryValuesSection(
        List<string> lines,
        string target,
        bool value)
    {
        var sectionStart =
            lines.FindIndex(line =>
                line.Trim().Equals(
                    "[Registry Values]",
                    StringComparison.OrdinalIgnoreCase));

        if (sectionStart < 0)
        {
            var versionSection =
                lines.FindIndex(line =>
                    line.Trim().Equals(
                        "[Version]",
                        StringComparison.OrdinalIgnoreCase));

            sectionStart =
                versionSection >= 0
                    ? versionSection
                    : lines.Count;

            lines.Insert(
                sectionStart,
                "[Registry Values]");

            lines.Insert(
                sectionStart + 1,
                $"{target}=4,{(value ? 1 : 0)}");

            lines.Insert(
                sectionStart + 2,
                string.Empty);

            return;
        }

        var sectionEnd =
            lines.Count;

        for (var index =
             sectionStart + 1;
             index < lines.Count;
             index++)
        {
            var trimmed =
                lines[index].Trim();

            if (trimmed.StartsWith(
                    '[') &&
                trimmed.EndsWith(
                    ']'))
            {
                sectionEnd =
                    index;

                break;
            }
        }

        for (var index =
             sectionStart + 1;
             index < sectionEnd;
             index++)
        {
            var line =
                lines[index];

            var equals =
                line.IndexOf('=');

            if (equals <= 0)
            {
                continue;
            }

            var name =
                line[..equals]
                    .Trim();

            if (!name.Equals(
                    target,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            lines[index] =
                $"{target}=4,{(value ? 1 : 0)}";

            return;
        }

        lines.Insert(
            sectionEnd,
            $"{target}=4,{(value ? 1 : 0)}");
    }

    private static void WriteText(
        string path,
        string text,
        Encoding encoding)
    {
        using var stream =
            new FileStream(
                path,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None);

        var preamble =
            encoding.GetPreamble();

        if (preamble.Length > 0)
        {
            stream.Write(
                preamble);
        }

        var bytes =
            encoding.GetBytes(
                text);

        stream.Write(
            bytes);
    }

    private static Encoding DetectEncoding(
        byte[] bytes)
    {
        if (bytes.Length >= 2 &&
            bytes[0] == 0xFF &&
            bytes[1] == 0xFE)
        {
            return new UnicodeEncoding(
                bigEndian: false,
                byteOrderMark: true);
        }

        if (bytes.Length >= 2 &&
            bytes[0] == 0xFE &&
            bytes[1] == 0xFF)
        {
            return new UnicodeEncoding(
                bigEndian: true,
                byteOrderMark: true);
        }

        if (bytes.Length >= 3 &&
            bytes[0] == 0xEF &&
            bytes[1] == 0xBB &&
            bytes[2] == 0xBF)
        {
            return new UTF8Encoding(
                encoderShouldEmitUTF8Identifier: true);
        }

        var sample =
            bytes.Take(
                    Math.Min(
                        bytes.Length,
                        128))
                .ToArray();

        var oddZeroCount =
            sample
                .Where(
                    (_, index) =>
                        index % 2 == 1)
                .Count(value =>
                    value == 0);

        if (oddZeroCount > 8)
        {
            return new UnicodeEncoding(
                bigEndian: false,
                byteOrderMark: false);
        }

        return new UTF8Encoding(
            encoderShouldEmitUTF8Identifier: false);
    }

    private static byte[] StripPreamble(
        byte[] bytes,
        Encoding encoding)
    {
        var preamble =
            encoding.GetPreamble();

        if (preamble.Length == 0 ||
            bytes.Length < preamble.Length)
        {
            return bytes;
        }

        for (var index = 0;
             index < preamble.Length;
             index++)
        {
            if (bytes[index] !=
                preamble[index])
            {
                return bytes;
            }
        }

        return bytes[
            preamble.Length..];
    }

    private sealed class NativeGroupPolicyObject : IDisposable
    {
        private const uint ClsCtxInprocServer = 0x1;
        private const uint GpoOpenLoadRegistry = 0x00000001;

        private static readonly Guid Clsid =
            new("EA502722-A23D-11D1-A7D3-0000F87571E3");

        private static readonly Guid Iid =
            new("EA502723-A23D-11D1-A7D3-0000F87571E3");

        private IntPtr _instance;

        public NativeGroupPolicyObject(
            GpoInfo gpo,
            string domainDistinguishedName)
        {
            var clsid =
                Clsid;

            var iid =
                Iid;

            ThrowIfFailed(
                CoCreateInstance(
                    ref clsid,
                    IntPtr.Zero,
                    ClsCtxInprocServer,
                    ref iid,
                    out _instance));

            try
            {
                var open =
                    GetMethod<OpenDsgpoDelegate>(
                        slot: 4);

                var ldapPath =
                    $"LDAP://CN={gpo.Id:B},CN=Policies,CN=System,{domainDistinguishedName}";

                ThrowIfFailed(
                    open(
                        _instance,
                        ldapPath,
                        GpoOpenLoadRegistry));
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public void Save(
            bool machine,
            bool add,
            ref Guid extensionGuid,
            ref Guid toolGuid)
        {
            var save =
                GetMethod<SaveDelegate>(
                    slot: 7);

            ThrowIfFailed(
                save(
                    _instance,
                    machine,
                    add,
                    ref extensionGuid,
                    ref toolGuid));
        }

        private T GetMethod<T>(
            int slot)
            where T : Delegate
        {
            var vtable =
                Marshal.ReadIntPtr(
                    _instance);

            var address =
                Marshal.ReadIntPtr(
                    vtable,
                    checked(
                        slot *
                        IntPtr.Size));

            return Marshal
                .GetDelegateForFunctionPointer<T>(
                    address);
        }

        public void Dispose()
        {
            if (_instance == IntPtr.Zero)
            {
                return;
            }

            Marshal.Release(
                _instance);

            _instance =
                IntPtr.Zero;
        }
    }

    [UnmanagedFunctionPointer(
        CallingConvention.StdCall,
        CharSet = CharSet.Unicode)]
    private delegate int OpenDsgpoDelegate(
        IntPtr instance,
        [MarshalAs(UnmanagedType.LPWStr)]
        string path,
        uint flags);

    [UnmanagedFunctionPointer(
        CallingConvention.StdCall)]
    private delegate int SaveDelegate(
        IntPtr instance,
        [MarshalAs(UnmanagedType.Bool)]
        bool machine,
        [MarshalAs(UnmanagedType.Bool)]
        bool add,
        ref Guid extensionGuid,
        ref Guid toolGuid);

    [DllImport(
        "ole32.dll")]
    private static extern int CoCreateInstance(
        ref Guid clsid,
        IntPtr outer,
        uint context,
        ref Guid iid,
        out IntPtr instance);

    private static void ThrowIfFailed(
        int hresult)
    {
        if (hresult < 0)
        {
            Marshal.ThrowExceptionForHR(
                hresult);
        }
    }
}

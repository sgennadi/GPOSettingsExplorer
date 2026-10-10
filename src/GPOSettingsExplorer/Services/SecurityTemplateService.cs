using System.DirectoryServices;
using System.Runtime.InteropServices;
using System.Text;
using GPOSettingsExplorer.Models;

namespace GPOSettingsExplorer.Services;

public sealed partial class SecurityTemplateService
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
        bool value,
        string gpmcBackupDirectory)
    {
        if (string.IsNullOrWhiteSpace(gpmcBackupDirectory) ||
            !Directory.Exists(gpmcBackupDirectory) ||
            !Directory.EnumerateFiles(gpmcBackupDirectory, "bkupInfo.xml",
                SearchOption.AllDirectories).Any())
            throw new InvalidOperationException(
                "A completed GPMC safety backup is mandatory before editing Security Settings.");
        EditingGuard.EnsureEnabled("Edit Security Option");
        if (!CanEditBoolean(setting))
            throw new InvalidOperationException(
                "Only an existing, unambiguous Boolean Security Settings value is supported.");

        var context = DomainConnectionState.Context;
        if (context is null ||
            string.IsNullOrWhiteSpace(context.ConnectedServer) ||
            !context.DomainName.Equals(gpo.DomainName, StringComparison.OrdinalIgnoreCase) ||
            !context.DomainDistinguishedName.Equals(
                domainDistinguishedName, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "The pinned DC or target domain changed. Reconnect before editing.");

        // Do not follow gPCFileSysPath through a domain DFS alias for writes.
        var templatePath = Path.Combine(
            DomainConnectionState.BuildSysvolRoot(gpo.DomainName),
            "Policies", gpo.Id.ToString("B").ToUpperInvariant(),
            "Machine", "Microsoft", "Windows NT", "SecEdit", "GptTmpl.inf");

        if (!File.Exists(templatePath))
            throw new FileNotFoundException(
                "Existing security template is required; refusing to create it.", templatePath);

        var length = new FileInfo(templatePath).Length;
        if (length > SecurityTemplateSourceReader.MaxFileBytes)
            throw new InvalidDataException("Security template exceeds the read safety cap.");
        var original = File.ReadAllBytes(templatePath);
        if (original.Length > SecurityTemplateSourceReader.MaxFileBytes)
            throw new InvalidDataException("Security template grew beyond the read cap.");

        var sha = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(original));
        var source = SecurityTemplateSourceReader.Parse(original,
            gpo.Id, gpo.DisplayName, templatePath, sha);
        if (!source.IsComplete)
            throw new InvalidDataException(
                "Source security template has parsing errors. Refusing an unsafe edit: " +
                string.Join(" | ", source.Issues.Take(5)));

        var encoding = DetectEncoding(original);
        var previousText = encoding.GetString(StripPreamble(original, encoding));
        var registryTarget = BuildRegistryTarget(setting);
        var existingValue = bool.Parse(setting.Value);
        var updatedText = SecurityTemplateEditRules.ChangeExistingRegistryBoolean(
            previousText, registryTarget, existingValue, value);
        if (previousText.Equals(updatedText, StringComparison.Ordinal))
            return;

        ChangePreviewGuard.ConfirmRequired(new ChangePreviewRequest(
            $"Edit Security Option: {setting.SettingName}",
            gpo.DisplayName,
            $"[{registryTarget}] = {(existingValue ? 1 : 0)}",
            $"[{registryTarget}] = {(value ? 1 : 0)}",
            $"Pinned DC: {context.ConnectedServer}\nSource SHA-256: {sha}\n" +
            "This is a stored GPO value, NOT verified effective RSoP. " +
            "The caller must create a GPMC backup before editing.",
            "Apply"));

        var staging = templatePath + ".gposes-" + Guid.NewGuid().ToString("N") + ".tmp";
        var rollbackDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GPOSettingsExplorer", "SecurityRollback");
        Directory.CreateDirectory(rollbackDirectory);
        var rollback = Path.Combine(rollbackDirectory,
            $"{gpo.Id:N}-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.inf");

        var updatedBytes = encoding.GetPreamble()
            .Concat(encoding.GetBytes(updatedText)).ToArray();
        var replaced = false;
        try
        {
            File.WriteAllBytes(rollback, original);
            using (var stream = new FileStream(staging, FileMode.CreateNew,
                       FileAccess.Write, FileShare.None))
                stream.Write(updatedBytes);

            // Best-effort optimistic concurrency precondition; a concurrent
            // writer must never be silently overwritten with a guessed value.
            if (!File.ReadAllBytes(templatePath).AsSpan().SequenceEqual(original))
                throw new IOException(
                    "GptTmpl.inf changed after inspection. Reload and review before editing.");
            EditingGuard.EnsureEnabled("Edit Security Option");
            File.Replace(staging, templatePath, null);
            replaced = true;

            using (var policy = new NativeGroupPolicyObject(gpo, domainDistinguishedName))
            {
                var extensionGuid = SecurityExtensionGuid;
                var toolGuid = SecurityToolGuid;
                policy.Save(machine: true, add: true, ref extensionGuid, ref toolGuid);
            }
            try { File.Delete(rollback); }
            catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException)
            {
                CrashLogService.Write("Cleanup local Security Settings rollback", cleanup);
            }
        }
        catch (Exception ex) when (replaced)
        {
            // Do NOT overwrite post-save edits from another administrator.
            // Preserve the exact original locally for controlled recovery.
            throw new IOException(
                "Source replacement finished, but GPMC Save did not complete. " +
                "Run GPO Health Check; do not retry blindly. " +
                "Original bytes preserved locally at: " + rollback, ex);
        }
        finally
        {
            try { if (File.Exists(staging)) File.Delete(staging); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            if (!replaced)
            {
                try { if (File.Exists(rollback)) File.Delete(rollback); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
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
                DomainConnectionState.BuildLdapPath(
                    distinguishedName));

        var path =
            Convert.ToString(
                entry.Properties[
                    "gPCFileSysPath"].Value);

        if (!string.IsNullOrWhiteSpace(path))
        {
            return path;
        }

        return Path.Combine(
            DomainConnectionState.BuildSysvolRoot(
                gpo.DomainName),
            "Policies",
            gpo.Id.ToString("B").ToUpperInvariant());
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
                    DomainConnectionState.BuildLdapPath(
                        $"CN={gpo.Id:B},CN=Policies,CN=System,{domainDistinguishedName}");

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

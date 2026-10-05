using System.Runtime.InteropServices;
using System.Text;
using GPOSettingsExplorer.Models;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace GPOSettingsExplorer.Services;

public sealed class RegistryPolicyService
{
    private const uint GpoOpenLoadRegistry = 0x00000001;
    private const uint GpoSectionUser = 1;
    private const uint GpoSectionMachine = 2;

    private static readonly Guid RegistryExtensionGuid =
        new("35378EAC-683F-11D2-A89A-00C04FBBCFA2");

    private static readonly Guid GpeSnapInGuid =
        new("8FC0B734-A0E1-11D1-A7D3-0000F87571E3");

    public PolicyEditSession Read(
        GpoInfo gpo,
        string domainDistinguishedName,
        AdmxPolicyDefinition definition,
        string reportState)
    {
        var session = new PolicyEditSession
        {
            State = reportState.Equals("Enabled", StringComparison.OrdinalIgnoreCase)
                ? PolicyEditState.Enabled
                : reportState.Equals("Disabled", StringComparison.OrdinalIgnoreCase)
                    ? PolicyEditState.Disabled
                    : PolicyEditState.NotConfigured
        };

        var policyObject = Open(gpo, domainDistinguishedName);

        try
        {
            using var root = OpenRegistryRoot(policyObject, definition.Scope);
            foreach (var element in definition.Elements)
            {
                session.Values[element.Id] = ReadElement(root, element);
            }
        }
        finally
        {
            Marshal.FinalReleaseComObject(policyObject);
        }

        return session;
    }

    public void Apply(
        GpoInfo gpo,
        string domainDistinguishedName,
        AdmxPolicyDefinition definition,
        PolicyEditSession session)
    {
        var policyObject = Open(gpo, domainDistinguishedName);

        try
        {
            using var root = OpenRegistryRoot(policyObject, definition.Scope);

            switch (session.State)
            {
                case PolicyEditState.NotConfigured:
                    DeletePolicyValues(root, definition);
                    break;

                case PolicyEditState.Disabled:
                    ApplyBaseValue(root, definition.Key, definition.ValueName, definition.DisabledValue);
                    DeleteElementValues(root, definition.Elements);
                    break;

                case PolicyEditState.Enabled:
                    ApplyBaseValue(root, definition.Key, definition.ValueName, definition.EnabledValue);
                    ApplyElementValues(root, definition.Elements, session.Values);
                    break;
            }

            var machine = definition.Scope.Equals("Computer", StringComparison.OrdinalIgnoreCase);
            var extensionGuid = RegistryExtensionGuid;
            var snapInGuid = GpeSnapInGuid;

            ThrowIfFailed(policyObject.Save(
                machine,
                true,
                ref extensionGuid,
                ref snapInGuid));
        }
        finally
        {
            Marshal.FinalReleaseComObject(policyObject);
        }
    }

    private static IGroupPolicyObject Open(GpoInfo gpo, string domainDistinguishedName)
    {
        var comType = Type.GetTypeFromCLSID(new Guid("EA502722-A23D-11D1-A7D3-0000F87571E3"))
            ?? throw new InvalidOperationException("Windows Group Policy API is unavailable.");
        var rawInstance = Activator.CreateInstance(comType)
            ?? throw new InvalidOperationException("Unable to create the Windows Group Policy object.");
        var instance = (IGroupPolicyObject)rawInstance;
        var ldapPath = $"LDAP://CN={gpo.Id:B},CN=Policies,CN=System,{domainDistinguishedName}";
        ThrowIfFailed(instance.OpenDSGPO(ldapPath, GpoOpenLoadRegistry));
        return instance;
    }

    private static RegistryKey OpenRegistryRoot(IGroupPolicyObject policyObject, string scope)
    {
        var section = scope.Equals("User", StringComparison.OrdinalIgnoreCase)
            ? GpoSectionUser
            : GpoSectionMachine;

        ThrowIfFailed(policyObject.GetRegistryKey(section, out var rawHandle));

        var safeHandle = new SafeRegistryHandle(rawHandle, ownsHandle: true);
        return RegistryKey.FromHandle(safeHandle, RegistryView.Default);
    }

    private static object? ReadElement(RegistryKey root, AdmxElementDefinition element)
    {
        if (element.Type == AdmxElementType.List)
        {
            using var listKey = root.OpenSubKey(element.Key, writable: false);
            if (listKey is null)
            {
                return Array.Empty<string>();
            }

            return listKey
                .GetValueNames()
                .Where(name => string.IsNullOrEmpty(element.ValuePrefix) ||
                               name.StartsWith(element.ValuePrefix, StringComparison.OrdinalIgnoreCase))
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .Select(name => Convert.ToString(listKey.GetValue(name)) ?? string.Empty)
                .Where(value => !string.IsNullOrEmpty(value))
                .ToArray();
        }

        if (string.IsNullOrWhiteSpace(element.ValueName))
        {
            return null;
        }

        using var key = root.OpenSubKey(element.Key, writable: false);
        var raw = key?.GetValue(element.ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        if (raw is null)
        {
            return null;
        }

        return element.Type switch
        {
            AdmxElementType.Boolean => ValuesEqual(raw, element.TrueValue?.Value),
            AdmxElementType.Decimal => Convert.ToInt64(raw),
            _ => raw
        };
    }

    private static void ApplyElementValues(
        RegistryKey root,
        IEnumerable<AdmxElementDefinition> elements,
        IReadOnlyDictionary<string, object?> values)
    {
        foreach (var element in elements)
        {
            values.TryGetValue(element.Id, out var value);

            if (element.Type == AdmxElementType.List)
            {
                WriteList(root, element, value);
                continue;
            }

            if (string.IsNullOrWhiteSpace(element.ValueName))
            {
                continue;
            }

            using var key = root.CreateSubKey(element.Key, writable: true)
                ?? throw new InvalidOperationException($"Cannot open policy key '{element.Key}'.");

            if (value is null)
            {
                key.DeleteValue(element.ValueName, throwOnMissingValue: false);
                continue;
            }

            switch (element.Type)
            {
                case AdmxElementType.Text:
                    key.SetValue(element.ValueName, Convert.ToString(value) ?? string.Empty, RegistryValueKind.String);
                    break;

                case AdmxElementType.Decimal:
                    key.SetValue(element.ValueName, Convert.ToInt32(value), RegistryValueKind.DWord);
                    break;

                case AdmxElementType.Boolean:
                    ApplyRegistryValue(
                        key,
                        element.ValueName,
                        Convert.ToBoolean(value) ? element.TrueValue : element.FalseValue);
                    break;

                case AdmxElementType.Enum:
                    if (value is AdmxEnumChoice choice)
                    {
                        ApplyRegistryValue(key, element.ValueName, choice.RegistryValue);
                    }
                    else
                    {
                        var selected = element.Choices.FirstOrDefault(c =>
                            ValuesEqual(c.RegistryValue.Value, value) ||
                            string.Equals(c.DisplayName, Convert.ToString(value), StringComparison.CurrentCultureIgnoreCase));

                        if (selected is not null)
                        {
                            ApplyRegistryValue(key, element.ValueName, selected.RegistryValue);
                        }
                    }
                    break;

                default:
                    key.SetValue(element.ValueName, Convert.ToString(value) ?? string.Empty, RegistryValueKind.String);
                    break;
            }
        }
    }

    private static void WriteList(RegistryKey root, AdmxElementDefinition element, object? value)
    {
        using var key = root.CreateSubKey(element.Key, writable: true)
            ?? throw new InvalidOperationException($"Cannot open policy key '{element.Key}'.");

        var prefix = element.ValuePrefix ?? string.Empty;

        foreach (var name in key.GetValueNames()
                     .Where(name => string.IsNullOrEmpty(prefix) ||
                                    name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
        {
            key.DeleteValue(name, throwOnMissingValue: false);
        }

        var items = value switch
        {
            string[] array => array,
            IEnumerable<string> enumerable => enumerable.ToArray(),
            string text => text.Split(
                new[] { "\r\n", "\n" },
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            _ => Array.Empty<string>()
        };

        for (var i = 0; i < items.Length; i++)
        {
            var name = string.IsNullOrEmpty(prefix)
                ? (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture)
                : prefix + (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);

            key.SetValue(name, items[i], RegistryValueKind.String);
        }
    }

    private static void DeletePolicyValues(RegistryKey root, AdmxPolicyDefinition definition)
    {
        if (!string.IsNullOrWhiteSpace(definition.ValueName))
        {
            using var key = root.OpenSubKey(definition.Key, writable: true);
            key?.DeleteValue(definition.ValueName, throwOnMissingValue: false);
        }

        DeleteElementValues(root, definition.Elements);
    }

    private static void DeleteElementValues(
        RegistryKey root,
        IEnumerable<AdmxElementDefinition> elements)
    {
        foreach (var element in elements)
        {
            using var key = root.OpenSubKey(element.Key, writable: true);
            if (key is null)
            {
                continue;
            }

            if (element.Type == AdmxElementType.List)
            {
                foreach (var name in key.GetValueNames()
                             .Where(name => string.IsNullOrEmpty(element.ValuePrefix) ||
                                            name.StartsWith(element.ValuePrefix, StringComparison.OrdinalIgnoreCase)))
                {
                    key.DeleteValue(name, throwOnMissingValue: false);
                }
            }
            else if (!string.IsNullOrWhiteSpace(element.ValueName))
            {
                key.DeleteValue(element.ValueName, throwOnMissingValue: false);
            }
        }
    }

    private static void ApplyBaseValue(
        RegistryKey root,
        string keyPath,
        string valueName,
        AdmxRegistryValue? value)
    {
        if (string.IsNullOrWhiteSpace(keyPath) ||
            string.IsNullOrWhiteSpace(valueName) ||
            value is null)
        {
            return;
        }

        using var key = root.CreateSubKey(keyPath, writable: true)
            ?? throw new InvalidOperationException($"Cannot open policy key '{keyPath}'.");

        ApplyRegistryValue(key, valueName, value);
    }

    private static void ApplyRegistryValue(
        RegistryKey key,
        string valueName,
        AdmxRegistryValue? value)
    {
        if (value is null)
        {
            return;
        }

        if (value.Delete)
        {
            key.DeleteValue(valueName, throwOnMissingValue: false);
            return;
        }

        var kind = value.Kind == RegistryValueKind.Unknown
            ? InferKind(value.Value)
            : value.Kind;

        var normalized = NormalizeForRegistry(value.Value, kind);
        key.SetValue(valueName, normalized ?? string.Empty, kind);
    }

    private static RegistryValueKind InferKind(object? value) =>
        value is byte or short or int or long or uint or ulong
            ? RegistryValueKind.DWord
            : RegistryValueKind.String;

    private static object? NormalizeForRegistry(object? value, RegistryValueKind kind)
    {
        if (value is null)
        {
            return null;
        }

        return kind switch
        {
            RegistryValueKind.DWord => Convert.ToInt32(value),
            RegistryValueKind.QWord => Convert.ToInt64(value),
            RegistryValueKind.String or RegistryValueKind.ExpandString => Convert.ToString(value) ?? string.Empty,
            _ => value
        };
    }

    private static bool ValuesEqual(object? left, object? right)
    {
        if (left is null || right is null)
        {
            return left is null && right is null;
        }

        if (long.TryParse(Convert.ToString(left), out var leftNumber) &&
            long.TryParse(Convert.ToString(right), out var rightNumber))
        {
            return leftNumber == rightNumber;
        }

        return string.Equals(
            Convert.ToString(left),
            Convert.ToString(right),
            StringComparison.OrdinalIgnoreCase);
    }

    private static void ThrowIfFailed(int hresult)
    {
        if (hresult < 0)
        {
            Marshal.ThrowExceptionForHR(hresult);
        }
    }

    [ComImport]
    [Guid("EA502722-A23D-11D1-A7D3-0000F87571E3")]
    private sealed class GroupPolicyObjectCom
    {
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

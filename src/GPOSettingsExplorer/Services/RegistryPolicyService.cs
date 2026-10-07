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

        using var policyObject =
            Open(
                gpo,
                domainDistinguishedName);

        using (var root =
               OpenRegistryRoot(
                   policyObject,
                   definition.Scope))
        {
            session.State = InferState(root, definition, session.State);

            foreach (var element in definition.Elements)
            {
                session.Values[element.Id] =
                    ReadElement(
                        root,
                        element);
            }
        }

        return session;
    }

    public void Apply(
        GpoInfo gpo,
        string domainDistinguishedName,
        AdmxPolicyDefinition definition,
        PolicyEditSession session)
    {
        EditingGuard.EnsureEnabled(
            "Edit Administrative Template policy");

        var before =
            Read(
                gpo,
                domainDistinguishedName,
                definition,
                "Not Configured");

        ChangePreviewGuard.Confirm(
            new ChangePreviewRequest(
                $"Edit policy: {definition.DisplayName}",
                $"{gpo.DisplayName} | {definition.Scope} Configuration",
                PolicySessionSummary(
                    before),
                PolicySessionSummary(
                    session),
                BuildPolicyTargetSummary(
                    definition),
                "Apply"));

        using var policyObject =
            Open(
                gpo,
                domainDistinguishedName);

        using (var root =
               OpenRegistryRoot(
                   policyObject,
                   definition.Scope))
        {

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

            policyObject.Save(
                machine,
                add: true,
                ref extensionGuid,
                ref snapInGuid);
        }
    }

    private static GroupPolicyObjectHandle Open(
        GpoInfo gpo,
        string domainDistinguishedName)
    {
        var ldapPath =
            DomainConnectionState.BuildLdapPath(
                $"CN={gpo.Id:B},CN=Policies,CN=System,{domainDistinguishedName}");

        return new GroupPolicyObjectHandle(
            ldapPath);
    }

    private static RegistryKey OpenRegistryRoot(
        GroupPolicyObjectHandle policyObject,
        string scope)
    {
        var section =
            scope.Equals(
                "User",
                StringComparison.OrdinalIgnoreCase)
                ? GpoSectionUser
                : GpoSectionMachine;

        var rawHandle =
            policyObject.GetRegistryKey(
                section);

        var safeHandle =
            new SafeRegistryHandle(
                rawHandle,
                ownsHandle: true);

        return RegistryKey.FromHandle(
            safeHandle,
            RegistryView.Default);
    }

    private static string PolicySessionSummary(
        PolicyEditSession session)
    {
        var values =
            session.Values
                .OrderBy(
                    pair =>
                        pair.Key,
                    StringComparer.OrdinalIgnoreCase)
                .Select(
                    pair =>
                        $"{pair.Key} = {FormatPolicyValue(pair.Value)}");

        return
            $"State: {session.State}\n" +
            string.Join(
                "\n",
                values);
    }

    private static string FormatPolicyValue(
        object? value)
    {
        if (value is null)
            return "<null>";

        if (value is IEnumerable<string> values)
        {
            return string.Join(
                "; ",
                values);
        }

        return Convert.ToString(
                   value,
                   System.Globalization.CultureInfo.InvariantCulture)
               ?? string.Empty;
    }

    private static string BuildPolicyTargetSummary(
        AdmxPolicyDefinition definition)
    {
        var targets =
            new List<string>();

        if (!string.IsNullOrWhiteSpace(
                definition.Key))
        {
            targets.Add(
                string.IsNullOrWhiteSpace(
                    definition.ValueName)
                    ? definition.Key
                    : $"{definition.Key} \\ {definition.ValueName}");
        }

        foreach (var element in definition.Elements)
        {
            if (string.IsNullOrWhiteSpace(
                    element.Key))
            {
                continue;
            }

            targets.Add(
                string.IsNullOrWhiteSpace(
                    element.ValueName)
                    ? element.Key
                    : $"{element.Key} \\ {element.ValueName}");
        }

        return
            "Registry targets:\n" +
            string.Join(
                "\n",
                targets.Distinct(
                    StringComparer.OrdinalIgnoreCase));
    }

    private static PolicyEditState InferState(
        RegistryKey root,
        AdmxPolicyDefinition definition,
        PolicyEditState fallback)
    {
        if (!string.IsNullOrWhiteSpace(definition.Key) &&
            !string.IsNullOrWhiteSpace(definition.ValueName))
        {
            using var key = root.OpenSubKey(definition.Key, writable: false);
            var raw = key?.GetValue(
                definition.ValueName,
                null,
                RegistryValueOptions.DoNotExpandEnvironmentNames);

            if (raw is not null)
            {
                if (definition.EnabledValue is not null &&
                    !definition.EnabledValue.Delete &&
                    ValuesEqual(raw, definition.EnabledValue.Value))
                {
                    return PolicyEditState.Enabled;
                }

                if (definition.DisabledValue is not null &&
                    !definition.DisabledValue.Delete &&
                    ValuesEqual(raw, definition.DisabledValue.Value))
                {
                    return PolicyEditState.Disabled;
                }
            }
        }

        if (fallback != PolicyEditState.NotConfigured)
        {
            return fallback;
        }

        foreach (var element in definition.Elements)
        {
            if (element.Type == AdmxElementType.List)
            {
                using var listKey = root.OpenSubKey(element.Key, writable: false);
                if (listKey is not null && listKey.GetValueNames().Length > 0)
                {
                    return PolicyEditState.Enabled;
                }

                continue;
            }

            if (string.IsNullOrWhiteSpace(element.ValueName))
            {
                continue;
            }

            using var key = root.OpenSubKey(element.Key, writable: false);
            if (key?.GetValue(element.ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames) is not null)
            {
                return PolicyEditState.Enabled;
            }
        }

        return PolicyEditState.NotConfigured;
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

    private sealed class GroupPolicyObjectHandle : IDisposable
    {
        private const uint ClsCtxInprocServer = 0x1;
        private const uint CoInitApartmentThreaded = 0x2;
        private const int RpcEChangedMode = unchecked((int)0x80010106);

        private static readonly Guid ClsidGroupPolicyObject =
            new("EA502722-A23D-11D1-A7D3-0000F87571E3");

        private static readonly Guid IidGroupPolicyObject =
            new("EA502723-A23D-11D1-A7D3-0000F87571E3");

        private IntPtr _instance;
        private bool _uninitializeCom;

        public GroupPolicyObjectHandle(
            string ldapPath)
        {
            var initializeResult =
                CoInitializeEx(
                    IntPtr.Zero,
                    CoInitApartmentThreaded);

            if (initializeResult >= 0)
            {
                _uninitializeCom = true;
            }
            else if (initializeResult != RpcEChangedMode)
            {
                ThrowIfFailed(
                    initializeResult);
            }

            var clsid =
                ClsidGroupPolicyObject;

            var iid =
                IidGroupPolicyObject;

            var result =
                CoCreateInstance(
                    ref clsid,
                    IntPtr.Zero,
                    ClsCtxInprocServer,
                    ref iid,
                    out _instance);

            ThrowIfFailed(
                result);

            try
            {
                var open =
                    GetMethod<OpenDsgpoDelegate>(
                        slot: 4);

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

        public IntPtr GetRegistryKey(
            uint section)
        {
            var method =
                GetMethod<GetRegistryKeyDelegate>(
                    slot: 15);

            ThrowIfFailed(
                method(
                    _instance,
                    section,
                    out var key));

            if (key == IntPtr.Zero)
            {
                throw new InvalidOperationException(
                    "The Group Policy API returned an empty registry handle.");
            }

            return key;
        }

        public void Save(
            bool machine,
            bool add,
            ref Guid extensionGuid,
            ref Guid snapInGuid)
        {
            var method =
                GetMethod<SaveDelegate>(
                    slot: 7);

            ThrowIfFailed(
                method(
                    _instance,
                    machine,
                    add,
                    ref extensionGuid,
                    ref snapInGuid));
        }

        private T GetMethod<T>(
            int slot)
            where T : Delegate
        {
            if (_instance == IntPtr.Zero)
            {
                throw new ObjectDisposedException(
                    nameof(GroupPolicyObjectHandle));
            }

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
            if (_instance != IntPtr.Zero)
            {
                Marshal.Release(
                    _instance);

                _instance =
                    IntPtr.Zero;
            }

            if (_uninitializeCom)
            {
                CoUninitialize();
                _uninitializeCom =
                    false;
            }
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
    private delegate int GetRegistryKeyDelegate(
        IntPtr instance,
        uint section,
        out IntPtr key);

    [UnmanagedFunctionPointer(
        CallingConvention.StdCall)]
    private delegate int SaveDelegate(
        IntPtr instance,
        [MarshalAs(UnmanagedType.Bool)]
        bool machine,
        [MarshalAs(UnmanagedType.Bool)]
        bool add,
        ref Guid extensionGuid,
        ref Guid snapInGuid);

    [DllImport(
        "ole32.dll")]
    private static extern int CoInitializeEx(
        IntPtr reserved,
        uint coInit);

    [DllImport(
        "ole32.dll")]
    private static extern void CoUninitialize();

    [DllImport(
        "ole32.dll")]
    private static extern int CoCreateInstance(
        ref Guid clsid,
        IntPtr outer,
        uint context,
        ref Guid iid,
        out IntPtr instance);

}

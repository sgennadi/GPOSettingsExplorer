using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Xml.Linq;
using GPOSettingsExplorer.Models;

namespace GPOSettingsExplorer.Services;

public sealed class GppRegistryService
{
    private static readonly Guid RegistryPreferencesCseGuid =
        new("B087BE9D-ED37-454F-AF9C-04291E351182");

    private static readonly Guid RegistryPreferencesToolGuid =
        new("BEE07A6A-EC9F-4659-B8C9-0B1937907C83");

    private const string RegistrySettingsClsid = "{A3CCFC41-DFDB-43A5-8D26-0FE8B954DA51}";
    private const string RegistryItemClsid = "{9CD4B2F4-923D-47F5-A062-E897DD1DAD50}";
    private const uint GpoOpenLoadRegistry = 0x00000001;

    public IReadOnlyList<GppRegistryItemInfo> Load(
        IEnumerable<GpoInfo> gpos,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var result = new List<GppRegistryItemInfo>();
        var list = gpos.ToList();

        for (var i = 0; i < list.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var gpo = list[i];

            progress?.Report($"Registry Preferences {i + 1}/{list.Count}: {gpo.DisplayName}");

            result.AddRange(LoadScope(gpo, "Computer"));
            result.AddRange(LoadScope(gpo, "User"));
        }

        return result
            .OrderBy(item => item.GpoName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(item => item.Scope, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Ordinal)
            .ToArray();
    }

    public GppRegistryItemInfo CreateNew(GpoInfo gpo, string scope)
    {
        var machine = scope.Equals("Computer", StringComparison.OrdinalIgnoreCase);

        return new GppRegistryItemInfo
        {
            GpoId = gpo.Id,
            GpoName = gpo.DisplayName,
            DomainName = gpo.DomainName,
            Scope = machine ? "Computer" : "User",
            XmlPath = GetXmlPath(gpo, machine ? "Computer" : "User"),
            Uid = Guid.NewGuid().ToString("B").ToUpperInvariant(),
            DisplayName = "New Registry Preference",
            Action = "U",
            Hive = machine ? "HKEY_LOCAL_MACHINE" : "HKEY_CURRENT_USER",
            ValueType = "REG_SZ"
        };
    }

    public void Save(
        GpoInfo gpo,
        string domainDistinguishedName,
        GppRegistryItemInfo item)
    {
        Validate(item);

        var path = GetXmlPath(gpo, item.Scope);
        var directory = Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException("Invalid Registry Preferences path.");

        Directory.CreateDirectory(directory);

        byte[]? original = File.Exists(path) ? File.ReadAllBytes(path) : null;

        try
        {
            var document = LoadOrCreateDocument(path);
            var registryElements = document
                .Descendants()
                .Where(element => element.Name.LocalName == "Registry")
                .ToArray();

            var registry = FindRegistryElement(registryElements, item);

            if (registry is null)
            {
                registry = CreateRegistryElement(item);
                document.Root!.Add(registry);
            }
            else
            {
                UpdateRegistryElement(registry, item);
            }

            WriteDocument(path, document);
            CommitExtension(gpo, domainDistinguishedName, item.Scope, add: true);
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
        GppRegistryItemInfo item)
    {
        var path = GetXmlPath(gpo, item.Scope);
        if (!File.Exists(path))
            return;

        var original = File.ReadAllBytes(path);

        try
        {
            var document = XDocument.Load(path, LoadOptions.PreserveWhitespace);
            var elements = document
                .Descendants()
                .Where(element => element.Name.LocalName == "Registry")
                .ToArray();

            var target = FindRegistryElement(elements, item)
                ?? throw new InvalidOperationException(
                    "The selected Registry Preference item no longer exists. Refresh the list.");

            target.Remove();

            var hasRegistryItems = document
                .Descendants()
                .Any(element => element.Name.LocalName == "Registry");

            if (hasRegistryItems)
            {
                WriteDocument(path, document);
                CommitExtension(gpo, domainDistinguishedName, item.Scope, add: true);
            }
            else
            {
                File.Delete(path);
                TryDeleteEmptyParents(path);
                CommitExtension(gpo, domainDistinguishedName, item.Scope, add: false);
            }
        }
        catch
        {
            RestoreFile(path, original);
            throw;
        }
    }

    private static IEnumerable<GppRegistryItemInfo> LoadScope(
        GpoInfo gpo,
        string scope)
    {
        var path = GetXmlPath(gpo, scope);
        if (!File.Exists(path))
            return Array.Empty<GppRegistryItemInfo>();

        var document = XDocument.Load(path, LoadOptions.PreserveWhitespace);
        var result = new List<GppRegistryItemInfo>();
        var ordinal = 0;

        foreach (var registry in document
                     .Descendants()
                     .Where(element => element.Name.LocalName == "Registry"))
        {
            ordinal++;

            var properties = registry.Elements()
                .FirstOrDefault(element => element.Name.LocalName == "Properties");

            if (properties is null)
                continue;

            var type = Attr(properties, "type");
            var rawValue = Attr(properties, "value");

            if (type.Equals("REG_MULTI_SZ", StringComparison.OrdinalIgnoreCase))
            {
                var values = registry
                    .Descendants()
                    .Where(element => element.Name.LocalName == "Value")
                    .Select(element => element.Value)
                    .ToArray();

                if (values.Length > 0)
                    rawValue = string.Join(Environment.NewLine, values);
            }
            else if (type.Equals("REG_DWORD", StringComparison.OrdinalIgnoreCase))
            {
                rawValue = FormatDwordForEditor(
                    rawValue,
                    IsTrue(Attr(properties, "displayDecimal")));
            }

            var filters = registry.Elements()
                .FirstOrDefault(element => element.Name.LocalName == "Filters");

            result.Add(new GppRegistryItemInfo
            {
                GpoId = gpo.Id,
                GpoName = gpo.DisplayName,
                DomainName = gpo.DomainName,
                Scope = scope,
                XmlPath = path,
                Uid = Attr(registry, "uid"),
                Ordinal = ordinal,
                DisplayName = FirstNonEmpty(
                    Attr(registry, "name"),
                    Attr(registry, "status"),
                    Attr(properties, "name"),
                    "Registry Preference"),
                Description = FirstNonEmpty(
                    Attr(registry, "desc"),
                    Attr(registry, "descr")),
                Action = FirstNonEmpty(Attr(properties, "action"), "U"),
                Hive = Attr(properties, "hive"),
                Key = Attr(properties, "key"),
                ValueName = Attr(properties, "name"),
                ValueType = type,
                ValueData = rawValue,
                DefaultValue = IsTrue(Attr(properties, "default")),
                DisplayDecimal = IsTrue(Attr(properties, "displayDecimal")),
                Disabled = IsTrue(Attr(registry, "disabled")),
                BypassErrors = IsTrue(Attr(registry, "bypassErrors")),
                RemoveWhenNoLongerApplied = IsTrue(Attr(registry, "removePolicy")),
                RunInUserContext = IsTrue(Attr(registry, "userContext")),
                FiltersXml = filters?.ToString(SaveOptions.DisableFormatting) ?? string.Empty
            });
        }

        return result;
    }

    private static XDocument LoadOrCreateDocument(string path)
    {
        if (File.Exists(path))
            return XDocument.Load(path, LoadOptions.PreserveWhitespace);

        return new XDocument(
            new XDeclaration("1.0", "utf-8", null),
            new XElement("RegistrySettings",
                new XAttribute("clsid", RegistrySettingsClsid)));
    }

    private static XElement CreateRegistryElement(GppRegistryItemInfo item)
    {
        var registry = new XElement("Registry",
            new XAttribute("clsid", RegistryItemClsid));

        UpdateRegistryElement(registry, item);
        return registry;
    }

    private static void UpdateRegistryElement(
        XElement registry,
        GppRegistryItemInfo item)
    {
        var displayName = string.IsNullOrWhiteSpace(item.DisplayName)
            ? (string.IsNullOrWhiteSpace(item.ValueName)
                ? item.Key
                : item.ValueName)
            : item.DisplayName.Trim();

        SetAttr(registry, "clsid", RegistryItemClsid);
        SetAttr(registry, "name", displayName);
        SetAttr(registry, "status", displayName);
        SetAttr(registry, "image", "2");
        SetAttr(registry, "changed", DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));

        if (string.IsNullOrWhiteSpace(item.Uid))
            item.Uid = Guid.NewGuid().ToString("B").ToUpperInvariant();

        SetAttr(registry, "uid", item.Uid);
        SetOptionalBool(registry, "disabled", item.Disabled);
        SetOptionalBool(registry, "bypassErrors", item.BypassErrors);
        SetOptionalBool(registry, "removePolicy", item.RemoveWhenNoLongerApplied);
        SetOptionalBool(registry, "userContext", item.RunInUserContext);

        if (string.IsNullOrWhiteSpace(item.Description))
        {
            registry.SetAttributeValue("desc", null);
            registry.SetAttributeValue("descr", null);
        }
        else
        {
            SetAttr(registry, "desc", item.Description);
            registry.SetAttributeValue("descr", null);
        }

        var properties = registry.Elements()
            .FirstOrDefault(element => element.Name.LocalName == "Properties");

        if (properties is null)
        {
            properties = new XElement("Properties");
            registry.AddFirst(properties);
        }

        var normalizedType = item.ValueType.Trim().ToUpperInvariant();
        var normalizedValue = NormalizeValueForXml(item, normalizedType);

        SetAttr(properties, "action", NormalizeAction(item.Action));
        SetAttr(properties, "displayDecimal", item.DisplayDecimal ? "1" : "0");
        SetAttr(properties, "default", item.DefaultValue ? "1" : "0");
        SetAttr(properties, "hive", item.Hive.Trim().ToUpperInvariant());
        SetAttr(properties, "key", item.Key.Trim().Trim('\\'));
        SetAttr(properties, "name", item.DefaultValue ? string.Empty : item.ValueName.Trim());
        SetAttr(properties, "type", normalizedType);
        SetAttr(properties, "value", normalizedValue);

        registry.Elements()
            .Where(element => element.Name.LocalName == "Values")
            .Remove();

        if (normalizedType == "REG_MULTI_SZ")
        {
            var lines = item.ValueData
                .Split(
                    new[] { "\r\n", "\n" },
                    StringSplitOptions.RemoveEmptyEntries)
                .ToArray();

            var values = new XElement("Values",
                lines.Select(value => new XElement("Value", value)));

            properties.AddAfterSelf(values);
        }

        var existingFilters = registry.Elements()
            .FirstOrDefault(element => element.Name.LocalName == "Filters");

        if (string.IsNullOrWhiteSpace(item.FiltersXml))
        {
            existingFilters?.Remove();
        }
        else
        {
            XElement parsedFilters;
            try
            {
                parsedFilters = XElement.Parse(
                    item.FiltersXml,
                    LoadOptions.PreserveWhitespace);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Item-level targeting XML is invalid: {ex.Message}",
                    ex);
            }

            if (!parsedFilters.Name.LocalName.Equals(
                    "Filters",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "Item-level targeting XML must have a <Filters> root element.");
            }

            if (existingFilters is null)
                registry.Add(parsedFilters);
            else
                existingFilters.ReplaceWith(parsedFilters);
        }
    }

    private static XElement? FindRegistryElement(
        IReadOnlyList<XElement> elements,
        GppRegistryItemInfo item)
    {
        if (!string.IsNullOrWhiteSpace(item.Uid))
        {
            var byUid = elements.FirstOrDefault(element =>
                Attr(element, "uid").Equals(
                    item.Uid,
                    StringComparison.OrdinalIgnoreCase));

            if (byUid is not null)
                return byUid;
        }

        if (item.Ordinal > 0 && item.Ordinal <= elements.Count)
            return elements[item.Ordinal - 1];

        return null;
    }

    private static void Validate(GppRegistryItemInfo item)
    {
        if (string.IsNullOrWhiteSpace(item.Hive))
            throw new InvalidOperationException("Registry hive cannot be empty.");

        var allowedHives = new[]
        {
            "HKEY_CLASSES_ROOT",
            "HKEY_CURRENT_USER",
            "HKEY_LOCAL_MACHINE",
            "HKEY_USERS",
            "HKEY_CURRENT_CONFIG"
        };

        if (!allowedHives.Contains(
                item.Hive.Trim(),
                StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Unsupported registry hive '{item.Hive}'.");
        }

        if (string.IsNullOrWhiteSpace(item.Key))
            throw new InvalidOperationException("Registry key cannot be empty.");

        var type = item.ValueType.Trim().ToUpperInvariant();
        var allowedTypes = new[]
        {
            string.Empty,
            "REG_SZ",
            "REG_DWORD",
            "REG_EXPAND_SZ",
            "REG_MULTI_SZ"
        };

        if (!allowedTypes.Contains(type, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"Unsupported Registry Preferences value type '{item.ValueType}'.");

        if (string.IsNullOrWhiteSpace(type) &&
            !string.IsNullOrWhiteSpace(item.ValueName))
        {
            throw new InvalidOperationException(
                "Value type is required when a registry value name is specified.");
        }

        if (type == "REG_DWORD")
            _ = ParseDword(item.ValueData);
    }

    private static string NormalizeValueForXml(
        GppRegistryItemInfo item,
        string type)
    {
        if (string.IsNullOrWhiteSpace(type))
            return string.Empty;

        if (type == "REG_DWORD")
            return ParseDword(item.ValueData).ToString("X8", CultureInfo.InvariantCulture);

        if (type == "REG_MULTI_SZ")
        {
            return string.Join(
                " ",
                item.ValueData.Split(
                    new[] { "\r\n", "\n" },
                    StringSplitOptions.RemoveEmptyEntries));
        }

        return item.ValueData ?? string.Empty;
    }

    private static uint ParseDword(string value)
    {
        var text = (value ?? string.Empty).Trim();

        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            if (uint.TryParse(
                    text[2..],
                    NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture,
                    out var hex))
                return hex;
        }
        else if (uint.TryParse(
                     text,
                     NumberStyles.Integer,
                     CultureInfo.InvariantCulture,
                     out var dec))
        {
            return dec;
        }

        throw new InvalidOperationException(
            "REG_DWORD value must be a decimal number or hexadecimal value prefixed with 0x.");
    }

    private static string FormatDwordForEditor(
        string raw,
        bool displayDecimal)
    {
        if (!uint.TryParse(
                raw,
                NumberStyles.HexNumber,
                CultureInfo.InvariantCulture,
                out var number))
            return raw;

        return displayDecimal
            ? number.ToString(CultureInfo.InvariantCulture)
            : $"0x{number:X8}";
    }

    private static string GetXmlPath(GpoInfo gpo, string scope)
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
            "Registry",
            "Registry.xml");
    }

    private static void WriteDocument(string path, XDocument document)
    {
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);

        var temp = Path.Combine(
            directory,
            $".Registry.{Guid.NewGuid():N}.tmp");

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

            var cse = RegistryPreferencesCseGuid;
            var tool = RegistryPreferencesToolGuid;
            var machine = !scope.Equals(
                "User",
                StringComparison.OrdinalIgnoreCase);

            ThrowIfFailed(
                policyObject.Save(
                    machine,
                    add,
                    ref cse,
                    ref tool));
        }
        finally
        {
            Marshal.FinalReleaseComObject(policyObject);
        }
    }

    private static string NormalizeAction(string action) =>
        action.Trim().ToUpperInvariant() switch
        {
            "C" or "CREATE" => "C",
            "D" or "DELETE" => "D",
            "R" or "REPLACE" => "R",
            _ => "U"
        };

    private static string Attr(XElement element, string name) =>
        element.Attributes()
            .FirstOrDefault(attribute =>
                attribute.Name.LocalName.Equals(
                    name,
                    StringComparison.OrdinalIgnoreCase))?
            .Value ?? string.Empty;

    private static bool IsTrue(string value) =>
        value == "1" ||
        value.Equals("true", StringComparison.OrdinalIgnoreCase);

    private static string FirstNonEmpty(params string[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))
        ?? string.Empty;

    private static void SetAttr(
        XElement element,
        string name,
        string value) =>
        element.SetAttributeValue(name, value ?? string.Empty);

    private static void SetOptionalBool(
        XElement element,
        string name,
        bool value)
    {
        if (value)
            element.SetAttributeValue(name, "1");
        else
            element.SetAttributeValue(name, null);
    }

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

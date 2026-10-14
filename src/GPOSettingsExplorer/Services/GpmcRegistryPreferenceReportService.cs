using System.Xml.Linq;
using GPOSettingsExplorer.Models;

namespace GPOSettingsExplorer.Services;

/// <summary>
/// Typed, read-only GPMC XML description for a Group Policy Preferences
/// Registry item. This is NOT Registry.pol and NOT live client registry.
/// Only a Registry node with direct GPP Properties and an exact hive/key/action
/// is accepted; generic XML keys must never be promoted to registry targets.
/// The stored value data is deliberately not included in report/index text.
/// </summary>
public static class GpmcRegistryPreferenceReportService
{
    public sealed record Item(
        string DisplayName, string Action, string Hive, string KeyPath,
        string ValueName, string ValueType, string Uid, bool HasFilters)
    {
        public string RegistryKey => Hive + "\\" + KeyPath;
        public string RegistryTarget => string.IsNullOrWhiteSpace(ValueName)
            ? RegistryKey : RegistryKey + " / " + ValueName;
        public string SourceSection => "Preferences > Windows Settings > Registry";
        public string Details => string.Join("\n", new[]
        {
            "Source: GPMC XML / Group Policy Preferences Registry (not Registry.pol)",
            "Setting: " + DisplayName,
            "Action: " + Action,
            "Hive: " + Hive,
            "Key path: " + KeyPath,
            "Value name: " + (ValueName.Length == 0 ? "(key/default item)" : ValueName),
            "Value type: " + (ValueType.Length == 0 ? "Not reported" : ValueType),
            "UID: " + (Uid.Length == 0 ? "Not reported" : Uid),
            "Item-level targeting filters: " + (HasFilters ? "Present; not evaluated" : "None reported"),
            "Value data: not copied into the index/report detail (may be sensitive)",
            "Stored GPO preference only. Client application / RSoP NOT VERIFIED."
        });
    }

    // Index metadata is deliberately separate from native Registry.pol
    // fields. Keeping the typed hive/key/value allows GPMC-style search
    // while preventing a false Registry.pol/RSoP correlation.
    public static PolicySettingInfo ToPolicySetting(
        GpoInfo gpo, string scope, Item item) => new()
    {
        GpoId = gpo.Id, GpoName = gpo.DisplayName,
        Scope = scope, Extension = "GppRegistry",
        Category = item.SourceSection,
        SettingName = "Registry preference: " + item.DisplayName,
        State = "Configured (GPP " + item.Action + ")",
        Value = "Action=" + item.Action + "; Hive=" + item.Hive +
            "; Key path=" + item.KeyPath +
            "; Value name=" + item.ValueName +
            "; Item targeting=" + (item.HasFilters ? "present" : "not reported") +
            "; source=GPMC GPP Registry (not Registry.pol)",
        GppRegistryHive = item.Hive,
        GppRegistryKeyPath = item.KeyPath,
        GppRegistryValueName = item.ValueName,
        GppRegistryAction = item.Action,
        GppRegistryUid = item.Uid
    };

    public static bool IsCandidate(XElement element) =>
        element.Name.LocalName.Equals("Registry", StringComparison.OrdinalIgnoreCase) &&
        element.Elements().Any(x => x.Name.LocalName.Equals(
            "Properties", StringComparison.OrdinalIgnoreCase) &&
            (Has(x, "hive") || Has(x, "key") || Has(x, "action")));

    public static bool TryRead(XElement element, out Item item)
    {
        item = null!;
        if (!IsCandidate(element))
            return false;

        var props = element.Elements()
            .Where(x => x.Name.LocalName.Equals("Properties", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (props.Length != 1)
            return false;
        var properties = props[0];
        var hive = Read(properties, "hive").Trim().ToUpperInvariant() switch
        {
            "HKLM" or "HKEY_LOCAL_MACHINE" => "HKEY_LOCAL_MACHINE",
            "HKCU" or "HKEY_CURRENT_USER" => "HKEY_CURRENT_USER",
            "HKCR" or "HKEY_CLASSES_ROOT" => "HKEY_CLASSES_ROOT",
            "HKU" or "HKEY_USERS" => "HKEY_USERS",
            "HKCC" or "HKEY_CURRENT_CONFIG" => "HKEY_CURRENT_CONFIG",
            _ => string.Empty
        };
        var action = Read(properties, "action").Trim().ToUpperInvariant() switch
        {
            "C" or "CREATE" => "Create",
            "U" or "UPDATE" => "Update",
            "R" or "REPLACE" => "Replace",
            "D" or "DELETE" => "Delete",
            _ => string.Empty
        };
        var key = Read(properties, "key").Trim();
        var valueName = Read(properties, "name").Trim();
        var name = Read(element, "name").Trim();
        var type = Read(properties, "type").Trim();
        var uidRaw = Read(element, "uid").Trim();
        var uid = Guid.TryParse(uidRaw, out var parsedGuid)
            ? parsedGuid.ToString("D") : string.Empty;
        if (hive.Length == 0 || action.Length == 0 ||
            !Valid(key, allowEmpty: false) ||
            !Valid(valueName, allowEmpty: true) ||
            !Valid(name, allowEmpty: true) ||
            !Valid(type, allowEmpty: true))
            return false;

        // Every GPP Registry item is one source item, not proof of a
        // deployed key. No environment-variable expansion or path guessing.
        item = new Item(
            name.Length == 0 ? (valueName.Length == 0 ? key : valueName) : name,
            action, hive, key, valueName, type, uid,
            element.Elements().Any(x => x.Name.LocalName.Equals(
                "Filters", StringComparison.OrdinalIgnoreCase)));
        return true;
    }

    private static string Read(XElement e, string field) =>
        e.Attributes().FirstOrDefault(a => a.Name.LocalName.Equals(
            field, StringComparison.OrdinalIgnoreCase))?.Value ??
        e.Elements().FirstOrDefault(x => x.Name.LocalName.Equals(
            field, StringComparison.OrdinalIgnoreCase))?.Value ?? string.Empty;

    private static bool Has(XElement e, string field) =>
        e.Attributes().Any(a => a.Name.LocalName.Equals(field, StringComparison.OrdinalIgnoreCase)) ||
        e.Elements().Any(x => x.Name.LocalName.Equals(field, StringComparison.OrdinalIgnoreCase));

    private static bool Valid(string value, bool allowEmpty) =>
        (allowEmpty || value.Length > 0) &&
        value.Length <= 4096 &&
        !value.Any(char.IsControl) &&
        !value.Contains('"');
}

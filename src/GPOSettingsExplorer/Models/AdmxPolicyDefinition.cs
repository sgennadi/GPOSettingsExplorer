using Microsoft.Win32;

namespace GPOSettingsExplorer.Models;

public enum AdmxElementType
{
    Text,
    Decimal,
    Boolean,
    Enum,
    List,
    Unknown
}

public sealed class AdmxRegistryValue
{
    public object? Value { get; init; }
    public RegistryValueKind Kind { get; init; } = RegistryValueKind.Unknown;
    public bool Delete { get; init; }
}

public sealed class AdmxEnumChoice
{
    public string DisplayName { get; init; } = string.Empty;
    public AdmxRegistryValue RegistryValue { get; init; } = new();
}

public sealed class AdmxElementDefinition
{
    public string Id { get; init; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public AdmxElementType Type { get; init; }
    public string Key { get; init; } = string.Empty;
    public string ValueName { get; init; } = string.Empty;
    public bool Required { get; init; }
    public long? MinValue { get; init; }
    public long? MaxValue { get; init; }
    public string ValuePrefix { get; init; } = string.Empty;
    public AdmxRegistryValue? TrueValue { get; init; }
    public AdmxRegistryValue? FalseValue { get; init; }
    public IReadOnlyList<AdmxEnumChoice> Choices { get; init; } = Array.Empty<AdmxEnumChoice>();
}

public sealed class AdmxPolicyDefinition
{
    public string AdmxFile { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Scope { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public string ExplainText { get; init; } = string.Empty;
    public string Key { get; init; } = string.Empty;
    public string ValueName { get; init; } = string.Empty;
    public AdmxRegistryValue? EnabledValue { get; init; }
    public AdmxRegistryValue? DisabledValue { get; init; }
    public IReadOnlyList<AdmxElementDefinition> Elements { get; init; } = Array.Empty<AdmxElementDefinition>();

    public string Identity => $"{Scope}|{DisplayName}";
}

public enum PolicyEditState
{
    NotConfigured,
    Enabled,
    Disabled
}

public sealed class PolicyEditValue
{
    public string ElementId { get; init; } = string.Empty;
    public object? Value { get; set; }
}

public sealed class PolicyEditSession
{
    public PolicyEditState State { get; set; }
    public Dictionary<string, object?> Values { get; } = new(StringComparer.OrdinalIgnoreCase);
}

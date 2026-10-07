using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using GPOSettingsExplorer.Models;
using Microsoft.Win32;

namespace GPOSettingsExplorer.Services;

public sealed class AdmxCatalogService
{
    public string LastSourcePath { get; private set; } = string.Empty;
    public string LastLanguage { get; private set; } = string.Empty;

    public IReadOnlyList<AdmxPolicyDefinition> Load(
        string domainName)
    {
        return Load(
            GetStoreState(
                domainName));
    }

    public IReadOnlyList<AdmxPolicyDefinition> Load(
        AdmxStoreState storeState)
    {
        LastSourcePath =
            storeState.SourcePath;

        LastLanguage =
            storeState.Language;

        var result =
            new List<AdmxPolicyDefinition>();

        foreach (var admxPath in Directory.EnumerateFiles(
                     storeState.SourcePath,
                     "*.admx",
                     SearchOption.TopDirectoryOnly))
        {
            try
            {
                result.AddRange(
                    ParseFile(
                        admxPath,
                        storeState.Language));
            }
            catch
            {
                // A single malformed or vendor-specific ADMX file must not block the catalog.
            }
        }

        return result
            .OrderBy(
                policy => policy.DisplayName,
                StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    public AdmxStoreState GetStoreState(
        string domainName)
    {
        var store =
            ResolvePolicyDefinitionsStore(
                domainName);

        var language =
            ResolveLanguage(
                store);

        return new AdmxStoreState
        {
            SourcePath = store,
            Language = language,
            Fingerprint =
                BuildStoreFingerprint(
                    store,
                    language)
        };
    }

    public void UseCachedStoreState(
        string sourcePath,
        string language)
    {
        LastSourcePath =
            sourcePath;

        LastLanguage =
            language;
    }

    public AdmxPolicyDefinition? Find(
        IEnumerable<AdmxPolicyDefinition> policies,
        PolicySettingInfo setting)
    {
        var policyArray =
            policies.ToArray();

        var exact =
            policyArray.FirstOrDefault(policy =>
                ScopeMatches(
                    policy.Scope,
                    setting.Scope) &&
                string.Equals(
                    policy.DisplayName,
                    setting.SettingName,
                    StringComparison.CurrentCultureIgnoreCase));

        if (exact is not null)
        {
            return exact;
        }

        var settingKey =
            NormalizeRegistryPath(
                setting.RegistryKey);

        var settingValue =
            NormalizeRegistryValueName(
                setting.RegistryValue);

        if (!string.IsNullOrWhiteSpace(settingKey) &&
            !string.IsNullOrWhiteSpace(settingValue))
        {
            var byRegistry =
                policyArray.FirstOrDefault(policy =>
                    ScopeMatches(
                        policy.Scope,
                        setting.Scope) &&
                    RegistryTargets(policy).Any(target =>
                        NormalizeRegistryPath(target.Key)
                            .Equals(
                                settingKey,
                                StringComparison.OrdinalIgnoreCase) &&
                        NormalizeRegistryValueName(target.ValueName)
                            .Equals(
                                settingValue,
                                StringComparison.OrdinalIgnoreCase)));

            if (byRegistry is not null)
            {
                return byRegistry;
            }
        }

        return policyArray.FirstOrDefault(policy =>
            ScopeMatches(
                policy.Scope,
                setting.Scope) &&
            string.Equals(
                policy.DisplayName,
                setting.SettingName,
                StringComparison.CurrentCultureIgnoreCase));
    }

    private static bool ScopeMatches(
        string policyScope,
        string settingScope)
    {
        return policyScope.Equals(
                   "Both",
                   StringComparison.OrdinalIgnoreCase) ||
               policyScope.Equals(
                   settingScope,
                   StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<(string Key, string ValueName)> RegistryTargets(
        AdmxPolicyDefinition policy)
    {
        if (!string.IsNullOrWhiteSpace(policy.Key) &&
            !string.IsNullOrWhiteSpace(policy.ValueName))
        {
            yield return (
                policy.Key,
                policy.ValueName);
        }

        foreach (var element in policy.Elements)
        {
            if (!string.IsNullOrWhiteSpace(element.Key) &&
                !string.IsNullOrWhiteSpace(element.ValueName))
            {
                yield return (
                    element.Key,
                    element.ValueName);
            }
        }
    }

    private static string NormalizeRegistryPath(
        string value)
    {
        var text =
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
            if (!text.StartsWith(
                    prefix,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            text =
                text[prefix.Length..];

            break;
        }

        return text.Trim('\\');
    }

    private static string NormalizeRegistryValueName(
        string value)
    {
        var text =
            (value ?? string.Empty)
            .Trim();

        var equals =
            text.IndexOf('=');

        return equals >= 0
            ? text[(equals + 1)..].Trim()
            : text;
    }

    private static string BuildStoreFingerprint(
        string store,
        string language)
    {
        var records =
            new List<string>();

        foreach (var path in Directory.EnumerateFiles(
                     store,
                     "*.admx",
                     SearchOption.TopDirectoryOnly)
                 .OrderBy(
                     value => value,
                     StringComparer.OrdinalIgnoreCase))
        {
            var info =
                new FileInfo(
                    path);

            records.Add(
                $"ADMX|{info.Name}|{info.Length}|{info.LastWriteTimeUtc.Ticks}");
        }

        if (!string.IsNullOrWhiteSpace(
                language))
        {
            var languagePath =
                Path.Combine(
                    store,
                    language);

            if (Directory.Exists(
                    languagePath))
            {
                foreach (var path in Directory.EnumerateFiles(
                             languagePath,
                             "*.adml",
                             SearchOption.TopDirectoryOnly)
                         .OrderBy(
                             value => value,
                             StringComparer.OrdinalIgnoreCase))
                {
                    var info =
                        new FileInfo(
                            path);

                    records.Add(
                        $"ADML|{language}|{info.Name}|{info.Length}|{info.LastWriteTimeUtc.Ticks}");
                }
            }
        }

        var payload =
            Encoding.UTF8.GetBytes(
                string.Join(
                    "\n",
                    records));

        return Convert.ToHexString(
            SHA256.HashData(
                payload));
    }

    private static string ResolvePolicyDefinitionsStore(string domainName)
    {
        var centralStore = $@"\\{domainName}\SYSVOL\{domainName}\Policies\PolicyDefinitions";
        if (Directory.Exists(centralStore))
        {
            return centralStore;
        }

        var localStore = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            "PolicyDefinitions");

        if (!Directory.Exists(localStore))
        {
            throw new DirectoryNotFoundException(
                "Neither the domain Central Store nor the local PolicyDefinitions directory is available.");
        }

        return localStore;
    }

    private static string ResolveLanguage(string store)
    {
        var candidates = new[]
        {
            CultureInfo.CurrentUICulture.Name,
            CultureInfo.CurrentCulture.Name,
            "en-US"
        }
        .Where(s => !string.IsNullOrWhiteSpace(s))
        .Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var candidate in candidates)
        {
            var path = Path.Combine(store, candidate);
            if (Directory.Exists(path))
            {
                return candidate;
            }
        }

        var firstLanguageDirectory = Directory
            .EnumerateDirectories(store)
            .FirstOrDefault(path => Directory.EnumerateFiles(path, "*.adml").Any());

        return firstLanguageDirectory is null
            ? string.Empty
            : Path.GetFileName(firstLanguageDirectory);
    }

    private static IEnumerable<AdmxPolicyDefinition> ParseFile(string admxPath, string language)
    {
        var document = XDocument.Load(admxPath, LoadOptions.None);
        var admlPath = string.IsNullOrWhiteSpace(language)
            ? string.Empty
            : Path.Combine(
                Path.GetDirectoryName(admxPath)!,
                language,
                Path.GetFileNameWithoutExtension(admxPath) + ".adml");

        var resources = LoadResources(admlPath);
        var presentationLabels = LoadPresentationLabels(admlPath);
        var categories = LoadCategories(document, resources);

        foreach (var policy in document.Descendants().Where(e => e.Name.LocalName == "policy"))
        {
            var displayName = ResolveResource(
                policy.Attribute("displayName")?.Value ?? policy.Attribute("name")?.Value ?? string.Empty,
                resources);

            var scope = NormalizeScope(policy.Attribute("class")?.Value);
            var policyKey = policy.Attribute("key")?.Value ?? string.Empty;
            var valueName = policy.Attribute("valueName")?.Value ?? string.Empty;
            var parentCategoryRef = policy.Elements()
                .FirstOrDefault(e => e.Name.LocalName == "parentCategory")?
                .Attribute("ref")?.Value ?? string.Empty;

            var category = ResolveCategoryPath(parentCategoryRef, categories);
            var explain = ResolveResource(policy.Attribute("explainText")?.Value ?? string.Empty, resources);
            var elements = ParseElements(policy, policyKey, resources, presentationLabels);

            yield return new AdmxPolicyDefinition
            {
                AdmxFile = Path.GetFileName(admxPath),
                Name = policy.Attribute("name")?.Value ?? string.Empty,
                DisplayName = displayName,
                Scope = scope,
                Category = category,
                ExplainText = explain,
                Key = policyKey,
                ValueName = valueName,
                EnabledValue = ParseContainerValue(
                    policy.Elements().FirstOrDefault(e => e.Name.LocalName == "enabledValue")),
                DisabledValue = ParseContainerValue(
                    policy.Elements().FirstOrDefault(e => e.Name.LocalName == "disabledValue")),
                Elements = elements
            };
        }
    }

    private static Dictionary<string, string> LoadResources(string admlPath)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(admlPath) || !File.Exists(admlPath))
        {
            return result;
        }

        var document = XDocument.Load(admlPath, LoadOptions.None);
        foreach (var item in document.Descendants().Where(e => e.Name.LocalName == "string"))
        {
            var id = item.Attribute("id")?.Value;
            if (!string.IsNullOrWhiteSpace(id))
            {
                result[id] = item.Value.Trim();
            }
        }

        return result;
    }

    private static Dictionary<string, Dictionary<string, string>> LoadPresentationLabels(string admlPath)
    {
        var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(admlPath) || !File.Exists(admlPath))
        {
            return result;
        }

        var document = XDocument.Load(admlPath, LoadOptions.None);

        foreach (var presentation in document.Descendants().Where(e => e.Name.LocalName == "presentation"))
        {
            var presentationId = presentation.Attribute("id")?.Value;
            if (string.IsNullOrWhiteSpace(presentationId))
            {
                continue;
            }

            var labels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var element in presentation.Descendants())
            {
                var refId = element.Attribute("refId")?.Value;
                if (string.IsNullOrWhiteSpace(refId))
                {
                    continue;
                }

                var label = element.Elements()
                    .FirstOrDefault(e => e.Name.LocalName == "label")?
                    .Value.Trim();

                if (string.IsNullOrWhiteSpace(label))
                {
                    label = element.Value.Trim();
                }

                if (!string.IsNullOrWhiteSpace(label))
                {
                    labels[refId] = label;
                }
            }

            result[presentationId] = labels;
        }

        return result;
    }

    private static Dictionary<string, CategoryRecord> LoadCategories(
        XDocument document,
        IReadOnlyDictionary<string, string> resources)
    {
        var categories = new Dictionary<string, CategoryRecord>(StringComparer.OrdinalIgnoreCase);

        foreach (var category in document.Descendants().Where(e => e.Name.LocalName == "category"))
        {
            var name = category.Attribute("name")?.Value;
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            categories[name] = new CategoryRecord(
                ResolveResource(category.Attribute("displayName")?.Value ?? name, resources),
                category.Elements()
                    .FirstOrDefault(e => e.Name.LocalName == "parentCategory")?
                    .Attribute("ref")?.Value ?? string.Empty);
        }

        return categories;
    }

    private static string ResolveCategoryPath(
        string categoryRef,
        IReadOnlyDictionary<string, CategoryRecord> categories)
    {
        if (string.IsNullOrWhiteSpace(categoryRef))
        {
            return string.Empty;
        }

        var current = StripNamespacePrefix(categoryRef);
        var parts = new List<string>();
        var guard = 0;

        while (!string.IsNullOrWhiteSpace(current) && guard++ < 64)
        {
            if (!categories.TryGetValue(current, out var category))
            {
                if (parts.Count == 0)
                {
                    parts.Add(current);
                }

                break;
            }

            parts.Add(category.DisplayName);
            current = StripNamespacePrefix(category.ParentRef);
        }

        parts.Reverse();
        return string.Join(" > ", parts);
    }

    private static IReadOnlyList<AdmxElementDefinition> ParseElements(
        XElement policy,
        string policyKey,
        IReadOnlyDictionary<string, string> resources,
        IReadOnlyDictionary<string, Dictionary<string, string>> presentationLabels)
    {
        var result = new List<AdmxElementDefinition>();
        var presentationId = policy.Attribute("presentation")?.Value;
        var resolvedPresentationId = ExtractResourceId(presentationId ?? string.Empty);

        presentationLabels.TryGetValue(resolvedPresentationId, out var labels);

        var elementsContainer = policy.Elements()
            .FirstOrDefault(e => e.Name.LocalName == "elements");

        if (elementsContainer is null)
        {
            return result;
        }

        foreach (var element in elementsContainer.Elements())
        {
            var id = element.Attribute("id")?.Value ?? string.Empty;
            var type = element.Name.LocalName switch
            {
                "text" => AdmxElementType.Text,
                "decimal" => AdmxElementType.Decimal,
                "boolean" => AdmxElementType.Boolean,
                "enum" => AdmxElementType.Enum,
                "list" => AdmxElementType.List,
                _ => AdmxElementType.Unknown
            };

            var choices = type == AdmxElementType.Enum
                ? ParseEnumChoices(element, resources)
                : Array.Empty<AdmxEnumChoice>();

            result.Add(new AdmxElementDefinition
            {
                Id = id,
                Label = labels is not null && labels.TryGetValue(id, out var label) ? label : id,
                Type = type,
                Key = element.Attribute("key")?.Value ?? policyKey,
                ValueName = element.Attribute("valueName")?.Value ?? string.Empty,
                Required = ParseBool(element.Attribute("required")?.Value),
                MinValue = ParseLong(element.Attribute("minValue")?.Value),
                MaxValue = ParseLong(element.Attribute("maxValue")?.Value),
                ValuePrefix = element.Attribute("valuePrefix")?.Value ?? string.Empty,
                TrueValue = ParseContainerValue(
                    element.Elements().FirstOrDefault(e => e.Name.LocalName == "trueValue")),
                FalseValue = ParseContainerValue(
                    element.Elements().FirstOrDefault(e => e.Name.LocalName == "falseValue")),
                Choices = choices
            });
        }

        return result;
    }

    private static AdmxEnumChoice[] ParseEnumChoices(
        XElement element,
        IReadOnlyDictionary<string, string> resources)
    {
        return element.Elements()
            .Where(e => e.Name.LocalName == "item")
            .Select(item => new AdmxEnumChoice
            {
                DisplayName = ResolveResource(
                    item.Attribute("displayName")?.Value ?? string.Empty,
                    resources),
                RegistryValue = ParseContainerValue(
                    item.Elements().FirstOrDefault(e => e.Name.LocalName == "value"))
                    ?? new AdmxRegistryValue()
            })
            .ToArray();
    }

    private static AdmxRegistryValue? ParseContainerValue(XElement? container)
    {
        if (container is null)
        {
            return null;
        }

        var value = container.Elements().FirstOrDefault();
        if (value is null)
        {
            return null;
        }

        return ParseValue(value);
    }

    private static AdmxRegistryValue ParseValue(XElement value)
    {
        switch (value.Name.LocalName)
        {
            case "decimal":
            case "longDecimal":
                return new AdmxRegistryValue
                {
                    Value = ParseLong(value.Attribute("value")?.Value) ?? 0L,
                    Kind = RegistryValueKind.DWord
                };

            case "string":
                return new AdmxRegistryValue
                {
                    Value = value.Attribute("value")?.Value ?? value.Value,
                    Kind = RegistryValueKind.String
                };

            case "delete":
                return new AdmxRegistryValue
                {
                    Delete = true,
                    Kind = RegistryValueKind.Unknown
                };

            default:
                return new AdmxRegistryValue
                {
                    Value = value.Attribute("value")?.Value ?? value.Value,
                    Kind = RegistryValueKind.String
                };
        }
    }

    private static string NormalizeScope(string? value)
    {
        return value?.ToLowerInvariant() switch
        {
            "machine" => "Computer",
            "user" => "User",
            "both" => "Both",
            _ => value ?? string.Empty
        };
    }

    private static bool ParseBool(string? value) =>
        string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, "1", StringComparison.OrdinalIgnoreCase);

    private static long? ParseLong(string? value) =>
        long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result)
            ? result
            : null;

    private static string ResolveResource(
        string value,
        IReadOnlyDictionary<string, string> resources)
    {
        var id = ExtractResourceId(value);
        return resources.TryGetValue(id, out var resolved) ? resolved : value;
    }

    private static string ExtractResourceId(string value)
    {
        const string prefix = "$(string.";
        if (value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && value.EndsWith(')'))
        {
            return value[prefix.Length..^1];
        }

        return value;
    }

    private static string StripNamespacePrefix(string value)
    {
        var index = value.IndexOf(':');
        return index >= 0 ? value[(index + 1)..] : value;
    }

    private sealed record CategoryRecord(string DisplayName, string ParentRef);
}

public sealed class AdmxStoreState
{
    public string SourcePath { get; init; } = string.Empty;
    public string Language { get; init; } = string.Empty;
    public string Fingerprint { get; init; } = string.Empty;
}


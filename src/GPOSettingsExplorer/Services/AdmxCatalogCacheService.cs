using System.Text.Json;
using System.Text.Json.Serialization;
using GPOSettingsExplorer.Models;
using Microsoft.Win32;

namespace GPOSettingsExplorer.Services;

public sealed class AdmxCatalogCacheService
{
    private const int SchemaVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions =
        CreateJsonOptions();

    public AdmxCatalogCacheSnapshot? Load(
        string domainName)
    {
        var path =
            GetPath(
                domainName);

        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var snapshot =
                JsonSerializer.Deserialize<AdmxCatalogCacheSnapshot>(
                    File.ReadAllText(path),
                    JsonOptions);

            if (snapshot is null ||
                snapshot.SchemaVersion != SchemaVersion ||
                !snapshot.DomainName.Equals(
                    domainName,
                    StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return snapshot;
        }
        catch
        {
            try
            {
                File.Move(
                    path,
                    path + ".corrupt-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss"),
                    overwrite: true);
            }
            catch
            {
            }

            return null;
        }
    }

    public void Save(
        string domainName,
        AdmxStoreState storeState,
        IReadOnlyList<AdmxPolicyDefinition> policies)
    {
        var snapshot =
            new AdmxCatalogCacheSnapshot
            {
                SchemaVersion = SchemaVersion,
                DomainName = domainName,
                SourcePath = storeState.SourcePath,
                Language = storeState.Language,
                Fingerprint = storeState.Fingerprint,
                GeneratedUtc = DateTime.UtcNow,
                Policies = policies.ToList()
            };

        var path =
            GetPath(
                domainName);

        var temp =
            path + "." + Guid.NewGuid().ToString("N") + ".tmp";

        File.WriteAllText(
            temp,
            JsonSerializer.Serialize(
                snapshot,
                JsonOptions));

        File.Move(
            temp,
            path,
            overwrite: true);
    }

    public string GetPath(
        string domainName)
    {
        var safeDomain =
            string.Concat(
                domainName.Select(character =>
                    char.IsLetterOrDigit(character) ||
                    character is '.' or '-' or '_'
                        ? character
                        : '_'));

        return Path.Combine(
            StoragePaths.Cache,
            $"admx-catalog-{safeDomain}.json");
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options =
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                WriteIndented = false
            };

        options.Converters.Add(
            new RegistryValueObjectConverter());

        return options;
    }

    private sealed class RegistryValueObjectConverter : JsonConverter<object>
    {
        public override object? Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options)
        {
            return reader.TokenType switch
            {
                JsonTokenType.Null => null,
                JsonTokenType.True => true,
                JsonTokenType.False => false,
                JsonTokenType.String => reader.GetString(),
                JsonTokenType.Number when reader.TryGetInt32(out var intValue) => intValue,
                JsonTokenType.Number when reader.TryGetInt64(out var longValue) => longValue,
                JsonTokenType.Number => reader.GetDouble(),
                _ => JsonDocument.ParseValue(ref reader).RootElement.Clone()
            };
        }

        public override void Write(
            Utf8JsonWriter writer,
            object value,
            JsonSerializerOptions options)
        {
            switch (value)
            {
                case string text:
                    writer.WriteStringValue(text);
                    break;
                case bool boolean:
                    writer.WriteBooleanValue(boolean);
                    break;
                case byte number:
                    writer.WriteNumberValue(number);
                    break;
                case short number:
                    writer.WriteNumberValue(number);
                    break;
                case int number:
                    writer.WriteNumberValue(number);
                    break;
                case long number:
                    writer.WriteNumberValue(number);
                    break;
                case uint number:
                    writer.WriteNumberValue(number);
                    break;
                case ulong number:
                    writer.WriteNumberValue(number);
                    break;
                case float number:
                    writer.WriteNumberValue(number);
                    break;
                case double number:
                    writer.WriteNumberValue(number);
                    break;
                case decimal number:
                    writer.WriteNumberValue(number);
                    break;
                default:
                    JsonSerializer.Serialize(
                        writer,
                        value,
                        value.GetType(),
                        options);
                    break;
            }
        }
    }
}

public sealed class AdmxCatalogCacheSnapshot
{
    public int SchemaVersion { get; init; }
    public string DomainName { get; init; } = string.Empty;
    public string SourcePath { get; init; } = string.Empty;
    public string Language { get; init; } = string.Empty;
    public string Fingerprint { get; init; } = string.Empty;
    public DateTime GeneratedUtc { get; init; }
    public List<AdmxPolicyDefinition> Policies { get; init; } = new();
}

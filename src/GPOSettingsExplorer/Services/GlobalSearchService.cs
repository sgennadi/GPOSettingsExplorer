using System.Collections;
using System.Collections.ObjectModel;
using System.Reflection;

namespace GPOSettingsExplorer.Services;

public sealed record GlobalSearchResult(
    string Category,
    string Title,
    string Details,
    object Source);

public sealed class GlobalSearchService
{
    public IReadOnlyList<GlobalSearchResult> Search(
        object owner,
        string query)
    {
        if (string.IsNullOrWhiteSpace(
                query))
        {
            return Array.Empty<GlobalSearchResult>();
        }

        var normalized =
            query.Trim();

        var result =
            new List<GlobalSearchResult>();

        var fields =
            owner.GetType()
                .GetFields(
                    BindingFlags.Instance |
                    BindingFlags.NonPublic |
                    BindingFlags.Public);

        foreach (var field in fields)
        {
            if (!IsObservableCollection(
                    field.FieldType))
            {
                continue;
            }

            if (field.GetValue(
                    owner) is not IEnumerable collection)
            {
                continue;
            }

            foreach (var item in collection)
            {
                if (item is null ||
                    item.GetType().Namespace is not string ns ||
                    !ns.StartsWith(
                        "GPOSettingsExplorer",
                        StringComparison.Ordinal))
                {
                    continue;
                }

                var searchText =
                    BuildSearchText(
                        item);

                if (!searchText.Contains(
                        normalized,
                        StringComparison.CurrentCultureIgnoreCase))
                {
                    continue;
                }

                result.Add(
                    new GlobalSearchResult(
                        FriendlyCategory(
                            item.GetType()),
                        BuildTitle(
                            item),
                        Truncate(
                            searchText,
                            700),
                        item));
            }
        }

        return result
            .DistinctBy(
                item =>
                    item.Source)
            .OrderBy(
                item =>
                    item.Category,
                StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(
                item =>
                    item.Title,
                StringComparer.CurrentCultureIgnoreCase)
            .Take(
                2000)
            .ToArray();
    }

    private static bool IsObservableCollection(
        Type type) =>
        type.IsGenericType &&
        type.GetGenericTypeDefinition() ==
        typeof(ObservableCollection<>);

    private static string BuildSearchText(
        object item)
    {
        var type =
            item.GetType();

        var searchText =
            type.GetProperty(
                "SearchText",
                BindingFlags.Instance |
                BindingFlags.Public);

        if (searchText?.PropertyType ==
            typeof(string))
        {
            try
            {
                return Convert.ToString(
                           searchText.GetValue(
                               item))
                       ?? string.Empty;
            }
            catch
            {
            }
        }

        var values =
            new List<string>();

        foreach (var property in type.GetProperties(
                     BindingFlags.Instance |
                     BindingFlags.Public))
        {
            if (!property.CanRead ||
                property.GetIndexParameters().Length != 0)
            {
                continue;
            }

            if (!IsSimple(
                    property.PropertyType))
            {
                continue;
            }

            try
            {
                var value =
                    Convert.ToString(
                        property.GetValue(
                            item));

                if (!string.IsNullOrWhiteSpace(
                        value))
                {
                    values.Add(
                        value);
                }
            }
            catch
            {
            }
        }

        return string.Join(
            " | ",
            values);
    }

    private static string BuildTitle(
        object item)
    {
        foreach (var name in new[]
                 {
                     "DisplayName",
                     "SettingName",
                     "Name",
                     "FileName",
                     "GpoName",
                     "PreferenceType",
                     "Path",
                     "IdText"
                 })
        {
            try
            {
                var property =
                    item.GetType()
                        .GetProperty(
                            name);

                var value =
                    Convert.ToString(
                        property?.GetValue(
                            item));

                if (!string.IsNullOrWhiteSpace(
                        value))
                {
                    return value;
                }
            }
            catch
            {
            }
        }

        return item.GetType().Name;
    }

    private static string FriendlyCategory(
        Type type)
    {
        var name =
            type.Name;

        foreach (var suffix in new[]
                 {
                     "ItemInfo",
                     "Info",
                     "Item"
                 })
        {
            if (name.EndsWith(
                    suffix,
                    StringComparison.Ordinal))
            {
                name =
                    name[..^suffix.Length];

                break;
            }
        }

        return name;
    }

    private static bool IsSimple(
        Type type)
    {
        var underlying =
            Nullable.GetUnderlyingType(
                type)
            ?? type;

        return underlying.IsPrimitive ||
               underlying.IsEnum ||
               underlying ==
               typeof(string) ||
               underlying ==
               typeof(Guid) ||
               underlying ==
               typeof(DateTime) ||
               underlying ==
               typeof(DateTimeOffset) ||
               underlying ==
               typeof(decimal);
    }

    private static string Truncate(
        string text,
        int length)
    {
        if (text.Length <=
            length)
        {
            return text;
        }

        return text[..length];
    }
}

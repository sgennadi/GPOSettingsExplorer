using GPOSettingsExplorer.Models;
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
    public IReadOnlyList<object> CaptureItems(
        object owner)
    {
        var items =
            new List<object>();

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

                items.Add(
                    item);
            }
        }

        return items;
    }

    public IReadOnlyList<GlobalSearchResult> Search(
        object owner,
        string query) =>
        Search(
            CaptureItems(
                owner),
            query);

    public IReadOnlyList<GlobalSearchResult> Search(
        IReadOnlyList<object> items,
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

        foreach (var item in items)
        {
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
                    BuildMatchDetails(
                        searchText,
                        normalized),
                    item));
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

    private static string BuildMatchDetails(
        string text,
        string query)
    {
        const int radius =
            280;

        var index =
            text.IndexOf(
                query,
                StringComparison.CurrentCultureIgnoreCase);

        if (index < 0)
        {
            return Truncate(
                text,
                700);
        }

        var start =
            Math.Max(
                0,
                index -
                radius);

        var length =
            Math.Min(
                text.Length -
                start,
                700);

        var snippet =
            text.Substring(
                    start,
                    length)
                .Replace(
                    "\r",
                    " ",
                    StringComparison.Ordinal)
                .Replace(
                    "\n",
                    " ",
                    StringComparison.Ordinal);

        if (start > 0)
        {
            snippet =
                "... " +
                snippet;
        }

        if (start + length <
            text.Length)
        {
            snippet +=
                " ...";
        }

        return snippet;
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

        if (item is GppDocumentInfo gppDocument)
        {
            var metadata =
                $"{gppDocument.SearchText} {gppDocument.XmlPath}";

            try
            {
                return metadata +
                       " " +
                       GppXmlCacheService.ReadText(
                           gppDocument.XmlPath);
            }
            catch
            {
                return metadata;
            }
        }

        if (item is GpoScriptInfo script)
        {
            var metadata =
                $"{script.GpoName} {script.Scope} {script.EventName} {script.FileName} " +
                $"{script.Parameters} {script.FullPath}";

            if (!script.Exists)
            {
                return metadata;
            }

            try
            {
                var document =
                    new GpoScriptService()
                        .ReadDocument(
                            script.FullPath);

                return metadata +
                       " " +
                       document.Text;
            }
            catch
            {
                return metadata;
            }
        }

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

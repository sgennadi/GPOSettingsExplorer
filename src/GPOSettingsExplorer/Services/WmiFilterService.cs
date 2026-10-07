using System.Diagnostics;
using System.DirectoryServices;
using System.Globalization;
using System.Management;
using GPOSettingsExplorer.Models;

namespace GPOSettingsExplorer.Services;

public sealed class WmiFilterService
{
    public IReadOnlyList<WmiFilterInfo> LoadFilters(string domainName)
    {
        // WMI filters are directory objects (msWMI-Som). Reading them through
        // LDAP avoids depending on the local root\\policy PolicSOM provider,
        // which can return WBEM_E_PROVIDER_NOT_CAPABLE on otherwise healthy
        // management hosts.
        using var rootDse =
            new DirectoryEntry(
                DomainConnectionState.BuildRootDsePath());

        var defaultNamingContext =
            Convert.ToString(
                rootDse.Properties[
                    "defaultNamingContext"].Value)
            ?? throw new InvalidOperationException(
                "The Active Directory default naming context is unavailable.");

        using var systemContainer =
            new DirectoryEntry(
                DomainConnectionState.BuildLdapPath(
                    $"CN=System,{defaultNamingContext}"));

        using var searcher =
            new DirectorySearcher(systemContainer)
            {
                Filter = "(objectClass=msWMI-Som)",
                SearchScope = SearchScope.Subtree,
                PageSize = 500,
                CacheResults = false
            };

        foreach (var propertyName in new[]
                 {
                     "cn",
                     "msWMI-ID",
                     "msWMI-Name",
                     "msWMI-Parm1",
                     "msWMI-Parm2",
                     "msWMI-Author",
                     "msWMI-SourceOrganization",
                     "msWMI-CreationDate",
                     "msWMI-ChangeDate"
                 })
        {
            searcher.PropertiesToLoad.Add(
                propertyName);
        }

        using var results =
            searcher.FindAll();

        var filters =
            new List<WmiFilterInfo>();

        foreach (SearchResult result in results)
        {
            filters.Add(
                ToInfo(
                    result,
                    domainName));
        }

        return filters
            .OrderBy(
                filter => filter.Name,
                StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    public WmiFilterInfo Save(
        string domainName,
        WmiFilterInfo filter)
    {
        EditingGuard.EnsureEnabled(
            "Edit WMI filter");

        Validate(
            filter);

        var existing =
            string.IsNullOrWhiteSpace(
                filter.Id)
                ? null
                : LoadFilters(
                        domainName)
                    .FirstOrDefault(
                        candidate =>
                            candidate.Id.Equals(
                                filter.Id,
                                StringComparison.OrdinalIgnoreCase));

        ChangePreviewGuard.Confirm(
            new ChangePreviewRequest(
                existing is null
                    ? "Create WMI filter"
                    : "Edit WMI filter",
                domainName,
                existing is null
                    ? "<not present>"
                    : WmiSummary(
                        existing),
                WmiSummary(
                    filter),
                existing is null
                    ? "A new msWMI-Som object will be created on the connected domain controller."
                    : $"WMI filter ID: {filter.Id}",
                existing is null
                    ? "Create"
                    : "Apply"));

        var domainDn =
            GetDomainDistinguishedName(
                domainName);

        var containerDn =
            $"CN=SOM,CN=WMIPolicy,CN=System,{domainDn}";

        using var container =
            new DirectoryEntry(
                DomainConnectionState.BuildLdapPath(
                    containerDn));

        container.RefreshCache();

        var now =
            DateTime.Now;

        var isNew =
            string.IsNullOrWhiteSpace(
                filter.Id);

        if (isNew)
        {
            filter.Id =
                Guid.NewGuid()
                    .ToString("B")
                    .ToUpperInvariant();
        }

        var objectDn =
            $"CN={filter.Id},{containerDn}";

        using var target =
            isNew
                ? container.Children.Add(
                    $"CN={filter.Id}",
                    "msWMI-Som")
                : new DirectoryEntry(
                    DomainConnectionState.BuildLdapPath(
                        objectDn));

        if (!isNew)
        {
            target.RefreshCache();
        }

        var author =
            string.IsNullOrWhiteSpace(
                filter.Author)
                ? $"{Environment.UserDomainName}\\{Environment.UserName}"
                : filter.Author.Trim();

        var sourceOrganization =
            string.IsNullOrWhiteSpace(
                filter.SourceOrganization)
                ? domainName
                : filter.SourceOrganization.Trim();

        SetDirectoryValue(
            target,
            "msWMI-ID",
            filter.Id);

        SetDirectoryValue(
            target,
            "msWMI-Name",
            filter.Name.Trim());

        SetDirectoryValue(
            target,
            "msWMI-Parm1",
            filter.Description
            ?? string.Empty);

        SetDirectoryValue(
            target,
            "msWMI-Parm2",
            SerializeDirectoryRules(
                filter.Rules));

        SetDirectoryValue(
            target,
            "msWMI-Author",
            author);

        SetDirectoryValue(
            target,
            "msWMI-SourceOrganization",
            sourceOrganization);

        if (isNew)
        {
            SetDirectoryValue(
                target,
                "msWMI-CreationDate",
                ManagementDateTimeConverter.ToDmtfDateTime(
                    now));
        }

        SetDirectoryValue(
            target,
            "msWMI-ChangeDate",
            ManagementDateTimeConverter.ToDmtfDateTime(
                now));

        target.CommitChanges();

        filter.Domain =
            domainName;

        filter.Author =
            author;

        filter.SourceOrganization =
            sourceOrganization;

        if (isNew)
        {
            filter.CreationDate =
                now;
        }

        filter.ChangeDate =
            now;

        return filter.Clone();
    }

    public WmiFilterInfo Clone(string domainName, WmiFilterInfo source, string newName)
    {
        EditingGuard.EnsureEnabled(
            "Clone WMI filter");
        var clone = source.Clone();
        clone.Id = string.Empty;
        clone.Name = newName;
        clone.CreationDate = null;
        clone.ChangeDate = null;
        clone.Author = $"{Environment.UserDomainName}\\{Environment.UserName}";
        return Save(domainName, clone);
    }

    public void Delete(
        string domainName,
        string id)
    {
        EditingGuard.EnsureEnabled(
            "Delete WMI filter");

        var existing =
            LoadFilters(
                    domainName)
                .FirstOrDefault(
                    candidate =>
                        candidate.Id.Equals(
                            id,
                            StringComparison.OrdinalIgnoreCase));

        if (existing is null)
        {
            return;
        }

        ChangePreviewGuard.Confirm(
            new ChangePreviewRequest(
                "Delete WMI filter",
                existing.Name,
                WmiSummary(
                    existing),
                "<deleted>",
                $"WMI filter ID: {existing.Id}",
                "Delete"));

        var domainDn =
            GetDomainDistinguishedName(
                domainName);

        var objectDn =
            $"CN={existing.Id},CN=SOM,CN=WMIPolicy,CN=System,{domainDn}";

        using var target =
            new DirectoryEntry(
                DomainConnectionState.BuildLdapPath(
                    objectDn));

        target.DeleteTree();
    }

    public IReadOnlyList<WmiTestResult> Test(WmiFilterInfo filter, string computerName)
    {
        Validate(filter);

        var results = new List<WmiTestResult>();

        for (var i = 0; i < filter.Rules.Count; i++)
        {
            var rule = filter.Rules[i];
            var watch = Stopwatch.StartNew();
            var matched = false;
            var error = string.Empty;

            try
            {
                var targetNamespace = NormalizeNamespace(rule.TargetNamespace);
                var path = string.Equals(computerName, ".", StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(computerName, "localhost", StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(computerName, Environment.MachineName, StringComparison.OrdinalIgnoreCase)
                    ? $@"\\.\{targetNamespace}"
                    : $@"\\{computerName}\{targetNamespace}";

                var scope = new ManagementScope(path);
                scope.Connect();

                using var searcher = new ManagementObjectSearcher(
                    scope,
                    new ObjectQuery(rule.Query));

                using var queryResults = searcher.Get();
                using var enumerator = queryResults.GetEnumerator();
                matched = enumerator.MoveNext();
            }
            catch (Exception ex)
            {
                error = ex.Message;
            }
            finally
            {
                watch.Stop();
            }

            results.Add(new WmiTestResult
            {
                RuleNumber = i + 1,
                Namespace = rule.TargetNamespace,
                Query = rule.Query,
                IsMatch = matched,
                Error = error,
                Duration = watch.Elapsed
            });
        }

        return results;
    }

    private static WmiFilterInfo ToInfo(
        SearchResult result,
        string domainName)
    {
        var id =
            ReadDirectoryProperty(
                result,
                "msWMI-ID");

        if (string.IsNullOrWhiteSpace(id))
        {
            id =
                ReadDirectoryProperty(
                    result,
                    "cn");
        }

        var serializedRules =
            ReadDirectoryProperty(
                result,
                "msWMI-Parm2");

        return new WmiFilterInfo
        {
            Id = id,
            Domain = domainName,
            Name =
                ReadDirectoryProperty(
                    result,
                    "msWMI-Name"),
            Description =
                ReadDirectoryProperty(
                    result,
                    "msWMI-Parm1")
                .TrimEnd(),
            Author =
                ReadDirectoryProperty(
                    result,
                    "msWMI-Author"),
            SourceOrganization =
                ReadDirectoryProperty(
                    result,
                    "msWMI-SourceOrganization"),
            CreationDate =
                ParseWmiDate(
                    ReadDirectoryProperty(
                        result,
                        "msWMI-CreationDate")),
            ChangeDate =
                ParseWmiDate(
                    ReadDirectoryProperty(
                        result,
                        "msWMI-ChangeDate")),
            Rules =
                ParseDirectoryRules(
                    serializedRules)
        };
    }

    private static string ReadDirectoryProperty(
        SearchResult result,
        string propertyName)
    {
        var values =
            result.Properties[propertyName];

        return values.Count == 0
            ? string.Empty
            : Convert.ToString(
                  values[0],
                  CultureInfo.InvariantCulture)
              ?? string.Empty;
    }

    private static System.Collections.ObjectModel.ObservableCollection<WmiRuleInfo>
        ParseDirectoryRules(string serialized)
    {
        var rules =
            new System.Collections.ObjectModel.ObservableCollection<WmiRuleInfo>();

        if (string.IsNullOrWhiteSpace(serialized))
        {
            return rules;
        }

        var offset = 0;

        if (!TryReadNumber(
                serialized,
                ref offset,
                out var ruleCount) ||
            ruleCount < 0)
        {
            return rules;
        }

        for (var index = 0;
             index < ruleCount;
             index++)
        {
            if (!TryReadNumber(
                    serialized,
                    ref offset,
                    out var languageLength) ||
                !TryReadNumber(
                    serialized,
                    ref offset,
                    out var namespaceLength) ||
                !TryReadNumber(
                    serialized,
                    ref offset,
                    out var queryLength) ||
                !TryReadSizedField(
                    serialized,
                    ref offset,
                    languageLength,
                    out var language) ||
                !TryReadSizedField(
                    serialized,
                    ref offset,
                    namespaceLength,
                    out var targetNamespace) ||
                !TryReadSizedField(
                    serialized,
                    ref offset,
                    queryLength,
                    out var query))
            {
                // Do not make one malformed legacy filter prevent the rest of
                // the domain's filters from loading.
                break;
            }

            rules.Add(
                new WmiRuleInfo
                {
                    QueryLanguage =
                        string.IsNullOrWhiteSpace(language)
                            ? "WQL"
                            : language,
                    TargetNamespace =
                        string.IsNullOrWhiteSpace(targetNamespace)
                            ? @"root\CIMv2"
                            : targetNamespace,
                    Query = query
                });
        }

        return rules;
    }

    private static bool TryReadNumber(
        string value,
        ref int offset,
        out int number)
    {
        number = 0;

        if (offset < 0 ||
            offset >= value.Length)
        {
            return false;
        }

        var separator =
            value.IndexOf(
                ';',
                offset);

        if (separator < 0)
        {
            return false;
        }

        var token =
            value.AsSpan(
                offset,
                separator - offset);

        if (!int.TryParse(
                token,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out number))
        {
            return false;
        }

        offset =
            separator + 1;

        return true;
    }

    private static bool TryReadSizedField(
        string value,
        ref int offset,
        int length,
        out string field)
    {
        field = string.Empty;

        if (length < 0 ||
            offset < 0 ||
            offset + length > value.Length)
        {
            return false;
        }

        field =
            value.Substring(
                offset,
                length);

        offset +=
            length;

        if (offset < value.Length &&
            value[offset] == ';')
        {
            offset++;
            return true;
        }

        return offset ==
               value.Length;
    }

    private static string GetDomainDistinguishedName(
        string domainName)
    {
        var context =
            DomainConnectionState.Context;

        if (context is not null &&
            context.DomainName.Equals(
                domainName,
                StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(
                context.DomainDistinguishedName))
        {
            return context.DomainDistinguishedName;
        }

        using var rootDse =
            new DirectoryEntry(
                DomainConnectionState.BuildRootDsePath());

        rootDse.RefreshCache(
            new[]
            {
                "defaultNamingContext"
            });

        return Convert.ToString(
                   rootDse.Properties[
                       "defaultNamingContext"].Value)
               ?? throw new InvalidOperationException(
                   "The Active Directory default naming context is unavailable.");
    }

    private static void SetDirectoryValue(
        DirectoryEntry entry,
        string name,
        string value)
    {
        entry.Properties[
                name].Value =
            value
            ?? string.Empty;
    }

    private static string SerializeDirectoryRules(
        IEnumerable<WmiRuleInfo> rules)
    {
        var normalized =
            rules
                .Select(
                    rule =>
                        new
                        {
                            Language =
                                string.IsNullOrWhiteSpace(
                                    rule.QueryLanguage)
                                    ? "WQL"
                                    : rule.QueryLanguage.Trim(),
                            Namespace =
                                NormalizeNamespace(
                                    rule.TargetNamespace),
                            Query =
                                rule.Query.Trim()
                        })
                .ToArray();

        var builder =
            new System.Text.StringBuilder();

        builder.Append(
            normalized.Length.ToString(
                CultureInfo.InvariantCulture));

        builder.Append(
            ';');

        foreach (var rule in normalized)
        {
            builder.Append(
                rule.Language.Length.ToString(
                    CultureInfo.InvariantCulture));

            builder.Append(
                ';');

            builder.Append(
                rule.Namespace.Length.ToString(
                    CultureInfo.InvariantCulture));

            builder.Append(
                ';');

            builder.Append(
                rule.Query.Length.ToString(
                    CultureInfo.InvariantCulture));

            builder.Append(
                ';');

            builder.Append(
                rule.Language);

            builder.Append(
                ';');

            builder.Append(
                rule.Namespace);

            builder.Append(
                ';');

            builder.Append(
                rule.Query);

            builder.Append(
                ';');
        }

        return builder.ToString();
    }

    private static string WmiSummary(
        WmiFilterInfo filter)
    {
        var rules =
            filter.Rules
                .Select(
                    (rule, index) =>
                        $"{index + 1}. [{rule.TargetNamespace}] {rule.Query}");

        return
            $"Name: {filter.Name}\n" +
            $"Description: {filter.Description}\n" +
            $"Author: {filter.Author}\n" +
            $"Source organization: {filter.SourceOrganization}\n" +
            $"Rules:\n{string.Join("\n", rules)}";
    }

    private static ManagementScope CreatePolicyScope()
    {
        var scope = new ManagementScope(@"\\.\root\policy");
        scope.Connect();
        return scope;
    }

    private static ManagementObject GetFilterObject(ManagementScope scope, string domainName, string id)
    {
        var escapedDomain = EscapeWmiKey(domainName);
        var escapedId = EscapeWmiKey(id);
        var path = new ManagementPath(
            $"MSFT_SomFilter.Domain=\"{escapedDomain}\",ID=\"{escapedId}\"");

        return new ManagementObject(scope, path, null);
    }

    private static ManagementBaseObject[] BuildRules(
        ManagementScope scope,
        IEnumerable<WmiRuleInfo> rules)
    {
        using var ruleClass = new ManagementClass(
            scope,
            new ManagementPath("MSFT_Rule"),
            null);

        var result = new List<ManagementBaseObject>();

        foreach (var rule in rules)
        {
            var item = ruleClass.CreateInstance()
                ?? throw new InvalidOperationException("Unable to create a WMI rule instance.");

            item["QueryLanguage"] = "WQL";
            item["TargetNamespace"] = NormalizeNamespace(rule.TargetNamespace);
            item["Query"] = rule.Query.Trim();
            result.Add(item);
        }

        return result.ToArray();
    }

    private static WmiFilterInfo ToInfo(ManagementObject item)
    {
        var rules = new System.Collections.ObjectModel.ObservableCollection<WmiRuleInfo>();

        if (item["Rules"] is Array rawRules)
        {
            foreach (var raw in rawRules)
            {
                if (raw is not ManagementBaseObject rule)
                {
                    continue;
                }

                rules.Add(new WmiRuleInfo
                {
                    QueryLanguage = Convert.ToString(rule["QueryLanguage"]) ?? "WQL",
                    TargetNamespace = Convert.ToString(rule["TargetNamespace"]) ?? @"root\CIMv2",
                    Query = Convert.ToString(rule["Query"]) ?? string.Empty
                });
            }
        }

        return new WmiFilterInfo
        {
            Id = Convert.ToString(item["ID"]) ?? string.Empty,
            Domain = Convert.ToString(item["Domain"]) ?? string.Empty,
            Name = Convert.ToString(item["Name"]) ?? string.Empty,
            Description = Convert.ToString(item["Description"]) ?? string.Empty,
            Author = Convert.ToString(item["Author"]) ?? string.Empty,
            SourceOrganization = Convert.ToString(item["SourceOrganization"]) ?? string.Empty,
            CreationDate = ParseWmiDate(item["CreationDate"]),
            ChangeDate = ParseWmiDate(item["ChangeDate"]),
            Rules = rules
        };
    }

    private static DateTime? ParseWmiDate(object? value)
    {
        var text = Convert.ToString(value);
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        try
        {
            return ManagementDateTimeConverter.ToDateTime(text);
        }
        catch
        {
            return null;
        }
    }

    private static void Validate(WmiFilterInfo filter)
    {
        if (string.IsNullOrWhiteSpace(filter.Name))
        {
            throw new InvalidOperationException("WMI filter name cannot be empty.");
        }

        if (filter.Rules.Count == 0)
        {
            throw new InvalidOperationException("A WMI filter must contain at least one WQL rule.");
        }

        foreach (var rule in filter.Rules)
        {
            if (!string.Equals(rule.QueryLanguage, "WQL", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Only WQL rules are supported.");
            }

            if (string.IsNullOrWhiteSpace(rule.TargetNamespace))
            {
                throw new InvalidOperationException("WMI namespace cannot be empty.");
            }

            if (string.IsNullOrWhiteSpace(rule.Query))
            {
                throw new InvalidOperationException("WQL query cannot be empty.");
            }
        }
    }

    private static string NormalizeNamespace(string value)
    {
        var text = value.Trim().TrimStart('\\');
        if (text.StartsWith(".\\", StringComparison.Ordinal))
        {
            text = text[2..];
        }

        return text;
    }

    private static string EscapeWmiKey(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
             .Replace("\"", "\\\"", StringComparison.Ordinal);
}

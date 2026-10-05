using System.Diagnostics;
using System.Management;
using GPOSettingsExplorer.Models;

namespace GPOSettingsExplorer.Services;

public sealed class WmiFilterService
{
    public IReadOnlyList<WmiFilterInfo> LoadFilters(string domainName)
    {
        var scope = CreatePolicyScope();
        using var searcher = new ManagementObjectSearcher(
            scope,
            new ObjectQuery("SELECT * FROM MSFT_SomFilter"));

        using var results = searcher.Get();
        var filters = new List<WmiFilterInfo>();

        foreach (ManagementObject item in results)
        {
            var itemDomain = Convert.ToString(item["Domain"]) ?? string.Empty;
            if (!string.Equals(itemDomain, domainName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            filters.Add(ToInfo(item));
        }

        return filters
            .OrderBy(f => f.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    public WmiFilterInfo Save(string domainName, WmiFilterInfo filter)
    {
        Validate(filter);

        var scope = CreatePolicyScope();
        var now = DateTime.Now;
        ManagementObject target;

        if (string.IsNullOrWhiteSpace(filter.Id))
        {
            using var filterClass = new ManagementClass(
                scope,
                new ManagementPath("MSFT_SomFilter"),
                null);

            target = filterClass.CreateInstance()
                ?? throw new InvalidOperationException("Unable to create a WMI filter instance.");

            filter.Id = Guid.NewGuid().ToString("B").ToUpperInvariant();
            target["ID"] = filter.Id;
            target["Domain"] = domainName;
            target["CreationDate"] = ManagementDateTimeConverter.ToDmtfDateTime(now);
        }
        else
        {
            target = GetFilterObject(scope, domainName, filter.Id);
            target.Get();
        }

        using (target)
        {
            target["Name"] = filter.Name.Trim();
            target["Description"] = filter.Description ?? string.Empty;
            target["Author"] = string.IsNullOrWhiteSpace(filter.Author)
                ? $"{Environment.UserDomainName}\\{Environment.UserName}"
                : filter.Author.Trim();
            target["SourceOrganization"] = string.IsNullOrWhiteSpace(filter.SourceOrganization)
                ? domainName
                : filter.SourceOrganization.Trim();
            target["ChangeDate"] = ManagementDateTimeConverter.ToDmtfDateTime(now);
            target["Rules"] = BuildRules(scope, filter.Rules);

            target.Put();
            target.Get();
            return ToInfo(target);
        }
    }

    public WmiFilterInfo Clone(string domainName, WmiFilterInfo source, string newName)
    {
        var clone = source.Clone();
        clone.Id = string.Empty;
        clone.Name = newName;
        clone.CreationDate = null;
        clone.ChangeDate = null;
        clone.Author = $"{Environment.UserDomainName}\\{Environment.UserName}";
        return Save(domainName, clone);
    }

    public void Delete(string domainName, string id)
    {
        var scope = CreatePolicyScope();
        using var target = GetFilterObject(scope, domainName, id);
        target.Delete();
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

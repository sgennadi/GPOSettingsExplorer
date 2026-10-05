using System.Collections.ObjectModel;

namespace GPOSettingsExplorer.Models;

public sealed class WmiRuleInfo
{
    public string QueryLanguage { get; set; } = "WQL";
    public string TargetNamespace { get; set; } = @"root\CIMv2";
    public string Query { get; set; } = string.Empty;

    public WmiRuleInfo Clone() => new()
    {
        QueryLanguage = QueryLanguage,
        TargetNamespace = TargetNamespace,
        Query = Query
    };
}

public sealed class WmiFilterInfo
{
    public string Id { get; set; } = string.Empty;
    public string Domain { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public string SourceOrganization { get; set; } = string.Empty;
    public DateTime? CreationDate { get; set; }
    public DateTime? ChangeDate { get; set; }
    public ObservableCollection<WmiRuleInfo> Rules { get; set; } = new();
    public int UsedByCount { get; set; }

    public string QueriesPreview => string.Join(" | ", Rules.Select(r => r.Query));
    public string Path => $"MSFT_SomFilter.Domain=\"{Domain}\",ID=\"{Id}\"";

    public WmiFilterInfo Clone() => new()
    {
        Id = Id,
        Domain = Domain,
        Name = Name,
        Description = Description,
        Author = Author,
        SourceOrganization = SourceOrganization,
        CreationDate = CreationDate,
        ChangeDate = ChangeDate,
        UsedByCount = UsedByCount,
        Rules = new ObservableCollection<WmiRuleInfo>(Rules.Select(r => r.Clone()))
    };
}

public sealed class WmiTestResult
{
    public int RuleNumber { get; init; }
    public string Namespace { get; init; } = string.Empty;
    public string Query { get; init; } = string.Empty;
    public bool IsMatch { get; init; }
    public string Error { get; init; } = string.Empty;
    public TimeSpan Duration { get; init; }
}

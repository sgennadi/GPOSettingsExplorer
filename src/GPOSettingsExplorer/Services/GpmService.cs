using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Xml.Linq;
using GPOSettingsExplorer.Models;

namespace GPOSettingsExplorer.Services;

public sealed class GpmService
{
    public bool IsAvailable => Type.GetTypeFromProgID("GPMgmt.GPM") is not null;

    public IReadOnlyList<GpoInfo> LoadGpos(string domainName)
    {
        dynamic gpm = CreateGpm();
        dynamic constants = gpm.GetConstants();
        dynamic domain = gpm.GetDomain(domainName, string.Empty, constants.UseAnyDC);
        dynamic criteria = gpm.CreateSearchCriteria();
        dynamic collection = domain.SearchGPOs(criteria);

        var result = new List<GpoInfo>();
        var count = Convert.ToInt32(collection.Count);

        for (var i = 1; i <= count; i++)
        {
            dynamic gpo = collection.Item(i);
            dynamic? wmiFilter = null;
            try
            {
                wmiFilter = gpo.GetWMIFilter();
            }
            catch
            {
                // A missing filter may surface as either null or a COM status code.
            }

            var idText = Convert.ToString(gpo.ID) ?? string.Empty;
            if (!Guid.TryParse(idText, out var id))
            {
                continue;
            }

            result.Add(new GpoInfo
            {
                Id = id,
                DisplayName = Convert.ToString(gpo.DisplayName) ?? idText,
                DomainName = domainName,
                CreationTime = TryConvertDateTime(gpo.CreationTime),
                ModificationTime = TryConvertDateTime(gpo.ModificationTime),
                ComputerEnabled = SafeBool(() => gpo.IsComputerEnabled()),
                UserEnabled = SafeBool(() => gpo.IsUserEnabled()),
                WmiFilterName = wmiFilter is null ? string.Empty : Convert.ToString(wmiFilter.Name) ?? string.Empty,
                WmiFilterPath = wmiFilter is null ? string.Empty : Convert.ToString(wmiFilter.Path) ?? string.Empty
            });
        }

        return result.OrderBy(g => g.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    public IReadOnlyList<PolicySettingInfo> BuildSettingsIndex(
        string domainName,
        IEnumerable<GpoInfo> gpos,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        dynamic gpm = CreateGpm();
        dynamic constants = gpm.GetConstants();
        dynamic domain = gpm.GetDomain(domainName, string.Empty, constants.UseAnyDC);

        var settings = new List<PolicySettingInfo>();
        var gpoList = gpos.ToList();

        for (var index = 0; index < gpoList.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var info = gpoList[index];
            progress?.Report($"Indexing {index + 1}/{gpoList.Count}: {info.DisplayName}");

            dynamic gpo = domain.GetGPO(info.Id.ToString("B"));
            var tempFile = Path.Combine(Path.GetTempPath(), $"GPOSettingsExplorer-{Guid.NewGuid():N}.xml");

            try
            {
                gpo.GenerateReportToFile(constants.ReportXML, tempFile);
                settings.AddRange(ParseReport(tempFile, info));
            }
            finally
            {
                try { File.Delete(tempFile); } catch { }
            }
        }

        return settings;
    }

    public void OpenEditor(GpoInfo gpo, string domainDistinguishedName)
    {
        var objectPath = $"LDAP://CN={gpo.Id:B},CN=Policies,CN=System,{domainDistinguishedName}";
        var arguments = $"gpme.msc /gpobject:\"{objectPath}\"";

        Process.Start(new ProcessStartInfo
        {
            FileName = "mmc.exe",
            Arguments = arguments,
            UseShellExecute = true
        });
    }

    public void SetWmiFilter(string domainName, Guid gpoId, WmiFilterInfo? filter)
    {
        dynamic gpm = CreateGpm();
        dynamic constants = gpm.GetConstants();
        dynamic domain = gpm.GetDomain(domainName, string.Empty, constants.UseAnyDC);
        dynamic gpo = domain.GetGPO(gpoId.ToString("B"));

        if (filter is null)
        {
            gpo.SetWMIFilter(null);
            return;
        }

        dynamic wmiFilter = domain.GetWMIFilter(filter.Path);
        gpo.SetWMIFilter(wmiFilter);
    }

    private static dynamic CreateGpm()
    {
        var type = Type.GetTypeFromProgID("GPMgmt.GPM")
            ?? throw new InvalidOperationException(
                "Group Policy Management components (GPMC/RSAT) are not installed.");

        return Activator.CreateInstance(type)
            ?? throw new InvalidOperationException("Unable to create the GPMC automation object.");
    }

    private static IEnumerable<PolicySettingInfo> ParseReport(string path, GpoInfo gpo)
    {
        var document = XDocument.Load(path, LoadOptions.None);
        var settings = new List<PolicySettingInfo>();

        foreach (var scopeName in new[] { "Computer", "User" })
        {
            var scope = document.Descendants().FirstOrDefault(e => e.Name.LocalName == scopeName);
            if (scope is null)
            {
                continue;
            }

            foreach (var policy in scope.Descendants().Where(e => e.Name.LocalName == "Policy"))
            {
                var name = ChildValue(policy, "Name");
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                var state = ChildValue(policy, "State");
                var category = ChildValue(policy, "Category");
                var extension = policy.Ancestors()
                    .FirstOrDefault(e => e.Name.LocalName == "Extension")?
                    .Attribute("type")?.Value ?? "Administrative Templates";

                var values = policy.Descendants()
                    .Where(e => !e.HasElements)
                    .Where(e => IsUsefulValueElement(e.Name.LocalName))
                    .Select(e => e.Value.Trim())
                    .Where(v => !string.IsNullOrWhiteSpace(v))
                    .Where(v => !string.Equals(v, name, StringComparison.OrdinalIgnoreCase))
                    .Where(v => !string.Equals(v, state, StringComparison.OrdinalIgnoreCase))
                    .Where(v => !string.Equals(v, category, StringComparison.OrdinalIgnoreCase))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(32)
                    .ToArray();

                var key = DescendantValue(policy, "Key");
                var valueName = DescendantValue(policy, "ValueName");

                settings.Add(new PolicySettingInfo
                {
                    GpoId = gpo.Id,
                    GpoName = gpo.DisplayName,
                    Scope = scopeName,
                    Extension = extension,
                    Category = category,
                    SettingName = name,
                    State = state,
                    Value = string.Join("; ", values),
                    RegistryKey = key,
                    RegistryValue = valueName
                });
            }
        }

        return settings;
    }

    private static bool IsUsefulValueElement(string localName)
    {
        return localName is not ("Name" or "State" or "Explain" or "Supported" or "Category" or "Presentation");
    }

    private static string ChildValue(XElement element, string localName)
        => element.Elements().FirstOrDefault(e => e.Name.LocalName == localName)?.Value.Trim() ?? string.Empty;

    private static string DescendantValue(XElement element, string localName)
        => element.Descendants().FirstOrDefault(e => e.Name.LocalName == localName)?.Value.Trim() ?? string.Empty;

    private static DateTime? TryConvertDateTime(object? value)
    {
        if (value is null)
        {
            return null;
        }

        try
        {
            return Convert.ToDateTime(value);
        }
        catch
        {
            return null;
        }
    }

    private static bool SafeBool(Func<object> getter)
    {
        try
        {
            return Convert.ToBoolean(getter());
        }
        catch (COMException)
        {
            return false;
        }
    }
}

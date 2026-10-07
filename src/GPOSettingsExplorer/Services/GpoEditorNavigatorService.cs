using System.Diagnostics;
using GPOSettingsExplorer.Models;

namespace GPOSettingsExplorer.Services;

public sealed class GpoEditorNavigatorService
{
    public Task<bool> OpenAtSettingAsync(
        GpoInfo gpo,
        string domainDistinguishedName,
        PolicySettingInfo setting)
    {
        return Task.Run(() =>
            OpenAtSetting(
                gpo,
                domainDistinguishedName,
                setting));
    }

    private static bool OpenAtSetting(
        GpoInfo gpo,
        string domainDistinguishedName,
        PolicySettingInfo setting)
    {
        var objectPath =
            $"LDAP://CN={gpo.Id:B},CN=Policies,CN=System,{domainDistinguishedName}";

        using var process =
            Process.Start(
                new ProcessStartInfo
                {
                    FileName = "mmc.exe",
                    Arguments =
                        $"-Embedding gpme.msc /s /gpobject:\"{objectPath}\"",
                    UseShellExecute = false
                })
            ?? throw new InvalidOperationException(
                "Unable to start the Group Policy Management Editor.");

        var applicationType =
            Type.GetTypeFromProgID(
                "MMC20.Application")
            ?? throw new InvalidOperationException(
                "MMC 2.0 automation is unavailable on this computer.");

        dynamic? application = null;

        try
        {
            application =
                Activator.CreateInstance(
                    applicationType)
                ?? throw new InvalidOperationException(
                    "Unable to connect to the MMC automation session.");

            application.UserControl = 1;
            application.Show();

            dynamic document =
                WaitForDocument(
                    application);

            dynamic scopeNamespace =
                document.ScopeNamespace;

            dynamic view =
                document.ActiveView;

            dynamic current =
                scopeNamespace.GetRoot();

            foreach (var segment in BuildPath(setting))
            {
                dynamic? next =
                    FindNode(
                        scopeNamespace,
                        current,
                        segment,
                        recursive:
                            IsTopLevelSegment(segment));

                if (next is null)
                {
                    return false;
                }

                current =
                    next;

                TryExpand(
                    scopeNamespace,
                    current);
            }

            view.ActiveScopeNode =
                current;

            Thread.Sleep(350);

            dynamic? row =
                FindResultRow(
                    view,
                    setting.SettingName);

            if (row is null)
            {
                return false;
            }

            view.Select(
                row);

            view.DisplaySelectionPropertySheet();

            GC.KeepAlive(
                application);

            return true;
        }
        catch (Microsoft.CSharp.RuntimeBinder.RuntimeBinderException)
        {
            return false;
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            return false;
        }
    }

    private static dynamic WaitForDocument(
        dynamic application)
    {
        var deadline =
            DateTime.UtcNow.AddSeconds(15);

        Exception? lastError =
            null;

        while (DateTime.UtcNow < deadline)
        {
            try
            {
                dynamic document =
                    application.Document;

                dynamic scopeNamespace =
                    document.ScopeNamespace;

                _ = scopeNamespace.GetRoot();

                return document;
            }
            catch (Exception ex)
            {
                lastError =
                    ex;

                Thread.Sleep(250);
            }
        }

        throw new InvalidOperationException(
            "The Group Policy editor did not finish loading in time.",
            lastError);
    }

    private static dynamic? FindNode(
        dynamic scopeNamespace,
        dynamic parent,
        string wantedName,
        bool recursive)
    {
        var deadline =
            DateTime.UtcNow.AddSeconds(8);

        while (DateTime.UtcNow < deadline)
        {
            TryExpand(
                scopeNamespace,
                parent);

            Thread.Sleep(100);

            var match =
                FindNodeOnce(
                    scopeNamespace,
                    parent,
                    wantedName,
                    recursive,
                    depth: 0);

            if (match is not null)
            {
                return match;
            }

            Thread.Sleep(150);
        }

        return null;
    }

    private static dynamic? FindNodeOnce(
        dynamic scopeNamespace,
        dynamic parent,
        string wantedName,
        bool recursive,
        int depth)
    {
        dynamic? child =
            TryGetChild(
                scopeNamespace,
                parent);

        while (child is not null)
        {
            var name =
                Convert.ToString(
                    child.Name)
                ?? string.Empty;

            if (name.Equals(
                    wantedName,
                    StringComparison.CurrentCultureIgnoreCase))
            {
                return child;
            }

            if (recursive &&
                depth < 3)
            {
                TryExpand(
                    scopeNamespace,
                    child);

                var nested =
                    FindNodeOnce(
                        scopeNamespace,
                        child,
                        wantedName,
                        recursive: true,
                        depth + 1);

                if (nested is not null)
                {
                    return nested;
                }
            }

            child =
                TryGetNext(
                    scopeNamespace,
                    child);
        }

        return null;
    }

    private static dynamic? FindResultRow(
        dynamic view,
        string settingName)
    {
        var deadline =
            DateTime.UtcNow.AddSeconds(8);

        while (DateTime.UtcNow < deadline)
        {
            try
            {
                dynamic items =
                    view.ListItems;

                var count =
                    Convert.ToInt32(
                        items.Count);

                for (var index = 1;
                     index <= count;
                     index++)
                {
                    dynamic item =
                        items.Item(index);

                    var name =
                        Convert.ToString(
                            item.Name)
                        ?? string.Empty;

                    if (name.Equals(
                            settingName,
                            StringComparison.CurrentCultureIgnoreCase) ||
                        name.StartsWith(
                            settingName,
                            StringComparison.CurrentCultureIgnoreCase))
                    {
                        return item;
                    }
                }
            }
            catch
            {
            }

            Thread.Sleep(200);
        }

        return null;
    }

    private static dynamic? TryGetChild(
        dynamic scopeNamespace,
        dynamic parent)
    {
        try
        {
            return scopeNamespace.GetChild(
                parent);
        }
        catch
        {
            return null;
        }
    }

    private static dynamic? TryGetNext(
        dynamic scopeNamespace,
        dynamic node)
    {
        try
        {
            return scopeNamespace.GetNext(
                node);
        }
        catch
        {
            return null;
        }
    }

    private static void TryExpand(
        dynamic scopeNamespace,
        dynamic node)
    {
        try
        {
            scopeNamespace.Expand(
                node);
        }
        catch
        {
        }
    }

    private static bool IsTopLevelSegment(
        string segment)
    {
        return segment.Equals(
                   "Computer Configuration",
                   StringComparison.OrdinalIgnoreCase) ||
               segment.Equals(
                   "User Configuration",
                   StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<string> BuildPath(
        PolicySettingInfo setting)
    {
        var result =
            new List<string>
            {
                setting.Scope.Equals(
                    "User",
                    StringComparison.OrdinalIgnoreCase)
                    ? "User Configuration"
                    : "Computer Configuration",
                "Policies"
            };

        if (setting.Extension.Equals(
                "SecuritySettings",
                StringComparison.OrdinalIgnoreCase))
        {
            result.AddRange(
                new[]
                {
                    "Windows Settings",
                    "Security Settings",
                    "Local Policies",
                    "Security Options"
                });

            return result;
        }

        result.Add(
            "Administrative Templates");

        result.AddRange(
            (setting.Category ?? string.Empty)
            .Replace(
                "/",
                " > ",
                StringComparison.Ordinal)
            .Split(
                '>',
                StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries)
            .Where(part =>
                !part.Equals(
                    "Administrative Templates",
                    StringComparison.OrdinalIgnoreCase)));

        return result;
    }
}

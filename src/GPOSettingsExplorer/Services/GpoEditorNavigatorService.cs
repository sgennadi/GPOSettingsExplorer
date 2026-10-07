using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Automation;
using GPOSettingsExplorer.Models;

namespace GPOSettingsExplorer.Services;

public sealed class GpoEditorNavigatorService
{
    public bool CanNavigateExactly(
        PolicySettingInfo setting)
    {
        // Non-ADMX settings arrive here only after ADMX mapping failed.
        // Security Options have a stable native editor path. Generic report
        // extensions such as RegistrySettings/PublicKeySettings do not.
        return setting.Extension.Equals(
            "SecuritySettings",
            StringComparison.OrdinalIgnoreCase);
    }

    public async Task<bool> OpenAtSettingAsync(
        GpoInfo gpo,
        string domainDistinguishedName,
        PolicySettingInfo setting,
        CancellationToken cancellationToken = default)
    {
        var process =
            StartEditor(
                gpo,
                domainDistinguishedName);

        if (!CanNavigateExactly(
                setting))
        {
            return false;
        }

        return await Task.Run(
            () =>
            {
                try
                {
                    return Navigate(
                        process,
                        setting,
                        cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (COMException)
                {
                    return false;
                }
                catch (ElementNotAvailableException)
                {
                    return false;
                }
                catch (InvalidOperationException)
                {
                    return false;
                }
            },
            cancellationToken);
    }

    private static Process StartEditor(
        GpoInfo gpo,
        string domainDistinguishedName)
    {
        var systemDirectory =
            Environment.GetFolderPath(
                Environment.SpecialFolder.System);

        var mmcPath =
            Path.Combine(
                systemDirectory,
                "mmc.exe");

        var objectPath =
            $"LDAP://CN={gpo.Id:B},CN=Policies,CN=System,{domainDistinguishedName}";

        return Process.Start(
                   new ProcessStartInfo
                   {
                       FileName =
                           mmcPath,
                       Arguments =
                           $"gpme.msc /gpobject:\"{objectPath}\"",
                       WorkingDirectory =
                           systemDirectory,
                       UseShellExecute =
                           true
                   })
               ?? throw new InvalidOperationException(
                   "Unable to start the Group Policy Management Editor.");
    }

    private static bool Navigate(
        Process process,
        PolicySettingInfo setting,
        CancellationToken cancellationToken)
    {
        var deadline =
            DateTime.UtcNow.AddSeconds(20);

        AutomationElement? window =
            null;

        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            process.Refresh();

            if (process.HasExited)
            {
                return false;
            }

            if (process.MainWindowHandle != IntPtr.Zero)
            {
                try
                {
                    window =
                        AutomationElement.FromHandle(
                            process.MainWindowHandle);

                    if (window is not null)
                    {
                        break;
                    }
                }
                catch (COMException)
                {
                }
                catch (ElementNotAvailableException)
                {
                }
            }

            Thread.Sleep(250);
        }

        if (window is null)
        {
            return false;
        }

        AutomationElement? tree;

        try
        {
            tree =
                window.FindFirst(
                    TreeScope.Descendants,
                    new PropertyCondition(
                        AutomationElement.ControlTypeProperty,
                        ControlType.Tree));
        }
        catch (COMException)
        {
            return false;
        }

        if (tree is null)
        {
            return false;
        }

        AutomationElement? current =
            null;

        foreach (var segment in BuildTreePath(
                     setting))
        {
            current =
                current is null
                    ? FindTreeItem(
                        tree,
                        segment,
                        TreeScope.Descendants,
                        deadline,
                        cancellationToken)
                    : FindTreeItem(
                        current,
                        segment,
                        TreeScope.Children,
                        deadline,
                        cancellationToken);

            if (current is null)
            {
                return false;
            }

            TryExpand(
                current);

            TrySelect(
                current);
        }

        if (current is null)
        {
            return false;
        }

        Thread.Sleep(400);

        var row =
            FindSettingRow(
                window,
                setting.SettingName,
                deadline,
                cancellationToken);

        if (row is null)
        {
            return false;
        }

        TrySelect(
            row);

        // Do not call AutomationElement.SetFocus(). MMC frequently exposes
        // scope/result elements that can be selected but cannot receive focus,
        // which produced "Target element cannot receive focus".
        try
        {
            if (row.TryGetCurrentPattern(
                    InvokePattern.Pattern,
                    out var invokeObject) &&
                invokeObject is InvokePattern invoke)
            {
                invoke.Invoke();
            }
        }
        catch (COMException)
        {
            // The exact row is already selected. Leave the editor there.
        }
        catch (InvalidOperationException)
        {
            // Some MMC result rows are selectable but not invokable.
        }

        return true;
    }

    private static IReadOnlyList<string> BuildTreePath(
        PolicySettingInfo setting)
    {
        var scope =
            setting.Scope.Equals(
                "User",
                StringComparison.OrdinalIgnoreCase)
                ? "User Configuration"
                : "Computer Configuration";

        return new[]
        {
            scope,
            "Policies",
            "Windows Settings",
            "Security Settings",
            "Local Policies",
            "Security Options"
        };
    }

    private static AutomationElement? FindTreeItem(
        AutomationElement parent,
        string name,
        TreeScope scope,
        DateTime deadline,
        CancellationToken cancellationToken)
    {
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            AutomationElementCollection items;

            try
            {
                items =
                    parent.FindAll(
                        scope,
                        new PropertyCondition(
                            AutomationElement.ControlTypeProperty,
                            ControlType.TreeItem));
            }
            catch (COMException)
            {
                return null;
            }
            catch (ElementNotAvailableException)
            {
                return null;
            }

            foreach (AutomationElement item in items)
            {
                try
                {
                    if (item.Current.Name.Equals(
                            name,
                            StringComparison.CurrentCultureIgnoreCase))
                    {
                        return item;
                    }
                }
                catch (ElementNotAvailableException)
                {
                }
            }

            Thread.Sleep(150);
        }

        return null;
    }

    private static AutomationElement? FindSettingRow(
        AutomationElement window,
        string settingName,
        DateTime deadline,
        CancellationToken cancellationToken)
    {
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            AutomationElementCollection rows;

            try
            {
                rows =
                    window.FindAll(
                        TreeScope.Descendants,
                        new PropertyCondition(
                            AutomationElement.ControlTypeProperty,
                            ControlType.ListItem));
            }
            catch (COMException)
            {
                return null;
            }
            catch (ElementNotAvailableException)
            {
                return null;
            }

            foreach (AutomationElement row in rows)
            {
                try
                {
                    var name =
                        row.Current.Name;

                    if (name.Equals(
                            settingName,
                            StringComparison.CurrentCultureIgnoreCase) ||
                        name.StartsWith(
                            settingName,
                            StringComparison.CurrentCultureIgnoreCase))
                    {
                        return row;
                    }
                }
                catch (ElementNotAvailableException)
                {
                }
            }

            Thread.Sleep(200);
        }

        return null;
    }

    private static void TryExpand(
        AutomationElement element)
    {
        try
        {
            if (!element.TryGetCurrentPattern(
                    ExpandCollapsePattern.Pattern,
                    out var value) ||
                value is not ExpandCollapsePattern pattern)
            {
                return;
            }

            if (pattern.Current.ExpandCollapseState ==
                ExpandCollapseState.Collapsed)
            {
                pattern.Expand();
            }
        }
        catch (COMException)
        {
        }
        catch (InvalidOperationException)
        {
        }
        catch (ElementNotAvailableException)
        {
        }
    }

    private static void TrySelect(
        AutomationElement element)
    {
        try
        {
            if (element.TryGetCurrentPattern(
                    SelectionItemPattern.Pattern,
                    out var value) &&
                value is SelectionItemPattern pattern)
            {
                pattern.Select();
            }
        }
        catch (COMException)
        {
        }
        catch (InvalidOperationException)
        {
        }
        catch (ElementNotAvailableException)
        {
        }
    }
}

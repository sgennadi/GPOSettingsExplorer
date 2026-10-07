using System.Diagnostics;
using System.Windows.Automation;
using GPOSettingsExplorer.Models;

namespace GPOSettingsExplorer.Services;

public sealed class GpoEditorNavigatorService
{
    public async Task<bool> OpenAtSettingAsync(
        GpoInfo gpo,
        string domainDistinguishedName,
        PolicySettingInfo setting,
        CancellationToken cancellationToken = default)
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

        var process =
            Process.Start(
                new ProcessStartInfo
                {
                    FileName = mmcPath,
                    Arguments =
                        $"gpme.msc /gpobject:\"{objectPath}\"",
                    WorkingDirectory =
                        systemDirectory,
                    UseShellExecute = true
                })
            ?? throw new InvalidOperationException(
                "Unable to start the Group Policy Management Editor.");

        return await Task.Run(
            () => Navigate(
                process,
                setting,
                cancellationToken),
            cancellationToken);
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
                return false;

            if (process.MainWindowHandle != IntPtr.Zero)
            {
                try
                {
                    window =
                        AutomationElement.FromHandle(
                            process.MainWindowHandle);

                    if (window is not null)
                        break;
                }
                catch
                {
                }
            }

            Thread.Sleep(250);
        }

        if (window is null)
            return false;

        var tree =
            window.FindFirst(
                TreeScope.Descendants,
                new PropertyCondition(
                    AutomationElement.ControlTypeProperty,
                    ControlType.Tree));

        if (tree is null)
            return false;

        AutomationElement? current =
            null;

        foreach (var segment in BuildTreePath(setting))
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
                return false;

            Expand(current);
            Select(current);
        }

        if (current is null)
            return false;

        current.SetFocus();
        Thread.Sleep(350);

        var row =
            FindSettingRow(
                window,
                setting.SettingName,
                deadline,
                cancellationToken);

        if (row is null)
            return false;

        Select(row);
        row.SetFocus();

        if (row.TryGetCurrentPattern(
                InvokePattern.Pattern,
                out var invokeObject) &&
            invokeObject is InvokePattern invoke)
        {
            invoke.Invoke();
            return true;
        }

        if (row.TryGetCurrentPattern(
                LegacyIAccessiblePattern.Pattern,
                out var legacyObject) &&
            legacyObject is LegacyIAccessiblePattern legacy)
        {
            try
            {
                legacy.DoDefaultAction();
                return true;
            }
            catch
            {
            }
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

        if (setting.Extension.Equals(
                "SecuritySettings",
                StringComparison.OrdinalIgnoreCase))
        {
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

        var result =
            new List<string>
            {
                scope,
                "Policies",
                "Administrative Templates"
            };

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

            var items =
                parent.FindAll(
                    scope,
                    new PropertyCondition(
                        AutomationElement.ControlTypeProperty,
                        ControlType.TreeItem));

            foreach (AutomationElement item in items)
            {
                if (item.Current.Name.Equals(
                        name,
                        StringComparison.CurrentCultureIgnoreCase))
                    return item;
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

            var rows =
                window.FindAll(
                    TreeScope.Descendants,
                    new PropertyCondition(
                        AutomationElement.ControlTypeProperty,
                        ControlType.ListItem));

            foreach (AutomationElement row in rows)
            {
                var name =
                    row.Current.Name;

                if (name.Equals(
                        settingName,
                        StringComparison.CurrentCultureIgnoreCase) ||
                    name.StartsWith(
                        settingName,
                        StringComparison.CurrentCultureIgnoreCase))
                    return row;
            }

            Thread.Sleep(200);
        }

        return null;
    }

    private static void Expand(
        AutomationElement element)
    {
        if (!element.TryGetCurrentPattern(
                ExpandCollapsePattern.Pattern,
                out var value) ||
            value is not ExpandCollapsePattern pattern)
            return;

        if (pattern.Current.ExpandCollapseState ==
            ExpandCollapseState.Collapsed)
            pattern.Expand();
    }

    private static void Select(
        AutomationElement element)
    {
        if (element.TryGetCurrentPattern(
                SelectionItemPattern.Pattern,
                out var value) &&
            value is SelectionItemPattern pattern)
        {
            pattern.Select();
        }
    }
}

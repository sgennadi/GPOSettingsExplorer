using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using GPOSettingsExplorer.Models;

namespace GPOSettingsExplorer.Services;

public sealed class GpoEditorNavigatorService
{
    private const uint MouseEventLeftDown = 0x0002;
    private const uint MouseEventLeftUp = 0x0004;
    private const int SwRestore = 9;

    public bool CanNavigateExactly(
        PolicySettingInfo setting)
    {
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
                catch (Exception ex)
                    when (!IsFatal(
                        ex))
                {
                    CrashLogService.Write(
                        $"Open exact GPO setting: {setting.SettingName}",
                        ex);

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
            DateTime.UtcNow.AddSeconds(
                15);

        var window =
            WaitForWindow(
                process,
                deadline,
                cancellationToken);

        if (window is null)
        {
            return false;
        }

        ShowWindow(
            process.MainWindowHandle,
            SwRestore);

        SetForegroundWindow(
            process.MainWindowHandle);

        var tree =
            FindControl(
                window,
                ControlType.Tree);

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

            TryScrollIntoView(
                current);

            TrySelectOrClick(
                current,
                doubleClick: false);

            TryExpand(
                current);

            Thread.Sleep(
                250);
        }

        if (current is null)
        {
            return false;
        }

        TrySelectOrClick(
            current,
            doubleClick: false);

        Thread.Sleep(
            700);

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

        TryScrollIntoView(
            row);

        TrySelectOrClick(
            row,
            doubleClick: false);

        Thread.Sleep(
            250);

        if (!TryInvoke(
                row))
        {
            TrySelectOrClick(
                row,
                doubleClick: true);
        }

        return true;
    }

    private static AutomationElement? WaitForWindow(
        Process process,
        DateTime deadline,
        CancellationToken cancellationToken)
    {
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            process.Refresh();

            if (process.HasExited)
            {
                return null;
            }

            if (process.MainWindowHandle != IntPtr.Zero)
            {
                try
                {
                    var window =
                        AutomationElement.FromHandle(
                            process.MainWindowHandle);

                    if (window is not null)
                    {
                        return window;
                    }
                }
                catch (COMException)
                {
                }
                catch (ElementNotAvailableException)
                {
                }
            }

            Thread.Sleep(
                250);
        }

        return null;
    }

    private static AutomationElement? FindControl(
        AutomationElement parent,
        ControlType controlType)
    {
        try
        {
            return parent.FindFirst(
                TreeScope.Descendants,
                new PropertyCondition(
                    AutomationElement.ControlTypeProperty,
                    controlType));
        }
        catch (COMException)
        {
            return null;
        }
        catch (ElementNotAvailableException)
        {
            return null;
        }
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
                if (ElementNameMatches(
                        item,
                        name))
                {
                    return item;
                }
            }

            TryExpand(
                parent);

            Thread.Sleep(
                180);
        }

        return null;
    }

    private static AutomationElement? FindSettingRow(
        AutomationElement window,
        string settingName,
        DateTime deadline,
        CancellationToken cancellationToken)
    {
        var rowCondition =
            new OrCondition(
                new PropertyCondition(
                    AutomationElement.ControlTypeProperty,
                    ControlType.ListItem),
                new PropertyCondition(
                    AutomationElement.ControlTypeProperty,
                    ControlType.DataItem));

        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            AutomationElementCollection rows;

            try
            {
                rows =
                    window.FindAll(
                        TreeScope.Descendants,
                        rowCondition);
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
                if (ElementContainsSettingName(
                        row,
                        settingName))
                {
                    return row;
                }
            }

            var byText =
                FindRowByText(
                    window,
                    settingName);

            if (byText is not null)
            {
                return byText;
            }

            Thread.Sleep(
                220);
        }

        return null;
    }

    private static AutomationElement? FindRowByText(
        AutomationElement window,
        string settingName)
    {
        try
        {
            var texts =
                window.FindAll(
                    TreeScope.Descendants,
                    new PropertyCondition(
                        AutomationElement.ControlTypeProperty,
                        ControlType.Text));

            foreach (AutomationElement text in texts)
            {
                if (!ElementNameMatches(
                        text,
                        settingName))
                {
                    continue;
                }

                var current =
                    text;

                for (var depth = 0;
                     depth < 6;
                     depth++)
                {
                    current =
                        TreeWalker.ControlViewWalker.GetParent(
                            current);

                    if (current is null)
                    {
                        break;
                    }

                    var type =
                        current.Current.ControlType;

                    if (type == ControlType.ListItem ||
                        type == ControlType.DataItem)
                    {
                        return current;
                    }
                }
            }
        }
        catch (COMException)
        {
        }
        catch (ElementNotAvailableException)
        {
        }

        return null;
    }

    private static bool ElementContainsSettingName(
        AutomationElement element,
        string settingName)
    {
        if (ElementNameMatches(
                element,
                settingName))
        {
            return true;
        }

        try
        {
            var texts =
                element.FindAll(
                    TreeScope.Descendants,
                    new PropertyCondition(
                        AutomationElement.ControlTypeProperty,
                        ControlType.Text));

            foreach (AutomationElement text in texts)
            {
                if (ElementNameMatches(
                        text,
                        settingName))
                {
                    return true;
                }
            }
        }
        catch (COMException)
        {
        }
        catch (ElementNotAvailableException)
        {
        }

        return false;
    }

    private static bool ElementNameMatches(
        AutomationElement element,
        string expected)
    {
        try
        {
            return TextMatches(
                element.Current.Name,
                expected);
        }
        catch (ElementNotAvailableException)
        {
            return false;
        }
    }

    private static bool TextMatches(
        string actual,
        string expected)
    {
        var normalizedActual =
            NormalizeUiText(
                actual);

        var normalizedExpected =
            NormalizeUiText(
                expected);

        if (normalizedActual.Length == 0 ||
            normalizedExpected.Length == 0)
        {
            return false;
        }

        return normalizedActual.Equals(
                   normalizedExpected,
                   StringComparison.CurrentCultureIgnoreCase) ||
               normalizedActual.StartsWith(
                   normalizedExpected,
                   StringComparison.CurrentCultureIgnoreCase);
    }

    private static string NormalizeUiText(
        string value)
    {
        return string.Join(
            " ",
            (value ?? string.Empty)
                .Split(
                    (char[]?)null,
                    StringSplitOptions.RemoveEmptyEntries));
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

    private static void TryScrollIntoView(
        AutomationElement element)
    {
        try
        {
            if (element.TryGetCurrentPattern(
                    ScrollItemPattern.Pattern,
                    out var value) &&
                value is ScrollItemPattern pattern)
            {
                pattern.ScrollIntoView();
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

    private static bool TryInvoke(
        AutomationElement element)
    {
        try
        {
            if (element.TryGetCurrentPattern(
                    InvokePattern.Pattern,
                    out var value) &&
                value is InvokePattern pattern)
            {
                pattern.Invoke();
                return true;
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

        return false;
    }

    private static void TrySelectOrClick(
        AutomationElement element,
        bool doubleClick)
    {
        try
        {
            if (!doubleClick &&
                element.TryGetCurrentPattern(
                    SelectionItemPattern.Pattern,
                    out var value) &&
                value is SelectionItemPattern pattern)
            {
                pattern.Select();
                return;
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

        TryMouseClick(
            element,
            doubleClick);
    }

    private static void TryMouseClick(
        AutomationElement element,
        bool doubleClick)
    {
        Rect rectangle;

        try
        {
            rectangle =
                element.Current.BoundingRectangle;

            if (rectangle.IsEmpty ||
                rectangle.Width <= 1 ||
                rectangle.Height <= 1 ||
                element.Current.IsOffscreen)
            {
                return;
            }
        }
        catch (ElementNotAvailableException)
        {
            return;
        }

        var x =
            checked(
                (int)Math.Round(
                    rectangle.Left +
                    Math.Min(
                        rectangle.Width / 2,
                        180)));

        var y =
            checked(
                (int)Math.Round(
                    rectangle.Top +
                    rectangle.Height / 2));

        if (!SetCursorPos(
                x,
                y))
        {
            return;
        }

        MouseClick();

        if (doubleClick)
        {
            Thread.Sleep(
                90);

            MouseClick();
        }
    }

    private static void MouseClick()
    {
        mouse_event(
            MouseEventLeftDown,
            0,
            0,
            0,
            UIntPtr.Zero);

        mouse_event(
            MouseEventLeftUp,
            0,
            0,
            0,
            UIntPtr.Zero);
    }

    [DllImport(
        "user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(
        IntPtr windowHandle);

    [DllImport(
        "user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(
        IntPtr windowHandle,
        int command);

    [DllImport(
        "user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetCursorPos(
        int x,
        int y);

    private static bool IsFatal(
        Exception exception) =>
        exception is OutOfMemoryException or
                     StackOverflowException or
                     AccessViolationException;

    [DllImport(
        "user32.dll")]
    private static extern void mouse_event(
        uint flags,
        uint dx,
        uint dy,
        uint data,
        UIntPtr extraInfo);
}

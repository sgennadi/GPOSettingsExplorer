using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using GPOSettingsExplorer.Models;

namespace GPOSettingsExplorer.Services;

public sealed class GpoEditorNavigatorService
{
    private const uint MouseEventLeftDown = 0x0002;
    private const uint MouseEventLeftUp = 0x0004;
    private const int SwRestore = 9;

    private const int LvmFirst = 0x1000;
    private const int LvmGetItemCount = LvmFirst + 4;
    private const int LvmGetItemRect = LvmFirst + 14;
    private const int LvmEnsureVisible = LvmFirst + 19;
    private const int LvmSetItemState = LvmFirst + 43;
    private const int LvmGetItemTextW = LvmFirst + 115;
    private const int LvmFindItemW = LvmFirst + 83;
    private const uint LvfiString = 0x0002;
    private const int LvmGetNextItem = LvmFirst + 12;
    private const uint LvifText = 0x0001;
    private const uint LvisFocused = 0x0001;
    private const uint LvisSelected = 0x0002;
    private const int LvniFocused = 0x0001;
    private const int LvniSelected = 0x0002;
    private const int WmSetFocus = 0x0007;
    private const int WmKeyDown = 0x0100;
    private const int WmKeyUp = 0x0101;
    private const int VkReturn = 0x0D;
    private const int LvirBounds = 0;

    private const uint ProcessVmOperation = 0x0008;
    private const uint ProcessVmRead = 0x0010;
    private const uint ProcessVmWrite = 0x0020;
    private const uint ProcessQueryLimitedInformation = 0x1000;

    private const uint MemCommit = 0x1000;
    private const uint MemReserve = 0x2000;
    private const uint MemRelease = 0x8000;
    private const uint PageReadWrite = 0x04;

    public bool CanNavigateExactly(
        PolicySettingInfo setting)
    {
        // Only Security Options rows have a stable, matching name in MMC.
        // Generic entries such as "Audit Setting" must never be opened as
        // an unrelated Security Option because their extension name matches.
        return setting.Extension.Equals("SecuritySettings", StringComparison.OrdinalIgnoreCase) &&
               setting.Category.Contains("Security Options", StringComparison.OrdinalIgnoreCase) &&
               !string.IsNullOrWhiteSpace(setting.SettingName) &&
               !setting.SettingName.Equals("Security option", StringComparison.OrdinalIgnoreCase);
    }

    private static bool CanNavigateToSection(PolicySettingInfo setting) =>
        setting.Extension.Equals("SecuritySettings", StringComparison.OrdinalIgnoreCase) ||
        setting.Extension.Equals("AuditSettings", StringComparison.OrdinalIgnoreCase) ||
        setting.Extension.Equals("RegistrySettings", StringComparison.OrdinalIgnoreCase);

    public async Task<bool> OpenAtSettingAsync(
        GpoInfo gpo,
        string domainDistinguishedName,
        PolicySettingInfo setting,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var process =
            StartEditor(
                gpo,
                domainDistinguishedName);

        if (!CanNavigateExactly(setting) && !CanNavigateToSection(setting))
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
                        progress,
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
            DomainConnectionState.BuildLdapPath(
                $"CN={gpo.Id:B},CN=Policies,CN=System,{domainDistinguishedName}");

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

    private bool Navigate(
        Process process,
        PolicySettingInfo setting,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        var deadline =
            DateTime.UtcNow.AddSeconds(
                60);

        progress?.Report(
            "Waiting for Group Policy Management Editor...");

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

        progress?.Report(
            "Locating the policy tree...");

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
            progress?.Report(
                $"Opening: {segment}");

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

        if (!CanNavigateExactly(setting))
        {
            progress?.Report("Opened the closest supported MMC category. Exact row navigation is not available for this setting type.");
            return false;
        }

        Thread.Sleep(450);

        progress?.Report("Trying an exact name match in the native MMC list view...");

        var candidates = BuildRowCandidates(setting);
        if (TryOpenNativeListViewSetting(
                process,
                setting,
                candidates,
                cancellationToken,
                out var nativeDiagnostics))
        {
            progress?.Report("Exact setting opened through the native MMC list view.");
            return true;
        }

        progress?.Report(
            "Searching visible MMC settings through UI Automation...");

        var row =
            FindSettingRow(
                window,
                setting,
                deadline,
                cancellationToken);

        if (row is null)
        {
            progress?.Report(
                "UI Automation exposed no matching row. Trying native MMC list view...");

            var nativeAccessDenied = nativeDiagnostics.Contains(
                "OpenProcess denied", StringComparison.OrdinalIgnoreCase);
            progress?.Report(nativeAccessDenied
                ? "MMC row inspection failed (access denied). Run the app and MMC at the same elevation; see the navigation log."
                : "The MMC category opened, but no exact policy-name match could be verified. No other policy was opened; see the navigation log.");

            CrashLogService.Write(
                $"MMC exact navigation miss: {setting.SettingName}",
                BuildNavigationMissDetails(
                    window,
                    setting,
                    candidates) +
                Environment.NewLine +
                nativeDiagnostics);

            return false;
        }

        progress?.Report(
            "Exact setting found. Selecting it...");

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

        progress?.Report(
            "Exact setting opened.");

        return true;
    }

    public static string NavigationTarget(
        PolicySettingInfo setting)
    {
        return string.Join(
            " > ",
            BuildTreePath(
                setting));
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
            setting.Scope.Equals("User", StringComparison.OrdinalIgnoreCase)
                ? "User Configuration"
                : "Computer Configuration";

        if (setting.Extension.Equals("RegistrySettings", StringComparison.OrdinalIgnoreCase))
        {
            // registry.pol / Extra Registry Settings are Administrative Templates,
            // NOT Group Policy Preferences Registry items.
            return new[] { scope, "Policies", "Administrative Templates" };
        }

        var security = new List<string>
        {
            scope, "Policies", "Windows Settings", "Security Settings"
        };

        if (setting.Extension.Equals("AuditSettings", StringComparison.OrdinalIgnoreCase) ||
            setting.Category.Contains("Advanced Audit", StringComparison.OrdinalIgnoreCase))
        {
            security.Add("Advanced Audit Policy Configuration");
            security.Add("Audit Policies");
            return security;
        }

        if (setting.Category.Contains("Account Policies", StringComparison.OrdinalIgnoreCase))
        {
            security.Add("Account Policies");
            return security;
        }

        if (setting.Category.Contains("Local Policies", StringComparison.OrdinalIgnoreCase))
        {
            security.Add("Local Policies");
            if (setting.Category.Contains("Security Options", StringComparison.OrdinalIgnoreCase))
                security.Add("Security Options");
            else if (setting.Category.Contains("Audit Policy", StringComparison.OrdinalIgnoreCase))
                security.Add("Audit Policy");
            else if (setting.Category.Contains("User Rights Assignment", StringComparison.OrdinalIgnoreCase))
                security.Add("User Rights Assignment");
        }

        return security;
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
        PolicySettingInfo setting,
        DateTime deadline,
        CancellationToken cancellationToken)
    {
        var candidates = BuildRowCandidates(setting);
        if (candidates.Count == 0)
            return null;

        var rowCondition = new OrCondition(
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem),
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.DataItem));

        cancellationToken.ThrowIfCancellationRequested();
        var visible = FindVisibleRow(window, candidates, rowCondition);
        if (visible is not null) return visible;

        var byText = FindRowByText(window, candidates);
        if (byText is not null) return byText;

        var virtualized = FindVirtualizedRow(window, candidates);
        if (virtualized is not null) return virtualized;

        // Single bounded pass, no repeated jump-to-top / scroll-to-bottom loops.
        return FindRowByScrolling(window, candidates, rowCondition, deadline, cancellationToken);
    }

    private static string BuildNavigationMissDetails(
        AutomationElement window,
        PolicySettingInfo setting,
        IReadOnlyList<string> candidates)
    {
        var builder =
            new System.Text.StringBuilder();

        builder.AppendLine(
            $"Setting: {setting.SettingName}");

        builder.AppendLine(
            $"Extension: {setting.Extension}");

        builder.AppendLine(
            $"Scope: {setting.Scope}");

        builder.AppendLine(
            $"Registry key: {setting.RegistryKey}");

        builder.AppendLine(
            $"Registry value: {setting.RegistryValue}");

        builder.AppendLine(
            "Candidates:");

        foreach (var candidate in candidates)
        {
            builder.AppendLine(
                "  - " +
                candidate);
        }

        builder.AppendLine(
            "Visible MMC rows:");

        try
        {
            var rowCondition =
                new OrCondition(
                    new PropertyCondition(
                        AutomationElement.ControlTypeProperty,
                        ControlType.ListItem),
                    new PropertyCondition(
                        AutomationElement.ControlTypeProperty,
                        ControlType.DataItem));

            var rows =
                window.FindAll(
                    TreeScope.Descendants,
                    rowCondition);

            var count =
                0;

            foreach (AutomationElement row in rows)
            {
                if (count++ >=
                    80)
                {
                    break;
                }

                var names =
                    new List<string>();

                try
                {
                    AddDiagnosticName(
                        names,
                        row.Current.Name);

                    var descendants =
                        row.FindAll(
                            TreeScope.Descendants,
                            System.Windows.Automation.Condition.TrueCondition);

                    foreach (AutomationElement descendant in descendants)
                    {
                        AddDiagnosticName(
                            names,
                            descendant.Current.Name);
                    }
                }
                catch (Exception ex)
                    when (!IsFatal(
                        ex))
                {
                }

                if (names.Count >
                    0)
                {
                    builder.AppendLine(
                        "  - " +
                        string.Join(
                            " | ",
                            names.Distinct(
                                StringComparer.CurrentCultureIgnoreCase)));
                }
            }
        }
        catch (Exception ex)
            when (!IsFatal(
                ex))
        {
            builder.AppendLine(
                $"  <unable to enumerate rows: {ex.Message}>");
        }

        return builder.ToString();
    }

    private static void AddDiagnosticName(
        ICollection<string> names,
        string? value)
    {
        var normalized =
            NormalizeUiText(
                value
                ?? string.Empty);

        if (!string.IsNullOrWhiteSpace(
                normalized))
        {
            names.Add(
                normalized);
        }
    }

    private static IReadOnlyList<string> BuildRowCandidates(
        PolicySettingInfo setting)
    {
        var result =
            new List<string>();

        AddCandidate(
            result,
            setting.SettingName);

        // Security Options must match the displayed setting name, not a
        // commonly shared registry key/value or a fuzzy partial prefix.
        if (setting.Extension.Equals("SecuritySettings", StringComparison.OrdinalIgnoreCase))
            return result;

        AddCandidate(
            result,
            setting.RegistryValue);

        AddCandidate(
            result,
            setting.RegistryKey);

        AddCandidate(
            result,
            ExtractSummaryValue(
                setting.Value,
                "name"));

        AddCandidate(
            result,
            ExtractSummaryValue(
                setting.Value,
                "status"));

        AddCandidate(
            result,
            ExtractSummaryValue(
                setting.Value,
                "ValueName"));

        AddCandidate(
            result,
            ExtractSummaryValue(
                setting.Value,
                "Key"));

        AddCandidate(
            result,
            ExtractSummaryValue(
                setting.Value,
                "KeyPath"));

        if (setting.SettingName.StartsWith(
                "Registry:",
                StringComparison.OrdinalIgnoreCase))
        {
            AddCandidate(
                result,
                setting.SettingName[
                    "Registry:".Length..]);
        }

        if (setting.Extension.Equals(
                "RegistrySettings",
                StringComparison.OrdinalIgnoreCase))
        {
            AddCandidate(
                result,
                LastRegistrySegment(
                    setting.RegistryKey));

            AddCandidate(
                result,
                LastRegistrySegment(
                    ExtractSummaryValue(
                        setting.Value,
                        "KeyPath")));

            AddCandidate(
                result,
                LastRegistrySegment(
                    ExtractSummaryValue(
                        setting.Value,
                        "Key")));
        }

        return result;
    }

    private static string LastRegistrySegment(
        string? value)
    {
        if (string.IsNullOrWhiteSpace(
                value))
        {
            return string.Empty;
        }

        var normalized =
            value
                .Trim()
                .Replace(
                    '/',
                    '\\')
                .TrimEnd(
                    '\\');

        var index =
            normalized.LastIndexOf(
                '\\');

        return index >= 0 &&
               index <
               normalized.Length - 1
            ? normalized[(index + 1)..]
            : normalized;
    }

    private static void AddCandidate(
        ICollection<string> candidates,
        string? value)
    {
        var normalized =
            NormalizeUiText(
                value ?? string.Empty);

        if (normalized.Length < 2 ||
            candidates.Any(
                candidate =>
                    candidate.Equals(
                        normalized,
                        StringComparison.CurrentCultureIgnoreCase)))
        {
            return;
        }

        candidates.Add(
            normalized);
    }

    private static string ExtractSummaryValue(
        string summary,
        string name)
    {
        if (string.IsNullOrWhiteSpace(
                summary))
        {
            return string.Empty;
        }

        foreach (var part in summary.Split(
                     ';',
                     StringSplitOptions.RemoveEmptyEntries |
                     StringSplitOptions.TrimEntries))
        {
            var separator =
                part.IndexOf('=');

            if (separator <= 0)
            {
                continue;
            }

            if (!part[..separator].Trim().Equals(
                    name,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return part[(separator + 1)..].Trim();
        }

        return string.Empty;
    }

    private static AutomationElement? FindVisibleRow(
        AutomationElement parent,
        IReadOnlyList<string> candidates,
        System.Windows.Automation.Condition rowCondition)
    {
        AutomationElementCollection rows;

        try
        {
            rows =
                parent.FindAll(
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
            if (ElementContainsAnySettingName(
                    row,
                    candidates))
            {
                return row;
            }
        }

        return null;
    }

    private static AutomationElement? FindRowByText(
        AutomationElement window,
        IReadOnlyList<string> candidates)
    {
        try
        {
            var descendants =
                window.FindAll(
                    TreeScope.Descendants,
                    System.Windows.Automation.Condition.TrueCondition);

            foreach (AutomationElement descendant in descendants)
            {
                if (!ElementNameMatchesAny(
                        descendant,
                        candidates))
                {
                    continue;
                }

                var current =
                    descendant;

                for (var depth = 0;
                     depth < 10;
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

    private static AutomationElement? FindVirtualizedRow(
        AutomationElement window,
        IReadOnlyList<string> candidates)
    {
        var containerCondition =
            new OrCondition(
                new PropertyCondition(
                    AutomationElement.ControlTypeProperty,
                    ControlType.List),
                new PropertyCondition(
                    AutomationElement.ControlTypeProperty,
                    ControlType.DataGrid));

        AutomationElementCollection containers;

        try
        {
            containers =
                window.FindAll(
                    TreeScope.Descendants,
                    containerCondition);
        }
        catch (COMException)
        {
            return null;
        }
        catch (ElementNotAvailableException)
        {
            return null;
        }

        foreach (AutomationElement container in containers)
        {
            try
            {
                if (!container.TryGetCurrentPattern(
                        ItemContainerPattern.Pattern,
                        out var value) ||
                    value is not ItemContainerPattern pattern)
                {
                    continue;
                }

                foreach (var candidate in candidates)
                {
                    var item =
                        pattern.FindItemByProperty(
                            null,
                            AutomationElement.NameProperty,
                            candidate);

                    if (item is null)
                    {
                        continue;
                    }

                    TryRealize(
                        item);

                    return item;
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

        return null;
    }

    private static AutomationElement? FindRowByScrolling(
        AutomationElement window,
        IReadOnlyList<string> candidates,
        System.Windows.Automation.Condition rowCondition,
        DateTime deadline,
        CancellationToken cancellationToken)
    {
        var containerCondition =
            new OrCondition(
                new PropertyCondition(
                    AutomationElement.ControlTypeProperty,
                    ControlType.List),
                new PropertyCondition(
                    AutomationElement.ControlTypeProperty,
                    ControlType.DataGrid));

        AutomationElementCollection containers;

        try
        {
            containers =
                window.FindAll(
                    TreeScope.Descendants,
                    containerCondition);
        }
        catch (COMException)
        {
            return null;
        }
        catch (ElementNotAvailableException)
        {
            return null;
        }

        foreach (AutomationElement container in containers)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (DateTime.UtcNow >= deadline)
            {
                break;
            }

            ScrollPattern? scroll =
                null;

            try
            {
                if (container.TryGetCurrentPattern(
                        ScrollPattern.Pattern,
                        out var value) &&
                    value is ScrollPattern pattern &&
                    pattern.Current.VerticallyScrollable)
                {
                    scroll =
                        pattern;
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

            if (scroll is null)
            {
                continue;
            }

            try
            {
                scroll.SetScrollPercent(
                    ScrollPattern.NoScroll,
                    0);
            }
            catch (COMException)
            {
            }
            catch (InvalidOperationException)
            {
            }
            catch (ElementNotAvailableException)
            {
                continue;
            }

            for (var page = 0;
                 page < 12 &&
                 DateTime.UtcNow < deadline;
                 page++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                Thread.Sleep(
                    70);

                var row =
                    FindVisibleRow(
                        container,
                        candidates,
                        rowCondition);

                if (row is not null)
                {
                    return row;
                }

                double before;

                try
                {
                    before =
                        scroll.Current.VerticalScrollPercent;

                    scroll.Scroll(
                        ScrollAmount.NoAmount,
                        ScrollAmount.LargeIncrement);

                    Thread.Sleep(
                        70);

                    var after =
                        scroll.Current.VerticalScrollPercent;

                    if (after >= 99.9 ||
                        Math.Abs(
                            after -
                            before) < 0.01)
                    {
                        var finalRow =
                            FindVisibleRow(
                                container,
                                candidates,
                                rowCondition);

                        if (finalRow is not null)
                        {
                            return finalRow;
                        }

                        break;
                    }
                }
                catch (COMException)
                {
                    break;
                }
                catch (InvalidOperationException)
                {
                    break;
                }
                catch (ElementNotAvailableException)
                {
                    break;
                }
            }
        }

        return null;
    }

    private static bool ElementContainsAnySettingName(
        AutomationElement element,
        IReadOnlyList<string> candidates)
    {
        if (ElementNameMatchesAny(
                element,
                candidates))
        {
            return true;
        }

        try
        {
            var descendants =
                element.FindAll(
                    TreeScope.Descendants,
                    System.Windows.Automation.Condition.TrueCondition);

            foreach (AutomationElement descendant in descendants)
            {
                if (ElementNameMatchesAny(
                        descendant,
                        candidates))
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

    private static bool ElementNameMatchesAny(
        AutomationElement element,
        IReadOnlyList<string> candidates)
    {
        try
        {
            var actual =
                element.Current.Name;

            return candidates.Any(
                expected =>
                    TextMatches(
                        actual,
                        expected));
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
        var actualText = NormalizeUiText(actual);
        var expectedText = NormalizeUiText(expected);
        if (actualText.Length == 0 || expectedText.Length == 0)
            return false;

        if (actualText.Equals(expectedText, StringComparison.CurrentCultureIgnoreCase))
            return true;

        // MMC may show a shortened long heading ending in an ellipsis.
        // A shared prefix without an ellipsis is NOT proof of an exact setting.
        var prefix = actualText.TrimEnd('.');
        return actualText.EndsWith("...", StringComparison.Ordinal) &&
               prefix.Length >= 28 &&
               expectedText.StartsWith(prefix, StringComparison.CurrentCultureIgnoreCase);
    }

    private static string NormalizeUiText(
        string value)
    {
        var cleaned =
            (value ?? string.Empty)
            .Replace(
                "&",
                string.Empty,
                StringComparison.Ordinal)
            .Replace(
                "…",
                "...",
                StringComparison.Ordinal);

        return string.Join(
            " ",
            cleaned.Split(
                (char[]?)null,
                StringSplitOptions.RemoveEmptyEntries));
    }

    private static bool TryOpenNativeListViewSetting(
        Process process,
        PolicySettingInfo setting,
        IReadOnlyList<string> candidates,
        CancellationToken cancellationToken,
        out string diagnostics)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Native MMC list-view probe:");
        builder.AppendLine($"  Policy: {setting.SettingName}");
        builder.AppendLine($"  Process ID: {process.Id}");
        builder.AppendLine($"  Process architecture: {(Environment.Is64BitProcess ? "64-bit" : "32-bit")} explorer");

        var access = ProcessVmOperation | ProcessVmRead |
                     ProcessVmWrite | ProcessQueryLimitedInformation;
        var processHandle = OpenProcess(access, inheritHandle: false, process.Id);

        if (processHandle == IntPtr.Zero)
        {
            var win32 = Marshal.GetLastWin32Error();
            builder.AppendLine($"  OpenProcess denied or unavailable. Win32={win32}.");
            builder.AppendLine("  Try launching GPO Settings Explorer and MMC at the same Windows integrity level.");
            diagnostics = builder.ToString();
            return false;
        }

        try
        {
            // Opening a Security Options node does not synchronously populate
            // the right-hand MMC list view. Poll the native exact-name lookup
            // for a bounded interval before trying a slower full-list scan.
            var deadline = DateTime.UtcNow.AddSeconds(9);
            var sawListView = false;
            var sawItems = false;
            var counts = new Dictionary<IntPtr, int>();
            var searchAttempts = 0;

            while (DateTime.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();
                process.Refresh();
                if (process.HasExited)
                {
                    builder.AppendLine("  MMC closed while searching.");
                    break;
                }

                var listViews = FindNativeListViews(process.MainWindowHandle);
                sawListView |= listViews.Count > 0;
                foreach (var listView in listViews)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var count = checked((int)SendMessage(
                        listView, LvmGetItemCount, IntPtr.Zero, IntPtr.Zero));

                    counts[listView] = count;
                    if (count <= 0 || count > 20000)
                        continue;

                    sawItems = true;
                    searchAttempts++;

                    // The native LVFI_STRING search compares the *complete*
                    // item name, not the text clipped with an ellipsis in MMC.
                    // It avoids matching similarly named NTLM policies.
                    var index = FindNativeListViewExactIndex(
                        processHandle, listView, setting.SettingName);

                    if (index < 0 || index >= count)
                        continue;

                    builder.AppendLine($"  LVM_FINDITEMW exact match: row {index} of {count}.");

                    if (SelectAndOpenNativeListViewRow(
                        process, processHandle, listView, index,
                        out var selectedIndex, out var focusedIndex))
                    {
                        builder.AppendLine(
                            $"  Verified selected={selectedIndex}, focused={focusedIndex}; opened exact row.");
                        diagnostics = builder.ToString();
                        return true;
                    }

                    builder.AppendLine(
                        $"  Exact native match found, but activation failed (selected={selectedIndex}, focused={focusedIndex}).");
                }

                Thread.Sleep(220);
            }

            builder.AppendLine($"  MMC list views found: {sawListView}; populated: {sawItems}; exact-name probes: {searchAttempts}.");
            foreach (var pair in counts)
                builder.AppendLine($"  ListView 0x{pair.Key.ToInt64():X}: {pair.Value} item(s)");

            // Older MMC configurations may not support LVM_FINDITEMW reliably.
            // Read and compare *only the Policy (first) column* of each row;
            // never match text from the Policy Setting or other value columns.
            foreach (var pair in counts)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var listView = pair.Key;
                if (pair.Value is <= 0 or > 20000)
                    continue;

                var rows = ReadNativeListViewRows(
                    processHandle, listView, pair.Value, cancellationToken);
                builder.AppendLine($"  Native fallback returned {rows.Count} named rows.");
                foreach (var row in rows.Take(30))
                    builder.AppendLine($"    [{row.Index}] {row.Text}");

                var match = FindBestNativeListViewMatch(rows, setting, candidates);
                if (match is null)
                    continue;

                builder.AppendLine($"  Verified fallback name match: [{match.Index}] {match.Text}");
                if (SelectAndOpenNativeListViewRow(
                    process, processHandle, listView, match.Index,
                    out var selectedIndex, out var focusedIndex))
                {
                    builder.AppendLine($"  Opened verified row {match.Index}.");
                    diagnostics = builder.ToString();
                    return true;
                }

                builder.AppendLine($"  Row {match.Index} selection failed: selected={selectedIndex}, focused={focusedIndex}.");
            }

            builder.AppendLine(
                "  Could not prove an exact policy name match. No approximate match was opened.");
            diagnostics = builder.ToString();
            return false;
        }
        finally
        {
            CloseHandle(processHandle);
        }
    }

    private static int FindNativeListViewExactIndex(
        IntPtr processHandle,
        IntPtr listView,
        string policyName)
    {
        // Native LVFINDINFO searches the literal MMC row text. Do not
        // remove ampersands or normalize internal spaces for this lookup.
        var name = policyName.Trim();
        if (string.IsNullOrWhiteSpace(name))
            return -1;

        var bytes = Encoding.Unicode.GetBytes(name + "\0");
        var infoSize = Marshal.SizeOf<NativeLvFindInfo>();
        var remoteText = VirtualAllocEx(processHandle, IntPtr.Zero,
            (UIntPtr)bytes.Length, MemCommit | MemReserve, PageReadWrite);
        if (remoteText == IntPtr.Zero)
            return -1;

        var remoteFindInfo = IntPtr.Zero;
        try
        {
            remoteFindInfo = VirtualAllocEx(processHandle, IntPtr.Zero,
                (UIntPtr)infoSize, MemCommit | MemReserve, PageReadWrite);
            if (remoteFindInfo == IntPtr.Zero)
                return -1;

            if (!WriteProcessMemory(processHandle, remoteText, bytes,
                    bytes.Length, out var written) ||
                written.ToUInt64() != (ulong)bytes.Length)
                return -1;

            var info = new NativeLvFindInfo
            {
                Flags = LvfiString,
                Text = remoteText
            };
            if (!WriteRemoteStructure(processHandle, remoteFindInfo, info, infoSize))
                return -1;

            // LVM_FINDITEMW with LVFI_STRING (not LVFI_PARTIAL) is an
            // exact-name lookup inside the target MMC process.
            return checked((int)SendMessage(
                listView, LvmFindItemW, new IntPtr(-1), remoteFindInfo));
        }
        finally
        {
            if (remoteFindInfo != IntPtr.Zero)
                VirtualFreeEx(processHandle, remoteFindInfo, UIntPtr.Zero, MemRelease);
            VirtualFreeEx(processHandle, remoteText, UIntPtr.Zero, MemRelease);
        }
    }

    private static NativeListViewRow? FindBestNativeListViewMatch(
        IReadOnlyList<NativeListViewRow> rows,
        PolicySettingInfo setting,
        IReadOnlyList<string> candidates)
    {
        // The first column is Policy. Other columns contain settings/values
        // and cannot identify which policy would be opened.
        var names = rows
            .Select(row => row.Text.Split(" | ", StringSplitOptions.None)[0])
            .ToArray();
        var index = MmcPolicyNameMatcher.FindUniqueMatch(names, setting.SettingName);
        return index >= 0 ? rows[index] : null;
    }

    private static IReadOnlyList<IntPtr> FindNativeListViews(
        IntPtr parent)
    {
        var result =
            new List<IntPtr>();

        if (parent ==
            IntPtr.Zero)
        {
            return result;
        }

        EnumChildWindows(
            parent,
            (handle, _) =>
            {
                var className =
                    new StringBuilder(
                        128);

                if (GetClassName(
                        handle,
                        className,
                        className.Capacity) >
                    0 &&
                    className.ToString()
                        .Equals(
                            "SysListView32",
                            StringComparison.OrdinalIgnoreCase))
                {
                    result.Add(
                        handle);
                }

                return true;
            },
            IntPtr.Zero);

        return result;
    }

    private static IReadOnlyList<NativeListViewRow> ReadNativeListViewRows(
        IntPtr processHandle,
        IntPtr listView,
        int count,
        CancellationToken cancellationToken)
    {
        const int textCharacters =
            2048;

        var textBytes =
            checked(
                textCharacters *
                sizeof(char));

        var itemSize =
            Marshal.SizeOf<NativeLvItem>();

        var remoteText =
            VirtualAllocEx(
                processHandle,
                IntPtr.Zero,
                (UIntPtr)textBytes,
                MemCommit |
                MemReserve,
                PageReadWrite);

        var remoteItem =
            VirtualAllocEx(
                processHandle,
                IntPtr.Zero,
                (UIntPtr)itemSize,
                MemCommit |
                MemReserve,
                PageReadWrite);

        if (remoteText ==
                IntPtr.Zero ||
            remoteItem ==
                IntPtr.Zero)
        {
            if (remoteText !=
                IntPtr.Zero)
            {
                VirtualFreeEx(
                    processHandle,
                    remoteText,
                    UIntPtr.Zero,
                    MemRelease);
            }

            if (remoteItem !=
                IntPtr.Zero)
            {
                VirtualFreeEx(
                    processHandle,
                    remoteItem,
                    UIntPtr.Zero,
                    MemRelease);
            }

            return Array.Empty<NativeListViewRow>();
        }

        try
        {
            var result =
                new List<NativeListViewRow>(
                    Math.Min(
                        count,
                        2048));

            for (var index = 0;
                 index < count;
                 index++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var parts =
                    new List<string>();

                for (var subItem = 0;
                     subItem < 3;
                     subItem++)
                {
                    var text =
                        ReadNativeListViewText(
                            processHandle,
                            listView,
                            remoteItem,
                            itemSize,
                            remoteText,
                            textBytes,
                            textCharacters,
                            index,
                            subItem);

                    if (!string.IsNullOrWhiteSpace(
                            text))
                    {
                        parts.Add(
                            text);
                    }
                }

                if (parts.Count ==
                    0)
                {
                    continue;
                }

                result.Add(
                    new NativeListViewRow(
                        index,
                        string.Join(
                            " | ",
                            parts.Distinct(
                                StringComparer.CurrentCultureIgnoreCase))));
            }

            return result;
        }
        finally
        {
            VirtualFreeEx(
                processHandle,
                remoteText,
                UIntPtr.Zero,
                MemRelease);

            VirtualFreeEx(
                processHandle,
                remoteItem,
                UIntPtr.Zero,
                MemRelease);
        }
    }

    private static string ReadNativeListViewText(
        IntPtr processHandle,
        IntPtr listView,
        IntPtr remoteItem,
        int itemSize,
        IntPtr remoteText,
        int textBytes,
        int textCharacters,
        int index,
        int subItem)
    {
        var item =
            new NativeLvItem
            {
                Mask =
                    LvifText,
                Item =
                    index,
                SubItem =
                    subItem,
                Text =
                    remoteText,
                TextMax =
                    textCharacters
            };

        if (!WriteRemoteStructure(
                processHandle,
                remoteItem,
                item,
                itemSize))
        {
            return string.Empty;
        }

        _ =
            SendMessage(
                listView,
                LvmGetItemTextW,
                (IntPtr)index,
                remoteItem);

        var buffer =
            new byte[
                textBytes];

        if (!ReadProcessMemory(
                processHandle,
                remoteText,
                buffer,
                buffer.Length,
                out _))
        {
            return string.Empty;
        }

        return Encoding.Unicode
            .GetString(
                buffer)
            .TrimEnd(
                '\0')
            .Trim();
    }

    private static bool SelectAndOpenNativeListViewRow(
        Process process,
        IntPtr processHandle,
        IntPtr listView,
        int index,
        out int selectedIndex,
        out int focusedIndex)
    {
        selectedIndex =
            -1;

        focusedIndex =
            -1;

        var itemSize =
            Marshal.SizeOf<NativeLvItem>();

        var remoteItem =
            VirtualAllocEx(
                processHandle,
                IntPtr.Zero,
                (UIntPtr)itemSize,
                MemCommit |
                MemReserve,
                PageReadWrite);

        if (remoteItem ==
            IntPtr.Zero)
        {
            return false;
        }

        try
        {
            var clearState =
                new NativeLvItem
                {
                    State =
                        0,
                    StateMask =
                        LvisSelected |
                        LvisFocused
                };

            if (!WriteRemoteStructure(
                    processHandle,
                    remoteItem,
                    clearState,
                    itemSize))
            {
                return false;
            }

            _ =
                SendMessage(
                    listView,
                    LvmSetItemState,
                    new IntPtr(
                        -1),
                    remoteItem);

            var state =
                new NativeLvItem
                {
                    State =
                        LvisSelected |
                        LvisFocused,
                    StateMask =
                        LvisSelected |
                        LvisFocused
                };

            if (!WriteRemoteStructure(
                    processHandle,
                    remoteItem,
                    state,
                    itemSize))
            {
                return false;
            }

            _ =
                SendMessage(
                    listView,
                    LvmSetItemState,
                    (IntPtr)index,
                    remoteItem);

            _ =
                SendMessage(
                    listView,
                    LvmEnsureVisible,
                    (IntPtr)index,
                    IntPtr.Zero);

            Thread.Sleep(
                120);

            selectedIndex =
                checked(
                    (int)SendMessage(
                        listView,
                        LvmGetNextItem,
                        new IntPtr(
                            -1),
                        (IntPtr)LvniSelected));

            focusedIndex =
                checked(
                    (int)SendMessage(
                        listView,
                        LvmGetNextItem,
                        new IntPtr(
                            -1),
                        (IntPtr)LvniFocused));

            if (selectedIndex !=
                    index ||
                focusedIndex !=
                    index)
            {
                return false;
            }

            ShowWindow(
                process.MainWindowHandle,
                SwRestore);

            SetForegroundWindow(
                process.MainWindowHandle);

            _ =
                SendMessage(
                    listView,
                    WmSetFocus,
                    IntPtr.Zero,
                    IntPtr.Zero);

            Thread.Sleep(
                80);

            _ =
                SendMessage(
                    listView,
                    WmKeyDown,
                    (IntPtr)VkReturn,
                    IntPtr.Zero);

            _ =
                SendMessage(
                    listView,
                    WmKeyUp,
                    (IntPtr)VkReturn,
                    IntPtr.Zero);

            return true;
        }
        finally
        {
            VirtualFreeEx(
                processHandle,
                remoteItem,
                UIntPtr.Zero,
                MemRelease);
        }
    }

    private static bool WriteRemoteStructure<T>(
        IntPtr processHandle,
        IntPtr remoteAddress,
        T value,
        int size)
        where T : struct
    {
        var local =
            Marshal.AllocHGlobal(
                size);

        try
        {
            Marshal.StructureToPtr(
                value,
                local,
                false);

            var bytes =
                new byte[
                    size];

            Marshal.Copy(
                local,
                bytes,
                0,
                bytes.Length);

            return WriteProcessMemory(
                processHandle,
                remoteAddress,
                bytes,
                bytes.Length,
                out _);
        }
        finally
        {
            Marshal.FreeHGlobal(
                local);
        }
    }

    private static bool ReadRemoteStructure<T>(
        IntPtr processHandle,
        IntPtr remoteAddress,
        out T value)
        where T : struct
    {
        var size =
            Marshal.SizeOf<T>();

        var bytes =
            new byte[
                size];

        if (!ReadProcessMemory(
                processHandle,
                remoteAddress,
                bytes,
                bytes.Length,
                out _))
        {
            value =
                default;

            return false;
        }

        var local =
            Marshal.AllocHGlobal(
                size);

        try
        {
            Marshal.Copy(
                bytes,
                0,
                local,
                bytes.Length);

            value =
                Marshal.PtrToStructure<T>(
                    local);

            return true;
        }
        finally
        {
            Marshal.FreeHGlobal(
                local);
        }
    }

    private sealed record NativeListViewRow(
        int Index,
        string Text);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeLvFindInfo
    {
        public uint Flags;
        public IntPtr Text;
        public IntPtr Parameter;
        public NativePoint Point;
        public uint Direction;
    }

    [StructLayout(
        LayoutKind.Sequential,
        CharSet =
            CharSet.Unicode)]
    private struct NativeLvItem
    {
        public uint Mask;
        public int Item;
        public int SubItem;
        public uint State;
        public uint StateMask;
        public IntPtr Text;
        public int TextMax;
        public int Image;
        public IntPtr Parameter;
        public int Indent;
        public int GroupId;
        public uint Columns;
        public IntPtr ColumnIndices;
        public IntPtr ColumnFormats;
        public int Group;
    }

    [StructLayout(
        LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(
        LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    private static void TryRealize(
        AutomationElement element)
    {
        try
        {
            if (element.TryGetCurrentPattern(
                    VirtualizedItemPattern.Pattern,
                    out var value) &&
                value is VirtualizedItemPattern pattern)
            {
                pattern.Realize();
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

    private delegate bool EnumChildProc(
        IntPtr windowHandle,
        IntPtr parameter);

    [DllImport(
        "user32.dll",
        CharSet =
            CharSet.Unicode)]
    private static extern int GetClassName(
        IntPtr windowHandle,
        StringBuilder className,
        int maxCount);

    [DllImport(
        "user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumChildWindows(
        IntPtr parentWindow,
        EnumChildProc callback,
        IntPtr parameter);

    [DllImport(
        "user32.dll",
        CharSet =
            CharSet.Unicode)]
    private static extern IntPtr SendMessage(
        IntPtr windowHandle,
        int message,
        IntPtr wParam,
        IntPtr lParam);

    [DllImport(
        "user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ClientToScreen(
        IntPtr windowHandle,
        ref NativePoint point);

    [DllImport(
        "kernel32.dll",
        SetLastError =
            true)]
    private static extern IntPtr OpenProcess(
        uint desiredAccess,
        [MarshalAs(UnmanagedType.Bool)]
        bool inheritHandle,
        int processId);

    [DllImport(
        "kernel32.dll",
        SetLastError =
            true)]
    private static extern IntPtr VirtualAllocEx(
        IntPtr processHandle,
        IntPtr address,
        UIntPtr size,
        uint allocationType,
        uint protect);

    [DllImport(
        "kernel32.dll",
        SetLastError =
            true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool VirtualFreeEx(
        IntPtr processHandle,
        IntPtr address,
        UIntPtr size,
        uint freeType);

    [DllImport(
        "kernel32.dll",
        SetLastError =
            true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReadProcessMemory(
        IntPtr processHandle,
        IntPtr baseAddress,
        [Out] byte[] buffer,
        int size,
        out UIntPtr bytesRead);

    [DllImport(
        "kernel32.dll",
        SetLastError =
            true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WriteProcessMemory(
        IntPtr processHandle,
        IntPtr baseAddress,
        byte[] buffer,
        int size,
        out UIntPtr bytesWritten);

    [DllImport(
        "kernel32.dll",
        SetLastError =
            true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(
        IntPtr handle);

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

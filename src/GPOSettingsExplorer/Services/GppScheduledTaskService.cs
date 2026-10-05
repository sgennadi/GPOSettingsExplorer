using System.Globalization;
using System.Xml.Linq;
using GPOSettingsExplorer.Models;

namespace GPOSettingsExplorer.Services;

public sealed class GppScheduledTaskService
{
    private const string RootClsid =
        "{CC63F200-7309-4BA0-B154-A71CD118DBCC}";
    private const string TaskV2Clsid =
        "{D8896631-B747-47A7-84A6-C155337F3BC8}";
    private const string ImmediateTaskV2Clsid =
        "{9756B581-76EC-4169-9AFC-0CA8D43ADB5F}";

    private const string TaskSchedulerNamespace =
        "http://schemas.microsoft.com/windows/2004/02/mit/task";

    private readonly GppDocumentService _documents;

    public GppScheduledTaskService(GppDocumentService documents)
    {
        _documents = documents;
    }

    public IReadOnlyList<GppScheduledTaskInfo> Load(
        IEnumerable<GpoInfo> gpos,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var result = new List<GppScheduledTaskInfo>();
        var list = gpos.ToList();
        var type = GetScheduledTaskType();

        for (var index = 0; index < list.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var gpo = list[index];

            progress?.Report(
                $"Scheduled Tasks {index + 1}/{list.Count}: {gpo.DisplayName}");

            foreach (var scope in new[] { "Computer", "User" })
            {
                var target = _documents.BuildTarget(gpo, scope, type);
                if (!File.Exists(target.XmlPath))
                    continue;

                try
                {
                    var document = XDocument.Load(
                        target.XmlPath,
                        LoadOptions.PreserveWhitespace);

                    foreach (var kind in new[]
                             {
                                 "TaskV2",
                                 "ImmediateTaskV2",
                                 "Task",
                                 "ImmediateTask"
                             })
                    {
                        var ordinal = 0;

                        foreach (var element in document
                                     .Descendants()
                                     .Where(child =>
                                         child.Name.LocalName == kind))
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            ordinal++;

                            var properties = element.Elements()
                                .FirstOrDefault(child =>
                                    child.Name.LocalName == "Properties");

                            if (properties is null)
                                continue;

                            var filters = element.Elements()
                                .FirstOrDefault(child =>
                                    child.Name.LocalName == "Filters");

                            var task = properties.Elements()
                                .FirstOrDefault(child =>
                                    child.Name.LocalName == "Task");

                            result.Add(ReadItem(
                                gpo,
                                scope,
                                target.XmlPath,
                                kind,
                                ordinal,
                                element,
                                properties,
                                task,
                                filters));
                        }
                    }
                }
                catch
                {
                    // Malformed ScheduledTasks.xml stays accessible in the raw GPP XML editor.
                }
            }
        }

        return result
            .OrderBy(item => item.GpoName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(item => item.Scope, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.KindDisplay, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(item => item.TaskName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(item => item.Ordinal)
            .ToArray();
    }

    public GppScheduledTaskInfo CreateNew(
        GpoInfo gpo,
        string scope,
        bool immediate)
    {
        var normalizedScope = NormalizeScope(scope);
        var kind = immediate ? "ImmediateTaskV2" : "TaskV2";

        var item = new GppScheduledTaskInfo
        {
            GpoId = gpo.Id,
            GpoName = gpo.DisplayName,
            DomainName = gpo.DomainName,
            Scope = normalizedScope,
            XmlPath = _documents.BuildTarget(
                gpo,
                normalizedScope,
                GetScheduledTaskType()).XmlPath,
            Uid = Guid.NewGuid().ToString("B").ToUpperInvariant(),
            ItemKind = kind,
            DisplayName = immediate
                ? "New Immediate Task"
                : "New Scheduled Task",
            Action = "U",
            TaskName = immediate
                ? "New Immediate Task"
                : "New Scheduled Task",
            RunAs = normalizedScope == "Computer"
                ? "SYSTEM"
                : "%LogonDomain%\%LogonUser%",
            LogonType = normalizedScope == "Computer"
                ? "ServiceAccount"
                : "InteractiveToken",
            Author = "GPOSettingsExplorer",
            RunLevel = "HighestAvailable",
            TaskEnabled = true,
            AllowStartOnDemand = true,
            Command = "cmd.exe"
        };

        item.TaskXml = BuildDefaultTaskXml(item);
        PopulateTaskFields(item, XElement.Parse(item.TaskXml));
        return item;
    }

    public void Save(
        GpoInfo gpo,
        string domainDistinguishedName,
        GppScheduledTaskInfo item)
    {
        if (!item.IsV2)
        {
            throw new InvalidOperationException(
                "Legacy Task and ImmediateTask items are read-only in the structured editor. " +
                "Use Show raw XML to edit legacy task definitions.");
        }

        Validate(item);

        var target = _documents.BuildTarget(
            gpo,
            item.Scope,
            GetScheduledTaskType());

        XDocument document;

        if (File.Exists(target.XmlPath))
        {
            document = XDocument.Load(
                target.XmlPath,
                LoadOptions.PreserveWhitespace);

            if (document.Root is null ||
                !document.Root.Name.LocalName.Equals(
                    "ScheduledTasks",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "The existing ScheduledTasks.xml root element is not <ScheduledTasks>. " +
                    "Use the raw GPP XML editor to repair it.");
            }
        }
        else
        {
            document = new XDocument(
                new XDeclaration("1.0", "utf-8", null),
                new XElement(
                    "ScheduledTasks",
                    new XAttribute("clsid", RootClsid)));
        }

        var items = document
            .Descendants()
            .Where(element =>
                element.Name.LocalName == item.ItemKind)
            .ToArray();

        var selected = FindItem(items, item);

        if (selected is null)
        {
            selected = new XElement(
                item.ItemKind,
                new XAttribute(
                    "clsid",
                    item.ItemKind == "ImmediateTaskV2"
                        ? ImmediateTaskV2Clsid
                        : TaskV2Clsid));

            document.Root!.Add(selected);
        }

        UpdateItem(selected, item);

        _documents.SaveXml(
            gpo,
            domainDistinguishedName,
            target,
            document.ToString());
    }

    public void Delete(
        GpoInfo gpo,
        string domainDistinguishedName,
        GppScheduledTaskInfo item)
    {
        var target = _documents.BuildTarget(
            gpo,
            item.Scope,
            GetScheduledTaskType());

        if (!File.Exists(target.XmlPath))
            return;

        var document = XDocument.Load(
            target.XmlPath,
            LoadOptions.PreserveWhitespace);

        var items = document
            .Descendants()
            .Where(element =>
                element.Name.LocalName == item.ItemKind)
            .ToArray();

        var selected = FindItem(items, item)
            ?? throw new InvalidOperationException(
                "The selected Scheduled Task preference no longer exists. Refresh the list.");

        selected.Remove();

        var hasItems = document.Root?
            .Elements()
            .Any(element =>
                element.Name.LocalName is
                    "TaskV2" or
                    "ImmediateTaskV2" or
                    "Task" or
                    "ImmediateTask")
            == true;

        if (!hasItems)
        {
            _documents.Delete(
                gpo,
                domainDistinguishedName,
                target);
            return;
        }

        _documents.SaveXml(
            gpo,
            domainDistinguishedName,
            target,
            document.ToString());
    }

    public void CopyEditableValues(
        GppScheduledTaskInfo source,
        GppScheduledTaskInfo target)
    {
        target.DisplayName = source.DisplayName;
        target.Description = source.Description;
        target.Action = source.Action;
        target.TaskName = source.TaskName;
        target.RunAs = source.RunAs;
        target.LogonType = source.LogonType;

        // cpassword is intentionally never copied to a new preference item.
        target.OpaqueCredential = string.Empty;
        target.ClearStoredCredential = false;

        target.Author = source.Author;
        target.TaskDescription = source.TaskDescription;
        target.RunLevel = source.RunLevel;
        target.TaskEnabled = source.TaskEnabled;
        target.Hidden = source.Hidden;
        target.WakeToRun = source.WakeToRun;
        target.StartWhenAvailable = source.StartWhenAvailable;
        target.AllowStartOnDemand = source.AllowStartOnDemand;
        target.DisallowStartIfOnBatteries =
            source.DisallowStartIfOnBatteries;
        target.StopIfGoingOnBatteries =
            source.StopIfGoingOnBatteries;

        target.Command = source.Command;
        target.Arguments = source.Arguments;
        target.WorkingDirectory = source.WorkingDirectory;
        target.TaskXml = source.TaskXml;
        target.TriggerSummary = source.TriggerSummary;

        target.Disabled = source.Disabled;
        target.BypassErrors = source.BypassErrors;
        target.RemoveWhenNoLongerApplied =
            source.RemoveWhenNoLongerApplied;
        target.RunInUserContext = source.RunInUserContext;
        target.FiltersXml = source.FiltersXml;
    }

    public void SynchronizeTaskXmlFromStructuredFields(
        GppScheduledTaskInfo item)
    {
        var task = ParseTaskXml(item.TaskXml);

        SetElementValue(
            task,
            "RegistrationInfo",
            "Author",
            item.Author);

        SetElementValue(
            task,
            "RegistrationInfo",
            "Description",
            item.TaskDescription);

        var principal = EnsureDescendant(
            task,
            "Principals",
            "Principal");

        SetChildValue(
            principal,
            "UserId",
            item.RunAs);

        SetChildValue(
            principal,
            "LogonType",
            item.LogonType);

        SetChildValue(
            principal,
            "RunLevel",
            item.RunLevel);

        var settings = EnsureChild(
            task,
            "Settings");

        SetChildValue(
            settings,
            "Enabled",
            BoolWord(item.TaskEnabled));

        SetChildValue(
            settings,
            "Hidden",
            BoolWord(item.Hidden));

        SetChildValue(
            settings,
            "WakeToRun",
            BoolWord(item.WakeToRun));

        SetChildValue(
            settings,
            "StartWhenAvailable",
            BoolWord(item.StartWhenAvailable));

        SetChildValue(
            settings,
            "AllowStartOnDemand",
            BoolWord(item.AllowStartOnDemand));

        SetChildValue(
            settings,
            "DisallowStartIfOnBatteries",
            BoolWord(item.DisallowStartIfOnBatteries));

        SetChildValue(
            settings,
            "StopIfGoingOnBatteries",
            BoolWord(item.StopIfGoingOnBatteries));

        var actions = EnsureChild(
            task,
            "Actions");

        var exec = actions.Elements()
            .FirstOrDefault(element =>
                element.Name.LocalName == "Exec");

        if (exec is null)
        {
            exec = new XElement(
                task.GetDefaultNamespace() + "Exec");

            actions.Add(exec);
        }

        SetChildValue(
            exec,
            "Command",
            item.Command);

        SetChildValue(
            exec,
            "Arguments",
            item.Arguments,
            removeIfEmpty: true);

        SetChildValue(
            exec,
            "WorkingDirectory",
            item.WorkingDirectory,
            removeIfEmpty: true);

        item.TaskXml = task.ToString();
        item.TriggerSummary = BuildTriggerSummary(task);
    }

    public void RefreshStructuredFieldsFromTaskXml(
        GppScheduledTaskInfo item)
    {
        var task = ParseTaskXml(item.TaskXml);
        PopulateTaskFields(item, task);
    }

    private static GppScheduledTaskInfo ReadItem(
        GpoInfo gpo,
        string scope,
        string xmlPath,
        string kind,
        int ordinal,
        XElement element,
        XElement properties,
        XElement? task,
        XElement? filters)
    {
        var item = new GppScheduledTaskInfo
        {
            GpoId = gpo.Id,
            GpoName = gpo.DisplayName,
            DomainName = gpo.DomainName,
            Scope = scope,
            XmlPath = xmlPath,
            Uid = Attr(element, "uid"),
            Ordinal = ordinal,
            ItemKind = kind,
            DisplayName = FirstNonEmpty(
                Attr(element, "name"),
                Attr(element, "status"),
                Attr(properties, "name"),
                "Scheduled Task"),
            Description = FirstNonEmpty(
                Attr(element, "desc"),
                Attr(element, "descr")),
            Action = FirstNonEmpty(
                Attr(properties, "action"),
                "U"),
            TaskName = Attr(properties, "name"),
            RunAs = Attr(properties, "runAs"),
            LogonType = Attr(properties, "logonType"),
            OpaqueCredential = Attr(properties, "cpassword"),
            Disabled = IsTrue(Attr(element, "disabled")),
            BypassErrors = IsTrue(Attr(element, "bypassErrors")),
            RemoveWhenNoLongerApplied = IsTrue(
                Attr(element, "removePolicy")),
            RunInUserContext = IsTrue(
                Attr(element, "userContext")),
            FiltersXml =
                filters?.ToString(SaveOptions.DisableFormatting)
                ?? string.Empty,
            TaskXml = task?.ToString() ?? string.Empty
        };

        if (task is not null)
            PopulateTaskFields(item, task);

        return item;
    }

    private static void PopulateTaskFields(
        GppScheduledTaskInfo item,
        XElement task)
    {
        item.Author = DescendantValue(
            task,
            "RegistrationInfo",
            "Author");

        item.TaskDescription = DescendantValue(
            task,
            "RegistrationInfo",
            "Description");

        var principal = task.Descendants()
            .FirstOrDefault(element =>
                element.Name.LocalName == "Principal");

        if (principal is not null)
        {
            item.RunAs = FirstNonEmpty(
                ChildValue(principal, "UserId"),
                item.RunAs);

            item.LogonType = FirstNonEmpty(
                ChildValue(principal, "LogonType"),
                item.LogonType);

            item.RunLevel = FirstNonEmpty(
                ChildValue(principal, "RunLevel"),
                "LeastPrivilege");
        }

        var settings = task.Descendants()
            .FirstOrDefault(element =>
                element.Name.LocalName == "Settings");

        if (settings is not null)
        {
            item.TaskEnabled = ReadBool(
                ChildValue(settings, "Enabled"),
                defaultValue: true);

            item.Hidden = ReadBool(
                ChildValue(settings, "Hidden"));

            item.WakeToRun = ReadBool(
                ChildValue(settings, "WakeToRun"));

            item.StartWhenAvailable = ReadBool(
                ChildValue(settings, "StartWhenAvailable"));

            item.AllowStartOnDemand = ReadBool(
                ChildValue(settings, "AllowStartOnDemand"),
                defaultValue: true);

            item.DisallowStartIfOnBatteries = ReadBool(
                ChildValue(
                    settings,
                    "DisallowStartIfOnBatteries"));

            item.StopIfGoingOnBatteries = ReadBool(
                ChildValue(
                    settings,
                    "StopIfGoingOnBatteries"));
        }

        var exec = task.Descendants()
            .FirstOrDefault(element =>
                element.Name.LocalName == "Exec");

        if (exec is not null)
        {
            item.Command = ChildValue(
                exec,
                "Command");

            item.Arguments = ChildValue(
                exec,
                "Arguments");

            item.WorkingDirectory = ChildValue(
                exec,
                "WorkingDirectory");
        }

        item.TriggerSummary = BuildTriggerSummary(task);
    }

    private static void UpdateItem(
        XElement element,
        GppScheduledTaskInfo item)
    {
        var display = string.IsNullOrWhiteSpace(item.DisplayName)
            ? item.TaskName.Trim()
            : item.DisplayName.Trim();

        SetAttr(
            element,
            "clsid",
            item.ItemKind == "ImmediateTaskV2"
                ? ImmediateTaskV2Clsid
                : TaskV2Clsid);

        SetAttr(
            element,
            "name",
            display);

        SetAttr(
            element,
            "status",
            item.TaskName.Trim());

        SetAttr(
            element,
            "image",
            "2");

        SetAttr(
            element,
            "changed",
            DateTime.UtcNow.ToString(
                "yyyy-MM-dd HH:mm:ss",
                CultureInfo.InvariantCulture));

        if (string.IsNullOrWhiteSpace(item.Uid))
        {
            item.Uid = Guid.NewGuid()
                .ToString("B")
                .ToUpperInvariant();
        }

        SetAttr(
            element,
            "uid",
            item.Uid);

        if (string.IsNullOrWhiteSpace(item.Description))
            element.SetAttributeValue("desc", null);
        else
            SetAttr(element, "desc", item.Description);

        SetOptionalBool(
            element,
            "disabled",
            item.Disabled);

        SetOptionalBool(
            element,
            "bypassErrors",
            item.BypassErrors);

        SetOptionalBool(
            element,
            "removePolicy",
            item.RemoveWhenNoLongerApplied);

        SetOptionalBool(
            element,
            "userContext",
            item.RunInUserContext);

        var properties = element.Elements()
            .FirstOrDefault(child =>
                child.Name.LocalName == "Properties");

        if (properties is null)
        {
            properties = new XElement("Properties");
            element.Add(properties);
        }

        SetAttr(
            properties,
            "action",
            NormalizeAction(item.Action));

        SetAttr(
            properties,
            "name",
            item.TaskName.Trim());

        SetAttr(
            properties,
            "runAs",
            item.RunAs.Trim());

        SetAttr(
            properties,
            "logonType",
            NormalizeLogonType(item.LogonType));

        if (item.ClearStoredCredential ||
            string.IsNullOrWhiteSpace(item.OpaqueCredential))
        {
            properties.SetAttributeValue(
                "cpassword",
                null);
        }
        else
        {
            SetAttr(
                properties,
                "cpassword",
                item.OpaqueCredential);
        }

        var existingTask = properties.Elements()
            .FirstOrDefault(child =>
                child.Name.LocalName == "Task");

        var task = ParseTaskXml(item.TaskXml);

        if (existingTask is null)
            properties.Add(task);
        else
            existingTask.ReplaceWith(task);

        ApplyFilters(
            element,
            item.FiltersXml);
    }

    private static XElement ParseTaskXml(
        string taskXml)
    {
        if (string.IsNullOrWhiteSpace(taskXml))
        {
            throw new InvalidOperationException(
                "Task Scheduler XML cannot be empty.");
        }

        XElement task;

        try
        {
            task = XElement.Parse(
                taskXml,
                LoadOptions.PreserveWhitespace);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Task Scheduler XML is invalid: {ex.Message}",
                ex);
        }

        if (!task.Name.LocalName.Equals(
                "Task",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Task Scheduler XML must have a <Task> root element.");
        }

        return task;
    }

    private static string BuildDefaultTaskXml(
        GppScheduledTaskInfo item)
    {
        XNamespace ns = TaskSchedulerNamespace;

        XElement trigger = item.Scope.Equals(
            "Computer",
            StringComparison.OrdinalIgnoreCase)
            ? new XElement(
                ns + "BootTrigger",
                new XElement(
                    ns + "Enabled",
                    "true"))
            : new XElement(
                ns + "LogonTrigger",
                new XElement(
                    ns + "Enabled",
                    "true"));

        var task = new XElement(
            ns + "Task",
            new XAttribute(
                "version",
                "1.2"),
            new XElement(
                ns + "RegistrationInfo",
                new XElement(
                    ns + "Author",
                    item.Author)),
            new XElement(
                ns + "Triggers",
                trigger),
            new XElement(
                ns + "Principals",
                new XElement(
                    ns + "Principal",
                    new XAttribute(
                        "id",
                        "Author"),
                    new XElement(
                        ns + "UserId",
                        item.RunAs),
                    new XElement(
                        ns + "LogonType",
                        item.LogonType),
                    new XElement(
                        ns + "RunLevel",
                        item.RunLevel))),
            new XElement(
                ns + "Settings",
                new XElement(
                    ns + "MultipleInstancesPolicy",
                    "IgnoreNew"),
                new XElement(
                    ns + "DisallowStartIfOnBatteries",
                    "false"),
                new XElement(
                    ns + "StopIfGoingOnBatteries",
                    "false"),
                new XElement(
                    ns + "AllowHardTerminate",
                    "true"),
                new XElement(
                    ns + "StartWhenAvailable",
                    "false"),
                new XElement(
                    ns + "RunOnlyIfNetworkAvailable",
                    "false"),
                new XElement(
                    ns + "AllowStartOnDemand",
                    "true"),
                new XElement(
                    ns + "Enabled",
                    "true"),
                new XElement(
                    ns + "Hidden",
                    "false"),
                new XElement(
                    ns + "WakeToRun",
                    "false"),
                new XElement(
                    ns + "ExecutionTimeLimit",
                    "PT1H"),
                new XElement(
                    ns + "Priority",
                    "7")),
            new XElement(
                ns + "Actions",
                new XAttribute(
                    "Context",
                    "Author"),
                new XElement(
                    ns + "Exec",
                    new XElement(
                        ns + "Command",
                        item.Command))));

        return task.ToString();
    }

    private static void Validate(
        GppScheduledTaskInfo item)
    {
        if (string.IsNullOrWhiteSpace(item.TaskName))
        {
            throw new InvalidOperationException(
                "Scheduled task name cannot be empty.");
        }

        if (string.IsNullOrWhiteSpace(item.RunAs))
        {
            throw new InvalidOperationException(
                "Run-as account cannot be empty.");
        }

        var logonType = NormalizeLogonType(
            item.LogonType);

        if (logonType is not (
                "Group" or
                "ServiceAccount" or
                "InteractiveToken"))
        {
            throw new InvalidOperationException(
                "GPP TaskV2 logonType must be Group, ServiceAccount, or InteractiveToken.");
        }

        if (string.IsNullOrWhiteSpace(item.Command) &&
            NormalizeAction(item.Action) != "D")
        {
            throw new InvalidOperationException(
                "The first Exec action requires a command. " +
                "For complex multi-action tasks, edit the Task XML on the Advanced tab.");
        }

        _ = ParseTaskXml(item.TaskXml);

        if (!string.IsNullOrWhiteSpace(item.FiltersXml))
        {
            var filters = XElement.Parse(
                item.FiltersXml);

            if (!filters.Name.LocalName.Equals(
                    "Filters",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "Item-level targeting XML must have a <Filters> root element.");
            }
        }
    }

    private static void ApplyFilters(
        XElement item,
        string filtersXml)
    {
        var existing = item.Elements()
            .FirstOrDefault(element =>
                element.Name.LocalName == "Filters");

        if (string.IsNullOrWhiteSpace(filtersXml))
        {
            existing?.Remove();
            return;
        }

        XElement filters;

        try
        {
            filters = XElement.Parse(
                filtersXml,
                LoadOptions.PreserveWhitespace);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Item-level targeting XML is invalid: {ex.Message}",
                ex);
        }

        if (!filters.Name.LocalName.Equals(
                "Filters",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Item-level targeting XML must have a <Filters> root element.");
        }

        if (existing is null)
            item.AddFirst(filters);
        else
            existing.ReplaceWith(filters);
    }

    private static XElement? FindItem(
        IReadOnlyList<XElement> items,
        GppScheduledTaskInfo item)
    {
        if (!string.IsNullOrWhiteSpace(item.Uid))
        {
            var byUid = items.FirstOrDefault(
                element =>
                    Attr(
                        element,
                        "uid").Equals(
                        item.Uid,
                        StringComparison.OrdinalIgnoreCase));

            if (byUid is not null)
                return byUid;
        }

        if (item.Ordinal > 0 &&
            item.Ordinal <= items.Count)
        {
            return items[item.Ordinal - 1];
        }

        return null;
    }

    private static string BuildTriggerSummary(
        XElement task)
    {
        var triggers = task.Descendants()
            .Where(element =>
                element.Parent?.Name.LocalName == "Triggers")
            .Select(element =>
            {
                var type = element.Name.LocalName;
                var start = ChildValue(
                    element,
                    "StartBoundary");

                return string.IsNullOrWhiteSpace(start)
                    ? type
                    : $"{type}: {start}";
            })
            .ToArray();

        return triggers.Length == 0
            ? "<No triggers>"
            : string.Join(" | ", triggers);
    }

    private GppDocumentTypeInfo GetScheduledTaskType() =>
        _documents.GetKnownTypes().First(
            type =>
                type.Name.Equals(
                    "Scheduled Tasks",
                    StringComparison.OrdinalIgnoreCase));

    private static XElement EnsureDescendant(
        XElement root,
        string parentName,
        string childName)
    {
        var parent = root.Elements()
            .FirstOrDefault(element =>
                element.Name.LocalName == parentName);

        if (parent is null)
        {
            parent = new XElement(
                root.GetDefaultNamespace() + parentName);
            root.Add(parent);
        }

        var child = parent.Elements()
            .FirstOrDefault(element =>
                element.Name.LocalName == childName);

        if (child is not null)
            return child;

        child = new XElement(
            root.GetDefaultNamespace() + childName);

        if (childName == "Principal")
        {
            child.SetAttributeValue(
                "id",
                "Author");
        }

        parent.Add(child);
        return child;
    }

    private static XElement EnsureChild(
        XElement parent,
        string childName)
    {
        var child = parent.Elements()
            .FirstOrDefault(element =>
                element.Name.LocalName == childName);

        if (child is not null)
            return child;

        child = new XElement(
            parent.GetDefaultNamespace() + childName);

        parent.Add(child);
        return child;
    }

    private static void SetElementValue(
        XElement root,
        string parentName,
        string childName,
        string value)
    {
        var parent = EnsureChild(
            root,
            parentName);

        SetChildValue(
            parent,
            childName,
            value,
            removeIfEmpty: true);
    }

    private static void SetChildValue(
        XElement parent,
        string childName,
        string value,
        bool removeIfEmpty = false)
    {
        var child = parent.Elements()
            .FirstOrDefault(element =>
                element.Name.LocalName == childName);

        if (removeIfEmpty &&
            string.IsNullOrWhiteSpace(value))
        {
            child?.Remove();
            return;
        }

        if (child is null)
        {
            child = new XElement(
                parent.GetDefaultNamespace() + childName);

            parent.Add(child);
        }

        child.Value = value ?? string.Empty;
    }

    private static string DescendantValue(
        XElement root,
        string parentName,
        string childName)
    {
        var parent = root.Descendants()
            .FirstOrDefault(element =>
                element.Name.LocalName == parentName);

        return parent is null
            ? string.Empty
            : ChildValue(
                parent,
                childName);
    }

    private static string ChildValue(
        XElement parent,
        string childName) =>
        parent.Elements()
            .FirstOrDefault(element =>
                element.Name.LocalName == childName)?
            .Value ?? string.Empty;

    private static string NormalizeScope(
        string scope) =>
        scope.Equals(
            "User",
            StringComparison.OrdinalIgnoreCase)
            ? "User"
            : "Computer";

    private static string NormalizeAction(
        string action) =>
        action.Trim()
            .ToUpperInvariant() switch
        {
            "C" or "CREATE" => "C",
            "D" or "DELETE" => "D",
            "R" or "REPLACE" => "R",
            _ => "U"
        };

    private static string NormalizeLogonType(
        string value) =>
        value.Trim()
            .Replace(
                " ",
                string.Empty,
                StringComparison.Ordinal)
            .ToUpperInvariant() switch
        {
            "GROUP" => "Group",
            "SERVICEACCOUNT" => "ServiceAccount",
            _ => "InteractiveToken"
        };

    private static string Attr(
        XElement element,
        string name) =>
        element.Attributes()
            .FirstOrDefault(attribute =>
                attribute.Name.LocalName.Equals(
                    name,
                    StringComparison.OrdinalIgnoreCase))?
            .Value ?? string.Empty;

    private static bool IsTrue(
        string value) =>
        value == "1" ||
        value.Equals(
            "true",
            StringComparison.OrdinalIgnoreCase);

    private static bool ReadBool(
        string value,
        bool defaultValue = false)
    {
        if (string.IsNullOrWhiteSpace(value))
            return defaultValue;

        return value == "1" ||
               value.Equals(
                   "true",
                   StringComparison.OrdinalIgnoreCase);
    }

    private static string BoolWord(
        bool value) =>
        value ? "true" : "false";

    private static string FirstNonEmpty(
        params string[] values) =>
        values.FirstOrDefault(
            value =>
                !string.IsNullOrWhiteSpace(value))
        ?? string.Empty;

    private static void SetAttr(
        XElement element,
        string name,
        string value) =>
        element.SetAttributeValue(
            name,
            value ?? string.Empty);

    private static void SetOptionalBool(
        XElement element,
        string name,
        bool value)
    {
        if (value)
            element.SetAttributeValue(
                name,
                "1");
        else
            element.SetAttributeValue(
                name,
                null);
    }
}

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

    private readonly GppDocumentService _documents;

    public GppScheduledTaskService(GppDocumentService documents)
    {
        _documents = documents;
    }

    public IReadOnlyList<GppScheduledTaskItemInfo> Load(
        IEnumerable<GpoInfo> gpos,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var result = new List<GppScheduledTaskItemInfo>();
        var list = gpos.ToList();
        var type = GetScheduledTasksType();

        for (var index = 0; index < list.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var gpo = list[index];
            progress?.Report($"Scheduled Tasks {index + 1}/{list.Count}: {gpo.DisplayName}");

            foreach (var scope in new[] { "Computer", "User" })
            {
                var target = _documents.BuildTarget(gpo, scope, type);
                if (!File.Exists(target.XmlPath))
                    continue;

                try
                {
                    var document = XDocument.Load(target.XmlPath, LoadOptions.PreserveWhitespace);
                    var ordinal = 0;

                    foreach (var item in document.Root?.Elements() ?? Enumerable.Empty<XElement>())
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        if (!IsTaskElement(item.Name.LocalName))
                            continue;

                        ordinal++;
                        result.Add(ReadItem(gpo, scope, target.XmlPath, ordinal, item));
                    }
                }
                catch
                {
                    // Malformed ScheduledTasks.xml remains available through the raw GPP XML editor.
                }
            }
        }

        return result
            .OrderBy(item => item.GpoName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(item => item.Scope, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(item => item.Ordinal)
            .ToArray();
    }

    public GppScheduledTaskItemInfo CreateNew(
        GpoInfo gpo,
        string scope,
        bool immediate)
    {
        var normalizedScope = NormalizeScope(scope);

        return new GppScheduledTaskItemInfo
        {
            GpoId = gpo.Id,
            GpoName = gpo.DisplayName,
            DomainName = gpo.DomainName,
            Scope = normalizedScope,
            XmlPath = _documents.BuildTarget(
                gpo,
                normalizedScope,
                GetScheduledTasksType()).XmlPath,
            Uid = Guid.NewGuid().ToString("B").ToUpperInvariant(),
            TaskKind = immediate ? "ImmediateTaskV2" : "TaskV2",
            DisplayName = immediate ? "New Immediate Task" : "New Scheduled Task",
            Action = "U",
            RunAs = normalizedScope.Equals("Computer", StringComparison.OrdinalIgnoreCase)
                ? "NT AUTHORITY\\SYSTEM"
                : string.Empty,
            Author = $"{Environment.UserDomainName}\\{Environment.UserName}",
            TriggerType = immediate ? "Immediate" : "Daily",
            StartBoundary = DateTime.Now
                .AddMinutes(5)
                .ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture),
            Command = string.Empty,
            TaskEnabled = true
        };
    }

    public void Save(
        GpoInfo gpo,
        string domainDistinguishedName,
        GppScheduledTaskItemInfo item)
    {
        Validate(item);

        var target = _documents.BuildTarget(
            gpo,
            item.Scope,
            GetScheduledTasksType());

        XDocument document;

        if (File.Exists(target.XmlPath))
        {
            document = XDocument.Load(target.XmlPath, LoadOptions.PreserveWhitespace);

            if (document.Root is null ||
                !document.Root.Name.LocalName.Equals("ScheduledTasks", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "The existing ScheduledTasks.xml root is not <ScheduledTasks>. Use the raw GPP XML editor to repair it.");
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

        var task = FindItem(document, item);

        if (task is null)
        {
            task = new XElement(
                item.TaskKind,
                new XAttribute("clsid", ItemClsid(item.TaskKind)));

            document.Root!.Add(task);
        }

        UpdateTask(task, item);

        _documents.SaveXml(
            gpo,
            domainDistinguishedName,
            target,
            document.ToString());
    }

    public void Delete(
        GpoInfo gpo,
        string domainDistinguishedName,
        GppScheduledTaskItemInfo item)
    {
        var target = _documents.BuildTarget(
            gpo,
            item.Scope,
            GetScheduledTasksType());

        if (!File.Exists(target.XmlPath))
            return;

        var document = XDocument.Load(target.XmlPath, LoadOptions.PreserveWhitespace);
        var task = FindItem(document, item)
            ?? throw new InvalidOperationException(
                "The selected Scheduled Task preference no longer exists. Refresh the list.");

        task.Remove();

        if (!(document.Root?.Elements().Any(e => IsTaskElement(e.Name.LocalName)) ?? false))
        {
            _documents.Delete(gpo, domainDistinguishedName, target);
            return;
        }

        _documents.SaveXml(
            gpo,
            domainDistinguishedName,
            target,
            document.ToString());
    }

    public void CopyEditableValues(
        GppScheduledTaskItemInfo source,
        GppScheduledTaskItemInfo target,
        bool preserveCredential)
    {
        target.TaskKind = source.TaskKind;
        target.DisplayName = source.DisplayName;
        target.Description = source.Description;
        target.Action = source.Action;
        target.RunAs = source.RunAs;
        target.LogonType = source.LogonType;
        target.RunLevel = source.RunLevel;
        target.Author = source.Author;
        target.Command = source.Command;
        target.Arguments = source.Arguments;
        target.WorkingDirectory = source.WorkingDirectory;
        target.TriggerType = source.TriggerType;
        target.StartBoundary = source.StartBoundary;
        target.DaysInterval = source.DaysInterval;
        target.WeeksInterval = source.WeeksInterval;
        target.DaysOfWeek = source.DaysOfWeek;
        target.TriggerDelay = source.TriggerDelay;
        target.TaskEnabled = source.TaskEnabled;
        target.Hidden = source.Hidden;
        target.StartWhenAvailable = source.StartWhenAvailable;
        target.RunOnlyIfNetworkAvailable = source.RunOnlyIfNetworkAvailable;
        target.DisallowStartIfOnBatteries = source.DisallowStartIfOnBatteries;
        target.StopIfGoingOnBatteries = source.StopIfGoingOnBatteries;
        target.WakeToRun = source.WakeToRun;
        target.AllowStartOnDemand = source.AllowStartOnDemand;
        target.MultipleInstancesPolicy = source.MultipleInstancesPolicy;
        target.ExecutionTimeLimit = source.ExecutionTimeLimit;
        target.Priority = source.Priority;
        target.Disabled = source.Disabled;
        target.BypassErrors = source.BypassErrors;
        target.RemoveWhenNoLongerApplied = source.RemoveWhenNoLongerApplied;
        target.RunInUserContext = source.RunInUserContext;
        target.FiltersXml = source.FiltersXml;
        target.OpaqueCredential = preserveCredential
            ? source.OpaqueCredential
            : string.Empty;
        target.ClearStoredCredential = false;
        target.TaskXml = source.TaskXml;
    }

    private static GppScheduledTaskItemInfo ReadItem(
        GpoInfo gpo,
        string scope,
        string xmlPath,
        int ordinal,
        XElement element)
    {
        var properties = element.Elements()
            .FirstOrDefault(child => child.Name.LocalName == "Properties");

        var filters = element.Elements()
            .FirstOrDefault(child => child.Name.LocalName == "Filters");

        var taskXml = FindTaskXml(properties);
        var taskDocument = ParseTaskXml(taskXml);

        var displayName = FirstNonEmpty(
            Attr(element, "name"),
            Attr(element, "status"),
            ReadTaskElement(taskDocument, "RegistrationInfo", "Description"),
            "Scheduled Task");

        var trigger = ReadTrigger(taskDocument, element.Name.LocalName);

        return new GppScheduledTaskItemInfo
        {
            GpoId = gpo.Id,
            GpoName = gpo.DisplayName,
            DomainName = gpo.DomainName,
            Scope = scope,
            XmlPath = xmlPath,
            Uid = Attr(element, "uid"),
            Ordinal = ordinal,
            TaskKind = element.Name.LocalName,
            DisplayName = displayName,
            Description = ReadTaskElement(taskDocument, "RegistrationInfo", "Description"),
            Action = FirstNonEmpty(Attr(properties, "action"), "U"),
            RunAs = FirstNonEmpty(
                ReadTaskElement(taskDocument, "Principals", "UserId"),
                Attr(properties, "runAs")),
            LogonType = FirstNonEmpty(
                ReadTaskElement(taskDocument, "Principals", "LogonType"),
                "InteractiveToken"),
            RunLevel = FirstNonEmpty(
                ReadTaskElement(taskDocument, "Principals", "RunLevel"),
                "LeastPrivilege"),
            Author = ReadTaskElement(taskDocument, "RegistrationInfo", "Author"),
            Command = ReadTaskElement(taskDocument, "Actions", "Command"),
            Arguments = ReadTaskElement(taskDocument, "Actions", "Arguments"),
            WorkingDirectory = ReadTaskElement(taskDocument, "Actions", "WorkingDirectory"),
            TriggerType = trigger.Type,
            StartBoundary = trigger.StartBoundary,
            DaysInterval = trigger.DaysInterval,
            WeeksInterval = trigger.WeeksInterval,
            DaysOfWeek = trigger.DaysOfWeek,
            TriggerDelay = trigger.Delay,
            TaskEnabled = ReadTaskBool(taskDocument, "Settings", "Enabled", defaultValue: true),
            Hidden = ReadTaskBool(taskDocument, "Settings", "Hidden", defaultValue: false),
            StartWhenAvailable = ReadTaskBool(taskDocument, "Settings", "StartWhenAvailable", defaultValue: false),
            RunOnlyIfNetworkAvailable = ReadTaskBool(taskDocument, "Settings", "RunOnlyIfNetworkAvailable", defaultValue: false),
            DisallowStartIfOnBatteries = ReadTaskBool(taskDocument, "Settings", "DisallowStartIfOnBatteries", defaultValue: true),
            StopIfGoingOnBatteries = ReadTaskBool(taskDocument, "Settings", "StopIfGoingOnBatteries", defaultValue: true),
            WakeToRun = ReadTaskBool(taskDocument, "Settings", "WakeToRun", defaultValue: false),
            AllowStartOnDemand = ReadTaskBool(taskDocument, "Settings", "AllowStartOnDemand", defaultValue: true),
            MultipleInstancesPolicy = FirstNonEmpty(
                ReadTaskElement(taskDocument, "Settings", "MultipleInstancesPolicy"),
                "IgnoreNew"),
            ExecutionTimeLimit = FirstNonEmpty(
                ReadTaskElement(taskDocument, "Settings", "ExecutionTimeLimit"),
                "PT0S"),
            Priority = FirstNonEmpty(
                ReadTaskElement(taskDocument, "Settings", "Priority"),
                "7"),
            Disabled = IsTrue(Attr(element, "disabled")),
            BypassErrors = IsTrue(Attr(element, "bypassErrors")),
            RemoveWhenNoLongerApplied = IsTrue(Attr(element, "removePolicy")),
            RunInUserContext = IsTrue(Attr(element, "userContext")),
            FiltersXml = filters?.ToString(SaveOptions.DisableFormatting) ?? string.Empty,
            OpaqueCredential = Attr(properties, "cpassword"),
            TaskXml = taskXml
        };
    }

    private static void UpdateTask(
        XElement task,
        GppScheduledTaskItemInfo item)
    {
        if (item.IsLegacy)
        {
            throw new InvalidOperationException(
                "Legacy Scheduled Task preference items are read-only in the structured editor. Use raw XML editing for legacy items.");
        }

        SetAttr(task, "clsid", ItemClsid(item.TaskKind));
        SetAttr(task, "name", item.DisplayName.Trim());
        SetAttr(task, "status", item.DisplayName.Trim());
        SetAttr(task, "image", "2");
        SetAttr(task, "changed", DateTime.UtcNow.ToString(
            "yyyy-MM-dd HH:mm:ss",
            CultureInfo.InvariantCulture));

        if (string.IsNullOrWhiteSpace(item.Uid))
            item.Uid = Guid.NewGuid().ToString("B").ToUpperInvariant();

        SetAttr(task, "uid", item.Uid);
        SetOptionalBool(task, "disabled", item.Disabled);
        SetOptionalBool(task, "bypassErrors", item.BypassErrors);
        SetOptionalBool(task, "removePolicy", item.RemoveWhenNoLongerApplied);
        SetOptionalBool(task, "userContext", item.RunInUserContext);

        var properties = task.Elements()
            .FirstOrDefault(child => child.Name.LocalName == "Properties");

        if (properties is null)
        {
            properties = new XElement("Properties");
            task.AddFirst(properties);
        }

        SetAttr(properties, "action", item.Action.ToUpperInvariant());
        SetOptionalAttr(properties, "runAs", item.RunAs);

        if (item.ClearStoredCredential)
        {
            properties.SetAttributeValue("cpassword", null);
            item.OpaqueCredential = string.Empty;
        }
        else if (!string.IsNullOrWhiteSpace(item.OpaqueCredential))
        {
            SetAttr(properties, "cpassword", item.OpaqueCredential);
        }
        else
        {
            properties.SetAttributeValue("cpassword", null);
        }

        var taskXml = BuildTaskXml(item);

        var taskNode = properties.Elements()
            .FirstOrDefault(child => child.Name.LocalName == "Task");

        if (taskNode is null)
        {
            taskNode = new XElement("Task");
            properties.Add(taskNode);
        }

        taskNode.RemoveNodes();
        taskNode.Add(new XCData(taskXml));

        var existingFilters = task.Elements()
            .FirstOrDefault(child => child.Name.LocalName == "Filters");

        existingFilters?.Remove();

        if (!string.IsNullOrWhiteSpace(item.FiltersXml))
        {
            var filters = XElement.Parse(item.FiltersXml);
            if (!filters.Name.LocalName.Equals("Filters", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    "Item-level targeting root element must be <Filters>.");

            task.Add(filters);
        }
    }

    private static string BuildTaskXml(GppScheduledTaskItemInfo item)
    {
        XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";

        var registrationInfo = new XElement(ns + "RegistrationInfo",
            new XElement(ns + "Author", item.Author ?? string.Empty),
            new XElement(ns + "Description", item.Description ?? string.Empty));

        var principal = new XElement(ns + "Principal",
            new XAttribute("id", "Author"));

        if (!string.IsNullOrWhiteSpace(item.RunAs))
            principal.Add(new XElement(ns + "UserId", item.RunAs));

        principal.Add(
            new XElement(ns + "LogonType", FirstNonEmpty(item.LogonType, "InteractiveToken")),
            new XElement(ns + "RunLevel", FirstNonEmpty(item.RunLevel, "LeastPrivilege")));

        var triggers = new XElement(ns + "Triggers");

        if (!item.IsImmediate)
        {
            var trigger = BuildTrigger(item, ns);
            if (trigger is not null)
                triggers.Add(trigger);
        }

        var settings = new XElement(ns + "Settings",
            new XElement(ns + "MultipleInstancesPolicy",
                FirstNonEmpty(item.MultipleInstancesPolicy, "IgnoreNew")),
            new XElement(ns + "DisallowStartIfOnBatteries",
                Bool(item.DisallowStartIfOnBatteries)),
            new XElement(ns + "StopIfGoingOnBatteries",
                Bool(item.StopIfGoingOnBatteries)),
            new XElement(ns + "AllowHardTerminate", "true"),
            new XElement(ns + "StartWhenAvailable", Bool(item.StartWhenAvailable)),
            new XElement(ns + "RunOnlyIfNetworkAvailable", Bool(item.RunOnlyIfNetworkAvailable)),
            new XElement(ns + "IdleSettings",
                new XElement(ns + "StopOnIdleEnd", "true"),
                new XElement(ns + "RestartOnIdle", "false")),
            new XElement(ns + "AllowStartOnDemand", Bool(item.AllowStartOnDemand)),
            new XElement(ns + "Enabled", Bool(item.TaskEnabled)),
            new XElement(ns + "Hidden", Bool(item.Hidden)),
            new XElement(ns + "RunOnlyIfIdle", "false"),
            new XElement(ns + "WakeToRun", Bool(item.WakeToRun)),
            new XElement(ns + "ExecutionTimeLimit",
                FirstNonEmpty(item.ExecutionTimeLimit, "PT0S")),
            new XElement(ns + "Priority",
                FirstNonEmpty(item.Priority, "7")));

        var actions = new XElement(ns + "Actions",
            new XAttribute("Context", "Author"));

        if (!string.IsNullOrWhiteSpace(item.Command))
        {
            var exec = new XElement(ns + "Exec",
                new XElement(ns + "Command", item.Command));

            if (!string.IsNullOrWhiteSpace(item.Arguments))
                exec.Add(new XElement(ns + "Arguments", item.Arguments));

            if (!string.IsNullOrWhiteSpace(item.WorkingDirectory))
                exec.Add(new XElement(ns + "WorkingDirectory", item.WorkingDirectory));

            actions.Add(exec);
        }

        var task = new XElement(ns + "Task",
            new XAttribute("version", "1.3"),
            registrationInfo,
            new XElement(ns + "Principals", principal),
            triggers,
            settings,
            actions);

        return new XDocument(
            new XDeclaration("1.0", "UTF-16", null),
            task)
            .ToString(SaveOptions.DisableFormatting);
    }

    private static XElement? BuildTrigger(
        GppScheduledTaskItemInfo item,
        XNamespace ns)
    {
        var start = string.IsNullOrWhiteSpace(item.StartBoundary)
            ? DateTime.Now.AddMinutes(5)
                .ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture)
            : item.StartBoundary.Trim();

        return item.TriggerType switch
        {
            "Once" => new XElement(ns + "TimeTrigger",
                new XElement(ns + "StartBoundary", start),
                new XElement(ns + "Enabled", "true")),

            "Weekly" => new XElement(ns + "CalendarTrigger",
                new XElement(ns + "StartBoundary", start),
                new XElement(ns + "Enabled", "true"),
                new XElement(ns + "ScheduleByWeek",
                    new XElement(ns + "WeeksInterval",
                        FirstNonEmpty(item.WeeksInterval, "1")),
                    new XElement(ns + "DaysOfWeek",
                        SplitDays(item.DaysOfWeek)
                            .Select(day => new XElement(ns + day))))),

            "AtStartup" => new XElement(ns + "BootTrigger",
                string.IsNullOrWhiteSpace(item.TriggerDelay)
                    ? null
                    : new XElement(ns + "Delay", item.TriggerDelay.Trim()),
                new XElement(ns + "Enabled", "true")),

            "AtLogon" => new XElement(ns + "LogonTrigger",
                string.IsNullOrWhiteSpace(item.TriggerDelay)
                    ? null
                    : new XElement(ns + "Delay", item.TriggerDelay.Trim()),
                new XElement(ns + "Enabled", "true")),

            _ => new XElement(ns + "CalendarTrigger",
                new XElement(ns + "StartBoundary", start),
                new XElement(ns + "Enabled", "true"),
                new XElement(ns + "ScheduleByDay",
                    new XElement(ns + "DaysInterval",
                        FirstNonEmpty(item.DaysInterval, "1"))))
        };
    }

    private static TriggerInfo ReadTrigger(
        XDocument? document,
        string taskKind)
    {
        if (taskKind.Equals("ImmediateTaskV2", StringComparison.OrdinalIgnoreCase) ||
            taskKind.Equals("ImmediateTask", StringComparison.OrdinalIgnoreCase))
        {
            return new TriggerInfo("Immediate", string.Empty, "1", "1", string.Empty, string.Empty);
        }

        var triggers = document?.Descendants()
            .FirstOrDefault(element => element.Name.LocalName == "Triggers");

        var trigger = triggers?.Elements().FirstOrDefault();
        if (trigger is null)
            return new TriggerInfo("Advanced", string.Empty, "1", "1", string.Empty, string.Empty);

        var start = Child(trigger, "StartBoundary");
        var delay = Child(trigger, "Delay");

        switch (trigger.Name.LocalName)
        {
            case "TimeTrigger":
                return new TriggerInfo("Once", start, "1", "1", string.Empty, delay);

            case "BootTrigger":
                return new TriggerInfo("AtStartup", start, "1", "1", string.Empty, delay);

            case "LogonTrigger":
                return new TriggerInfo("AtLogon", start, "1", "1", string.Empty, delay);

            case "CalendarTrigger":
                var byDay = trigger.Descendants()
                    .FirstOrDefault(element => element.Name.LocalName == "ScheduleByDay");

                if (byDay is not null)
                {
                    return new TriggerInfo(
                        "Daily",
                        start,
                        FirstNonEmpty(Child(byDay, "DaysInterval"), "1"),
                        "1",
                        string.Empty,
                        delay);
                }

                var byWeek = trigger.Descendants()
                    .FirstOrDefault(element => element.Name.LocalName == "ScheduleByWeek");

                if (byWeek is not null)
                {
                    var daysNode = byWeek.Descendants()
                        .FirstOrDefault(element => element.Name.LocalName == "DaysOfWeek");

                    var days = daysNode is null
                        ? string.Empty
                        : string.Join(",",
                            daysNode.Elements().Select(element => element.Name.LocalName));

                    return new TriggerInfo(
                        "Weekly",
                        start,
                        "1",
                        FirstNonEmpty(Child(byWeek, "WeeksInterval"), "1"),
                        days,
                        delay);
                }

                break;
        }

        return new TriggerInfo("Advanced", start, "1", "1", string.Empty, delay);
    }

    private static string FindTaskXml(XElement? properties)
    {
        if (properties is null)
            return string.Empty;

        var task = properties.Elements()
            .FirstOrDefault(child => child.Name.LocalName == "Task");

        if (task is null)
            return string.Empty;

        if (!string.IsNullOrWhiteSpace(task.Value))
            return task.Value.Trim();

        var nested = task.Elements().FirstOrDefault();
        return nested?.ToString(SaveOptions.DisableFormatting) ?? string.Empty;
    }

    private static XDocument? ParseTaskXml(string xml)
    {
        if (string.IsNullOrWhiteSpace(xml))
            return null;

        try
        {
            return XDocument.Parse(xml, LoadOptions.PreserveWhitespace);
        }
        catch
        {
            return null;
        }
    }

    private static string ReadTaskElement(
        XDocument? document,
        string parentName,
        string childName)
    {
        var parent = document?.Descendants()
            .FirstOrDefault(element => element.Name.LocalName == parentName);

        return parent?.Descendants()
            .FirstOrDefault(element => element.Name.LocalName == childName)?
            .Value.Trim() ?? string.Empty;
    }

    private static bool ReadTaskBool(
        XDocument? document,
        string parentName,
        string childName,
        bool defaultValue)
    {
        var text = ReadTaskElement(document, parentName, childName);
        return string.IsNullOrWhiteSpace(text)
            ? defaultValue
            : IsTrue(text);
    }

    private static XElement? FindItem(
        XDocument document,
        GppScheduledTaskItemInfo item)
    {
        var candidates = document.Root?.Elements()
            .Where(element => IsTaskElement(element.Name.LocalName))
            .ToArray() ?? Array.Empty<XElement>();

        if (!string.IsNullOrWhiteSpace(item.Uid))
        {
            var byUid = candidates.FirstOrDefault(element =>
                Attr(element, "uid")
                    .Equals(item.Uid, StringComparison.OrdinalIgnoreCase));

            if (byUid is not null)
                return byUid;
        }

        if (item.Ordinal > 0 && item.Ordinal <= candidates.Length)
            return candidates[item.Ordinal - 1];

        return candidates.FirstOrDefault(element =>
            Attr(element, "name")
                .Equals(item.DisplayName, StringComparison.CurrentCultureIgnoreCase));
    }

    private static bool IsTaskElement(string localName) =>
        localName is "Task" or "ImmediateTask" or "TaskV2" or "ImmediateTaskV2";

    private static string ItemClsid(string taskKind) =>
        taskKind.Equals("ImmediateTaskV2", StringComparison.OrdinalIgnoreCase)
            ? ImmediateTaskV2Clsid
            : TaskV2Clsid;

    private GppDocumentTypeInfo GetScheduledTasksType() =>
        _documents.GetKnownTypes()
            .First(type => type.Name.Equals(
                "Scheduled Tasks",
                StringComparison.OrdinalIgnoreCase));

    private static void Validate(GppScheduledTaskItemInfo item)
    {
        if (string.IsNullOrWhiteSpace(item.DisplayName))
            throw new InvalidOperationException("Task name cannot be empty.");

        if (item.IsLegacy)
            throw new InvalidOperationException(
                "Legacy task items are read-only in the structured editor.");

        if (!item.IsImmediate &&
            item.TriggerType is not ("Daily" or "Weekly" or "Once" or "AtStartup" or "AtLogon"))
        {
            throw new InvalidOperationException(
                "This task uses an advanced trigger that the structured editor does not safely rewrite. Use raw XML editing.");
        }

        if (item.Action.ToUpperInvariant() is not ("C" or "U" or "R" or "D"))
            throw new InvalidOperationException("Invalid preference action.");

        if (item.TriggerType is "Daily" &&
            (!uint.TryParse(item.DaysInterval, out var days) || days < 1))
        {
            throw new InvalidOperationException("Days interval must be a positive integer.");
        }

        if (item.TriggerType is "Weekly" &&
            (!uint.TryParse(item.WeeksInterval, out var weeks) || weeks < 1))
        {
            throw new InvalidOperationException("Weeks interval must be a positive integer.");
        }
    }

    private static string NormalizeScope(string value) =>
        value.Equals("User", StringComparison.OrdinalIgnoreCase)
            ? "User"
            : "Computer";

    private static string Attr(XElement? element, string name) =>
        element?.Attributes()
            .FirstOrDefault(attribute =>
                attribute.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase))?
            .Value.Trim() ?? string.Empty;

    private static string Child(XElement element, string localName) =>
        element.Elements()
            .FirstOrDefault(child =>
                child.Name.LocalName.Equals(localName, StringComparison.OrdinalIgnoreCase))?
            .Value.Trim() ?? string.Empty;

    private static string FirstNonEmpty(params string[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;

    private static bool IsTrue(string value) =>
        value.Equals("1", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("yes", StringComparison.OrdinalIgnoreCase);

    private static string Bool(bool value) => value ? "true" : "false";

    private static IEnumerable<string> SplitDays(string value)
    {
        var allowed = new HashSet<string>(
            new[]
            {
                "Monday", "Tuesday", "Wednesday", "Thursday",
                "Friday", "Saturday", "Sunday"
            },
            StringComparer.OrdinalIgnoreCase);

        return value
            .Split(new[] { ',', ';', ' ' },
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(allowed.Contains)
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static void SetAttr(XElement element, string name, string value) =>
        element.SetAttributeValue(name, value);

    private static void SetOptionalAttr(
        XElement element,
        string name,
        string value)
    {
        element.SetAttributeValue(
            name,
            string.IsNullOrWhiteSpace(value) ? null : value.Trim());
    }

    private static void SetOptionalBool(
        XElement element,
        string name,
        bool value)
    {
        element.SetAttributeValue(name, value ? "1" : null);
    }

    private sealed record TriggerInfo(
        string Type,
        string StartBoundary,
        string DaysInterval,
        string WeeksInterval,
        string DaysOfWeek,
        string Delay);
}

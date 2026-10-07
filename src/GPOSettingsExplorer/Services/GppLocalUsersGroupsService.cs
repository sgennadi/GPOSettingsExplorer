using System.Globalization;
using System.Xml.Linq;
using GPOSettingsExplorer.Models;

namespace GPOSettingsExplorer.Services;

public sealed class GppLocalUsersGroupsService
{
    private const string RootClsid =
        "{3125E937-EB16-4B4C-9934-544FC6D24D26}";

    private const string UserClsid =
        "{DF5F1855-51E5-4D24-8B1A-D9BDE98BA1D1}";

    private const string GroupClsid =
        "{6D4A79E4-529C-4481-ABD0-F5BD7EA93BA7}";

    private readonly GppDocumentService _documents;

    public GppLocalUsersGroupsService(
        GppDocumentService documents)
    {
        _documents = documents;
    }

    public IReadOnlyList<GppLocalUserInfo> LoadUsers(
        IEnumerable<GpoInfo> gpos,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var result =
            new List<GppLocalUserInfo>();

        var list =
            gpos.ToList();

        var type =
            GetTypeInfo();

        for (var index = 0;
             index < list.Count;
             index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var gpo =
                list[index];

            progress?.Report(
                $"Local Users {index + 1}/{list.Count}: {gpo.DisplayName}");

            foreach (var scope
                     in new[]
                     {
                         "Computer",
                         "User"
                     })
            {
                var target =
                    _documents.BuildTarget(
                        gpo,
                        scope,
                        type);

                if (!File.Exists(
                        target.XmlPath))
                    continue;

                try
                {
                    var document =
                        GppXmlCacheService.Load(
                            target.XmlPath,
                            LoadOptions.PreserveWhitespace);

                    var ordinal =
                        0;

                    foreach (var user
                             in document
                                 .Descendants()
                                 .Where(
                                     element =>
                                         element.Name.LocalName ==
                                         "User"))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        ordinal++;

                        var properties =
                            user.Elements()
                                .FirstOrDefault(
                                    element =>
                                        element.Name.LocalName ==
                                        "Properties");

                        if (properties is null)
                            continue;

                        var filters =
                            user.Elements()
                                .FirstOrDefault(
                                    element =>
                                        element.Name.LocalName ==
                                        "Filters");

                        var userName =
                            Attr(
                                properties,
                                "userName");

                        result.Add(
                            new GppLocalUserInfo
                            {
                                GpoId =
                                    gpo.Id,
                                GpoName =
                                    gpo.DisplayName,
                                DomainName =
                                    gpo.DomainName,
                                Scope =
                                    scope,
                                XmlPath =
                                    target.XmlPath,
                                Uid =
                                    Attr(
                                        user,
                                        "uid"),
                                Ordinal =
                                    ordinal,
                                DisplayName =
                                    FirstNonEmpty(
                                        Attr(
                                            user,
                                            "name"),
                                        userName),
                                Description =
                                    Attr(
                                        user,
                                        "desc"),
                                Action =
                                    FirstNonEmpty(
                                        Attr(
                                            properties,
                                            "action"),
                                        "U"),
                                UserName =
                                    userName,
                                NewName =
                                    Attr(
                                        properties,
                                        "newName"),
                                FullName =
                                    Attr(
                                        properties,
                                        "fullName"),
                                AccountDescription =
                                    Attr(
                                        properties,
                                        "description"),
                                ChangePasswordAtLogon =
                                    IsTrue(
                                        Attr(
                                            properties,
                                            "changeLogon")),
                                UserCannotChangePassword =
                                    IsTrue(
                                        Attr(
                                            properties,
                                            "noChange")),
                                PasswordNeverExpires =
                                    IsTrue(
                                        Attr(
                                            properties,
                                            "neverExpires")),
                                AccountDisabled =
                                    IsTrue(
                                        Attr(
                                            properties,
                                            "acctDisabled")),
                                Expires =
                                    Attr(
                                        properties,
                                        "expires"),
                                OpaqueCredential =
                                    Attr(
                                        properties,
                                        "cpassword"),
                                Disabled =
                                    IsTrue(
                                        Attr(
                                            user,
                                            "disabled")),
                                BypassErrors =
                                    IsTrue(
                                        Attr(
                                            user,
                                            "bypassErrors")),
                                RemoveWhenNoLongerApplied =
                                    IsTrue(
                                        Attr(
                                            user,
                                            "removePolicy")),
                                RunInUserContext =
                                    IsTrue(
                                        Attr(
                                            user,
                                            "userContext")),
                                FiltersXml =
                                    filters?.ToString(
                                        SaveOptions.DisableFormatting)
                                    ?? string.Empty
                            });
                    }
                }
                catch
                {
                    // The raw GPP XML editor remains available for malformed documents.
                }
            }
        }

        return result
            .OrderBy(
                item =>
                    item.GpoName,
                StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(
                item =>
                    item.Scope,
                StringComparer.OrdinalIgnoreCase)
            .ThenBy(
                item =>
                    item.UserName,
                StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(
                item =>
                    item.Ordinal)
            .ToArray();
    }

    public IReadOnlyList<GppLocalGroupInfo> LoadGroups(
        IEnumerable<GpoInfo> gpos,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var result =
            new List<GppLocalGroupInfo>();

        var list =
            gpos.ToList();

        var type =
            GetTypeInfo();

        for (var index = 0;
             index < list.Count;
             index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var gpo =
                list[index];

            progress?.Report(
                $"Local Groups {index + 1}/{list.Count}: {gpo.DisplayName}");

            foreach (var scope
                     in new[]
                     {
                         "Computer",
                         "User"
                     })
            {
                var target =
                    _documents.BuildTarget(
                        gpo,
                        scope,
                        type);

                if (!File.Exists(
                        target.XmlPath))
                    continue;

                try
                {
                    var document =
                        GppXmlCacheService.Load(
                            target.XmlPath,
                            LoadOptions.PreserveWhitespace);

                    var ordinal =
                        0;

                    foreach (var group
                             in document
                                 .Descendants()
                                 .Where(
                                     element =>
                                         element.Name.LocalName ==
                                         "Group"))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        ordinal++;

                        var properties =
                            group.Elements()
                                .FirstOrDefault(
                                    element =>
                                        element.Name.LocalName ==
                                        "Properties");

                        if (properties is null)
                            continue;

                        var filters =
                            group.Elements()
                                .FirstOrDefault(
                                    element =>
                                        element.Name.LocalName ==
                                        "Filters");

                        var members =
                            properties.Elements()
                                .FirstOrDefault(
                                    element =>
                                        element.Name.LocalName ==
                                        "Members")?
                                .Elements()
                                .Where(
                                    element =>
                                        element.Name.LocalName ==
                                        "Member")
                                .Select(
                                    member =>
                                        new GppLocalGroupMemberInfo
                                        {
                                            Name =
                                                Attr(
                                                    member,
                                                    "name"),
                                            Action =
                                                NormalizeMemberAction(
                                                    Attr(
                                                        member,
                                                        "action")),
                                            Sid =
                                                Attr(
                                                    member,
                                                    "sid")
                                        })
                                .ToList()
                            ?? new List<GppLocalGroupMemberInfo>();

                        var groupName =
                            Attr(
                                properties,
                                "groupName");

                        result.Add(
                            new GppLocalGroupInfo
                            {
                                GpoId =
                                    gpo.Id,
                                GpoName =
                                    gpo.DisplayName,
                                DomainName =
                                    gpo.DomainName,
                                Scope =
                                    scope,
                                XmlPath =
                                    target.XmlPath,
                                Uid =
                                    Attr(
                                        group,
                                        "uid"),
                                Ordinal =
                                    ordinal,
                                DisplayName =
                                    FirstNonEmpty(
                                        Attr(
                                            group,
                                            "name"),
                                        groupName),
                                Description =
                                    Attr(
                                        group,
                                        "desc"),
                                Action =
                                    FirstNonEmpty(
                                        Attr(
                                            properties,
                                            "action"),
                                        "U"),
                                GroupName =
                                    groupName,
                                GroupSid =
                                    Attr(
                                        properties,
                                        "groupSid"),
                                NewName =
                                    Attr(
                                        properties,
                                        "newName"),
                                GroupDescription =
                                    Attr(
                                        properties,
                                        "description"),
                                CurrentUserAction =
                                    NormalizeCurrentUserAction(
                                        Attr(
                                            properties,
                                            "userAction")),
                                DeleteAllUsers =
                                    IsTrue(
                                        Attr(
                                            properties,
                                            "deleteAllUsers")),
                                DeleteAllGroups =
                                    IsTrue(
                                        Attr(
                                            properties,
                                            "deleteAllGroups")),
                                RemoveAccounts =
                                    IsTrue(
                                        Attr(
                                            properties,
                                            "removeAccounts")),
                                PropertiesDisabled =
                                    IsTrue(
                                        Attr(
                                            properties,
                                            "disabled")),
                                Members =
                                    members,
                                Disabled =
                                    IsTrue(
                                        Attr(
                                            group,
                                            "disabled")),
                                BypassErrors =
                                    IsTrue(
                                        Attr(
                                            group,
                                            "bypassErrors")),
                                RemoveWhenNoLongerApplied =
                                    IsTrue(
                                        Attr(
                                            group,
                                            "removePolicy")),
                                RunInUserContext =
                                    IsTrue(
                                        Attr(
                                            group,
                                            "userContext")),
                                FiltersXml =
                                    filters?.ToString(
                                        SaveOptions.DisableFormatting)
                                    ?? string.Empty
                            });
                    }
                }
                catch
                {
                    // The raw GPP XML editor remains available for malformed documents.
                }
            }
        }

        return result
            .OrderBy(
                item =>
                    item.GpoName,
                StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(
                item =>
                    item.Scope,
                StringComparer.OrdinalIgnoreCase)
            .ThenBy(
                item =>
                    item.GroupName,
                StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(
                item =>
                    item.Ordinal)
            .ToArray();
    }

    public GppLocalUserInfo CreateNewUser(
        GpoInfo gpo,
        string scope)
    {
        var normalizedScope =
            NormalizeScope(
                scope);

        return new GppLocalUserInfo
        {
            GpoId =
                gpo.Id,
            GpoName =
                gpo.DisplayName,
            DomainName =
                gpo.DomainName,
            Scope =
                normalizedScope,
            XmlPath =
                _documents.BuildTarget(
                    gpo,
                    normalizedScope,
                    GetTypeInfo()).XmlPath,
            Uid =
                Guid.NewGuid()
                    .ToString("B")
                    .ToUpperInvariant(),
            DisplayName =
                "New Local User",
            Action =
                "U"
        };
    }

    public GppLocalGroupInfo CreateNewGroup(
        GpoInfo gpo,
        string scope)
    {
        var normalizedScope =
            NormalizeScope(
                scope);

        return new GppLocalGroupInfo
        {
            GpoId =
                gpo.Id,
            GpoName =
                gpo.DisplayName,
            DomainName =
                gpo.DomainName,
            Scope =
                normalizedScope,
            XmlPath =
                _documents.BuildTarget(
                    gpo,
                    normalizedScope,
                    GetTypeInfo()).XmlPath,
            Uid =
                Guid.NewGuid()
                    .ToString("B")
                    .ToUpperInvariant(),
            DisplayName =
                "New Local Group",
            Action =
                "U",
            CurrentUserAction =
                string.Empty
        };
    }

    public void SaveUser(
        GpoInfo gpo,
        string domainDistinguishedName,
        GppLocalUserInfo item)
    {
        ValidateUser(
            item);

        var target =
            _documents.BuildTarget(
                gpo,
                item.Scope,
                GetTypeInfo());

        var document =
            LoadOrCreate(
                target.XmlPath);

        var users =
            document
                .Descendants()
                .Where(
                    element =>
                        element.Name.LocalName ==
                        "User")
                .ToArray();

        var user =
            FindItem(
                users,
                item.Uid,
                item.Ordinal);

        if (user is null)
        {
            user =
                new XElement(
                    "User",
                    new XAttribute(
                        "clsid",
                        UserClsid));

            document.Root!.Add(
                user);
        }

        UpdateUser(
            user,
            item);

        _documents.SaveXml(
            gpo,
            domainDistinguishedName,
            target,
            document.ToString());
    }

    public void SaveGroup(
        GpoInfo gpo,
        string domainDistinguishedName,
        GppLocalGroupInfo item)
    {
        ValidateGroup(
            item);

        var target =
            _documents.BuildTarget(
                gpo,
                item.Scope,
                GetTypeInfo());

        var document =
            LoadOrCreate(
                target.XmlPath);

        var groups =
            document
                .Descendants()
                .Where(
                    element =>
                        element.Name.LocalName ==
                        "Group")
                .ToArray();

        var group =
            FindItem(
                groups,
                item.Uid,
                item.Ordinal);

        if (group is null)
        {
            group =
                new XElement(
                    "Group",
                    new XAttribute(
                        "clsid",
                        GroupClsid));

            document.Root!.Add(
                group);
        }

        UpdateGroup(
            group,
            item);

        _documents.SaveXml(
            gpo,
            domainDistinguishedName,
            target,
            document.ToString());
    }

    public void DeleteUser(
        GpoInfo gpo,
        string domainDistinguishedName,
        GppLocalUserInfo item)
    {
        DeleteItem(
            gpo,
            domainDistinguishedName,
            item.Scope,
            "User",
            item.Uid,
            item.Ordinal);
    }

    public void DeleteGroup(
        GpoInfo gpo,
        string domainDistinguishedName,
        GppLocalGroupInfo item)
    {
        DeleteItem(
            gpo,
            domainDistinguishedName,
            item.Scope,
            "Group",
            item.Uid,
            item.Ordinal);
    }

    public void CopyUserValues(
        GppLocalUserInfo source,
        GppLocalUserInfo target,
        bool preserveCredential)
    {
        target.DisplayName =
            source.DisplayName;
        target.Description =
            source.Description;
        target.Action =
            source.Action;
        target.UserName =
            source.UserName;
        target.NewName =
            source.NewName;
        target.FullName =
            source.FullName;
        target.AccountDescription =
            source.AccountDescription;
        target.ChangePasswordAtLogon =
            source.ChangePasswordAtLogon;
        target.UserCannotChangePassword =
            source.UserCannotChangePassword;
        target.PasswordNeverExpires =
            source.PasswordNeverExpires;
        target.AccountDisabled =
            source.AccountDisabled;
        target.Expires =
            source.Expires;
        target.OpaqueCredential =
            preserveCredential
                ? source.OpaqueCredential
                : string.Empty;
        target.ClearStoredCredential =
            false;
        target.Disabled =
            source.Disabled;
        target.BypassErrors =
            source.BypassErrors;
        target.RemoveWhenNoLongerApplied =
            source.RemoveWhenNoLongerApplied;
        target.RunInUserContext =
            source.RunInUserContext;
        target.FiltersXml =
            source.FiltersXml;
    }

    public void CopyGroupValues(
        GppLocalGroupInfo source,
        GppLocalGroupInfo target)
    {
        target.DisplayName =
            source.DisplayName;
        target.Description =
            source.Description;
        target.Action =
            source.Action;
        target.GroupName =
            source.GroupName;
        target.GroupSid =
            source.GroupSid;
        target.NewName =
            source.NewName;
        target.GroupDescription =
            source.GroupDescription;
        target.CurrentUserAction =
            source.CurrentUserAction;
        target.DeleteAllUsers =
            source.DeleteAllUsers;
        target.DeleteAllGroups =
            source.DeleteAllGroups;
        target.RemoveAccounts =
            source.RemoveAccounts;
        target.PropertiesDisabled =
            source.PropertiesDisabled;
        target.Members =
            source.Members
                .Select(
                    member =>
                        member.Clone())
                .ToList();
        target.Disabled =
            source.Disabled;
        target.BypassErrors =
            source.BypassErrors;
        target.RemoveWhenNoLongerApplied =
            source.RemoveWhenNoLongerApplied;
        target.RunInUserContext =
            source.RunInUserContext;
        target.FiltersXml =
            source.FiltersXml;
    }

    private void DeleteItem(
        GpoInfo gpo,
        string domainDistinguishedName,
        string scope,
        string itemName,
        string uid,
        int ordinal)
    {
        var target =
            _documents.BuildTarget(
                gpo,
                scope,
                GetTypeInfo());

        if (!File.Exists(
                target.XmlPath))
            return;

        var document =
            GppXmlCacheService.Load(
                target.XmlPath,
                LoadOptions.PreserveWhitespace);

        var items =
            document
                .Descendants()
                .Where(
                    element =>
                        element.Name.LocalName ==
                        itemName)
                .ToArray();

        var selected =
            FindItem(
                items,
                uid,
                ordinal)
            ?? throw new InvalidOperationException(
                $"The selected Local {itemName} preference no longer exists. Refresh the list.");

        selected.Remove();

        if (!document
                .Descendants()
                .Any(
                    element =>
                        element.Name.LocalName is
                            "User" or "Group"))
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

    private static XDocument LoadOrCreate(
        string path)
    {
        if (File.Exists(
                path))
        {
            var document =
                GppXmlCacheService.Load(
                    path,
                    LoadOptions.PreserveWhitespace);

            if (document.Root is null ||
                !document.Root.Name.LocalName.Equals(
                    "Groups",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "The existing Groups.xml root element is not <Groups>. " +
                    "Use the raw GPP XML editor to repair it.");
            }

            return document;
        }

        return new XDocument(
            new XDeclaration(
                "1.0",
                "utf-8",
                null),
            new XElement(
                "Groups",
                new XAttribute(
                    "clsid",
                    RootClsid)));
    }

    private static void UpdateUser(
        XElement user,
        GppLocalUserInfo item)
    {
        var display =
            string.IsNullOrWhiteSpace(
                item.DisplayName)
                ? item.UserName.Trim()
                : item.DisplayName.Trim();

        SetCommonItemAttributes(
            user,
            UserClsid,
            display,
            item.Description,
            item.Uid,
            item.Disabled,
            item.BypassErrors,
            item.RemoveWhenNoLongerApplied,
            item.RunInUserContext,
            out var uid);

        item.Uid =
            uid;

        var properties =
            EnsureProperties(
                user);

        SetAttr(
            properties,
            "action",
            NormalizeAction(
                item.Action));

        SetAttr(
            properties,
            "newName",
            item.NewName.Trim());

        SetAttr(
            properties,
            "fullName",
            item.FullName.Trim());

        SetAttr(
            properties,
            "description",
            item.AccountDescription);

        SetAttr(
            properties,
            "changeLogon",
            Bool(
                item.ChangePasswordAtLogon));

        SetAttr(
            properties,
            "noChange",
            Bool(
                item.UserCannotChangePassword));

        SetAttr(
            properties,
            "neverExpires",
            Bool(
                item.PasswordNeverExpires));

        SetAttr(
            properties,
            "acctDisabled",
            Bool(
                item.AccountDisabled));

        SetAttr(
            properties,
            "userName",
            item.UserName.Trim());

        if (string.IsNullOrWhiteSpace(
                item.Expires))
        {
            properties.SetAttributeValue(
                "expires",
                null);
        }
        else
        {
            SetAttr(
                properties,
                "expires",
                item.Expires.Trim());
        }

        if (item.ClearStoredCredential)
        {
            properties.SetAttributeValue(
                "cpassword",
                null);
        }
        else if (!string.IsNullOrWhiteSpace(
                     item.OpaqueCredential))
        {
            SetAttr(
                properties,
                "cpassword",
                item.OpaqueCredential);
        }
        else
        {
            properties.SetAttributeValue(
                "cpassword",
                null);
        }

        ApplyFilters(
            user,
            item.FiltersXml);
    }

    private static void UpdateGroup(
        XElement group,
        GppLocalGroupInfo item)
    {
        var display =
            string.IsNullOrWhiteSpace(
                item.DisplayName)
                ? item.GroupName.Trim()
                : item.DisplayName.Trim();

        SetCommonItemAttributes(
            group,
            GroupClsid,
            display,
            item.Description,
            item.Uid,
            item.Disabled,
            item.BypassErrors,
            item.RemoveWhenNoLongerApplied,
            item.RunInUserContext,
            out var uid);

        item.Uid =
            uid;

        var properties =
            EnsureProperties(
                group);

        SetAttr(
            properties,
            "action",
            NormalizeAction(
                item.Action));

        SetAttr(
            properties,
            "newName",
            item.NewName.Trim());

        SetAttr(
            properties,
            "description",
            item.GroupDescription);

        SetAttr(
            properties,
            "userAction",
            NormalizeCurrentUserAction(
                item.CurrentUserAction));

        SetAttr(
            properties,
            "deleteAllUsers",
            Bool(
                item.DeleteAllUsers));

        SetAttr(
            properties,
            "deleteAllGroups",
            Bool(
                item.DeleteAllGroups));

        SetAttr(
            properties,
            "removeAccounts",
            Bool(
                item.RemoveAccounts));

        SetAttr(
            properties,
            "groupName",
            item.GroupName.Trim());

        if (string.IsNullOrWhiteSpace(
                item.GroupSid))
        {
            properties.SetAttributeValue(
                "groupSid",
                null);
        }
        else
        {
            SetAttr(
                properties,
                "groupSid",
                item.GroupSid.Trim());
        }

        if (item.PropertiesDisabled)
        {
            SetAttr(
                properties,
                "disabled",
                "1");
        }
        else
        {
            properties.SetAttributeValue(
                "disabled",
                null);
        }

        var members =
            properties.Elements()
                .FirstOrDefault(
                    element =>
                        element.Name.LocalName ==
                        "Members");

        members?.Remove();

        var membersElement =
            new XElement(
                "Members");

        foreach (var member
                 in item.Members
                     .Where(
                         member =>
                             !string.IsNullOrWhiteSpace(
                                 member.Name) ||
                             !string.IsNullOrWhiteSpace(
                                 member.Sid)))
        {
            membersElement.Add(
                new XElement(
                    "Member",
                    new XAttribute(
                        "name",
                        member.Name.Trim()),
                    new XAttribute(
                        "action",
                        NormalizeMemberAction(
                            member.Action)),
                    new XAttribute(
                        "sid",
                        member.Sid.Trim())));
        }

        properties.Add(
            membersElement);

        ApplyFilters(
            group,
            item.FiltersXml);
    }

    private static void SetCommonItemAttributes(
        XElement element,
        string clsid,
        string displayName,
        string description,
        string uid,
        bool disabled,
        bool bypassErrors,
        bool removePolicy,
        bool userContext,
        out string normalizedUid)
    {
        SetAttr(
            element,
            "clsid",
            clsid);

        SetAttr(
            element,
            "name",
            displayName);

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

        normalizedUid =
            string.IsNullOrWhiteSpace(
                uid)
                ? Guid.NewGuid()
                    .ToString("B")
                    .ToUpperInvariant()
                : uid;

        SetAttr(
            element,
            "uid",
            normalizedUid);

        if (string.IsNullOrWhiteSpace(
                description))
        {
            element.SetAttributeValue(
                "desc",
                null);
        }
        else
        {
            SetAttr(
                element,
                "desc",
                description);
        }

        SetOptionalBool(
            element,
            "disabled",
            disabled);

        SetOptionalBool(
            element,
            "bypassErrors",
            bypassErrors);

        SetOptionalBool(
            element,
            "removePolicy",
            removePolicy);

        SetOptionalBool(
            element,
            "userContext",
            userContext);
    }

    private static XElement EnsureProperties(
        XElement element)
    {
        var properties =
            element.Elements()
                .FirstOrDefault(
                    child =>
                        child.Name.LocalName ==
                        "Properties");

        if (properties is not null)
            return properties;

        properties =
            new XElement(
                "Properties");

        element.AddFirst(
            properties);

        return properties;
    }

    private static void ApplyFilters(
        XElement item,
        string filtersXml)
    {
        var existing =
            item.Elements()
                .FirstOrDefault(
                    element =>
                        element.Name.LocalName ==
                        "Filters");

        if (string.IsNullOrWhiteSpace(
                filtersXml))
        {
            existing?.Remove();
            return;
        }

        XElement filters;

        try
        {
            filters =
                XElement.Parse(
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
            item.Add(
                filters);
        else
            existing.ReplaceWith(
                filters);
    }

    private static XElement? FindItem(
        IReadOnlyList<XElement> items,
        string uid,
        int ordinal)
    {
        if (!string.IsNullOrWhiteSpace(
                uid))
        {
            var byUid =
                items.FirstOrDefault(
                    element =>
                        Attr(
                            element,
                            "uid").Equals(
                            uid,
                            StringComparison.OrdinalIgnoreCase));

            if (byUid is not null)
                return byUid;
        }

        if (ordinal > 0 &&
            ordinal <= items.Count)
        {
            return items[
                ordinal - 1];
        }

        return null;
    }

    private static void ValidateUser(
        GppLocalUserInfo item)
    {
        if (string.IsNullOrWhiteSpace(
                item.UserName))
        {
            throw new InvalidOperationException(
                "Local user name cannot be empty.");
        }

        if (item.UserName.Contains(
                '\\'))
        {
            throw new InvalidOperationException(
                "Local user name must not contain a domain prefix.");
        }

        if (!string.IsNullOrWhiteSpace(
                item.FiltersXml))
        {
            ValidateFilters(
                item.FiltersXml);
        }
    }

    private static void ValidateGroup(
        GppLocalGroupInfo item)
    {
        if (string.IsNullOrWhiteSpace(
                item.GroupName))
        {
            throw new InvalidOperationException(
                "Local group name cannot be empty.");
        }

        foreach (var member
                 in item.Members)
        {
            if (string.IsNullOrWhiteSpace(
                    member.Name) &&
                string.IsNullOrWhiteSpace(
                    member.Sid))
            {
                throw new InvalidOperationException(
                    "Every group member row must contain a name or SID.");
            }

            _ =
                NormalizeMemberAction(
                    member.Action);
        }

        if (!string.IsNullOrWhiteSpace(
                item.FiltersXml))
        {
            ValidateFilters(
                item.FiltersXml);
        }
    }

    private static void ValidateFilters(
        string filtersXml)
    {
        var filters =
            XElement.Parse(
                filtersXml);

        if (!filters.Name.LocalName.Equals(
                "Filters",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Item-level targeting XML must have a <Filters> root element.");
        }
    }

    private GppDocumentTypeInfo GetTypeInfo() =>
        _documents.GetKnownTypes().First(
            type =>
                type.Name.Equals(
                    "Local Users and Groups",
                    StringComparison.OrdinalIgnoreCase));

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

    private static string NormalizeMemberAction(
        string action) =>
        action.Trim()
            .ToUpperInvariant() switch
        {
            "REMOVE" or "DELETE" => "REMOVE",
            _ => "ADD"
        };

    private static string NormalizeCurrentUserAction(
        string action) =>
        action.Trim()
            .ToUpperInvariant() switch
        {
            "ADD" => "ADD",
            "REMOVE" => "REMOVE",
            _ => string.Empty
        };

    private static string Attr(
        XElement element,
        string name) =>
        element.Attributes()
            .FirstOrDefault(
                attribute =>
                    attribute.Name.LocalName.Equals(
                        name,
                        StringComparison.OrdinalIgnoreCase))?
            .Value
        ?? string.Empty;

    private static bool IsTrue(
        string value) =>
        value == "1" ||
        value.Equals(
            "true",
            StringComparison.OrdinalIgnoreCase);

    private static string Bool(
        bool value) =>
        value
            ? "1"
            : "0";

    private static string FirstNonEmpty(
        params string[] values) =>
        values.FirstOrDefault(
            value =>
                !string.IsNullOrWhiteSpace(
                    value))
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
        {
            element.SetAttributeValue(
                name,
                "1");
        }
        else
        {
            element.SetAttributeValue(
                name,
                null);
        }
    }
}

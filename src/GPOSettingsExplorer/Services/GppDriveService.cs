using System.Globalization;
using System.Xml.Linq;
using GPOSettingsExplorer.Models;

namespace GPOSettingsExplorer.Services;

public sealed class GppDriveService
{
    private const string RootClsid = "{8FDDCC1A-0C3C-43CD-A6B4-71A6DF20DA8C}";
    private const string ItemClsid = "{935D1B74-9CB8-4E3C-9914-7DD559B7A417}";

    private readonly GppDocumentService _documents;

    public GppDriveService(GppDocumentService documents)
    {
        _documents = documents;
    }

    public IReadOnlyList<GppDriveItemInfo> Load(
        IEnumerable<GpoInfo> gpos,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var result = new List<GppDriveItemInfo>();
        var list = gpos.ToList();
        var type = GetDriveType();

        for (var index = 0; index < list.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var gpo = list[index];
            progress?.Report($"Drive Maps {index + 1}/{list.Count}: {gpo.DisplayName}");

            var target = _documents.BuildTarget(gpo, "User", type);
            if (!File.Exists(target.XmlPath))
                continue;

            try
            {
                var document = XDocument.Load(
                    target.XmlPath,
                    LoadOptions.PreserveWhitespace);

                var ordinal = 0;

                foreach (var drive in document
                             .Descendants()
                             .Where(element => element.Name.LocalName == "Drive"))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    ordinal++;

                    var properties = drive.Elements()
                        .FirstOrDefault(element => element.Name.LocalName == "Properties");

                    if (properties is null)
                        continue;

                    var filters = drive.Elements()
                        .FirstOrDefault(element => element.Name.LocalName == "Filters");

                    var letter = Attr(properties, "letter").Trim().TrimEnd(':');
                    if (string.IsNullOrWhiteSpace(letter))
                        letter = "S";

                    result.Add(new GppDriveItemInfo
                    {
                        GpoId = gpo.Id,
                        GpoName = gpo.DisplayName,
                        DomainName = gpo.DomainName,
                        XmlPath = target.XmlPath,
                        Uid = Attr(drive, "uid"),
                        Ordinal = ordinal,
                        DisplayName = FirstNonEmpty(
                            Attr(drive, "name"),
                            Attr(drive, "status"),
                            letter + ":"),
                        Action = FirstNonEmpty(Attr(properties, "action"), "U"),
                        Letter = letter[..1].ToUpperInvariant(),
                        UseExactLetter = !Attr(properties, "useLetter").Equals("0", StringComparison.OrdinalIgnoreCase),
                        Path = Attr(properties, "path"),
                        Label = Attr(properties, "label"),
                        Persistent = IsTrue(Attr(properties, "persistent")),
                        UserName = Attr(properties, "userName"),
                        ThisDriveVisibility = NormalizeVisibility(Attr(properties, "thisDrive")),
                        AllDrivesVisibility = NormalizeVisibility(Attr(properties, "allDrives")),
                        Disabled = IsTrue(Attr(drive, "disabled")),
                        BypassErrors = IsTrue(Attr(drive, "bypassErrors")),
                        RemoveWhenNoLongerApplied = IsTrue(Attr(drive, "removePolicy")),
                        RunInUserContext = IsTrue(Attr(drive, "userContext")),
                        FiltersXml = filters?.ToString(SaveOptions.DisableFormatting) ?? string.Empty,
                        OpaqueCredential = Attr(properties, "cpassword")
                    });
                }
            }
            catch
            {
                // The raw GPP XML tab can be used to repair malformed documents.
            }
        }

        return result
            .OrderBy(item => item.GpoName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(item => item.Letter, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Ordinal)
            .ToArray();
    }

    public GppDriveItemInfo CreateNew(GpoInfo gpo) =>
        new()
        {
            GpoId = gpo.Id,
            GpoName = gpo.DisplayName,
            DomainName = gpo.DomainName,
            XmlPath = _documents.BuildTarget(gpo, "User", GetDriveType()).XmlPath,
            Uid = Guid.NewGuid().ToString("B").ToUpperInvariant(),
            DisplayName = "S:",
            Action = "U",
            Letter = "S",
            UseExactLetter = true,
            ThisDriveVisibility = "NOCHANGE",
            AllDrivesVisibility = "NOCHANGE"
        };

    public void Save(
        GpoInfo gpo,
        string domainDistinguishedName,
        GppDriveItemInfo item)
    {
        Validate(item);

        var type = GetDriveType();
        var target = _documents.BuildTarget(gpo, "User", type);

        XDocument document;

        if (File.Exists(target.XmlPath))
        {
            document = XDocument.Load(
                target.XmlPath,
                LoadOptions.PreserveWhitespace);

            if (document.Root is null ||
                !document.Root.Name.LocalName.Equals("Drives", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "The existing Drives.xml root element is not <Drives>. Use the raw GPP XML editor to repair it.");
            }
        }
        else
        {
            document = new XDocument(
                new XDeclaration("1.0", "utf-8", null),
                new XElement("Drives",
                    new XAttribute("clsid", RootClsid)));
        }

        var drives = document
            .Descendants()
            .Where(element => element.Name.LocalName == "Drive")
            .ToArray();

        var drive = FindDrive(drives, item);

        if (drive is null)
        {
            drive = new XElement(
                "Drive",
                new XAttribute("clsid", ItemClsid));

            document.Root!.Add(drive);
        }

        UpdateDrive(drive, item);

        _documents.SaveXml(
            gpo,
            domainDistinguishedName,
            target,
            document.ToString());
    }

    public void Delete(
        GpoInfo gpo,
        string domainDistinguishedName,
        GppDriveItemInfo item)
    {
        var type = GetDriveType();
        var target = _documents.BuildTarget(gpo, "User", type);

        if (!File.Exists(target.XmlPath))
            return;

        var document = XDocument.Load(
            target.XmlPath,
            LoadOptions.PreserveWhitespace);

        var drives = document
            .Descendants()
            .Where(element => element.Name.LocalName == "Drive")
            .ToArray();

        var drive = FindDrive(drives, item)
            ?? throw new InvalidOperationException(
                "The selected Drive Maps preference no longer exists. Refresh the list.");

        drive.Remove();

        if (!document.Descendants().Any(element => element.Name.LocalName == "Drive"))
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

    private static void UpdateDrive(
        XElement drive,
        GppDriveItemInfo item)
    {
        var letter = item.Letter.Trim().TrimEnd(':').ToUpperInvariant();
        var display = string.IsNullOrWhiteSpace(item.DisplayName)
            ? letter + ":"
            : item.DisplayName.Trim();

        SetAttr(drive, "clsid", ItemClsid);
        SetAttr(drive, "name", display);
        SetAttr(drive, "status", display);
        SetAttr(drive, "image", "2");
        SetAttr(
            drive,
            "changed",
            DateTime.UtcNow.ToString(
                "yyyy-MM-dd HH:mm:ss",
                CultureInfo.InvariantCulture));

        if (string.IsNullOrWhiteSpace(item.Uid))
            item.Uid = Guid.NewGuid().ToString("B").ToUpperInvariant();

        SetAttr(drive, "uid", item.Uid);

        SetOptionalBool(drive, "disabled", item.Disabled);
        SetOptionalBool(drive, "bypassErrors", item.BypassErrors);
        SetOptionalBool(drive, "removePolicy", item.RemoveWhenNoLongerApplied);
        SetOptionalBool(drive, "userContext", item.RunInUserContext);

        var properties = drive.Elements()
            .FirstOrDefault(element => element.Name.LocalName == "Properties");

        if (properties is null)
        {
            properties = new XElement("Properties");
            drive.AddFirst(properties);
        }

        SetAttr(properties, "action", NormalizeAction(item.Action));
        SetAttr(properties, "thisDrive", NormalizeVisibility(item.ThisDriveVisibility));
        SetAttr(properties, "allDrives", NormalizeVisibility(item.AllDrivesVisibility));
        SetAttr(properties, "userName", item.UserName.Trim());
        SetAttr(properties, "path", item.Path.Trim());
        SetAttr(properties, "label", item.Label.Trim());
        SetAttr(properties, "persistent", item.Persistent ? "1" : "0");
        SetAttr(properties, "useLetter", item.UseExactLetter ? "1" : "0");
        SetAttr(properties, "letter", letter);

        if (item.ClearStoredCredential)
            properties.SetAttributeValue("cpassword", null);
        else if (!string.IsNullOrWhiteSpace(item.OpaqueCredential))
            SetAttr(properties, "cpassword", item.OpaqueCredential);

        var existingFilters = drive.Elements()
            .FirstOrDefault(element => element.Name.LocalName == "Filters");

        if (string.IsNullOrWhiteSpace(item.FiltersXml))
        {
            existingFilters?.Remove();
        }
        else
        {
            XElement filters;

            try
            {
                filters = XElement.Parse(
                    item.FiltersXml,
                    LoadOptions.PreserveWhitespace);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Item-level targeting XML is invalid: {ex.Message}",
                    ex);
            }

            if (!filters.Name.LocalName.Equals("Filters", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "Item-level targeting XML must have a <Filters> root element.");
            }

            if (existingFilters is null)
                drive.Add(filters);
            else
                existingFilters.ReplaceWith(filters);
        }
    }

    private static XElement? FindDrive(
        IReadOnlyList<XElement> drives,
        GppDriveItemInfo item)
    {
        if (!string.IsNullOrWhiteSpace(item.Uid))
        {
            var byUid = drives.FirstOrDefault(element =>
                Attr(element, "uid").Equals(
                    item.Uid,
                    StringComparison.OrdinalIgnoreCase));

            if (byUid is not null)
                return byUid;
        }

        if (item.Ordinal > 0 && item.Ordinal <= drives.Count)
            return drives[item.Ordinal - 1];

        return null;
    }

    private static void Validate(GppDriveItemInfo item)
    {
        var letter = item.Letter.Trim().TrimEnd(':');

        if (letter.Length != 1 ||
            letter[0] < 'A' ||
            letter[0] > 'Z')
        {
            throw new InvalidOperationException(
                "Drive letter must be a single letter from A through Z.");
        }

        var action = NormalizeAction(item.Action);

        if (action is "C" or "R")
        {
            if (string.IsNullOrWhiteSpace(item.Path))
            {
                throw new InvalidOperationException(
                    "Create and Replace require a UNC path.");
            }
        }

        if (!string.IsNullOrWhiteSpace(item.Path) &&
            !item.Path.StartsWith(@"\\", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Drive Maps path must be a UNC path beginning with \\\\.");
        }

        _ = NormalizeVisibility(item.ThisDriveVisibility);
        _ = NormalizeVisibility(item.AllDrivesVisibility);
    }

    private GppDocumentTypeInfo GetDriveType() =>
        _documents.GetKnownTypes().First(type =>
            type.Name.Equals("Drive Maps", StringComparison.OrdinalIgnoreCase));

    private static string NormalizeAction(string action) =>
        action.Trim().ToUpperInvariant() switch
        {
            "C" or "CREATE" => "C",
            "D" or "DELETE" => "D",
            "R" or "REPLACE" => "R",
            _ => "U"
        };

    private static string NormalizeVisibility(string value) =>
        value.Trim().ToUpperInvariant() switch
        {
            "HIDE" => "HIDE",
            "SHOW" => "SHOW",
            _ => "NOCHANGE"
        };

    private static string Attr(XElement element, string name) =>
        element.Attributes()
            .FirstOrDefault(attribute =>
                attribute.Name.LocalName.Equals(
                    name,
                    StringComparison.OrdinalIgnoreCase))?
            .Value ?? string.Empty;

    private static bool IsTrue(string value) =>
        value == "1" ||
        value.Equals("true", StringComparison.OrdinalIgnoreCase);

    private static string FirstNonEmpty(params string[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))
        ?? string.Empty;

    private static void SetAttr(
        XElement element,
        string name,
        string value) =>
        element.SetAttributeValue(name, value ?? string.Empty);

    private static void SetOptionalBool(
        XElement element,
        string name,
        bool value)
    {
        if (value)
            element.SetAttributeValue(name, "1");
        else
            element.SetAttributeValue(name, null);
    }
}

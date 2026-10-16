using System.Security.Cryptography;
using System.Xml;
using System.Xml.Linq;
using GPOSettingsExplorer.Models;

namespace GPOSettingsExplorer.Services;

/// <summary>
/// Bounded read-only GPP Registry.xml structural metadata comparison. Deliberately
/// DOES NOT load or expose any Registry.xml value=, cpassword, filters' private
/// values or client-side settings. Identity/metadata agreement is NOT value
/// verification or Group Policy Preferences CSE/RSoP execution.
/// </summary>
public static class GpoGppRegistrySourceEvidenceService
{
    public const string NotApplicable = "NOT_GPP_REGISTRY";
    public const string NoSnapshot = "GPP_REGISTRY_SOURCE_NOT_SCANNED";
    public const string WrongSnapshot = "GPP_GPO_OR_DC_CHANGED";
    public const string UntrustedGpmc = "GPP_GPMC_IDENTITY_NOT_EXACT";
    public const string AmbiguousGpmc = "GPP_GPMC_UID_AMBIGUOUS";
    public const string FileUnavailable = "GPP_REGISTRY_XML_UNAVAILABLE";
    public const string FileIncomplete = "GPP_REGISTRY_XML_NOT_COMPLETE";
    public const string SourceMissing = "GPP_REGISTRY_UID_NOT_FOUND";
    public const string SourceAmbiguous = "GPP_REGISTRY_SOURCE_AMBIGUOUS";
    public const string Match = "GPP_STORED_METADATA_MATCH";
    public const string Mismatch = "GPP_STORED_METADATA_DIFFER";

    public sealed record Snapshot(
        Guid GpoId, string Scope, string Domain, string DomainController,
        string SourceFile, string Status, string FileSha256,
        IReadOnlyList<GpmcRegistryPreferenceReportService.Item> Items,
        DateTimeOffset CapturedAt);

    public sealed record Result(string Status, string Explanation, string SourceSha256 = "")
    {
        public bool IsCompared => Status is Match or Mismatch;
    }

    /// <summary>
    /// A single selected GPO/scope Registry.xml read from the session-pinned DC.
    /// No native MMC automation, GPO writes, Registry.pol access, or folder
    /// enumeration. The raw byte array is discarded after extracting metadata.
    /// </summary>
    public static Snapshot Capture(
        GpoInfo gpo, string scope, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(gpo);
        if (scope is not ("Computer" or "User"))
            throw new ArgumentException("GPP source scope must be Computer or User.", nameof(scope));

        var context = DomainConnectionState.Context;
        if (context is null || string.IsNullOrWhiteSpace(context.ConnectedServer) ||
            !context.DomainName.Equals(gpo.DomainName, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("One same-domain pinned DC is required.");

        var domain = context.DomainName;
        var dc = context.ConnectedServer;
        var file = ExpectedPath(gpo.Id, domain, dc, scope);
        token.ThrowIfCancellationRequested();
        var status = "Read";
        byte[] bytes = Array.Empty<byte>();
        DateTime modifiedAt = DateTime.MinValue;
        try
        {
            var info = new FileInfo(file);
            if (!info.Exists)
                status = "Absent";
            else if (info.Length > GppXmlSourceReader.MaxFileBytes)
                status = "Invalid";
            else
            {
                modifiedAt = info.LastWriteTimeUtc;
                using var stream = new FileStream(file, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete);
                if (stream.Length > GppXmlSourceReader.MaxFileBytes)
                    throw new InvalidDataException("GPP Registry.xml file exceeds bounded read cap.");
                using var buffer = new MemoryStream();
                var chunk = new byte[32768];
                var length = stream.Length;
                int read;
                while ((read = stream.Read(chunk, 0, chunk.Length)) > 0)
                {
                    token.ThrowIfCancellationRequested();
                    if (buffer.Length + read > GppXmlSourceReader.MaxFileBytes)
                        throw new InvalidDataException("GPP Registry.xml changed beyond bounded read cap.");
                    buffer.Write(chunk, 0, read);
                }
                if (buffer.Length != length || stream.Length != length)
                    status = "Changed";
                else
                    bytes = buffer.ToArray();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                                   InvalidDataException or System.Security.SecurityException)
        {
            status = "Unreadable";
        }

        token.ThrowIfCancellationRequested();
        var parsed = status == "Read" ? Parse(bytes) :
            new Parsed("Invalid", string.Empty,
                Array.Empty<GpmcRegistryPreferenceReportService.Item>());
        if (status == "Read")
        {
            status = parsed.Status;
            if (File.GetLastWriteTimeUtc(file) != modifiedAt)
                status = "Changed";
        }

        // Do not let a GPO source read migrate to another DC between open and
        // parse, even if both SYSVOL replicas appear to contain the same bytes.
        var current = DomainConnectionState.Context;
        if (current is null ||
            !current.DomainName.Equals(domain, StringComparison.OrdinalIgnoreCase) ||
            !current.ConnectedServer.Equals(dc, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "Domain/DC changed during GPP source read; evidence discarded.");

        return new Snapshot(gpo.Id, scope, domain, dc, file,
            status, status == "Read" ? parsed.Sha256 : string.Empty,
            status == "Read" ? parsed.Items :
                Array.Empty<GpmcRegistryPreferenceReportService.Item>(),
            DateTimeOffset.Now);
    }

    public sealed record Parsed(
        string Status, string Sha256,
        IReadOnlyList<GpmcRegistryPreferenceReportService.Item> Items);

    /// <summary>
    /// Pure parser used by the pinned read and by regression fixtures.
    /// Only direct Registry items of the standard RegistrySettings root
    /// are accepted. Unknown collection or XML layout is INCOMPLETE, not
    /// reported as "zero Registry values".
    /// </summary>
    public static Parsed Parse(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (bytes.Length is 0 or > GppXmlSourceReader.MaxFileBytes)
            return Fail();

        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            using var xml = XmlReader.Create(stream, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = GppXmlSourceReader.MaxFileBytes,
                MaxCharactersFromEntities = 0
            });
            var doc = XDocument.Load(xml);
            if (doc.Root?.Name.LocalName != "RegistrySettings")
                return Fail();

            var children = doc.Root.Elements().ToArray();
            if (children.Length > 20000 ||
                children.Any(x => x.Name.LocalName != "Registry"))
                return Fail(); // unsupported folder/collection layout
            var items = new List<GpmcRegistryPreferenceReportService.Item>();
            var uids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < children.Length; i++)
            {
                if (!GpmcRegistryPreferenceReportService.TryRead(children[i], out var item) ||
                    item.Uid.Length == 0 || !uids.Add(item.Uid))
                    return Fail();
                items.Add(item with { Order = i + 1 });
            }
            return new Parsed("Read", Convert.ToHexString(SHA256.HashData(bytes)),
                items);
        }
        catch (Exception ex) when (ex is XmlException or InvalidDataException)
        {
            return Fail();
        }

        static Parsed Fail() =>
            new("Invalid", "", Array.Empty<GpmcRegistryPreferenceReportService.Item>());
    }

    public static Result Compare(
        PolicySettingInfo setting, IReadOnlyList<PolicySettingInfo> indexed,
        Snapshot? snapshot, string expectedDomain, string pinnedDc)
    {
        ArgumentNullException.ThrowIfNull(setting);
        ArgumentNullException.ThrowIfNull(indexed);
        if (!setting.Extension.Equals("GppRegistry", StringComparison.OrdinalIgnoreCase))
            return Block(NotApplicable, "Only typed GPMC GPP Registry items are supported.");

        if (setting.GpoId == Guid.Empty ||
            setting.Scope is not ("Computer" or "User") ||
            !Guid.TryParse(setting.GppRegistryUid, out var uid) ||
            uid == Guid.Empty || setting.GppRegistryOrder < 1 ||
            string.IsNullOrWhiteSpace(setting.GppRegistryHive) ||
            string.IsNullOrWhiteSpace(setting.GppRegistryKeyPath) ||
            string.IsNullOrWhiteSpace(setting.GppRegistryAction))
            return Block(UntrustedGpmc,
                "A complete typed GPMC UID, order, action, hive and key is required.");

        if (indexed.Count(other => other.GpoId == setting.GpoId &&
            other.Scope.Equals(setting.Scope, StringComparison.OrdinalIgnoreCase) &&
            other.Extension.Equals("GppRegistry", StringComparison.OrdinalIgnoreCase) &&
            Guid.TryParse(other.GppRegistryUid, out var otherUid) &&
            otherUid == uid) != 1)
            return Block(AmbiguousGpmc,
                "Zero or multiple same-GPO/scope GPMC items have the same Registry UID.");

        if (snapshot is null)
            return Block(NoSnapshot,
                "Explicitly read this one GPO/scope Registry.xml from the pinned DC.");
        if (string.IsNullOrWhiteSpace(expectedDomain) ||
            string.IsNullOrWhiteSpace(pinnedDc) ||
            snapshot.GpoId != setting.GpoId ||
            !snapshot.Scope.Equals(setting.Scope, StringComparison.OrdinalIgnoreCase) ||
            !snapshot.Domain.Equals(expectedDomain, StringComparison.OrdinalIgnoreCase) ||
            !snapshot.DomainController.Equals(pinnedDc, StringComparison.OrdinalIgnoreCase) ||
            !snapshot.SourceFile.Equals(
                ExpectedPath(setting.GpoId, expectedDomain, pinnedDc, setting.Scope),
                StringComparison.OrdinalIgnoreCase))
            return Block(WrongSnapshot,
                "Selected GPO, scope, SYSVOL path, domain or pinned controller differs.");

        if (snapshot.Status == "Absent")
            return Block(FileUnavailable, "GPP Registry.xml was not captured; do not infer an unconfigured policy.");
        if (snapshot.Status != "Read" ||
            snapshot.FileSha256 is not { Length: 64 } ||
            !snapshot.FileSha256.All(Uri.IsHexDigit))
            return Block(FileIncomplete,
                "Registry.xml is unreadable, incomplete, invalid or changed while being captured.");

        var items = snapshot.Items.Where(item => Guid.TryParse(item.Uid, out var parsed) &&
            parsed == uid).ToArray();
        if (items.Length == 0)
            return Block(SourceMissing, "GPP Registry UID is absent from the selected source.");
        if (items.Length != 1)
            return Block(SourceAmbiguous, "Duplicate source Registry UIDs cannot be selected.");

        var candidate = items[0];
        var metadataMatches =
            candidate.Order == setting.GppRegistryOrder &&
            candidate.Hive.Equals(setting.GppRegistryHive, StringComparison.OrdinalIgnoreCase) &&
            candidate.KeyPath.Equals(setting.GppRegistryKeyPath, StringComparison.OrdinalIgnoreCase) &&
            candidate.ValueName.Equals(setting.GppRegistryValueName, StringComparison.OrdinalIgnoreCase) &&
            candidate.Action.Equals(setting.GppRegistryAction, StringComparison.OrdinalIgnoreCase) &&
            candidate.ValueType.Equals(setting.GppRegistryValueType, StringComparison.OrdinalIgnoreCase) &&
            candidate.HasFilters == setting.GppRegistryHasFilters;

        return new Result(metadataMatches ? Match : Mismatch,
            metadataMatches
                ? "Same-UID GPMC item and pinned Registry.xml agree on order, action, hive, " +
                  "key, value name, type and presence of targeting filters. Stored VALUE DATA, " +
                  "filter decisions, item disabled flags, precedence and client application " +
                  "are NOT verified."
                : "GPMC item metadata differs from the pinned Registry.xml. No registry " +
                  "value comparison was made; refresh the GPMC index and recapture " +
                  "the source before interpreting the difference.",
            snapshot.FileSha256);
    }

    private static string ExpectedPath(Guid gpoId, string domain, string dc, string scope) =>
        Path.Combine($@"\\{dc}\SYSVOL\{domain}", "Policies", gpoId.ToString("B"),
            scope == "User" ? "User" : "Machine",
            "Preferences", "Registry", "Registry.xml");

    private static Result Block(string status, string detail) => new(status, detail);
}

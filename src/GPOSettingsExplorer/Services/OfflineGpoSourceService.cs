using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using GPOSettingsExplorer.Models;

namespace GPOSettingsExplorer.Services;

/// <summary>
/// Read-only GPMC backup source reader. A manifest and fixed internal relative
/// paths are used; no AD connection, GPMC COM, MMC or file writes are required.
/// This is a snapshot of stored files, never effective RSoP.
/// </summary>
public static class OfflineGpoSourceService
{
    public const int MaxManifestBytes = 1024 * 1024;

    public static RealSettingsScanResult ReadManifest(
        string manifestPath, CancellationToken cancellation = default)
    {
        if (!Path.GetFileName(manifestPath).Equals("bkupInfo.xml",
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Select an original GPMC bkupInfo.xml manifest.");

        var manifest = Path.GetFullPath(manifestPath);
        var bytes = ReadBounded(manifest, MaxManifestBytes, cancellation);
        XDocument doc;
        using (var input = new MemoryStream(bytes))
        using (var reader = XmlReader.Create(input, new XmlReaderSettings
               {
                   DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null,
                   MaxCharactersInDocument = MaxManifestBytes
               }))
            doc = XDocument.Load(reader);

        string Value(params string[] names)
        {
            foreach (var name in names)
            {
                var item = doc.Root?.DescendantsAndSelf().FirstOrDefault(e =>
                    e.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase));
                if (item is not null && !string.IsNullOrWhiteSpace(item.Value))
                    return item.Value.Trim();
            }
            return "";
        }

        if (!Guid.TryParse(Value("GPOGuid", "GPOID", "GPOId"), out var id) ||
            !Guid.TryParse(Value("ID"), out _))
            throw new InvalidDataException(
                "The GPMC manifest has no unambiguous GPO/backup GUID.");
        var domain = Value("GPODomain", "Domain");
        if (string.IsNullOrWhiteSpace(domain) || domain.Length > 253 ||
            domain.Contains('/') || domain.Contains('\\') || domain.Contains(".."))
            throw new InvalidDataException("The backup domain metadata is missing or malformed.");
        var name = Value("GPODisplayName", "DisplayName");
        if (string.IsNullOrWhiteSpace(name))
            name = id.ToString("B");

        var root = Path.Combine(Path.GetDirectoryName(manifest)!,
            "DomainSysvol", "GPO");
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException(
                "The backup has no DomainSysvol/GPO content: " + root);
        return ReadTree(root, id, name, domain, cancellation);
    }

    public static RealSettingsScanResult ReadTree(
        string sourceRoot, Guid gpoId, string gpoName, string domain,
        CancellationToken cancellation = default)
    {
        if (gpoId == Guid.Empty)
            throw new ArgumentException("A real GPO GUID is required.", nameof(gpoId));
        var root = Path.GetFullPath(sourceRoot);
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException(root);

        var rows = new List<RealSettingRecord>();
        var sources = new List<RealSettingsFileEvidence>();
        foreach (var spec in new[]
        {
            (Path: Path.Combine("Machine", "Registry.pol"), Scope: "Computer", Pol: true),
            (Path: Path.Combine("User", "Registry.pol"), Scope: "User", Pol: true),
            (Path: Path.Combine("Machine", "Microsoft", "Windows NT",
                "SecEdit", "GptTmpl.inf"), Scope: "Computer", Pol: false)
        })
        {
            cancellation.ThrowIfCancellationRequested();
            var path = Path.Combine(root, spec.Path);
            if (!File.Exists(path))
            {
                sources.Add(new RealSettingsFileEvidence(path, "Absent", 0, "",
                    "Absent in this saved backup; not proof that a setting is Not Configured."));
                continue;
            }
            try
            {
                var cap = spec.Pol ? RegistryPolReader.MaxFileBytes :
                    SecurityTemplateSourceReader.MaxFileBytes;
                var original = ReadBounded(path, cap, cancellation);
                var sha = Convert.ToHexString(SHA256.HashData(original));
                var parsed = spec.Pol
                    ? RegistryPolReader.Parse(original, gpoId, gpoName,
                        spec.Scope, path, sha)
                    : SecurityTemplateSourceReader.Parse(original, gpoId, gpoName,
                        path, sha);
                rows.AddRange(parsed.Rows);
                sources.Add(new RealSettingsFileEvidence(
                    path, parsed.IsComplete ? "Read" : "Partial", parsed.Rows.Count,
                    sha, parsed.IsComplete
                        ? "Read from local GPMC backup; stored state only."
                        : string.Join(" | ", parsed.Issues.Take(10))));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                                       System.Security.SecurityException)
            {
                sources.Add(new RealSettingsFileEvidence(path, "Unreadable", 0, "",
                    ex.GetType().Name + ": " + ex.Message));
            }
        }

        var backupGpo = new GpoInfo
        {
            Id = gpoId, DisplayName = gpoName, DomainName = domain
        };
        GppXmlSourceScanService.Scan(root, backupGpo, rows, sources, cancellation);

        return new RealSettingsScanResult(
            gpoId, gpoName, domain, "OFFLINE-GPMC-BACKUP",
            DateTimeOffset.UtcNow, rows, sources);
    }

    internal static byte[] ReadBounded(
        string path, int maxBytes, CancellationToken cancellation = default)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        if (file.Length > maxBytes)
            throw new InvalidDataException("Source exceeds read cap: " + path);
        using var memory = new MemoryStream();
        var block = new byte[32 * 1024];
        int count;
        while ((count = file.Read(block, 0, block.Length)) > 0)
        {
            cancellation.ThrowIfCancellationRequested();
            if (memory.Length + count > maxBytes)
                throw new InvalidDataException("Source grew past cap: " + path);
            memory.Write(block, 0, count);
        }
        return memory.ToArray();
    }
}
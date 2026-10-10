using System.Text;
using GPOSettingsExplorer.Models;

namespace GPOSettingsExplorer.Services;

/// <summary>
/// Reads only the three core policy-storage files from the DC-pinned SYSVOL.
/// This is an on-demand inspection of stored files, not a full CSE inventory
/// and not an RSoP evaluator. Individual file failures remain visible.
/// </summary>
public static class StoredGpoReadService
{
    public static Task<StoredGpoScanResult> ScanAsync(
        GpoInfo gpo,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => Scan(gpo, cancellationToken), cancellationToken);

    public static StoredGpoScanResult Scan(
        GpoInfo gpo,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(gpo);
        if (gpo.Id == Guid.Empty || string.IsNullOrWhiteSpace(gpo.DomainName))
            throw new ArgumentException("A valid GPO and domain are required.");

        // An empty server would permit a DFS-referral to an arbitrary DC,
        // breaking the pinned-DC consistency promised by the application.
        if (string.IsNullOrWhiteSpace(
                DomainConnectionState.GetServerFor(gpo.DomainName)))
            throw new InvalidOperationException(
                "A pinned domain controller is required for the native stored-file scan.");

        var root = Path.Combine(
            DomainConnectionState.BuildSysvolRoot(gpo.DomainName),
            "Policies", gpo.Id.ToString("B"));
        var rows = new List<StoredGpoSetting>();
        var sources = new List<StoredGpoSourceStatus>();

        var candidates = new (string Name, string Scope, string Relative)[]
        {
            ("Registry.pol", "Computer", Path.Combine("Machine", "Registry.pol")),
            ("Registry.pol", "User", Path.Combine("User", "Registry.pol")),
            ("GptTmpl.inf", "Computer",
                Path.Combine("Machine", "Microsoft", "Windows NT", "SecEdit", "GptTmpl.inf"))
        };

        foreach (var c in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = Path.Combine(root, c.Relative);
            try
            {
                // Absence is a missing source, NEVER a Not Configured verdict.
                if (!File.Exists(path))
                {
                    sources.Add(new StoredGpoSourceStatus(
                        c.Name, c.Scope, "Not present",
                        "File was not present on the selected DC; no policy state inferred.",
                        path, 0));
                    continue;
                }

                var info = new FileInfo(path);
                if (info.Length > StoredGpoFileParser.MaxFileBytes)
                    throw new FormatException("Stored policy file exceeded the 32 MiB safety limit.");

                // Open strictly for read; other administrators may still save
                // the GPO. Avoid unbounded CopyTo allocations and detect
                // obvious concurrent writes before accepting evidence.
                byte[] file;
                using (var stream = new FileStream(path, FileMode.Open,
                    FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    var length = stream.Length;
                    if (length > StoredGpoFileParser.MaxFileBytes)
                        throw new FormatException("GPO source changed beyond size limit during reading.");
                    using var data = new MemoryStream((int)length);
                    var buffer = new byte[64 * 1024];
                    int count;
                    while ((count = stream.Read(buffer)) > 0)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (data.Length + count > StoredGpoFileParser.MaxFileBytes)
                            throw new FormatException("GPO file grew beyond size limit while reading.");
                        data.Write(buffer, 0, count);
                    }
                    if (stream.Length != length || data.Length != length)
                        throw new IOException("GPO file changed length during this read; retry.");
                    file = data.ToArray();
                }
                info.Refresh();
                if (info.Length != file.Length)
                    throw new IOException("GPO file changed while scanning; retry.");

                cancellationToken.ThrowIfCancellationRequested();
                var parsed = c.Name == "Registry.pol"
                    ? StoredGpoFileParser.ReadRegistryPol(file, gpo.Id,
                        gpo.DisplayName, c.Scope, path)
                    : StoredGpoFileParser.ReadSecurityInf(file, gpo.Id,
                        gpo.DisplayName, path);
                rows.AddRange(parsed);
                sources.Add(new StoredGpoSourceStatus(
                    c.Name, c.Scope, "Read",
                    "Read-only decode completed; this does not prove applied policy.",
                    path, parsed.Count));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                      or FormatException or DecoderFallbackException)
            {
                sources.Add(new StoredGpoSourceStatus(c.Name, c.Scope,
                    "Read error", ex.Message, path, 0));
            }
        }

        return new StoredGpoScanResult(
            gpo.Id, gpo.DisplayName, rows, sources, DateTimeOffset.Now);
    }
}

using System.DirectoryServices;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using GPOSettingsExplorer.Models;

namespace GPOSettingsExplorer.Services;

/// <summary>
/// Evidence from AD and SYSVOL on a single, session-pinned DC.
/// Never treats single-DC agreement as replication convergence or client RSoP.
/// </summary>
public sealed record GpoConsistencyFinding(string Check, string Status, string Details);

public sealed record GpoConsistencyReport(
    string GpoName, Guid GpoId, string Domain, string DomainController,
    DateTimeOffset CapturedAt, IReadOnlyList<GpoConsistencyFinding> Findings)
{
    public int Errors => Findings.Count(f => f.Status == "Error");
    public int Unknowns => Findings.Count(f => f.Status == "Unknown");
    public bool HasBlockingIssues => Errors > 0 || Unknowns > 0;
    public string Summary => $"{Errors} error(s), {Unknowns} unknown(s), " +
        $"{Findings.Count(f => f.Status == "Warning")} warning(s), pinned DC: {DomainController}";
    public string ToText() =>
        $"GPO HEALTH CHECK (READ ONLY)\nCaptured: {CapturedAt:O}\nGPO: {GpoName} ({GpoId:B})\n" +
        $"Domain: {Domain}\nPinned DC: {DomainController}\n{Summary}\n" +
        "Scope: one DC only, NOT a domain-wide replication, ACL, WMI or RSoP verdict.\n\n" +
        string.Join("\n", Findings.Select(f => $"[{f.Status}] {f.Check}: {f.Details}"));
}

public sealed record GptIniVersionResult(bool Valid, uint Version, string Error)
{
    public uint ComputerVersion => Version >> 16;
    public uint UserVersion => Version & 0xFFFF;
}

public static class GptIniVersionParser
{
    public const int MaxBytes = 64 * 1024;

    public static byte[] ReadBounded(
        string path, CancellationToken cancellation = default)
    {
        using var source = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        if (source.Length > MaxBytes)
            throw new InvalidDataException("GPT.INI exceeds the 64 KiB read cap.");
        using var buffer = new MemoryStream();
        var chunk = new byte[4096];
        int read;
        while ((read = source.Read(chunk, 0, chunk.Length)) > 0)
        {
            cancellation.ThrowIfCancellationRequested();
            if (buffer.Length + read > MaxBytes)
                throw new InvalidDataException("GPT.INI grew beyond the 64 KiB read cap.");
            buffer.Write(chunk, 0, read);
        }
        return buffer.ToArray();
    }

    public static GptIniVersionResult Parse(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (bytes.Length == 0 || bytes.Length > MaxBytes)
            return Fail("GPT.INI is empty or exceeds the 64 KiB read cap.");

        string text;
        try
        {
            if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
                text = new UnicodeEncoding(false, true, true).GetString(bytes, 2, bytes.Length - 2);
            else if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
                text = new UnicodeEncoding(true, true, true).GetString(bytes, 2, bytes.Length - 2);
            else if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
                text = new UTF8Encoding(false, true).GetString(bytes, 3, bytes.Length - 3);
            else
                text = new UTF8Encoding(false, true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return Fail("GPT.INI has invalid encoding; no replacement characters were accepted.");
        }

        var section = "";
        uint? version = null;
        using var reader = new StringReader(text);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            var item = line.Trim();
            if (item.Length == 0 || item.StartsWith(';') || item.StartsWith('#'))
                continue;
            if (item.StartsWith('['))
            {
                if (!item.EndsWith(']'))
                    return Fail("Malformed GPT.INI section header.");
                section = item[1..^1].Trim();
                continue;
            }
            if (!section.Equals("General", StringComparison.OrdinalIgnoreCase))
                continue;
            var index = item.IndexOf('=');
            if (index <= 0 || !item[..index].Trim().Equals("Version",
                    StringComparison.OrdinalIgnoreCase))
                continue;
            if (version is not null)
                return Fail("Duplicate [General] Version values are ambiguous.");
            if (!uint.TryParse(item[(index + 1)..].Trim(), NumberStyles.None,
                    CultureInfo.InvariantCulture, out var parsed))
                return Fail("Invalid [General] Version: expected unsigned decimal UInt32.");
            version = parsed;
        }

        return version is uint number
            ? new GptIniVersionResult(true, number, "")
            : Fail("GPT.INI is missing [General] Version.");
    }

    private static GptIniVersionResult Fail(string message) => new(false, 0, message);
}

public sealed class GpoConsistencyService
{
    public GpoConsistencyReport Inspect(GpoInfo gpo, CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(gpo);
        var context = DomainConnectionState.Context;
        if (context is null || string.IsNullOrWhiteSpace(context.ConnectedServer) ||
            !context.DomainName.Equals(gpo.DomainName, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "A connected, session-pinned DC matching the selected GPO is required.");

        var results = new List<GpoConsistencyFinding>();
        void Add(string check, string status, string details) =>
            results.Add(new GpoConsistencyFinding(check, status, details));

        var id = gpo.Id.ToString("B").ToUpperInvariant();
        var root = Path.Combine(DomainConnectionState.BuildSysvolRoot(context.DomainName),
            "Policies", id);
        var gptPath = Path.Combine(root, "GPT.INI");
        uint? adVersion = null;
        uint? sysvolVersion = null;

        cancellation.ThrowIfCancellationRequested();
        try
        {
            var dn = $"CN={id},CN=Policies,CN=System,{context.DomainDistinguishedName}";
            using var entry = new DirectoryEntry(DomainConnectionState.BuildLdapPath(dn));
            entry.RefreshCache(new[] { "versionNumber", "gPCFileSysPath" });

            if (entry.Properties["versionNumber"].Value is { } raw)
            {
                adVersion = unchecked((uint)Convert.ToInt32(raw, CultureInfo.InvariantCulture));
                Add("AD GPC", "Pass", $"AD versionNumber={adVersion.Value}.");
            }
            else
                Add("AD GPC", "Error", "AD versionNumber missing.");

            var advertised = Convert.ToString(entry.Properties["gPCFileSysPath"].Value) ?? "";
            var plausible = GpoSysvolPathValidator.MatchesGpo(
                advertised, context.DomainName, gpo.Id, context.ConnectedServer);
            Add("AD gPCFileSysPath", plausible ? "Pass" : "Error",
                plausible ? "AD path identifies the selected GPO. Reads are pinned to the current DC." :
                    "Noncanonical SYSVOL path, unexpected domain/server, or wrong GPO GUID: " + advertised);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Add("AD GPC", "Unknown", $"{ex.GetType().Name}: {ex.Message}");
        }

        cancellation.ThrowIfCancellationRequested();
        try
        {
            Add("SYSVOL GPT folder", Directory.Exists(root) ? "Pass" : "Error",
                (Directory.Exists(root) ? "Reachable: " : "Absent or inaccessible: ") + root);

            if (!File.Exists(gptPath))
                Add("GPT.INI", "Error", "Absent or inaccessible on pinned DC: " + gptPath);
            else
            {
                var info = new FileInfo(gptPath);
                if (info.Length > GptIniVersionParser.MaxBytes)
                    Add("GPT.INI", "Error", $"Exceeds {GptIniVersionParser.MaxBytes} byte limit.");
                else
                {
                    using var file = new FileStream(gptPath, FileMode.Open, FileAccess.Read,
                        FileShare.ReadWrite | FileShare.Delete);
                    using var buffer = new MemoryStream();
                    var block = new byte[4096];
                    int count;
                    while ((count = file.Read(block, 0, block.Length)) > 0)
                    {
                        cancellation.ThrowIfCancellationRequested();
                        if (buffer.Length + count > GptIniVersionParser.MaxBytes)
                            throw new InvalidDataException("GPT.INI grew beyond read cap.");
                        buffer.Write(block, 0, count);
                    }
                    var bytes = buffer.ToArray();
                    var parsed = GptIniVersionParser.Parse(bytes);
                    if (!parsed.Valid)
                        Add("GPT.INI", "Error", parsed.Error);
                    else
                    {
                        sysvolVersion = parsed.Version;
                        Add("GPT.INI", "Pass",
                            $"Version={parsed.Version}, computer={parsed.ComputerVersion}, " +
                            $"user={parsed.UserVersion}, SHA-256={Convert.ToHexString(SHA256.HashData(bytes))}.");
                    }
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Add("Pinned SYSVOL", "Unknown", $"{ex.GetType().Name}: {ex.Message}");
        }

        if (adVersion is uint ad && sysvolVersion is uint gpt)
            Add("AD/SYSVOL version", ad == gpt ? "Pass" : "Error",
                $"AD={ad} [computer={ad >> 16}, user={ad & 0xFFFF}]; " +
                $"GPT.INI={gpt} [computer={gpt >> 16}, user={gpt & 0xFFFF}]. " +
                (ad == gpt ? "Equal on this DC." : "Mismatch; investigate replication/interrupted write."));
        else
            Add("AD/SYSVOL version", "Unknown",
                "Cannot conclude consistency: one or both versions could not be read.");

        cancellation.ThrowIfCancellationRequested();
        try
        {
            var sources = new RealSettingsSourceService().Scan(gpo, cancellation);
            foreach (var source in sources.Files)
                Add("Source: " + Path.GetFileName(source.SourceFile),
                    source.Status switch
                    {
                        "Read" => "Pass",
                        "Absent" => "Info",
                        _ => "Error"
                    }, source.Status + ": " + source.Details);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Add("Source coverage", "Unknown", ex.GetType().Name + ": " + ex.Message);
        }

        Add("Coverage", "Info",
            "Read-only snapshot of ONE selected DC. DFSR, other DCs, ACL effectiveness, " +
            "security group tokens, WMI, loopback and client RSoP remain unverified.");
        return new GpoConsistencyReport(gpo.DisplayName, gpo.Id, context.DomainName,
            context.ConnectedServer, DateTimeOffset.Now, results);
    }
}

using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace GPOSettingsExplorer.Services;

/// <summary>
/// Bounded read-only client GroupPolicy Operational event collection via the
/// built-in Windows wevtutil.exe. Explicit computer target, no PowerShell,
/// no agent installation. Stores only event metadata, not sensitive event data.
/// </summary>
public sealed record GpoClientEvent(
    int EventId, int Level, DateTimeOffset? Time, string Provider, string Status);
public sealed record GpoClientEventReport(
    string Computer, DateTimeOffset CollectedUtc,
    IReadOnlyList<GpoClientEvent> Events)
{
    public string ToText() =>
        "GROUP POLICY CLIENT EVENT DIAGNOSTICS (READ ONLY)\n" +
        "Computer: " + Computer + " | Captured UTC: " +
        CollectedUtc.ToString("O") + "\n" +
        "Event data/message bodies omitted. Review event logs on the target to diagnose.\n" +
        "A missing event is not proof that a GPO was applied or excluded.\n\n" +
        string.Join("\n", Events.Select(e =>
            (e.Time?.ToString("O") ?? "unknown time") +
            " | ID " + e.EventId + " | Level " + e.Level +
            " | " + e.Status));
}

public static class GpoClientEventService
{
    private static readonly Regex Host = new(
        @"^[A-Za-z0-9](?:[A-Za-z0-9.-]{0,251}[A-Za-z0-9])?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static GpoClientEventReport ParseXml(string computer, string xml)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(xml);
        if (xml.Length > 8 * 1024 * 1024)
            throw new InvalidDataException("Group Policy event XML exceeds 8 MiB.");
        // wevtutil /f:xml may print multiple independent Event elements.
        var cleaned = Regex.Replace(xml, @"<\?xml[^>]*\?>", "",
            RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200));
        var wrapped = "<Events>" + cleaned + "</Events>";
        XDocument doc;
        using (var stream = new StringReader(wrapped))
        using (var reader = XmlReader.Create(stream, new XmlReaderSettings
               {
                   DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null,
                   MaxCharactersInDocument = 8 * 1024 * 1024
               }))
            doc = XDocument.Load(reader);
        var events = new List<GpoClientEvent>();
        foreach (var item in doc.Root!.Elements().Take(400))
        {
            if (item.Name.LocalName != "Event")
                continue;
            var system = item.Elements().FirstOrDefault(e => e.Name.LocalName == "System");
            if (system is null)
                continue;
            string? Value(string name) => system.Elements()
                .FirstOrDefault(e => e.Name.LocalName == name)?.Value;
            if (!int.TryParse(Value("EventID"), out var id))
                continue;
            _ = int.TryParse(Value("Level"), out var level);
            var provider = system.Elements().FirstOrDefault(e =>
                e.Name.LocalName == "Provider")?.Attribute("Name")?.Value ?? "";
            if (provider.Length > 256) provider = provider[..256];
            var at = system.Elements().FirstOrDefault(e =>
                e.Name.LocalName == "TimeCreated")?.Attribute("SystemTime")?.Value;
            DateTimeOffset? timestamp =
                DateTimeOffset.TryParse(at, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal, out var value) ? value : null;
            var status = level switch
            {
                1 or 2 => "Error: inspect local event data",
                3 => "Warning: inspect local event data",
                _ when id == 4016 => "Extension processing started (not success)",
                _ when id == 5016 => "Extension processing finished (check event payload)",
                _ => "Event observed (no policy success inference)"
            };
            events.Add(new(id, level, timestamp, provider, status));
        }
        return new GpoClientEventReport(
            computer, DateTimeOffset.UtcNow, events);
    }

    public static async Task<GpoClientEventReport> CollectAsync(
        string computer, CancellationToken cancellation = default)
    {
        if (computer != "." &&
            (!Host.IsMatch(computer) || computer.Contains("..", StringComparison.Ordinal)))
            throw new ArgumentException("Specify a hostname, FQDN or '.' for local events.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(TimeSpan.FromSeconds(40));
        var exe = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "wevtutil.exe");
        var start = new ProcessStartInfo(exe)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (var arg in new[]
        {
            "qe", "Microsoft-Windows-GroupPolicy/Operational",
            "/f:xml", "/c:120", "/rd:true"
        }) start.ArgumentList.Add(arg);
        if (computer != ".") start.ArgumentList.Add("/r:" + computer);

        using var process = Process.Start(start) ??
            throw new InvalidOperationException("Windows event reader could not start.");
        try
        {
            var stdoutTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var stderrTask = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            var stdout = await stdoutTask;
            var stderr = await stderrTask;
            if (process.ExitCode != 0)
                throw new IOException("wevtutil failed: " + process.ExitCode +
                    ". Check Remote Event Log Management access and Event Log service. " +
                    (stderr.Length > 1024 ? stderr[..1024] : stderr));
            return ParseXml(computer, stdout);
        }
        finally
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
    }
}
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using GPOSettingsExplorer.Models;

namespace GPOSettingsExplorer.Services;

public sealed class GpoScriptService
{
    private static readonly HashSet<string> ScriptExtensions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".bat", ".cmd", ".ps1", ".vbs", ".js", ".wsf",
            ".psm1", ".psd1", ".hta"
        };

    private static readonly Regex IniAssignment =
        new(
            @"^(?<index>\d+)(?<field>CmdLine|Parameters)\s*=\s*(?<value>.*)$",
            RegexOptions.IgnoreCase |
            RegexOptions.CultureInvariant |
            RegexOptions.Compiled);

    private static readonly Guid ScriptsExtensionGuid =
        new("42B5FAAE-6536-11D2-AE5A-0000F87571E3");

    private static readonly Guid MachineScriptsToolGuid =
        new("40B6664F-4972-11D1-A7CA-0000F87571E3");

    private static readonly Guid UserScriptsToolGuid =
        new("40B66650-4972-11D1-A7CA-0000F87571E3");

    public IReadOnlyList<GpoScriptInfo> Load(
        IEnumerable<GpoInfo> gpos,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var gpoArray = gpos.ToArray();
        var result = new List<GpoScriptInfo>();

        for (var gpoIndex = 0; gpoIndex < gpoArray.Length; gpoIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var gpo = gpoArray[gpoIndex];
            progress?.Report(
                $"Scanning GPO scripts {gpoIndex + 1}/{gpoArray.Length}: {gpo.DisplayName}");

            foreach (var scope in new[] { "Computer", "User" })
            {
                ScanScope(
                    gpo,
                    scope,
                    result,
                    cancellationToken);
            }
        }

        return result
            .OrderBy(item => item.GpoName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(item => item.Scope, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.EventName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Order)
            .ThenBy(item => item.FileName, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    public IReadOnlyList<GpoScriptSearchResult> SearchContent(
        IEnumerable<GpoScriptInfo> scripts,
        string query,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return Array.Empty<GpoScriptSearchResult>();
        }

        var needle =
            query.Trim();

        var physicalFiles =
            scripts
                .Where(script =>
                    script.Exists &&
                    IsSupportedScriptFile(
                        script.FullPath))
                .GroupBy(
                    script => script.FullPath,
                    StringComparer.OrdinalIgnoreCase)
                .Select(group => group.ToArray())
                .ToArray();

        var contentGroups =
            new Dictionary<string, List<GpoScriptInfo>>(
                StringComparer.OrdinalIgnoreCase);

        var documents =
            new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase);

        foreach (var pathGroup in physicalFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var representative =
                pathGroup[0];

            byte[] bytes;

            try
            {
                bytes =
                    File.ReadAllBytes(
                        representative.FullPath);
            }
            catch
            {
                continue;
            }

            var hash =
                Convert.ToHexString(
                    SHA256.HashData(
                        bytes));

            if (!contentGroups.TryGetValue(
                    hash,
                    out var copies))
            {
                copies =
                    new List<GpoScriptInfo>();

                contentGroups[hash] =
                    copies;
            }

            copies.AddRange(
                pathGroup);

            if (!documents.ContainsKey(
                    hash))
            {
                try
                {
                    documents[hash] =
                        ReadDocument(
                            representative.FullPath)
                        .Text;
                }
                catch
                {
                    documents[hash] =
                        string.Empty;
                }
            }
        }

        var result =
            new List<GpoScriptSearchResult>();

        foreach (var pair in contentGroups)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var copies =
                pair.Value
                    .OrderBy(
                        item => item.GpoName,
                        StringComparer.CurrentCultureIgnoreCase)
                    .ThenBy(
                        item => item.Scope,
                        StringComparer.OrdinalIgnoreCase)
                    .ThenBy(
                        item => item.EventName,
                        StringComparer.OrdinalIgnoreCase)
                    .ToArray();

            var representative =
                copies[0];

            var metadataMatch =
                copies.FirstOrDefault(script =>
                    script.FileName.Contains(
                        needle,
                        StringComparison.CurrentCultureIgnoreCase) ||
                    script.Parameters.Contains(
                        needle,
                        StringComparison.CurrentCultureIgnoreCase) ||
                    script.FullPath.Contains(
                        needle,
                        StringComparison.CurrentCultureIgnoreCase));

            if (metadataMatch is not null)
            {
                result.Add(
                    new GpoScriptSearchResult
                    {
                        Scripts =
                            copies,
                        Identity =
                            pair.Key,
                        LineNumber =
                            0,
                        LineText =
                            BuildMetadataMatchText(
                                metadataMatch,
                                needle)
                    });
            }

            var text =
                documents.TryGetValue(
                    pair.Key,
                    out var cachedText)
                    ? cachedText
                    : string.Empty;

            if (string.IsNullOrEmpty(
                    text))
            {
                continue;
            }

            var lines =
                text
                    .Replace(
                        "\r\n",
                        "\n",
                        StringComparison.Ordinal)
                    .Replace(
                        "\r",
                        "\n",
                        StringComparison.Ordinal)
                    .Split('\n');

            for (var lineIndex = 0;
                 lineIndex < lines.Length;
                 lineIndex++)
            {
                if (!lines[lineIndex].Contains(
                        needle,
                        StringComparison.CurrentCultureIgnoreCase))
                {
                    continue;
                }

                result.Add(
                    new GpoScriptSearchResult
                    {
                        Scripts =
                            copies,
                        Identity =
                            pair.Key,
                        LineNumber =
                            lineIndex + 1,
                        LineText =
                            lines[lineIndex].Trim()
                    });
            }
        }

        // Missing script references cannot be content-hashed, but their
        // metadata is still useful for searches such as ".vbs".
        foreach (var script in scripts.Where(item =>
                     !item.Exists))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!script.FileName.Contains(
                    needle,
                    StringComparison.CurrentCultureIgnoreCase) &&
                !script.Parameters.Contains(
                    needle,
                    StringComparison.CurrentCultureIgnoreCase) &&
                !script.FullPath.Contains(
                    needle,
                    StringComparison.CurrentCultureIgnoreCase))
            {
                continue;
            }

            result.Add(
                new GpoScriptSearchResult
                {
                    Scripts =
                        new[] { script },
                    Identity =
                        "MISSING|" +
                        script.FullPath,
                    LineNumber =
                        0,
                    LineText =
                        BuildMetadataMatchText(
                            script,
                            needle)
                });
        }

        return result
            .OrderBy(
                item => item.FileName,
                StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(
                item => item.LineNumber)
            .ThenBy(
                item => item.LineText,
                StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    private static string BuildMetadataMatchText(
        GpoScriptInfo script,
        string needle)
    {
        if (script.FileName.Contains(
                needle,
                StringComparison.CurrentCultureIgnoreCase))
        {
            return "<matched file name>";
        }

        if (script.Parameters.Contains(
                needle,
                StringComparison.CurrentCultureIgnoreCase))
        {
            return $"<parameters> {script.Parameters}";
        }

        return "<matched path>";
    }

    public GpoScriptDocument ReadDocument(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var (encoding, bomLength, emitBom) = DetectEncoding(bytes);

        return new GpoScriptDocument
        {
            Text = encoding.GetString(bytes, bomLength, bytes.Length - bomLength),
            CodePage = encoding.CodePage,
            EmitBom = emitBom
        };
    }

    public void SaveDocument(
        GpoInfo gpo,
        string domainDistinguishedName,
        GpoScriptInfo script,
        GpoScriptDocument document)
    {
        if (!script.Exists ||
            !IsSupportedScriptFile(script.FullPath))
        {
            throw new InvalidOperationException(
                "Only existing text script files stored in the GPO can be edited.");
        }

        var gpoRoot = GetGpoRoot(gpo);
        var fullPath = Path.GetFullPath(script.FullPath);
        var fullRoot = Path.GetFullPath(gpoRoot);

        if (!fullPath.StartsWith(
                fullRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "The selected script is outside this GPO's SYSVOL folder and will not be modified.");
        }

        var encoding =
            Encoding.GetEncoding(
                document.CodePage);

        var original =
            File.ReadAllBytes(
                fullPath);

        var temp =
            fullPath +
            ".gposes-" +
            Guid.NewGuid().ToString("N") +
            ".tmp";

        WriteText(
            temp,
            document.Text,
            encoding,
            document.EmitBom);

        try
        {
            File.Copy(
                temp,
                fullPath,
                overwrite: true);

            using var policy =
                new NativeGroupPolicyObject(
                    gpo,
                    domainDistinguishedName);

            var extensionGuid =
                ScriptsExtensionGuid;

            var toolGuid =
                script.Scope.Equals(
                    "User",
                    StringComparison.OrdinalIgnoreCase)
                    ? UserScriptsToolGuid
                    : MachineScriptsToolGuid;

            policy.Save(
                machine: !script.Scope.Equals(
                    "User",
                    StringComparison.OrdinalIgnoreCase),
                add: true,
                ref extensionGuid,
                ref toolGuid);
        }
        catch
        {
            File.WriteAllBytes(
                fullPath,
                original);

            throw;
        }
        finally
        {
            try
            {
                File.Delete(
                    temp);
            }
            catch
            {
            }
        }
    }

    private static void ScanScope(
        GpoInfo gpo,
        string scope,
        ICollection<GpoScriptInfo> result,
        CancellationToken cancellationToken)
    {
        var root = Path.Combine(
            GetGpoRoot(gpo),
            scope.Equals("User", StringComparison.OrdinalIgnoreCase)
                ? "User"
                : "Machine",
            "Scripts");

        if (!Directory.Exists(root))
            return;

        var knownPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var iniName in new[] { "scripts.ini", "psscripts.ini" })
        {
            var iniPath = Path.Combine(root, iniName);
            if (!File.Exists(iniPath))
                continue;

            foreach (var entry in ParseIni(iniPath))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var path = ResolveScriptPath(
                    root,
                    entry.EventName,
                    entry.CommandLine);

                var exists = File.Exists(path);
                var info = exists ? new FileInfo(path) : null;

                result.Add(new GpoScriptInfo
                {
                    GpoId = gpo.Id,
                    GpoName = gpo.DisplayName,
                    DomainName = gpo.DomainName,
                    Scope = scope,
                    EventName = entry.EventName,
                    Order = entry.Order,
                    FileName = Path.GetFileName(
                        string.IsNullOrWhiteSpace(entry.CommandLine)
                            ? path
                            : entry.CommandLine.Trim('"')),
                    Parameters = entry.Parameters,
                    FullPath = path,
                    SourceIni = iniPath,
                    Referenced = true,
                    Exists = exists,
                    Size = info?.Length ?? 0,
                    Modified = info?.LastWriteTime
                });

                knownPaths.Add(Path.GetFullPath(path));
            }
        }

        foreach (var path in Directory.EnumerateFiles(
                     root,
                     "*",
                     SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!IsSupportedScriptFile(path))
                continue;

            var fullPath = Path.GetFullPath(path);
            if (knownPaths.Contains(fullPath))
                continue;

            var info = new FileInfo(path);
            result.Add(new GpoScriptInfo
            {
                GpoId = gpo.Id,
                GpoName = gpo.DisplayName,
                DomainName = gpo.DomainName,
                Scope = scope,
                EventName = DetectEventName(root, path),
                Order = int.MaxValue,
                FileName = info.Name,
                FullPath = fullPath,
                Referenced = false,
                Exists = true,
                Size = info.Length,
                Modified = info.LastWriteTime
            });
        }
    }

    private static IEnumerable<IniScriptEntry> ParseIni(string path)
    {
        var document = ReadText(path);
        var section = string.Empty;
        var entries =
            new Dictionary<(string Section, int Index), MutableIniEntry>();

        foreach (var rawLine in document
                     .Replace("\r\n", "\n", StringComparison.Ordinal)
                     .Replace("\r", "\n", StringComparison.Ordinal)
                     .Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 ||
                line.StartsWith(';') ||
                line.StartsWith('#'))
                continue;

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                section = line[1..^1].Trim();
                continue;
            }

            if (!IsScriptEvent(section))
                continue;

            var match = IniAssignment.Match(line);
            if (!match.Success ||
                !int.TryParse(match.Groups["index"].Value, out var index))
                continue;

            var key = (section, index);
            if (!entries.TryGetValue(key, out var entry))
            {
                entry = new MutableIniEntry();
                entries[key] = entry;
            }

            var value = match.Groups["value"].Value.Trim();
            if (match.Groups["field"].Value.Equals(
                    "CmdLine",
                    StringComparison.OrdinalIgnoreCase))
                entry.CommandLine = value;
            else
                entry.Parameters = value;
        }

        return entries
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Value.CommandLine))
            .OrderBy(pair => pair.Key.Section, StringComparer.OrdinalIgnoreCase)
            .ThenBy(pair => pair.Key.Index)
            .Select(pair => new IniScriptEntry(
                pair.Key.Section,
                pair.Key.Index,
                pair.Value.CommandLine,
                pair.Value.Parameters))
            .ToArray();
    }

    private static string ResolveScriptPath(
        string scriptsRoot,
        string eventName,
        string commandLine)
    {
        var command = (commandLine ?? string.Empty).Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(command))
            return Path.Combine(scriptsRoot, eventName, "<missing-script>");

        if (Path.IsPathFullyQualified(command))
            return command;

        var eventCandidate = Path.GetFullPath(
            Path.Combine(scriptsRoot, eventName, command));

        if (File.Exists(eventCandidate))
            return eventCandidate;

        var rootCandidate = Path.GetFullPath(
            Path.Combine(scriptsRoot, command));

        return File.Exists(rootCandidate)
            ? rootCandidate
            : eventCandidate;
    }

    private static string DetectEventName(string root, string path)
    {
        var relative = Path.GetRelativePath(root, path);
        var first = relative.Split(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar)[0];

        return IsScriptEvent(first) ? first : string.Empty;
    }

    private static bool IsScriptEvent(string value) =>
        value.Equals("Startup", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("Shutdown", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("Logon", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("Logoff", StringComparison.OrdinalIgnoreCase);

    private static bool IsSupportedScriptFile(string path) =>
        ScriptExtensions.Contains(Path.GetExtension(path));

    private static string GetGpoRoot(
        GpoInfo gpo) =>
        Path.Combine(
            DomainConnectionState.BuildSysvolRoot(
                gpo.DomainName),
            "Policies",
            gpo.Id.ToString("B").ToUpperInvariant());

    private static string ReadText(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var (encoding, bomLength, _) = DetectEncoding(bytes);
        return encoding.GetString(bytes, bomLength, bytes.Length - bomLength);
    }

    private static (Encoding Encoding, int BomLength, bool EmitBom) DetectEncoding(
        byte[] bytes)
    {
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            return (new UnicodeEncoding(false, true), 2, true);

        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            return (new UnicodeEncoding(true, true), 2, true);

        if (bytes.Length >= 3 &&
            bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return (new UTF8Encoding(true), 3, true);

        var sample = bytes.Take(Math.Min(bytes.Length, 256)).ToArray();
        var oddZeros = sample
            .Where((value, index) => index % 2 == 1 && value == 0)
            .Count();

        if (oddZeros > 12)
            return (new UnicodeEncoding(false, false), 0, false);

        return (new UTF8Encoding(false), 0, false);
    }

    private static void WriteText(
        string path,
        string text,
        Encoding encoding,
        bool emitBom)
    {
        using var stream = new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None);

        if (emitBom)
        {
            var preamble = encoding.GetPreamble();
            if (preamble.Length > 0)
                stream.Write(preamble);
        }

        var bytes = encoding.GetBytes(text);
        stream.Write(bytes);
    }

    private sealed class NativeGroupPolicyObject : IDisposable
    {
        private const uint ClsCtxInprocServer = 0x1;
        private const uint GpoOpenLoadRegistry = 0x00000001;
        private const uint CoInitApartmentThreaded = 0x2;
        private const int RpcEChangedMode = unchecked((int)0x80010106);

        private static readonly Guid Clsid =
            new("EA502722-A23D-11D1-A7D3-0000F87571E3");

        private static readonly Guid Iid =
            new("EA502723-A23D-11D1-A7D3-0000F87571E3");

        private IntPtr _instance;
        private bool _uninitializeCom;

        public NativeGroupPolicyObject(
            GpoInfo gpo,
            string domainDistinguishedName)
        {
            var initializeResult =
                CoInitializeEx(IntPtr.Zero, CoInitApartmentThreaded);

            if (initializeResult >= 0)
                _uninitializeCom = true;
            else if (initializeResult != RpcEChangedMode)
                ThrowIfFailed(initializeResult);

            var clsid = Clsid;
            var iid = Iid;

            ThrowIfFailed(
                CoCreateInstance(
                    ref clsid,
                    IntPtr.Zero,
                    ClsCtxInprocServer,
                    ref iid,
                    out _instance));

            try
            {
                var open = GetMethod<OpenDsgpoDelegate>(4);
                var ldapPath =
                    DomainConnectionState.BuildLdapPath(
                        $"CN={gpo.Id:B},CN=Policies,CN=System,{domainDistinguishedName}");

                ThrowIfFailed(
                    open(
                        _instance,
                        ldapPath,
                        GpoOpenLoadRegistry));
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public void Save(
            bool machine,
            bool add,
            ref Guid extensionGuid,
            ref Guid toolGuid)
        {
            var save = GetMethod<SaveDelegate>(7);
            ThrowIfFailed(
                save(
                    _instance,
                    machine,
                    add,
                    ref extensionGuid,
                    ref toolGuid));
        }

        private T GetMethod<T>(int slot)
            where T : Delegate
        {
            var vtable = Marshal.ReadIntPtr(_instance);
            var address = Marshal.ReadIntPtr(vtable, checked(slot * IntPtr.Size));
            return Marshal.GetDelegateForFunctionPointer<T>(address);
        }

        public void Dispose()
        {
            if (_instance != IntPtr.Zero)
            {
                Marshal.Release(_instance);
                _instance = IntPtr.Zero;
            }

            if (_uninitializeCom)
            {
                CoUninitialize();
                _uninitializeCom = false;
            }
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode)]
    private delegate int OpenDsgpoDelegate(
        IntPtr instance,
        [MarshalAs(UnmanagedType.LPWStr)] string path,
        uint flags);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int SaveDelegate(
        IntPtr instance,
        [MarshalAs(UnmanagedType.Bool)] bool machine,
        [MarshalAs(UnmanagedType.Bool)] bool add,
        ref Guid extensionGuid,
        ref Guid toolGuid);

    [DllImport("ole32.dll")]
    private static extern int CoInitializeEx(IntPtr reserved, uint coInit);

    [DllImport("ole32.dll")]
    private static extern void CoUninitialize();

    [DllImport("ole32.dll")]
    private static extern int CoCreateInstance(
        ref Guid clsid,
        IntPtr outer,
        uint context,
        ref Guid iid,
        out IntPtr instance);

    private static void ThrowIfFailed(int hresult)
    {
        if (hresult < 0)
            Marshal.ThrowExceptionForHR(hresult);
    }

    private sealed class MutableIniEntry
    {
        public string CommandLine { get; set; } = string.Empty;
        public string Parameters { get; set; } = string.Empty;
    }

    private sealed record IniScriptEntry(
        string EventName,
        int Order,
        string CommandLine,
        string Parameters);
}

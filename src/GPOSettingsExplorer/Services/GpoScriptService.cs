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
        CancellationToken cancellationToken = default,
        bool forceRefresh = false)
    {
        var gpoArray =
            gpos.ToArray();

        var result =
            new List<GpoScriptInfo>();

        for (var gpoIndex = 0;
             gpoIndex < gpoArray.Length;
             gpoIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var gpo =
                gpoArray[gpoIndex];

            if (!forceRefresh &&
                GpoScriptCacheService.TryLoad(
                    gpo,
                    out var cached))
            {
                progress?.Report(
                    $"Scripts cache {gpoIndex + 1}/{gpoArray.Length}: {gpo.DisplayName}");

                result.AddRange(
                    cached);

                continue;
            }

            progress?.Report(
                $"Scanning GPO scripts {gpoIndex + 1}/{gpoArray.Length}: {gpo.DisplayName}");

            var perGpo =
                new List<GpoScriptInfo>();

            foreach (var scope in new[]
                     {
                         "Computer",
                         "User"
                     })
            {
                ScanScope(
                    gpo,
                    scope,
                    perGpo,
                    cancellationToken);
            }

            var snapshot =
                perGpo
                    .OrderBy(
                        item =>
                            item.Scope,
                        StringComparer.OrdinalIgnoreCase)
                    .ThenBy(
                        item =>
                            item.EventName,
                        StringComparer.OrdinalIgnoreCase)
                    .ThenBy(
                        item =>
                            item.Order)
                    .ThenBy(
                        item =>
                            item.FileName,
                        StringComparer.CurrentCultureIgnoreCase)
                    .ToArray();

            GpoScriptCacheService.Save(
                gpo,
                snapshot);

            result.AddRange(
                snapshot);
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
                    item.EventName,
                StringComparer.OrdinalIgnoreCase)
            .ThenBy(
                item =>
                    item.Order)
            .ThenBy(
                item =>
                    item.FileName,
                StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    public IReadOnlyList<GpoScriptSearchResult> SearchContent(
        IEnumerable<GpoScriptInfo> scripts,
        string query,
        CancellationToken cancellationToken = default,
        GpoScriptSearchMode mode = GpoScriptSearchMode.Both)
    {
        if (string.IsNullOrWhiteSpace(query))
            return Array.Empty<GpoScriptSearchResult>();

        var needle = query.Trim();
        var includeMetadata = mode != GpoScriptSearchMode.ContentOnly;
        var includeContent = mode != GpoScriptSearchMode.FileNamesAndPaths;

        var byPath = scripts
            .GroupBy(script => script.FullPath, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.ToArray())
            .ToArray();

        var result = new List<GpoScriptSearchResult>();

        if (includeMetadata)
        {
            // Search every file name/path individually. Grouping by content hash
            // here would report the wrong file if two differently named scripts
            // happen to contain identical bytes.
            // This mode never needs to read file contents over SYSVOL.
            foreach (var pathGroup in byPath)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var matched = pathGroup.FirstOrDefault(script =>
                    script.FileName.Contains(needle, StringComparison.CurrentCultureIgnoreCase) ||
                    script.Parameters.Contains(needle, StringComparison.CurrentCultureIgnoreCase) ||
                    script.FullPath.Contains(needle, StringComparison.CurrentCultureIgnoreCase));
                if (matched is null)
                    continue;

                result.Add(new GpoScriptSearchResult
                {
                    Scripts = pathGroup,
                    Identity = "METADATA|" + matched.FullPath,
                    LineNumber = 0,
                    MatchType = GetMetadataMatchType(matched, needle),
                    LineText = BuildMetadataMatchText(matched, needle)
                });
            }
        }

        if (includeContent)
        {
            var contentGroups = new Dictionary<string, List<GpoScriptInfo>>(StringComparer.OrdinalIgnoreCase);
            var documents = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var pathGroup in byPath)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var representative = pathGroup[0];
                if (!representative.Exists || !IsSupportedScriptFile(representative.FullPath))
                    continue;

                try
                {
                    var bytes = File.ReadAllBytes(representative.FullPath);
                    var hash = Convert.ToHexString(SHA256.HashData(bytes));
                    if (!contentGroups.TryGetValue(hash, out var copies))
                    {
                        copies = new List<GpoScriptInfo>();
                        contentGroups[hash] = copies;
                    }
                    copies.AddRange(pathGroup);
                    if (!documents.ContainsKey(hash))
                    {
                        var (encoding, bomLength, _) = DetectEncoding(bytes, representative.FullPath);
                        documents[hash] = encoding.GetString(bytes, bomLength, bytes.Length - bomLength);
                    }
                }
                catch (IOException)
                {
                    // A temporarily inaccessible remote SYSVOL file is not a match.
                    continue;
                }
                catch (UnauthorizedAccessException)
                {
                    continue;
                }
            }

            foreach (var pair in contentGroups)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!documents.TryGetValue(pair.Key, out var text) || text.Length == 0)
                    continue;

                var copies = pair.Value
                    .OrderBy(item => item.GpoName, StringComparer.CurrentCultureIgnoreCase)
                    .ThenBy(item => item.Scope, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(item => item.EventName, StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal)
                    .Replace("\r", "\n", StringComparison.Ordinal)
                    .Split('\n');

                for (var index = 0; index < lines.Length; index++)
                {
                    if (!lines[index].Contains(needle, StringComparison.CurrentCultureIgnoreCase))
                        continue;

                    result.Add(new GpoScriptSearchResult
                    {
                        Scripts = copies,
                        Identity = pair.Key,
                        LineNumber = index + 1,
                        MatchType = "Content",
                        LineText = lines[index].Trim()
                    });
                }
            }
        }

        return result
            .OrderBy(item => item.FileName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(item => item.LineNumber)
            .ThenBy(item => item.LineText, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    private static string GetMetadataMatchType(
        GpoScriptInfo script,
        string needle)
    {
        if (script.FileName.Contains(needle, StringComparison.OrdinalIgnoreCase))
            return "File name";
        if (script.Parameters.Contains(needle, StringComparison.OrdinalIgnoreCase))
            return "Parameters";
        return "Path";
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

    public GpoScriptDocument ReadDocument(string path, int? forcedCodePage = null)
    {
        var bytes = File.ReadAllBytes(path);
        var (encoding, bomLength, emitBom) = DetectEncoding(bytes, path);
        var codePage = forcedCodePage ?? encoding.CodePage;
        var text = ScriptEncodingService.Strict(codePage)
            .GetString(bytes, bomLength, bytes.Length - bomLength);
        var newLine = ScriptEncodingService.DetectNewline(text);

        return new GpoScriptDocument
        {
            Text = text,
            CodePage = codePage,
            OriginalCodePage = codePage,
            EmitBom = emitBom,
            OriginalEmitBom = emitBom,
            OriginalSha256 = Convert.ToHexString(SHA256.HashData(bytes)),
            OriginalText = text,
            NewLine = newLine,
            OriginalNewLine = newLine
        };
    }

    public void SaveDocument(
        GpoInfo gpo,
        string domainDistinguishedName,
        GpoScriptInfo script,
        GpoScriptDocument document)
    {
        EditingGuard.EnsureEnabled(
            "Edit GPO script");
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

        // Strict encoder: reject lossy conversions of Cyrillic and Hebrew.
        if (!ScriptEncodingService.CanRoundTrip(document.Text, document.CodePage,
                out var conversionError))
            throw new InvalidOperationException(
                "Cannot encode the script without replacing characters: " + conversionError);

        if (document.EmitBom && !ScriptEncodingService.SupportsBom(document.CodePage))
            throw new InvalidOperationException("BOM is unavailable for this code page.");

        var encoding = ScriptEncodingService.Strict(document.CodePage);

        var original =
            File.ReadAllBytes(
                fullPath);

        if (!string.IsNullOrEmpty(document.OriginalSha256) &&
            !Convert.ToHexString(SHA256.HashData(original)).Equals(
                document.OriginalSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new IOException("The script changed in SYSVOL after it was opened. Reload it before saving to avoid overwriting someone else's changes.");
        }

        ChangePreviewGuard.Confirm(
            new ChangePreviewRequest(
                "Save GPO script",
                $"{gpo.DisplayName} | {script.Scope} {script.EventName} | {script.FileName}",
                ReadText(
                    fullPath),
                document.Text,
                $"SYSVOL path: {fullPath}; code page {document.OriginalCodePage} -> {document.CodePage};" +
                $" BOM {document.OriginalEmitBom} -> {document.EmitBom};" +
                $" EOL {ScriptEncodingService.DisplayLineEnding(document.OriginalNewLine)} -> " +
                ScriptEncodingService.DisplayLineEnding(document.NewLine),
                "Save"));

        var temp =
            fullPath +
            ".gposes-" +
            Guid.NewGuid().ToString("N") +
            ".tmp";

        var stage = "creating staged file";
        var overwritten = false;
        var expectedBytes = Array.Empty<byte>();

        try
        {
            var textToWrite = document.Text.Equals(
                    document.OriginalText, StringComparison.Ordinal) &&
                document.NewLine == document.OriginalNewLine
                    ? document.Text
                    : ScriptEncodingService.NormalizeNewlines(document.Text, document.NewLine);

            var bytesToWrite = ScriptEncodingService.Encode(
                textToWrite, document.CodePage, document.EmitBom);
            using (var staged = new FileStream(
                temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                staged.Write(bytesToWrite);

            expectedBytes = File.ReadAllBytes(temp);
            if (expectedBytes.AsSpan().SequenceEqual(original))
            {
                GpoScriptCacheService.Invalidate(gpo);
                return;
            }

            stage = "checking SYSVOL source version";
            var latest = File.ReadAllBytes(fullPath);
            if (!latest.AsSpan().SequenceEqual(original))
                throw new IOException("The source script was modified in SYSVOL while this edit was in progress. Reload it before saving.");

            stage = "writing script to SYSVOL";
            // Copy can partially overwrite a file before raising an IOException.
            overwritten = true;
            File.Copy(temp, fullPath, overwrite: true);

            stage = "verifying script content in SYSVOL";
            var written = File.ReadAllBytes(fullPath);
            if (!written.AsSpan().SequenceEqual(expectedBytes))
                throw new IOException("Post-save byte verification failed: SYSVOL content differs from the edited script.");

            stage = "committing the GPO scripts extension";
            using var policy =
                new NativeGroupPolicyObject(
                    gpo,
                    domainDistinguishedName);

            var extensionGuid = ScriptsExtensionGuid;
            var toolGuid =
                script.Scope.Equals("User", StringComparison.OrdinalIgnoreCase)
                    ? UserScriptsToolGuid
                    : MachineScriptsToolGuid;

            policy.Save(
                machine: !script.Scope.Equals("User", StringComparison.OrdinalIgnoreCase),
                add: true,
                ref extensionGuid,
                ref toolGuid);

            stage = "verifying saved script after GPO commit";
            var finalBytes = File.ReadAllBytes(fullPath);
            if (!finalBytes.AsSpan().SequenceEqual(expectedBytes))
                throw new IOException("SYSVOL content changed after the GPO commit.");

            GpoScriptCacheService.Invalidate(gpo);
        }
        catch (Exception ex)
        {
            string rollback;
            if (overwritten)
            {
                try
                {
                    File.WriteAllBytes(fullPath, original);
                    rollback = File.ReadAllBytes(fullPath).AsSpan().SequenceEqual(original)
                        ? "Original script restored."
                        : "WARNING: original script restoration was not verified.";
                }
                catch (Exception restoreEx)
                {
                    rollback = "WARNING: original script could not be restored: " + restoreEx.Message;
                }
            }
            else
            {
                rollback = "Original script was not overwritten.";
            }

            throw new IOException(
                $"Cannot save '{script.FileName}' at stage '{stage}'. {rollback} " +
                $"Path: {fullPath}. Original error: {ex.Message}", ex);
        }
        finally
        {
            try { File.Delete(temp); }
            catch { /* Preserve the primary save error. */ }
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
                    GpoComputerEnabled = gpo.ComputerEnabled,
                    GpoUserEnabled = gpo.UserEnabled,
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
                GpoComputerEnabled = gpo.ComputerEnabled,
                GpoUserEnabled = gpo.UserEnabled,
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
        var (encoding, bomLength, _) = DetectEncoding(bytes, path);
        return encoding.GetString(bytes, bomLength, bytes.Length - bomLength);
    }

    private static (Encoding Encoding, int BomLength, bool EmitBom) DetectEncoding(
        byte[] bytes,
        string? path = null)
    {
        var (bomCodePage, bomLength) = ScriptEncodingService.DetectBom(bytes);
        if (bomCodePage != 0)
            return (ScriptEncodingService.Strict(bomCodePage), bomLength, true);

        var sample = bytes.Take(Math.Min(bytes.Length, 256)).ToArray();
        var oddZeros = sample.Where((value, index) => index % 2 == 1 && value == 0).Count();
        if (oddZeros > 12)
            return (new UnicodeEncoding(false, false), 0, false);

        // An invalid UTF-8 byte stream is usually a legacy Windows ANSI script.
        // Detect strictly; silently replacing undecodable bytes loses BAT/CMD text.
        var validUtf8 = true;
        try
        {
            _ = new UTF8Encoding(false, true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            validUtf8 = false;
        }

        var ext = Path.GetExtension(path ?? string.Empty);
        var batchScript = ext.Equals(".bat", StringComparison.OrdinalIgnoreCase) ||
                          ext.Equals(".cmd", StringComparison.OrdinalIgnoreCase);

        if (!validUtf8 || (batchScript && bytes.All(value => value <= 0x7F)))
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return (Encoding.GetEncoding(checked((int)GetACP())), 0, false);
        }

        return (new UTF8Encoding(false), 0, false);
    }

    private static string NormalizeLineEndings(string text, string newLine)
    {
        var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal);
        return newLine == "\n"
            ? normalized
            : normalized.Replace("\n", newLine, StringComparison.Ordinal);
    }

    [DllImport("kernel32.dll")]
    private static extern uint GetACP();

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

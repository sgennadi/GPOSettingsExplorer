using GPOSettingsExplorer.Models;
using GPOSettingsExplorer.Services;

var tests = new (string Name, Action Body)[]
{
    ("Script sanitizer removes Markdown fences", TestScriptSanitizer),
    ("BAT diagnostics detect broken labels", TestBatchSyntaxDiagnostics),
    ("AvalonEdit BAT and PowerShell syntax definitions load", TestScriptHighlighting),
    ("PowerShell syntax parser reports malformed code without running it", TestPowerShellSyntaxDiagnostics),
    ("Script searches distinguish content and file metadata", TestScriptSearchModes),
    ("MMC navigation does not confuse audit and registry with security options", TestMmcNavigationRouting),
    ("MMC policy names must match uniquely and exactly", TestMmcPolicyNameMatcher),
    ("MMC UTF-16 list-view buffers do not leak previous policy tails", TestMmcNativeTextBuffer),
    ("Domain connection pins LDAP and SYSVOL", TestDomainConnectionPaths),
    ("DPAPI current-user round trip", TestDpapiRoundTrip),
    ("GPP XML cache refreshes after file change", TestGppXmlCache),
    ("Script inventory cache invalidates on GPO modification", TestScriptCache),
    ("Automatic update checks respect 24-hour and retry windows", TestUpdateCheckSchedule),
    ("Blocked GitHub socket is treated as expected connectivity failure", TestBlockedUpdateConnectivity),
    ("Semantic XML diff ignores report timestamps and finds setting changes", TestSemanticXmlDiff)
};

var failures = new List<string>();

foreach (var test in tests)
{
    try
    {
        test.Body();
        Console.WriteLine($"PASS  {test.Name}");
    }
    catch (Exception ex)
    {
        failures.Add($"{test.Name}: {ex}");
        Console.Error.WriteLine($"FAIL  {test.Name}: {ex.Message}");
    }
}

if (failures.Count == 0)
{
    Console.WriteLine($"Core tests passed: {tests.Length}.");
    return 0;
}

Console.Error.WriteLine();
Console.Error.WriteLine($"Core tests failed: {failures.Count}/{tests.Length}.");

foreach (var failure in failures)
{
    Console.Error.WriteLine(failure);
}

return 1;

static void TestScriptSanitizer()
{
    var fence =
        new string(
            (char)96,
            3);

    var input =
        fence + "bat\r\n@echo off\r\necho hello\r\n" + fence + "\r\n";

    var result =
        ScriptTextSanitizer.StripOuterMarkdownFence(
            input,
            out var removed);

    Assert(
        removed,
        "Expected the outer Markdown fence to be removed.");

    Assert(
        !result.Contains(
            fence,
            StringComparison.Ordinal),
        "Sanitized script still contains a Markdown fence.");

    Assert(
        result.Contains(
            "@echo off",
            StringComparison.Ordinal),
        "Script contents were lost.");
}

static void TestScriptSearchModes()
{
    var root = Path.Combine(Path.GetTempPath(), "GPOSE-search-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    try
    {
        var nameMatch = Path.Combine(root, "sample.bat");
        var bodyMatch = Path.Combine(root, "content.cmd");
        File.WriteAllText(nameMatch, "@echo off\r\necho hello\r\n");
        File.WriteAllText(bodyMatch, "@echo off\r\necho bat appears in the script body\r\n");

        var scripts = new[] { nameMatch, bodyMatch }
            .Select(path => new GpoScriptInfo
            {
                GpoId = Guid.NewGuid(),
                GpoName = "Test GPO",
                DomainName = "example.test",
                Scope = "Computer",
                FileName = Path.GetFileName(path),
                FullPath = path,
                Exists = true
            })
            .ToArray();
        var service = new GpoScriptService();

        var contents = service.SearchContent(
            scripts, "bat", mode: GpoScriptSearchMode.ContentOnly);
        Assert(contents.Count == 1 &&
               contents[0].FileName == "content.cmd" &&
               contents[0].MatchType == "Content" &&
               contents[0].LineNumber > 0,
            "Content-only search matched a file extension or missed a body match.");

        var names = service.SearchContent(
            scripts, "bat", mode: GpoScriptSearchMode.FileNamesAndPaths);
        Assert(names.Count == 1 &&
               names[0].FileName == "sample.bat" &&
               names[0].MatchType == "File name" &&
               names[0].LineNumber == 0,
            "File name/path search included body-only matches.");

        var combined = service.SearchContent(
            scripts, "bat", mode: GpoScriptSearchMode.Both);
        Assert(combined.Count == 2 &&
               combined.Any(x => x.MatchType == "Content") &&
               combined.Any(x => x.MatchType == "File name"),
            "Combined search did not return both kinds of matches.");

        var edited = service.ReadDocument(nameMatch);
        Assert(!string.IsNullOrWhiteSpace(edited.OriginalSha256),
            "Script editor did not capture the original file checksum.");
        Assert(edited.NewLine == "\r\n",
            "The script's CRLF line endings were not detected correctly.");

        var otherNameSameBody = Path.Combine(root, "other-script.cmd");
        File.Copy(nameMatch, otherNameSameBody);
        var alternate = new GpoScriptInfo
        {
            GpoId = Guid.NewGuid(),
            GpoName = "A first alphabetically",
            DomainName = "example.test",
            Scope = "Computer",
            FileName = "other-script.cmd",
            FullPath = otherNameSameBody,
            Exists = true
        };
        var collidingContent = scripts.Append(alternate).ToArray();
        var fileMatches = service.SearchContent(
            collidingContent, "bat", mode: GpoScriptSearchMode.FileNamesAndPaths);
        Assert(fileMatches.Count == 1 && fileMatches[0].FileName == "sample.bat",
            "Metadata search showed a different filename with identical script contents.");
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static void TestMmcNativeTextBuffer()
{
    const string policy = "Network security: LAN Manager authentication level";
    const string staleTail = " ire n next password change online identities.";
    var actual = System.Text.Encoding.Unicode.GetBytes(policy);
    var buffer = System.Text.Encoding.Unicode.GetBytes(policy + staleTail);

    var decoded = MmcNativeListViewText.DecodeUtf16(buffer, policy.Length);
    Assert(decoded == policy,
        "Native MMC policy-name read contains old buffer contents beyond LVM_GETITEMTEXTW returned length.");
    Assert(decoded.Length == policy.Length,
        "MMC native ListView text reader produced extra characters.");

    var names = Enumerable.Range(0, 100)
        .Select(index => index == 69 ? decoded : $"Other policy {index}")
        .ToArray();
    Assert(MmcPolicyNameMatcher.FindUniqueMatch(names, policy) == 69,
        "MMC Security Options failed to find the target policy at row 69.");
    Assert(MmcNativeListViewText.PolicyColumn(
        policy + " | Send NTLMv2 response only") == policy,
        "MMC fallback incorrectly matched the second (value) column.");
    Assert(MmcNativeListViewText.DecodeUtf16(Array.Empty<byte>(), 0) == string.Empty,
        "Empty remote text should be safe.");
    try
    {
        MmcNativeListViewText.DecodeUtf16(actual, actual.Length);
        throw new InvalidOperationException("MMC text decoding accepted more UTF-16 chars than the supplied buffer.");
    }
    catch (ArgumentOutOfRangeException)
    {
    }
}

static void TestMmcPolicyNameMatcher()
{
    const string target = "Network security: LAN Manager authentication level";
    var rows = new[]
    {
        "Network security: Restrict NTLM: NTLM authentication in this domain",
        target,
        "Network security: Restrict NTLM: Outgoing NTLM traffic to remote servers",
        "Network security: LAN Manager authentication levels",
        "Network security: Force logoff when logon hours expire"
    };

    Assert(MmcPolicyNameMatcher.FindUniqueMatch(rows, target) == 1,
        "Native MMC lookup missed the LAN Manager policy among similar NTLM policies.");

    Assert(MmcPolicyNameMatcher.FindUniqueMatch(rows, "Network security: LAN Manager") == -1,
        "Unsafe partial prefix unexpectedly matched a different policy.");

    Assert(MmcPolicyNameMatcher.FindUniqueMatch(new[] { target, target }, target) == -1,
        "Ambiguous duplicate policy name should never be opened automatically.");

    Assert(MmcPolicyNameMatcher.Exact("  Network   security: LAN Manager authentication level  ", target),
        "MMC normalization should ignore whitespace differences.");

    Assert(MmcPolicyNameMatcher.FindUniqueMatch(
        new[] { "Network security: LAN Manager authenti..." }, target) == 0,
        "A single explicitly truncated MMC label should be recognized.");

    Assert(MmcPolicyNameMatcher.FindUniqueMatch(
        new[] { "Network security: LAN Manager authenti...", "Network security: LAN Manager authenti..." },
        target) == -1, "Ambiguous truncated MMC rows should never be activated.");

    Assert(MmcPolicyNameMatcher.FindUniqueMatch(
        new[] { "Network security: LAN Manager authentication" }, target) == -1,
        "Non-ellipsis truncation is unsafe and must not count as a verified match.");
}

static void TestMmcNavigationRouting()
{
    var navigator = new GpoEditorNavigatorService();
    var security = new PolicySettingInfo
    {
        Extension = "SecuritySettings",
        Scope = "Computer",
        Category = "Security Settings > Local Policies > Security Options",
        SettingName = "Network security: LAN Manager authentication level"
    };
    Assert(navigator.CanNavigateExactly(security),
        "Named Security Options should permit exact MMC navigation.");
    Assert(GpoEditorNavigatorService.NavigationTarget(security).EndsWith(
        "Local Policies > Security Options", StringComparison.Ordinal),
        "Security Options opened the wrong policy tree.");

    var audit = new PolicySettingInfo
    {
        Extension = "AuditSettings",
        Scope = "Computer",
        Category = "AuditSettings",
        SettingName = "Audit Setting"
    };
    Assert(!navigator.CanNavigateExactly(audit),
        "A generic Audit Setting must never open an unrelated Security Option.");
    Assert(GpoEditorNavigatorService.NavigationTarget(audit).Contains(
        "Advanced Audit Policy Configuration", StringComparison.Ordinal),
        "Audit settings did not route to Advanced Audit Policy.");

    var registry = new PolicySettingInfo
    {
        Extension = "RegistrySettings",
        Scope = "Computer",
        Category = "Registry > Extra Registry Settings",
        SettingName = "Registry: foo"
    };
    var target = GpoEditorNavigatorService.NavigationTarget(registry);
    Assert(!navigator.CanNavigateExactly(registry) &&
           target.Contains("Administrative Templates", StringComparison.Ordinal) &&
           !target.Contains("Preferences", StringComparison.Ordinal),
        "registry.pol was incorrectly routed to GPP Preferences.");
}

static void TestBatchSyntaxDiagnostics()
{
    var source = "goto :absent\r\n:present\r\ngoto present\r\n";
    var messages = ScriptSyntaxService.CheckBatch(source);
    Assert(messages.Count(x => x.Message.Contains("absent", StringComparison.OrdinalIgnoreCase)) == 1,
        "Unresolved BAT label was not detected.");
    Assert(!messages.Any(x => x.Message.Contains("present", StringComparison.OrdinalIgnoreCase)),
        "A defined BAT label was flagged as missing.");

    var markdown = new string((char)96, 3) + "bat\r\n@echo off\r\n" +
        new string((char)96, 3);
    Assert(ScriptSyntaxService.CheckBatch(markdown).Count(
        x => x.Severity == ScriptDiagnosticSeverity.Error) == 2,
        "Markdown fence syntax errors were not detected.");
}

static void TestScriptHighlighting()
{
    Assert(ScriptSyntaxHighlightingService.ForFile("deploy.bat") is not null,
        "Batch syntax highlighter could not be loaded.");
    Assert(ScriptSyntaxHighlightingService.ForFile("startup.cmd") is not null,
        "CMD syntax highlighter could not be loaded.");
    Assert(ScriptSyntaxHighlightingService.ForFile("policy.ps1") is not null,
        "PowerShell syntax highlighter could not be loaded.");
}

static void TestPowerShellSyntaxDiagnostics()
{
    const string invalid = "if ($true) { Write-Output 'missing brace' ";
    var diagnostics = ScriptSyntaxService.Analyze(invalid, "startup.ps1");
    Assert(diagnostics.Any(x => x.Severity == ScriptDiagnosticSeverity.Error),
        "PowerShell AST parser failed to detect a missing closing brace.");

    // Parser.ParseFile must never execute script statements; only parse source text.
    var valid = ScriptSyntaxService.Analyze("Write-Output 'safe'\r\n", "startup.ps1");
    Assert(!valid.Any(x => x.Severity == ScriptDiagnosticSeverity.Error),
        "The PowerShell parser rejected valid source text.");
}

static void TestDomainConnectionPaths()
{
    DomainConnectionState.SetProfile(
        DomainConnectionProfile.CurrentSession(
            "example.test",
            "dc01.example.test"));

    DomainConnectionState.SetContext(
        new DomainContext(
            "example.test",
            "DC=example,DC=test",
            "CN=Configuration,DC=example,DC=test",
            "dc01.example.test"));

    var ldap =
        DomainConnectionState.BuildLdapPath(
            "CN=Policies,CN=System,DC=example,DC=test");

    var sysvol =
        DomainConnectionState.BuildSysvolRoot(
            "example.test");

    Assert(
        ldap.Equals(
            "LDAP://dc01.example.test/CN=Policies,CN=System,DC=example,DC=test",
            StringComparison.OrdinalIgnoreCase),
        $"Unexpected LDAP path: {ldap}");

    Assert(
        sysvol.Equals(
            @"\\dc01.example.test\SYSVOL\example.test",
            StringComparison.OrdinalIgnoreCase),
        $"Unexpected SYSVOL path: {sysvol}");
}

static void TestDpapiRoundTrip()
{
    const string value =
        "CoreTests-secret-12345";

    var encrypted =
        DpapiCredentialProtector.Protect(
            value,
            CredentialPersistenceScope.CurrentUser);

    Assert(
        !string.IsNullOrWhiteSpace(
            encrypted),
        "DPAPI returned an empty protected value.");

    Assert(
        !encrypted.Contains(
            value,
            StringComparison.Ordinal),
        "Protected value contains the plaintext.");

    var decrypted =
        DpapiCredentialProtector.Unprotect(
            encrypted,
            CredentialPersistenceScope.CurrentUser);

    Assert(
        decrypted.Equals(
            value,
            StringComparison.Ordinal),
        "DPAPI round trip returned a different value.");
}

static void TestGppXmlCache()
{
    var directory =
        Path.Combine(
            Path.GetTempPath(),
            "GPOSettingsExplorer-CoreTests-" +
            Guid.NewGuid().ToString("N"));

    Directory.CreateDirectory(
        directory);

    var path =
        Path.Combine(
            directory,
            "sample.xml");

    try
    {
        File.WriteAllText(
            path,
            "<Root><Item value=\"one\" /></Root>");

        var first =
            GppXmlCacheService.Load(
                path);

        Assert(
            first.Root?
                .Element("Item")?
                .Attribute("value")?
                .Value ==
            "one",
            "Initial cached XML value is incorrect.");

        File.WriteAllText(
            path,
            "<Root><Item value=\"two-longer\" /></Root>");

        File.SetLastWriteTimeUtc(
            path,
            DateTime.UtcNow.AddSeconds(
                2));

        var second =
            GppXmlCacheService.Load(
                path);

        Assert(
            second.Root?
                .Element("Item")?
                .Attribute("value")?
                .Value ==
            "two-longer",
            "XML cache returned stale content.");

        GppXmlCacheService.Invalidate(
            path);
    }
    finally
    {
        try
        {
            Directory.Delete(
                directory,
                recursive:
                    true);
        }
        catch
        {
        }
    }
}

static void TestScriptCache()
{
    DomainConnectionState.SetProfile(
        DomainConnectionProfile.CurrentSession(
            "example.test",
            "dc01.example.test"));

    DomainConnectionState.SetContext(
        new DomainContext(
            "example.test",
            "DC=example,DC=test",
            "CN=Configuration,DC=example,DC=test",
            "dc01.example.test"));

    var time =
        DateTime.Now;

    var gpo =
        new GpoInfo
        {
            Id =
                Guid.NewGuid(),
            DisplayName =
                "Core Test GPO",
            DomainName =
                "example.test",
            ModificationTime =
                time
        };

    var scripts =
        new[]
        {
            new GpoScriptInfo
            {
                GpoId =
                    gpo.Id,
                GpoName =
                    gpo.DisplayName,
                DomainName =
                    gpo.DomainName,
                Scope =
                    "Computer",
                EventName =
                    "Startup",
                FileName =
                    "test.cmd",
                FullPath =
                    @"\\dc01.example.test\SYSVOL\example.test\test.cmd",
                Exists =
                    true
            }
        };

    GpoScriptCacheService.Save(
        gpo,
        scripts);

    Assert(
        GpoScriptCacheService.TryLoad(
            gpo,
            out var cached),
        "Expected a script cache hit.");

    Assert(
        cached.Count ==
        1,
        "Unexpected cached script count.");

    var changed =
        new GpoInfo
        {
            Id =
                gpo.Id,
            DisplayName =
                gpo.DisplayName,
            DomainName =
                gpo.DomainName,
            ModificationTime =
                time.AddSeconds(
                    10)
        };

    Assert(
        !GpoScriptCacheService.TryLoad(
            changed,
            out _),
        "Changed GPO metadata should invalidate the script cache.");

    GpoScriptCacheService.Invalidate(
        gpo);
}

static void TestUpdateCheckSchedule()
{
    var now =
        new DateTime(
            2026,
            10,
            8,
            6,
            0,
            0,
            DateTimeKind.Utc);

    var never =
        new UpdateCheckState(
            DateTime.MinValue,
            DateTime.MinValue,
            string.Empty);

    Assert(
        UpdateCheckStateService.ShouldCheckAutomatically(
            never,
            now),
        "A machine that never checked should check immediately.");

    var recentSuccess =
        new UpdateCheckState(
            now.AddHours(
                -1),
            now.AddHours(
                -1),
            "v0.3.2");

    Assert(
        !UpdateCheckStateService.ShouldCheckAutomatically(
            recentSuccess,
            now),
        "A successful check within 24 hours should not repeat.");

    var staleSuccess =
        new UpdateCheckState(
            now.AddHours(
                -25),
            now.AddHours(
                -25),
            "v0.3.2");

    Assert(
        UpdateCheckStateService.ShouldCheckAutomatically(
            staleSuccess,
            now),
        "A successful check older than 24 hours should repeat.");

    var recentFailureAfterStaleSuccess =
        new UpdateCheckState(
            now.AddMinutes(
                -30),
            now.AddHours(
                -25),
            "v0.3.2");

    Assert(
        !UpdateCheckStateService.ShouldCheckAutomatically(
            recentFailureAfterStaleSuccess,
            now),
        "A recent failed retry should be throttled for two hours.");

    var oldFailureAfterStaleSuccess =
        new UpdateCheckState(
            now.AddHours(
                -3),
            now.AddHours(
                -25),
            "v0.3.2");

    Assert(
        UpdateCheckStateService.ShouldCheckAutomatically(
            oldFailureAfterStaleSuccess,
            now),
        "A failed retry older than two hours should be attempted again.");
}

static void TestBlockedUpdateConnectivity()
{
    var exception =
        new System.Net.Http.HttpRequestException(
            "blocked",
            new System.Net.Sockets.SocketException(
                10013));

    Assert(
        UpdateService.IsExpectedConnectivityFailure(
            exception),
        "Socket 10013 should be handled as an expected updater connectivity restriction.");

    var message =
        UpdateService.BuildConnectivityFailureMessage(
            exception);

    Assert(
        message.Contains(
            "10013",
            StringComparison.Ordinal),
        "The connectivity explanation should identify socket error 10013.");
}

static void TestSemanticXmlDiff()
{
    const string left =
        "<GPO><GeneratedTime>2026-10-01</GeneratedTime><Policy name=\"A\"><Value>1</Value></Policy></GPO>";

    const string right =
        "<GPO><GeneratedTime>2026-10-08</GeneratedTime><Policy name=\"A\"><Value>2</Value></Policy></GPO>";

    var rows =
        new SemanticXmlDiffService()
            .CompareText(
                left,
                right);

    Assert(
        rows.Any(
            row =>
                row.IsDifferent &&
                row.LeftValue ==
                "1" &&
                row.RightValue ==
                "2"),
        "Expected semantic diff to report the changed policy value.");

    Assert(
        !rows.Any(
            row =>
                row.Path.Contains(
                    "GeneratedTime",
                    StringComparison.OrdinalIgnoreCase)),
        "Generated report timestamps should not appear in semantic diff output.");
}

static void Assert(
    bool condition,
    string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(
            message);
    }
}

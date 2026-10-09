using GPOSettingsExplorer.Models;
using GPOSettingsExplorer.Services;

var tests = new (string Name, Action Body)[]
{
    ("Script sanitizer removes Markdown fences", TestScriptSanitizer),
    ("BAT diagnostics detect broken labels", TestBatchSyntaxDiagnostics),
    ("AvalonEdit BAT and PowerShell syntax definitions load", TestScriptHighlighting),
    ("PowerShell syntax parser reports malformed code without running it", TestPowerShellSyntaxDiagnostics),
    ("Script searches distinguish content and file metadata", TestScriptSearchModes),
    ("Identical GPO script copies resolve without implicit mass edit", TestGpoScriptCopyResolution),
    ("Public Key, NRPT and MSI metadata route to the right MMC sections", TestExtendedMmcSectionRouting),
    ("MMC navigation does not confuse audit and registry with security options", TestMmcNavigationRouting),
    ("MMC policy names must match uniquely and exactly", TestMmcPolicyNameMatcher),
    ("Domain connection pins LDAP and SYSVOL", TestDomainConnectionPaths),
    ("DPAPI current-user round trip", TestDpapiRoundTrip),
    ("GPP XML cache refreshes after file change", TestGppXmlCache),
    ("Script inventory cache invalidates on GPO modification", TestScriptCache),
    ("Automatic update checks respect 24-hour and retry windows", TestUpdateCheckSchedule),
    ("Diagnostic queue never removes unsent logs, archives only after confirmation", TestDiagnosticQueue),
    ("Public diagnostic reports redact identifiers and omit raw logs by default", TestGitHubDiagnosticsPrivacy),
    ("Blocked GitHub socket is treated as expected connectivity failure", TestBlockedUpdateConnectivity),
    ("Script encodings preserve Cyrillic and Hebrew and reject loss", TestScriptEncodingRoundTrips),
    ("Script audit records verified hashes and safe before/after metadata", TestScriptAuditEvidence),
    ("Unicode script safety warns on bidi and invisible special characters", TestScriptUnicodeSafety),
    ("GPMC Link Order reverses gPLink storage order", TestGpoLinkOrderPrecedence),
    ("GPO conflicts distinguish duplicate values and linked mismatches", TestGpoConflictAnalysis),
    ("RSoP verification rejects missing, excluded and nested GPOs", TestRsopVerificationEvidence),
    ("MMC inventory verifies source, path, scope and incomplete coverage", TestMmcFullInventoryReconciliation),
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

static void TestDiagnosticQueue()
{
    var root = Path.Combine(Path.GetTempPath(), "GPOSE-queue-" +
        Guid.NewGuid().ToString("N"));
    var errors = Path.Combine(root, "Errors");
    var audit = Path.Combine(root, "Audit");
    var state = Path.Combine(root, "State");
    Directory.CreateDirectory(errors);
    Directory.CreateDirectory(audit);
    try
    {
        for (var i = 0; i < 3; i++)
        {
            var name = Path.Combine(errors, $"error-{20261009 + i}-000000.log");
            File.WriteAllText(name,
                $"Context: Test {i}\nVersion: 0.4.7.0\nSystem.InvalidOperationException: sample");
            File.SetLastWriteTimeUtc(name, DateTime.UtcNow.AddMinutes(-4));
        }

        var service = new DiagnosticLogQueueService(errors, audit, state);
        var pending = service.GetPending();
        Assert(pending.Count == 3, "New diagnostics should be pending.");
        Assert(service.ShouldOffer(pending.Count, DateTime.UtcNow),
            "Three unsent logs should be eligible for an online offer.");
        service.MarkOffered(DateTime.UtcNow);
        Assert(!service.ShouldOffer(3, DateTime.UtcNow.AddHours(1)),
            "One hour later the same log prompt should not repeat.");

        Assert(Directory.GetFiles(errors, "*.log").Length == 3,
            "Unsent offline logs should never be deleted.");
        try
        {
            service.MarkSubmitted(pending, "https://untrusted.example/issues/123");
            throw new InvalidOperationException("Non-GitHub acknowledgement unexpectedly cleared logs.");
        }
        catch (ArgumentException)
        {
        }

        Assert(service.GetPending().Count == 3,
            "Unconfirmed issues must not clear the local queue.");
        var issue = "https://github.com/sgennadi/GPOSettingsExplorer/issues/123";
        service.MarkSubmitted(pending, issue);
        Assert(service.GetPending().Count == 0,
            "Confirmed issue must remove matching entries from the pending queue.");
        Assert(Directory.GetFiles(errors, "*.log").Length == 0,
            "Confirmed sent logs should be moved out of the active queue.");
        var archive = Path.Combine(state, "SentArchive");
        Assert(Directory.GetFiles(archive).Length == 3,
            "Confirmed issues must retain fourteen-day local copies.");
        var reopened = new DiagnosticLogQueueService(errors, audit, state);
        Assert(reopened.GetPending().Count == 0,
            "Acknowledgement must survive application restart.");
    }
    finally
    {
        try { Directory.Delete(root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

static void TestGitHubDiagnosticsPrivacy()
{
    var fake = "Context: CN=Student,OU=Clinic,DC=yosh,DC=ac,DC=il\n" +
        @"Path: \\domain-server\SYSVOL\Policies\GPO" + "\n" +
        "IP: 10.1.2.3\n" +
        "User: student@example.com\n" +
        "Token: super-secret-token\n" +
        "SID: S-1-5-21-123-456-789\n";
    var redacted = GitHubDiagnosticReportService.Redact(fake);
    foreach (var secret in new[] {
        "domain-server", "Clinic", "10.1.2.3", "student@example.com",
        "super-secret-token", "S-1-5-21-123-456-789"
    })
        Assert(!redacted.Contains(secret, StringComparison.OrdinalIgnoreCase),
            "Public diagnostic report left a sensitive detail: " + secret);

    var folder = Path.Combine(Path.GetTempPath(), "GPOSE-privacy-" +
        Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(folder);
    try
    {
        var file = Path.Combine(folder, "error-privacy.log");
        File.WriteAllText(file, "Context: Private Policy For Clinic\n" +
            "Version: 0.4.7.0\n" +
            "System.InvalidOperationException: Confidential Value\n" +
            "Password: TopSecret\n");
        var bytes = File.ReadAllBytes(file);
        var item = new PendingDiagnosticLog(file, "Error",
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)),
            bytes.Length, DateTime.UtcNow.AddMinutes(-1));
        var report = GitHubDiagnosticReportService.BuildReport(new[] { item });
        Assert(report.Contains("Diagnostic-Fingerprint:", StringComparison.Ordinal),
            "Reports must be recognized by multi-computer CI.");
        Assert(!report.Contains("Private Policy", StringComparison.Ordinal) &&
               !report.Contains("TopSecret", StringComparison.Ordinal),
            "Default report must not contain private log text.");
    }
    finally
    {
        try { Directory.Delete(folder, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

static void TestScriptEncodingRoundTrips()
{
    var russian = "@echo off\r\nrem Привет, мир\r\n";
    var hebrew = "@echo off\r\nrem שלום עולם\r\n";
    foreach (var (codePage, text) in new[] { (1251, russian), (866, russian),
                                             (1255, hebrew), (862, hebrew),
                                             (65001, russian + hebrew) })
    {
        Assert(ScriptEncodingService.CanRoundTrip(text, codePage, out _),
            $"Code page {codePage} cannot preserve native text.");
        var encoded = ScriptEncodingService.Encode(text, codePage, false);
        Assert(ScriptEncodingService.Decode(encoded, codePage) == text,
            $"Code page {codePage} failed exact round-trip.");
    }

    Assert(!ScriptEncodingService.CanRoundTrip(hebrew, 1251, out _),
        "Windows-1251 must reject lossy Hebrew conversion.");
    Assert(!ScriptEncodingService.CanRoundTrip(russian, 1255, out _),
        "Windows-1255 must reject lossy Cyrillic conversion.");

    foreach (var cp in new[] { 65001, 1200, 1201, 12000, 12001 })
    {
        var bytes = ScriptEncodingService.Encode(hebrew + russian, cp, true);
        var bom = ScriptEncodingService.DetectBom(bytes);
        Assert(bom.CodePage == cp && bom.BomLength == ScriptEncodingService.BomFor(cp).Length,
            $"Unicode code page {cp} BOM detection failed.");
        Assert(ScriptEncodingService.Decode(bytes, cp) == hebrew + russian,
            $"Unicode code page {cp} with BOM did not round-trip.");
    }

    Assert(ScriptEncodingService.HasMixedNewlines("a\r\nb\nc\r"),
        "Mixed line endings must be reported.");
    Assert(ScriptEncodingService.NormalizeNewlines("a\r\nb\nc\r", "\n") == "a\nb\nc\n",
        "DOS / Unix / Mac conversion unexpectedly changed characters.");
    Assert(ScriptEncodingService.DetectNewline("a\r\nb\r\n") == "\r\n",
        "DOS line ending detection failed.");
    Assert(ScriptEncodingService.DetectNewline("a\nb\n") == "\n",
        "Unix line ending detection failed.");
}

static void TestScriptUnicodeSafety()
{
    var script = "Write-Output 'שלום'\r\n$var = \"Привет\"";
    var safe = ScriptTextSafetyService.Analyze(script, "setup.ps1", 65001, true, "\r\n");
    Assert(safe.All(x => x.Severity != ScriptDiagnosticSeverity.Error),
        "Ordinary Hebrew/Cyrillic text must never be flagged as an error.");

    var suspect = "@echo\u00A0off\r\nset\u200B NAME=VALUE\r\n" +
                  "echo\u202E danger\r\nexit\uFEFF /b\r\n";
    var findings = ScriptTextSafetyService.Analyze(suspect, "install.bat",
        65001, false, "\r\n");
    Assert(findings.Any(x => x.Message.Contains("Nonstandard Unicode whitespace",
        StringComparison.Ordinal)), "Non-breaking space must be diagnosed.");
    Assert(findings.Any(x => x.Message.Contains("zero-width", StringComparison.OrdinalIgnoreCase)),
        "Zero-width chars must be diagnosed.");
    Assert(findings.Any(x => x.Message.Contains("bidirectional", StringComparison.OrdinalIgnoreCase)),
        "Bidi overrides must be diagnosed.");
    Assert(findings.Any(x => x.Severity == ScriptDiagnosticSeverity.Error),
        "Hidden BOM/bidi controls must warn strongly.");
    Assert(findings.Any(x => x.Line == 3),
        "Diagnostics must retain source line positions.");
}

static void TestScriptAuditEvidence()
{
    var secret = "credential=PrivateTestSecret";
    var beforeText = "@echo off\r\nset " + secret + "\r\necho before\r\n";
    var afterText = "@echo off\r\nset " + secret + "\r\necho after\r\n";
    var oldBytes = ScriptEncodingService.Encode(beforeText, 65001, false);
    var newBytes = ScriptEncodingService.Encode(afterText, 65001, false);
    var document = new GpoScriptDocument
    {
        OriginalText = beforeText,
        OriginalCodePage = 65001,
        OriginalEmitBom = false,
        CodePage = 65001,
        EmitBom = false,
        Text = afterText
    };

    var evidence = GpoScriptAuditEvidenceService.Create(oldBytes, newBytes, document);
    Assert(evidence.Changed, "Different actual bytes should produce a modified audit result.");
    Assert(evidence.Before.Contains("SHA-256:", StringComparison.Ordinal) &&
           evidence.After.Contains("SHA-256:", StringComparison.Ordinal) &&
           evidence.Before.Contains("Bytes:", StringComparison.Ordinal) &&
           evidence.After.Contains("Bytes:", StringComparison.Ordinal) &&
           evidence.Before != evidence.After,
        "Audit before/after must contain distinct verified file fingerprints.");
    Assert(evidence.ChangeSummary.Contains("Changed text region:", StringComparison.Ordinal),
        "A text edit must identify the affected line range.");
    Assert(!evidence.Before.Contains(secret, StringComparison.Ordinal) &&
           !evidence.After.Contains(secret, StringComparison.Ordinal) &&
           !evidence.ChangeSummary.Contains(secret, StringComparison.Ordinal),
        "Audit records must never include script commands or embedded secrets.");

    var same = GpoScriptAuditEvidenceService.Create(oldBytes, oldBytes, document);
    Assert(!same.Changed &&
           same.ChangeSummary.Contains("No byte change", StringComparison.Ordinal),
        "An unchanged file must not be reported as an actual saved modification.");

    var lfText = beforeText.Replace("\r\n", "\n", StringComparison.Ordinal);
    var lfBytes = ScriptEncodingService.Encode(lfText, 65001, false);
    var newlines = GpoScriptAuditEvidenceService.Create(oldBytes, lfBytes, document);
    Assert(newlines.Changed &&
           newlines.ChangeSummary.Contains("line endings", StringComparison.Ordinal),
        "Only changed EOL markers must not be reported as edited commands.");

    var unicode = ScriptEncodingService.Encode(beforeText, 1200, true);
    var unicodeDocument = new GpoScriptDocument
    {
        OriginalCodePage = 65001,
        CodePage = 1200,
        EmitBom = true
    };
    var formatted = GpoScriptAuditEvidenceService.Create(oldBytes, unicode, unicodeDocument);
    Assert(formatted.Changed &&
           formatted.ChangeSummary.Contains("File format changed", StringComparison.Ordinal),
        "A BOM/encoding-only change should have a meaningful audit explanation.");
}

static void TestGpoLinkOrderPrecedence()
{
    Assert(GpoLinkOrder.FromStorageIndex(3, 0) == 3,
        "Leftmost AD gPLink entry must be lowest precedence.");
    Assert(GpoLinkOrder.FromStorageIndex(3, 2) == 1,
        "Rightmost AD gPLink entry must be Link Order 1.");
    Assert(GpoLinkOrder.InsertionIndex(2, 1) == 2,
        "Link Order 1 must insert as rightmost gPLink entry.");
    Assert(GpoLinkOrder.InsertionIndex(2, 3) == 0,
        "Lowest priority GPO should insert at leftmost index.");
}

static void TestGpoConflictAnalysis()
{
    var a = Guid.NewGuid();
    var b = Guid.NewGuid();
    var common = @"OU=Desktop,DC=example,DC=local";
    var policies = new[]
    {
        new GpoInfo { Id = a, DisplayName = "GPO-A", ComputerEnabled = true },
        new GpoInfo { Id = b, DisplayName = "GPO-B", ComputerEnabled = true }
    };
    var links = new[]
    {
        new GpoLinkInfo { GpoId = a, GpoName = "GPO-A", TargetDn = common,
            TargetName = "Desktop", TargetType = "OU", Enabled = true, Order = 1 },
        new GpoLinkInfo { GpoId = b, GpoName = "GPO-B", TargetDn = common,
            TargetName = "Desktop", TargetType = "OU", Enabled = true, Order = 2 }
    };
    var settings = new[]
    {
        new PolicySettingInfo { GpoId = a, GpoName = "GPO-A", Scope = "Computer",
            RegistryKey = "Software\\Example", RegistryValue = "Setting1",
            SettingName = "Policy 1", State = "Enabled", Value = "1" },
        new PolicySettingInfo { GpoId = b, GpoName = "GPO-B", Scope = "Computer",
            RegistryKey = "Software\\Example", RegistryValue = "Setting1",
            SettingName = "Policy 1", State = "Enabled", Value = "1" },
        new PolicySettingInfo { GpoId = a, GpoName = "GPO-A", Scope = "Computer",
            RegistryKey = "Software\\Example", RegistryValue = "Setting2",
            SettingName = "Policy 2", State = "Enabled", Value = "1" },
        new PolicySettingInfo { GpoId = b, GpoName = "GPO-B", Scope = "Computer",
            RegistryKey = "Software\\Example", RegistryValue = "Setting2",
            SettingName = "Policy 2", State = "Enabled", Value = "2" }
    };
    var findings = GpoConflictAnalysisService.Analyze(settings, links, policies);
    Assert(findings.Count == 2, "Both matching and differing values must be listed.");
    Assert(findings.Count(x => x.Kind == "Duplicate") == 1,
        "Matching configured values should be marked as duplication.");
    Assert(findings.Count(x => x.Kind == "Different values") == 1,
        "Different configured values should be marked as a potential conflict.");
    Assert(findings.All(x => x.IsPotentialOverlap &&
        x.OverlapStatus.StartsWith("Shared target", StringComparison.Ordinal)),
        "Both links in the same OU should produce evidence of potential scope overlap.");
    Assert(findings.Any(x => x.Recommendation.Contains("back up both", StringComparison.OrdinalIgnoreCase)),
        "The deduplication plan must recommend independent backups.");

    var disabled = policies.Select(g => new GpoInfo
    {
        Id = g.Id, DisplayName = g.DisplayName,
        ComputerEnabled = g.Id == a
    }).ToArray();
    var inactive = GpoConflictAnalysisService.Analyze(settings, links, disabled);
    Assert(inactive.All(x => !x.IsPotentialOverlap &&
        x.OverlapStatus == "Not simultaneously active"),
        "Disabled GPO configuration scope must not count as an active overlap.");
}

static void TestRsopVerificationEvidence()
{
    var a = Guid.NewGuid();
    var b = Guid.NewGuid();

    static string Gpo(Guid id, bool allowed = true) =>
        $"<GPO><ID>{{{id:D}}}</ID><Enabled>true</Enabled><IsValid>true</IsValid>" +
        $"<FilterAllowed>{allowed.ToString().ToLowerInvariant()}</FilterAllowed>" +
        "<AccessDenied>false</AccessDenied></GPO>";

    var xml = "<Rsop xmlns='urn:example:rsop'><ComputerResults>" +
        Gpo(a) + Gpo(b) + "</ComputerResults>" +
        "<UserResults><GPO><ID>{00000000-0000-0000-0000-000000000001}</ID></GPO></UserResults></Rsop>";
    var match = GpoApplicabilityVerificationService.AssessRsopXml(
        xml, "Computer", new[] { a, b });
    Assert(match.AllApplied && !match.AnyExcluded,
        "Both identified and explicitly allowed applied GPOs must pass as a sample.");

    var denied = GpoApplicabilityVerificationService.AssessRsopXml(
        "<Rsop><ComputerResults>" + Gpo(a) + Gpo(b, false) +
        "</ComputerResults></Rsop>", "Computer", new[] { a, b });
    Assert(!denied.AllApplied && denied.AnyExcluded,
        "An explicitly filtered GPO must block the sample.");

    var unknown = GpoApplicabilityVerificationService.AssessRsopXml(
        "<Rsop><ComputerResults>" + Gpo(a) +
        "</ComputerResults></Rsop>", "Computer", new[] { a, b });
    Assert(!unknown.AllApplied && !unknown.AnyExcluded,
        "An absent GPO must remain unknown, not a proven exclusion.");

    var nested = GpoApplicabilityVerificationService.AssessRsopXml(
        "<Rsop><ComputerResults><ExtensionData><GPO>" +
        $"<ID>{{{a:D}}}</ID><FilterAllowed>true</FilterAllowed>" +
        "<AccessDenied>false</AccessDenied></GPO></ExtensionData>" +
        Gpo(b) + "</ComputerResults></Rsop>",
        "Computer", new[] { a, b });
    Assert(!nested.AllApplied && !nested.AnyExcluded,
        "Nested extension references must not masquerade as applied GPOs.");

    var repeated = GpoApplicabilityVerificationService.AssessRsopXml(
        "<Rsop><ComputerResults>" + Gpo(a) + Gpo(a) + Gpo(b) +
        "</ComputerResults></Rsop>", "Computer", new[] { a, b });
    Assert(!repeated.AllApplied && !repeated.AnyExcluded,
        "Duplicate GPO records must not silently select one as effective.");

    var noFlags = GpoApplicabilityVerificationService.AssessRsopXml(
        "<Rsop><ComputerResults>" +
        $"<GPO><ID>{{{a:D}}}</ID></GPO>" +
        Gpo(b) + "</ComputerResults></Rsop>",
        "Computer", new[] { a, b });
    Assert(!noFlags.AllApplied && !noFlags.AnyExcluded,
        "Missing security and WMI application flags must never imply applied.");
}

static void TestMmcFullInventoryReconciliation()
{
    var id = Guid.NewGuid();
    var row = new MmcInventoryEntry
    {
        GpoId = id,
        GpoName = "Reference GPO",
        Scope = "Computer",
        SectionPath =
            "Computer Configuration > Policies > Windows Settings > " +
            "Security Settings > Local Policies > Security Options",
        TreeSegments = new[]
        {
            "Computer Configuration", "Policies", "Windows Settings",
            "Security Settings", "Local Policies", "Security Options"
        },
        SettingName = "Network security: LAN Manager authentication level",
        MmcValue = "Send NTLMv2 response only",
        Source = "MMC native list"
    };
    var configured = new[]
    {
        new PolicySettingInfo
        {
            GpoId = id, Scope = "Computer",
            SettingName = row.SettingName,
            Category = "Security Settings > Local Policies > Security Options",
            State = "Enabled", Value = "NTLMv2"
        },
        // A matching label elsewhere must not count as an exact GPO setting.
        new PolicySettingInfo
        {
            GpoId = Guid.NewGuid(), Scope = "Computer",
            SettingName = row.SettingName,
            Category = "Security Settings > Local Policies > Security Options",
            State = "Disabled"
        }
    };
    var admx = new[]
    {
        new AdmxPolicyDefinition
        {
            Scope = "Computer", DisplayName = row.SettingName,
            Category = "Windows Components > Unrelated"
        }
    };
    var matched = MmcInventoryReconciliation.Reconcile(row, configured, admx);
    Assert(matched.GpmcMatch.StartsWith("Indexed: Enabled", StringComparison.Ordinal),
        "Only same-GPO, same-scope and same-category configured rows may be associated.");
    Assert(matched.AdmxMatch.StartsWith("No exact ADMX", StringComparison.Ordinal),
        "An ADMX policy with the same name but unrelated category is not an exact match.");
    Assert(MmcInventoryReconciliation.SectionMatches(
        row.SectionPath, "Security Settings > Local Policies > Security Options"),
        "MMC path suffixes must match genuine GPMC policy category paths.");
    Assert(!MmcInventoryReconciliation.SectionMatches(
        row.SectionPath, "Windows Components > Security Options"),
        "Matching only a leaf name must never join unrelated policy categories.");

    var unknown = MmcInventoryReconciliation.Reconcile(
        row, Array.Empty<PolicySettingInfo>(), null);
    Assert(unknown.GpmcMatch.Contains("not proof of Not Configured",
            StringComparison.OrdinalIgnoreCase),
        "No GPMC entry is not evidence that MMC says Not Configured.");
    Assert(unknown.AdmxMatch == "ADMX catalog not loaded",
        "Missing ADMX catalog must be explicit.");

    var ambiguous = MmcInventoryReconciliation.Reconcile(
        row, configured.Concat(new[] { configured[0] }).ToArray(), admx);
    Assert(ambiguous.GpmcMatch.StartsWith("Ambiguous", StringComparison.Ordinal),
        "Multiple same-scope GPMC entries must never be silently chosen.");

    Assert(MmcFullSettingsInventoryService.ParseState("Not Configured") ==
           "Not Configured (MMC)", "Only an explicit MMC state may show Not Configured.");
    Assert(MmcFullSettingsInventoryService.ParseState("Custom registry data")
           .StartsWith("Not reported", StringComparison.Ordinal),
        "Unrecognized MMC text must not be inferred to be configured or disabled.");

    var noNative = new MmcInventoryScanResult(
        Array.Empty<MmcInventoryEntry>(),
        new[] { new MmcInventorySection(
            "Computer Configuration > Custom vendor node", "No list", 0) },
        1, false, "Tree finished.", DateTimeOffset.Now);
    Assert(!noNative.IsComplete && noNative.Failures == 1 &&
           noNative.Coverage.Contains("PARTIAL", StringComparison.Ordinal),
        "A custom non-native MMC view must be counted as incomplete coverage.");

    var interrupted = noNative with { Interrupted = true, CompletionReason = "Canceled" };
    Assert(!interrupted.IsComplete && interrupted.Coverage.Contains("Canceled",
            StringComparison.Ordinal), "Cancellation must remain visible in the scan summary.");
}

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

static void TestGpoScriptCopyResolution()
{
    var gpoA = Guid.NewGuid();
    var gpoB = Guid.NewGuid();
    var sourceA = new GpoScriptInfo
    {
        GpoId = gpoA, GpoName = "GPO A", Scope = "Computer",
        EventName = "Startup", FileName = "deploy.bat",
        FullPath = @"\\server\SYSVOL\Policies\A\deploy.bat",
        Exists = true, Referenced = true
    };
    var sourceB = new GpoScriptInfo
    {
        GpoId = gpoB, GpoName = "GPO B", Scope = "Computer",
        EventName = "Startup", FileName = "deploy.bat",
        FullPath = @"\\server\SYSVOL\Policies\B\deploy.bat",
        Exists = true, Referenced = true
    };
    var result = new GpoScriptSearchResult
    {
        Identity = "same sha256",
        Scripts = new[] { sourceB, sourceA, sourceB }
    };
    Assert(result.CopyCount == 2 && result.HasMultipleCopies &&
           result.CopiesLabel == "2 copies",
        "Identical scripts should show an interactive two-copy counter.");
    var unique = GpoScriptCopyResolver.PhysicalCopies(result);
    Assert(unique.Count == 2, "The viewer should deduplicate identical physical paths.");
    Assert(GpoScriptCopyResolver.PreferredCopy(result, gpoA)?.FullPath == sourceA.FullPath,
        "Editing from the selected GPO must use only that GPO's physical script.");
    Assert(GpoScriptCopyResolver.PreferredCopy(result, null, sourceB.FullPath)?.GpoId == gpoB,
        "Explicitly selected physical copy must be retained.");
    Assert(GpoScriptCopyResolver.PreferredCopy(result)?.GpoName == "GPO A",
        "No-scope default must be deterministic.");
    Assert(GpoScriptCopyResolver.PreferredCopy(
        new GpoScriptSearchResult { Scripts = new[] { sourceA } })?.GpoId == gpoA,
        "One-copy result must open without a chooser.");
}

static void TestExtendedMmcSectionRouting()
{
    var path = GpoEditorNavigatorService.NavigationTarget(new PolicySettingInfo
    {
        Extension = "PublicKeySettings", Scope = "Computer",
        SettingName = "Root Certificate Settings", Category = "PublicKeySettings"
    });
    Assert(path.EndsWith(
        "Security Settings > Public Key Policies > Trusted Root Certification Authorities",
        StringComparison.Ordinal),
        "Root Certificate Settings was not routed into Public Key Policies.");

    var nrpt = GpoEditorNavigatorService.NavigationTarget(new PolicySettingInfo
    {
        Extension = "NrptSettings", Scope = "Computer",
        SettingName = "Fallback"
    });
    Assert(nrpt.EndsWith("Policies > Windows Settings > Name Resolution Policy",
        StringComparison.Ordinal), "NRPT fell back to unrelated Security Settings.");

    var software = GpoEditorNavigatorService.NavigationTarget(new PolicySettingInfo
    {
        Extension = "SoftwareInstallationSettings", Scope = "Computer",
        SettingName = "Trustee Auditing"
    });
    Assert(software.EndsWith(
        "Computer Configuration > Policies > Software Settings > Software installation",
        StringComparison.Ordinal), "Trustee Auditing should point to Software installation package security.");
    Assert(!new GpoEditorNavigatorService().CanNavigateExactly(new PolicySettingInfo
    {
        Extension = "SoftwareInstallationSettings", Scope = "Computer",
        SettingName = "Trustee Auditing"
    }), "Nested package ACL metadata cannot be opened as a Security Option.");

    var notMapped = GpoPolicySectionRoutes.Resolve(new PolicySettingInfo
    {
        Extension = "RegistrySettings", Scope = "Computer", SettingName = "Registry: raw"
    });
    Assert(notMapped.Count == 0, "Raw registry.pol navigation must remain unchanged.");
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

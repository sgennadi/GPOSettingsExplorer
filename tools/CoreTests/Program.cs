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
    ("Security XML registry and restricted-group details remain read-only", TestSecurityXmlClassificationAndRoutes),
    ("MMC navigation does not confuse audit and registry with security options", TestMmcNavigationRouting),
    ("MMC policy names must match uniquely and exactly", TestMmcPolicyNameMatcher),
    ("MMC ADMX tree suffix is recognized only with unique section identity", TestMmcTreePathMatcher),
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
    ("GPO permissions require separate AD and SYSVOL evidence", TestGpoCapabilityEvidence),
    ("GPO comparisons fail closed on technical XML and duplicate identities", TestGpoComparisonEvidence),
    ("GPO rights deny/unknown edge cases", TestGpoCapabilityEvidenceExtended),
    ("Comparison rejects XML details and ambiguous indexed values", TestSafeGpoSettingsComparison),
    ("RSoP verification rejects missing, excluded and nested GPOs", TestRsopVerificationEvidence),
    ("MMC inventory verifies source, path, scope and incomplete coverage", TestMmcFullInventoryReconciliation),
    ("MMC inventory skips fragile Scripts snap-ins before automation", TestMmcInventorySnapinSafety),
    ("Unified catalog joins evidence without cross-GPO or false state inference", TestUnifiedSettingsCatalog),
    ("Registry.pol binary parser preserves exact source values and flags malformed data", TestRealSettingsRegistryPol),
    ("Security-template parser reports source values and invalid encodings safely", TestRealSettingsSecurityTemplate),
    ("Unified catalog never interprets source-file values as effective RSoP", TestRealSettingsUnifiedEvidence),
    ("Cross-DC GPO version evidence refuses aliases and incomplete comparisons", TestCrossDcVersionEvidence),
    ("GPP XML evidence is bounded, redacted and never interpreted as effective policy", TestGppXmlEvidence),
    ("Legacy security boolean edit rejects absent and ambiguous DWORD values", TestSafeRegistryBooleanSecurityEdit),
    ("Comprehensive evidence ZIP contains manifests, source hashes and safe CSV", TestGpoEvidenceArchive),
    ("Selective recovery requires same GPO, intact backup and supported key", TestSelectiveSecurityRecovery),
    ("GPO impact preview does not equate OU link with applied RSoP", TestImpactPreviewEvidence),
    ("Security template editor rejects unknown and duplicate source values", TestSecurityTemplateEditingRules),
    ("GPT.INI parses split AD/SYSVOL version and rejects corruption", TestGptIniVersionParser),
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

static void TestGpoCapabilityEvidence()
{
    const string user = "S-1-5-21-1234";
    var write = new DiagnosticItem("GPT.INI", "Write access available", true);
    var deniedWrite = new DiagnosticItem("GPT.INI", "Access denied", false);

    GpoPermissionInfo Rule(GpoPermissionLevel level, bool deny = false) =>
        new() { TrusteeSid = user, TrusteeName = "Operator", Level = level, Denied = deny };

    var noAd = GpoCapabilityService.EvaluateForToken(
        Array.Empty<GpoPermissionInfo>(), new[] { user }, write);
    Assert(!noAd.CanEditSettings && !noAd.CanRead && !noAd.CanEditSecurity,
        "Opening GPT.INI writable must NEVER grant missing GPO AD permissions.");
    Assert(noAd.Details.Contains("AD: no matching", StringComparison.OrdinalIgnoreCase),
        "Incomplete AD evidence must be clear to the operator.");

    var editNoGpt = GpoCapabilityService.EvaluateForToken(
        new[] { Rule(GpoPermissionLevel.Edit) }, new[] { user }, deniedWrite);
    Assert(editNoGpt.CanRead && !editNoGpt.CanEditSettings,
        "AD edit is insufficient for a GPT-writing command when SYSVOL is unverified.");

    var editGranted = GpoCapabilityService.EvaluateForToken(
        new[] { Rule(GpoPermissionLevel.Edit) }, new[] { user }, write);
    Assert(editGranted.CanEditSettings && !editGranted.CanEditSecurity,
        "AD edit plus independently confirmed GPT access can enable guarded settings UI.");

    var editDenied = GpoCapabilityService.EvaluateForToken(
        new[] { Rule(GpoPermissionLevel.Edit), Rule(GpoPermissionLevel.Edit, true) },
        new[] { user }, write);
    Assert(!editDenied.CanEditSettings,
        "Explicit matching-token AD edit deny must win over positive SYSVOL evidence.");

    var customDenied = GpoCapabilityService.EvaluateForToken(
        new[] { Rule(GpoPermissionLevel.FullControl), Rule(GpoPermissionLevel.Custom, true) },
        new[] { user }, write);
    Assert(!customDenied.CanEditSettings && !customDenied.CanEditSecurity,
        "Unknown custom AD deny must fail closed, not be disregarded.");

    var onlyApply = GpoCapabilityService.EvaluateForToken(
        new[] { Rule(GpoPermissionLevel.Apply) }, new[] { user }, write);
    Assert(onlyApply.CanRead && !onlyApply.CanEditSettings,
        "Apply/Read permissions do not authorize a policy edit.");

    var wrongPrincipal = GpoCapabilityService.EvaluateForToken(
        new[] { Rule(GpoPermissionLevel.FullControl) }, new[] { "S-1-5-21-5678" }, write);
    Assert(!wrongPrincipal.CanEditSettings && !wrongPrincipal.CanEditSecurity,
        "Permissions belonging to another SID must never authorize current user.");

    var full = GpoCapabilityService.EvaluateForToken(
        new[] { Rule(GpoPermissionLevel.FullControl) }, new[] { user }, null);
    Assert(full.CanEditSecurity && !full.CanEditSettings,
        "AD security editing evidence is distinct from GPT-writing evidence.");
}

static void TestSafeGpoSettingsComparison()
{
    var left = Guid.NewGuid();
    var right = Guid.NewGuid();
    PolicySettingInfo Make(Guid id, string name, string value, string ext = "RegistrySettings") =>
        new() { GpoId = id, GpoName = id == left ? "Left" : "Right",
            Scope = "Computer", Extension = ext, Category = "Policy",
            SettingName = name, State = "Enabled", Value = value,
            RegistryKey = @"Software\\Policies\\Example", RegistryValue = name };

    var a = Make(left, "Setting 1", "1");
    var b = Make(right, "Setting 1", "1");
    var leftUnknown = Make(left, "Left only", "2");
    var technical = new PolicySettingInfo
    {
        GpoId = left, GpoName = "Left", Scope = "Computer",
        Extension = "SecuritySettings", Category = "SecuritySettings",
        SettingName = "Member: DOMAIN\\Operators", State = "Configured",
        Value = "SID=S-1-5-21-1; Name=DOMAIN\\Operators"
    };

    var rows = GpoSettingsComparisonService.Compare(
        new[] { a, leftUnknown, technical }, new[] { b });
    Assert(rows.Count == 2 && rows.Any(r => r.Status == "Same"),
        "Identical configured policies should compare, without GPMC XML leaf noise.");
    Assert(rows.Any(r => r.Status == "Left only (index)" &&
            r.RightState == "Not in loaded index"),
        "Missing index entry must not be mislabeled as Not Configured.");

    var ambiguous = GpoSettingsComparisonService.Compare(
        new[] { a, Make(left, "Setting 1", "3") }, new[] { b });
    Assert(ambiguous.Single().Status == "Ambiguous index" &&
           ambiguous.Single().LeftState == "Ambiguous indexed values",
        "Conflicting duplicate identity inside a GPO cannot be silently collapsed.");

    var gs = new[] {
        new GpoInfo { Id = left, DisplayName = "Left", ComputerEnabled = true },
        new GpoInfo { Id = right, DisplayName = "Right", ComputerEnabled = true }
    };
    var links = new[] {
        new GpoLinkInfo { GpoId = left, Enabled = true,
            TargetDn = "OU=Workstations,DC=example,DC=com", TargetType = "OU" },
        new GpoLinkInfo { GpoId = right, Enabled = true,
            TargetDn = "OU=Workstations,DC=example,DC=com", TargetType = "OU" }
    };
    var findings = GpoConflictAnalysisService.Analyze(
        new[] { a, Make(left, "Setting 1", "3"), b, technical }, links, gs);
    Assert(findings.Count == 1 && findings[0].Kind == "Ambiguous index",
        "Conflict finder must not claim a proven Duplicate or Different value from ambiguous same-GPO entries.");
    Assert(findings[0].Participants.Count == 3 &&
           findings[0].Recommendation.Contains("Do not merge", StringComparison.OrdinalIgnoreCase),
        "Ambiguous identity must retain all source evidence and block consolidation recommendations.");
}

static void TestGpoCapabilityEvidenceExtended()
{
    const string sid = "S-1-5-21-100-200-300-400";
    const string unrelated = "S-1-5-21-100-200-300-401";
    var allowEdit = new GpoPermissionInfo
    {
        TrusteeSid = sid, TrusteeName = "Editor",
        Level = GpoPermissionLevel.Edit
    };
    var allowFull = new GpoPermissionInfo
    {
        TrusteeSid = sid, TrusteeName = "Owner",
        Level = GpoPermissionLevel.FullControl
    };
    var denyEdit = new GpoPermissionInfo
    {
        TrusteeSid = sid, TrusteeName = "DeniedEditor",
        Level = GpoPermissionLevel.Edit, Denied = true
    };
    var writeYes = new DiagnosticItem("SYSVOL", "Write access available", true);
    var writeNo = new DiagnosticItem("SYSVOL", "Write access unavailable", false);

    var none = GpoCapabilityService.EvaluateForToken(
        Array.Empty<GpoPermissionInfo>(), new[] { sid }, writeYes);
    Assert(!none.CanRead && !none.CanEditSettings && !none.CanEditSecurity &&
           none.Summary.Contains("unconfirmed", StringComparison.OrdinalIgnoreCase),
        "Writable SYSVOL alone must not grant missing AD permissions.");

    var foreign = GpoCapabilityService.EvaluateForToken(
        new[] { allowEdit }, new[] { unrelated }, writeYes);
    Assert(!foreign.CanEditSettings && !foreign.CanRead,
        "Permission for an unrelated SID must not grant rights to current token.");

    var adOnly = GpoCapabilityService.EvaluateForToken(
        new[] { allowEdit }, new[] { sid }, null);
    Assert(adOnly.CanRead && !adOnly.CanEditSettings &&
           adOnly.Details.Contains("not tested", StringComparison.OrdinalIgnoreCase),
        "AD Edit alone without SYSVOL evidence must not activate UI write buttons.");

    var complete = GpoCapabilityService.EvaluateForToken(
        new[] { allowEdit }, new[] { sid }, writeYes);
    Assert(complete.CanRead && complete.CanEditSettings &&
           !complete.CanEditSecurity,
        "Matching AD edit grant AND SYSVOL access may enable edit UI hints, not security editing.");

    var noSysvol = GpoCapabilityService.EvaluateForToken(
        new[] { allowFull }, new[] { sid }, writeNo);
    Assert(noSysvol.CanEditSecurity && !noSysvol.CanEditSettings,
        "An AD FullControl grant and failed SYSVOL probe must remain independent.");

    var denied = GpoCapabilityService.EvaluateForToken(
        new[] { allowEdit, denyEdit }, new[] { sid }, writeYes);
    Assert(!denied.CanEditSettings,
        "Matching AD deny must override allow even with SYSVOL write access.");

    var customDeny = GpoCapabilityService.EvaluateForToken(
        new[] { allowFull, new GpoPermissionInfo
            { TrusteeSid = sid, Level = GpoPermissionLevel.Custom, Denied = true } },
        new[] { sid }, writeYes);
    Assert(!customDeny.CanEditSecurity && !customDeny.CanEditSettings,
        "Unknown matching AD deny cannot be treated as an allow.");
}

static void TestGpoComparisonEvidence()
{
    var a = Guid.NewGuid();
    var b = Guid.NewGuid();
    PolicySettingInfo Row(Guid id, string name, string value) => new()
    {
        GpoId = id, GpoName = id == a ? "A" : "B",
        Scope = "Computer", Extension = "RegistrySettings",
        Category = "Administrative Templates > System",
        SettingName = name, State = "Enabled",
        RegistryKey = @"Software\\Policies\\Example",
        RegistryValue = "Flag", Value = value
    };
    var left = Row(a, "Flag", "1");
    var duplicate = Row(a, "Flag", "2");
    var right = Row(b, "Flag", "1");
    var raw = new PolicySettingInfo
    {
        GpoId = a, GpoName = "A", Scope = "Computer",
        Extension = "SecuritySettings", Category = "SecuritySettings",
        SettingName = "Registry", State = "Configured",
        Value = "ACL descriptor with SID"
    };

    var compare = GpoSettingsComparisonService.Compare(new[] { left, raw },
        new[] { right });
    Assert(compare.Count == 1 && compare[0].Status == "Same",
        "Technical security XML must not become a comparison identity.");

    var ambiguous = GpoSettingsComparisonService.Compare(
        new[] { left, duplicate }, new[] { right });
    Assert(ambiguous.Count == 1 &&
           ambiguous[0].Status == "Ambiguous index" &&
           ambiguous[0].LeftState == "Ambiguous indexed values",
        "Multiple differing index entries in one GPO cannot silently choose first record.");

    var missing = GpoSettingsComparisonService.Compare(
        new[] { left }, Array.Empty<PolicySettingInfo>());
    Assert(missing.Count == 1 &&
           missing[0].RightState == "Not in loaded index" &&
           missing[0].Status.Contains("(index)", StringComparison.Ordinal),
        "Missing indexed item must not be asserted to be Not Configured.");

    var links = new[]
    {
        new GpoLinkInfo { GpoId = a, TargetDn = "OU=Lab,DC=example,DC=local",
            TargetType = "OU", TargetName = "Lab", Enabled = true, Order = 1 },
        new GpoLinkInfo { GpoId = b, TargetDn = "OU=Lab,DC=example,DC=local",
            TargetType = "OU", TargetName = "Lab", Enabled = true, Order = 2 }
    };
    var gpos = new[]
    {
        new GpoInfo { Id = a, DisplayName = "A", ComputerEnabled = true },
        new GpoInfo { Id = b, DisplayName = "B", ComputerEnabled = true }
    };
    var findings = GpoConflictAnalysisService.Analyze(
        new[] { left, duplicate, right, raw }, links, gpos);
    Assert(findings.Count == 1 &&
           findings[0].Kind == "Ambiguous index" &&
           findings[0].Recommendation.Contains("Do not merge",
               StringComparison.OrdinalIgnoreCase),
        "Conflicting same-GPO entries must be ambiguous, not a spurious duplicate.");
    Assert(GpoConflictAnalysisService.Analyze(
        new[] { raw, new PolicySettingInfo
        {
            GpoId = b, GpoName = "B", Scope = "Computer",
            Extension = "SecuritySettings", Category = "SecuritySettings",
            SettingName = "Registry", State = "Configured", Value = "different SID"
        } }, links, gpos).Count == 0,
        "Same-label XML security ACL details must never create conflict findings.");
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

static void TestRealSettingsRegistryPol()
{
    var gpo = Guid.NewGuid();
    const string key = @"Software\Policies\Example";
    const string file = @"\\dc.example.local\SYSVOL\example.local\Policies\{GPO}\Machine\Registry.pol";
    var data = new List<byte>();
    void Raw(byte[] bytes) => data.AddRange(bytes);
    void U16(string value) => Raw(System.Text.Encoding.Unicode.GetBytes(value));
    void Dword(uint value)
    {
        var bytes = new byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
        Raw(bytes);
    }
    void Entry(string valueName, uint type, byte[] payload)
    {
        U16("["); U16(key + "\0"); U16(";");
        U16(valueName + "\0"); U16(";");
        Dword(type); U16(";"); Dword((uint)payload.Length); U16(";");
        Raw(payload); U16("]");
    }
    Dword(0x67655250); Dword(1);
    Entry("Flag", 4, new byte[] { 1, 0, 0, 0 });
    Entry("Greeting", 1, System.Text.Encoding.Unicode.GetBytes("Shalom שלום\0"));
    Entry("**Del.OldSetting", 4, Array.Empty<byte>());
    var parsed = RegistryPolReader.Parse(data.ToArray(), gpo, "Example",
        "Computer", file, new string('A', 64));
    Assert(parsed.IsComplete && parsed.Rows.Count == 3,
        "Valid PReg binary stream should yield three source records.");
    Assert(parsed.Rows[0].Value == "1" && parsed.Rows[0].ValueType == "REG_DWORD" &&
           parsed.Rows[0].RegistryKey == key,
        "DWORD must be binary little-endian; registry path must remain unchanged.");
    Assert(parsed.Rows[1].Value == "Shalom שלום",
        "UTF-16LE source data must preserve Hebrew and Unicode.");
    Assert(parsed.Rows[2].State == "Stored operation" &&
           parsed.Rows[2].Evidence.Contains("Special", StringComparison.Ordinal),
        "Delete instruction is not evidence of a currently configured value.");

    var truncated = data.Take(data.Count - 2).ToArray();
    var partial = RegistryPolReader.Parse(truncated, gpo, "Example",
        "Computer", file, "");
    Assert(!partial.IsComplete && partial.Rows.Count == 2 &&
           partial.Issues[0].Contains("Record 3", StringComparison.Ordinal),
        "Truncated PReg source must be PARTIAL; previously complete entries retained.");

    var badVersion = data.ToArray();
    badVersion[4] = 2;
    var unsupported = RegistryPolReader.Parse(badVersion, gpo, "Example",
        "Computer", file, "");
    Assert(!unsupported.IsComplete && unsupported.Rows.Count == 0,
        "Unsupported version must not be interpreted as a valid policy file.");

    var badHeader = RegistryPolReader.Parse(new byte[] { 1, 2, 3, 4 },
        gpo, "Example", "Computer", file, "");
    Assert(!badHeader.IsComplete && badHeader.Rows.Count == 0,
        "Invalid PReg header must fail closed without fabricated entries.");
}

static void TestRealSettingsSecurityTemplate()
{
    const string source = @"\\dc.example.local\SYSVOL\example.local\Policies\{GPO}\Machine\Microsoft\Windows NT\SecEdit\GptTmpl.inf";
    var inf = "[Unicode]\r\nUnicode=yes\r\n" +
              "[System Access]\r\nMinimumPasswordLength = 14\r\n" +
              "[Privilege Rights]\r\nSeRemoteInteractiveLogonRight = *S-1-5-32-544\r\n";
    var raw = System.Text.Encoding.Unicode.GetPreamble().Concat(
        System.Text.Encoding.Unicode.GetBytes(inf)).ToArray();
    var gpo = Guid.NewGuid();
    var result = SecurityTemplateSourceReader.Parse(raw, gpo,
        "Example", source, new string('B', 64));
    Assert(result.IsComplete &&
           result.Rows.Any(x => x.Category == "Security template > System Access" &&
                                x.SettingName == "MinimumPasswordLength" &&
                                x.Value == "14"),
        "SecEdit system access values must be read directly without MMC.");
    Assert(result.Rows.Any(x => x.Category == "Security template > Privilege Rights" &&
                                x.Value.Contains("*S-1-5-32-544", StringComparison.Ordinal)),
        "Privilege rights SIDs must be preserved as source text, not interpreted as effective access.");

    var unknownEncoding = SecurityTemplateSourceReader.Parse(
        new byte[] { 0xFF, 0x41, 0x42, 0x43 },
        gpo, "Example", source, "");
    Assert(!unknownEncoding.IsComplete && unknownEncoding.Rows.Count == 0,
        "Unknown non-Unicode encoding must fail closed rather than invent values.");

    var badLine = SecurityTemplateSourceReader.Parse(
        System.Text.Encoding.UTF8.GetBytes("[System Access]\nCorruptRecord\nMinimumPasswordLength=8"),
        gpo, "Example", source, "");
    Assert(!badLine.IsComplete && badLine.Rows.Count == 1,
        "Broken INF lines must be reported while valid source entries remain available.");
}

static void TestRealSettingsUnifiedEvidence()
{
    var gpo = Guid.NewGuid();
    var other = Guid.NewGuid();
    var source = new RealSettingRecord
    {
        GpoId = gpo, GpoName = "Example", Scope = "Computer",
        SettingName = "PolicyValue", Category = "Registry policy (source file)",
        RegistryKey = @"Software\Policies\Example", RegistryValue = "PolicyValue",
        Value = "42", ValueType = "REG_DWORD", State = "Stored registry value",
        SourceFile = @"\\dc\SYSVOL\example.local\Policies\gpo\Machine\Registry.pol",
        SourceSha256 = new string('C', 64),
        Evidence = "From PReg v1, not effective RSoP."
    };
    var file = new RealSettingsFileEvidence(source.SourceFile, "Read",
        1, source.SourceSha256, "read-only");
    var scan = new RealSettingsScanResult(gpo, "Example", "example.local",
        "dc.example.local", DateTimeOffset.UtcNow, new[] { source }, new[] { file });

    var catalog = UnifiedSettingsCatalogService.Build(
        Array.Empty<PolicySettingInfo>(), null, null, gpo,
        "", scan);
    Assert(catalog.SourceFileEntries == 1 &&
           catalog.Rows.Single().StoredSource == source &&
           catalog.Rows.Single().Capability.Contains("read-only", StringComparison.Ordinal) &&
           !catalog.Rows.Single().State.Contains("Not Configured", StringComparison.OrdinalIgnoreCase),
        "Real source evidence must be read-only, separate and never imply effective RSoP.");

    var wrongGpo = UnifiedSettingsCatalogService.Build(
        Array.Empty<PolicySettingInfo>(), null, null, other, "", scan);
    Assert(wrongGpo.SourceFileEntries == 0,
        "A source scan from another GPO must never leak into the filtered target.");

    var incomplete = scan with { Files = new[] {
        new RealSettingsFileEvidence(source.SourceFile, "Partial", 1,
            source.SourceSha256, "incomplete") } };
    Assert(incomplete.IsPartial && incomplete.Coverage.Contains("PARTIAL", StringComparison.Ordinal),
        "Malformed file evidence must never be described as a complete scan.");
}

static void TestUnifiedSettingsCatalog()
{
    var gpoA = Guid.NewGuid();
    var gpoB = Guid.NewGuid();
    const string name = "Network security: LAN Manager authentication level";

    var a = new PolicySettingInfo
    {
        GpoId = gpoA, GpoName = "Baseline A", Scope = "Computer",
        Extension = "SecuritySettings",
        Category = "Security Settings > Local Policies > Security Options",
        SettingName = name, State = "Enabled", Value = "NTLMv2"
    };
    var b = new PolicySettingInfo
    {
        GpoId = gpoB, GpoName = "Baseline B", Scope = "Computer",
        Extension = "SecuritySettings",
        Category = "Security Settings > Local Policies > Security Options",
        SettingName = name, State = "Disabled", Value = "NTLM"
    };

    var correct = new AdmxPolicyDefinition
    {
        AdmxFile = "security.admx", Name = "LanManagerAuthentication",
        DisplayName = name, Scope = "Computer",
        Category = "Security Settings > Local Policies > Security Options",
        Key = @"Software\\Policies\\Example", ValueName = "LanManager"
    };
    var wrongCategory = new AdmxPolicyDefinition
    {
        AdmxFile = "unrelated.admx", Name = "UnrelatedPolicy",
        DisplayName = name, Scope = "Computer",
        Category = "Windows Components > Other", Key = "Unrelated"
    };

    var mmc = new MmcInventoryEntry
    {
        GpoId = gpoA, GpoName = "Baseline A", Scope = "Computer",
        SectionPath = "Computer Configuration > Policies > Windows Settings > " +
                      "Security Settings > Local Policies > Security Options",
        SettingName = name,
        MmcState = "Enabled (MMC)", MmcValue = "NTLMv2",
        Source = "MMC native list",
        Navigation = "Exact MMC row candidate"
    };

    var combined = UnifiedSettingsCatalogService.Build(
        new[] { a, b }, new[] { correct, wrongCategory }, new[] { mmc }, null,
        "PARTIAL: Scripts snap-in excluded");

    Assert(combined.ConfiguredCount == 2 && combined.MmcOnlyCount == 0,
        "Only the specific matching configured setting may consume an MMC observation.");
    var rowsA = combined.Rows.Where(row => row.Configured?.GpoId == gpoA).ToArray();
    Assert(rowsA.Length == 1 && rowsA[0].Admx == correct &&
           ReferenceEquals(rowsA[0].Mmc, mmc) &&
           rowsA[0].State == "Enabled" &&
           rowsA[0].Sources.Contains("MMC", StringComparison.Ordinal),
        "Configured, ADMX and MMC evidence must join only on exact GPO, scope and category.");
    var rowsB = combined.Rows.Where(row => row.Configured?.GpoId == gpoB).ToArray();
    Assert(rowsB.Length == 1 && rowsB[0].Mmc is null &&
           rowsB[0].State == "Disabled",
        "Another GPO must never inherit the reference MMC editor's value.");
    Assert(combined.Coverage.Contains("PARTIAL", StringComparison.Ordinal),
        "Incomplete MMC scan must remain visible in the unified catalog.");

    var other = UnifiedSettingsCatalogService.Build(
        new[] { a }, new[] { correct, wrongCategory }, new[] { mmc }, gpoB);
    Assert(other.ConfiguredCount == 0 && other.MmcOnlyCount == 0 &&
           other.Rows.Any(row => row.Kind == "ADMX template" && row.Admx == correct),
        "A GPO with no configured instance must still expose the ADMX template as unknown.");
    Assert(other.Rows.Where(row => row.Kind == "ADMX template")
        .All(row => row.GpoId is null &&
                    row.State == "Template - state unknown"),
        "Templates must never be labeled Not Configured from missing GPMC rows.");

    var mmcOnly = UnifiedSettingsCatalogService.Build(
        Array.Empty<PolicySettingInfo>(), null, new[] { mmc }, gpoA);
    Assert(mmcOnly.MmcOnlyCount == 1 && !mmcOnly.AdmxLoaded &&
           mmcOnly.Rows[0].Kind == "MMC observed" &&
           mmcOnly.Rows[0].State == "Enabled (MMC)",
        "MMC-only evidence must remain visible without an ADMX/GPMC index.");

    var ambiguous = UnifiedSettingsCatalogService.Build(
        new[] { a }, new[] { correct, new AdmxPolicyDefinition
        {
            AdmxFile = "duplicate.admx", Name = "OtherId",
            DisplayName = name, Scope = "Computer",
            Category = correct.Category
        } }, null, gpoA);
    Assert(ambiguous.Rows.Single(row => row.Kind == "Configured").Admx is null,
        "An ambiguous same-name/category ADMX reference must not be arbitrarily resolved.");
    Assert(UnifiedSettingsCatalogService.ResolveDefinition(a, new[]
        {
            correct,
            new AdmxPolicyDefinition
            {
                Scope = "Computer", DisplayName = name,
                Category = correct.Category, AdmxFile = "duplicate.admx"
            }
        }) is null,
        "Legacy GPMC/Global Search editing must also reject ambiguous same-name ADMX records.");
    Assert(ReferenceEquals(
        UnifiedSettingsCatalogService.ResolveDefinition(a, new[] { correct, wrongCategory }),
        correct), "A unique exact category match must remain editable.");
}

static void TestMmcInventorySnapinSafety()
{
    var startup = new[]
    {
        "01.06.Yosh-DC-AD.Events.Falcon",
        "Computer Configuration", "Policies", "Windows Settings",
        "Scripts (Startup/Shutdown)"
    };
    Assert(MmcInventorySafetyRules.ShouldSkipNode(startup, out var message) &&
           message.Contains("GPO Scripts", StringComparison.Ordinal),
        "Scripts (Startup/Shutdown) must be excluded before Expand/Select.");

    var logon = new[]
    {
        "Example GPO", "User Configuration", "Policies", "Windows Settings",
        "Scripts (Logon/Logoff)"
    };
    Assert(MmcInventorySafetyRules.ShouldSkipNode(logon, out _),
        "User logon/logoff Scripts snap-in must also be excluded.");

    var unrelated = new[]
    {
        "Example GPO", "Computer Configuration", "Policies",
        "Administrative Templates", "System", "Scripts"
    };
    Assert(!MmcInventorySafetyRules.ShouldSkipNode(unrelated, out _),
        "An unrelated Administrative Templates Scripts category must not be blocked.");

    var safe = new[]
    {
        "Example GPO", "Computer Configuration", "Policies",
        "Windows Settings", "Security Settings", "Local Policies", "Security Options"
    };
    Assert(!MmcInventorySafetyRules.ShouldSkipNode(safe, out _),
        "Security Options and unrelated safe settings must remain discoverable.");

    var result = new MmcInventoryScanResult(
        Array.Empty<MmcInventoryEntry>(),
        new[] { new MmcInventorySection(
            string.Join(" > ", startup), "Skipped - unsafe snap-in", 0,
            "Skip documented in coverage") },
        5, false, "Completed safe nodes.", DateTimeOffset.Now);
    Assert(!result.IsComplete && result.Failures == 1 &&
           result.Coverage.Contains("PARTIAL", StringComparison.Ordinal),
        "Intentionally skipped snap-ins must keep inventory coverage PARTIAL.");

    var modalAbort = result with
    {
        Sections = new[] { new MmcInventorySection(
            "Computer Configuration", "Scan aborted", 0,
            "MMC reported a visible modal error") },
        Interrupted = true,
        CompletionReason = "MMC modal dialog was detected"
    };
    Assert(modalAbort.Failures == 1 && !modalAbort.IsComplete &&
           modalAbort.Coverage.Contains("modal", StringComparison.OrdinalIgnoreCase),
        "A modal MMC error must never be reported as a complete scan.");
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

static void TestSecurityXmlClassificationAndRoutes()
{
    var groupXml = System.Xml.Linq.XElement.Parse(
        "<Extension><RestrictedGroups><Group name='Local Administrators'>" +
        "<Member name='DOMAIN\\Domain Admins' SID='S-1-5-21-42'/>" +
        "</Group></RestrictedGroups></Extension>");
    var groupMember = groupXml.Descendants()
        .Single(x => x.Name.LocalName == "Member");
    Assert(SecurityXmlEntryClassifier.InferCategory(groupMember) ==
           SecurityXmlEntryClassifier.RestrictedGroupsSection,
        "Member entries nested below group security data must be Restricted Groups.");

    var registryXml = System.Xml.Linq.XElement.Parse(
        "<Extension><Registry key='MACHINE\\Software\\Example'>" +
        "<Permissions>ACL</Permissions></Registry></Extension>");
    Assert(SecurityXmlEntryClassifier.InferCategory(
        registryXml.Descendants().Single(x => x.Name.LocalName == "Registry")) ==
           SecurityXmlEntryClassifier.RegistrySection,
        "Security registry ACL XML must identify the Registry MMC section.");

    var securityOptionXml = System.Xml.Linq.XElement.Parse(
        "<Extension><SecurityOptions><Registry value='TRUE'/></SecurityOptions></Extension>");
    Assert(SecurityXmlEntryClassifier.InferCategory(
        securityOptionXml.Descendants().Single(x => x.Name.LocalName == "Registry")) is null,
        "A registry field inside Security Options must not be mislabeled as Registry ACL.");

    var unrelatedMember = System.Xml.Linq.XElement.Parse(
        "<Extension><Setting><Member name='someone'/></Setting></Extension>");
    Assert(SecurityXmlEntryClassifier.InferCategory(
        unrelatedMember.Descendants().Single(x => x.Name.LocalName == "Member")) is null,
        "Unrelated Member names without group ancestry must not become Restricted Groups.");

    var gpo = Guid.NewGuid();
    var legacyMember = new PolicySettingInfo
    {
        GpoId = gpo,
        GpoName = "Example GPO",
        Scope = "Computer",
        Extension = "SecuritySettings",
        Category = "SecuritySettings",
        SettingName = "Member: DOMAIN\\Group.Yosh.AD.Limited.Administrators",
        State = "Configured",
        Value = "SID=S-1-5-21-42; Name=DOMAIN\\Group.Yosh.AD.Limited.Administrators"
    };
    var legacyRegistry = new PolicySettingInfo
    {
        GpoId = gpo,
        GpoName = "Example GPO",
        Scope = "Computer",
        Extension = "SecuritySettings",
        Category = "SecuritySettings",
        SettingName = "Registry", State = "Configured",
        Value = "Type=PermissionType; Path=MACHINE\\Software\\Example; SecurityDescriptor=ABC"
    };
    var nonMember = new PolicySettingInfo
    {
        GpoId = gpo, Scope = "Computer", Extension = "SecuritySettings",
        Category = "SecuritySettings",
        SettingName = "Member: not enough evidence",
        State = "Configured", Value = "unknown"
    };

    Assert(SecurityXmlEntryClassifier.InferLegacyCategory(legacyMember) ==
           SecurityXmlEntryClassifier.RestrictedGroupsSection,
        "Legacy cached member XML with both SID and Name must resolve the related section.");
    Assert(SecurityXmlEntryClassifier.InferLegacyCategory(legacyRegistry) ==
           SecurityXmlEntryClassifier.RegistrySection,
        "Legacy cached Registry XML must resolve the related section.");
    Assert(SecurityXmlEntryClassifier.InferLegacyCategory(nonMember) is null,
        "An ambiguous cached member must not be blindly routed to Restricted Groups.");

    Assert(GpoEditorNavigatorService.NavigationTarget(legacyMember).EndsWith(
        "Security Settings > Restricted Groups", StringComparison.Ordinal),
        "Member XML should open Restricted Groups and not the generic Security Settings root.");
    Assert(GpoEditorNavigatorService.NavigationTarget(legacyRegistry).EndsWith(
        "Security Settings > Registry", StringComparison.Ordinal),
        "Registry XML should open the Security Registry section rather than the generic root.");

    var navigator = new GpoEditorNavigatorService();
    Assert(!navigator.CanNavigateExactly(legacyMember) &&
           !navigator.CanNavigateExactly(legacyRegistry),
        "XML SecuritySettings detail must never claim verified exact MMC row editing.");

    var unified = UnifiedSettingsCatalogService.Build(
        new[] { legacyMember, legacyRegistry },
        Array.Empty<AdmxPolicyDefinition>(),
        null, gpo);
    Assert(unified.ConfiguredCount == 0 &&
           unified.Rows.Count(r => r.IsTechnicalDetail) == 2,
        "Raw descriptor leaves must not count as independent configured policy settings.");
    Assert(unified.Rows.All(r =>
            r.Kind == "GPMC detail" && r.Capability.Contains("read-only", StringComparison.Ordinal) &&
            r.Value.Length < 200 &&
            r.State == "XML detail"),
        "Unified All Settings must show concise read-only detail instead of raw security ACL dumps.");
    Assert(unified.Rows.Single(r => r.SettingName == "Registry").Category ==
           SecurityXmlEntryClassifier.RegistrySection,
        "Old cached XML must be dynamically reclassified without full domain reindexing.");
    Assert(legacyRegistry.Value.Contains("SecurityDescriptor=ABC", StringComparison.Ordinal),
        "The original XML value must remain intact for the raw detail viewer.");
    Assert(!SecurityXmlEntryClassifier.IsTechnicalDetail(nonMember) ||
           SecurityXmlEntryClassifier.InferLegacyCategory(nonMember) is null,
        "An unknown member label must not cause a confident navigation route.");
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

static void TestMmcTreePathMatcher()
{
    const string expected = "Administrative Templates";
    const string central =
        "Administrative Templates: Policy definitions (ADMX files) retrieved from the central store";
    const string local =
        "Administrative Templates: Policy definitions (ADMX files) retrieved from the local computer";

    Assert(MmcTreePathMatcher.SectionNameMatches(central, expected),
        "Central-store ADMX caption must match the actual MMC Administrative Templates node.");
    Assert(MmcTreePathMatcher.SectionNameMatches(local, expected),
        "Local ADMX caption must match without assuming a domain central store.");
    Assert(MmcTreePathMatcher.SectionNameMatches("  Administrative   Templates  ", expected),
        "Excess whitespace must not hide an otherwise exact tree section.");
    Assert(MmcTreePathMatcher.SectionNameMatches(
        "Administrative Templates", expected), "Unmodified MMC section must still match.");
    Assert(!MmcTreePathMatcher.SectionNameMatches(
        "Administrative Templates: Unrelated policy extension", expected),
        "No generic prefix matching is allowed for a decorated MMC node.");
    Assert(!MmcTreePathMatcher.SectionNameMatches(
        "Administrative Templates (vendor custom)", expected),
        "A same-prefix third-party MMC section must not be mistaken for ADMX.");
    Assert(!MmcTreePathMatcher.SectionNameMatches(
        "Administrative Templates: Policy definitions (ADMX files) retrieved from unknown place",
        expected), "Unknown ADMX source descriptions must not bypass exact identity.");
    Assert(!MmcTreePathMatcher.SectionNameMatches(
        central, "Software installation"),
        "ADMX special alias cannot accidentally match another MMC section.");
    Assert(!MmcTreePathMatcher.SectionNameMatches(
        "Scripts (Startup/Shutdown)", "Scripts"),
        "Safety rules must not be bypassed through arbitrary child-prefix matching.");

    var siblings = new[]
    {
        "Software Settings", "Windows Settings", central
    };
    Assert(MmcTreePathMatcher.FindUniqueIndex(siblings, expected) == 2,
        "The provided YOSH-DC03 report must resolve Administrative Templates as a found path.");
    Assert(MmcTreePathMatcher.FindUniqueIndex(
        new[] { central, "Administrative Templates" }, expected) == 1,
        "Exact matches must win over a recognized decorated alias.");
    Assert(MmcTreePathMatcher.FindUniqueIndex(
        new[] { central, central }, expected) == MmcTreePathMatcher.Ambiguous,
        "Duplicate aliases must not produce an arbitrary navigation target.");
    Assert(MmcTreePathMatcher.FindUniqueIndex(
        new[] { expected, expected }, expected) == MmcTreePathMatcher.Ambiguous,
        "Duplicate exact labels must be reported as ambiguous.");
    Assert(MmcTreePathMatcher.FindUniqueIndex(
        new[] { central }, "Windows Settings") == MmcTreePathMatcher.NotFound,
        "Unrelated section names must still report missing.");
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


static void TestGptIniVersionParser()
{
    var input = System.Text.Encoding.UTF8.GetBytes("[General]\r\nVersion=131075\r\n");
    var parsed = GptIniVersionParser.Parse(input);
    Assert(parsed.Valid && parsed.Version == 131075 &&
        parsed.ComputerVersion == 2 && parsed.UserVersion == 3,
        "GPT.INI must preserve AD computer/user 16-bit version halves.");

    var utf16 = System.Text.Encoding.Unicode.GetPreamble()
        .Concat(System.Text.Encoding.Unicode.GetBytes("[General]\r\nVersion=4294967295\r\n"))
        .ToArray();
    var max = GptIniVersionParser.Parse(utf16);
    Assert(max.Valid && max.ComputerVersion == 65535 && max.UserVersion == 65535,
        "Unsigned UInt32 version must not overflow a signed int.");

    foreach (var value in new[]
    {
        "[General]\nVersion=-1",
        "[General]\nVersion=4294967296",
        "[General]\nVersion=1\nVersion=2",
        "[Other]\nVersion=1",
        "[General]\nVersion=garbage"
    })
        Assert(!GptIniVersionParser.Parse(System.Text.Encoding.UTF8.GetBytes(value)).Valid,
            "Ambiguous or corrupt GPT.INI version must fail closed: " + value);

    Assert(!GptIniVersionParser.Parse(new byte[1 + GptIniVersionParser.MaxBytes]).Valid,
        "Oversized source must not be parsed.");
}


static void TestSecurityTemplateEditingRules()
{
    var baseRecord = new RealSettingRecord
    {
        GpoId = Guid.NewGuid(), GpoName = "Test", Scope = "Computer",
        Category = "Security template > Event Audit",
        SettingName = "AuditLogonEvents", State = "Stored template value",
        Value = "1", ValueType = "INF string",
        SourceFile = @"\\test-dc\SYSVOL\test\GptTmpl.inf",
        SourceSha256 = new string('F', 64)
    };
    Assert(SecurityTemplateEditRules.TryDescribe(baseRecord, out var rule) &&
        rule is not null && rule.Allows(0) && rule.Allows(3) && !rule.Allows(4),
        "An existing known audit setting must permit values 0..3 only.");
    var text = "[Version]\r\nsignature=\"$CHICAGO$\"\r\n[Event Audit]\r\n" +
        "AuditLogonEvents = 1\r\nAuditSystemEvents = 2\r\n[System Access]\r\n" +
        "PasswordComplexity = 1\r\n";
    var updated = SecurityTemplateEditRules.ChangeExistingValue(text, rule!, 3);
    Assert(updated.Contains("AuditLogonEvents =3\r\n") &&
        updated.Contains("AuditSystemEvents = 2\r\n") &&
        updated.Contains("PasswordComplexity = 1\r\n"),
        "Only exact selected security value should change; preserve other fields.");
    try
    {
        SecurityTemplateEditRules.ChangeExistingValue(
            text.Replace("AuditSystemEvents = 2\r\n",
                "AuditLogonEvents = 2\r\n"), rule!, 3);
        throw new InvalidOperationException("Duplicate security setting was edited.");
    }
    catch (InvalidDataException) { }

    var rights = baseRecord with
    {
        Category = "Security template > Privilege Rights",
        SettingName = "SeDebugPrivilege",
        Value = "*S-1-5-32-544"
    };
    Assert(!SecurityTemplateEditRules.TryDescribe(rights, out _),
        "Privilege Rights / SID strings must never become numeric editor inputs.");
    Assert(!SecurityTemplateEditRules.TryDescribe(
        baseRecord with { SourceSha256 = "bad" }, out _),
        "Records without full source SHA-256 evidence must remain read-only.");
    Assert(!SecurityTemplateEditRules.TryDescribe(
        baseRecord with { Value = "6" }, out _),
        "Unsupported current value must be read-only, not normalized.");
}


static void TestImpactPreviewEvidence()
{
    var id = Guid.NewGuid();
    var gpo = new GpoInfo
    {
        Id = id, DisplayName = "Example", DomainName = "test.example",
        ComputerEnabled = true, UserEnabled = false,
        WmiFilterName = "Laptop filter", WmiFilterPath = "CN=Filter,DC=test"
    };
    var links = new[]
    {
        new GpoLinkInfo { GpoId = id, GpoName = "Example",
            TargetName = "Students", TargetDn = "OU=Students,DC=test,DC=example",
            TargetType = "OU", Order = 1, Enabled = true, Enforced = false,
            BlockInheritance = true },
        new GpoLinkInfo { GpoId = id, GpoName = "Example",
            TargetName = "test.example", TargetDn = "DC=test,DC=example",
            TargetType = "Domain", Order = 2, Enabled = false, Enforced = false },
        new GpoLinkInfo { GpoId = Guid.NewGuid(), GpoName = "Different",
            TargetName = "Unrelated", TargetType = "OU", Enabled = true }
    };
    var preview = GpoImpactPreviewService.Build(gpo, links,
        "test.example", "dc.test.example", inventoryComplete: true);
    Assert(preview.DirectLinks.Count == 2 &&
        preview.EnabledLinks == 1 && preview.DisabledLinks == 1,
        "Only direct links belonging to selected GPO may be counted.");
    Assert(preview.ToText().Contains("NOT effective application") &&
        preview.WmiEvidence.Contains("not evaluated", StringComparison.OrdinalIgnoreCase),
        "Link assignment and unevaluated WMI must not be mistaken for effective RSoP.");
    Assert(preview.GpoSections.Contains("User disabled", StringComparison.Ordinal),
        "GPO section enablement must be surfaced.");
    var incomplete = GpoImpactPreviewService.Build(gpo,
        Array.Empty<GpoLinkInfo>(), "test.example", "dc.test.example", false);
    Assert(incomplete.Summary.Contains("INCOMPLETE"),
        "Failed link inventory must never be reported as zero targets.");
}


static void TestSelectiveSecurityRecovery()
{
    var root = Path.Combine(Path.GetTempPath(), "GPOSE-selective-" + Guid.NewGuid().ToString("N"));
    var id = Guid.NewGuid();
    var backupId = Guid.NewGuid();
    var gpo = new GpoInfo { Id = id, DisplayName = "Test Policy",
        DomainName = "test.example", ComputerEnabled = true, UserEnabled = true };
    var backup = new GpoBackupInfo { GpoId = id, BackupId = backupId,
        DomainName = "test.example", DisplayName = "Test Policy",
        BackupDirectory = root };
    var path = Path.Combine(root, backupId.ToString("B").ToUpperInvariant(),
        "DomainSysvol", "GPO", "Machine", "Microsoft", "Windows NT", "SecEdit", "GptTmpl.inf");
    var originalConnection = DomainConnectionState.Profile;
    try
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var backupTemplate = System.Text.Encoding.UTF8.GetBytes(
            "[Version]\r\nSignature=\"$CHICAGO$\"\r\n[Event Audit]\r\nAuditLogonEvents = 3\r\n");
        File.WriteAllBytes(path, backupTemplate);
        DomainConnectionState.SetProfile(
            DomainConnectionProfile.CurrentSession("test.example", "dc.test.example"));
        var currentPath = @"\\dc.test.example\SYSVOL\test.example\Policies\" +
            id.ToString("B") + @"\Machine\Microsoft\Windows NT\SecEdit\GptTmpl.inf";
        var currentSource = SecurityTemplateSourceReader.Parse(
            System.Text.Encoding.UTF8.GetBytes(
                "[Version]\r\nSignature=\"$CHICAGO$\"\r\n[Event Audit]\r\nAuditLogonEvents = 1\r\n"),
            id, "Test Policy", currentPath, new string('A', 64));
        Assert(currentSource.IsComplete,
            "Current test security source should parse cleanly.");
        var live = new RealSettingsScanResult(id, "Test Policy",
            "test.example", "dc.test.example", DateTimeOffset.Now,
            currentSource.Rows, new[]
            {
                new RealSettingsFileEvidence(currentPath, "Read",
                    currentSource.Rows.Count, new string('A', 64), "Synthetic test")
            });
        var plan = GpoSelectiveRecoveryService.Inspect(backup, gpo, live);
        Assert(plan.Candidates.Count == 1 &&
            plan.Candidates[0].CurrentValue == 1 &&
            plan.Candidates[0].BackupValue == 3,
            "Only changed, recognized, existing source key should be recoverable.");
        GpoSelectiveRecoveryService.EnsureBackupUnchanged(plan);
        try
        {
            GpoSelectiveRecoveryService.Inspect(
                new GpoBackupInfo { GpoId = Guid.NewGuid(), BackupId = backupId,
                    DomainName = backup.DomainName, BackupDirectory = root },
                gpo, live);
            throw new InvalidOperationException("Different GPO backup was accepted.");
        }
        catch (InvalidOperationException) { }
        File.AppendAllText(path, "\r\n; Concurrent change");
        try
        {
            GpoSelectiveRecoveryService.EnsureBackupUnchanged(plan);
            throw new InvalidOperationException("Modified backup was accepted.");
        }
        catch (IOException) { }
    }
    finally
    {
        DomainConnectionState.SetProfile(originalConnection);
        try { Directory.Delete(root, recursive: true); } catch { }
    }
}


static void TestGpoEvidenceArchive()
{
    var folder = Path.Combine(Path.GetTempPath(),
        "GPOSE-evidence-" + Guid.NewGuid().ToString("N"));
    var path = Path.Combine(folder, "report.zip");
    try
    {
        var id = Guid.NewGuid();
        var gpo = new GpoInfo
        {
            Id = id, DisplayName = "Test GPO", DomainName = "test.example",
            ComputerEnabled = true, UserEnabled = true
        };
        var record = new RealSettingRecord
        {
            GpoId = id, GpoName = gpo.DisplayName, Scope = "Computer",
            Category = "Registry policy (source file)", SettingName = "FormulaTest",
            Value = "=2+2", SourceFile = "Registry.pol",
            SourceSha256 = new string('B', 64)
        };
        var sources = new RealSettingsScanResult(id, gpo.DisplayName,
            "test.example", "dc.test.example", DateTimeOffset.Now,
            new[] { record }, new[]
            {
                new RealSettingsFileEvidence("Registry.pol", "Read", 1,
                    record.SourceSha256, "synthetic")
            });
        var health = new GpoConsistencyReport(gpo.DisplayName, id,
            "test.example", "dc.test.example", DateTimeOffset.Now,
            new[] { new GpoConsistencyFinding("GPT.INI", "Pass", "synthetic") });
        var impact = GpoImpactPreviewService.Build(gpo,
            Array.Empty<GpoLinkInfo>(), "test.example", "dc.test.example", true);

        GpoEvidenceArchiveService.Export(path, gpo, sources, health, impact,
            "<GPO>synthetic</GPO>");
        using var zip = System.IO.Compression.ZipFile.OpenRead(path);
        var names = zip.Entries.Select(entry => entry.FullName).ToHashSet(
            StringComparer.Ordinal);
        foreach (var expected in new[]
        {
            "README.txt", "sources.json", "settings.csv", "gpmc-report.xml",
            "health.txt", "impact.txt", "sha256sums.txt", "manifest.json"
        })
            Assert(names.Contains(expected), "Evidence ZIP is missing " + expected);

        string Read(string filename)
        {
            using var input = zip.GetEntry(filename)!.Open();
            using var reader = new StreamReader(input);
            return reader.ReadToEnd();
        }

        Assert(Read("settings.csv").Contains("'=2+2"),
            "CSV exported setting must be neutralized against spreadsheet formulas.");
        Assert(Read("manifest.json").Contains("\"CompleteDomainEffectiveRsop\": false") &&
            Read("manifest.json").Contains("ContentSha256"),
            "Manifest must record non-RSoP scope and SHA-256 evidence.");
        Assert(Read("sha256sums.txt").Contains("sources.json"),
            "Evidence source checksums must be included.");

        try
        {
            GpoEvidenceArchiveService.Export(Path.Combine(folder, "bad.zip"),
                new GpoInfo { Id = Guid.NewGuid(), DomainName = "test.example" },
                sources, health, impact, null);
            throw new InvalidOperationException("Different GPO evidence was mixed.");
        }
        catch (InvalidOperationException) { }
    }
    finally
    {
        try { Directory.Delete(folder, recursive: true); } catch { }
    }
}


static void TestGppXmlEvidence()
{
    var source = System.Text.Encoding.UTF8.GetBytes(
        "<Drives><Drive name=\"H:\" uid=\"{A}\" disabled=\"0\">" +
        "<Properties action=\"U\" path=\"\\\\fileserver\\home\" " +
        "cpassword=\"DO_NOT_DISCLOSE\" apiToken=\"EXPOSE_TOKEN\" " +
        "privateKey=\"EXPOSE_KEY\"/>" +
        "<Filters><FilterGroup name=\"Students\"/></Filters>" +
        "</Drive></Drives>");
    var parsed = GppXmlSourceReader.Parse(source, Guid.NewGuid(), "Test",
        "User", "Drives.xml", new string('A', 64));
    Assert(parsed.IsComplete && parsed.Rows.Count == 1,
        "A single GPP Drive item should be projected from stored XML.");
    Assert(parsed.Rows[0].Value.Contains("fileserver") &&
        !parsed.Rows[0].Value.Contains("DO_NOT_DISCLOSE") &&
        !parsed.Rows[0].Value.Contains("EXPOSE_TOKEN") &&
        !parsed.Rows[0].Value.Contains("EXPOSE_KEY") &&
        parsed.Rows[0].Value.Contains("[REDACTED IN EVIDENCE]") &&
        parsed.Rows[0].Evidence.Contains("NOT evaluated"),
        "Stored properties should be visible, but passwords and ILT must not leak or be inferred.");
    var malicious = GppXmlSourceReader.Parse(
        System.Text.Encoding.UTF8.GetBytes(
            "<!DOCTYPE foo [<!ENTITY x SYSTEM \"file:///C:/secret\">]>" +
            "<Drives>&x;</Drives>"),
        Guid.NewGuid(), "Test", "User", "Drives.xml", "00");
    Assert(!malicious.IsComplete && malicious.Rows.Count == 0,
        "DTD and external entity references must be rejected.");
    Assert(!GppXmlSourceReader.Parse(
        new byte[GppXmlSourceReader.MaxFileBytes + 1],
        Guid.NewGuid(), "Test", "User", "Drives.xml", "00").IsComplete,
        "Oversized GPP XML must fail closed.");
}


static void TestCrossDcVersionEvidence()
{
    var dcs = GpoCrossDcConsistencyService.ValidateControllers(
        "dc01.test.example,\ndc02.test.example", "test.example");
    Assert(dcs.Length == 2 && dcs[0] == "dc01.test.example",
        "Explicit DC host list must preserve distinct named controllers.");
    foreach (var bad in new[]
    {
        "test.example", @"\\dc01.test.example", "dc01.test.example/../x",
        "dc01..test.example"
    })
    {
        try
        {
            GpoCrossDcConsistencyService.ValidateControllers(bad, "test.example");
            throw new InvalidOperationException("Invalid DC name was accepted: " + bad);
        }
        catch (ArgumentException) { }
    }
    var versionOk = new[]
    {
        new GpoDcVersionEvidence("dc01.test.example", 65538, 65538,
            new string('A', 64), "test", "test"),
        new GpoDcVersionEvidence("dc02.test.example", 65538, 65538,
            new string('A', 64), "test", "test")
    };
    Assert(GpoCrossDcConsistencyService.Assess(versionOk).Contains("VERSION MATCH") &&
        GpoCrossDcConsistencyService.Assess(versionOk).Contains("NOT a full"),
        "Matching version numbers on two DCs are evidence, not full replication proof.");
    Assert(GpoCrossDcConsistencyService.Assess(
        new[] { versionOk[0], versionOk[1] with { GptVersion = 65537 } })
        .Contains("MISMATCH"),
        "A DC-local mismatch must be reported before cross-DC equality.");
    Assert(GpoCrossDcConsistencyService.Assess(
        new[] { versionOk[0], versionOk[1] with { GptVersion = null } })
        .Contains("INCOMPLETE"),
        "Unavailable DC version must never be treated as consistent.");
}


static void TestSafeRegistryBooleanSecurityEdit()
{
    var target = @"MACHINE\Software\Policies\Example\EnableFeature";
    var original = "[Version]\r\nsignature=\"$CHICAGO$\"\r\n" +
        "[Registry Values]\r\n" + target + "=4,1\r\n" +
        @"MACHINE\Software\Policies\Example\Unrelated=4,0" + "\r\n";
    var changed = SecurityTemplateEditRules.ChangeExistingRegistryBoolean(
        original, target, expectedOld: true, proposed: false);
    Assert(changed.Contains(target + "=4,0\r\n") &&
        changed.Contains("Unrelated=4,0\r\n") &&
        changed.StartsWith("[Version]\r\n", StringComparison.Ordinal),
        "Only the exactly matched DWORD entry should be updated.");
    foreach (var invalid in new[]
    {
        original.Replace(target + "=4,1", target + "=3,1"),
        original.Replace(target + "=4,1", target + "=4,2"),
        original.Replace(target + "=4,1", target + "=4,0"),
        original.Replace(target + "=4,1", ""),
        original + target + "=4,1\r\n"
    })
    {
        try
        {
            SecurityTemplateEditRules.ChangeExistingRegistryBoolean(
                invalid, target, expectedOld: true, proposed: false);
            throw new InvalidOperationException(
                "Malformed or ambiguous DWORD was permitted.");
        }
        catch (InvalidDataException) { }
    }
}

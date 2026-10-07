using GPOSettingsExplorer.Models;
using GPOSettingsExplorer.Services;

var tests = new (string Name, Action Body)[]
{
    ("Script sanitizer removes Markdown fences", TestScriptSanitizer),
    ("Domain connection pins LDAP and SYSVOL", TestDomainConnectionPaths),
    ("DPAPI current-user round trip", TestDpapiRoundTrip),
    ("GPP XML cache refreshes after file change", TestGppXmlCache),
    ("Script inventory cache invalidates on GPO modification", TestScriptCache)
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

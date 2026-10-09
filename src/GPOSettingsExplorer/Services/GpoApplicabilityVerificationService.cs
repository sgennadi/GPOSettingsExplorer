using System.Diagnostics;
using System.DirectoryServices;
using System.Security.AccessControl;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using GPOSettingsExplorer.Models;

namespace GPOSettingsExplorer.Services;

/// <summary>
/// Read-only, per-computer evidence for a configured GPO conflict.
/// A successful sample never establishes identical domain-wide applicability.
/// </summary>
public sealed record GpoApplicabilityVerification(
    string Status,
    string RsopEvidence,
    string WmiEvidence,
    string SecurityEvidence,
    string Recommendation);

public sealed class GpoApplicabilityVerificationService
{
    private sealed record Check(bool Passed, bool Failed, string Details);
    private static readonly Regex GuidInPath = new(
        @"\{[0-9a-fA-F-]{36}\}", RegexOptions.Compiled);

    public GpoApplicabilityVerification Verify(
        GpoConflictInfo finding,
        IReadOnlyList<GpoInfo> gpos,
        IReadOnlyList<WmiFilterInfo> filters,
        string domainDistinguishedName,
        string computerName,
        string targetUser = "")
    {
        if (finding.GpoIds.Count < 2)
            throw new ArgumentException("At least two GPOs are required.");
        computerName = computerName.Trim();
        if (!Regex.IsMatch(computerName, @"^[a-zA-Z0-9_.:-]{1,253}$"))
            throw new ArgumentException("Enter a valid computer name or IP address.");
        if (finding.Scope.Equals("User", StringComparison.OrdinalIgnoreCase) &&
            string.IsNullOrWhiteSpace(targetUser))
            throw new ArgumentException("User scope requires DOMAIN\\user for the RSoP query.");

        var participating = finding.GpoIds
            .Select(id => gpos.FirstOrDefault(g => g.Id == id))
            .ToArray();
        if (participating.Any(g => g is null))
            throw new InvalidOperationException("Reload GPOs before verifying: a GPO is missing.");

        var policies = participating.Select(g => g!).ToArray();
        var active = policies.All(g =>
            finding.Scope.Equals("User", StringComparison.OrdinalIgnoreCase)
                ? g.UserEnabled
                : g.ComputerEnabled);

        // Each independent check can fail. Failure must never mean 'passed'.
        var wmi = CheckWmi(policies, filters, computerName);
        var security = CheckSecurity(policies, domainDistinguishedName);
        var rsop = QueryRsop(computerName, targetUser, finding.Scope, finding.GpoIds);

        var failed = !active || wmi.Failed || security.Failed || rsop.Failed;
        var verified = active && wmi.Passed && security.Passed && rsop.Passed;

        var status = failed
            ? "Blocked - applicability differs"
            : verified
                ? "Sample checked - not domain-wide"
                : "Incomplete - do not consolidate";

        var recommendation = !active
            ? "NO CONSOLIDATION: at least one required GPO configuration section is disabled."
            : finding.Kind != "Duplicate"
                ? "DIFFERING VALUES: review winning settings and processing order; never consolidate based only on matching scope."
                : verified
                    ? "REVIEW-ONLY CANDIDATE: both GPOs applied on this one computer and the tested WMI/DACL snapshots agree. " +
                      "This does NOT prove identical targets domain-wide, the winning setting or client-side extension behavior. " +
                      "Test representative computers/users in every affected OU and site, examine loopback, inheritance and all links; " +
                      "back up each GPO separately before any explicitly authorized manual edit. No automatic merge or unlink."
                    : "NO CONSOLIDATION RECOMMENDATION: resolve every missing/failed RSoP, WMI and Security Filtering check, " +
                      "then validate representative machines across all affected OUs/sites. No automatic changes.";

        if (!active)
            status = "Blocked - GPO scope disabled";

        return new GpoApplicabilityVerification(
            status,
            (active ? "" : "At least one GPO section is disabled. ") + rsop.Details,
            wmi.Details,
            security.Details,
            recommendation);
    }

    private static Check CheckWmi(
        IReadOnlyList<GpoInfo> gpos,
        IReadOnlyList<WmiFilterInfo> filters,
        string computer)
    {
        var paths = gpos.Select(g => g.WmiFilterPath.Trim()).ToArray();
        if (paths.Distinct(StringComparer.OrdinalIgnoreCase).Count() != 1)
            return new Check(false, true,
                "WMI filter assignments differ between the GPOs. Matching OU links do not override this.");

        if (paths[0].Length == 0)
            return new Check(true, false, "Both GPOs have no WMI filter.");

        var filter = filters.FirstOrDefault(f =>
            !string.IsNullOrWhiteSpace(f.Id) &&
            paths[0].Contains(f.Id, StringComparison.OrdinalIgnoreCase));

        if (filter is null)
            return new Check(false, false,
                "The referenced WMI filter was not loaded or cannot be identified; evaluation is unknown.");

        try
        {
            if (filter.Rules.Count == 0)
                return new Check(false, false, "The WMI filter has no loaded WQL rules.");
            var checks = new WmiFilterService().Test(filter, computer);
            var errors = checks.Where(c => !string.IsNullOrWhiteSpace(c.Error)).ToArray();
            if (errors.Length > 0)
                return new Check(false, false,
                    "Remote WMI evaluation failed: " +
                    string.Join("; ", errors.Select(c => $"Rule {c.RuleNumber}: {c.Error}")));
            if (checks.Any(c => !c.IsMatch))
                return new Check(false, true,
                    "WMI filter did not match this computer: " +
                    string.Join(", ", checks.Where(c => !c.IsMatch).Select(c => $"rule {c.RuleNumber}")));
            return new Check(true, false,
                $"WMI filter '{filter.Name}': all {checks.Count} rule(s) matched on {computer}. " +
                "A result on one computer does not prove the filter matches other computers.");
        }
        catch (Exception ex)
        {
            return new Check(false, false, "WMI evaluation unavailable: " + ex.Message);
        }
    }

    private static Check CheckSecurity(
        IReadOnlyList<GpoInfo> gpos,
        string domainDistinguishedName)
    {
        try
        {
            var acls = new List<string>();
            foreach (var policy in gpos)
            {
                var path = DomainConnectionState.BuildLdapPath(
                    $"CN={policy.Id:B},CN=Policies,CN=System,{domainDistinguishedName}");
                using var entry = new DirectoryEntry(path);
                entry.Options.SecurityMasks = SecurityMasks.Dacl;
                var dacl = entry.ObjectSecurity.GetSecurityDescriptorSddlForm(
                    AccessControlSections.Access);
                if (string.IsNullOrWhiteSpace(dacl))
                    return new Check(false, false, "A GPO DACL could not be read.");
                acls.Add(dacl);
            }

            if (acls.Distinct(StringComparer.Ordinal).Count() != 1)
                return new Check(false, true,
                    "Security Filtering/Delegation: AD GPO DACLs differ. Review Apply/Read grants, " +
                    "denies, group membership and SYSVOL ACLs before any consolidation.");

            return new Check(true, false,
                "Security Filtering: AD GPO DACLs match exactly. " +
                "This is not a substitute for effective target token membership, SYSVOL access or RSoP.");
        }
        catch (Exception ex)
        {
            return new Check(false, false,
                "Security Filtering DACL comparison could not complete: " + ex.Message);
        }
    }

    private static Check QueryRsop(
        string computer, string user, string scope, IReadOnlyList<Guid> ids)
    {
        var filename = Path.Combine(Path.GetTempPath(),
            "GPOSE-rsop-" + Guid.NewGuid().ToString("N") + ".xml");
        try
        {
            var start = new ProcessStartInfo
            {
                FileName = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.System), "gpresult.exe"),
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            if (computer != "." &&
                !computer.Equals("localhost", StringComparison.OrdinalIgnoreCase))
            {
                start.ArgumentList.Add("/S");
                start.ArgumentList.Add(computer);
            }
            if (scope.Equals("User", StringComparison.OrdinalIgnoreCase))
            {
                start.ArgumentList.Add("/USER");
                start.ArgumentList.Add(user.Trim());
            }
            start.ArgumentList.Add("/SCOPE");
            start.ArgumentList.Add(scope.Equals("User", StringComparison.OrdinalIgnoreCase)
                ? "USER" : "COMPUTER");
            start.ArgumentList.Add("/X");
            start.ArgumentList.Add(filename);
            start.ArgumentList.Add("/F");

            using var process = Process.Start(start) ??
                throw new InvalidOperationException("gpresult could not start.");
            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(60000))
            {
                process.Kill(entireProcessTree: true);
                return new Check(false, false,
                    "RSoP timed out after 60 seconds. No equivalence was established.");
            }
            var output = outputTask.GetAwaiter().GetResult();
            var error = errorTask.GetAwaiter().GetResult();
            if (process.ExitCode != 0 || !File.Exists(filename))
                return new Check(false, false,
                    $"gpresult failed (exit code {process.ExitCode}): " +
                    (string.IsNullOrWhiteSpace(error) ? output : error).Trim());

            var rsop = XDocument.Load(filename);
            return AssessRsop(rsop, scope, ids);
        }
        catch (Exception ex)
        {
            return new Check(false, false,
                "RSoP unavailable (permissions, firewall, logged-on user or remote service): " +
                ex.Message);
        }
        finally
        {
            try { File.Delete(filename); } catch { }
        }
    }

    /// <summary>Pure parser used by regression tests. Only GPO elements directly
    /// under the requested scope are accepted; nested Policy/GPO references
    /// are not the list of applied GPOs.</summary>
    public static (bool AllApplied, bool AnyExcluded, string Details) AssessRsopXml(
        string xml, string scope, IReadOnlyList<Guid> ids)
    {
        var result = AssessRsop(XDocument.Parse(xml), scope, ids);
        return (result.Passed, result.Failed, result.Details);
    }

    private static Check AssessRsop(
        XDocument report, string scope, IReadOnlyList<Guid> ids)
    {
        var sectionName = scope.Equals("User", StringComparison.OrdinalIgnoreCase)
            ? "UserResults" : "ComputerResults";
        var section = report.Root?.Elements()
            .FirstOrDefault(e => e.Name.LocalName.Equals(sectionName,
                StringComparison.OrdinalIgnoreCase));

        if (section is null)
            return new Check(false, false,
                "gpresult XML has no " + sectionName + " section. RSoP could not be verified.");

        var entries = section.Elements()
            .Where(e => e.Name.LocalName.Equals("GPO", StringComparison.OrdinalIgnoreCase))
            .Select(e => new
            {
                Id = Identify(e),
                FilterAllowed = ReadBool(e, "FilterAllowed"),
                AccessDenied = ReadBool(e, "AccessDenied"),
                Enabled = ReadBool(e, "Enabled"),
                IsValid = ReadBool(e, "IsValid")
            })
            .ToArray();

        var details = new List<string>();
        var allApplied = true;
        var excluded = false;

        foreach (var id in ids)
        {
            var matches = entries.Where(e => e.Id == id).ToArray();
            if (matches.Length != 1)
            {
                details.Add(matches.Length == 0
                    ? $"{id:B}: not identified in the scope GPO list (unknown, NOT proof of filtering)"
                    : $"{id:B}: duplicated GPO records in the scope list; ambiguous result");
                allApplied = false;
                continue;
            }
            var match = matches[0];

            if (match.FilterAllowed == false || match.AccessDenied == true ||
                match.Enabled == false || match.IsValid == false)
            {
                details.Add($"{id:B}: not applied (filter, permission or scope)");
                excluded = true;
                allApplied = false;
            }
            else if (match.FilterAllowed == true && match.AccessDenied == false &&
                     match.Enabled == true && match.IsValid == true)
                details.Add($"{id:B}: applied on this sample");
            else
            {
                details.Add($"{id:B}: XML has insufficient applied/filter status flags");
                allApplied = false;
            }
        }

        return new Check(allApplied, excluded,
            "gpresult logged RSoP (" + sectionName + "): " +
            string.Join("; ", details) +
            ". This is a sample of the last policy processing, not a full domain scope proof.");
    }

    private static Guid? Identify(XElement element)
    {
        foreach (var key in new[] { "Identifier", "ID", "Path" })
        {
            var value = element.Elements()
                .FirstOrDefault(e => e.Name.LocalName.Equals(key,
                    StringComparison.OrdinalIgnoreCase))?.Value;
            if (Guid.TryParse(value, out var direct))
                return direct;
            var match = GuidInPath.Match(value ?? "");
            if (match.Success && Guid.TryParse(match.Value, out var embedded))
                return embedded;
        }
        return null; // GPO names are not guaranteed unique; never infer an ID.
    }

    private static bool? ReadBool(XElement element, string name)
    {
        var value = element.Elements()
            .FirstOrDefault(e => e.Name.LocalName.Equals(name,
                StringComparison.OrdinalIgnoreCase))?.Value;
        return bool.TryParse(value, out var result) ? result : null;
    }
}

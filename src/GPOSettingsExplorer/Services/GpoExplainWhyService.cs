using GPOSettingsExplorer.Models;

namespace GPOSettingsExplorer.Services;

/// <summary>Per-computer evidence explanation, not a predictive RSoP simulator.</summary>
public sealed record GpoExplanationCheck(
    string Area, string Level, string Finding, string NextAction);

public sealed record GpoExplanationReport(
    Guid GpoId, string GpoName, string Computer, string Scope,
    string Overall, DateTimeOffset CreatedUtc,
    IReadOnlyList<GpoExplanationCheck> Checks)
{
    public string ToText() =>
        "GPO EXPLAIN WHY - READ ONLY, ONE CLIENT\n" +
        "Captured UTC: " + CreatedUtc.ToString("O") +
        "\nGPO: " + GpoName + " (" + GpoId.ToString("B") + ")" +
        "\nComputer: " + Computer + " | Scope: " + Scope +
        "\n\nOVERALL: " + Overall +
        "\nLogged RSoP describes the most recent observed processing on one client, " +
        "not a future-policy simulation.\n\n" +
        string.Join("\n\n", Checks.Select(c =>
            "[" + c.Level + "] " + c.Area + "\n" + c.Finding +
            "\nNext check: " + c.NextAction)) +
        "\n\nNo automatic gpupdate, GPO change, ACL change, link merge, " +
        "DFSR repair or client-policy reset is performed.";
}

/// <summary>
/// Joins bounded independent sources with distinct evidence levels.
/// Missing RSoP, ACL context, WMI/client logs or AD location never implies
/// filtering. Site / group membership / loopback remain unresolved.
/// </summary>
public static class GpoExplainWhyService
{
    public static GpoExplanationReport Build(
        GpoInfo gpo, string scope, GpoRsopSample sample,
        GpoClientScopeReport? location, GpoClientEventReport? events,
        string locationError = "", string eventError = "")
    {
        ArgumentNullException.ThrowIfNull(gpo);
        ArgumentNullException.ThrowIfNull(sample);
        if (scope is not ("Computer" or "User"))
            throw new ArgumentException("Scope must be Computer or User.");
        var checks = new List<GpoExplanationCheck>();

        var configEnabled = scope == "Computer" ? gpo.ComputerEnabled : gpo.UserEnabled;
        checks.Add(new(
            "Current GPO configuration section",
            configEnabled ? "Configured enabled" : "Current blocker",
            configEnabled
                ? scope + " Configuration is enabled in the current GPO metadata."
                : scope + " Configuration is disabled in current AD GPO metadata. " +
                  "Historical RSoP may refer to an earlier state.",
            configEnabled
                ? "Validate replication and actual client processing."
                : "Review GPO Status in GPMC; do not enable it without change approval."));

        var applied = sample.Status.Equals(
            "Applied (logged sample)", StringComparison.Ordinal);
        var excluded = sample.Status.Equals(
            "Excluded (logged sample)", StringComparison.Ordinal);
        checks.Add(new("Last logged gpresult / RSoP",
            applied ? "Observed applied" : excluded ? "Observed excluded" : "Unknown",
            sample.Details,
            applied
                ? "Check individual winning settings/CSE processing; applied GPO does " +
                  "not prove every stored setting succeeded."
                : excluded
                    ? "Inspect gpresult flags and filtering on this exact client; " +
                      "the sample does not identify a unique root cause."
                    : "Run gpresult.exe locally or with remote administrative rights; " +
                      "verify Group Policy processing and permissions."));

        if (location is null)
            checks.Add(new("Computer OU / domain inheritance", "Unknown",
                string.IsNullOrWhiteSpace(locationError)
                    ? "Computer AD location was not inspected."
                    : "AD path inspection unavailable: " + locationError,
                "Check the computer's AD location, direct and inherited GPO links, " +
                "block inheritance, enforced links and site assignment."));
        else
        {
            var candidates = location.Links.Count(l =>
                l.Status == "Enabled path candidate");
            checks.Add(new("Computer OU / domain inheritance",
                !location.Complete ? "Unknown" :
                candidates > 0 ? "Path candidate" : "No enabled path candidate",
                location.Summary + " Pinned DC: " + location.PinnedDc +
                "; computer DN: " + location.ComputerDn +
                ". Site links are not included.",
                "Compare site links, alternate OU paths and client gpresult. " +
                "OU path alone never establishes effective policy."));
        }

        if (string.IsNullOrWhiteSpace(gpo.WmiFilterPath))
            checks.Add(new("WMI filter assignment", "No WMI filter assigned",
                "The selected GPO metadata has no WMI filter assignment.",
                "No WMI query needed; other filtering and CSE processing still apply."));
        else
            checks.Add(new("WMI filter assignment", "Unknown",
                "WMI filter assigned: " +
                (string.IsNullOrWhiteSpace(gpo.WmiFilterName)
                    ? gpo.WmiFilterPath : gpo.WmiFilterName) +
                ". The WQL conditions were NOT executed on the target.",
                "Test each WQL rule from the WMI Filters tab on this client " +
                "using authorized remote WMI rights; verify results against RSoP."));

        checks.Add(new("Security Filtering / target token", "Not evaluated",
            "Current GPO metadata and OU links do not prove target Read+Apply " +
            "permissions, group nesting, explicit denies, or SYSVOL NTFS access.",
            "Check the GPO Security Filtering/Delegation view, computer/user " +
            "token groups and effective access. Verify SYSVOL on the pinned DC."));

        if (scope == "User")
            checks.Add(new("User OU and loopback processing", "Unknown",
                "Computer OU scanning does not establish user account OU, loopback " +
                "Replace/Merge processing, or site of the target user.",
                "Inspect UserResults and ComputerResults in gpresult, target user OU, " +
                "and loopback configuration on the workstation."));

        if (events is null)
            checks.Add(new("Client GroupPolicy Operational events", "Unknown",
                string.IsNullOrWhiteSpace(eventError)
                    ? "Event collection was not requested or produced no evidence."
                    : eventError,
                "Query the GroupPolicy/Operational log on this client. " +
                "Verify Event Log service, remote permissions and firewall."));
        else
        {
            var errors = events.Events.Count(e => e.Level is 1 or 2);
            var warnings = events.Events.Count(e => e.Level == 3);
            var starts = events.Events.Count(e => e.EventId == 4016);
            var finishes = events.Events.Count(e => e.EventId == 5016);
            checks.Add(new("Client GroupPolicy Operational events",
                events.Events.Count == 0 ? "Unknown" :
                    errors > 0 ? "Client errors observed" :
                    warnings > 0 ? "Client warnings observed" : "Event metadata available",
                events.Events.Count + " recent events returned; errors " + errors +
                ", warnings " + warnings + ", CSE start ID 4016 " + starts +
                ", CSE completion ID 5016 " + finishes +
                ". These are NOT tied to the selected GPO or a unique processing " +
                "instance; absence of a completion event in this bounded window " +
                "does not establish failure.",
                errors + warnings > 0
                    ? "Open Event Viewer on the target and inspect the full event details " +
                      "and ActivityID correlation for the same processing session."
                    : "Use Event Viewer with ActivityID correlation to validate " +
                      "specific extension processing; event counts alone are not success."));
        }

        var overall = !configEnabled
            ? "CURRENT GPO SECTION DISABLED. The last logged client state " +
              "may be older; no future-policy conclusion."
            : excluded
                ? "GPO EXCLUDED IN LAST LOGGED CLIENT SAMPLE. " +
                  "The exact filter/ACL/WMI cause requires further evidence."
                : applied
                    ? "GPO APPLIED IN LAST LOGGED CLIENT SAMPLE. " +
                      "Individual policy/CSE success not established."
                    : "UNKNOWN / INCOMPLETE: no confirmed logged RSoP verdict. " +
                      "Do NOT infer applied, excluded or safe-to-merge.";

        return new GpoExplanationReport(
            gpo.Id, gpo.DisplayName, sample.Computer, scope,
            overall, DateTimeOffset.UtcNow, checks);
    }
}

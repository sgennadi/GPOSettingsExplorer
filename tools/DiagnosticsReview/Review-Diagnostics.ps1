param(
    [string]$Repository = "sgennadi/GPOSettingsExplorer",
    [string]$OutputDirectory = "artifacts/diagnostics-review",
    [int]$RecentDays = 7,
    [string]$InputJson = ""
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Get-Issues {
    if ($InputJson) {
        return @(Get-Content -LiteralPath $InputJson -Raw -Encoding utf8 |
            ConvertFrom-Json -Depth 40)
    }

    if (-not $env:GH_TOKEN) {
        throw "GH_TOKEN is required to retrieve GitHub diagnostics."
    }

    $headers = @{
        Authorization = "Bearer $($env:GH_TOKEN)"
        Accept = "application/vnd.github+json"
        "X-GitHub-Api-Version" = "2022-11-28"
        "User-Agent" = "GPOSettingsExplorer-DiagnosticsReview"
    }
    $all = [System.Collections.Generic.List[object]]::new()
    for ($page = 1; $page -le 15; $page++) {
        $uri = "https://api.github.com/repos/$Repository/issues?state=all&per_page=100&page=$page"
        $pageIssues = @(Invoke-RestMethod -Uri $uri -Headers $headers -Method Get -TimeoutSec 40)
        foreach ($item in $pageIssues) { $all.Add($item) }
        if ($pageIssues.Count -lt 100) { return $all.ToArray() }
    }
    Write-Warning "Issue scan limited to 1,500 most recent issues; inspect earlier pages if needed."
    return $all.ToArray()
}

function Is-DiagnosticIssue($issue) {
    if ($issue.PSObject.Properties.Name -contains "pull_request" -and $null -ne $issue.pull_request) {
        return $false
    }
    if ([string]$issue.title -match '^\[(Diagnostics|GPO-DIAG)\]') {
        return $true
    }
    foreach ($label in @($issue.labels)) {
        if ([string]$label.name -match '^(diagnostic|mmc-diagnostic|gposettingsexplorer-diagnostic)$') {
            return $true
        }
    }
    return $false
}

function Sha256-Text([string]$value) {
    $bytes = [Text.Encoding]::UTF8.GetBytes($value)
    return [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes))
}

function Get-Fingerprint($issue) {
    $body = [string]$issue.body
    $match = [regex]::Match($body,
        '(?im)^\s*(?:Diagnostic-Fingerprint|Fingerprint)\s*:\s*([a-f0-9]{16,64})\s*$')
    if ($match.Success) { return $match.Groups[1].Value.ToUpperInvariant() }

    # Legacy reports contain no explicit fingerprint. Keep unrelated issues
    # separate unless a recognizable Context/Operation and exception exist.
    $context = [regex]::Match($body,
        '(?im)^\s*(?:Context|Operation):\s*(.{3,140})\s*$')
    $exception = [regex]::Match($body,
        '(?im)^\s*([A-Za-z_][\w.]*(?:Exception|Error))\s*:')
    if ($context.Success -and $exception.Success) {
        $identity = ($context.Groups[1].Value.Trim() + "|" +
            $exception.Groups[1].Value.Trim()).ToLowerInvariant()
        return Sha256-Text $identity
    }
    # A generic issue title is not proof of identical technical problems.
    return Sha256-Text ("issue:" + [string]$issue.number)
}

function Get-ValidVersions([string]$body) {
    $matches = [regex]::Matches($body,
        '(?im)^\s*(?:App-Version|Version):\s*v?(\d+\.\d+\.\d+(?:\.\d+)?)\s*$')
    return @($matches | ForEach-Object { $_.Groups[1].Value } |
        Sort-Object -Unique)
}

function Get-ReporterId([string]$body) {
    $match = [regex]::Match($body,
        '(?im)^\s*Reporter-ID:\s*([a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12})\s*$')
    if ($match.Success) { return $match.Groups[1].Value.ToLowerInvariant() }
    return $null
}

$now = [DateTimeOffset]::UtcNow
$cutoff = $now.AddDays(-[math]::Max(1, $RecentDays))
$issues = @(Get-Issues | Where-Object { $null -ne $_ })
$diagnostics = @($issues | Where-Object { Is-DiagnosticIssue $_ })
$rows = foreach ($issue in $diagnostics) {
    $updated = [DateTimeOffset]::Parse([string]$issue.updated_at)
    [pscustomobject]@{
        Number = [int]$issue.number
        State = [string]$issue.state
        Fingerprint = Get-Fingerprint $issue
        Versions = @(Get-ValidVersions ([string]$issue.body))
        ReporterId = Get-ReporterId ([string]$issue.body)
        UpdatedUtc = $updated
        Recent = $updated -ge $cutoff
    }
}

$signatures = @($rows | Group-Object Fingerprint | ForEach-Object {
    $group = @($_.Group)
    $open = @($group | Where-Object State -eq "open")
    $reporters = @($group.ReporterId | Where-Object { $_ } | Sort-Object -Unique)
    [pscustomobject]@{
        Fingerprint = [string]$_.Name
        OpenReports = $open.Count
        ClosedReports = $group.Count - $open.Count
        TotalReports = $group.Count
        UniqueAnonymousReporters = $reporters.Count
        Versions = @($group.Versions | ForEach-Object { $_ } | Sort-Object -Unique)
        IssueNumbers = @($group.Number | Sort-Object)
        LastSeenUtc = ($group | Sort-Object UpdatedUtc -Descending |
            Select-Object -First 1).UpdatedUtc.ToString("O")
        RecentlyUpdated = @($group | Where-Object Recent).Count -gt 0
    }
} | Sort-Object -Property @{Expression="OpenReports"; Descending=$true},
    @{Expression="TotalReports"; Descending=$true}, Fingerprint)

$summary = [pscustomobject]@{
    SchemaVersion = 1
    GeneratedUtc = $now.ToString("O")
    Repository = $Repository
    IssueCountScanned = $issues.Count
    DiagnosticReports = $diagnostics.Count
    OpenReports = @($rows | Where-Object State -eq "open").Count
    RecentlyUpdatedReports = @($rows | Where-Object Recent).Count
    Signatures = $signatures
}

New-Item -Path $OutputDirectory -ItemType Directory -Force | Out-Null
$jsonPath = Join-Path $OutputDirectory "diagnostics-summary.json"
$mdPath = Join-Path $OutputDirectory "diagnostics-summary.md"
$summary | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $jsonPath -Encoding utf8

$md = [System.Collections.Generic.List[string]]::new()
$md.Add("## GPO Settings Explorer diagnostic intake")
$md.Add("")
$md.Add("Repository: \`$Repository\`")
$md.Add("Generated (UTC): $($now.ToString("yyyy-MM-dd HH:mm:ss"))")
$md.Add("**$($summary.OpenReports) open report(s)** across $($signatures.Count) diagnostic fingerprint(s); $($summary.RecentlyUpdatedReports) report(s) updated in the last $RecentDays day(s).")
$md.Add("")
if ($signatures.Count -eq 0) {
    $md.Add("No diagnostic GitHub Issues found. Reports must have a \`[Diagnostics]\` title or diagnostic label.")
}
else {
    $md.Add("| Fingerprint | Open | Closed | Recent | Versions | Issues |")
    $md.Add("|---|---:|---:|:---:|---|---|")
    foreach ($item in $signatures) {
        $fingerprint = $item.Fingerprint.Substring(0, [math]::Min(12, $item.Fingerprint.Length))
        $versions = if ($item.Versions.Count) {
            ($item.Versions | Select-Object -First 5) -join ", "
        } else { "unknown" }
        $links = @($item.IssueNumbers | Select-Object -First 8 | ForEach-Object {
            "[#$($_)](https://github.com/$Repository/issues/$_)"
        }) -join ", "
        $recent = if ($item.RecentlyUpdated) { "Yes" } else { "No" }
        $md.Add("| $fingerprint | $($item.OpenReports) | $($item.ClosedReports) | $recent | $versions | $links |")
    }
}
$md.Add("")
$md.Add("**Safety:** Issues are public. This artifact includes fingerprints, versions, counts and issue links only. It does not echo issue bodies, usernames, AD paths, domains, hostnames or SYSVOL contents.")
$md.Add("**Scope:** CI performs deterministic intake and deduplication, not AI code review or a claim that a defect is fixed.")
$mdText = $md -join [Environment]::NewLine
Set-Content -LiteralPath $mdPath -Value $mdText -Encoding utf8

if ($env:GITHUB_STEP_SUMMARY) {
    Add-Content -LiteralPath $env:GITHUB_STEP_SUMMARY -Value $mdText -Encoding utf8
}
Write-Host "Diagnostic review: $($summary.OpenReports) open report(s), $($signatures.Count) signature(s), $($summary.RecentlyUpdatedReports) updated recently."
Write-Host "Report: $mdPath"

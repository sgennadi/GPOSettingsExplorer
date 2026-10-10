# Changelog

## Unreleased

- No unreleased changes yet.

## 1.0.1 - 2026-10-10

- Stop treating GPMC `RestoreGPO` / `IGPMResult.OverallStatus()` exceptions or missing results as success. Invoke the HRESULT-returning COM method directly and propagate failures; never fall back to status code zero.
- Require a canonical domain-scoped `gPCFileSysPath` structure and matching GPO GUID in both pinned-DC and multi-DC health reports. Never read the advertised UNC path.
- Enforce the 64 KiB GPT.INI limit during multi-DC file reads, not only with a pre-read length snapshot.
- Add regression coverage for GPMC status failures, corrupted/mismatched SYSVOL paths and bounded GPT.INI file reads.
- Windows CI remains a code/build check; live AD/GPMC restore and network SYSVOL behavior still require controlled nonproduction DC validation.

## 1.0.0 - 2026-10-10

- Build on v0.8.0 with bounded, read-only curated GPP XML attribute evidence from stored Machine/User Preferences paths. Sensitive password-like attributes are redacted; no GPP/ILT execution inference.
- Add finite-choice Security Settings edits for existing Event Audit and two System Access fields only. Require WRITE ENABLED, completed GPMC safety backup manifest, same-domain/selected DC source path, SHA-256 precondition and mandatory preview. Unknown/ambiguous or unsupported values remain read-only.
- Add read-only GPO Health Check comparing AD GPC versionNumber to SYSVOL GPT.INI and source file health on the pinned DC, with copy/TXT export.
- Add explicit multi-DC version comparison for up to 16 named DCs, including unknown/mismatch states and GPT.INI SHA-256 fingerprint differences; no automated repairs or assumption of full DFSR convergence.
- Add read-only link impact footprint and optional single-client logged gpresult/RSoP observation; never claim domain-wide effective policy or future impact prediction.
- Add local-only comprehensive evidence ZIP (GPMC report, source JSON/CSV, health/impact, SHA-256, coverage manifest), with confidentiality warning and no automatic uploads.
- Add limited selective recovery of ONE existing, vetted Security Settings numeric value from a matching original-domain GPMC backup, a fresh safety backup, precondition checks and confirmation. This does not restore ACLs, rights, missing settings, scripts, GPP, links, WMI or Registry.pol.
- Extend CoreTests for GPT.INI, GPP XML safe parsing, SecEdit editor, cross-DC status, impact, local ZIP and selective recovery. Windows CI builds portable x64/ARM64.
- NOTE: Domain-controller live smoke testing is still required before production writes. Values from source files are not RSoP; cross-DC matching version numbers do not prove full content replication.

## 0.8.0 - 2026-10-10

- Introduce **Real Settings Engine (phase 1)** as a read-only direct GPO source inspector in **All Settings**, without adding a main tab.
- The **Read selected GPO files** action reads one explicitly selected GPO from the session-pinned AD domain controller, never all GPOs by default. Files: `Machine/Registry.pol`, `User/Registry.pol` and `Machine/Microsoft/Windows NT/SecEdit/GptTmpl.inf`. No MMC automation, no PowerShell, no registry writing, no changes to AD/SYSVOL.
- Parse Registry.pol's actual mixed UTF-16LE and binary PReg format (version 1) with bounds, data-type formatting, deletion/operation markers, Unicode and precise partial errors. Do not interpret special `**Del` instructions as effective values.
- Parse GptTmpl.inf sections conservatively with encoding checks, section names, raw privilege-right, account-policy and security-value evidence. Do not interpret a listed SID/ACL as effective rights on any target.
- Record source UNC path, file hash (SHA-256), parse coverage, absent/partial/error files and pinned DC identity. Missing optional file never implies Not Configured. An unreadable or malformed file is PARTIAL with diagnostics.
- Add source-file entries to Unified All Settings with a dedicated filter, clearly labeled read-only capability, and a resizable source-details viewer. The original GPMC, ADMX and MMC catalog remains available; source rows are not silently equated with ADMX configuration or effective RSoP.
- Add CoreTests for Registry.pol binary DWORD/Unicode/special instructions, truncated/unsupported files, BOM-driven security-template records, unknown encoding and cross-GPO evidence isolation.
- This is phase 1: Firewall, AppLocker, Software Installation, GPP targeting and other CSE-specific formats still require separate parser modules. Full Windows AuthZ and RSoP computation are NOT claimed. Live test on a nonproduction GPO at YOSH-DC03 remains necessary.

## 0.7.3 - 2026-10-10

- Fix permission-aware controls where a positive SYSVOL GPT.INI writability check was incorrectly treated as evidence of AD GPO edit permission. Separate matching-token AD rights and SYSVOL evidence; normal GPO settings edits require BOTH. Preserve safe mode and authorization at actual write operations.
- Explicit AD deny and ambiguous Custom ACE entries fail closed. Show truthful UI labels: these are simplified GPMC permission indications, not exhaustive Windows AuthZ effective access results. Missing or failed evidence never grants access.
- Exclude technical GPMC SecuritySettings XML Member/Registry ACL records from duplicate/conflict findings and ordinary two-GPO comparison. Do not infer a policy is Not Configured simply because the loaded index lacks it.
- Reject silent selection of the first indexed setting when one GPO contains conflicting values for the same setting identity. Mark these cases **Ambiguous index**, retain source evidence and prohibit merge recommendations.
- Include CSE identity in fallback policy-name matching when a complete registry key/value target is absent. Global conflict drill-down now opens Unified All Settings.
- Add repeatable CoreTests covering AD/SYSVOL separation, explicit denies, missing index entries, nested XML exclusion and ambiguous policy identities. Live GPO permissions and MMC testing remain necessary.

## 0.7.2 - 2026-10-09

- Fix incorrect section-only MMC navigation for GPMC **SecuritySettings** XML records. Group members and registry security-descriptor entries are classified by actual XML ancestry where available, and carefully inferred from recognizable legacy cached rows otherwise. Sections now resolve to **Restricted Groups** and **Security Settings > Registry**, rather than the generic Security Settings root.
- Distinguish descriptive GPMC XML leaf entries from independently editable policy settings. **All Settings** hides verbose `Member:` and `Registry` security XML details by default, with an explicit **Show XML details** checkbox to inspect them. Original raw data remains in Advanced Sources and the read-only detail window.
- Show concise source/value summaries, accurate source and edit capability labels, and context-aware **View XML details...** actions instead of advertising a generic edit button for raw ACLs. Preserve full unmodified GPMC XML value in read-only detail dialogs.
- Block accidental direct ADMX or Boolean security-template writes for technical XML descriptors, regardless of same-named ADMX definitions. Section-only MMC navigation does not imply a verified editable setting.
- Keep low-level source records, GPO Scripts, WMI, Security, and GPO Hierarchy & Links unchanged. Full RSoP/WMI/Security Filtering verification remains required before policy consolidation recommendations.
- Add CoreTests with representative restricted-group members and security Registry XML, legacy cached data, ambiguous members, navigation and edit safety. Runtime smoke-testing on YOSH-DC03 remains necessary.

## 0.7.1 - 2026-10-09

- Fix an MMC Route Audit false MISSING for Computer Configuration > Policies > Administrative Templates when MMC presents the section as **Administrative Templates: Policy definitions (ADMX files) retrieved from the central store**. Exact and recognized ADMX-store captions are matched conservatively; broad fuzzy prefix matches remain disallowed.
- Reuse the strict unique tree label matcher for exact Group Policy editor tree navigation. If multiple matching MMC child labels exist, navigation refuses to guess and the audit records an error.
- Verify indexed MMC section paths before capturing the diagnostic tree, so a long ADMX expansion cannot consume the route verification window first. Increase the tree snapshot cap from 220 nodes/depth 7 to 1,500 nodes/depth 12, bounded by the same deadline.
- Mark scan coverage explicitly as PARTIAL when unsafe snap-ins were skipped, snapshot node/depth/time limits were hit, or UI Automation nodes failed. Separate route summary counts include unchecked paths, and recognized ADMX display-name aliases are shown in the report.
- Do not automatically dismiss modal MMC errors; an unsafe dialog prevents further unattended path checks. The MMC process is left open for operator review.
- Add regression tests for central/local ADMX display labels, duplicate nodes, unrelated names, and the exact YOSH-DC03 report's missing route.
- No GPO settings, SYSVOL content, or AD links are written by MMC audit. Live validation on a test GPO is still necessary.

## 0.7.0 - 2026-10-09

- Replace competing ADMX/GPMC/MMC main settings views with one **All Settings / Unified Catalog**. Browse by text, source, state and GPO; see verified source evidence, state/value, capability, registry target and path in one virtualized WPF grid.
- Join GPMC configured rows to ADMX and a selected GPO's read-only MMC observations conservatively, matching GPO ID, scope, category, policy name and unique registry targets. Never infer Not Configured from the absence of GPMC XML; ADMX templates are labeled **Template - state unknown** until a target is read.
- Connect unified row actions to the existing backed-up ADMX editor, supported security editor and verified native MMC row opening; unsupported types stay read-only or open GPMC manually. Preserve safe-mode/write-confirmation requirements.
- Preserve the original configured GPMC, MMC inventory and ADMX data grids under collapsed **Advanced sources and diagnostics** inside All Settings, instead of deleting diagnostic capabilities.
- Remove the separate top-level ADMX Catalog tab and group sixteen GPP editors beneath a single **Preferences (GPP)** tab. No GPP editor or migration/backup logic has been deleted.
- Migrate pre-0.7.0 saved tab indices and maintain global-search navigation into the unified settings view and nested GPP editors.
- Export the visible unified search result with source and capability evidence. The optional MMC scan keeps PARTIAL diagnostics, and an unverified extension is never treated as editable.
- Add regression coverage for cross-GPO MMC isolation, source matching, ambiguity and unknown policy states. Domain controller integration testing is still necessary for actual MMC/GPMC behavior.

## 0.6.1 - 2026-10-09

- Prevent MMC Full Settings Inventory from entering **Scripts (Startup/Shutdown)** and **Scripts (Logon/Logoff)** snap-ins through UI Automation. A production DC screenshot showed an MMC snap-in error in the former. These nodes are safely excluded **before expansion/selection** and listed as **Skipped - unsafe snap-in** under Coverage, not fabricated as complete data.
- Use the built-in **GPO Scripts** view for script assignments and content; MMC inventory no longer activates fragile script property pages in the background.
- Add a read-only visible MMC modal dialog guard. A snap-in error popup aborts the current automated scan with **PARTIAL** coverage and a diagnostic message. The app never auto-clicks Microsoft error dialogs or chooses permanent ignore; the dedicated MMC session remains available to the operator.
- Improve MMC scan failure diagnostics to show the last section being visited; child enumeration errors can be reported per section without discarding other reachable branches.
- Apply the same Scripts exclusion to **MMC Route Audit** tree snapshots/route checks and deny exact inventory edit navigation into these unsafe snap-ins.
- Add regression tests for safe script-node exclusions, unrelated Administrative Templates categories, partial coverage and modal abort behavior.
- CI success is not proof of a healthy snap-in on the domain controller; test v0.6.1 on a nonproduction reference GPO and investigate Windows snap-in crashes separately.

## 0.6.0 - 2026-10-09

- Added **MMC Full Settings Inventory** as an inner view of the existing **All Settings** tab (no new top-level tab). Select a reference GPO and explicitly launch a read-only native MMC tree and policy list scan; cancel while running.
- Scans the real MMC tree through UI Automation and reads right-pane SysListView32 first-column names and secondary values without opening setting dialogs. Supports search, exact-row navigation with literal-path and unique-name checks, CSV export, and per-section coverage/error diagnostics.
- Reconciles observed rows conservatively with GPMC's configured settings index and cached ADMX policy definitions using GPO ID, scope, name and section path. A missing GPMC setting is **not** treated as Not Configured, and no setting is fabricated from ADMX when absent from MMC.
- Marks scan coverage PARTIAL on inaccessible nodes, non-native/custom views, timeouts, limits or cancellation. Hard bounds: 10 minutes, 4,000 nodes, 18 tree levels, 12,000 rows per section and 100,000 rows overall. A finished scan never claims universal snap-in support or effective RSoP.
- Preserves the existing exact Security Options navigator; scanning creates no GPO write, backup, merge or unlink. A setting dialog opens only after an explicit action and literal name verification.
- Adds regression tests covering GPMC/ADMX correlation, false Not Configured prevention, name collisions, source status and incomplete coverage.

## 0.5.2 - 2026-10-09

- Fix empty `Before` / `After` for newly committed **Edit GPO Script** audit events. Capture original SYSVOL file bytes and return before/after evidence only after script byte verification and GPO Scripts extension commit.
- Store exact SHA-256 hashes, byte lengths, logical line counts, code pages, actual BOM presence and line-ending styles in the two audit columns, plus a changed-line region summary in Details. Protect script content and possible embedded credentials: raw source code is not copied into JSONL audit entries.
- Distinguish text edits from encoding/BOM-only and line-ending-only changes. Do not write a false successful edit event when no bytes changed.
- The audit detail view explains fingerprint-only records and warns that historical entries with blank Before/After cannot be reconstructed from the JSONL log alone; the dated GPO backup may help manual recovery.
- Add CoreTests for verified before/after fingerprints, no-op writes, secret-safe audit output, line endings and encoding-only changes.
- Audit entries are append-only; do not silently deduplicate records with the same script name, since repeat saves may be legitimate.

## 0.5.1 - 2026-10-09

- Main window title now displays the running assembly version.
- Renamed the existing **GPO Links** tab to **GPO Hierarchy & Links** without adding another tab or changing tab index. Two clearly marked, resizable side-by-side panels show the expandable Site/Domain/OU GPO tree on the left and guarded GPO Link management on the right.
- Selecting an OU filters its direct GPO links; selecting a linked GPO highlights the exact editable link in the right-hand table. Search, expand/collapse, refresh, full hierarchy view, export and a GPOs toolbar shortcut make the hierarchy discoverable. Opening the tab loads links automatically.
- Added opt-in, read-only **Verify RSoP / WMI / Security** in conflict details for a specified sample computer (and user for user policies). Compares the GPO AD DACL, assigned WMI filters/rules and gpresult XML; explicitly missing/error results block any consolidation recommendation.
- An apparently matching sample is NOT proof of identical domain-wide application or safe merge. Link location alone is never treated as conclusive; no automatic merge, GPO unlink or delete was added.
- Core regressions check that RSoP evidence cannot pass on missing/excluded entries, nested policy references or absent result flags.

## 0.5.0 - 2026-10-09

- Script editor: editable code-page selection for Cyrillic (Windows-1251, DOS-866), Hebrew (Windows-1255, DOS-862), Unicode (UTF-8/16/32; BOM on/off), KOI8 and installed Windows/OEM pages. Safe source-file reload in a selected encoding and explicit DOS/Windows CRLF, Unix LF, classic Mac CR conversion.
- Strict lossless encoding before SYSVOL write and local export, with detection of non-representable characters; preserve BOM and original mixed line endings unless explicitly converted.
- Combined syntax and Unicode safety diagnostics without script execution: invisible Unicode, bidi overrides, stray U+FEFF, NBSP, dangerous control codes and smart punctuation, with line/column positioning and warnings for PowerShell 5.1 UTF-8 without BOM or unsuitable BAT encodings.
- Diagnostics: explicit read-only MMC Route Audit of a selected GPO, bounded tree traversal, per-section FOUND/MISSING/ERROR logs and local report export through the existing diagnostic queue. Exact per-setting MMC navigation for known Security Options remains unchanged.
- GPO Links: colored, searchable site/domain/OU hierarchy showing block inheritance, Enforced, disabled links and GPMC Link Order. Link Order mapping corrected against reverse gPLink storage; existing WRITE ENABLED editor used for changes, concurrent link updates rejected, and a dated AD gPLink/gPOptions before-state snapshot is saved locally before each link or inheritance mutation.
- Compare & Conflicts: distinguish identical duplicate settings from differing-value candidates; show shared linked containers, potential inheritance overlap and uncertain/no scope evidence. Colored rows, per-GPO value breakdown, explainable consolidation recommendations, copyable remediation plan, navigation to protected settings/links editing.
- Important: Link and conflict predictions are NOT RSoP. WMI/security filters, computer/user location, loopback, inheritance, sites and client-side extensions must be verified separately before cleanup or consolidation. No automatic cross-GPO mutation occurs.
- Added regression tests for Cyrillic/Hebrew round-tripping, BOM, line endings, unsafe Unicode controls, reverse AD link precedence and duplicate/conflicting policy scope.



## 0.4.8 - 2026-10-09

- Added GitHub API connectivity check before automatic/manual release lookups; offline machines skip update checks without creating false error logs or retrying every launch.
- Added an optional, nonintrusive "N logs · Review" counter when at least three unsent diagnostics accumulate **and GitHub is reachable**. Rechecks every 30 minutes, with a 12-hour reminder cooldown; offline machines do not receive an Internet submission prompt.
- New review window for local diagnostic reports, with manual export, redacted preview, optional short excerpts, optional encrypted GitHub token, and a second consent step before opening a public GitHub Issue. **Nothing uploads in the background.**
- Error logs, MMC route audits and GPO hierarchy reports are queued locally. Only after GitHub confirms successful receipt are unchanged source files moved to a 14-day local sent archive and removed from the active queue. Failed/offline uploads never delete pending logs.
- Keep offline unsent error logs instead of deleting them after the previous 30-day/100-file limit.
- Add a diagnostics intake GitHub Actions workflow triggered after builds and daily; it groups public diagnostic Issues from multiple computers into a concise report with fingerprints, versions and counts (no raw issue bodies or confidential logs).
- Add regression tests for queue persistence, offline retention, acknowledgement-only cleanup and default public-report sanitization.



## 0.4.7 - 2026-10-09

- GPO Scripts search: identical script files are deduplicated into one content match; the **Copies** column is an accent-colored, clickable count. Click it to see which GPOs contain the identical script, copy the SYSVOL path, or explicitly edit another physical copy.
- Double-clicking a search result opens one deterministic physical file without forcing the copy chooser; preferred context is the selected script/GPO. A GPO change still affects **only** that single physical file and remains subject to WRITE ENABLED, backup, confirmation and byte verification.
- Added section-only MMC navigation for `PublicKeySettings` (Root Certificate Settings, certificate-related sections), `NrptSettings` (Name Resolution Policy) and `SoftwareInstallationSettings` (Software installation).
- Clarified that `SoftwareInstallationSettings` Trustee Auditing / Trustee Permissions entries are nested package security metadata, not separately editable Security Options. The Setting Value window retains the reported XML summary and labels manual navigation.
- Improved captions to distinguish attempted section navigation from a confirmed exact policy dialog; keep raw `registry.pol` metadata handling as before.
- Added regression tests for multiple identical script copies, per-GPO selection and extended MMC section routing. The user-confirmed LAN Manager native exact-lookup algorithm is unchanged.



## 0.4.6 - 2026-10-09

- Preserve the **field-tested v0.4.5 exact MMC Security Options navigation** unchanged. The user confirmed that `Network security: LAN Manager authentication level` opens correctly in v0.4.5.
- Keep the `Setting Value` window open when an exact MMC policy opens or when only a category can be opened; the window closes only upon an explicit user action.
- For raw `Registry > Extra Registry Settings` / `AdmSetting=false`, explain that `registry.pol` records do not necessarily have an ADMX editor entry, keep the current value visible, and offer Copy registry key / Copy value name.
- Retain existing safe read-only behavior and centralized UI status colors.

## 0.4.5 - 2026-10-08

- Fixed exact MMC Security Options navigation when the requested parameter, such as **Network security: LAN Manager authentication level**, is not currently visible in the scrolled policy list.
- Native list lookup now uses `LVM_FINDITEMW` with an exact full-name comparison, polls for delayed MMC list population and verifies the selected/focused row before opening it.
- Strict native fallback matches only the Policy column, never an NTLM value from another column or an ambiguous partial name.
- Better failure diagnostics distinguish MMC access/permission problems from genuinely missing policy names; failures are logged under `%LOCALAPPDATA%\\GPOSettingsExplorer\\Logs`.
- Regression tests cover the LAN Manager setting among similarly named NTLM options, ellipsis-only matching and ambiguous duplicate labels.

## 0.4.4 - 2026-10-08

- GPO Scripts search now supports **Content only**, **File names & paths**, and **Both**; the result grid identifies the match type and line number.
- Filename-only searches no longer read every script from SYSVOL, and scripts with identical contents but different filenames are identified correctly.
- BAT/CMD/PS1 and other script edits keep the editor open while saving. If backup, SYSVOL write, or the Group Policy API commit fails, the edited text remains in the window for retry.
- Saving requires WRITE ENABLED; the editor makes READ ONLY restrictions visible rather than closing without an actionable explanation.
- Save checks the source script's SHA-256 against the originally opened version, retains original code page/BOM/line endings, verifies the SYSVOL write byte-for-byte, logs the exact failing stage and reports whether a rollback succeeded.
- MMC exact navigation now uses strict matching and bounded native-first lookup. Non-Security-Option settings are no longer opened as unrelated Security Options.
- Audit and raw registry settings route to Advanced Audit Policy and Administrative Templates rather than unrelated nodes; audit subcategory names are extracted for searching.
- Bumped the settings-index cache schema to rebuild old generic Audit Setting results.
- Global colored WPF styling centralizes accent/status colors and prevents data grid column headings from collapsing into vertical letters using DPI-aware measurement of minimum header widths.
- Added regression tests for script search modes, duplicate-content file-name matching, script line endings, and MMC navigation.
- Added a full AvalonEdit script editor with centralized syntax colors for BAT, CMD, PS1, PSM1 and PSD1, plus built-in highlighting for supported JS/VBS/HTML-based scripts.
- Colored GPO rows (disabled, partial scope), script file types and missing/unassigned/inactive scripts. Syntax diagnostics are color-coded by severity, with text labels for accessibility.
- Added non-executing PowerShell AST syntax checking and basic BAT/CMD static validation (missing/duplicate labels, pasted Markdown fences). Checks run on demand and before saving.
- Expanded script editing: Find Next, Replace, Replace All, Ctrl+S/F/H/G shortcuts, F3/F7, goto line, comment/uncomment, indent/outdent, Undo/Redo, configurable whitespace, word wrap, line numbers, zoom, and local-file export.
- Safeguarded in-place editing: unsaved-change confirmation, retry after failed SYSVOL writes without losing editor text, active-window change previews, toolbars that remain accessible at high DPI, and a revised script cache schema that preserves GPO scope statuses.

## 0.4.3 - 2026-10-08

- Fixed native MMC exact navigation opening the wrong Security Option after the correct target row was found.
- Replaced coordinate-based double-click activation with verified native ListView selection/focus followed by Enter, so MMC opens the row that was actually selected.
- Before opening, the fallback clears any previous ListView selection, selects and focuses only the target index, scrolls it into view, and verifies both selected and focused indexes.
- Native row matching now gives priority to an exact/truncated match in the first Policy column before using broader registry/value aliases.
- Diagnostics now report the selected and focused native indexes if row activation fails, making any remaining MMC-specific behavior directly observable.


## 0.4.2 - 2026-10-08

- Added a native Win32 SysListView32 fallback for exact MMC Security Options navigation on Windows Server builds where UI Automation exposes the policy tree but no result rows.
- The fallback reads MMC ListView row and subitem text directly from the MMC process, matches the requested policy, scrolls to the row, selects it, and double-clicks it to open the exact setting.
- Exact-navigation diagnostics now include the native SysListView32 row inventory when both UI Automation and native matching fail, making future MMC-version differences directly diagnosable.
- Targeted the field case where "Network security: LAN Manager authentication level" / LmCompatibilityLevel opened Security Options but produced an empty "Visible MMC rows" diagnostic on Windows Server 2022.


## 0.4.1 - 2026-10-08

- Fixed Security & Delegation failures when GPMC exposes a stale/deleted trustee property as FileNotFoundException or another non-COM exception. Unresolvable trustee properties are now isolated instead of aborting the complete permission list.
- Security & Delegation now follows the GPO currently selected on the main GPO list instead of automatically loading the first alphabetic GPO.
- Corrected raw RegistrySettings parsing. KeyPath, value name, value data, and AdmSetting are now extracted explicitly, and Extra Registry Settings are no longer misidentified as Group Policy Preferences Registry items.
- Advanced the settings-index cache schema so cached v0.4.0 rows are rebuilt automatically with the corrected RegistrySettings metadata.
- Exact Security Options navigation now waits longer, searches all UI Automation descendants/cells, understands truncated MMC row text, and uses registry/value aliases as additional row identities.
- Removed false exact-navigation claims for raw RegistrySettings. AdmSetting=false rows are identified as Extra Registry Settings and no longer open the unrelated Preferences > Windows Settings > Registry node.
- Automatic GitHub update checks no longer write crash-style logs when outbound HTTPS is blocked by firewall/proxy policy. Manual checks show a compact network explanation, including socket error 10013 when applicable.
- Added regression coverage for blocked GitHub socket handling.


## 0.4.0 - 2026-10-08

- Reworked portable self-update into a staged install. Update ZIPs are verified before shutdown, extracted into a staging directory, existing application files are backed up, and failed replacement attempts automatically restore the previous files.
- Retain the three most recent application rollback snapshots and expose "Rollback last update" from Diagnostics.
- Update availability now includes GitHub release notes and publication time, with Install now, Install on exit, Open release, and Later actions.
- Added an actionable crash/error window with Copy error, Open log, Open logs folder, and Create support package actions. Recoverable INI Files failures use the same diagnostic workflow.
- Exact native MMC navigation now reports the target tree path and live navigation progress while it waits for MMC, opens policy nodes, searches virtualized/scrolled result panes, and selects the exact row.
- Global Search now has an "Open / edit exact" action and double-click behavior that selects the source object and invokes its dedicated editor where available.
- Added GPO Favorites and Recent views. Recently opened/edited GPOs are ranked and favorites persist across sessions.
- Added semantic Backup vs Current comparison based on normalized GPMC XML reports. Report timestamps are ignored and identifiable XML elements are compared independent of ordering.
- Audit Log now opens a focused before/after detail window with copy support and one-click restore of the linked GPO backup.
- Added permission-aware controls. The application evaluates current-token GPO rights and selected-GPO SYSVOL write access, displays a compact Read/Edit/Security summary, and disables confirmed-unavailable write actions before they are attempted.
- Added persistent workspace state: window bounds/maximized state, selected tab/GPO, GPO quick filter, favorites/recent GPOs, named text/combo filters, DataGrid column widths, and sort order are restored across runs.
- Added regression coverage for semantic XML comparison while retaining the update-schedule, cache, DPAPI, domain-routing, and script-sanitization tests.


## 0.3.3 - 2026-10-08

- Added a quiet automatic update check after startup. A successful check is repeated at most once every 24 hours.
- Temporary GitHub/network failures no longer interrupt startup and are throttled to a two-hour retry window instead of checking on every launch.
- When a newer release is found automatically, the top-bar button changes to "Update vX.Y.Z" without showing a modal prompt.
- The last successful release check is persisted under LocalAppData so an available update remains visible across restarts without another network request.
- Manual "Check updates" still performs an immediate fresh check and then offers to download, verify and install the matching x64/ARM64 portable package.
- Added core regression tests for the 24-hour successful-check interval and two-hour failed-check retry interval.


## 0.3.2 - 2026-10-08

- Fixed the GPP INI Files crash dialog: the Targeting column was binding TwoWay to the read-only computed HasFilters property.
- Converted all MainWindow DataGrid checkbox columns to explicit read-only OneWay bindings so display-only values such as HasFilters, HasStoredCredential, Referenced, Exists, Inherited and similar computed flags cannot trigger WPF source-update exceptions.
- Overview checkbox cells are now non-editable; changes continue to go through the dedicated Edit/action dialogs where backups, previews and audit logging are enforced.
- Added a UI lint guard that fails CI if a MainWindow DataGridCheckBoxColumn is added without Mode=OneWay and IsReadOnly=True.


## 0.3.1 - 2026-10-08

- Fixed exact MMC navigation for Security Options such as "Network security: LAN Manager authentication level" by handling virtualized MMC rows and scrolling through the full result pane instead of searching only currently materialized rows.
- Added exact navigation for GPP Registry items. Registry rows now open Computer/User Configuration > Preferences > Windows Settings > Registry and search for the selected item instead of only opening the GPO.
- GPP Registry report parsing now reads nested key/value attributes from Properties nodes, so Registry metadata is populated instead of showing "<not reported>" when GPMC emits attributes rather than child elements.
- Settings-index cache schema was advanced so existing v0.3.0 cached rows are rebuilt automatically with the corrected Registry metadata.
- Exact navigation now uses multiple row aliases (display name, registry value name, and GPP name/status) and a longer bounded navigation window for slower MMC consoles.

## 0.3.0 - 2026-10-07

- Added a startup Active Directory connection manager with current-session or alternate credentials, optional DC pinning, connection testing, and DPAPI-protected credential persistence.
- Added session-wide single-DC routing so LDAP, GPMC, SYSVOL, GPO scripts, WMI filters, links, Administrative Templates, Security Options, and Group Policy Preferences use the selected domain controller consistently.
- Added Safe mode. The application starts read-only and all supported Group Policy write paths are blocked until WRITE ENABLED is explicitly confirmed.
- Added before/after change previews for GPO creation/copy/import/rename/delete, scope changes, permissions, WMI assignments, GPO links, GPP XML, scripts, Administrative Templates, Security Options, and backup restore/delete operations.
- Added a visual Group Policy Preferences item-level targeting tree editor that preserves existing and unknown filter elements and attributes while keeping raw XML available for advanced editing.
- Added structured tree/attribute editing for every known GPP XML document, including Applications, Devices, Internet Settings, Network Options, and Start Menu and Taskbar, while retaining the specialized editors for the common preference types.
- Added a Diagnostics window with domain/DC/GPMC/SYSVOL/Central Store checks, selected-GPO write-access testing, crash-log access, and sanitized support-package ZIP creation.
- Added global cross-feature search over loaded GPOs, settings, WMI filters, scripts, GPP items, backups, audit entries, and other loaded model collections with navigation back to the source tab.
- Added Audit Log actions to open or restore the GPO backup linked to an audit entry, reusing the safety-backup restore workflow.
- Added persistent GPP XML and GPO script inventory caches. Cache entries are invalidated when source metadata changes or writes occur, reducing repeated SYSVOL reads.
- WMI filter create/edit/delete now writes the msWMI-Som objects directly through LDAP on the pinned DC instead of depending on the local root\\policy provider.
- Added architecture-aware in-app update checks and staged portable self-update for x64 and ARM64 GitHub releases.
- Added a CoreTests regression runner and CI test stage covering script sanitization, single-DC path routing, DPAPI, GPP XML cache freshness, and script cache invalidation.
- Removed the redundant System.DirectoryServices package reference on .NET 10.


## 0.2.11 - 2026-10-07

- Hardened GPP INI Files loading so overlapping scans, cancellation, tab initialization failures, or unexpected read errors no longer terminate the application.
- INI Files errors are now written to %LOCALAPPDATA%\GPOSettingsExplorer\Logs and the UI remains open with a recoverable error message.
- Added application-level logging for unhandled UI/task exceptions so field failures have a diagnostic file instead of disappearing with the process.
- Exact Security Options navigation now treats unexpected MMC/UI Automation failures as recoverable and leaves the standard GPO editor open as a fallback.
- The Setting Value window shows an explicit Opening... state and logs editor-launch failures for troubleshooting.

## 0.2.10 - 2026-10-07

- GPO Scripts now supports multi-select search scoping by both policies and files. Use Select GPOs... to choose one or more GPOs and Ctrl/Shift in the script grid to choose one or more files; when both are selected, search uses their intersection.
- The script search bar shows the active scope and match counts so it is clear whether the query is running across all GPOs, selected policies, selected files, or both.
- After saving a script, the active search is automatically rerun against the newly saved content. A fixed wmic/vbs/etc. match disappears from the result list immediately without pressing Find again.
- Accidental outer Markdown code fences such as ```bat ... ``` are stripped from the editor view and again before saving, so chat/Markdown wrappers cannot be written back into BAT/CMD/PowerShell/VBS script files.
- Exact Security Options navigation in the native Group Policy editor was strengthened for lazy-loaded MMC trees and result panes. It now supports both ListItem and DataItem rows, scrolls the target into view, and falls back to guarded UI clicks/double-clicks without unsafe SetFocus calls.
- The GPO editor is brought to the foreground and the selected Security Option is opened when MMC exposes the corresponding row.

## 0.2.9 - 2026-10-07

- GPO Scripts content search now deduplicates identical physical script contents by SHA-256. The same copied script no longer produces repeated matches for every GPO that contains it.
- Deduplicated search rows show the file, matching line, number of physical copies, and number of GPO references instead of listing every policy name/path.
- When an identical script exists in multiple GPOs, double-clicking a search result opens a compact copy picker only at edit time so the administrator can choose the physical copy to modify.
- Fixed MMC UI Automation failure "Target element cannot receive focus" by removing unsafe SetFocus calls and using guarded selection/expand/invoke operations.
- Hardened MMC exact-navigation against transient COM/UI Automation HRESULT failures. A failed automation step no longer produces the red "Unexpected HRESULT ... COM component" error dialog.
- Exact native-editor navigation is attempted only for Security Options where the snap-in path is deterministic. Generic report rows such as RegistrySettings and PublicKeySettings now open the selected GPO normally without claiming exact navigation.
- All Group Policy editor launches now use the full %SystemRoot%\System32\mmc.exe path with System32 as the working directory.
- The All Settings parser now suppresses raw RegistrySettings rows that duplicate already reported Security Options by registry key/value.
- Settings-index cache schema was advanced so the parser cleanup is applied automatically on first run of this version.

## 0.2.8 - 2026-10-07

- GPO Scripts now initializes and scans automatically on the first actual selection of the tab, avoiding the WPF TabItem Loaded timing case where the tab could initialize before the domain GPO list was available.

## 0.2.7 - 2026-10-07

- Added persistent ADMX Catalog caching under %LOCALAPPDATA%\GPOSettingsExplorer\Cache. Cached Administrative Templates load immediately and the Central Store is validated in the background using an ADMX/ADML file fingerprint; full XML parsing runs only when PolicyDefinitions actually changes.
- Fixed native GPO editor launch failures caused by inheriting an inaccessible current working directory such as C:\Temp. MMC is now started from the full %SystemRoot%\System32\mmc.exe path with System32 as its working directory.
- Hardened direct Administrative Template editing by running Group Policy COM operations on dedicated STA threads and explicitly initializing COM apartment state, addressing "Interface not registered" failures on affected Windows/GPMC builds.
- Added a GPO Scripts tab that discovers Computer/User Startup, Shutdown, Logon and Logoff scripts from scripts.ini / psscripts.ini and also finds unassigned script files stored under the GPO Scripts folders.
- GPO Scripts supports BAT, CMD, PowerShell (PS1/PSM1/PSD1), VBS, JS, WSF and HTA text files. Double-click/Edit opens the file in the built-in editor; scripts are never executed by the application.
- Saving a GPO script creates an automatic GPO backup, writes the SYSVOL file atomically, updates the Scripts Group Policy extension revision, writes an audit entry, and rolls the file back if the revision update fails.
- Added full-text GPO script search across All files or a Selected file. Results include GPO, scope/event, file, line number and matching line; double-click opens the built-in editor at the matching line.
- Script search also checks file names, parameters and paths, so searches such as "wmic" or "vbs" can find both content and script references.

## 0.2.6 - 2026-10-07

- Background All Settings refresh failures now keep the previously cached index visible instead of interrupting normal use.
- Deleted GPOs are removed from the settings cache without running unnecessary report generation.

## 0.2.5 - 2026-10-07

- All Settings now automatically loads a persistent cached settings index when the tab is opened.
- The cache is stored under %LOCALAPPDATA%\GPOSettingsExplorer\Cache so it survives replacing or extracting a newer portable build.
- Cached settings are displayed immediately while the application compares current GPO GUID + ModificationTime metadata in the background.
- Only new or modified GPOs are re-indexed; deleted GPOs are removed from the cache automatically.
- If no cache exists, opening All Settings automatically starts the first full index build.
- Manual Rebuild index remains available as a forced full refresh.
- Successful direct policy edits update the persistent settings cache so All Settings stays current during the same session.

## 0.2.4 - 2026-10-07

- Reworked the non-ADMX Setting Value dialog into a compact Windows-style editor.
- Boolean Security Options reported as True/False can now be changed directly from All Settings with automatic GPO backup, audit logging, Security Template update, and Group Policy revision save.
- "Open exact setting in GPO editor..." now launches the selected GPO through MMC automation, navigates to the matching policy category, selects the exact result row, and opens its property sheet when the local MMC snap-in exposes the required automation objects.
- If exact MMC navigation is unavailable on a Windows build, the GPO editor remains available and the application reports that automatic row selection was not possible.
- Administrative Template direct editing now uses native IGroupPolicyObject activation instead of the fragile runtime COM cast.

## 0.2.3 - 2026-10-07

- Fixed direct Administrative Template editing failure "Specified cast is not valid" by using the registered Group Policy COM coclass directly.
- All Settings now maps Administrative Templates by registry key/value in addition to display name and scope.
- Policy Editor automatically scrolls to and focuses the selected setting value (or the policy state when no separate value field exists).
- Non-ADMX rows open a focused Setting Value window instead of the "Direct editor unavailable" prompt.
- Security Settings report rows now expose the actual Security Option name, displayed value, registry key, and registry value name.

## 0.2.2 - 2026-10-07

- Fixed unsolicited "Load GPO Security" errors during startup: Security & Delegation permissions are now loaded only when that tab is selected.
- Hardened GPMC permission enumeration so an unresolvable/deleted trustee no longer aborts the complete permission list.
- Added an Active Directory ACL fallback for GPO security when GPMC GetSecurityInfo fails, including SID-to-account resolution and simplified Apply/Read/Edit/Full Control mapping.

## 0.2.1 - 2026-10-07

- Fixed startup failure "Provider is not capable of the attempted operation" when the local root\\policy WMI provider cannot enumerate Group Policy WMI filters.
- WMI filter discovery now reads the domain's msWMI-Som objects directly through LDAP and parses the native msWMI-Parm2 rule format.
- GPO discovery and WMI filter discovery are isolated: a WMI filter error no longer prevents the GPO list from loading.

## 0.2.0 - 2026-10-07

- Completed the application-wide UiStyle/HiDPI cleanup pass: centralized typography, adaptive dialog button bars, resizable compact dialogs, flexible multiline fields, WMI editor layout cleanup, per-monitor DPI reflow, and overflow-safe compact dialogs for 100/125/150/175/200% Windows scaling.

- Added structured GPP Folder Options editing for Vista-and-later user shell/view settings, with legacy/file-association visibility, item-level targeting, backups, audit, clone/delete, and raw XML handoff.
- Added structured GPP Regional Options editing for user locale, number, currency, time and date formats, with item-level targeting, backups, audit, clone/delete, and raw XML handoff.
- Added structured GPP Power Options editing for Vista+ power plans with AC/DC settings, safe legacy read-only handling, item-level targeting, backup, audit, clone/delete, and raw XML handoff.
- Added structured GPP Data Sources editing for ODBC System/User DSNs, driver-specific attributes, item-level targeting, safe opaque legacy credential preservation/removal, backup, audit, clone, and raw XML handoff.
- Unified the application around a compact native Windows-style resource theme and added Per-Monitor V2 HiDPI behavior, adaptive wrapping command bars, monitor-aware dialog sizing, wrapped labels/grid headers, and no-ellipsis text handling for 125%-200% scaling and 4K displays.
- Added structured INI Files editing for Computer and User Configuration, including create/update/replace/delete semantics, property/section/file deletion modes, item-level targeting, automatic backups, audit logging, clone, and raw XML handoff.
- Added structured Network Shares editing for Computer Configuration, including create/edit/clone/delete, share path/comment, bulk share operations, user limits, access-based enumeration, item-level targeting, automatic backups, audit logging, and raw XML handoff.
- Added structured Scheduled Tasks editing for modern TaskV2 and ImmediateTaskV2 items, including run-as/logon settings, first Exec action, Task Scheduler XML preservation, item-level targeting, safe opaque legacy cpassword preservation/removal, backup, audit, clone, and raw XML handoff.
- Added structured GUI editors for Group Policy Preferences Registry, Drive Maps, Services, Shortcuts, Files, and Folders.
- Added generic GPP XML browsing, validation, import/replace, export, repair, and raw editing.
- Added GPO copy, backup import, restore, comparison, conflict detection, security/delegation management, WMI filter management, audit logging, and safe automatic backups before write operations.
- Added structured Files and Folders editing for Computer and User Configuration, including create/edit/clone/delete, attributes, delete behavior, item-level targeting, raw XML handoff, and audit integration.
- Added structured Environment Variables editing for Computer and User Configuration, including system/user variables, partial PATH mode, item-level targeting, backup, audit, clone, and raw XML handoff.
- Environment Variables XML uses the published MS-GPPREF EnvironmentVariables and EnvironmentVariable class identifiers and native user/partial properties.
- Added structured Local Users and Groups editing for Computer and User Configuration, including local user account flags, group membership management, group SID targeting, current-user membership actions, item-level targeting, backup, audit, clone, and raw XML handoff.
- Legacy Local User cpassword data is never decrypted, displayed, created, or copied to cloned items; existing data can only be preserved opaquely during an edit or removed.
- Added structured Printers editing for shared, TCP/IP, and local printer preferences in Computer and User Configuration, including printer-specific options, item-level targeting, backup, audit, clone, and raw XML handoff.
- Legacy shared-printer cpassword data is never decrypted, displayed, created, or copied to cloned items; existing data can only be preserved opaquely during an edit or removed.

## 0.1.0 - Initial development

- Portable WPF application foundation.
- Domain and GPO discovery without PowerShell.
- GPO list and settings indexing through the native GPMC COM API.
- WMI filter browsing and planned full CRUD workflow.
- Direct GPO editor launch.
- Portable self-contained x64 and ARM64 build pipeline.

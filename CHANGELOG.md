# Changelog

## Unreleased

- No unreleased changes yet.

## 0.4.6 - 2026-10-09

- Fixed native MMC `LVM_GETITEMTEXTW` decoding: use exactly the UTF-16 character count returned by Windows instead of decoding the full reused buffer, which previously appended stale policy name fragments to the current row.
- Verify the complete first-column policy name after `LVM_FINDITEMW` returns an index, before opening any Security Options policy; prevent accidental activation of a neighbouring NTLM setting.
- Keep the Setting Value window open after launching MMC (whether exact or section-only), instead of automatically discarding the value and registry details.
- For `Registry > Extra Registry Settings` / `AdmSetting=false`, explain that raw `registry.pol` data has no guaranteed ADMX editing entry; offer Copy registry key / Copy value name while retaining the value preview.
- Add regression tests reproducing stale ListView buffer tails at Security Options row 69. Native MMC integration must still be tested on the affected Windows Server.

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

# Changelog

## Unreleased

- No unreleased changes yet.

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

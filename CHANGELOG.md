# Changelog

## Unreleased

- No unreleased changes yet.

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

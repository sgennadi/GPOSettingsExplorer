# Changelog

## Unreleased

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

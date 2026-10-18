# GPO Settings Explorer

[![Build](https://github.com/sgennadi/GPOSettingsExplorer/actions/workflows/build.yml/badge.svg)](https://github.com/sgennadi/GPOSettingsExplorer/actions/workflows/build.yml)
[![Latest Release](https://img.shields.io/github/v/release/sgennadi/GPOSettingsExplorer)](https://github.com/sgennadi/GPOSettingsExplorer/releases/latest)
[![Windows](https://img.shields.io/badge/platform-Windows-0078D4)](https://github.com/sgennadi/GPOSettingsExplorer)

A portable Windows GUI for browsing, searching, editing, backing up, comparing, and managing Active Directory Group Policy Objects without relying on PowerShell for the main application workflow.

GPO Settings Explorer is intended for administrators who want one compact interface for classic GPO settings, Administrative Templates, WMI filters, delegation, links, backups, and Group Policy Preferences.

## Download

Download the latest portable build from:

**[GitHub Releases](https://github.com/sgennadi/GPOSettingsExplorer/releases/latest)**

Current stable release: **v2.0.3**.

## Preview 18: One cached policy map for every GPO

**[Download Preview 18](https://github.com/sgennadi/GPOSettingsExplorer/releases/tag/v2.1.0-preview.18)** (portable Windows x64 and ARM64).

The default **Policy map** lists individual ADMX input fields without repeated GPMC summaries. Choose a GPO, click **Browse fields**, search by field label, policy ID or registry key/value, and double-click to open that field with values read from the selected GPO. The domain-wide ADMX schema index and field rows are reused when you select another GPO; changed policy definitions trigger a new schema snapshot.

For a complete audit, switch **Source** to **All sources**. Security Settings, Group Policy Preferences and MMC-only settings remain visible when supported. A duplicate target anywhere in the ADMX catalog blocks the generic editor instead of guessing.

## Preview 17: Complete ADMX field map

[Preview 17 prerelease](https://github.com/sgennadi/GPOSettingsExplorer/releases/tag/v2.1.0-preview.17) (published after successful Windows CI). The stable release remains **v2.0.3**.

**Each field is a searchable row.** The shared ADMX schema now includes each text, numeric, list, enum and boolean input separately, even if its parent policy has configured entries elsewhere. Search by field ID, value name or full registry path. Double-click to open the selected GPO's editor and scroll to that exact field. Unconfigured templates do not invent stored GPO values.

**Safety:** Duplicate ADMX element IDs or registry targets disable the generic writer. Unavailable or stale schema entries cannot silently redirect to another GPO or policy.

**Quick start:** Connect to the domain, select a GPO, and click **Browse fields**. The **All Settings** view uses one cached ADMX schema from the domain's PolicyDefinitions store, shared across all GPOs. Search by policy name, category, registry key, or field name. Double-click a verified ADMX setting to open its actual controls with current values read from the selected GPO.

**How matching works:** A uniquely identified Computer/User scope + registry key + value maps a GPMC setting to its ADMX definition and exact input. Duplicated identities, a missing/stale GPO, and cross-extension collisions fail closed. ADMX template definitions are *not* evidence that a specific GPO configured the setting. Security Settings, Group Policy Preferences, vendor-specific CSEs, and native MMC forms without supported adapters retain their appropriate editors or guarded read-only source views.

**Interface:** Main GPO and All Settings toolbars prioritize browsing/editing. Optional diagnostics, WMI management, source scans, RSoP evidence, exact native MMC paths and recovery tools remain available in their advanced sections.

**Updater:** Portable preview-channel update checks use GitHub release digests for SHA-256 verification and keep rollback copies. The updater requires a rendered WPF window to acknowledge startup; installation and rollback need testing on actual Windows systems. Builds may be unsigned. Do not test writes against a production DC first.

For full prior-release history and migration notes see [CHANGELOG.md](CHANGELOG.md); for detailed source limitations see [Discovery evidence](docs/discovery-evidence-preview.md) and [Exact GPMC Registry source lookup](docs/gpmc-exact-registry-source.md).


## Main features

### GPO management

- Discover the current Active Directory domain and connected domain controller.
- Browse all Group Policy Objects.
- Create, copy, rename, and delete GPOs.
- Enable or disable Computer and User Configuration scopes.
- Open the native Group Policy editor for a selected GPO.
- Build a searchable index of configured settings across GPOs.
- Export settings data.
- Copy and import GPO settings.
- Backup and restore GPOs.
- Compare GPOs and identify differences/conflicts.
- Manage GPO links.
- Manage delegation and permissions.
- Automatic safety backups before supported write operations.
- Audit logging for administrative changes.
- Startup connection manager for domain/DC selection, current-session or alternate AD credentials, and connection testing.
- Optional DPAPI-protected credential persistence for the current user or local machine.
- Single-DC session pinning so LDAP, GPMC and SYSVOL operations stay on the same domain controller.
- Safe mode starts the application read-only; administrators explicitly enable write operations for the session.
- Before/after previews are shown for supported Group Policy changes before they are committed.
- Global search can locate loaded GPOs, settings, scripts, WMI filters, GPP items, backups and audit entries from one box.
- Diagnostics can test the connection, GPMC availability, SYSVOL/Central Store access and selected-GPO write access, and can create a support ZIP.
- The Audit Log can jump to or restore an associated GPO backup.
- Built-in update checking selects the correct x64 or ARM64 portable release, shows release notes, supports Install now / Install on exit, verifies the package, stages replacement safely, and retains rollback snapshots.
- Diagnostics can roll back the most recent application update.
- GPO Favorites and Recent views provide quick access to commonly used policies.
- Permission-aware controls show confirmed Read/Edit/Security rights for the selected GPO and disable unavailable write actions.
- Backup vs Current semantic comparison removes XML/report noise and highlights meaningful setting changes.
- Workspace state persists the selected tab/GPO, filters, window placement, column widths, and sort order across runs.

### Administrative Templates / ADMX

- Load policies from the domain Central Store when available.
- Fall back to the local Windows `PolicyDefinitions` store.
- Persist the parsed ADMX catalog under `%LOCALAPPDATA%\GPOSettingsExplorer\Cache` so 10,000+ policy catalogs appear immediately on later runs.
- Validate the Central Store in the background using an ADMX/ADML file fingerprint and reparse only when it actually changes.
- Search the ADMX catalog.
- View policy scope, category, registry key, value, and explanation.
- Configure supported Administrative Template settings directly from the application.

### WMI filters

- Browse WMI filters through LDAP on the connected domain controller.
- Create, edit, clone, and delete WMI filters directly as Active Directory msWMI-Som objects on the pinned DC.
- Edit WQL rules.
- Assign or remove WMI filters from GPOs.
- Test filter rules against computers.

### GPO scripts

- Browse script files stored inside GPO SYSVOL folders for Computer and User Configuration.
- Resolve Startup, Shutdown, Logon, and Logoff assignments from `scripts.ini` and `psscripts.ini`.
- Show GPO, scope, event, order, parameters, full UNC path, size, and modification time.
- Edit BAT, CMD, PS1, PSM1, PSD1, VBS, JS, WSF, and HTA text files in the built-in editor.
- Editing never executes the script.
- Every save creates a GPO backup, updates the Scripts extension revision, writes an audit entry, and rolls back the file if the GPO revision update fails.
- Search script contents for `wmic`, `.vbs`, `cscript`, `powershell.exe`, `net use`, or any other text.
- Search can be limited to one or more selected GPOs, one or more selected files, or the intersection of both selections. File rows use standard Ctrl/Shift multi-selection.
- Identical script copies are grouped by SHA-256 content, so the same `bgscript.bat` copied into many GPOs appears only once per matching line. The results show copy/reference counts instead of repeating GPO names.
- If an identical script has multiple physical GPO copies, double-clicking a result asks which copy to edit only at that point.
- After Save, the current search is rerun automatically against the saved content, so resolved matches disappear immediately.
- Outer Markdown wrappers such as ```bat / ``` are removed before editing/saving and are never written back to SYSVOL.

### Group Policy Preferences

Structured editors are available for:

- Registry
- Drive Maps
- Services
- Shortcuts
- Files
- Folders
- Environment Variables
- Local Users and Groups
- Printers
- Scheduled Tasks
- Network Shares
- INI Files
- Data Sources / ODBC
- Power Options
- Regional Options
- Folder Options

The application also includes structured tree/attribute editing for every known GPP XML document, including Applications, Devices, Internet Settings, Network Options, and Start Menu and Taskbar. Raw XML remains available for advanced repair and unsupported schema details.

GPP editors include a shared visual item-level targeting tree editor plus raw targeting XML, clone/delete operations, validation, backups, change previews, audit integration, and persistent XML caching to reduce repeated SYSVOL reads.

## Security notes

GPO Settings Explorer does not decrypt or expose legacy GPP `cpassword` values.

Existing legacy credential data is treated as opaque data where applicable. Supported editors may preserve it during an edit or remove it, but do not display or generate the plaintext password.

Always use an account with only the Active Directory and Group Policy permissions required for the intended operation.

## Requirements

- Windows 10/11 or a supported Windows Server version.
- Access to an Active Directory domain for domain GPO management.
- Group Policy Management components must be installed and the `GPMgmt.GPM` COM object must be available.
  - On administrative workstations, install the **Group Policy Management Tools** RSAT component.
  - On domain controllers, the required management components are normally available with Group Policy Management.
- Appropriate AD/GPO permissions for read or write operations.
- Network access to the domain controller and SYSVOL.
- For Central Store ADMX browsing, access to:
  `\\<domain>\SYSVOL\<domain>\Policies\PolicyDefinitions`

The application is published as a self-contained .NET 10 Windows application.

## Portable use

1. Download the correct ZIP for your architecture from [Releases](https://github.com/sgennadi/GPOSettingsExplorer/releases/latest).
2. Extract it to a local folder.
3. Run `GPOSettingsExplorer.exe`.
4. On the connection screen, use the current Windows session or specify a domain, domain controller, and alternate AD credentials.
5. Optionally remember alternate credentials with Windows DPAPI; plaintext passwords are never stored.
6. After connecting, the application starts in Safe mode (read-only). Enable WRITE ENABLED only when changes are required.
7. If GPMC is not installed, GPMC-dependent operations will not be available.

No installer is required. The same portable build can be used on a domain-joined workstation, a domain controller, or a standalone/workgroup Windows computer that has network access to the target AD domain and the required RSAT/GPMC components.

## UI and HiDPI

The application follows a single centralized UI styling layer:

- `Themes/WindowsCompact.xaml`
- `UiStyle.cs`
- `AdaptiveWindowManager.cs`

Every existing and new window must remain usable at:

- 100%
- 125%
- 150%
- 175%
- 200% Windows display scaling

The UI rules require:

- no clipped text;
- no clipped buttons or fields;
- no inaccessible controls;
- no ellipsis used as a substitute for proper layout;
- adaptive/wrapping command bars;
- resizable dialogs;
- scrolling where content can exceed the viewport;
- Per-Monitor V2 DPI behavior.

These rules are documented in [UI_GUIDELINES.md](UI_GUIDELINES.md) and enforced by the `UiStyleLint` CI check.

## Build from source

Requirements for development:

- Windows
- .NET 10 SDK
- Git

Clone the repository and build:

```cmd
git clone https://github.com/sgennadi/GPOSettingsExplorer.git
cd GPOSettingsExplorer
dotnet restore GPOSettingsExplorer.sln
dotnet build GPOSettingsExplorer.sln -c Release
```

Run the UI style validation manually:

```cmd
dotnet run --project tools\UiStyleLint\UiStyleLint.csproj -- src\GPOSettingsExplorer
```

Publish x64:

```cmd
dotnet publish src\GPOSettingsExplorer\GPOSettingsExplorer.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

Publish ARM64:

```cmd
dotnet publish src\GPOSettingsExplorer\GPOSettingsExplorer.csproj -c Release -r win-arm64 --self-contained true -p:PublishSingleFile=true
```

## Continuous integration and releases

GitHub Actions performs the following on `main`:

1. Restore.
2. Run `UiStyleLint`.
3. Build Release configuration.
4. Run the CoreTests regression suite.
5. Publish self-contained win-x64.
6. Publish self-contained win-arm64.
7. Create portable ZIP archives.
8. Upload build artifacts.
9. Create a GitHub Release for a new project version.

The release version is read from:

`src/GPOSettingsExplorer/GPOSettingsExplorer.csproj`

Before publishing a new release, increment `<Version>` and update [CHANGELOG.md](CHANGELOG.md).

## Project structure

```text
GPOSettingsExplorer/
├─ .github/workflows/       GitHub Actions build/release pipeline
├─ src/GPOSettingsExplorer/
│  ├─ Models/               Application data models
│  ├─ Services/             AD, GPMC, GPP, WMI and backup services
│  ├─ Themes/               Shared Windows UI resources
│  ├─ MainWindow.*          Main application UI and feature modules
│  ├─ Gpp*Window.cs         Structured GPP editors
│  ├─ PolicyEditorWindow.cs Administrative Template editor
│  ├─ UiStyle.cs            Centralized programmatic UI style
│  └─ app.manifest          Windows/Per-Monitor V2 configuration
├─ tools/UiStyleLint/       CI guard for mandatory UI/HiDPI rules
├─ tools/CoreTests/         Regression tests for core platform behavior
├─ CHANGELOG.md
├─ UI_GUIDELINES.md
└─ GPOSettingsExplorer.sln
```

## Current status

**v1.2.0** combines the GPO administration platform with direct pinned-DC source analysis, optional offline GPMC backups, DPAPI snapshots, security triage, Intune readiness and opt-in Graph/local AI. Coverage is explicitly partial for unsupported CSEs; stored source files are not proof of effective client policy.

See [CHANGELOG.md](CHANGELOG.md) for release details.

## Disclaimer

Group Policy changes can affect many computers and users. Test changes in a controlled OU or lab before applying them broadly in production.

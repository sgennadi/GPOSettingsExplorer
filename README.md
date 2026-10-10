# GPO Settings Explorer

[![Build](https://github.com/sgennadi/GPOSettingsExplorer/actions/workflows/build.yml/badge.svg)](https://github.com/sgennadi/GPOSettingsExplorer/actions/workflows/build.yml)
[![Latest Release](https://img.shields.io/github/v/release/sgennadi/GPOSettingsExplorer)](https://github.com/sgennadi/GPOSettingsExplorer/releases/latest)
[![Windows](https://img.shields.io/badge/platform-Windows-0078D4)](https://github.com/sgennadi/GPOSettingsExplorer)

A portable Windows GUI for browsing, searching, editing, backing up, comparing, and managing Active Directory Group Policy Objects without relying on PowerShell for the main application workflow.

GPO Settings Explorer is intended for administrators who want one compact interface for classic GPO settings, Administrative Templates, WMI filters, delegation, links, backups, and Group Policy Preferences.

## Download

Download the latest portable build from:

**[GitHub Releases](https://github.com/sgennadi/GPOSettingsExplorer/releases/latest)**

Current release: **v1.3.0**

Available packages:

- `GPOSettingsExplorer-win-x64-portable.zip` — Intel/AMD 64-bit Windows.
- `GPOSettingsExplorer-win-arm64-portable.zip` — Windows on ARM64.

The builds are self-contained. A separate .NET runtime installation is not required.

### Release 1.3.0 - Explain Why GPO / per-client diagnostics

- **All Settings > Advanced analysis... > Client & DC health > Explain why (full evidence)...** combines independent read-only observations for one named computer and explicit User/Computer scope.
- Logged gpresult identifies whether a GPO was recorded as applied, excluded or unknown on one client. Historical RSoP is never treated as a future-policy prediction.
- A pinned-DC LDAP probe loads the computer AD object and walks its domain/OU ancestors, interpreting direct and inherited GPO links, link enablement, order, block inheritance and enforced links.
- Loaded selected GPO User/Computer section state (refresh GPO inventory to reduce staleness) and bounded recent GroupPolicy Operational event metadata appear alongside historical RSoP; Event Log ActivityID and record number are preserved without event bodies.
- Every missing RSoP, invalid gPLink, inaccessible client, unknown WMI matching, token/ACL status, site link, loopback setting or CSE result is explicitly marked as unknown. Event IDs are not attributed to this GPO without a matching processing ActivityID.
- Reports include practical next diagnostic steps without automatically changing any policy, link, ACL, WMI filter or Windows client.
- Regression tests cover link/path precedence, disabled/enforced behavior, invalid link syntax, missing RSoP, user loopback and misleading event attributions.
- Live AD/client testing is required on a nonproduction GPO and representative Windows computer.

See [Explain Why operator guide](docs/explain-why.md).

### Release 1.2.0 - Advanced Analysis workspace (read-only)

- New Advanced analysis action in All Settings opens a scrollable, resizable WPF workspace for offline backups, timeline, security triage, DC/client diagnostics, baseline checks, Intune and GitOps.
- Dedicated **Offline GPMC backup...** button on the initial connection screen and `--offline` startup switch launch Advanced Analysis without a domain, GPMC/RSAT or main GPO grid.
- Offline GPMC backup reader with bounded source access to Registry.pol, SecEdit, Advanced Audit audit.csv and curated GPP XML, plus same-GPO fingerprint comparisons.
- Current-user DPAPI encrypted, 40-snapshot-per-GPO local history. Only source-identity metadata and SHA-256 fingerprints are persisted, not raw setting values.
- Security scanner detects legacy GPP cpassword presence and script review patterns without disclosing secrets or executing commands.
- Multi-DC SHA-256 fingerprints of a curated policy-file set (including Advanced Audit audit.csv) and client GroupPolicy Operational event metadata through the built-in wevtutil tool.
- Operator-supplied JSON baseline comparison and exact reviewed registry-to-Policy-CSP mapping. Not a built-in official Microsoft baseline; unknown states remain unknown.
- Opt-in MSAL device-code read-only Intune configuration policy inventory (requires Entra App Registration and delegated permissions); Microsoft Graph /beta is subject to change.
- Privacy-safe, **current-user-DPAPI-keyed HMAC-SHA256** GitOps review fingerprints, with no automatic publishing and no policy-application engine. Different users/machines have noncomparable keys.
- Optional locally installed Ollama model at 127.0.0.1 only, with explicit approval and whitelisted finding counts (no raw policy names, paths or values).
- Read-only Registry.pol family names for Firewall, AppLocker, Defender, Windows Update, Edge, Chrome and Remote Desktop settings. Labels never imply CSE execution or target effectiveness.
- Direct Click-to-Edit for supported stored Security audit/access numeric entries (after WRITE ENABLED, mandatory preflight, GPMC backup and approval); unknown settings remain view-only.
- Optional code-signing on release builds requires manually configured Authenticode PFX GitHub secrets; otherwise executables remain unsigned. A separate GitHub build provenance attestation job covers released ZIPs and a CodeQL workflow performs PR/weekly security analysis.
- This is a staged foundation for further CSE/ADMX editors and impact modeling. Read-only output is NOT effective client RSoP, and live DC testing remains required.

See the Advanced Analysis operator guide in docs/advanced-analysis.md.

### Release 1.0.1 - reliability fixes

- GPMC RestoreGPO success now requires invoking the actual COM `OverallStatus()` HRESULT method; missing/broken status fails closed and COM failures are propagated, not replaced by zero.
- AD `gPCFileSysPath` is validated as a domain-specific canonical SYSVOL/Policies/GPO GUID path for both single-DC and multi-DC health reports. Actual reads remain pinned to selected controllers.
- Multi-DC GPT.INI reads enforce the 64 KiB cap during streaming, including files that grow after the initial metadata check.
- Regression tests cover malformed advertised paths, HRESULT failure propagation, missing status and bounded reads.

### Release 1.0.0 - delivered milestones

- **0.8 Real Settings Engine:** direct bounded Machine/User Registry.pol (PReg), Machine GptTmpl.inf and curated standard GPP XML item attributes from one session-pinned DC, independent source SHA-256. GPP attributes are a partial projection with cpassword and other sensitive attributes redacted; Item Level Targeting and CSE execution are NOT evaluated. Other CSE formats are still unsupported.
- **0.9 Security Settings Editor:** finite-choice editor for existing Event Audit and the PasswordComplexity/ClearTextPassword System Access entries. Requires WRITE ENABLED, validated same-domain source path, a completed GPMC backup, source-hash concurrency check, mandatory before/after approval and audit trail. Privilege Rights, SIDs, ACLs, Restricted Groups and arbitrary INF text are not editable.
- **0.10 GPO Health Check:** read-only AD GPC versionNumber vs pinned-DC SYSVOL GPT.INI comparison, policy-file read status, source fingerprinting and TXT export. **Compare DC versions...** compares up to 16 explicitly named DCs without a DFS alias; disagreement and unknown values are never treated as consistent. Version matching is not proof of all-file DFSR convergence.
- **0.11 Impact / RSoP preview:** direct site/domain/OU GPO link evidence, enabled/enforced state, WMI/scope caveats and optional last-logged client gpresult sample. It cannot predict exact recipients of a hypothetical change, and no domain-wide effective-policy verdict is claimed.
- **1.0 Full evidence and limited selective recovery:** local, operator-requested ZIP with GPMC XML when available, stored-source JSON/CSV, SHA-256 sums, GPO health, link footprint and coverage manifest. Confidential data is not uploaded automatically. Backups tab can selectively restore **one existing supported SecEdit numeric value** from an original-domain GPMC backup; no other GPO settings, ACLs or links are changed.
- Core regression tests, Windows WPF CI, x64 and ARM64 portable builds. **Before production writes, perform a nonproduction GPO smoke test on a real DC**; live AD/SYSVOL writes, network File.Replace support, GPMC Save and client RSoP cannot be fully validated in GitHub CI. Safe mode is read-only by default.

### Real Settings Engine: source-file evidence (0.8.0)

In **All Settings**, select a specific GPO and click **Read selected GPO files**. This is a read-only source snapshot on the session-pinned domain controller, requiring no MMC navigation. It parses Machine/User `Registry.pol` as binary PReg v1 and Machine's `Microsoft/Windows NT/SecEdit/GptTmpl.inf` as an encoding-aware security template. The file scan produces source rows with actual stored values, type, category, source path, SHA-256 and clear evidence/coverage. Set the Source filter to **Stored GPO files (read only)** and open a record's **View source evidence...** dialog. Raw source rows remain read-only by default. The separate **Edit stored security...** action permits only existing, recognized Event Audit and selected System Access numeric entries after enabling write mode, a GPMC safety backup, SHA-256 freshness checks and mandatory confirmation. All other raw source rows remain read-only.

A file absent from SYSVOL is marked **Absent**, NOT an inferred Not Configured policy. Corrupt, unsupported-version or truncated files are marked **PARTIAL** with diagnostics, preserving any verified prior rows. Raw source values do not establish winning settings, RSoP, WMI applicability or security-filter permissions. Registry.pol special deletion operations remain labeled as instructions, not current values. This is a first-stage source engine; additional CSE formats need separate modules and independent regression tests before treating them as supported.

### Permission and comparison accuracy (0.7.3)

Permission-aware buttons now require **two independent pieces of evidence** before enabling normal policy setting edits: matching-token AD GPO edit permission from the GPMC permission view, and a successful read/write GPT.INI handle check on the pinned SYSVOL. A positive SYSVOL check no longer grants missing AD edit permission, and explicit matching denies fail closed. The UI labels these as **simplified evidence, not a complete Windows AuthZ evaluation**. Safe mode and server-side authorization remain mandatory.

Compare & Conflicts excludes descriptive GPMC SecuritySettings XML leaves such as Member/Registry ACL. Multiple differing values for one policy identity in a single GPO are marked **Ambiguous index**, never silently reduced to the first row. Missing settings in the loaded index are shown as **Not in loaded index**, not assumed Not Configured. Conflict drill-down opens the unified All Settings workspace rather than the collapsed legacy grid.

### Unified Settings workspace (0.7.0)

**All Settings** is now the primary search, inspection and edit workspace. It merges configured GPMC XML index entries, available ADMX templates, and optional rows observed in the read-only MMC inventory. Search by setting, category, GPO, registry target, source or value, or filter by source/state/GPO. Each row distinguishes its evidence and safest supported editing route. Configure an ADMX template by explicitly choosing a GPO; configured security or ADMX settings continue through the existing backup/preview/write-guarded editors. Unsupported/custom MMC entries are read-only, not silently edited.

The old raw data grids were **not removed**: expand **Advanced sources and diagnostics** under All Settings for original GPMC, MMC and ADMX views. Previously separate GPP editors are now in **Preferences (GPP)** to keep the main tab bar compact. Existing user workspaces are migrated from their old tab positions. Settings absent from GPMC XML are never assumed Not Configured; ADMX template rows explicitly show **Template - state unknown**.

### GPMC Security Settings XML clarity (0.7.2)

In **All Settings**, verbose GPMC XML leaf descriptions (such as `Member: DOMAIN\\Group...` and `Registry` security ACLs) are hidden by default to keep the configurable policy list clear. Enable **Show XML details** to see them, or use **Advanced sources and diagnostics** for the complete unfiltered GPMC index. A technical row offers **View XML details...**, not an unsupported direct policy editor; the original text stays available and is never changed by viewing it.

GPMC SecuritySettings membership descriptions can navigate to **Restricted Groups** and registry ACL entries to **Security Settings > Registry** as **manual section-only MMC navigation** when the category can be confidently inferred. These are not independently verified exact setting dialogs, and no automatic ADMX/security-template write is performed for them.

### MMC route-audit accuracy (0.7.1)

MMC Route Audit now resolves the standard decorated **Administrative Templates: Policy definitions (ADMX files) retrieved from the central store** tree label without accepting unsafe partial names. The audit checks indexed paths first, then captures a bounded 1,500-node / depth-12 tree snapshot. Its report separates FOUND/MISSING/ERROR/SKIPPED/UNCHECKED routes and explicitly marks incomplete snapshots PARTIAL. It does not claim that all 14,000+ ADMX definitions were enumerated or that FOUND proves an exact editable policy row.

### MMC Full Settings Inventory (0.6.0; safer navigation in 0.6.1)

In **All Settings**, open **MMC Full Settings Inventory** (an inner view, not a separate main tab). Choose a reference GPO and click **Scan MMC (read-only)**. The application opens one native GPMC editor and walks its actual MMC tree and native SysListView32 policy lists. The inventory includes search, raw MMC values/states, GPMC XML and ADMX correlation, section coverage diagnostics, CSV export, and an explicit **Open exact in MMC** action that requires a unique literal row match.

This is a **bounded, best-effort inventory**, not a guarantee that all installed third-party snap-ins expose native rows. Unavailable/virtualized controls, canceled or timed-out scans, and unreadable sections are reported as **PARTIAL**, never silently completed. A missing GPMC configured row is **not** proof of Not Configured; that state is displayed only when actually observed in MMC. The scan never edits GPO settings. A manual native editor opens only on explicit request. From v0.6.1 the Scripts (Startup/Shutdown) and Scripts (Logon/Logoff) MMC snap-ins are intentionally skipped before automated selection/expansion because their extensions may crash; use the dedicated **GPO Scripts** tab instead. These sections appear as incomplete in **Coverage / skipped sections**. If MMC shows a modal snap-in error, the scan stops with PARTIAL diagnostics, never auto-clicks the dialog or sets permanent ignore, and leaves the MMC session for manual investigation.

### Script editor and visual status colors (0.4.4)

The GPO Scripts tab provides a rich, portable AvalonEdit editor with syntax colors for BAT/CMD and PowerShell scripts, plus supported JS/VBS/HTML script definitions. Colors come from the shared UiStyle / WindowsCompact.xaml palette. Script-file extensions are colored by type; disabled or partially active GPO scopes, missing files and unassigned scripts have clear visual and textual status indicators.

The editor provides line numbers, word wrap, visible tabs/spaces, adjustable font size, find/replace/replace all, Undo/Redo, goto line, comment/uncomment, indent/outdent, keyboard shortcuts, and export to a local copy. The syntax check uses the PowerShell AST parser **without executing script content**. BAT/CMD supports useful static warnings such as unresolved and duplicate labels, but this is not a full CMD interpreter. Diagnostics can be opened by line.

Saving still requires explicitly enabling WRITE mode, creates a GPO backup, previews the change, verifies the SYSVOL file bytes, and reports save/rollback errors without discarding the edited text. From v0.5.2 onward, successful script edits also record SHA-256/size/encoding/BOM/line-ending evidence in the audit Before and After fields, plus a changed-line summary; script source and embedded credentials are not logged. A no-op does not generate a misleading script edit audit event. Older empty Before/After fields remain historical and can only be investigated using surviving GPO backups. Test on an isolated GPO before editing production scripts.

### Diagnostics on multiple computers

The application watches its own local diagnostic log queue every 30 minutes.
If three or more unsent logs accumulate, an amber "N logs · Review" action is
shown **only when GitHub is reachable**. No Internet connectivity means no
automatic release lookup and no invitation to send logs. Diagnostic collection,
local export and GPO functions continue to work offline.

"Review local logs" in Diagnostics opens a local preview; the user can export
it without a network connection. Public GitHub Issues are **opt-in**, with a
separate approval immediately before sending. The default report contains
anonymous fingerprints/counts, not raw AD names or policy contents. Optional
redacted excerpts must be inspected before publication. A fine-grained GitHub
token with only Issues read/write permission is required; optionally store it
under the current Windows user using DPAPI. No token is included in an Issue.

On a confirmed successful issue submission, unchanged original log files move
into a 14-day local archive and their hashes are recorded, so they are not
offered twice. Interrupted/offline sends preserve the originals. A build/scheduled
GitHub Actions job summarizes existing Issues across installations, without
performing automatic repairs or posting private log bodies.

### Encoding, MMC routes, hierarchy and conflict analysis

- The script editor supports ANSI/OEM, Cyrillic, Hebrew and Unicode code pages, BOM choices, source-file re-decode, explicit DOS/Unix/Mac EOL conversion and static Unicode hazard diagnostics. Conversion uses strict encoders so unrepresentable characters cannot silently turn into question marks. SYSVOL writes retain the existing GPO backup/preview/byte-verification requirements.
- In Diagnostics, select a GPO and use **Audit MMC paths** after indexing its settings. This read-only scanner checks the actual MMC tree and saves a local `MmcRouteAudit-*.txt` report with found/missing sections. Existing opt-in diagnostics review can submit an anonymized summary to GitHub.
- The single **GPO Hierarchy & Links** tab has a live, searchable site/domain/OU tree on the left and link management on the right, with a resizable divider. OU selection filters its direct links; selecting a GPO link in the tree selects the matching row for editing. A shortcut is available on the GPOs tab; the full hierarchy window is still available. Link order 1 is highest only within the same container.
- Compare & Conflicts shows both duplicate and differing settings, overlap evidence derived from loaded links, and a per-GPO explanation. Its **Verify RSoP / WMI / Security** action checks a named sample computer, its gpresult XML, assigned WMI filters and GPO AD security descriptors. Missing, denied or unverifiable evidence fails closed. Even a passing sample does NOT establish equivalent domain-wide application or justify automatic merging. No merge/unlink occurs.
- The LAN Manager authentication setting continues to use the proven native MMC exact-row opening mechanism.

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

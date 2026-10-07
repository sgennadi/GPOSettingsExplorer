# GPO Settings Explorer

[![Build](https://github.com/sgennadi/GPOSettingsExplorer/actions/workflows/build.yml/badge.svg)](https://github.com/sgennadi/GPOSettingsExplorer/actions/workflows/build.yml)
[![Latest Release](https://img.shields.io/github/v/release/sgennadi/GPOSettingsExplorer)](https://github.com/sgennadi/GPOSettingsExplorer/releases/latest)
[![Windows](https://img.shields.io/badge/platform-Windows-0078D4)](https://github.com/sgennadi/GPOSettingsExplorer)

A portable Windows GUI for browsing, searching, editing, backing up, comparing, and managing Active Directory Group Policy Objects without relying on PowerShell for the main application workflow.

GPO Settings Explorer is intended for administrators who want one compact interface for classic GPO settings, Administrative Templates, WMI filters, delegation, links, backups, and Group Policy Preferences.

## Download

Download the latest portable build from:

**[GitHub Releases](https://github.com/sgennadi/GPOSettingsExplorer/releases/latest)**

Current release: **v0.2.5**

Available packages:

- `GPOSettingsExplorer-win-x64-portable.zip` — Intel/AMD 64-bit Windows.
- `GPOSettingsExplorer-win-arm64-portable.zip` — Windows on ARM64.

The builds are self-contained. A separate .NET runtime installation is not required.

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

### Administrative Templates / ADMX

- Load policies from the domain Central Store when available.
- Fall back to the local Windows `PolicyDefinitions` store.
- Search the ADMX catalog.
- View policy scope, category, registry key, value, and explanation.
- Configure supported Administrative Template settings directly from the application.

### WMI filters

- Browse WMI filters.
- Create and edit WMI filters.
- Edit WQL rules.
- Assign or remove WMI filters from GPOs.
- Test filter rules against computers.

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

The application also includes generic GPP XML browsing/editing for scenarios where a structured editor is not available or raw XML access is required.

Where supported, GPP editors include item-level targeting, clone/delete operations, raw XML handoff, validation, backups, and audit integration.

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
4. The application detects the current AD domain through LDAP/RootDSE.
5. If GPMC is not installed, GPMC-dependent operations will not be available.

No installer is required.

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
4. Publish self-contained win-x64.
5. Publish self-contained win-arm64.
6. Create portable ZIP archives.
7. Upload build artifacts.
8. Create a GitHub Release for a new project version.

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
├─ CHANGELOG.md
├─ UI_GUIDELINES.md
└─ GPOSettingsExplorer.sln
```

## Current status

**v0.2.5** adds a persistent incremental All Settings cache: cached results appear immediately, only new/modified GPOs are re-indexed by GUID + ModificationTime, deleted GPOs are removed automatically, and the cache survives portable application updates under %LOCALAPPDATA%.

See [CHANGELOG.md](CHANGELOG.md) for release details.

## Disclaimer

Group Policy changes can affect many computers and users. Test changes in a controlled OU or lab before applying them broadly in production.

# GPMC source-to-MMC route verification (v2.1 preview)

## Preview 12: exact GPMC Registry source lookup

The native GPMC Settings report groups one GPO by Computer/User, Policies and Preferences. In All Settings, a typed GPP Registry item can now open the fresh report directly at its exact UID/order/action/full target. Missing, changed or duplicate matches are blocked: a similarly named row is never a fallback. Package Applicability is technical metadata, not an independent registry setting. See [source navigation](gpmc-exact-registry-source.md).

## Preview 11: Password / Lockout and GPP Registry saved-source evidence

**Exact MMC Paths → Check selected stored source...** supports:
- Typed Account 'Type=Password' or 'Type=Account Lockout': one Computer GPMC Account row must match a unique SHA-256-backed [System Access] key in the same GPO's Machine/Microsoft/Windows NT/SecEdit/GptTmpl.inf. Canonical integers and explicit Booleans yield stored-snapshot match, difference or blocker. Effective account-policy precedence and client RSoP are not proven.
- GPP Registry: selected GPO/scope Registry.xml on pinned DC is read on demand. Complete, bounded and unique UID/order/action/hive/key/value name/type/targeting-filter presence are compared against cached GPMC metadata. **GPP value data, filters' predicates, client GPP execution and live registry values remain NOT VERIFIED.** Neither value nor targeting content is retained in this source-verification snapshot.
- Nonstandard nested GPP Collections, duplicate UIDs, wrong GPO/domain/DC, partial reads, missing sources, DTD XML and unsupported formats fail closed. Native MMC scans remain manual per selected GPO.


## Why this matters

GPMC XML includes both actual configured policies and *metadata* (permissions, certificate details, task/service attributes, GPP preferences, and package details). Those XML nodes do **not** all live under **Security Settings** or **Registry**. The older generic fallback routed unfamiliar extensions to the Security Settings root, which looked like a successful open but did not identify the actual setting.

The 2026-10-09 route audit of the user's domain indexed 185 GPOs and 12 distinct section paths. The scan found 11 paths and missed the decorated Administrative Templates node (its central-store suffix later received exact alias matching). **883 Computer and 709 User entries shared a generic Security Settings parent**, despite coming from many different source families. These are *indexed entries affected by imprecise navigation*, NOT a validated count of individually wrong policies.

## Typed Kerberos saved-source comparison (Preview 7)

**Exact MMC Paths** now has an operator-triggered **Check Kerberos GptTmpl.inf...** action. It reads only **one selected GPO** from the **pinned DC** and checks the complete `Machine/Microsoft/Windows NT/SecEdit/GptTmpl.inf` source. Comparison to the typed GPMC `[Kerberos Policy]` Account record requires one matching GPO/scope/Name/Type entry, canonical number/boolean, unique exact INF key and a complete SHA-256 source-file snapshot. `STORED_SNAPSHOTS_MATCH` and `STORED_SNAPSHOTS_DIFFER` compare **captured saved values only**. They never change the independent MMC route status or imply native MMC editing succeeded, replication consistency, live current DC state, effective client RSoP or compliance.

The new action does not invoke MMC or touch unrelated GPP/security families. Missing/partial/duplicate/mismatched source is reported as explicit `GPTTMPL_*` or `KERBEROS_SOURCE_*` blockers, not `Not Configured`. As of Preview 9, SRP **Path Rule** alone has a typed, unique, SHA-256-bound User/Computer Registry.pol ItemData path and Disallowed/Unrestricted level check. Other SRP rule kinds, flags, enforcement and effective client RSoP remain unverified. Full native MMC traversal remains manual for one selected GPO because fragile snap-ins have been observed on a DC.

## MMC snap-in error on a DC (Preview 6)

A GPO Settings Explorer 2.1 Preview screenshot from a YOSH-DC03 session showed the native **"MMC has detected an error in a snap-in"** dialog while a per-setting discovery action was running a full native inventory and the progress text had reached **Local Policies > User Rights Assignment**. The progress location is the last attempted section, **not definitive evidence which snap-in failed**. It does not alone establish corrupted GPOs or AD.

Preview 6 removes the hidden full scan from an individual Setting Value window. **Check captured MMC route** only uses an existing, non-aborted inventory for the same GPO and pinned DC; otherwise it directs the operator to select **MMC Full Settings Inventory** and explicitly run **Scan MMC (read-only)** for one reference GPO.

The manual scanner skips Scripts and User Rights Assignment *before* expanding/selecting those nodes and records them as **Skipped - unsafe snap-in**. A separate native dialog watchdog cancels the scan if an MMC modal appears. Captured records from an interrupted scan remain diagnostic only: **STOPPED / PARTIAL** blocks exact-route verification. The program NEVER presses Ignore, Always ignore, OK, or closes an MMC error dialog. A blocked UI Automation call might need the operator to close the native MMC window manually.

On a DC showing this error: cancel the application scan, close the failed MMC console manually without choosing to ignore the error, and inspect Event Viewer **Windows Logs > Application** for MMC/.NET/Application Error events. Avoid further unattended scans until the failing snap-in or OS component is isolated. Run the native editor in an isolated nonproduction environment for deeper diagnosis.

## Safe routing rules

- Only an exact, known extension and expected GPMC category yields an automated MMC section path.
- Security Settings section rows are classified by known categories. Kerberos additionally requires an explicit `Type=Kerberos`, and security details such as ACLs and group members are read-only.
- Raw `RegistrySettings` and explicit `Administrative Templates` source use **Policies > Administrative Templates**, not **Preferences > Windows Settings > Registry**. Some registry.pol keys do not have installed ADMX definitions, so exact row focus remains unverified.
- Generic GPMC `Key`, `Name`, or `ValueName` fields are *not* proof of a registry backing. GPMC Security Options `KeyName` is source metadata, not an independently verified Registry.pol location.
- No route for a source extension is **UNMAPPED** (not Missing / Not Configured). The operator can open the selected full GPO editor and navigate manually; no different policy is selected.
- Verified MMC **section** does not imply verified exact policy row, effective RSoP, permission to edit, or AD/SYSVOL replication consistency.

## Kerberos, Software Restriction and startup scan status (2.1 preview)

- **Known Kerberos Policy:** Five explicitly typed GPMC Account settings can request an exact native MMC row through their documented display names. Example: `Account: MaxClockSkew` must map to `Maximum tolerance for computer clock synchronization`. The current editor list must expose **exactly one** such row. A section-only route is not verified row navigation; activating Properties is not a saved change or effective RSoP.
- **SoftwareRestrictionSettings / Path Rule:** On both Computer and User scopes the route is **Windows Settings > Security Settings > Software Restriction Policies > Additional Rules**. A Path Rule can request an exact row only when its GPMC report includes one well-formed literal `Path=...` field and the native list contains one identical path. A generic Path Rule label is not a unique identity. Unknown SRP metadata may navigate only to the documented parent.
- **MMC Not Verified:** means native MMC row/value corroboration has **not** been completed. It does not mean Not Configured. A successful GPMC XML index or ADMX catalog scan cannot turn every Windows policy into verified native MMC evidence.
- **Refresh cached GPMC index on startup:** optional per-user checkbox in **MMC Full Settings Inventory**. Uses the existing on-disk index and refreshes only changed/new GPO XML in the background. Off by default for production DC safety. No automated MMC windows, no GPO edits, no client RSoP claims. For a selected GPO, run **Scan MMC (read-only)** and use **Discovery & Verification** to inspect which exact rows/source files are eligible for a stronger status.

## Exact MMC Paths for all indexed GPMC settings (Preview 3)

Open **All Settings > Configured GPMC settings > Exact MMC Paths...** (or **MMC Full Settings Inventory > Exact Paths (all indexed)...**). This window shows every currently indexed GPMC setting with separate:
- GPMC source/GPO/scope and the route's documented *candidate section*;
- `MMC_NOT_SCANNED`, `EXACT_ROW_OBSERVED`, `SECTION_ONLY`, `AMBIGUOUS`, `SOURCE_AMBIGUOUS`, `EXACT_ROW_NOT_OBSERVED`, or `UNSUPPORTED_SOURCE`;
- the literal complete MMC tree+row path only when a unique matching native row was actually observed.

Click **Find / open selected exact route...** to inspect one GPO. With explicit permission, a bounded read-only scan opens a separate MMC editor and reads its safe native tree/list. The selected row is matched by exact GPO, scope, known family, full section hierarchy, and literal policy identity; duplicate names/folders never silently select a first match. A new MMC editor re-reads the route before requesting Properties. For unknown/custom source extensions, a *one-time operator-reviewed route picker* can show the same-GPO/scope observed rows; it requires deliberate acknowledgement and re-verifies the selected route in a new native editor. Operator-reviewed navigation does not count as automatically verified source identity or stored/effective policy value. Source credentials and registry policy values are never copied into an automatic route cache. This reviewed binding is not persisted across sessions.

Recognized candidate sections include **Group Policy Preferences** (e.g. Scheduled Tasks, Drive Maps, Files, Folders, Environment, Printers, Local Users and Groups, Services, Power Options), **Kerberos**, **Password Policy**, **Account Lockout Policy**, **Security Options**, **Advanced Audit** children, selected **Public Key** sections and native **ADMX** categories. An ADMX row requires a unique matching ADMX definition and complete native category, not a same-name shortcut. For scripts, ACL XML detail, GPP custom forms, extension-specific controls or incomplete scans, the program deliberately retains SECTION_ONLY/UNSUPPORTED and directs users to the dedicated source view/editor rather than launching a potentially wrong dialog.

**Do not run unattended MMC scans over every GPO at startup on a DC.** The available startup checkbox indexes only new/modified GPMC XML using its per-user cache; it does not launch MMC or claim RSoP. All-GPO native MMC enumeration can take many hours and some snap-ins are unsafe to automate. Run a single-GPO scan with progress/cancel and consult coverage/skipped sections.

## How to validate on a test DC

1. Use a nonproduction GPO and run **MMC Route Audit (read-only)** from Diagnostics/Advanced tools.
2. Inspect **INDEX COVERAGE** and the **Unmapped source families** report. Do not treat generic extension counts as findings about the number of configured GPOs.
3. Compare a sample of each mapped category against the real Group Policy Management Editor at the same elevation, including central-store **Administrative Templates**, **Security Options**, **User Rights Assignment**, **Kerberos Policy**, and **Advanced Audit Policy**.
4. In All Settings, inspect source fields: the **Registry** label must say *not verified* for non-Registry.pol extension metadata. The source, GPO identity, and scope must be correct.
5. Open representative **Scripts**, **ScheduledTasksSettings**, **FolderRedirectionSettings**, **DriveMapSettings**, **PowerOptionsSettings**, **LugsSettings**, **WindowsFirewallSettings**, and third-party extensions. If a route is not covered, ensure the value window says **UNMAPPED** and opens only the full editor for manual navigation.
6. Save the new MMC Route Audit and versioned diagnostics for further mapping work. Do not attach confidential raw GPO XML, scripts, SID lists or credentials to a public GitHub issue. Review/redact first.

## Coverage limitations and future work

The verified route table is intentionally smaller than the Windows GPO feature catalog. To add automatic routes for GPP tasks, drives, scripts, folder redirection, firewall, and vendor-specific ADMX trees, collect read-only representative XML samples and exact observed MMC paths and require a unique exact match. Avoid guessing paths by substrings. A native, read-only GPMC-style full GPO Settings report remains available from the **GPOs** context menu irrespective of automatic MMC route coverage.

When extending routing, test ordinary and edge-case XML layouts, both scopes, absent categories, localized or central-store display names, duplicate policy names, partial MMC trees, and permission failures. Never treat Unknown as compliant or Not Configured.

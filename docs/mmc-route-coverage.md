# GPMC source-to-MMC route verification (v2.0.3)

## Why this matters

GPMC XML includes both actual configured policies and *metadata* (permissions, certificate details, task/service attributes, GPP preferences, and package details). Those XML nodes do **not** all live under **Security Settings** or **Registry**. The older generic fallback routed unfamiliar extensions to the Security Settings root, which looked like a successful open but did not identify the actual setting.

The 2026-10-09 route audit of the user's domain indexed 185 GPOs and 12 distinct section paths. The scan found 11 paths and missed the decorated Administrative Templates node (its central-store suffix later received exact alias matching). **883 Computer and 709 User entries shared a generic Security Settings parent**, despite coming from many different source families. These are *indexed entries affected by imprecise navigation*, NOT a validated count of individually wrong policies.

## Safe routing rules

- Only an exact, known extension and expected GPMC category yields an automated MMC section path.
- Security Settings section rows are classified by known categories. Kerberos additionally requires an explicit `Type=Kerberos`, and security details such as ACLs and group members are read-only.
- Raw `RegistrySettings` and explicit `Administrative Templates` source use **Policies > Administrative Templates**, not **Preferences > Windows Settings > Registry**. Some registry.pol keys do not have installed ADMX definitions, so exact row focus remains unverified.
- Generic GPMC `Key`, `Name`, or `ValueName` fields are *not* proof of a registry backing. GPMC Security Options `KeyName` is source metadata, not an independently verified Registry.pol location.
- No route for a source extension is **UNMAPPED** (not Missing / Not Configured). The operator can open the selected full GPO editor and navigate manually; no different policy is selected.
- Verified MMC **section** does not imply verified exact policy row, effective RSoP, permission to edit, or AD/SYSVOL replication consistency.

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

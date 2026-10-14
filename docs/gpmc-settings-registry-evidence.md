# GPMC Settings: typed Registry preference evidence (2.1 Preview 10)

## Why the Microsoft GPMC Settings tab looks different

Native GPMC's **Settings** tab renders an XML report for **one selected GPO**.
The GPO Settings Explorer **All Settings** grid instead combines many GPOs and
sources, including generic XML metadata that may not be an editable policy.

Two screenshots can be from different GPOs. An **Applicability** field under
**SoftwareInstallationSettings** is installation/package metadata, not a
registry preference. It must not become a registry path merely because GPMC
can show such paths for a *different* GPO.

## Example: a GPP Registry Delete preference

The GPMC Computer Preferences > Windows Settings > Registry report can display:

- Action: **Delete**
- Hive: **HKEY_LOCAL_MACHINE**
- Key path: **SYSTEM\CurrentControlSet\Services\LanmanServer\Shares\Security**
- Value name: **pslogging$**

This identifies a **stored GPP Registry preference item**. Action=Delete
requests that a client-side Group Policy Preferences Registry CSE remove the
target on eligible clients. It is not evidence the value currently exists, was
removed, or that the GPO applied to any particular machine.

The GPMC report and the preference XML are separate evidence sources from:
- Machine/Registry.pol or User/Registry.pol used by many Administrative Templates
- Security Settings/SecEdit GptTmpl.inf
- The registry of the DC or of a particular client
- Effective Group Policy RSoP

Never attach a GPP key to a Registry.pol value merely because the paths match.

## Using GPO Settings Explorer

1. Select the correct GPO in **All Settings** or the GPO grid. GPMC source
   reports operate on a specific GPO, not the whole domain.
2. On first load, the new GPMC index schema rebuilds the per-user local index.
   The **Rebuild index** command can be used to force a refresh.
3. Filter to **Configured (GPMC)** and search by pslogging$, LanmanServer,
   Registry preference or the literal key. Structured items show their
   GPP Action, Hive, Key path and Value name. No raw GPP value data is added
   to the search index.
4. Double-click a configured source row. In **Setting Value**, use
   **View GPMC Settings report...** to open **that row's GPO**, even if the
   current main-grid selection has changed.
5. In the report, use **Find in report** by key path, item label, value name,
   or extension. If a typed Registry preference is selected, click
   **Copy registry target**. Unparseable sources have no copyable target.
6. For the full original GPP Registry.xml source or supported guarded editing,
   use the program's existing **Preferences (GPP) > Registry** workspace;
   validate UID, scope, action, filters and source freshness before editing.
7. XML leaves such as MSI **Applicability** are hidden from the default primary
   list. Turn on **Show XML details** to inspect them; do not treat them as
   independent Administrative Templates.

## Evidence status

- **GPMC GPP source located:** a structurally recognized Registry
  preference item from a bounded GPMC report.
- **GPP source unparsed:** unknown action/hive or incomplete key attributes.
  No path is inferred.
- **MMC row not verified:** the native editor item has not been independently
  matched in an actual MMC session. GPMC report data alone does not change this.
- **Effective client RSoP not checked:** report discovery says nothing about
  filtering, precedence, client extension application or whether a Delete
  action succeeded.

The preview makes **no GPO/AD/SYSVOL writes** when indexing or viewing.
Mass native MMC startup scans remain disabled because OS/vendor snap-ins can
fail on domain controllers. Source reports may contain sensitive registry
paths: handle saved/copyable details as confidential administrative data.

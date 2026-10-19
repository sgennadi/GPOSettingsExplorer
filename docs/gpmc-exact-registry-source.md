# GPMC Registry source paths and exact report navigation (2.1 Preview 12)

The Microsoft Group Policy Management Console **Settings** tab is a hierarchical
report of **one selected GPO**, grouped by Computer/User, Policies and
Preferences. It is **not** an index of every XML element as a separately
editable policy.

## Why Applicability did not show a registry key

SoftwareInstallationSettings / Applicability describes the conditions and
security metadata of a software installation package. It is not a GPP Registry
entry and does not have a proven registry key.

A different GPO's **Preferences > Windows Settings > Registry** item may
contain Action, Hive, Key path, Value name, and item UID. Do **not** transfer
that path to Applicability: the screenshots show different GPOs and extensions.

The legacy **Configured GPMC settings** table hides technical XML leaves by
default. Turn on **Show XML metadata** to inspect them; the original report
remains available. Known GPP Registry items show their source key and value
name in separate columns, without treating them as Registry.pol policies.

## Locate the same GPP Registry item in a fresh GPMC report

1. In **All Settings**, select a GPO and find the typed **Registry preference**
   row (GppRegistry). Do not select Software Installation Applicability.
2. Inspect Action, Hive, Key path, Value name and Order in **Setting Value**.
   These fields come from typed Registry/Properties of that GPO's GPMC XML,
   **not local Regedit**.
3. Choose **Locate exact GPP item in GPMC report...**. A *fresh* read-only
   report is generated for the same GPO ID. The locator requires the same
   Computer/User scope, UID, order, action and full registry key/value target.
4. If exactly one item matches, its report tree node is expanded and selected.
   **Copy registry target** copies stored policy source metadata, not a live
   client registry observation.
5. If UID/order is missing, duplicated, or action/path/order has changed
   since indexing, no item is selected. Rebuild the GPMC index; never trust
   the first similarly named item.
6. For a separate on-demand comparison against Registry.xml on the pinned
   DC, use **Exact MMC Paths > Check GPP Registry.xml metadata...**.
   This SHA-256-bound check does not compare raw registry value data.

## Status distinctions

- **EXACT_GPMC_REGISTRY_SOURCE:** Same GPO, scope, UID, order, action and
  registry target in the new GPMC report. Not native MMC and not RSoP.
- **REGISTRY_UID_NOT_FOUND / REGISTRY_UID_AMBIGUOUS /
  GPMC_REGISTRY_METADATA_CHANGED / WRONG_GPO / SCOPE_NOT_FOUND:** Identity
  missing, stale, cross-GPO or ambiguous. No automatic jump.
- **GPP_STORED_METADATA_MATCH:** Separate Registry.xml structural evidence
  matches the GPMC item on a pinned DC. Not actual client processing,
  live registry state or verified raw registry value data.

Different Group Policy extension families require different source readers.
Report navigation never edits Active Directory, SYSVOL or client registry.

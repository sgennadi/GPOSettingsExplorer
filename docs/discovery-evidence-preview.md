# Discovery evidence (2.1 development)

## Preview 11: typed Password/Lockout and GPP Registry source checks

Open **Exact MMC Paths**, select a typed **Password Policy** or **Account Lockout** GPMC setting and choose **Check Password/Lockout GptTmpl.inf...**. It reads one GPO's GptTmpl.inf from pinned DC and compares exact [System Access] numbers/booleans with canonical typed GPMC values. A stored match/difference is separate from native MMC route, effective domain account policy and client RSoP.

For a GPMC **GPP Registry** item choose **Check GPP Registry.xml metadata...**. Read only that GPO's Computer or User Registry.xml from pinned DC; compare UID, order, action, hive, key, name, type and targeting-filter presence. Missing/partial/unknown XML remains blocked. Raw value/password/filter details are not retained: **GPP_STORED_METADATA_MATCH is NOT a value check**, GPP client execution or live client registry verification.


This feature is read-only and does not change Group Policy or SYSVOL. Optional startup **GPMC indexing** uses a per-user preference and the incremental cache; it does not start the native MMC editor for all GPOs. MMC observations, source values and RSoP have distinct statuses; no bulk scanner can truthfully set every Windows policy to VERIFIED without type-specific evidence.

## Preview 9: SRP stored source path and level comparison

In **Exact MMC Paths**, select a legacy **SoftwareRestrictionSettings / Path Rule** and click **Check SRP Registry.pol...**. Only the selected GPO's **User** or **Computer** Registry.pol is read from the session-pinned DC (read-only). The typed checker requires one literal GPMC Path, one known SecurityLevel (`Disallowed` or `Unrestricted`), a unique GPMC identity and exactly one REG_SZ/REG_EXPAND_SZ `Safer\\CodeIdentifiers\\{0|262144}\\Paths\\{GUID}\\ItemData` record with an exact unexpanded Path. The entire source file must be parsed successfully and have matching file/row SHA-256. A different saved security level yields `STORED_SRP_PATH_LEVEL_DIFFER`; one matching path and level yields `STORED_SRP_PATH_LEVEL_MATCH`. Every ambiguous/missing/partial/unknown match remains blocked. No registry hive is mounted or edited. Other rule flags, SRP evaluation precedence, native MMC state and client RSoP remain unverified. Hash, Internet Zone and certificate SRP rules are not yet supported.

## Preview 8: on-demand Kerberos comparison in Setting Value

Open a known typed Kerberos setting from **All Settings**, then select **Verify stored Kerberos source...**. This reads only the selected GPO's pinned-DC `Machine/Microsoft/Windows NT/SecEdit/GptTmpl.inf` (read-only and cancellable). It compares the cached GPMC report number/boolean with a uniquely identified, fully read, SHA-256-backed INF key. `STORED_SNAPSHOTS_MATCH` means the saved literals agree, not that native MMC, replication, live GPMC or effective client RSoP has been verified. Missing, duplicate, malformed, partial and mismatched source evidence cannot become Verified. An open **Exact MMC Paths** window also receives the latest same-GPO security source snapshot.

Software Restriction Path Rules still lack a trusted typed source comparison; the policy value dialog explains this limitation.

## Preview 7: Kerberos stored-source snapshot evidence

Open **Exact MMC Paths** (separate from the Registry.pol-only **Discovery & Verification** unit review), select a typed Kerberos Account setting, then choose **Check Kerberos GptTmpl.inf...**. The program reads only that GPO's SecEdit `GptTmpl.inf` on the pinned DC, hashes the captured file and compares one canonical `SettingNumber` or `SettingBoolean` with the exact `[Kerberos Policy]` entry. Results appear in the **Kerberos stored source** column independently of MMC navigation. Missing or incomplete files, wrong GPO/DC, duplicate source/GPMC identities and unsupported value representations are blockers. Values still represent captured GPMC/SYSVOL snapshots, not atomic live DC equality, effective client RSoP, or signed-off units. This action is opt-in per GPO and **never launches native MMC**.

## Preview 6 fail-closed MMC inventory

A per-setting **Check captured MMC route...** action must never cause a hidden deep inventory of the entire native GPO editor. With no existing non-aborted same-GPO/DC snapshot, it reports **NO VERIFIED MMC SNAPSHOT** and gives the explicit manual inventory workflow. The operator may choose a bounded read-only scan for one GPO in **MMC Full Settings Inventory**; Scripts and User Rights Assignment nodes are conservatively skipped and marked incomplete. A detected native MMC modal error requests cancellation and marks the results **STOPPED / PARTIAL**; those rows are never eligible for exact-route verification, unit approval or effective RSoP assertions. The native dialog is left intact for manual investigation. UI Automation can remain blocked until the operator closes that MMC dialog.

## Exact MMC paths for the complete GPMC index

The **Exact MMC Paths** window lists every indexed GPMC row, whether or not that GPO has been scanned. The candidate section and the exact observed native row have separate fields; a known parent section never means that a specific policy's properties or value is verified. The operator can explicitly request a bounded, cancellable read-only MMC scan of only the selected GPO from **MMC Full Settings Inventory**, never implicitly from a single Setting Value window. Exact GPO/scope/native paths are matched by source-aware types, and an unhandled source can receive a one-time, explicitly reviewed native route after revalidation in a newly opened editor. No automatic cross-GPO path inference, no blanket mass MMC scans, no persistent secret-bearing route cache, and no GPO writes.

## Using the current preview

1. Connect to the domain and load the GPMC settings index.
2. Open **MMC Full Settings Inventory** and select a specific GPO.
3. Run **Scan MMC (read-only)** to capture exact native rows for the selected GPO (incomplete sections remain explicitly marked).
4. In **All Settings**, load stored GPO source files on the same selected GPO and pinned domain controller, using **Read stored sources**.
5. Select **Discovery & Verification...** in the MMC Full Settings Inventory toolbar. Inspect the searchable verification grid, the selected setting's source/MMC/hash diagnostics and review eligibility. The **Coverage / next checks** tab groups observed MMC navigation, source provenance, value verification, unit mappings and review eligibility into counted statuses with specific next diagnostic checks.
6. For a uniquely correlated Administrative Templates DWORD element that the native MMC displays as exact English `N hours`, choose **Review minutes / hours...**. The dialog re-reads current ADMX + ADML bytes, shows their SHA-256 fingerprints and exact element, requires independent human verification of the stored minutes and displayed hours, a deliberate checkbox, review reference and confirmation. Merely locating an ADMX decimal element never authorizes conversion.
7. **Export review TSV...** inside the window re-reads Registry.pol on the session-pinned controller, checks the exact GPO/UNC path, DWORD target, type, stored value and SHA-256 against the selected snapshot, then revalidates the ADMX/ADML fingerprints before exporting any session-approved conversions. A changed/partial/unreadable source aborts the reviewed export. Approvals are discarded when the window closes. The original **Export discovery evidence...** remains a separate read-only, no-approval export.

The original plain discovery export uses only a previously captured snapshot for the selected GPO, domain and DC; it does not trigger a fresh SYSVOL scan. Only complete, SHA-256-verified Registry.pol file reads participate in literal comparisons. The **reviewed** export actively re-reads only Machine/User Registry.pol on the pinned DC when session approvals exist; it deliberately does not scan unrelated GPP, security-template or audit files during unit review. Its validation is not an atomic transaction with GPMC/MMC and cannot attest which DC the separate MMC process contacted. Exported rows may contain confidential policy values; the GUI requires explicit confirmation before saving. Do not publish or upload the TSV.

## Redacted coverage diagnostics

The dedicated **Coverage / next checks** tab summarizes status counts from the previously captured selected-GPO evidence (not all policies on the domain and not effective client RSoP). Each result includes a fixed explanation and an actionable next step. The separate **Copy redacted coverage** action copies only counts, allowlisted status codes and predefined diagnostic instructions. It never includes raw setting names, domain/GPO/DC identities, Registry.pol paths, stored values, SHA-256 hashes, GPMC XML or exception text. Unknown status strings are deliberately replaced with `OTHER_OR_UNRECOGNIZED` to prevent untrusted data leaking into the clipboard. The clipboard export independently revalidates every copied finding against its fixed status dictionary, so even a malformed coverage object cannot include arbitrary text. A separate source-freshness category distinguishes not rechecked, successful pinned-DC recheck and blocked/failed source recheck. This sanitized report is for troubleshooting only, not an attestation of compliance or source freshness. The ordinary and reviewed TSV exports are **not redacted** and must remain confidential.

## Interpretation

- SECTION_CANDIDATE: a section inferred from known extension/category rules; not proof of native MMC presence.
- MMC_ROW_OBSERVED: exactly one matching native row for the same GPO, scope, section and literal name, **with Exact MMC row candidate navigation**. A section match alone is insufficient.
- AMBIGUOUS: multiple candidate MMC rows; no exact target is asserted.
- UNMAPPED: no reliable section route.
- REGISTRY_SOURCE_LOCATED: one exact Registry.pol key/value target was found in the matching source snapshot.
- SOURCE_RECORD_LOCATED_VALUE_NOT_COMPARED: source value/type/hash are available, but MMC and source values have **not** been compared.
- MMC_OBSERVED_SOURCE_NOT_COMPARED: the MMC state/value was observed but has **not** been compared to the stored source.

The TSV includes raw source value, registry type and source-file SHA-256 when uniquely available. It also includes `SourceRecheckStatus`, `SourceRecheckedAtUtc`, `UnitMappingStatus`, `UnitMappingProvenance`, `UnitMappingDefinitionSha256`, and `UnitMappingReviewReference`. It is **not** an effective RSoP report. Limited `MATCH_LITERAL_REG_SZ` / `MISMATCH_LITERAL_REG_SZ` and `MATCH_LITERAL_INTEGER` / `MISMATCH_LITERAL_INTEGER` statuses compare only unambiguous literal source and MMC text under strict conditions. `MATCH_EXPLICIT_MINUTES_HOURS` / `MISMATCH_EXPLICIT_MINUTES_HOURS` require an explicitly proven `SourceUnit=minutes`; the plain export deliberately does **not** populate that field. Conversions in the dedicated GUI require an explicit operator approval and a successful source recheck; they never run automatically. These statuses do **not** verify effective RSoP. MMC text can be formatted in different units or localized; never infer a source unit from a policy name.

## Verified unit mapping (opt-in only)

- **NOT_EVALUATED**: no reviewed mapping was provided; an ordinary canonical-integer result, if present, is a *literal* comparison only, not proof of equivalent policy semantics or units.
- **VERIFIED_EXPLICIT**: one exact GPO, scope, Registry.pol target, policy identity SHA-256, source-file SHA-256 and reviewed ADMX/ADML evidence fingerprint were supplied together with a nonempty review reference and confirmation of the native MMC unit. Only this state may enable the explicitly supported DWORD minutes-to-hours conversion.
- **AMBIGUOUS_RULES**: multiple declarations for the same target, including invalid or unsupported ones. No conversion and no numeric fallback.
- **UNVERIFIED_RULE**: the review is missing, the policy/source fingerprint is stale, or a required evidence field is missing. No numeric fallback.
- **SOURCE_NOT_VERIFIED** or **UNSUPPORTED_UNIT**: the source isn't a hashed DWORD Registry.pol record or the unit pair is not allowlisted. No numeric fallback.

A label, explainText, name containing "minutes", decimal min/max, or a nonempty provenance string **does not** establish units. The mapping API requires trusted manual review; its fingerprints are audit anchors, not independent semantic proof. The dedicated Discovery & Verification window now allows a **temporary per-session** approval after the operator independently confirms the exact ADMX/ADML decimal element's stored unit and the exact native MMC display unit. The window does not automatically extract or guess units from ADMX names, labels, explain text or number ranges. Exact ADMX verification requires the **complete** MMC Administrative Templates category chain; sharing only a final folder name is not accepted. It checks policy identity, a **single decimal element in the entire matching policy** (a policy-level MMC row cannot disambiguate multiple numeric fields), the exact registry target, a source-file SHA-256, the ADMX + ADML file contents and the selected MMC row before offering that review. Duplicate GPMC identity records are reported as AMBIGUOUS and cannot be reviewed. The legacy export contains no reviewed mappings, and opening a window does not approve anything. Recheck evidence when the GPO, ADMX/ADML or source snapshot changes. The GUI now performs a fresh **read-only** Registry.pol scan before review, after the operator's confirmation, and immediately before a reviewed export. It rejects wrong-DC/wrong-GPO, different-path, duplicate, partial, stale-hash or changed-value evidence. The file recheck is deliberately not an atomic snapshot of GPMC/MMC, and no effective RSoP is inferred. Directly setting `SourceUnit` cannot authorize conversion. Do not present literal-integer agreement as equivalent units.

## Remaining release blockers

- Source-specific, type-aware and unit-aware value comparison (including Kerberos/SecEdit and GPP sources).
- Verify live recapture and source-provenance behavior against representative nonproduction DCs; the read-only Discovery & Verification window performs pinned-DC source rereads and approvals expire at close.
- Verification on representative domain policies and supported Windows MMC versions.
- Final version bump, release notes and x64/ARM64 portable release validation.

Until these are completed, this is a preview capability on a development branch, **not a completed 2.1.0 release**.


### Preview 12: safe source-first navigation and Advanced Audit

**Exact MMC Paths** now shows **Source file / registry path** and **Copy source / registry path**. A GPO-relative Registry.pol, GptTmpl.inf, audit.csv or GPP Registry.xml path is a source-family hint, not a proof that the file exists. An exact registry target appears only when GPMC provides typed evidence. Copy omits raw source/value data and GPP value payloads; GPO names, settings and registry paths remain potentially confidential clipboard content.

**Open GPO editor (manual)** launches the selected GPO at its root and never starts a native MMC inventory. Exact MMC row navigation now requires an existing, non-aborted, same-GPO and same-pinned-DC native snapshot; a single setting must not launch a broad inventory through unrelated or crash-prone snap-ins. A separately initiated one-GPO read-only MMC inventory is available through MMC Full Settings Inventory but requires nonproduction smoke testing.

**Check Advanced Audit audit.csv...** is read-only, selected Computer GPO only, and reads exactly the pinned-DC `Machine/Microsoft/Windows NT/Audit/audit.csv` via an audit-only source scan. It correlates a unique typed GPMC `PolicyTarget=System`, `SubcategoryGuid`, canonical numeric `SettingValue` 0-4 with one exact SHA-256-bound CSV `Policy Target=System` subcategory GUID/value. Missing or incomplete files, duplicate identities, invalid/unsupported literals, changed scope/DC/GPO and per-user SID targets fail closed. Matching captured SAVED sources does not verify native MMC, effective client auditpol, cross-GPO precedence, replication or RSoP.

Reference: [Microsoft MS-GPAC System audit SettingValue](https://learn.microsoft.com/en-us/openspecs/windows_protocols/ms-gpac/21ad2e80-3011-48ef-be13-cc11ff7bfeb1) and [System audit.csv example](https://learn.microsoft.com/en-us/openspecs/windows_protocols/ms-gpac/f2b42f67-98d0-4900-8b66-e9e7db31698a).

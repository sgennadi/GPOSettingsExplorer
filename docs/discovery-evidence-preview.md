# Discovery evidence (2.1 development)

This feature is read-only and does not change Group Policy or SYSVOL. Optional startup **GPMC indexing** uses a per-user preference and the incremental cache; it does not start the native MMC editor for all GPOs. MMC observations, source values and RSoP have distinct statuses; no bulk scanner can truthfully set every Windows policy to VERIFIED without type-specific evidence.

## Exact MMC paths for the complete GPMC index

The **Exact MMC Paths** window lists every indexed GPMC row, whether or not that GPO has been scanned. The candidate section and the exact observed native row have separate fields; a known parent section never means that a specific policy's properties or value is verified. The operator can request a bounded, cancellable read-only MMC scan of only the selected GPO. Exact GPO/scope/native paths are matched by source-aware types, and an unhandled source can receive a one-time, explicitly reviewed native route after revalidation in a newly opened editor. No automatic cross-GPO path inference, no blanket mass MMC scans, no persistent secret-bearing route cache, and no GPO writes.

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

# Discovery evidence (2.1 development)

This feature is read-only and does not change Group Policy or SYSVOL.

## Using the current preview

1. Connect to the domain and load the GPMC settings index.
2. Open **MMC Full Settings Inventory** and select a specific GPO.
3. Run **Scan MMC (read-only)** to capture exact native rows for the selected GPO (incomplete sections remain explicitly marked).
4. In **All Settings**, load stored GPO source files on the same selected GPO and pinned domain controller, using **Read stored sources**.
5. Select **Discovery & Verification...** in the MMC Full Settings Inventory toolbar. Inspect the searchable verification grid, the selected setting's source/MMC/hash diagnostics and review eligibility.
6. For a uniquely correlated Administrative Templates DWORD element that the native MMC displays as exact English `N hours`, choose **Review minutes / hours...**. The dialog re-reads current ADMX + ADML bytes, shows their SHA-256 fingerprints and exact element, requires independent human verification of the stored minutes and displayed hours, a deliberate checkbox, review reference and confirmation. Merely locating an ADMX decimal element never authorizes conversion.
7. **Export review TSV...** inside the window revalidates the ADMX/ADML fingerprints and exports session-approved conversions, review references, policy/source hashes and all current statuses. Approvals are discarded when the window closes. The original **Export discovery evidence...** remains a separate read-only, no-approval export.

The export uses only a previously captured source snapshot for the exact selected GPO, domain, and pinned domain controller. It does not trigger a fresh SYSVOL scan. Exported rows may contain confidential policy values; the GUI requires explicit confirmation before saving. Do not publish or upload the TSV.

## Interpretation

- SECTION_CANDIDATE: a section inferred from known extension/category rules; not proof of native MMC presence.
- MMC_ROW_OBSERVED: exactly one matching native row for the same GPO, scope, section and literal name.
- AMBIGUOUS: multiple candidate MMC rows; no exact target is asserted.
- UNMAPPED: no reliable section route.
- REGISTRY_SOURCE_LOCATED: one exact Registry.pol key/value target was found in the matching source snapshot.
- SOURCE_RECORD_LOCATED_VALUE_NOT_COMPARED: source value/type/hash are available, but MMC and source values have **not** been compared.
- MMC_OBSERVED_SOURCE_NOT_COMPARED: the MMC state/value was observed but has **not** been compared to the stored source.

The TSV includes raw source value, registry type and source-file SHA-256 when uniquely available. It also includes `UnitMappingStatus`, `UnitMappingProvenance`, `UnitMappingDefinitionSha256`, and `UnitMappingReviewReference`. It is **not** an effective RSoP report. Limited `MATCH_LITERAL_REG_SZ` / `MISMATCH_LITERAL_REG_SZ` and `MATCH_LITERAL_INTEGER` / `MISMATCH_LITERAL_INTEGER` statuses compare only unambiguous literal source and MMC text under strict conditions. `MATCH_EXPLICIT_MINUTES_HOURS` / `MISMATCH_EXPLICIT_MINUTES_HOURS` require an explicitly proven `SourceUnit=minutes`; the current production export does **not** populate that field, so this conversion is test-only for now. These statuses do **not** verify effective RSoP. MMC text can be formatted in different units or localized; never infer a source unit from a policy name.

## Verified unit mapping (opt-in only)

- **NOT_EVALUATED**: no reviewed mapping was provided; an ordinary canonical-integer result, if present, is a *literal* comparison only, not proof of equivalent policy semantics or units.
- **VERIFIED_EXPLICIT**: one exact GPO, scope, Registry.pol target, policy identity SHA-256, source-file SHA-256 and reviewed ADMX/ADML evidence fingerprint were supplied together with a nonempty review reference and confirmation of the native MMC unit. Only this state may enable the explicitly supported DWORD minutes-to-hours conversion.
- **AMBIGUOUS_RULES**: multiple declarations for the same target, including invalid or unsupported ones. No conversion and no numeric fallback.
- **UNVERIFIED_RULE**: the review is missing, the policy/source fingerprint is stale, or a required evidence field is missing. No numeric fallback.
- **SOURCE_NOT_VERIFIED** or **UNSUPPORTED_UNIT**: the source isn't a hashed DWORD Registry.pol record or the unit pair is not allowlisted. No numeric fallback.

A label, explainText, name containing "minutes", decimal min/max, or a nonempty provenance string **does not** establish units. The mapping API requires trusted manual review; its fingerprints are audit anchors, not independent semantic proof. The dedicated Discovery & Verification window now allows a **temporary per-session** approval after the operator independently confirms the exact ADMX/ADML decimal element's stored unit and the exact native MMC display unit. The window does not automatically extract or guess units from ADMX names, labels, explain text or number ranges. It checks policy identity, one matching decimal registry element, a source-file SHA-256, the ADMX + ADML file contents and the selected MMC row before offering that review. The legacy export contains no reviewed mappings, and opening a window does not approve anything. Recheck evidence when the GPO, ADMX/ADML or source snapshot changes; this UI verifies cached source hashes against reviewed identities, **not** a fresh atomic DC source reread. Directly setting `SourceUnit` cannot authorize conversion. Do not present literal-integer agreement as equivalent units.

## Remaining release blockers

- Source-specific, type-aware and unit-aware value comparison (including Kerberos/SecEdit and GPP sources).
- Live source snapshot recapture and provenance sign-off on a nonproduction domain controller; the new Discovery & Verification window is read-only and approvals expire at close.
- Verification on representative domain policies and supported Windows MMC versions.
- Final version bump, release notes and x64/ARM64 portable release validation.

Until these are completed, this is a preview capability on a development branch, **not a completed 2.1.0 release**.

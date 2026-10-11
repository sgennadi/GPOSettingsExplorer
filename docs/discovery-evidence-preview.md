# Discovery evidence (2.1 development)

This feature is read-only and does not change Group Policy or SYSVOL.

## Using the current preview

1. Connect to the domain and load the GPMC settings index.
2. Open **MMC Full Settings Inventory** and select a specific GPO.
3. Optionally run **Scan MMC (read-only)** to capture native observed rows.
4. In **All Settings**, select the same GPO and load its stored source files if Registry.pol source correlation is needed.
5. Select **Export discovery evidence...** and save the TSV locally.

The export uses only a previously captured source snapshot for the exact selected GPO, domain, and pinned domain controller. It does not trigger a fresh SYSVOL scan. Exported rows may contain confidential policy values; do not publish them.

## Interpretation

- SECTION_CANDIDATE: a section inferred from known extension/category rules; not proof of native MMC presence.
- MMC_ROW_OBSERVED: exactly one matching native row for the same GPO, scope, section and literal name.
- AMBIGUOUS: multiple candidate MMC rows; no exact target is asserted.
- UNMAPPED: no reliable section route.
- REGISTRY_SOURCE_LOCATED: one exact Registry.pol key/value target was found in the matching source snapshot.
- SOURCE_RECORD_LOCATED_VALUE_NOT_COMPARED: source value/type/hash are available, but MMC and source values have **not** been compared.
- MMC_OBSERVED_SOURCE_NOT_COMPARED: the MMC state/value was observed but has **not** been compared to the stored source.

The TSV includes raw source value, registry type and source-file SHA-256 when uniquely available. It is **not** an effective RSoP report. No status indicates verified value equality. Values shown in MMC can be formatted in different units or localized; naive string equality would be unsafe.

## Remaining release blockers

- Source-specific, type-aware and unit-aware value comparison (including Kerberos/SecEdit and GPP sources).
- Discovery & Verification dedicated GUI with explicit coverage and mismatch details.
- Verification on representative domain policies and supported Windows MMC versions.
- Final version bump, release notes and x64/ARM64 portable release validation.

Until these are completed, this is a preview capability on a development branch, **not a completed 2.1.0 release**.

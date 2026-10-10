# Portable release integrity and optional signing

The GitHub Actions build creates Windows x64 and ARM64 standalone ZIPs, checks the C# build and core tests, then publishes a GitHub Release for a new version on main.

## Provenance attestations

Main branch pushes run a separate GitHub artifact provenance job using actions/attest@v4 and the two release ZIPs. Artifacts can be checked with the GitHub CLI:

    gh attestation verify ./GPOSettingsExplorer-win-x64-portable.zip --repo sgennadi/GPOSettingsExplorer
    gh attestation verify ./GPOSettingsExplorer-win-arm64-portable.zip --repo sgennadi/GPOSettingsExplorer

The job generates attestations for each release ZIP; this documents the source workflow and digest, not that the files are safe. Verify tags and provenance before executing privileged software.

## Optional Authenticode signing

Unsigned releases are explicitly allowed until an organization-owned, appropriate code-signing certificate is available. No certificate or private key is stored in source control.

To enable signing, set GitHub Actions repository secrets AUTHENTICODE_PFX_BASE64 (base64 of an exported PFX containing a permitted code-signing private key) and AUTHENTICODE_PFX_PASSWORD. On a main-branch release build, the Windows runner signs and verifies both executable architectures before ZIP creation. The PFX is held only in runner temporary storage and deleted in the cleanup block.

For production certificates, prefer an HSM or managed signing service over an exportable PFX. If using a managed signing provider, replace the optional PFX step with an audited OIDC / keyless signing action approved by your organization.

## Automated source security scanning

The repository also includes `.github/workflows/codeql.yml` running GitHub CodeQL C# security-extended queries on PRs, the main branch and weekly. Findings require review; passing CodeQL does not prove a release is safe.

## Guardrails

Neither software signing nor provenance attestations bypass Safe Mode, GPMC permissions, GPO backups, before/after previews, or the requirement for controlled nonproduction live-DC tests.

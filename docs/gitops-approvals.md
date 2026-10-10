# GPO Settings Explorer 1.6.0 - GitOps review and protected export

## Purpose and limits

This feature is **read-only with respect to the domain**. It creates local
fingerprints, diff requests, human approval receipts and confidential encrypted
evidence for offline inspection. It does not clone Git repos, push to GitHub,
merge pull requests, modify Active Directory, SYSVOL, policy links, security
filtering, WMI filters, Intune or client systems.

A signed request means *a holder of a particular certificate private key signed
these exact JSON bytes*. It does NOT authenticate the human or their Active
Directory authorization, verify CA trust/revocation, attest that an input HMAC
manifest came from an actual domain snapshot, or authorize a deployment. Handle
signing and deployment separation through the organization's change-control
policy, private Git repository, protected branches, trusted pin management,
auditing and an independent backup/preview of proposed live changes.

## Creating a fingerprint and review

1. Open **All Settings > Advanced analysis...** and select a specific GPO.
   Open a trusted offline GPMC backup with **Open GPMC backup...** or run
   **Capture current GPO** from the pinned DC. The resulting stored values are
   *not* effective client RSoP, and live observations are not atomic.
2. Open **GitOps & approvals > Save redacted manifest...** and save JSON to a
   controlled private local folder. The document contains HMAC-SHA256 of
   identities and stored values, not raw names, domain names, paths or values.
   The HMAC key is randomly generated and protected by Windows DPAPI CurrentUser.
3. After the change is made through existing authorized GPMC/editor workflows,
   capture the same GPO from the intended DC again. Choose **Compare saved
   baseline...** and select the earlier manifest. Review and save the redacted
   request JSON for a private Git PR. Different GPOs, domains, per-user keys or
   malformed/duplicate fingerprints are rejected.
4. The request records both snapshot times, SHA-256 of the exact baseline and
   candidate manifests, scope HMAC identifiers and change kinds. It does not
   describe plaintext setting changes. Use the application and GPMC backup/
   preview to understand actual changes before approval.
5. Partial, absent, masked/truncated or zero-row source evidence must not be
   approved. Unknown source families, ACL/link/WMI differences, concurrent
   updates, cross-DC health and resulting client RSoP are not resolved by HMAC
   diffs. A GitOps fingerprint alone never means the change is safe.

**Important:** Different Windows users/computers have different HMAC keys and
their exports cannot be diffed. Do not copy/share the DPAPI key to work around
this limitation. Run the comparison from the same protected Windows profile and
share only the redacted request for independent signature review.

## Signing and verifying a review

1. Reviewer loads the saved request with **Open review request...**.
2. On the reviewer's Windows machine, install a suitable RSA/ECDSA
   *digital-signature* certificate with a private key in **CurrentUser\My**.
   Use **List signing certificates** to obtain the complete SHA-256 of the
   certificate DER. The default Windows Certificate Manager thumbprint is
   commonly SHA-1 and is **not** the value this tool expects.
3. Choose **Approve / reject...**, explicitly decide, enter the full local
   SHA-256 certificate pin and save the signed decision receipt JSON. The
   private key remains in Windows or on its backing token/provider.
4. Independently establish the trusted reviewer's SHA-256 certificate pin
   through your PKI/IT security team **before** verifying. Never trust a pin
   copied only from the receipt being checked.
5. Load the exact original review request, choose
   **Verify signed decision...**, open the receipt and provide the independent
   SHA-256 pin. The app verifies request digest, certificate DER hash, validity
   period and RSA/ECDSA signature. It deliberately does **not** claim
   certificate chain/revocation, intended operator identity or AD RBAC.
6. Commit redacted JSON and public-certificate decision receipts only after
   local inspection to a *private, access-controlled* repository. Apply
   organization-specific branch protection, code-owner approval and protected
   deployment workflows. There is no automatic GPO writer/importer.

## Encrypted source data (NEVER for Git)

**Export protected source...** writes \`*.gposesdpapi\` using Windows DPAPI
CurrentUser. This contains raw stored policy records, including potentially
sensitive values, encrypted under that user profile. No plaintext staging
file is written, and the envelope exposes no GPO/domain identifiers.

**Check protected source...** requires the same Windows DPAPI protection
context and validates schema and source-content hash. It does not load the
decrypted source into the active GPO editor, nor restore any policy. A
CurrentUser DPAPI archive is not a portable backup; use the existing GPMC
backup workflow and your organization's recovery protections for disaster
recovery. Maintain access controls, disk encryption, safe backup custody and
local retention.

The repository .gitignore excludes confidential \`*.gposesdpapi\` files and
private signing-key formats. Ignore rules are not a data-loss-prevention
guarantee: inspect every staged file and diff before pushing.

## Risks and operational smoke test

GitOps HMAC manifests can be tampered with or forged before review; there is
no source attestation or independently trusted signer for the capture. Verify
the baseline and proposed source from a trusted pinned DC/GPMC backup and
preserve an audited independent approval trail.

On a **nonproduction** domain and Windows operator account, test:

- Complete same-GPO, same-key changed/unchanged snapshots; reject unrelated
  GPOs or HMAC keys.
- Missing/unreadable source files, masked values, and no-change candidates
  stay nonapprovable.
- Signed approval/rejection with a controlled CurrentUser signing cert;
  signature tampering, different request, expired cert and wrong independently
  trusted pin all fail.
- DPAPI export/import under the same account; different account/machine
  cannot decrypt without the original Windows profile's DPAPI context.
- Main UI at 100/125/150/175/200% DPI; every control remains reachable.
- No AD/SYSVOL writes or outbound Git, Graph, or AI network traffic occur.

Windows GitHub Actions build/core tests cannot replace these live operator
and organizational security checks.

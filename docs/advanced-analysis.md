# Advanced GPO Analysis Operator Guide

Version: 1.3.0 (feature foundation; read-only)

The Client & DC health tab includes a new **Explain why (full evidence)...** command. It combines pinned-DC domain/OU ancestry and link flags, current GPO scope state, logged single-client gpresult and recent client event metadata. It does not evaluate site links or full Security Filtering target rights. See the [Explain Why guide](explain-why.md).

Open the main application, select the GPO if you want live source evidence, then select **All Settings > Advanced analysis...**. For **offline use on a standalone PC**, click **Offline GPMC backup...** on the startup connection window or launch `GPOSettingsExplorer.exe --offline`. No AD connection, domain join, RSAT or GPMC component is needed for local backup parsing. Offline mode opens Advanced Analysis directly. The workspace never modifies GPOs, restores backups, merges policies, or uploads information without an explicit user-triggered operation.

## Offline GPMC backup

Choose the original **bkupInfo.xml** from a complete GPMC backup folder. The reader reads the matching backup GUID, domain and GPO GUID, then analyzes Machine/User Registry.pol, Machine SecEdit GptTmpl.inf, Machine Advanced Audit audit.csv and a curated set of standard GPP XML files from DomainSysvol/GPO. A missing or unreadable file is explicitly recorded. Unsupported CSE formats are not inferred to be Not Configured. Source content is read in a bounded manner.

To compare two backups, open the first backup and then select **Compare with backup...**. The comparison is limited to the same domain and GPO GUID; it shows only setting-identity changes and SHA-256 changes, not a list of plaintext credential values.

## Timeline

**Save DPAPI timeline** creates a SHA-256 fingerprint snapshot in the current user's LocalAppData. It retains up to 40 snapshots per GPO/domain and encrypts the JSON under current-user Windows DPAPI. Snapshots include policy identifier metadata and hashes, not full raw setting values. This is a local history feature, not a replacement for full GPMC Backup or a ready-made automatic restore system.

## Additional source namespaces

The direct `Registry.pol` reader labels known Firewall, AppLocker, Microsoft Defender, Windows Update and browser ADMX registry-policy branches to simplify search; the data remain raw stored values. The new bounded Advanced Audit Policy `Machine/Microsoft/Windows NT/Audit/audit.csv` parser reads individual audit subcategory GUIDs and configured CSV values, but does not emulate security CSE processing. Unknown formats and Item Level Targeting are not silently interpreted.

## Security triage

**Security source scan** checks nonempty legacy cpassword attributes in standard GPP XML and highlights suspicious script patterns. Neither the password attribute values nor full script commands are copied into the report. Findings can be administrative/benign. Human review and credential rotation are required when appropriate. This scanner does not decrypt GPP passwords or execute a script.

## Normalized baseline manifest

Choose a local JSON file using **Compare baseline JSON...**. The baseline file must use this exact schema (the following is illustrative, not an official Microsoft baseline):

    {
      "schema": "gposes-baseline-v1",
      "source": "Administrator-reviewed local rules",
      "version": "1.0",
      "rules": [
        {
          "id": "password-complexity",
          "scope": "Computer",
          "category": "Security template > System Access",
          "setting": "PasswordComplexity",
          "expected": "1",
          "registryKey": "",
          "registryValue": ""
        }
      ]
    }

A match indicates only that the selected stored policy source contains that exact value. It does not establish the effective policy of every computer or user. Unknown/missing evidence never counts as compliant. Import and normalize official Microsoft Security Baseline data under your organization's review rather than assuming any bundled generic rules are authoritative.

## Explicit Policy CSP / Intune mapping

The **Load reviewed CSP map...** action loads mapping files with this schema. URI values below are illustrative placeholders, NOT verified Microsoft mappings:

    {
      "schema": "gposes-csp-map-v1",
      "source": "Reviewed Microsoft Policy CSP mapping",
      "mappings": [
        {
          "scope": "Computer",
          "registryKey": "Software\\Policies\\Example",
          "registryValue": "Enabled",
          "omaUri": "./Device/Vendor/MSFT/Policy/Config/ADMX_Example/Sample",
          "notes": "Example only - not verified"
        }
      ]
    }

**Assess Intune readiness** produces candidate mappings ONLY for explicitly matched registry keys and values. The tool does not infer value encoding, scope/assignment, migration success, or whether the policy is available in your tenant.

## Optional Intune Graph inventory

Microsoft Graph connectivity is opt-in. Enter an Entra tenant GUID and existing public-client App Registration GUID and click **Graph Intune (read only)...**. Requires a suitable Intune license, an App Registration with device-code support and delegated DeviceManagementConfiguration.Read.All permission, and consent when required. Microsoft Authentication Library (MSAL) displays a one-time sign-in code. Tokens remain in the library's in-memory cache. The tool queries the first page of configuration policies from Microsoft Graph /beta, which may change in future. No Intune or Entra write permissions are requested.

## GitOps export

**Export GitOps fingerprints** shows portable JSON with **HMAC-SHA256** protected fingerprints of source setting identities and values. The 256-bit private HMAC key is randomly generated and stored under current-user Windows DPAPI at first export. The exported manifest includes no raw domain identifiers, passwords, registry values or script bodies, and cannot be brute-force reversed without the local key. This avoids guessing low-entropy policy values from ordinary SHA-256 alone. **Exports from other Windows users or computers will not be comparable** until a separate, carefully designed key-sharing workflow exists. It is a review artifact, NOT an executable GPO/Intune deployment specification. Never publish confidential raw evidence ZIPs publicly.

## Client RSoP / events and cross-DC SHA-256

**Explain logged GPO** uses the last observed gpresult sample for the named client/scope; no hypothetical future RSoP is computed. **Client GP events** calls the Windows built-in wevtutil.exe for the GroupPolicy Operational channel, collecting only event IDs, times and levels. Remote log queries require Event Log service, appropriate permissions and network firewall access. The DC fingerprint action checks a selected bounded set of source files on 1 to 16 explicit hostnames and distinguishes inaccessible/unreadable files from matching hashes. Matching hashes are not proof of full DFSR convergence.

## Optional local AI

A locally installed Ollama-compatible service must be listening on 127.0.0.1:11434, with the selected model installed ahead of time. **Local AI analysis...** requires a previous security scan plus a separate Yes confirmation. Only whitelisted finding categories and counts are sent to the local service. No domain names, GPO paths, SIDs, credentials, script content or raw values are included in the prompt. No cloud provider or Ollama install is bundled. Model responses are advisory and never trigger policy changes.

## Security and testing boundary

The normal GPO editing UI still starts in Safe Mode. These new Advanced Analysis actions are read-only even if the user enables writes elsewhere. CI tests pure parsing and builds WPF for x64/ARM64, but does not validate GPMC COM operations, live Active Directory, remote client event access or DFSR behavior in a real production domain. Perform controlled nonproduction DC tests before trusting new features for sensitive operational decisions.


## Full SYSVOL tree verification (v1.4.0)

In Client & DC health, supply 2-16 explicitly named domain controllers
and choose Full SYSVOL tree SHA-256.... A bounded read-only scanner
inventories every accessible file within the selected GPO root on each
controller, compares relative paths, presence and streamed SHA-256 digests.

The scanner limits each DC to 4,000 directory entries, 30 directory
levels, 32 MiB per file and 256 MiB aggregate data. Reparse-point
directories and files are not traversed. Missing GPT.INI, unstable files,
permissions, unscanned directories and oversized files are
Unknown / Incomplete.

Only SMB file reads are requested. Identical snapshots are not proof
that DFSR or AD replication is fully healthy. If different hashes
are found, inspect replication and determine whether administrators
changed the policy during the scan.

## Compare against a reviewed GPMC reference baseline (v1.4.0)

Under Baseline / Intune / GitOps, capture the current GPO or open an
offline GPMC backup, then choose Compare reference GPMC baseline....
Select the bkupInfo.xml from a separately reviewed GPO baseline export.
The tool compares exactly supported stored source identities, values,
types and states. The baseline GPO GUID need not match the target GUID.

Matched/different/unknown/ambiguous are separate statuses.
This is not a Microsoft Security Compliance Toolkit importer or a
certified compliance score. Baseline files must be independently
approved for the intended OS and product versions.

## No-follow security scan safety

Legacy GPP password and startup-script triage uses bounded enumeration
that does not descend into reparse points, and marks skipped/inaccessible/
oversized files as unverified. Unknown coverage does not mean no risk.


## Microsoft Graph paginated inventory and CSP mapping (v1.5.0)

The Optional cloud & local AI tab offers Graph Intune inventory (up to 500)....
It uses explicitly approved MSAL device-code authorization and a delegated
read-only scope. It then reads at most ten pages / 500 policy metadata
records from the global Microsoft Graph beta configurationPolicies endpoint,
with a two MiB cap on every response.

Next-page links MUST remain on HTTPS graph.microsoft.com using the exact
beta/deviceManagement/configurationPolicies path. Redirects are never
followed, repeated links fail closed, and off-host destinations are rejected
before attaching the access token. Incomplete collection is marked visibly.
This version does not retrieve setting-value payloads or device assignment
objects, and cannot write Microsoft Graph or Intune configurations.

Reviewed local CSP mapping input requires canonical, scope-matched
Device/User Policy CSP URIs. Matching requires a complete ordinary
Registry.pol source value and exact registry key/value identity.
Truncated binary previews, masked preferences and non-registry CSE records
cannot be claimed equivalent. A candidate is NOT evidence of an applicable
Settings Catalog setting, identical datatype, target assignment or
successful migration.

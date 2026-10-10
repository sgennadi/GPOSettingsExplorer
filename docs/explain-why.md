# Explain Why GPO / client diagnosis

Release: v1.3.0, read-only.

## How to run

In the connected application select a GPO, open All Settings > Advanced analysis... > Client & DC health, enter a computer name from the connected AD domain and select Computer or User scope. For User scope, specify the target DOMAIN\user identity. Choose **Explain why (full evidence)...**. No agent or PowerShell is required.

The report combines three independently collected evidence streams: a logged gpresult sample for the specific client/scope, the current computer AD location and the GPO links on its OU/domain ancestor path from the session-pinned DC, and recent GroupPolicy/Operational event metadata collected through Windows wevtutil. Failures in any one stream are reported as Unknown; other evidence remains visible.

## Evidence boundaries

A recorded gpresult state is the result of the last observed processing, not a simulation of hypothetical future policy changes. An enabled direct/ancestor path link is a **candidate**, not proof the GPO applied. An ancestor link without enforced is blocked by Block Inheritance on a child container. Direct links on that child container remain eligible, and enforced ancestor links bypass blocked inheritance. Link order is meaningful only within the same AD container.

The AD path reader does NOT derive the applicable Active Directory site and does not evaluate site-level links. It also does not establish target Read+Apply security filtering, nested security-group membership, explicit deny rights, SYSVOL NTFS access, or client-specific WMI results. For User policy the user's account OU and loopback Merge/Replace remain unknown.

Event IDs 4016 and 5016 describe Client Side Extension processing. The summary reports counts and severity but **does not** attribute these events to the selected GPO or to the same processing ActivityID. View the original event in Event Viewer if that correlation is required.

## Permissions and communication

- Connected session pinned to the selected domain/DC, with LDAP read permission on the computer object and domain/OU parent chain.
- For logged remote gpresult, appropriate remote administration/RSoP permissions and a valid logged-on user context where needed.
- For remote events, a running Windows Event Log service, appropriate log read permission, and inbound Remote Event Log Management firewall rules. No port bypass or automatic remote service changes are attempted.
- A failed/ambiguous computer lookup or malformed gPLink is a diagnostic failure, never evidence that a GPO is blocked.

## Safety

This workflow is explicitly read-only. It never runs gpupdate, changes a GPO, edits links/ACLs/WMI, attempts DFSR repairs, clears client caches or initiates policy resets. The report may contain real domain and computer distinguished names; treat exported reports as confidential. GitHub Windows CI tests parser/evaluator behavior but cannot validate your live domain and remote client authorization.

# GPO Settings Explorer 2.0 - Unified read-only platform and optional local AI

Version 2.0 brings independent source, security, pinned-DC health, links,
Intune mapping and the version-1.6 GitOps review tools into one WPF
operator workspace. This is an **analysis and review platform**, not a
GPO deployment engine, compliance certifier or endpoint RSoP simulator.

## Entry points

- On a connected Windows workstation or DC, select a GPO and open
  **Unified platform / Advanced analysis...** under All Settings.
- For disconnected inspection, select **Offline GPMC backup...** on
  the startup connection screen (or use \`--offline\`).
- The first Advanced Analysis tab is **Unified overview**. The older
  sources, health, baseline/Intune, GitOps approval and optional Graph/AI
  tabs remain accessible.

## Unified overview operator workflow

1. **Read current GPO source** (one selected GPO on session-pinned DC)
   or **Open offline GPMC backup...**.
2. Optionally **Scan source security**. GPP password attributes,
   unreadable GPP XML and potentially dangerous script patterns are
   reported as heuristic findings. No script is executed.
3. For a **live, session-pinned** source only, explicitly choose
   **Check pinned DC health...** and **Inspect direct GPO links...**.
   This reads AD/SYSVOL and enumerates direct links/OU targets. A
   downloaded or offline source cannot be mixed into a live-domain
   observation, and cross-GPO/domain/DC evidence is rejected.
4. Optionally load an administrator-reviewed Policy CSP map on
   **Baseline / Intune**. Candidates represent only exact
   Registry.pol/CSP mapping evidence; conversion or assignment is not
   performed.
5. Choose **Build unified overview**. Counts summarize Computer/User,
   file coverage, masked values, observed security findings,
   AD/SYSVOL errors, direct links and explicit CSP candidates.
   Missing evidence appears as **NOT CHECKED / INCOMPLETE**, never PASS.
6. For source fingerprints, signed reviewer approvals and confidential
   DPAPI source files, use **GitOps & approvals**. Review decisions never
   initiate policy writes.

No scan is performed in the background. The platform invalidates earlier
security/health/link/AI review state whenever the source changes.

## Optional local AI

After building the unified overview, choose **Optional local AI triage...**.
The tool shows a separate **consent dialog** before every request.
The model name can be changed on the **Optional cloud & local AI** tab.

A local inference server, such as Ollama, must be installed and an
appropriate model pulled **manually**. Default request target:
\`http://127.0.0.1:11434/api/generate\` on the same Windows machine.
The application will never download models or contact cloud AI endpoints.
Its HTTP client disables system proxies, HTTP redirects and cookies, and
caps response size to 256 KiB with cancellation.

The prompt is reconstructed from a fixed schema of numeric and boolean
aggregate fields only. It excludes raw source values, names of policies,
domains, DCs, users, computers, SIDs, OU paths, GPP XML, scripts, event
bodies, membership lists and credentials. Never expose the encrypted
\`*.gposesdpapi\` files to AI. The model response is an **unverified
human-readable suggestion**, not a policy rule or an executable operation.

When Ollama is absent or no model is installed, the rest of the
application functions without it.

## What 2.0 does NOT claim

- A complete inventory of every Group Policy client-side extension.
- Actual applied/effective client Group Policy (RSoP) for all devices.
- Correct future processing of WMI filters, security ACL/group token,
  links, loopback, site membership or CSE behavior.
- Synchronization of all domain controllers or enterprise compliance.
- Automatic GPO merge, push-to-GitHub, Intune migration or AD/SYSVOL
  changes. Signed GitOps receipts from 1.6 are non-deploying.
- Cloud AI or any need to send confidential domain data outside the device.

## Validation

Windows GitHub Actions performs .NET 10 build, UI style lint, regression
tests and portable Win-x64/ARM64 packaging. Run further manual verification
in a **nonproduction** Active Directory environment:

- Live single-DC GPO and a different offline backup never combine
  identities. GPO/Domain/DC mismatches are explicitly rejected.
- Unreadable or missing data remains Unknown; no automatic green status.
- Confirm the AI service cannot follow a remote redirect or proxy and
  transmits only bounded aggregate counters to loopback.
- Disconnect the network and stop Ollama; ordinary offline features
  must remain usable.
- Test window sizes, scrolling and controls on 100/125/150/175/200%
  scaling, x64 and ARM64 Windows.
- Confirm repeated operator actions never write AD/SYSVOL or execute
  scripts, and that no Git push/Graph/AI request is implicit.

See [GitOps and approval workflow](gitops-approvals.md) for private
redacted artifacts and CurrentUser-DPAPI source archive limitations.

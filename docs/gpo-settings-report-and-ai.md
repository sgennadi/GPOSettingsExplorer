# GPO Settings Explorer 2.0.1 - GPO settings view and remote AI

## GPOs: native report view

1. Connect to the intended domain controller using the existing Connection dialog.
2. Open the **GPOs** tab.
3. Right-click an actual GPO row and choose **Settings (GPMC-style report)...**. The row under the pointer is selected first.
4. Or select a GPO and click **Settings report...** on the toolbar.
5. The read-only tree shows General, Links (from GPMC XML), WMI Filtering, security/delegation evidence, Computer Configuration, User Configuration, and available policy/Preferences extensions. Expand a section to inspect its stored value in the details panel.
6. **Open GPO editor...** separately launches native Group Policy Management Editor. That native editor may allow edits if the account has permissions; the report window never modifies policies.

**Scope:** GPMC XML is a stored-policy report from the connected/pinned DC, not a proof of domain-wide replication or actual client RSoP. Security Filtering and Delegation are observational and cannot substitute for an ACL/token/permissions evaluation. A missing source family is UNKNOWN, not necessarily Not Configured. GPMC/RSAT components must be available on the workstation. No HTML browser, scripts, or external URLs are used. Sensitive source fields are redacted where detected and the rest of the report must still be treated as confidential.

## Kerberos Policy route

The following `SecuritySettings` / `Account` XML identifiers are handled as Kerberos Policy, not as registry settings:

| GPMC internal ID | Native MMC policy label |
| --- | --- |
| MaxServiceAge | Maximum lifetime for service ticket |
| MaxTicketAge | Maximum lifetime for user ticket |
| MaxRenewAge | Maximum lifetime for user ticket renewal |
| MaxClockSkew | Maximum tolerance for computer clock synchronization |
| TicketValidateClient | Enforce user logon restrictions |

Canonical route:

Computer Configuration > Policies > Windows Settings > Security Settings > Account Policies > Kerberos Policy

The program opens the related MMC section. It does **not** assume a specific registry value or open an unrelated exact policy row based on fuzzy aliases. Windows policy labels and values can vary by Windows language and OS/GPMC version. Verify the target visually before editing.

## Optional remote AI

Local Ollama and Microsoft Graph inventory remain independent features. Remote AI is disabled by default.

1. In **Advanced Analysis**, capture a selected GPO or open a valid GPMC backup.
2. Run the relevant health checks and **Build unified overview**.
3. Open **Remote AI (opt-in)**.
4. Check the one-window **Enable REMOTE AI** box.
5. Select either **OpenAI** or **Azure OpenAI**, enter the model/deployment identifier, and enter a key in the ephemeral PasswordBox.
6. For Azure OpenAI, enter only your short resource name. The app constructs the official `https://{resource}.openai.azure.com/openai/v1/chat/completions` endpoint.
7. Click **Analyze anonymized summary...**, read the one-request disclosure prompt and confirm or cancel.

For OpenAI, the destination is `https://api.openai.com/v1/chat/completions`. Network requests require HTTPS and have no redirects, arbitrary hosts, proxies, automatic retries, background requests or auto-model downloads. The key is not stored. API cost and your provider's data-processing terms may apply. A corporate rule forbidding external AI use overrides local user consent.

The only outbound GPO content is the fixed `BuildRedactedUnifiedPrompt` output: source row counts, completeness flags, security finding counts, AD/SYSVOL and link-health counts, CSP mapping counts, and explicit UNKNOWN status. **No raw XML, GPO name, domain, SID, registry value, paths, script, password, recovery key, or manually typed prompt is sent.**

Cloud-model answers are untrusted text. They cannot execute commands, edit GPOs, download updates or approve GitOps deployments. Validate every AI recommendation using Microsoft documentation, current GPMC evidence, real clients and representative RSoP.

## Validation checklist

- On a test DC, use a GPO with a startup BAT/PowerShell script and a populated Kerberos policy; compare the native report and route against GPMC.
- Right-click a GPO that is NOT currently selected. The viewed GPO must be the clicked row.
- Verify that `MaxTicketAge` reports **Maximum lifetime for user ticket**, not an invented Registry value or Security Options policy.
- Confirm remote AI does nothing merely by opening the tab and cannot send if the enable box or confirmation is denied.
- Use a test API subscription and ensure the outbound payload contains only numeric/boolean counters. Check that error logs and saved connection profiles contain no API key.
- Verify WPF text/scroll behavior on 100%, 125%, 150%, 175% and 200% scaling and x64/ARM64.
- No production GPO changes, restores or automatic policy pushes are part of this feature.

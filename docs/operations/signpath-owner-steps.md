# Owner steps remaining before SignPath submission

All values below refer to the successfully built unsigned **1.2.8** candidate,
not the older exact-machine 1.2.7 DEV MSI. No application has been sent.

## Exact source and artifacts

- Source commit: `82465b48d6137b4f81a57f33900f3da69c194658`.
- Preserved candidate branch: [`codex/signpath-candidate-1.2.8-r2`](https://github.com/Mina1399-web/PC-Agent/tree/codex/signpath-candidate-1.2.8-r2). Its SHA was independently checked after creation. It is a pinned preparation reference, not an enforced protected branch or a published release.
- Successful [build/test/candidate run 40243225067](https://github.com/Mina1399-web/PC-Agent/actions/runs/40243225067).
- [Download the candidate ZIP](https://github.com/Mina1399-web/PC-Agent/actions/runs/40243225067/artifacts/12618056110) while signed into GitHub. The artifact expires **2026-11-09 00:11 Prague time**. If expired, rebuild and use the new receipt; do not reuse old hashes for different bytes.
- ZIP `HostAgent-1.2.8-submission-preview.zip`, 322,802,124 bytes, SHA-256 `C68119D7F394174FBBB49AC915BCF2B287CABCF59CBF14FF0557321BDA51E033`.
- Inside the ZIP: `manifest.json` SHA-256 `5DCF79DC3284B4937EBA64D4F50A7831A1FFE4F0891AE4D4B190A500B136B25A`; `HostAgent.msi` SHA-256 `F3CAA8EBCD8EBE9F163FE2D2E196F83D5B948DEC93EC6BADC2BA0452EF0AA197`.

## 1. Source verification completed — no repeat needed

At exact source82465, the complete approved DEV VM run passed: Release build
0warnings/0errors, Core62/62, Infrastructure565/565 and Desktop15/15,
total642PASS/0failed/0skipped. The wrapper reported SOURCE_CHECKS_COMPLETED,
protected services unchanged and no service control/live-network opt-in.
See the [full receipts and prior failures](../verification/signpath-submission-2026-10-25.md#completed-full-dev-gate-at82465).
Do not repeat these runs unless source or unresolved evidence changes.
This gate does not establish native installation, live UI/database acceptance
or a signature.

## 2. Download, verify and read the exact candidate

Download the ZIP above to the DEV VM. Confirm its SHA-256 before extraction;
extract into a **new** directory without overwriting an older candidate.
Its root is flat: `HostAgent.msi`, `manifest.json`, `Service`, `Helper`, `Cli`,
`Desktop`, `docs`, `tools`, `LICENSE` and signing preparation files.

In non-elevated **PowerShell 7**, adjust only the new extraction path:

```powershell
$package = 'C:\Users\AgentPCdevtest\Documents\ChatGPT\DevTest VM PC\HostAgent-DEV-releases\SignPath-1.2.8-82465b4-candidate'
& (Join-Path $package 'tools\Read-SubmissionPlan.ps1') `
    -PackagePath $package `
    -ExpectedSourceCommit '82465b48d6137b4f81a57f33900f3da69c194658' `
    -ExpectedVersion '1.2.8' `
    -ExpectedManifestSha256 '5DCF79DC3284B4937EBA64D4F50A7831A1FFE4F0891AE4D4B190A500B136B25A'
```

This validates exact files/metadata/MSI tables and displays privacy and
installation changes without installing. This candidate directory and the
ZIP in the sibling `SignPath-1.2.8-82465b4-handoff` now actually exist.
Local guarded Read-SubmissionPlan passed all1,234 files/17 owned entries,
exit0/no watchdog intervention. Earlier resource refusals remain historical.
Do not substitute the preserved older34fe candidate or its hashes.

## 3. Owner-run native acceptance in the DEV VM

Depends on steps 1–2 passing. Create and host-verify a fresh rollback snapshot
of the current DEV VM, preserving `clean-win-base` and earlier snapshots.
Do not perform this installation on the physical production host.

Before installation also satisfy the transaction reserve:15GiB baseline plus
2×570,229,057 expanded package bytes plus117,670,676 MSI bytes, total at least
16.172GiB free. The last measured15.599GiB does not meet that reserve. Free
at least another0.573GiB in the DEV VM (prefer1GiB headroom) or extend its
storage through an owner-reviewed VM operation; do not delete protected data,
old evidence or snapshots and do not lower the guard. The read-only installed
probe and complete Czech commands are in the new source-external handoff.

Read [announced installation changes](submission-preview.md) and
[privacy](privacy.md), confirm existing Shadow/read-only and outbound choices,
and approve the exact candidate. Then use Windows Installer as administrator
inside the approved DEV VM, with `HOSTAGENT_INSTALL_ACK=YES` and `/norestart`.
The MSI itself refuses install/repair/upgrade without acknowledgement.
Keep normal Windows protections and rollback enabled; stop if policy blocks it.

Record MSI exit code (0 = completed, 3010 = completed but restart pending),
installed version 1.2.8, installed first-party hashes against the manifest,
both services' identity/state, `status --verbose`, and the rendered dashboard.
Shadow/read-only must remain enabled. Return failures as failures; do not
force a missing metric to look healthy. Old database integrity evidence stays
dated; a fresh check must be reported separately if performed.

## 4. Correct the public landing branch

At [repository settings](https://github.com/Mina1399-web/PC-Agent/settings),
change the default branch from stale `main` to
`codex/signpath-submission-prep`. This changes the landing reference only;
keep `main` and both histories intact. Verify the repository root displays
the current README, MIT license, privacy and Code signing policy links.
Browser/account confirmation must be performed by the owner: current tools
could not complete repository administration, and an existing-credential API
probe was rejected before execution by automatic policy.

## 5. Publish the reviewed unsigned preview

Depends on successful native acceptance and human review. In
[new GitHub Release](https://github.com/Mina1399-web/PC-Agent/releases/new),
create tag `v1.2.8-preview.1` targeting the preserved branch
`codex/signpath-candidate-1.2.8-r2` (verify SHA `82465b48...`), title
**Host Agent V1 1.2.8 unsigned preview**, and mark it **pre-release**.
Use the [prepared release description](../releases/1.2.8-preview.1.md), replace
its draft instructions with the actual native result, and use absolute links.
Upload the exact ZIP, MSI, manifest and a SHA-256 sums file; verify the
downloaded published bytes match the receipt. Do not publish a different DEV
MSI or claim a SignPath signature. The release/download page must describe
the actual functionality and limits. A source tag or temporary Actions
artifact alone does not satisfy the intended-form release gate.

## 6. Owner identity, account conditions and application review

Verify GitHub MFA and the actual author/reviewer/approver roles. SignPath MFA
and account roles must be verified when that account is available; do not
claim them verified beforehand. Review the
[application text](signpath-submission-draft-2026-10-25.md) and
[provider questions](signpath-provider-questions.md), including the new
project's reputation, DTF composite signing and conditional privacy UI.
These are provider decisions; no reputation, endorsement or approval is
invented by preparation.

Open [the official application](https://signpath.org/apply), enter your own
name/contact email directly, the public source and actual published release
links, and the signing-policy/privacy links. Review all presented terms,
personal-data consent and any CAPTCHA yourself. Keep credentials, production
secrets and telemetry out of the form and repository. **Stop before final
submission for the requested review**; no form has been submitted here.

After admission, [the inactive signing design](../signing/after-admission.md)
lists the real provider IDs, protected submitter secret, GitHub App scope,
manual approval, publisher pin and final-byte verification still required.
That later signing activation is not a completed pre-application result.

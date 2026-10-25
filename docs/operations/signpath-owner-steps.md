# Owner steps remaining before SignPath submission

All values below refer to the successfully built unsigned **1.2.8** candidate,
not the older exact-machine 1.2.7 DEV MSI. No application has been sent.

## Exact source and artifacts

- Source commit: `34fe3baf0b37b9cda1db1a2ac95c34f854efbaba`.
- Preserved candidate branch: [`codex/signpath-candidate-1.2.8`](https://github.com/Mina1399-web/PC-Agent/tree/codex/signpath-candidate-1.2.8). Its SHA was independently checked after creation. It is a pinned preparation reference, not an enforced protected branch or a published release.
- Successful [build/test/candidate run 40230180663](https://github.com/Mina1399-web/PC-Agent/actions/runs/40230180663).
- [Download the candidate ZIP](https://github.com/Mina1399-web/PC-Agent/actions/runs/40230180663/artifacts/12612639952) while signed into GitHub. The artifact expires **2026-11-08**. If expired, rebuild and use the new receipt; do not reuse old hashes for different bytes.
- ZIP `HostAgent-1.2.8-submission-preview.zip`, 322,777,962 bytes, SHA-256 `722CAA759CD0E249C8F27549849148F524F1248DF05939AEAA901B417E566E9A`.
- Inside the ZIP: `manifest.json` SHA-256 `444D696AC0C879A1446A8FC85D8C62DA6AD6D15878B1FED8FC1F63C360189E93`; `HostAgent.msi` SHA-256 `78241E9B8EE79C9B9CFFD89D4F1634616CF946BC18644010162B5750F29A2272`.

## 1. Finish the resource-blocked DEV source verification

Close unneeded Chrome windows in the DEV VM; one Chrome window was opened
during preparation and could not be controlled after browser automation
stopped. Aim for at least 2 GiB available RAM so compilation has headroom.
Actual guards remain initial >1,024 MiB, soft stop below 768 MiB, hard 512 MiB,
and 15 GiB free on C:. Do not lower them.

Use the supplied local `Run-RemainingDevSourceChecks.ps1` in a **non-elevated**
PowerShell. The real user-maintenance tests intentionally reject an
administrator token. With no `-Execute`, the script reports a read-only plan.
With `-Execute`, it performs Release build, then all Core, Infrastructure and
Desktop tests separately, without hosted filters or live-network opt-in.
It stops on the first failure and preserves logs. It performs no service
control, MSI installation, mode/configuration change or installed-product
database opening. Infrastructure tests use isolated SQLite fixtures.
Return the final JSON, each project's actual passed/failed/skipped summary
and the watchdog receipts. An interrupted run is not a successful suite.

## 2. Download, verify and read the exact candidate

Download the ZIP above to the DEV VM. Confirm its SHA-256 before extraction;
extract into a **new** directory without overwriting an older candidate.
Its root is flat: `HostAgent.msi`, `manifest.json`, `Service`, `Helper`, `Cli`,
`Desktop`, `docs`, `tools`, `LICENSE` and signing preparation files.

In non-elevated **PowerShell 7**, adjust only the new extraction path:

```powershell
$package = 'C:\path\to\new\extracted\candidate'
& (Join-Path $package 'tools\Read-SubmissionPlan.ps1') `
    -PackagePath $package `
    -ExpectedSourceCommit '34fe3baf0b37b9cda1db1a2ac95c34f854efbaba' `
    -ExpectedVersion '1.2.8' `
    -ExpectedManifestSha256 '444D696AC0C879A1446A8FC85D8C62DA6AD6D15878B1FED8FC1F63C360189E93'
```

This validates the exact files/metadata/MSI tables and displays privacy and
installation changes. It does not install. Local download/extraction was
refused by the resource guard during preparation, so no local candidate
directory or local rehash result is claimed.

## 3. Owner-run native acceptance in the DEV VM

Depends on steps 1–2 passing. Create and host-verify a fresh rollback snapshot
of the current DEV VM, preserving `clean-win-base` and earlier snapshots.
Do not perform this installation on the physical production host.

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
`codex/signpath-candidate-1.2.8` (verify SHA `34fe3baf...`), title
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

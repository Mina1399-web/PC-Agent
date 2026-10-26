# Remaining SignPath steps after unsigned preview publication

Updated 2026-10-26. **Prepared, not submitted; unsigned.**
This checklist supersedes earlier instructions to change the landing branch,
publish this preview, or repeat native installation as a publication prerequisite.
It does not claim full installed acceptance or SignPath eligibility.

## Completed preparation

- Default branch: `codex/signpath-submission-prep`. The root README exposes
  functionality/download, privacy and Code signing policy links. `main` and
  all pre-existing branch histories were retained without force-push.
- Public [Host Agent V1 1.2.8 unsigned preview](https://github.com/Mina1399-web/PC-Agent/releases/tag/v1.2.8-preview.1):
  pre-release tag `v1.2.8-preview.1` points exactly to
  `82465b48d6137b4f81a57f33900f3da69c194658`.
- Exactly four uploaded public assets: ZIP, MSI, manifest and SHA256SUMS.
  Anonymous downloads matched all approved SHA-256 values and byte lengths.
  See [downloads](download.md) and
  [publication verification](../verification/signpath-publication-2026-10-26.md).
- Successful [hosted run 40243225067](https://github.com/Mina1399-web/PC-Agent/actions/runs/40243225067)
  and prior complete DEV Release build: 0 warnings/errors, 642/642 tests.
  Hosted exclusions and the complete DEV run remain separately recorded in
  [candidate evidence](../verification/signpath-submission-2026-10-25.md).
  No build, test, installer or service operation was repeated for publication.
- The candidate's generated inventory, component mapping, exact WiX source
  archive and owned-file configuration were inspected read-only against its
  manifest. This is artifact evidence, not provider approval.

## Installed preview limitation remains explicit

Owner-supplied inspection reported version 1.2.8, 1,225 matching installed
files, zero unexpected files, preserved configuration and both services
Running during the preview probe. It returned
`INSTALL_ACCEPTANCE_INCOMPLETE`, with partial CPU/process telemetry caused by
denied access. Shadow/read-only remained active.

Owner-supplied offline-copy SQLite checks reported `quick_check=ok` for
`host-agent.db` and `notifications.db`; the absent helper journal remains
`NOTRUN`. A later owner-run recovery returned both services to Running.
No claim is made that every database exists, every metric works, or the
preview is approved for production. These limitations are published with
the release. Reinstallation is not required to repeat the publication checks.

## Checks that require the owner's authenticated accounts

1. Complete and verify two-factor authentication in [GitHub password and authentication settings](https://github.com/settings/security)
   and retain recovery information securely. The authenticated account check
   found enrollment is still needed before the SignPath MFA requirement can
   be considered satisfied. No authenticator or recovery method was added by
   this preparation.
2. Sign into the owner's actual SignPath account; verify MFA and actual
   organization/project membership. No provider membership, MFA or organization
   ID is inferred from GitHub ownership. These remain **NEOVĚŘENO**.
3. Confirm the intended author/reviewer/approver membership in
   [Code signing policy](code-signing-policy.md). The proposed single owner
   is `@Mina1399-web`; actual SignPath role assignments remain unverified.
4. Review the [prepared application facts](signpath-submission-draft-2026-10-25.md)
   and [provider questions](signpath-provider-questions.md). The owner must
   supply personal contact details directly, review the live fields and
   personally handle identity/authentication challenges and applicable consent.

The existing instruction remains: **do not fill or submit the SignPath
application or accept legal terms during this preparation**.
No application, provider message or signing request has been sent.

## Decisions only SignPath can provide

- Eligibility/reputation of this new project; no adoption or endorsement is claimed.
- Treatment of WiX DTF's composite custom action in a signed MSI.
- Whether the disclosed outbound opt-in and installation disclosure satisfy
  the conditional privacy-display requirement.

After admission, follow the [inactive signing design](../signing/after-admission.md).
Actual provider IDs, scoped integration, protected submitter credentials,
manual signing approval and independent publisher verification must exist
before activation. No placeholder credential or automatic signing was enabled.

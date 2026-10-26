# SignPath application package and remaining gates — 2026-10-26

**Status: public unsigned preview published; application not submitted.**
The earlier repository observations below describe the pre-publication
checkpoint, not the current GitHub state. The
[publication verification](../verification/signpath-publication-2026-10-26.md)
supersedes the landing/public-download blockers. Historical tests and bounded
installed results remain unchanged; SignPath eligibility/approval is not claimed.

## Freshly checked repository and candidate

- The approved DEV VM identity was verified: `AGENTPC`, VirtualBox UUID
  `46543236-8C5E-49DA-953C-DB17F9B1E402`, Guest Additions service Running,
  account `AgentPC\AgentPCdevtest`.
- Local checkout: `codex/signpath-submission-prep` at
  `9b3630530d5201b7b02c4ad9d7b195e4408720c2`. Existing unrelated local
  verification edits were preserved; this handoff does not stage them.
- Candidate source: `82465b48d6137b4f81a57f33900f3da69c194658`, branch
  `codex/signpath-candidate-1.2.8-r2`; successful GitHub Actions run
  [40243225067](https://github.com/Mina1399-web/PC-Agent/actions/runs/40243225067).
- The preserved local ZIP was freshly SHA-256 checked against its receipt:
  `C68119D7F394174FBBB49AC915BCF2B287CABCF59CBF14FF0557321BDA51E033`
  (322,802,124 bytes). The local MSI hash is
  `F3CAA8EBCD8EBE9F163FE2D2E196F83D5B948DEC93EC6BADC2BA0452EF0AA197`
  (117,670,676 bytes); `manifest.json` is
  `5DCF79DC3284B4937EBA64D4F50A7831A1FFE4F0891AE4D4B190A500B136B25A`.
- The matching workflow artifact is [12618056110](https://github.com/Mina1399-web/PC-Agent/actions/runs/40243225067/artifacts/12618056110), recorded to expire 2026-11-09 00:11 Europe/Prague. The preserved handoff copy and SHA-256 sums are in the DEV VM's source-external `HostAgent-DEV-releases/SignPath-1.2.8-82465b4-handoff` directory.
- GitHub API was freshly read: repository is public; `main` still points to
  stale commit `188e3e259519e8713cee7aed84fcec7e40aa408c`; there are **no
  published Releases**. The current landing README on `main` still describes
  Task 2 as incomplete. The reviewed candidate/preparation branches have the
  current project and Code signing policy links.

## Installation and runtime evidence

The owner supplied the 1.2.8 installed-preview JSON: version `1.2.8`, source
`82465b48...`, 1,225 installed files matched and zero unexpected files,
critical installed hashes matched, configuration remained preserved, and
both `HostAgent` and `HostAgent.Helper` remained Running during that probe.
The probe explicitly returned `INSTALL_ACCEPTANCE_INCOMPLETE`; it reported
Shadow/read-only active, partial `cpu-memory` and `process-services` health,
and left live dashboard, database integrity, and telemetry review as gates.

Subsequent owner-supplied results establish the following narrower facts:

- The dashboard screenshot showed Shadow/read-only and bounded partial metric
  reasons. Per-process CPU/executable-path access remains unresolved; no
  broadened permission or fabricated healthy value is claimed.
- Offline-copy SQLite checks returned `quick_check=ok` for `host-agent.db`
  and `notifications.db`. The helper journal database was absent and remains
  `NOTRUN`. The copy-based checks do not prove the absent database's health.
- The initial offline-copy operation left both services stopped. A later
  owner-run SCM recovery returned both services to Running; a subsequent
  owner confirmation said the dashboard refreshed in Shadow/read-only mode.
  No claim is made that the original integrity script itself restored them.

The full DEV Release run on the candidate source is recorded separately:
0 warnings/errors and 642/642 tests passed. The hosted candidate workflow
has a narrower documented test scope and exclusions. These are source/build
results, not a claim that every installed runtime metric is complete. See
[`signpath-submission-2026-10-25.md`](../verification/signpath-submission-2026-10-25.md)
and [the public publication record](../verification/signpath-publication-2026-10-26.md)
for the scoped evidence and limitations.

## Prepared project description for the application

> Host Agent V1 is a Windows-first local monitoring and incident-assistance
> application with a Czech desktop dashboard. It collects bounded system,
> CPU/memory, disk, process/service, network and event metadata, stores
> history locally, and presents evidence-based diagnoses and recommendations.
> Shadow/read-only is the default. Optional connectivity checks, Pushover
> notifications and Healthchecks-compatible heartbeat are disabled unless
> configured by the operator. Administrative actions are limited to typed
> allowlisted operations with policy checks and approval bound to the specific
> operation. The application does not expose a public listener, arbitrary
> command execution, vulnerability scanning, or security-policy bypass.

In the form, describe the project as a local diagnostics/maintenance tool;
do not call the remaining partial process metrics fixed. Link the privacy
disclosure, functionality/download page, MIT license, dependency/license
inventory and Code signing policy. State that the candidate is an unsigned
preview. Do not claim public downloads, reputation, signatures, provider
acceptance, or an established user base without new evidence.

## Completed public preparation

- The default branch is now `codex/signpath-submission-prep`; `main`, all
  other pre-existing branches and their histories were retained.
- [Host Agent V1 1.2.8 unsigned preview](https://github.com/Mina1399-web/PC-Agent/releases/tag/v1.2.8-preview.1)
  is published as a pre-release. Tag `v1.2.8-preview.1` resolves exactly to
  source `82465b48d6137b4f81a57f33900f3da69c194658`.
- Exactly four uploaded public assets were downloaded anonymously and
  matched their approved SHA-256 and sizes. The
  [download page](download.md) now exposes those assets and actual limitations.
- The root README, release and download pages link the Code signing policy
  and privacy disclosure. Public text retains unsigned status, partial
  telemetry, incomplete installed acceptance and absent helper-journal check.
- Documentation was synchronized after publication without modifying source
  code, workflows, packages, release tag or asset bytes. No builds/tests,
  installer or service operations were repeated.

## Remaining before application submission

See the current [owner checklist](signpath-owner-steps.md),
[public-safe application facts](signpath-submission-draft-2026-10-25.md) and
[provider questions](signpath-provider-questions.md).

GitHub owner/admin access is confirmed. Complete and verify GitHub
two-factor authentication before applying. SignPath MFA and actual provider membership remain
**NEOVĚŘENO**. Confirm actual author/reviewer/approver roles and owner contact
details directly in authenticated accounts. Only SignPath can decide
eligibility/reputation, DTF composite signing treatment and the conditional
privacy-display requirement.

The owner's instruction remains: do not fill or submit the SignPath
application and do not accept legal terms during this preparation.
No application, provider message or signing request has been sent.

## Current decision

Public landing and binary-download preparation are complete and verified.
The preview remains unsigned, restricted to approved DEV VM testing and
incomplete in installed acceptance. Account/identity checks and provider
decisions are still outstanding. This is not a claim of admission, signing,
production readiness or complete application acceptance.

## Official references

- [SignPath Foundation conditions](https://signpath.org/terms.html)
- [SignPath application](https://signpath.org/apply)
- [SignPath GitHub trusted-build requirements](https://docs.signpath.io/trusted-build-systems/github)
- [Code signing policy](code-signing-policy.md)
- [Privacy disclosure](privacy.md)
- [Functionality/download page](download.md)
- [Candidate and checksums](signpath-candidate-1.2.8-r2.md)
- [Owner operation checklist](signpath-owner-steps.md)

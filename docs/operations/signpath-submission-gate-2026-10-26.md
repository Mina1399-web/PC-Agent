# SignPath application package and remaining gates — 2026-10-26

**Status: prepared, not submitted.** This dated handoff supersedes only the
older “native acceptance pending” wording for the 1.2.8 candidate. It does not
alter historical test records or claim SignPath eligibility/approval.

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
and [`installed-readonly-diagnostics-2026-10-26.md`](../verification/installed-readonly-diagnostics-2026-10-26.md)
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

## Required actions before the application can be submitted

1. **Update GitHub's landing branch.** The owner must change the repository
   default branch from stale `main` to the reviewed current branch
   `codex/signpath-submission-prep` (or a reviewed successor). Keep `main`
   and all histories intact; do not force-push. Then confirm the root page
   presents the current README, Code signing policy, privacy and functionality
   links. The README should be refreshed after the release is published so it
   no longer says that no public release exists.
2. **Publish the exact unsigned preview.** Create pre-release tag
   `v1.2.8-preview.1` targeting the reviewed candidate source commit
   `82465b48d6137b4f81a57f33900f3da69c194658`. Use the exact verified ZIP,
   MSI, manifest and `SHA256SUMS.txt` from the preserved handoff; verify the
   published downloads against the hashes above. Do not substitute another
   DEV installer. Include the prepared functionality, privacy and install
   disclosure, limits, Code signing policy link and unsigned status in the
   release page. Replace “acceptance pending” in the release draft with the
   bounded owner-supplied results above.
3. **Verify the published state.** Re-read the public release and download
   links, asset sizes and SHA-256 values; confirm the homepage and release
   page both expose the Code signing policy and privacy link. SignPath's
   stated free-OSS conditions require that the project already be released
   in the form intended for signing and that functionality be documented on
   its download page.
4. **Complete account-owned checks.** The owner must verify MFA on GitHub and
   SignPath; confirm the actual author/reviewer/approver membership; personally
   review the form's live fields, terms, privacy consent and CAPTCHA. The
   proposed single owner is `@Mina1399-web`; this document does not assert
   current MFA or SignPath account membership.
5. **Answer provider questions honestly.** Disclose the young project's
   limited reputation and ask SignPath to assess eligibility. Ask for a
   decision on the WiX DTF custom-action composition and the conditional
   privacy-installation requirement. Do not claim that either is approved.

Only after those steps, open [SignPath's application page](https://signpath.org/apply),
fill in the owner's own contact details, public repository and actual release
URLs, project description, privacy and signing-policy URLs, and review the
provider's displayed consent. Stop at the final submit control for the
owner's last review. No application has been sent.

## Current decision

**Not ready to submit today:** the public release prerequisite is unmet and
the GitHub default landing page is stale. The candidate artifacts, source
references, product description, policy/privacy pages and prior test/install
evidence are prepared. New repository reputation is a provider decision and
cannot be guaranteed by this preparation.

## Official references

- [SignPath Foundation conditions](https://signpath.org/terms.html)
- [SignPath application](https://signpath.org/apply)
- [SignPath GitHub trusted-build requirements](https://docs.signpath.io/trusted-build-systems/github)
- [Code signing policy](code-signing-policy.md)
- [Privacy disclosure](privacy.md)
- [Functionality/download page](download.md)
- [Candidate and checksums](signpath-candidate-1.2.8-r2.md)
- [Owner operation checklist](signpath-owner-steps.md)

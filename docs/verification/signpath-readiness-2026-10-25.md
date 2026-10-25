# SignPath readiness checkpoint — 2026-10-25

This checkpoint covers the earlier binary-review preparation. Continue with
[the MSI submission preparation](signpath-submission-2026-10-25.md); results
below retain their original source/artifact scope.

This records the source, repository, workflow, and submission preparation
checks made on 2026-10-25. It does not claim SignPath eligibility, enrollment,
signing, or a public release. The historical preparation records remain
unchanged.

## Source and public repository

- Repository: `Mina1399-web/PC-Agent`, public.
- Current complete source used for this preparation: branch
  `codex/installed-telemetry-stabilization`, base commit
  `44bb6d102faaca24f9fb4e6199a60a941cfabbdb`.
- GitHub default branch `main` is stale: its README still describes Task 2 as
  incomplete; the root MIT license and current signing policy are absent.
- The complete source branch and `main` have no common Git ancestor. Do not
  force-push or discard either history. Keep `main` intact; a default-branch
  switch to a reviewed complete public branch is the simplest preservation
  path, subject to a fresh post-push review.
- The preparation working branch is based on the existing complete public
  branch, so it preserves its history. It does not contain VM settings,
  runtime caches, test databases, or secrets.

## Workflow alignment

The review-only GitHub workflow and builder are now pointed to product
version 1.2.7 and the preparation branch. Their scope remains:

- GitHub-hosted Windows runner only; exact public repository guard.
- Pinned GitHub Actions and SDK; read-only repository permission and no
  persisted checkout credentials.
- Release compilation and the existing Core/portable Windows integration
  test set. The hosted test filter excludes `ReleaseToolingTests`,
  `InstallerStagingTests`, `CompleteProbeGuardVmIntegrationTests`,
  `UserMaintenanceTests`, `HostAgentDataDirectoryTests`, and
  `WindowsCollectorIntegrationTests`. These exact-identity, installer,
  non-admin-token, ACL-owner, and Windows 11 service-inventory checks remain
  required in the complete approved DEV VM suite; the hosted run does not
  verify them.
- Unsigned binary-review archive only. No MSI, installation, certificate,
  signing token, repository write permission, or signing request is included.
- Artifact outputs are temporary GitHub Actions artifacts, not a public
  product release.

The first hosted run for commit `428da9367dc6ec358e70c3652c45025c9c47364f`
completed its Release build successfully, then failed its test step: 43
environment-bound tests failed and 460 passed. The hosted runner had executed
VM-only MSI/probe checks, tests requiring a normal non-administrator token, a
directory-owner assertion tied to that token, and Windows 11 service-inventory
assumptions. The unsigned review payload was skipped. Those tests were not
weakened; the hosted filter was narrowed to keep them in the complete DEV VM
suite.

A fresh run then completed successfully for commit
`a74f039781e1e587f4dde07d58450e0ad582858c`: Release build succeeded with 0
warnings and 0 errors; Core tests passed 62/62; filtered Infrastructure tests
passed 449/449. The 6 excluded classes listed above were not run in this hosted
workflow. The unsigned review artifact
[`HostAgent-1.2.7-unsigned-review-a74f039781e1e587f4dde07d58450e0ad582858c`](https://github.com/Mina1399-web/PC-Agent/actions/runs/40217705947/artifacts/12608288196)
was uploaded successfully (194,338,040 bytes; GitHub-reported ZIP SHA-256
`215c4a6b8e2cb9aa1a156e283b6c2456d4df76a59dce35c676a8ab59459c158f`; expires
2026-11-08). This is a temporary review artifact, not a public release or a
signed package. The test-results artifact was also uploaded (ID
`12607538901`; SHA-256
`d5bc148fdacafbd7cec21279e43b448d99cfc4ceb1d5041e1f202e3ee52de095`).

Local Release build and test execution remain pending because the current VM
has 14.89 GiB free on C:, below the documented 15 GiB floor. No guard was
lowered.

## Product and eligibility gates

- The installed 1.2.7 DEV candidate and its package checks are recorded in
  [final acceptance](final-acceptance-2026-10-25.md). This is DEV evidence,
  not a public release; live telemetry acceptance remains incomplete.
- Database evidence presently supports read-only `quick_check=ok` for
  `host-agent.db` and operator-reported `notifications.db`. The helper journal
  has not been created by a normal journal operation and remains unchecked.
- Process metrics still report partial values for some processes due Windows
  access-denied reads; no permission broadening was performed.
- Dependency review is historical and not exhaustive for the present payload.
  WiX DTF redistribution/source treatment and SignPath's treatment of its
  composite custom action remain unresolved.
- The native MSI does not display the privacy policy during installation.
  Current defaults disable active connectivity probes and require operator
  setup for optional outbound services, but provider acceptance is unknown.
- No public GitHub release was present in the current Releases API response.
  The project has no established public reputation claim, no verified
  SignPath project/policy/certificate setup, and no proven signed-artifact
  verification path.
- GitHub/SignPath MFA, actual role membership, final artifact configuration,
  and provider eligibility are unverified.

## Actions and scope

The public-safe application draft is in
[signpath-submission-draft-2026-10-25.md](../operations/signpath-submission-draft-2026-10-25.md).
It intentionally leaves personal contact information and legal consent to
the owner in the authenticated provider form.

No SignPath account, GitHub App, API token, certificate, payment, secret,
installer, service, policy, or live endpoint was changed in this preparation.
No application was submitted and no legal consent was accepted.

## Fresh checks in this preparation

- PowerShell parsed `tools/Build-Review.ps1` without parser errors.
- Static workflow contract assertions passed before the filter correction:
  selected preparation branch,
  version 1.2.7 in workflow and builder, pinned checkout/setup-dotnet/upload
  actions, read-only content permission, and no signing, install, or release
  builder step.
- Ran `tools/Build-Review.ps1` from the DEV VM with hosted-runner identity
  variables absent. It returned `review_hosted_runner_required` (exit 1) and
  created no output directory. This confirms local execution is denied; it is
  not a hosted build result.
- `git diff --check` passed.
- At the check, C: had 15,999,873,024 bytes free, below the repository's
  15 GiB build/test floor (16,106,127,360 bytes). Therefore no Release build,
  test suite, or package was run. No guard or threshold was changed.
- After the recorded hosted run, `git diff --check` passed and the preparation
  branch was clean at `a74f039781e1e587f4dde07d58450e0ad582858c`, matching
  `origin/codex/signpath-submission-prep`.
- This verification record was amended afterward to add the hosted CI result;
  that documentation-only amendment has not required another build. Its diff
  is checked separately before commit.

## Readiness

**Not ready to submit.** The public landing branch is stale, there is no
public release in the intended signing format, hosted-run evidence for the
updated workflow is pending, and privacy/dependency/provider/account gates
remain open. Continue only after the relevant evidence and owner-only account
steps are complete. Stop immediately before the application's final submit
action.

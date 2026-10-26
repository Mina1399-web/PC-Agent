# SignPath application facts after publication — 2026-10-26

**Prepared, not filled or submitted.** This public-safe document records the
published unsigned preview and bounded evidence. It contains no private contact
details, credentials, telemetry or claim of provider approval.

## Reusable public project facts

- **Project:** Host Agent V1.
- **Repository:** <https://github.com/Mina1399-web/PC-Agent>.
- **Public unsigned release:** <https://github.com/Mina1399-web/PC-Agent/releases/tag/v1.2.8-preview.1>.
- **Functionality and downloads:** <https://github.com/Mina1399-web/PC-Agent/blob/codex/signpath-submission-prep/docs/operations/download.md>.
- **Privacy disclosure:** <https://github.com/Mina1399-web/PC-Agent/blob/codex/signpath-submission-prep/docs/operations/privacy.md>.
- **Code signing policy:** <https://github.com/Mina1399-web/PC-Agent/blob/codex/signpath-submission-prep/docs/operations/code-signing-policy.md>.
- **License:** [MIT](../../LICENSE) for first-party source; retain each
  third-party license and notice in the [component mapping](../legal/distribution-components-2026-10-25.md).
- **Immutable candidate:** `82465b48d6137b4f81a57f33900f3da69c194658`,
  preserved by source branch `codex/signpath-candidate-1.2.8-r2` and
  release tag `v1.2.8-preview.1`. Documentation advances separately on
  the default branch `codex/signpath-submission-prep`.
- **Release state:** publicly published **pre-release**, unsigned, approved
  DEV VM testing only; no production approval, SignPath signature or sponsorship.

## Project description

Host Agent V1 is a Windows local monitoring and incident-assistance application
with a Czech desktop dashboard. It gathers bounded system, CPU/memory, disk,
process/service, network and Event Log metadata, stores history locally, and
presents evidence-based diagnoses and recommendations. Shadow/read-only is
the default. Optional connectivity probes, Pushover notifications and heartbeat
are disabled unless the operator configures and enables them. A separate local
helper supports typed allowlisted administrative actions behind policy checks
and operation-specific approval. There is no public listener, cloud command
channel, vulnerability scanner, arbitrary shell or security-policy bypass.

## Build, artifact and installed evidence

- Successful [GitHub-hosted candidate run 40243225067](https://github.com/Mina1399-web/PC-Agent/actions/runs/40243225067)
  at the exact candidate source. Actions use pinned revisions.
  Setup requests SDK 10.0.401; `global.json` allows `latestFeature`, so the
  successful hosted run selected SDK 10.0.402 / host runtime 10.0.14.
  Self-contained payload remains pinned to 10.0.12.
- The prior complete DEV Release build used SDK 10.0.401 / runtime 10.0.12,
  with 0 warnings/errors and 642/642 tests passed. Hosted tests have a narrower
  documented scope. No bit-for-bit reproducibility or exact 401 hosted
  execution is claimed. See [scoped build evidence](../verification/signpath-submission-2026-10-25.md).
- Prior local artifact-only validation covered 1,234 files and 17 owned signing
  entries. Publication separately verified all four public asset hashes and
  lengths by anonymous download. The generated dependency inventory and
  immutable WiX source archive match candidate-manifest entries.
  See [publication evidence](../verification/signpath-publication-2026-10-26.md).
- The owner-supplied installed probe confirmed preserved configuration,
  matching installed critical hashes and both services Running. It returned
  **INSTALL_ACCEPTANCE_INCOMPLETE** with partial CPU/process metrics due to
  access denied. Offline-copy SQLite checks passed for two named databases;
  the absent helper journal remains `NOTRUN`. Later owner-run recovery
  returned both services to Running. Full application acceptance is not claimed.
- The public ZIP, MSI and manifest are the approved bytes. No substitute
  package, new build or test run was produced during publication/documentation.

## Proposed signing scope and provider questions

Request signing only for exact Host Agent-owned executable paths, with enforced
product name/version metadata, and the MSI only if SignPath accepts its
composition. Do not sign upstream .NET, SQLite or WiX binaries as Host Agent.
The MSI's WiX DTF composite contains upstream code; its treatment remains a
provider decision. See [prepared questions](signpath-provider-questions.md)
and [inactive post-admission design](../signing/after-admission.md).

This is a new project with no established adoption, independent reputation,
endorsement or previous signed release to claim. The public preview is evidence
of release, not proof of eligibility. SignPath must assess reputation,
DTF composition and the conditional privacy-installation requirement.

## Remaining owner/provider requirements

- Complete and verify GitHub two-factor authentication before applying.
  SignPath MFA and actual organization/project membership remain
  **NEOVĚŘENO**; GitHub administrator access does not prove provider
  membership.
- The proposed maintainer/author/reviewer/approver is `@Mina1399-web`.
  Confirm actual provider roles and manual approval; no other team member is asserted.
- The owner must supply their personal name/contact email directly and review
  the live form, applicable legal/privacy consent and any identity challenge.
  This draft is not a complete verified inventory of authenticated form fields.
- Only the provider can accept the project and decide unresolved signing/privacy
  questions. No provider decision or account setup was fabricated.

**The application must not be filled, submitted or legally accepted by this
preparation.** No application or provider message has been sent.
No credential, API token, private key, VM export, operational log or database
belongs in the public repository or application attachment.

## References

- [Foundation conditions](https://signpath.org/terms.html)
- [Trusted GitHub build requirements](https://docs.signpath.io/trusted-build-systems/github)
- [Publication verification](../verification/signpath-publication-2026-10-26.md)
- [Remaining owner steps](signpath-owner-steps.md)

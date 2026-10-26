# Code signing policy

Status: **SignPath preparation only; not enrolled, sponsored or signed.**
The owner approved open-source publication and GitHub-hosted builds/tests
for this signing path. Production host security boundaries are unchanged.

## Roles and approval

Proposed maintainer, code-review owner and release-signing approver:
[@Mina1399-web](https://github.com/Mina1399-web). GitHub owner admin permission
was confirmed read-only. GitHub MFA could not be determined from the current
API response; it remains **NEOVĚŘENO**. SignPath MFA and actual membership also
remain unverified. Proposed roles are not claims of provider account assignment.
CODEOWNERS is a review routing hint, not an enforced protection rule.
AI assistance does not supply the owner's manual signing approval.

Every release requires a reviewed immutable source commit, successful
appropriate Release build/tests, a reviewed dependency/license inventory,
an artifact configuration limited to first-party files, and manual approval
by the authorized maintainer. Do not sign PR/fork artifacts or unreviewed
changes. No automatic signing or release upload is currently configured.

## Proposed provider and credit

The project plans to apply to [SignPath Foundation](https://signpath.org/).
Acceptance, certificate availability and eligibility are not guaranteed.
If approved and actually using the service, the project and release pages
must display the required credit:

> Free code signing provided by [SignPath.io](https://signpath.io/), certificate by [SignPath Foundation](https://signpath.org/).

This conditional statement does not claim an existing sponsorship.
SignPath Foundation would be the certificate holder/publisher; a GitHub
username is not the certificate subject or proof of publisher trust.

## Verification and boundaries

Use trusted RSA Authenticode signing accepted by the target's existing
policy. Verify the chain, timestamp and expected publisher; obtain the
publisher thumbprint through an independently trusted channel. Preserve
the current fail-closed exact publisher pin. Certificate rotation requires
an explicit reviewed update, not accepting whichever signer an artifact
contains. Never import an untrusted root, weaken CI/SAC or trust a
self-signed development certificate on the production host.

Hash final signed bytes only. A signed manifest must cover final payload,
MSI and documents. Verify exact membership, lengths and SHA-256, first-party
signatures, MSI payload/custom actions and third-party provenance before
distribution. Do not re-sign upstream runtime, SQLite or WiX binaries as
Host Agent source. The WiX DTF composite custom-action signing treatment
must be agreed with SignPath before implementation.

The existing builder reads a CurrentUser/My certificate and is VM-bound;
it is not a SignPath integration. A DEV package remains VM-only, unsigned
and unsuitable for production. The separately guarded GitHub-hosted unsigned
binary-review workflow for version 1.2.7 passed its Release build and declared hosted
test subset on 2026-10-25 at commit
`a74f039781e1e587f4dde07d58450e0ad582858c`. Its temporary review archive is
not a signed or supported production release. That binary-only artifact
contained no MSI and was not signed or installed. A later continuation prepares a separate
unsigned 1.2.8 MSI submission candidate with explicit installation disclosure
and acknowledgement. Its [public unsigned preview](https://github.com/Mina1399-web/PC-Agent/releases/tag/v1.2.8-preview.1)
has now been published and all four uploaded assets independently downloaded
and verified. Initial installed acceptance remains incomplete with partial
CPU/process telemetry. The helper-journal integrity check remains `NOTRUN`;
production approval and the provider's composite-action decision remain gates.
See [publication verification](../verification/signpath-publication-2026-10-26.md),
the
[current continuation](../verification/signpath-submission-2026-10-25.md)
and [earlier readiness record](../verification/signpath-readiness-2026-10-25.md)
for exact executed tests and limits. The original production path and DEV MSI
machine restriction remain intact. See the
[application draft](signpath-submission-draft-2026-10-25.md).

See [privacy](privacy.md), [dependency inventory](../legal/dependencies.md),
[current preparation and remaining gates](signpath-submission-draft-2026-10-25.md),
[SignPath terms](https://signpath.org/terms.html) and
[GitHub origin verification](https://docs.signpath.io/trusted-build-systems/github).

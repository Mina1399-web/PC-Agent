# Code signing policy

Status: **SignPath preparation only; not enrolled, sponsored or signed.**
The owner approved open-source publication and GitHub-hosted builds/tests
for this signing path. Production host security boundaries are unchanged.

## Roles and approval

Proposed maintainer, code-review owner and release-signing approver:
[@Mina1399-web](https://github.com/Mina1399-web). GitHub owner admin permission
was confirmed read-only. MFA, branch-protection and SignPath membership
settings still need verification.
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
and unsuitable for production. A cloud build path and its tests must be
implemented separately without weakening either release validation or
the DEV MSI machine restriction.

See [privacy](privacy.md), [dependency inventory](../legal/dependencies.md),
[preparation and remaining gates](signpath-preparation.md),
[SignPath terms](https://signpath.org/terms.html) and
[GitHub origin verification](https://docs.signpath.io/trusted-build-systems/github).

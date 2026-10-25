# Signing configuration prepared for after admission

No API credential, provider project, policy or GitHub App installation was
created by this preparation. The example beside this file is inactive and
deliberately stops before a request. It is a design aid, not a verified signer.
Actual signing follows admission and cannot be represented as already tested.

## Inputs the owner/provider must establish

| Name | Source and meaning |
| --- | --- |
| `SIGNPATH_ORGANIZATION_ID` | The actual organization ID assigned by SignPath. |
| `SIGNPATH_PROJECT_SLUG` | The actual project created for this repository. |
| `SIGNPATH_SIGNING_POLICY_SLUG` | A policy requiring manual approval by the designated approver. |
| `SIGNPATH_ARTIFACT_CONFIGURATION_SLUG` | Provider-reviewed exact owned-file configuration from `signing-input/owned-payload.xml`. |
| `SIGNPATH_PUBLISHER_THUMBPRINT` | Public certificate thumbprint verified independently with the provider; never adopt whichever signer a returned file happens to carry. |
| `SIGNPATH_API_TOKEN` | Submitter credential entered directly into protected GitHub Actions secrets, never into source, chat or logs. |

Verify actual GitHub/SignPath MFA, roles and least privilege. Configure the
GitHub environment `signpath-production` with an owner reviewer and restricted
release refs. Allow the SignPath GitHub App only the intended repository
scope required by the provider's trusted-build connector. These permission
and account actions are owner-run. Do not claim a routing CODEOWNERS file
enforces branch protection or substitutes for human approval.

## Artifact design

The unsigned candidate is a flat ZIP uploaded with pinned upload-artifact's
`archive: false`. Its generated configuration enumerates only exact Host
Agent-owned PE paths, including the owned managed `Preflight.dll`, with
`Host Agent V1` product-name and required version restrictions. It excludes
upstream runtime, SQLite, WiX/DTF files and the MSI. The original candidate
manifest becomes stale for any signed file; it must never be presented as a
final signed release manifest.

The DTF composite must follow the provider's decision from the
[prepared questions](../operations/signpath-provider-questions.md). Rebuild
the MSI from verified signed owned payload, retain upstream source/notices,
obtain the separately approved MSI signature, regenerate hashes from the
final bytes, sign the final manifest, and verify exact MSI/ZIP payload parity.
Preserve the existing independently pinned publisher gate and Windows trust.

## Evidence still required after admission

All jobs leading to signing must actually run on GitHub-hosted runners, and
the unsigned input must be stored as a GitHub artifact first. Provider origin
verification must use GitHub's connector metadata, not an environment string
or self-authored manifest alone. Each signing request requires human approval.

Then verify returned signatures, chain, timestamp, pinned publisher, exact
membership, final hashes and package metadata. Perform signed-package native
DEV acceptance and subsequent separately authorized target-policy acceptance
before production distribution. None of these post-admission runs was
performed by preparing this example.

Primary references: [trusted GitHub origin](https://docs.signpath.io/trusted-build-systems/github),
[artifact configuration](https://docs.signpath.io/artifact-configuration/examples),
[exact v3 action inputs](https://github.com/SignPath/github-action-submit-signing-request/blob/f6d04783b4569d051e0c80105fe66e82819d0092/action.yml).

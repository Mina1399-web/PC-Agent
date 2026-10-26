# SignPath application draft — updated 2026-10-26

**Not submitted.** This is a public-safe draft of product facts and current
gates. The authenticated form still needs the owner's contact details, MFA,
provider account setup, applicable consent, and an actual release before it
can be submitted. Do not infer approval or eligibility from this document.

## Form content draft

- **Project:** Host Agent V1
- **Source repository:** <https://github.com/Mina1399-web/PC-Agent>
- **Reviewed candidate source:**
  <https://github.com/Mina1399-web/PC-Agent/commit/82465b48d6137b4f81a57f33900f3da69c194658>;
  preserved as `codex/signpath-candidate-1.2.8-r2`. The owner must publish the
  corresponding binary release and correct the stale default landing branch
  before claiming those gates complete.
- **License:** MIT for first-party source; third-party components retain their
  respective licenses and notices.
- **Proposed project description:** Host Agent V1 is a Windows local monitoring
  and incident-assistance application. It gathers bounded system, resource,
  service, network and event metadata, stores diagnoses and incident history
  locally, and presents a local dashboard. Shadow/read-only is the default.
  Optional outbound notifications and heartbeat require operator configuration.
  A separate local helper supports a small typed allowlist of administrative
  actions behind policy checks and operation-specific approval.
  It has no public listener, cloud command channel, vulnerability scanner,
  arbitrary shell, or security-policy bypass.
- **Proposed code-signing format:** Windows x64 first-party executable files
  and, if the provider accepts the composition, the Host Agent MSI. Do not sign
  third-party binaries as Host Agent. The MSI includes WiX DTF custom-action
  components whose exact signing and redistribution treatment is unresolved.
- **Build system:** GitHub Actions on GitHub-hosted Windows runners with pinned
  action revisions. Setup requests SDK10.0.401; global.json permits latestFeature,
  so the latest successful run actually selected SDK10.0.402/host runtime10.0.14.
  The self-contained payload remains pinned to10.0.12. Separate full DEV VM
  verification used SDK10.0.401/runtime10.0.12. No bit-for-bit reproduction
  or exact401 hosted execution is claimed.
- **Fresh candidate evidence:** Run `40243225067` succeeded at source
  `82465b48d6137b4f81a57f33900f3da69c194658`. Artifact checks passed for
  1,234 files and17 owned signing entries. The complete DEV Release build
  passed0warnings/0errors and642/642tests; local artifact-only validation
  also passed. Native installation, live UI/database acceptance remain pending.
- **Reputation:** New project. Do not claim public adoption, endorsements,
  established reputation, or previous signed releases. Ask SignPath to assess
  eligibility under its reputation policy.
- **Maintainers and roles:** GitHub owner `@Mina1399-web` is the proposed
  maintainer/author/reviewer/approver. Confirm actual role assignments and MFA
  directly in GitHub and SignPath before submission. No other member is
  asserted here.
- **Public release/download:** No public MSI release is currently established.
  The [functionality/download page](download.md) and unsigned preview release
  text are prepared; native acceptance and actual public publication remain
  gates. The old 1.2.7 exact-machine DEV MSI must not be relabelled as the new
  preview. A temporary review artifact does not establish public release.

## Current blockers before the form is ready

1. Publish a reviewed, user-facing release in the exact format intended for
   signing, with a download page that describes the functionality. Do not use
   the machine-guarded DEV MSI as that release.
2. Promote a complete licensed source branch to GitHub's project landing page
   without rewriting either existing history. The public default `main` is
   stale; the histories do not share a merge base.
3. Keep the hosted candidate evidence attached to the reviewed source commit.
   The hosted filter leaves exact-identity, installer, non-admin ACL, and
   Windows 11 service-inventory tests in the complete DEV VM suite; these
   exclusions alone are not evidence that those suites passed. The separate
   complete VM642/642PASS at82465 is now recorded in the current continuation.
4. Verify the generated candidate inventory and included immutable WiX source
   against the [dated component mapping](../legal/distribution-components-2026-10-25.md).
   Do not sign upstream files as Host Agent; the composite action's treatment
   requires the provider's decision.
5. Present the [prepared provider questions](signpath-provider-questions.md),
   including DTF composition and conditional privacy-display expectations.
   Absence of a native privacy wizard alone is not a proven violation: the
   documented condition concerns transfer to systems not specified by the
   user. Fresh outbound defaults are off and operator opt-in is explicit.
6. Perform owner-run native installation acceptance of the exact new preview
   and publish it only with truthful scope/limits and final hashes.
7. Verify MFA, assign actual author/reviewer/approver roles, and review the
   final provider artifact configuration. These account-specific items are
   deliberately not guessed or stored in source.

## Owner-only fields to complete in the authenticated form

- First and last name and contact email.
- Account discovery/source answer.
- Verify GitHub and SignPath MFA and confirm the real project-team roles.
- Review and personally accept any Code of Conduct, privacy, or personal-data
  consent presented by SignPath.
- Complete CAPTCHA if requested.

No credentials, API tokens, private keys, VM configuration exports, operational logs,
database contents, or secrets belong in this draft or repository. Stop before
the final application submission action.
The live authenticated form was not rendered/filled by this preparation;
the owner must check its actual fields and consent text rather than assume
this draft is a complete form-schema inventory.

## References

- [SignPath Foundation conditions](https://signpath.org/terms.html)
- [SignPath GitHub trusted-build requirements](https://docs.signpath.io/trusted-build-systems/github)
- [Project privacy disclosure](privacy.md)
- [Code signing policy](code-signing-policy.md)
- [Earlier readiness checkpoint](../verification/signpath-readiness-2026-10-25.md)
- [Current exact r2 candidate](signpath-candidate-1.2.8-r2.md)
- [Current continuation and remaining gates](../verification/signpath-submission-2026-10-25.md)
- [All owner steps with exact artifact links/checksums](signpath-owner-steps.md)

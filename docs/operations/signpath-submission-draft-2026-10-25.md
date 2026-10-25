# SignPath application draft — 2026-10-25

**Not submitted.** This is a public-safe draft of product facts and current
gates. The authenticated form still needs the owner's contact details, MFA,
provider account setup, applicable consent, and an actual release before it
can be submitted. Do not infer approval or eligibility from this document.

## Form content draft

- **Project:** Host Agent V1
- **Source repository:** <https://github.com/Mina1399-web/PC-Agent>
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
- **Build system:** GitHub Actions on GitHub-hosted Windows runners, pinned
  action revisions and .NET SDK 10.0.401. The earlier binary-review workflow
  completed successfully on 2026-10-25
  for commit `a74f039781e1e587f4dde07d58450e0ad582858c`; this proves only the
  hosted review build and its declared test subset. The current continuation
  prepares a separate unsigned 1.2.8 MSI submission candidate with explicit
  install disclosure/acknowledgement, fresh dependency metadata and immutable
  WiX source. Its actual run and native acceptance are recorded separately.
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
   exclusions are not evidence that those suites passed. The current run is
   recorded in the readiness checkpoint.
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

No credentials, API tokens, private keys, VM identifiers, operational logs,
database contents, or secrets belong in this draft or repository. Stop before
the final application submission action.

## References

- [SignPath Foundation conditions](https://signpath.org/terms.html)
- [SignPath GitHub trusted-build requirements](https://docs.signpath.io/trusted-build-systems/github)
- [Project privacy disclosure](privacy.md)
- [Code signing policy](code-signing-policy.md)
- [Current readiness evidence](../verification/signpath-readiness-2026-10-25.md)
- [Current continuation and remaining gates](../verification/signpath-submission-2026-10-25.md)

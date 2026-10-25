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
  action revisions and .NET SDK 10.0.401. The current workflow is intended to
  produce an unsigned binary-review archive containing self-contained
  executables, with no MSI or installation script. It is not a supported
  product release. A fresh run for the updated workflow is still required;
  the preceding run built successfully but its test filter included VM- and
  account-specific suites that cannot pass on GitHub's hosted runner.
- **Reputation:** New project. Do not claim public adoption, endorsements,
  established reputation, or previous signed releases. Ask SignPath to assess
  eligibility under its reputation policy.
- **Maintainers and roles:** GitHub owner `@Mina1399-web` is the proposed
  maintainer/author/reviewer/approver. Confirm actual role assignments and MFA
  directly in GitHub and SignPath before submission. No other member is
  asserted here.
- **Public release/download:** No public production MSI release is currently
  established. The 1.2.7 artifact verified in the DEV VM is machine-guarded,
  unsigned, and not suitable for public distribution. The review workflow
  archive is not an application release.

## Current blockers before the form is ready

1. Publish a reviewed, user-facing release in the exact format intended for
   signing, with a download page that describes the functionality. Do not use
   the machine-guarded DEV MSI as that release.
2. Promote a complete licensed source branch to GitHub's project landing page
   without rewriting either existing history. The public default `main` is
   stale; the histories do not share a merge base.
3. Run the updated unsigned review workflow on GitHub-hosted Windows and
   inspect the resulting test reports and artifact manifest. The hosted filter
   must leave exact-identity, installer, non-admin ACL, and Windows 11
   service-inventory tests in the complete DEV VM suite; these exclusions are
   not evidence that those suites passed.
4. Complete a current dependency and redistribution review, including the
   WiX DTF custom action and applicable source/notices obligations.
5. Resolve SignPath's requirements for displaying privacy information during
   MSI installation. The current native MSI has no privacy-consent screen;
   current source says active connectivity probes default off and optional
   outbound features are operator-enabled.
6. After any revised MSI/privacy interface, perform installed-product
   acceptance and create the exact release candidate to be submitted.
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

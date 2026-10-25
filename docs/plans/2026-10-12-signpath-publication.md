# SignPath publication and application preparation

Historical 2026-10-12 checklist. The
[2026-10-25 continuation](2026-10-25-signpath-submission.md) records current
public source, successful hosted MSI candidate and remaining owner gates.
Unchecked historical items below must not override that newer evidence.

The owner authorized all preparation steps up to, but excluding, sending the
SignPath application. Existing public-source and GitHub-hosted build consent
remain effective. Preserve main and all existing history; no force push,
security-policy change, secrets publication or purchase.

- [x] Verify DEV VM, clean base and create a verified all-ref rollback bundle.
- [x] Inspect the real application fields and current Foundation requirements.
- [x] Test-first: uniform product metadata and MSI license/privacy payload.
- [x] Test-first: explicit independent network-probe opt-in, default off,
      backward-compatible strict configuration and announced targets.
- [x] Prepare a separately guarded hosted review build with pinned official
      actions, read-only token, no persisted checkout credentials, no secrets
      or install operation. Keep exact-VM MSI tests in the VM.
- [x] Complete Release suite and actual newly built ZIP/MSI verification
      (213 tests; read-only package checks, not a new MSI installation).
- [x] Review the immutable diff and close actionable findings (one P2,
      test-first fix and independent rereview).
- [ ] Verify public visibility after the owner's GitHub reauthentication;
      expose current licensed source as default without changing main commits.
- [ ] Execute and inspect the hosted build/tests/review artifact.
- [ ] Prepare a clearly labeled prerelease/download page and SHA-256 manifest.
- [ ] Fill the non-submitted application using verified links and truthful
      reputation; keep personal contact details in a private VM draft only.
- [ ] Stop before Submit/legal consent. Acceptance, actual signing credentials,
      staged production signing and target-policy acceptance remain later gates.

See [actual evidence](../verification/signpath-publication.md); checked steps
do not imply Foundation enrollment or a signed production product.

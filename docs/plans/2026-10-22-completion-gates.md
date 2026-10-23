# Installed-product completion gates — 2026-10-22

Return point: `5828569580fd1f7816b815c1380ba8fdffd41692`, branch
`codex/installed-telemetry-stabilization`. The remote branch matches this
commit. All execution remains inside the approved DEV VM. Installed 1.2.6
retains Shadow/read-only; this plan grants no unattended operation.

## 1. Transactional installer counter-read prerequisite

The operator has already added the monitor's virtual account to builtin
Performance Monitor Users (`S-1-5-32-558`). Two service-produced samples prove
the three fixed disk counters available. Source installer provisioning is
still missing. In this VM, the membership is preexisting and must not be
claimed or removed by a later installer.

Implement fixed-target SYSTEM deferred MSI actions, not a generic bootstrap
command. Resolve the localized group through SID558, and the member through
`NT SERVICE\HostAgent`; use SID-based NetLocalGroup level0 APIs. No other
account/group, service identity, privilege or security policy is configurable.

- Grant after InstallServices/before StartServices; journal rollback first.
- Require enabled MSI rollback before starting the transaction.
- Persist provenance in a narrowly protected HKLM installer key, separately
  from application configuration/databases. Preserve preexisting unowned
  membership; native "already member" must not acquire ownership.
- Repair retains ownership. Major-upgrade removal skips release; the new
  product inherits provenance. Final uninstall releases only owned membership
  after StopServices/before DeleteServices, with rollback restoration.
- Journal before mutation and record confirmation after the native boundary.
  SAM membership and registry writes are not atomic together: an interrupted,
  ambiguous journal must retain evidence and refuse a destructive guess.
- Automated behavioral tests exercise the transaction with fake privileged
  boundaries. Inspect real disposable MSI action types/conditions/sequence.
  Elevated clean-install, repair, rollback, upgrade and uninstall remain a
  separate operator gate; do not transfer mock evidence to that gate.

Support thread independently reviewed this design without editing or running
privileged operations. The implementation/tests determine the actual result.

## 2. Process-metric access denial

The installed sample still reports access denial outside the narrowly handled
Idle case. Do not classify it as harmless or grant debug/admin privileges.
Runtime10.0.12 uses QUERY_LIMITED_INFORMATION for TotalProcessorTime; WorkingSet64
on the enumerated Process objects uses snapshot data. No excess access mask
has been demonstrated. Optional MainModule failure is separately handled.

Next evidence: fixed-stage access-denial reasons during the existing collector
reads, retaining the exact PID0 exclusion and without PIDs/names/paths/exception
text or extra OS calls. This is the smallest first diagnostic step; counts or
further probes are justified only if the live stage is insufficient. Keep
partial/degraded until service-context evidence supports any correction.

## 3. Current installed release and Confirmation acceptance

After source regression/focused/Release gates, publish an exact hash-bound DEV
candidate and review its MSI. Operator performs elevated installation only
after the package/preflight is concrete. Verify fresh service-token samples,
authenticated View/WPF freshness, preserved configuration and both databases.
Test exact operation approval, allowlist, audit, cooldown/timeout/replay and
kill switch with isolated harmless DEV targets. Default remains Shadow.

The historic chaos/soak PASS results have their own artifact hashes and scope;
they do not automatically establish every current installed-release action.
The prior intermittent Release timeouts remain visible despite a later
572/572 run. Do not increase timeouts or erase failures to obtain acceptance.

## 4. Signing/distribution

The current release is unsigned/DEV-bound. SignPath's enrollment, verified
build provenance and nested signing/manifest ordering remain separate gates.
Prepare the source/build integration and application draft, but stop before
submitting the owner's application/identity details. No production-host
installation, trust exception, security-policy change or host-project update.

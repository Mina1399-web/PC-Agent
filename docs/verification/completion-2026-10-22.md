# Installed-product completion verification — 2026-10-22

## Scope and baseline

Source return point: `5828569580fd1f7816b815c1380ba8fdffd41692`,
`codex/installed-telemetry-stabilization`, initially clean. It was already
published to the same origin branch. Fresh execution identity verified
AGENTPC / AgentPC\AgentPCdevtest / VirtualBox,
UUID46543236-8C5E-49DA-953C-DB17F9B1E402, VBoxService Running. Initial free
memory1908.3MiB and C:18.76GiB. This is the approved DEV VM.

Installed1.2.6/sourcebf818 remains Shadow/read-only. Its two successful live
disk-counter samples after the operator's manual permission grant are recorded
in `confirmation-readiness-2026-10-21.md`; they are prior evidence for this
source change, not native acceptance of the new MSI custom actions.

New validation evidence directory (outside the repository):
`%LOCALAPPDATA%\HostAgent-DEV-evidence\completion-20261022`.
All managed validation children retain384MiB heap limits, serial builds,
1024MiB initial/768MiB soft/512MiB hard memory guards,250ms monitor and the
owner-authorized15GiB disk floor. No security policy/host project was changed.

## Test-first development evidence

All focused commands use the local SDK through `run-check.ps1`:

```
dotnet test tests/HostAgent.Infrastructure.Tests/HostAgent.Infrastructure.Tests.csproj -c Release --no-restore -m:1 /p:UseSharedCompilation=false --filter <filter> --logger console;verbosity=normal
```

The runner records the complete arguments, exit, output paths and independent
monitor/service-identity checks in each `result.json`.

| Run directory | Actual result |
|---|---|
| installer-counter-read-red | 1 expected FAIL: actual disposable MSI has no GrantCounterRead action; exit1,466.1s |
| counter-transaction-red | Compilation failed CS8766 in the legacy nullable test boundary; no tests ran, not RED |
| counter-transaction-red-compiled | 8 FAIL/3 PASS, missing membership/rollback/error behavior; exit1,64.0s |
| counter-transaction-green | 11/11 PASS; exit0,40.7s |
| counter-msi-validation-red | Four real mutated MSIs were incorrectly accepted by the old validator; all four negative tests FAIL. Positive test lacked the new result field. Total0/5, exit1,159.3s |
| counter-commit-and-prior-journal-red | 2/2 expected FAIL: old pending journal ignored; commit erased rollback evidence; exit1,34.0s |
| counter-transaction-revised-green | 13/13 PASS; exit0,36.0s |
| counter-msi-validation-green | ABORTED by soft768MiB watchdog after one mutation test passed; not a completed suite |
| counter-attempt-isolation-red | 14 PASS/1 expected FAIL: completed attempt could be replayed; exit1,47.1s |
| installer-preparation-build-before-red | Release build exit0,46.2s |
| counter-preparation-red-cached-and-msi | 20 PASS/1 expected FAIL: real DTF Prepare produced identical IDs for two attempts with the same ProductCode; exit1,120.2s |
| process-stage-reasons-red | 1 PASS/10 expected FAIL: no fixed stage reason and health filtered it; exit1,222.9s |
| completion-source-build | Release build exit0,47.2s |
| completion-focused-green | 51/51 PASS; exit0,199.0s |
| completion-build-after-review | Release build exit0,56.6s after test-class organization refactor |
| counter-journal-codec-red | Compilation failed CS8625/CS8603 in the initial placeholder; no tests ran, not RED |
| counter-journal-codec-red-compiled | 10/10 expected FAIL against the empty codec; exit1,30.6s |
| counter-journal-codec-green | 25/25 PASS (codec10 + transaction15); exit0,32.0s |
| journal-native-package-green | 26/26 PASS including actual packaged DTF Prepare after the native adapter adopted the codec; exit0,20.6s |
| installed126-package-trust-check | Test-Release for the existing1.2.6 package exit0,44.2s; six installed critical files match its validated manifest, SourceCommit bf818144a17a759ac6588ce8c513e9cabd872d01 |
| completion-final-source-build | Release build exit0,26.0s,0 warnings/errors |
| completion-final-focused | 70/70 PASS; exit0,139.4s, including the actual private C#5/WiX build without installation |

The successful51-test command selects ProcessReadStageTests,
ProcessMetricFailureTests, CounterReadTransactionTests, the actual DTF prepare
case and the five new MSI cases. It additionally uses `--no-build`, after a
separate successful build, to avoid retaining MSBuild state during native
MSI inspection. The aborted run remains aborted; no threshold was lowered.
Its own process tree ended and fresh free memory returned to1924.3MiB.
Every completed runner above records protected service identity/PID unchanged
and no service-control operation; all monitor results except the declared
abort are null. No elevated MSI installation was run in this development step.

## Source behavior and independent review

The installer-private actions have fixed SID558/NT SERVICE\HostAgent targets,
SYSTEM deferred mutation, rollback before mutation, and grant between
InstallServices/StartServices. Only installer-owned membership is released on
final uninstall, between StopServices/DeleteServices; old-product removal
during an upgrade skips release. Existing unowned/manual membership is retained.
MSI rollback-disabled cases are refused. Actual MSI structure/mutation tests
inspect exact action types, targets, conditions and ordering.

One protected64-bit HKLM record holds a fresh attempt ID and bounded before/
after flags. Commit retains rollback evidence. Only a later serialized attempt
may replace a completed record; another pending attempt blocks Apply. Rollback
selects only its exact ID. The fixed HostAgent service SID was independently
confirmed by NTAccount.Translate and read-only sc showsid; Apply rechecks the
registered account, while commit/rollback do not depend on a deleted service.
The shared codec bounds the record to256 characters, validates the exact
attempt/SID/version/bit fields and refuses committed-but-unapplied or applied
owned-but-not-member state. Native registry reads/writes use that same tested
codec; no separate permissive parser authorizes a destructive rollback.

Host Agent Support performed independent source reviews and found two concrete
errors in the initial draft: commit journal deletion could break later rollback,
and ProductCode-based reuse could undo an older successful repair. Both received
regression RED evidence before correction. Its final source review found no
further blocker in the reviewed version; this is static review only.

The process collectors add only fixed read-stage access-denial reasons to their
existing warning flow. No new OS calls or permissions, process identities,
paths or exception messages are added. Exact Idle/native5 exclusion and overall
partial/degraded behavior are preserved. Runtime10.0.12's CPU-time getter uses
QUERY_LIMITED_INFORMATION, and enumeration already supplies working-set data;
no excess access mask is established. New installed service-context stage
evidence remains pending an exact candidate installation.

## Open gates

- Final source Release build and631-test suite passed (see R3 below). This is
  not live elevated installation acceptance.
- SYSTEM/SAM/registry install, repair, rollback, upgrade and uninstall matrix is
  NOT RUN for this new installer. An isolated GUID native fixture is being
  prepared; it may not alter SID558 or the protected HostAgent installation.
- Actual installed stage reasons/current candidate acceptance are NOT RUN.
  Current1.2.6 remains degraded for process-metric denial; no broader privileges
  or suppression are justified from that aggregate warning.
- SignPath enrollment, identity/MFA, nested signing provenance and target policy
  acceptance remain external gates. No application or identity details were sent.
- Earlier intermittent full-suite timeouts remain historical findings even
  after the previous572/572 result. No new test timeout was enlarged.

## Continuation — 2026-10-23

Fresh ordinary operator context reconfirmed AGENTPC/AgentPCdevtest,
VirtualBox UUID46543236-8C5E-49DA-953C-DB17F9B1E402, VBoxService Running.
Current branch/HEAD remains the return point plus the preserved working tree;
no reset, install, service-control or host operation occurred.

Support's final review found a concrete harness error after the70-test run:
removing a manually seeded fixture membership changes SAM, but must not rewrite
the older committed journal. The generic MSI proof incorrectly required that
historical after-state to match the new manual state. A separate seed-removal
proof now checks the before/after membership and preserves all four historical
journal flags. The strict after-state check remains mandatory for MSI phases.
Before any MSI starts, quiescence is now explicitly null, not unobserved true.

| Run directory | Actual result |
|---|---|
| fixture-history-quiescence-red | 2 expected FAIL/1 PASS; missing historical-proof behavior and falsely true quiescence; exit1,157.6s |
| fixture-history-quiescence-green | ABORTED at soft768MiB; six individual tests completed successfully but no complete suite result. Protected services unchanged; own test tree ended |
| fixture-history-quiescence-focused-green | 3/3 PASS for the two corrections and ordinary-token refusal; exit0,29.7s |
| completion-final-build-256 | Release build exit0,36.5s,0 warnings/errors |
| completion-final-suite-256 | INCOMPLETE/INTERRUPTED: only child11868 receipt exists; no stdout/result or surviving child at fresh inspection. Cause not established; no complete suite claimed |

After the abort, fresh free RAM recovered to1886.7MiB; no owned dotnet/testhost/
fixture-controller remained. C: had17.08GiB free. No application or service was
closed to obtain headroom. Further validation uses a smaller256MiB managed heap
budget for its own tools (`run-check-256.ps1`), without changing1024/768/512MiB
memory floors,15GiB disk floor,250ms monitoring or600/900s deadlines. The earlier
abort remains visible and is not a PASS.

Support independently re-read the narrow proof correction and reported no new
concrete blocker. This is static review; the elevated native matrix is NOT RUN.
The fixture uses only its own GUID group, virtual demand-start service (never
started), registry root and filesystem package. It never targets builtin558 or
the protected HostAgent. It checks installed hashes against the reviewed1.2.6
package (manifest SHA256262D2D6C044B441A3DB7924496EA6D8365D88D8A24D581903FEC77F7C5F7E07C).
All input/tool hashes must be separately pinned before operator elevation.
Failures preserve fixture evidence and stop further phases; unconfirmed MSI
quiescence prohibits cleanup. A hard deadline can kill only the owned client,
never the SYSTEM Windows Installer server. Even a complete fixture PASS will
not establish actual installed product upgrade/repair/uninstall acceptance.

### Owner-authorized memory profile change

Later on2026-10-23 the owner explicitly changed the requirement: start with
more than768MiB free and allow runtime dips below768MiB. This supersedes the
old soft-stop requirement for the new completion validation and new counter
MSI fixture only. Emergency abort at or below512MiB, disk15GiB, fixed deadlines,
GUID isolation and fail-fast cleanup gates remain. Historical chaos scripts
and past results were not changed. Runtime768MiB is now a separately recorded
warning, not a cancellation code. Start/minimum/end memory are reported;
post-run recovery above768MiB must be observed within30s. This is not a claim
that memory returns to its exact initial value or that arbitrary exhaustion is
safe. No physical low-memory pressure was applied for this profile change.

| Run/evidence | Actual result |
|---|---|
| warning768-policy-red.json | Four expected policy mismatches against the old monitor |
| warning768-policy-green.json | New monitor pure boundary policy PASS; start768 refuses,768.1 accepts, runtime767.9/512.1 warns without abort,512/invalid abort |
| fixture-warning-policy-red | 4 expected FAIL/2 PASS; exit1,21.5s; monitor null, minimum1938.98MiB/end2271.44MiB |
| fixture-warning-policy-green | 15/15 PASS including private C#5/WiX build and warning/resource boundaries; exit0,24.4s; minimum1625.30MiB/end1951.52MiB; recovery confirmed |
| parent-monitor-smoke.json | Actual inline C# compiled, read-only native memory/disk sampling and thread shutdown PASS; minimum1934.32MiB, no pressure/service control/MSI |
| completion-warning-profile-build | Release build exit0,5.4s,0 warnings/errors; minimum1724.11MiB/end1880.00MiB |
| completion-warning-profile-suite | Full Release suite completed, exit1,287.1s: Core62/62, Infrastructure551/552, Desktop15/15; total628 PASS/1 FAIL, no skips. Only failure is the existing live VM-identity test invoking the historical20GiB resource gate on this17GiB-free VM. Monitor null; minimum1340.76MiB/end2356.35MiB; recovery confirmed |
| identity-resource-separation-red | 1 expected FAIL: no separate read-only identity entry point; exit1,12.0s |
| identity-resource-separation-green | 4/4 PASS (live identity + GUID guard cases), exit0,14.3s |
| completion-final-build-r2 | Release build exit0,3.1s,0 warnings/errors; minimum1935.80MiB/end2137.01MiB |
| completion-final-suite-r2 | Full Release suite629/629 PASS,0 skips: Core62, Infrastructure552, Desktop15; exit0,246.4s; monitor null; minimum1640.33MiB/end2138.41MiB; protected service identity/PID unchanged |

The source resource guard distinguishes initial headroom from `-DuringRun`:
initial768 refuses,768.1 accepts; runtime700 accepts,512 refuses. The controller
tests independently distinguish its fixed warning from its abort/cancel/deadline
decision. These are deterministic boundary tests, not a new live512MiB pressure
or physical watchdog-stop proof. No earlier aborted run was reclassified.

The identity-only COM regression was separated from the historical chaos resource
gate after its concrete20GiB failure. `ValidateVm` still calls the extracted
unchanged `ValidateIdentity` followed by unchanged `CheckLimits`. All five live
legacy entry points still call `ValidateVm`; their20GiB/soft768MiB guards were
not relaxed. Support independently reviewed this scope. The new passing identity
test is not acceptance to execute those legacy probes on this17GiB-free VM.

Before native operator execution, review found an additional fixture evidence
gap:1603 plus restored before-state can also result from a failure before the
planned fault CA. Support independently confirmed the false-positive path.
The fixture-only fault CA now emits one of two exact fixed INFO markers via
Session.Message, distinguishing deferred/commit mode. The client compares only
those complete ordinal constants, records counts and never saves raw UI text.
Every expected1603 now requires exactly one matching marker and zero of the
other, in addition to exit/proof/quiescence/resource gates. Successful phases
require both counts0. Missing/wrong/duplicate markers fail closed and retain
the failed phase name/evidence. Cancellation remains progress-only.

| Run directory | Actual result |
|---|---|
| fixture-fault-receipt-red | 2/2 expected FAIL: absent receipt validator/callback counters; exit1,44.2s |
| fixture-fault-receipt-green | 17/17 PASS; exit0,40.4s. Includes15 receipt cases, real C#5 callback with unknown/prefix/suffix/case/progress rejection and latched emergency cancellation, and the private MSI build. Minimum1490.34MiB/end2071.81MiB; monitor null |
| completion-final-build-r3 | Final Release build exit0,9.8s,0 warnings/errors; minimum1821.99MiB/end2047.27MiB |
| completion-final-suite-r3 | Final full Release suite631/631 PASS,0 skips: Core62, Infrastructure554, Desktop15; exit0,256.6s; minimum1699.57MiB/end2159.08MiB; monitor null, protected service identity/PID unchanged |

The operator matrix remains NOT RUN. Synthetic callback tests establish the
parser contract, not delivery of Session.Message through a native MSI run.
The operator must observe the matching counts; missing transport proof will
fail rather than accepting an unrelated1603. The earlier629-test result predates
this extension; the separate631-test R3 run verifies its final source state.
Support's final read-only review found no new concrete blocker. Deferred failure
proves a fault after Apply, not a completed commit; the late commit marker is
after the checked commit action. Neither static review nor synthetic callbacks
establish native message delivery or SYSTEM/SAM/registry behavior.

The final read-only fixture preflight on2026-10-23 confirmed HostAgent Running
PID3984 and HostAgent.Helper Running PID3520, with six readable critical hashes
and no changes. Required LocalAccounts cmdlets and the protected-directory ACL
API were discovered without invoking mutations. Evidence:
`counter-fixture-readonly-preflight-20261023.json`. A fresh hash-bound native
manifest must still be pinned for the operator; no elevated test was run here.

Changed/untracked source review found no credential-pattern hits or binary/
environment paths. A verified23-file source copy is retained outside Git at
`source-returnpoint-a18ca912bbf847658f5278c297be9dd8` (before the final two
identity-test files were added to scope); commits preserve the subsequent work.
The current source changes do not alter installed configuration or data;
Shadow/read-only remains the required installed mode. No host project transfer,
production installation, actual helper action or external notification occurred.

## Fresh distribution checks

GitHub connector `get_repo` on2026-10-22 reports `Mina1399-web/PC-Agent`
**public**, default branch `main`. This supersedes the private-repository
observation in the historical SignPath publication record; no visibility or
repository setting was changed in this run. Fresh `git ls-remote --heads`
returned main188e3e259519e8713cee7aed84fcec7e40aa408c and this work branch
5828569580fd1f7816b815c1380ba8fdffd41692 before the new source commits.
The existing workflow automatically triggers only on codex/signpath-preparation;
this current work branch has not established a new hosted build or signing run.

SignPath's terms and GitHub trusted-build documentation were refreshed today.
They still require a released/documented OSS project, MFA/approval roles,
provider-approved provenance and GitHub-hosted jobs for the OSS signing path.
Public source alone does not close those gates. No enrollment application,
signing request, credentials, identity details or cloud billing were submitted.

Primary references:
[MSI commit failure/rollback](https://learn.microsoft.com/en-us/windows/win32/msi/commit-custom-actions),
[limited-user counters](https://learn.microsoft.com/en-us/windows/win32/perfctrs/limited-user-access-support),
[.NET10.0.12 Windows process reads](https://github.com/dotnet/runtime/blob/v10.0.12/src/libraries/System.Diagnostics.Process/src/System/Diagnostics/Process.Windows.cs),
[SignPath terms](https://signpath.org/terms.html),
[SignPath GitHub provenance](https://docs.signpath.io/trusted-build-systems/github).

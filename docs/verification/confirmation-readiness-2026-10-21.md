# Installed telemetry, incidents and Confirmation readiness — 2026-10-21

## Scope and return point

Approved AGENTPC / AgentPC\AgentPCdevtest, VirtualBox UUID
46543236-8C5E-49DA-953C-DB17F9B1E402, VBoxService Running. Initial free RAM
1707.8 MiB, C:20.18 GiB. Source base e410360ddc31f86c752587ff315d6244c7944eb7,
clean codex/installed-telemetry-stabilization. A complete `before.bundle` was
created and verified before edits. Evidence parent:
`%LOCALAPPDATA%/HostAgent-DEV-evidence/confirmation-readiness-20261021T130942Z`.

No installed service/configuration/data, group, privilege, ACL, snapshot or host
project was changed. The running installed1.2.5 remains Shadow/read-only. Source
fixes below have not been deployed. The execution token is not elevated.

## Fresh bounded incident inspection

`run-check.ps1 -Kind Inspect -Name incident-inspection` completed exit0,
14.8s, monitorFailure=null, protected services unchanged. It used authenticated
read-only View overview plus20 bounded incident details and the actual installed
RecommendationEngine. No raw IPC payload or database content was saved.

Latest20 heads: all Active;13 service_stopped,6 redacted other_diagnosis,1
cpu_host_saturation. Eleven of the13 service-stop heads were observed on
October18, while two automatic Google updater transitions were observed on
October21. Current read-only SCM query found gpsvc Running; many of the old
subjects are Manual or trigger-start. These results establish historical
unresolved records, not20 current PC failures and not a new reproduction of the
already-fixed Manual/trigger service bug. The updater services were Auto,
Stopped and had no triggers in this observation. No service was started.

`affected-service-triggers.json` records current trigger counts through the
installed read-only ServiceTriggerQuery. No recommendation was returned by the
engine for the inspected snapshot. The dashboard count is explicitly scoped to
the latest20 heads. No historical incident was deleted, rewritten or marked
Resolved merely to clear the dashboard.

## Confirmed source defects and observed regressions

Admin metadata, proposal validation and individual remediation lifecycle
transitions unnecessarily loaded snapshots from all unrelated incidents.
An unrelated invalid archive payload blocked an otherwise valid exact action.
`ConfirmationReadScopeTests`: RED1pass/7fail, then GREEN8/8. The selected
incident's corrupt archive remains refused. New exact reads use a parameterized
ID and one deferred transaction, retaining at most64 selected snapshots. Full
archive and global diagnostic restore behavior are unchanged.

A fresh approval could revive a days-old Active diagnosis because only approval
time was checked. `ConfirmationFreshnessTests`: RED7pass/7fail, then GREEN14/14.
Writable Confirmation now rejects future/stale observation timestamps using
max(180seconds,2*validated collection interval); exact boundaries remain valid.
Tests cover old diagnosis/new snapshot, valid correlation evidence, default and
3600second cadences, invalid intervals, Shadow/read-only, exact confidence90 and
zero helper calls on refusal. No real privileged action was performed.

Independent read-only review found an additional exact-reader integrity gap:
valid JSON snapshot identity could disagree with its SQLite row.
`IncidentReadIdentityTests`: RED3pass/3fail, then GREEN6/6. Payload incident ID,
snapshot ID and timestamp must match their selected row. This was review-driven
regression hardening of the new reader, not a claim of a deployed exploit.

The first attempted read-scope test was aborted by the unchanged soft768MiB
watchdog before assertions; it is not RED evidence. Later validation processes
use a384MiB managed-heap cap and workstation GC, only in their own process tree.
Initial1024MiB, independent250ms soft<768/hard<=512MiB and C:>=20GiB guards
remain unchanged. Completed RED/GREEN runs above had monitorFailure=null.

## Telemetry diagnostic preparation and remaining gates

Live partial process metrics include both exact access denial and other
unavailability. SMART is explicitly unimplemented/optional. The public disk
warning collapses several PDH failure stages; an interactive administrator PDH
success does not prove access for the service token. No warning was suppressed
merely to turn health green, and no group membership was changed.

`scripts/diagnostics/Read-ServiceDiskCounters.ps1` prepares a fixed read-only
comparison using the expected running service token, then an independent
interactive child. It validates VM/account/admin, six installed1.2.5 hashes,
service identities/PIDs, pinned Python and resources. Python requests only
query/duplicate/impersonate rights, verifies the effective SID, reads fixed
counter status codes and aggregate process capability counts (no PIDs, names,
paths or metric values), and reverts the thread token before output. It never
adjusts privileges. This is effective-token comparison, not full SCM acceptance.

Source/harness preparation is not installed telemetry acceptance. The actual
operator run is still NOT RUN. It may safely refuse token access; that is a
blocker, not permission to enable debug privileges or bypass UAC. No global
Confirmation switch or actual installed remediation is authorized by a test
fixture's success alone. A new candidate/live bounded acceptance is required
after the telemetry cause is established.

## Diagnostic artifact validation

Python own-token/native-status, subject mismatch, inaccessible process and PID0
classification checks passed4/4 (1.874s test time); the bounded parent reported
exit0/4.6s/monitorFailure=null. These are own-account validation, not execution as
the installed service. ERROR_INVALID_PARAMETER for PID0 is explicitly not
reported as a confirmed process exit. No measured process values are emitted.

The actual PowerShell guard functions were tested against the six installed
critical hashes and two local manifest mutations (missing entry, wrong digest).
PowerShell7.6.5 and WindowsPowerShell5.1 syntax/guard checks passed3/3 each.
Actual probe execution requires PowerShell7. A normal disposable child and two
running-child synthetic soft/hard abort cases passed using the actual runner.
The late monitor-latch test also passed. Thresholds were not reduced and no
physical memory pressure was injected. A full PSD1 import hit PowerShell's data
evaluation key limit during preparation; the final artifact reads only fixed
entries from the exact SHA-256-pinned generated manifest without evaluating it.
The importer failure was a diagnostic harness defect, not a product result.

The final read-only reviewer found no remaining diagnostic boundary blocker
after immediate own-child cancellation and late-watchdog result accounting were
corrected. The privileged entry has not been executed by the agent.

Primary references used for the next diagnostic interpretation:
[PDH status versus counter CStatus](https://learn.microsoft.com/en-us/windows/win32/perfctrs/checking-pdh-interface-return-values),
[PDH error codes](https://learn.microsoft.com/en-us/windows/win32/perfctrs/pdh-error-codes),
[noninteractive counter access](https://learn.microsoft.com/en-us/windows/win32/perfctrs/limited-user-access-support).
These describe Windows behavior; they do not establish the installed failure's
cause without its fresh native result.

## Final source validation and current handoff

Release build: exit0,25.5s outer duration,0warnings/0errors. Complete Release
suite: exit0,577.8s outer duration,505/505 PASS (Core62,Infrastructure428,
Desktop15),0failed/0skipped. Both monitorFailure=null. Exact pinned SDK
`%LOCALAPPDATA%/HostAgent-dev-sdk/dotnet.exe` version10.0.401, commands:

```powershell
dotnet build HostAgent.sln -c Release --no-restore -m:1 /p:UseSharedCompilation=false
dotnet test HostAgent.sln -c Release --no-build --no-restore -m:1 --logger 'console;verbosity=minimal'
```

Focused runs used that same executable, Release/no-restore/-m:1/no shared
compilation, Infrastructure test project and filters
`FullyQualifiedName~ConfirmationReadScopeTests`,
`FullyQualifiedName~ConfirmationFreshnessTests`, and
`FullyQualifiedName~IncidentReadIdentityTests`. Their RED/GREEN receipts and
all stdout/stderr remain under the evidence parent. Product test source did
not change after the final suite. The271file ledger comparison found zero
changed compiled inputs. Two later diagnostic PowerShell changes (pinning and
its guard test) were independently revalidated rather than re-running unrelated
product tests.

The final guard-stage validation detected a changed bundled Python hash before
any privileged probe. The Codex dependency tool confirmed bundle26.1020.11127
and its exact Python path; the current executable is3.12.14,107312bytes,
created2026-10-21T11:44:07Z, with Valid OpenAI Authenticode signature. Its
fresh SHA-256 is7C4EE3011932A75B30DB176F35FB91B28F0B299E608EAF643FB1BF36ED5ACE99.
The old B07... pin was not silently bypassed: provenance was checked and the
new fixed pin validated. Final PS7 tests passed actual four-input pins,
wrong-hash rejection, write-lock exclusion, three installed-identity guard
cases, normal child completion, two synthetic resource aborts and late latch.
PS5 passed syntax/three identity guards/late latch; actual execution requires7.
`operator-role-guard.json` records the expected ordinary-token exit1 with
elevated_operator_required; no administrative probe was run.

Final diagnostic artifact SHA-256:

- Read-ServiceDiskCounters.ps1:
  97C4EBA6D041DB5DC9E73F12FE30C4AFB9076B1621BD436BFBF968ACFA01BFC6
- Read-ServiceDiskCounters.py:
  B1B76EE15F62367BCAEAC51A86A95607B3800F8012240023C8BA4C56408C4AB5

Final installed services remain Running with the same PIDs3692/3432 and six
critical hashes matched. Fresh remaining free RAM1736.2MiB,C:20.22GiB. The
operator must recheck through the script immediately before its own run.

Status: source corrections verified; live bounded incident review completed;
service-token telemetry diagnosis and new-candidate/installed Confirmation
acceptance remain OPEN. No new MSI was published/deployed, no commit or host
transfer was made, and no mode/allowlist/permission/secret was changed. Do not
claim all three user goals complete until the missing runtime evidence and
reviewed candidate deployment/selected-operation acceptance actually occur.

## Follow-up: elevated operator probe, 2026-10-21

The operator supplied the completed wrapper result and its actual retained
`result.json` was read back from
`%LOCALAPPDATA%/HostAgent-DEV-evidence/service-token-pdh-bda23611537e4a02a1e2962ae60eb7dc`.
It reports INCOMPLETE / `native_probe_not_completed_service`: service child
exit1, `completed=false`, stage `open_process_token`, Win32 code5 (access
denied). Neither the service-context PDH calls nor the interactive comparison
ran. This is a diagnostic access limitation, not evidence that the service's
own PDH calls fail with access denied. No privilege or token-DACL change is
authorized or attempted to bypass it.
Retained result SHA-256:
`11A71C66BEB208DF5494FD8FC34516E48A756EB88D65A2D64D3B22109BA610C2`.

The retained result reports monitorFailure=null, servicesAndHashesUnchanged=true,
privilegesChanged=false, serviceControlPerformed=false, databaseOpened=false.
Fresh read-only checks again found the approved VirtualBox VM UUID and both
installed services Running. Source fixes remain uncommitted and undeployed.
Support independently reviewed the prior handoff as suitable while explicitly
keeping effective-token comparison separate from actual SCM runtime acceptance.

Next diagnostic route under review: bounded typed native stage/status/CStatus
evidence emitted by the real disk collector, preserving existing health/reason
semantics. No underlying PDH root cause or repair is established yet. A fresh
resource check found free RAM676.1MiB (C:21.59GiB), below initial1024 and soft768
limits. No new build/test was launched; no guard refusal is counted as RED or
as a product regression. Instrumentation implementation and candidate/deployment
acceptance remain OPEN until memory headroom permits the required TDD checks.
The later check improved to865.6MiB but still failed the unchanged initial
1024MiB requirement. No product source or test was changed in this follow-up.
Support's static review found no demonstrated marshalling defect and confirmed
that native per-stage status/CStatus is currently discarded. This was not a
fresh native ABI or service-context runtime test. Proposed instrumentation and
its test cases were reviewed only; implementation/RED/GREEN remain NOT RUN.

### Follow-up after memory release

Fresh VM/account/UUID/Guest Additions checks passed; free RAM1162.7MiB and
C:21.62GiB passed initial guards. `pdh-native-evidence-red` was nevertheless
terminated by the unchanged soft768MiB watchdog before test execution:
monitorFailure=`soft_free_memory_floor_768_mib`, protected services unchanged.
This attempt remains an aborted run, not RED.

After helper work ended, free RAM1416.1MiB permitted a serialized retry.
`run-check.ps1 -Kind Focused -Name pdh-native-evidence-red-retry -Filter
'FullyQualifiedName~WindowsDiskIoDiagnosticsTests'` completed34.1s, exit1,
monitorFailure=null, protectedServicesUnchanged=true. One executed test failed
on the exact assertion "The real disk read discarded its bounded native PDH
diagnostics." This establishes RED for observability, not the service's PDH
failure cause. The first minimal implementation then passed the same filter
1/1 (`pdh-native-evidence-green`,57.2s, monitorFailure=null, services unchanged).
Further native failure-stage and View projection tests are in progress;
no new full-suite, candidate or installed-runtime acceptance is claimed yet.

### Completed focused instrumentation and View checks

The native reader now records at most ten fixed call records (open, three
adds, two samples, three formats, close) in the existing schema-1 disk payload.
Stage/counter enums, unsigned API status, meaningful CStatus and nullable
ValueFinite carry no paths, values, identities or exception strings. The
native boundary accepts only the three fixed counter IDs. Cancellation and
close-once behavior are preserved; close metadata is attached after finally.

Native TDD receipts: `pdh-native-failures-red`8pass/11fail, then
`pdh-native-failures-green`19/19pass80.5s. `pdh-finite-guard-red`18pass/9fail,
then `pdh-finite-guard-green`27/27pass71.1s. The latter RED also established a
real fixture defect: finite seconds double.MaxValue overflowed to Infinity
when converted to milliseconds. Nonfinite converted latency is now rejected
with the existing partial/unavailable behavior. This is not established as the
installed failure's cause. PDH_INVALID_DATA0xC0000BC6 retains its meaningful
CStatus; ValueFinite is evaluated only for API success and valid/new data.

View uses one existing bounded disk read, independently projects volumes and
diagnostics, checks stored-sample freshness, caps/rejects metadata above ten,
rejects duplicate/undefined stage-counter combinations and exposes only fixed
fields. No new command, permission, helper call or live probe was added.
`pdh-view-projection-red`20/20fail15.6s, followed by
`pdh-view-projection-green`20/20pass78.3s. Additional malformed JSON field-type
tests reproduced volume loss: `pdh-view-types-red`20pass/3fail17.0s. Explicit
numeric-kind checks fixed it; `pdh-view-types-green`23/23pass28.7s. Source Native
and View changes received independent read-only Support reviews with no
blocking findings; reviews did not execute tests or establish live acceptance.

All completed focused receipts report monitorFailure=null, protected services
unchanged and no service-control operation. The earlier watchdog abort remains
separate. Fresh Release build `pdh-release-build` passed13.0s, zero warnings and
errors. The complete Release suite is currently in progress and not yet claimed
passing. Its new evidence runner `run-current-check.ps1` preserves the original
1024/768/512MiB and20GiB resource guards and uses a900s outer Suite deadline
(other checks600s), allowing the prior577.8s suite plus50 new regression cases.
This changes verification duration only, not product timeouts. Test children
receive HOSTAGENT_TEST_LIVE_NETWORK=0. A229compiled-source-file SHA-256 ledger
was saved as `pdh-tested-source-before.json` for final comparison.

### Fresh complete Release result

`run-current-check.ps1 -Kind Suite -Name pdh-release-suite` completed exit0,
281.9s outer duration, monitorFailure=null and protectedServicesUnchanged=true.
The actual command was the pinned10.0.401 SDK's
`dotnet test HostAgent.sln -c Release --no-build --no-restore -m:1 --logger
'console;verbosity=minimal'`, with live-network opt-in explicitly disabled.
All555 tests passed: Core62, Infrastructure478, Desktop15; zero failed or
skipped. The229compiled-input ledger comparison found zero changed files after
the run. `git diff --check` passed. No new installed service-token telemetry,
candidate deployment or native Confirmation acceptance follows from this
source-level result. Installed1.2.5 remains unchanged Shadow/read-only.

### Candidate packaging and diagnostic limits (fresh continuation)

Verified source commit `6a9e5e7ff841ceb6eb489e384058e70ed02481db` contains
all changes exercised by the 555-test run above. The guarded r1 publisher
(`pdh-candidate-126-publish`) exited 1 after 209.4s with
`release_build_failed`; monitorFailure=null, protectedServicesUnchanged=true.
All four publish outputs, MSI and manifest exist, but no ZIP was produced.
The generic publisher error does not establish which final stage failed.
Do not install or represent this incomplete release as approved.

A separate guarded read-only `tools/Test-Release.ps1 -DevVm` invocation
(`pdh-candidate-126-artifact-check`) completed exit0 in 8.2s. It validated
1209 listed files and source commit 6a9e5e7ff841ceb6eb489e384058e70ed02481db.
Its monitorFailure=null and protectedServicesUnchanged=true. This establishes
folder manifest integrity only, not successful packaging or live acceptance.
Candidate directory: `HostAgent-DEV-releases/HostAgent-1.2.6-dev-confirmation-diagnostics-r1`;
manifest SHA-256 `9A40631923FEC3FA6A77D8A03CBF1489B177AD93273D71AC185B86EEBA415D1B`.

Independent review identified a narrower process-telemetry case requiring a
regression: .NET 10.0.12 maps the Idle pseudo-process PID0 CPU-time open failure
to Win32 error5. This does not justify suppressing other access denials or
claiming the installed collector cause is completely known. Tests/fix are
currently in progress; the 555-test result predates this new work.

The operator token probe's `privilegesChanged=false` expresses no explicit
privilege-change request in the probe, not a verified before/after token audit.
.NET Process initialization can attempt to enable an already-assigned debug
privilege. No privilege/ACL/group changes or token-access workaround were
requested or performed in this continuation. Its earlier open_process_token
error5 remains an incomplete service-token PDH measurement.

### Exact Idle-process regression and fix

`idle-process-context-red` ran the ProcessMetricFailureTests filter against the
unchanged implementation: 19 tests, 17 pass and 2 expected behavioral failures,
exit1, 202.5s outer duration. The real current .NET Idle CPU-time read produced
native error5 and failed only the classification assertion. The minimal fix
passes a known nullable PID from inside each collector's existing try block;
only exact PID0+Win32 error5 omits the operational warning. Other PIDs, unknown
IDs, other native codes and non-native errors retain existing degradation.
`idle-process-context-green` repeated the same filter: 19/19 pass, exit0,
168.5s outer duration. Both receipts have monitorFailure=null and unchanged
protected services. Independent read-only implementation review found no
blocker. These are source/current unprivileged test-process results, not
installed service-token acceptance. A full Release rerun follows this change.

### Isolated archive failure reproduced

A bounded, separate Compress-Archive invocation on preserved r1 into a new
scratch evidence path (`pdh-r1-archive-isolated`) failed exit1 after48.3s.
Fixed diagnostics identify stage=archive, MethodInvocationException,
errorId=OutOfMemoryException,Compress-Archive,
innerType=System.OutOfMemoryException, heapLimit=18000000 (384MiB).
monitorFailure=null; protectedServicesUnchanged=true; no service control.
Thus archive creation itself reproduces the resource failure; it is not an
inferred cause from missing ZIP alone. The archive module removes its partial
output on exception, so absence of ZIP cannot establish that it never started.
A bounded streaming ZIP regression/fix is in progress; no passing package or
installation is claimed yet.

### Streaming archive regression verified

The new New-ReleaseArchive tooling entrypoint uses ZipFile Create mode,
retains the base directory and refuses existing output, output inside source
and reparse paths. The builder invokes this helper; no product runtime or
installer permission change was made. Independent read-only review found no
blocker. `release-archive-streaming-focused`:6/6pass, exit0,77.3s outer duration.

`pdh-r1-streaming-archive-green` repeated archive creation on the SAME preserved
517.7MiB r1 payload under the SAME384MiB heap limit and resource watchdog.
It passed exit0 after106.3s, peak working set114282496bytes, and verified all
1210 ZIP file entries by length and streaming SHA-256 against source files.
ZIP size297265295bytes; SHA-256
238FF1EB6009C3EFF50CDA8A099A03E2F28F38CB8DFCF78A647BCE51CF3E0E2D.
All receipts show monitorFailure=null, protectedServicesUnchanged=true and no
service-control operation. This repairs the reproduced archive resource
failure; the original failed full publisher remains historical FAIL.

The generated successful diagnostic ZIP was removed only after recording its
hash/size and checking its exact scratch path and plain ancestors. Original
r1 payload, all pre-existing packages and failure/success receipts remain.
This recovers283MiB for further verification; no old evidence was deleted.
Fresh `final-idle-archive-build` passed exit0 after30.8s. The full suite is
in progress; it is not yet claimed passing.

### Publishing resource gate (fresh preflight)

After the successful scratch ZIP was removed, free C: space was20.766GiB
(and20.760GiB during the final suite). The previous payload is517.7MiB and
its verified ZIP297265295bytes; another full candidate of comparable size
would cross the unchanged20GiB disk floor. No new publication or installation
was attempted. Independent read-only inspection found no obvious newly-owned
1GiB disposable artifact; the fresh MSI intermediate is only0.73MiB. Preserved
packages, SDKs, source and evidence were not removed to force this gate.
A further1GiB free-space reserve is needed before a new full candidate build.

### Full-suite timeout findings remain open

`final-idle-archive-suite` exited1 after545.9s: Core62pass,
Infrastructure493pass/2fail (495total), Desktop15pass:570/572overall.
monitorFailure=null and protectedServicesUnchanged=true. Failures:
DashboardViewTests.Idle_listener_time_does_not_consume_an_authenticated_views_request_deadline
(client=view_unavailable, server diagnostic absent), and
ServiceLifecycleTests.Timeout_does_not_start_overlapping_cycle_and_stop_is_bounded
(outer3s StopAsync wait at line212; timeout/one-cycle assertions passed).
This is FAIL, not a verified complete Release acceptance.

The unchanged two-test focused rerun `final-timeout-triage-focused` passed2/2,
exit0,25.7s; monitorFailure=null, services unchanged. This establishes
intermittence, not root cause or a fix. Read-only reviews found a2s intentional
shutdown drain with only1s test scheduling margin, but no evidence proving
scheduler/GC as cause or a product deadlock. Product deadlines and test budgets
were not raised. Test-only bounded stage/timing instrumentation was added to
capture the next failure without request payloads, paths or native exception
text; validation and an instrumented full run follow.

The first instrumentation validation `final-timeout-stage-instrumentation`
failed at compile time (xUnit2020: Assert.True(false) must be Assert.Fail),
exit1 after36.6s, monitorFailure=null/services unchanged. No tests ran in that
attempt. The test-only assertion was corrected to Assert.Fail without changing
its timing or failure condition; the retry is recorded separately.

### Fresh final instrumented verification

`final-timeout-stage-instrumentation-fix` passed2/2, exit0,39.2s after correcting
the analyzer-only issue above. Independent read-only review confirmed the
instrumentation preserves every original timeout/assertion condition and
records no payload. `final-instrumented-build` passed exit0 in9.6s.
`final-instrumented-suite` passed ALL572/572 (Core62, Infrastructure495,
Desktop15), zero failures or skips, exit0 after518.2s. Commands remained:
`dotnet build HostAgent.sln -c Release --no-restore -m:1 /p:UseSharedCompilation=false`
and `dotnet test HostAgent.sln -c Release --no-build --no-restore -m:1 --logger console;verbosity=minimal`.
Live network opt-in stayed0. Every completed final receipt has
monitorFailure=null, protectedServicesUnchanged=true, serviceControlPerformed=false.
The274-input hash ledger comparison found zero changes after the suite;
`git diff --check` passed. Six protected installed critical hashes still match
the pinned1.2.5 manifest. Both services retained their original PIDs3692/3432.

This passing rerun does NOT erase the preceding570/572 full-suite failure or
prove its cause was fixed. Two intermittent timing findings remain open with
safe test-stage diagnostics now available. Source PID0 and archive resource
fixes are verified; installed PDH root cause/service-context acceptance,
new-candidate installation and native Confirmation acceptance remain OPEN.
No mode/configuration/service/ACL change, host transfer or deployment occurred.
Latest resource sample during the suite:1274.3MiB RAM,20.713GiB C: free.
New complete packaging remains blocked by the unchanged20GiB floor; no
unverified candidate installation command is provided.

### Owner-authorized15GiB DEV disk floor and successful r2 publication

The owner explicitly lowered the free-disk guard to15GiB. Fresh VM identity,
account, UUID, VBoxService, clean branch and source HEADbf818144a17a759ac6588ce8c513e9cabd872d01
were verified before execution. New external DEV publish/artifact runners use
15GiB both initially and in their250ms monitor; original repository chaos
runners and historical20GiB records were not rewritten. Tests verified disk
15GiB-minus-one-byte rejection, exact15GiB acceptance and20GiB acceptance.
Memory thresholds remain initial1024MiB, soft768MiB and hard512MiB; three
boundary checks passed. Managed child heap remains384MiB. Guard hashes and
validation receipts are preserved outside the repo in the existing evidence
parent, as DevPublishMonitor15.cs / run-publish-15gib-check.ps1.

`candidate-126-r2-publish-15gib` completed exit0 in401.9s, producing the full
DEV-only1.2.6 r2 folder, MSI and ZIP from the clean verified source commit.
monitorFailure=null, protectedServicesUnchanged=true, serviceControlPerformed=false.
Package: `HostAgent-DEV-releases/HostAgent-1.2.6-dev-confirmation-diagnostics-r2`.
Manifest SHA-256:262D2D6C044B441A3DB7924496EA6D8365D88D8A24D581903FEC77F7C5F7E07C.
ZIP SHA-256:E5AD864C2DFF201595CC5C64776CA34B4A6E1440A870EABA5F34CD455153668B.
Fresh separate `candidate-126-r2-artifact-15gib` Test-Release -DevVm passed
exit0 in10.3s, monitorFailure=null/services unchanged. Post-publish resources:
1681.0MiB RAM,19.83GiB free C:. The earlier disk blocker is closed for this
DEV publication. No installation or mode change was performed. Prior572/572
suite applies to the pinned source; no new full suite was run for external
guard/documentation-only changes. Historical intermittent timeout findings
and pending installed/native Confirmation acceptance remain open.

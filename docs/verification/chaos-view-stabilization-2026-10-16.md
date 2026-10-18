# View stabilization — fresh DEV evidence, 2026-10-16

Continuation of [the chaos campaign](chaos-resilience-2026-10-13.md).
Earlier results there are historical. This record includes only evidence
obtained in this continuation. It is not a pilot approval.

## Environment and preservation

Fresh checks: AGENTPC, AgentPC\AgentPCdevtest, VirtualBox, VBoxService Running,
6,321,737,728 bytes RAM. Working copy:
C:\Users\AgentPCdevtest\Documents\ChatGPT\DevTest VM PC\HostAgent-DEV.
Branch codex/local-dashboard; HEAD
6cd0ec8ba61ce1c7c7cd89787f02f86b9b87ed8e. Pre-existing dirty CPU/service/health
and desktop work was preserved; no reset, commit, push or host transfer.

Protected HostAgent remained Running, PID 3756 at the pre-suite check.
Executable SHA-256 remained
04DF669D662BB08BDBD76653E46158E3D701D7E0D48653439F8F836BB417CB87.
It was not stopped or overwritten. Its database was not a fault target.
No security policy, UAC, real secrets, helper mutation or external delivery
was used. Snapshot gate relies on the operator's earlier host verification:
pre chaos snapshot save / dc8af832-31d6-49ac-a8c3-f00d9381597f;
clean-win-base preserved. No new host snapshot query, reboot or power-off.

Free-memory readings fell to 496.4 and 429.4 MiB during ordinary development/
tests; no new pressure injection was started at those readings. Later
preflights were 1,091.6 MiB for rebuild and 1,048.1 MiB for full suite.
These are samples, not a continuous minimum. The isolated live harness
required 768 MiB before starting and checked the 512 MiB physical-memory and
20 GiB disk floors every 250 ms. It completed without a guard abort.

## Confirmed findings and test-first fixes

**P2 — idle accept consumed the request deadline.** Expected: a connected
authenticated request receives its full bounded budget. Real-pipe regression
Idle_listener_time_does_not_consume_an_authenticated_views_request_deadline
used 2-second limit, 1.2-second idle delay and 1.2-second dispatch.
RED: client=view_unavailable; server=dispatch/server_deadline.
The first fix gave AdminPipeServer independent accept and request deadlines.
Later live testing exposed the separate idle-retirement race below; the final
implementation waits for accept until service cancellation and gives only
the connected request a fresh bounded deadline.
Authorization, ACLs, bounded framing and shutdown cancellation are preserved.
No automatic retry of mutating commands was added. Recovery on a fresh
connection after a dispatch timeout passed. This establishes a deterministic
EOF mechanism, not the cause of every historical protected-service failure.

**P2 — cycle duration extended sampling cadence.** A controlled-TimeProvider
test with 20-second work and 60-second interval expected 40-second delay,
but observed 60 seconds (RED). With five CPU samples required in five minutes,
consistent cycle duration above 15 seconds can suppress diagnosis.
ResilientWorker now uses start-to-start cadence for success, clamps delay at
zero and yields on overrun. Failure backoff and no-overlap behavior remain.
CPU freshness, thresholds and confidence were not relaxed. Live saturation
with this updated collection runtime remains open.

**P2 — Automatic trigger service stop false positive.** New theory reproduced
confidence 88 for Automatic+HasTriggers with no crash. Now expects no diagnosis
for this case, 88 for non-trigger Automatic stop and existing 78 for matching
SCM crash. An initial crash expectation of 96 was a test-authoring error,
corrected to existing behavior before green; product confidence did not change.
Nullable HasTriggers is collected using read-only QueryServiceConfig2W,
SCM connect/service query-config rights only, trigger count only, 8 KiB buffer
cap. Failure remains null with a fixed warning, never false.
Fresh sc.exe qtriggerinfo W32Time showed triggers; EventLog showed none.
Windows collector integration assertions matched both.
API references: [QueryServiceConfig2W](https://learn.microsoft.com/en-us/windows/win32/api/winsvc/nf-winsvc-queryserviceconfig2w),
[service trigger events](https://learn.microsoft.com/en-us/windows/win32/services/service-trigger-events).

## Instrumentation and live observations

Fixed-stage optional diagnostics cover server creation/accept, frames,
identity/authorization, dispatch, response wait and worker retry. Dispatcher
stages identify metric, telemetry, incident reads and serialization. Service
logging records fixed failure kinds and slow stages (at least 500 ms), without
payloads, exception messages, identities, secrets or database contents.
Idle accept timeouts are suppressed; diagnostic callbacks cannot break IPC.

A real competing FirstPipeInstance creation reproduced an IOException outside
the original catch boundary. Creation is now covered by sanitized diagnostics;
its regression passed. This does not prove a historical pipe-name collision.

Read-only probing of the unchanged protected server: attempt 1 timed out after
10,386 ms at client response_frame/client_deadline; attempt 2 succeeded in
9,506 ms. Connect, owner validation and request write completed. Server-internal
attribution is still unavailable for that protected binary.
Reading snapshots for every incident before projecting 20 entries is a
performance hypothesis only; no speculative storage optimization was made.

## Separate current-source live candidate

Published test-only PipeProbe to:
%LOCALAPPDATA%\HostAgent-DEV-evidence\view-release-candidate-20261016.
It hosts current AdminPipeServer, DashboardWorker, DashboardDispatcher and
SqliteHostAgentStore on a GUID-derived user-owned pipe and separate database.
No helper/outbound sender. This is component integration, not full SCM hosting.

~~~powershell
& .\scripts\chaos\run-isolated-view.ps1 -CandidateDirectory "$env:LOCALAPPDATA\HostAgent-DEV-evidence\view-release-candidate-20261016"
~~~

| Scenario | Expected and actual | Result |
| --- | --- | --- |
| Late accept plus 1.2-second dispatch | Fresh budget, 5/5 overview replies | PASS |
| Process restart, same fixture | 2/2 replies after restart; quick_check healthy | PASS |
| Short timeout-injected repetition | First dispatch timed out; subsequent 5/5 recovered | PASS |

Fault request failed at response_frame/io after 2,083 ms; server reported
dispatch/server_deadline. Subsequent replies took 1,241–1,340 ms. Each server
ended with quickCheckHealthy=true. This is a short bounded run, not endurance.
Evidence directory:
%LOCALAPPDATA%\HostAgent-DEV-evidence\view-candidate-d3cdfd8030744de3bf544f6e1f4a82b9.
Published PipeProbe DLL SHA-256:
66F65E71440084D88819115E78C2DCFDD276F8410CFE31C86C716DE441F87B77.

## Disposable storage

Actual tiny SQLite fixture tests passed:

- Corrupt 1 KiB header: SQLITE_NOTADB (26), original bytes unchanged.
- Page-count exhaustion: SQLITE_FULL (13), failed telemetry insert absent,
  committed metric retained and quick_check=ok.
- Uncommitted transaction disposal: only committed metric present after reopen.

Page-limit exhaustion is an engine equivalent, not physical Windows disk-full.
Transaction disposal is cooperative interruption, not process/VM power loss.
The page-limit fixture initially violated json_valid and returned constraint
19; fixing synthetic JSON produced intended 13. No product fix was needed.

## Independent review and harness guard

The separate thread “Vytvoř dashboard aplikace” performed read-only review in
the verified VM, with no builds, service operations or edits. It confirmed
one harness gap: lexical path-prefix checking accepted junctions.
An extracted original check failed junction_candidate_accepted. New
Assert-IsolatedCandidatePath rejects reparse points in existing ancestors
and candidate entries before traversal. Regression: plain directory accepted,
junction rejected. Script parsing and actual published path validation passed.
The reviewer found no other confirmed production defect in the reviewed diff.
This is not exhaustive security certification. Native SCM error branches
remain a coverage gap.

## Intermediate automated commands and results

SDK: existing local 10.0.401; no SDK installation/download. Machine-wide dotnet
has no SDK, so the initial machine-path test did not run. Test utility restore
used local NuGet cache only. HOSTAGENT_TEST_LIVE_NETWORK was not enabled.

~~~powershell
$taskSdkRoot=Join-Path $env:LOCALAPPDATA 'HostAgent-DEV-tools\dotnet-10.0.401'
$env:DOTNET_ROOT=$taskSdkRoot
$env:PATH="$taskSdkRoot;$env:PATH"
$taskDotnet=Join-Path $taskSdkRoot 'dotnet.exe'
& $taskDotnet build HostAgent.sln --configuration Release --no-restore -t:Rebuild -m:1 -p:UseSharedCompilation=false
& $taskDotnet test HostAgent.sln --configuration Release --no-build --no-restore -m:1 --logger 'console;verbosity=minimal'
~~~

First continuation rebuild: exit 0, 0 warnings, 0 errors, 41.19 seconds.
Final gates after all later corrections are recorded at the end.

Focused runs used test <project> -c Release --no-restore -m:1 with
-p:UseSharedCompilation=false on later runs, console minimal logger and:

| Project/filter | Result | Log |
| --- | --- | --- |
| Infrastructure: AdministrationTests / DashboardViewTests / cold auth | 40/40 | pipe-green.txt |
| Infrastructure: View_worker_accepts_a_fresh_connection_after_a_timed_out_dispatch / Idle_listener_time_does_not_consume_an_authenticated_views_request_deadline | 2/2 | recovery-green.txt |
| Infrastructure: ServiceLifecycleTests / CollectionCoordinatorTests | 17/17 | cadence-green.txt |
| Core: DiagnosticEngineTests | 33/33 | trigger-green.txt |
| Infrastructure: DashboardViewTests / AdministrationTests / WindowsCollectorIntegrationTests.Process_service | 40/40 | view-collector-green.txt |
| Infrastructure: DisposableStorageResilienceTests / WindowsCollectorIntegrationTests.Process_service / RemediationGuardTests / CollectionCoordinatorTests / OperatingModeTests | 28/28 | disposable-focused.txt |

Helper outcomes and collector failures in focused tests are simulated;
they do not prove live privileged remediation.
Earlier full suite without DOTNET_ROOT failed three process-launch checks.
A corrected run passed Infrastructure 279/279 but failed a Desktop assertion.
DashboardUiTests now waits for chart data and tests layout against ActualWidth,
since the window manager can constrain requested width. Focused Desktop test
passed 1/1. Desktop product code unchanged. Another earlier full run's final
output was not captured and is not counted as a pass.

Logs outside Git: %LOCALAPPDATA%\HostAgent-DEV-evidence\view-20261016.
SHA-256:

| Log | SHA-256 |
| --- | --- |
| release-build.txt | 340F3B798393E2B038CA8DB58BC38857F3603051D7E354AA1D125AB5F5CCDD79 |
| late-accept-red.txt | D5901F8CDB6EF2D59036D05B9E7AB413FCCDA232E7BE43549EFBD95C4C9F6BEF |
| cadence-red.txt | 4BED1CF4AE04BC5EDD9E9FE7037C9E53521842E0B59D540006E01B1A1087A1B8 |
| isolated-live.txt | B6E9863685F330FD184EDA26ACB346346BBF64835A3257B0857E55E821E6491F |

## Open gates and verdict

**NOT READY** for a resilience-qualified volunteer pilot.

1. Current-source View recovery, restart and bounded actual CPU pressure now
   passed (later results below). Historical protected-service latency is not
   fully attributed; that protected binary remains the old version.
2. CPU cadence regression and current-runtime saturation passed. This is one
   bounded run, not endurance qualification. Earlier low-memory observations
   require continuing resource guards, not a claim that the CPU repeat was blocked.
3. Full isolated Windows service not installed. Production entrypoint has fixed
   service identity/endpoints/ProgramData, so a second normal launch would
   violate isolation. Current token is unelevated; SCM needs operator elevated
   control plus a reviewed isolated bootstrap. Protected install stays untouched.
4. Disposable process/SQLite interruption passed (below). Service-level full/corrupt storage, baseline
   recovery, sustained dashboard outage/window recovery, live safe remediation
   timeout/retry, combined faults, reboot and endurance remain open. Existing
   unit/fixture tests are not substitutes.
5. Hard power-off additionally needs verified host-side VM control/current
   rollback check. No host-control workaround used.
6. SCM query error branches/operational unknown-trigger behavior need follow-up.

No pilot proposal while these gates remain open.

## Later same-day live runtime investigation (supersedes intermediate View gate)

The intermediate full suite completed exit 0: Core 62/62, Infrastructure
285/285, Desktop 15/15, total 362/362, no skips. Its log SHA-256:
227790036DEF9D895121A025D55B27A9D16D810F8D685BA24900CF25A267362F.
Additional live testing then found another defect, so this is an intermediate
gate, not the final gate for the subsequently changed idle listener.

IsolatedRuntimeProbe is a test-only bootstrap of current ServiceRuntime,
all six actual Windows collectors/projectors, diagnostic/baseline processing,
SQLite, local notification/outbound workers and View server. It uses Shadow,
read-only, no network probes, LocalNotificationSender, DisabledHeartbeat and
a denying helper; it registers no admin/helper endpoint and never invokes the
production Program entrypoint. Each run requires a new GUID fixture beneath
LocalAppData evidence. VM checks require AGENTPC, VirtualBox BIOS product and
running VBoxService. A dedicated watchdog checks physical RAM/disk every
250 ms, cancels pressure below 768 MiB (above the 512 MiB hard floor), and
cleanup joins Lowest-priority CPU threads. CPU pressure uses at most four
threads in this process, no memory allocator or external child stressors.

Read-only independent review identified VM-gate, post-create path recheck and
watchdog-cleanup gaps in the draft harness. They were corrected before CPU
injection: reject reparse ancestors immediately before storage construction;
use nested finally to stop/join watchdog even if host shutdown fails.
The reviewer later confirmed the final changes statically.

| Fresh live run | Actual result |
| --- | --- |
| Observe, 20.1 s | View 2/2, helper 0, no guard abort, minimum observed free RAM 1,158.9 MiB |
| CPU, 345.5 s, four Lowest threads | FAIL: View 26/33, 7 failures; helper 0; RAM minimum 1,058 MiB; 5 high CPU samples; no CPU diagnosis |
| CPU with stage diagnostics, 80.5 s | FAIL: View 5/8, 3 failures; helper 0; RAM minimum 1,032.2 MiB; no server/dispatch failures |

The short run's failed requests were response_frame/io in 6 ms and
request_frame/io in 36 and 1 ms; successful overview dispatches completed.
These are transport-close failures, not slow dispatch or client deadline.
Logs: runtime-observe.txt, runtime-cpu.txt, runtime-cpu-stages.txt.

Read-only Python SQLite inspection of the long-run fixture used mode=ro,
printed only bounded timestamps/counters/CPU values and quick_check=ok.
Five high values were 99.987–100%; timestamps spanned only 230.063 seconds,
with maximum gap 88.2 seconds. The four-minute rule requires at least 240
seconds, so absence of a CPU diagnosis in this run does not prove a rule
defect. Fixture ID: ef33ab9020b54ade85bd3a7c101a48b2.
Evidence: runtime-cpu-inspect.json. No checkpoint or fixture content changed
by the inspection.

**Additional P2 — periodic idle listener retirement races connect.**
The server cancelled accept every ten seconds; worker then waited 100 ms
before recreating the endpoint. Ten-second overview polling reproduced fast
I/O failure when connecting to a retiring listener. Idle accept cancellation
was intentionally omitted from earlier server diagnostics, explaining the
empty failure list.

New deterministic regression
Idle_view_listener_does_not_retire_at_a_request_deadline_boundary failed:
with 500 ms request limit and no client for 750 ms, the listener had already
retired. Final correction: keep the idle listener waiting on the service
token, use a fresh deadline after connect. Idle stop remains cancellation
bounded; Idle_listener_service_stop_is_prompt_and_not_reported_as_a_view_failure
checks completion within two seconds. Full authorization/framing/dispatch
limits remain intact. Focused admin/view/cold-auth/lifecycle tests passed
53/53; log idle-retirement-green.txt. RED: idle-retirement-red.txt.

A new separately published candidate was tested at
%LOCALAPPDATA%\HostAgent-DEV-evidence\runtime-release-candidate-idlefix-20261016.
Its completed 450-second CPU repeat and further checks are recorded below.
The protected install was not replaced. Full SCM hosting and reboot remain open.

## Final live repeats after idle-listener correction

**CPU + current complete collection runtime: PASS.** The repeat ran 450.1 s
with four Lowest-priority CPU threads. View succeeded 43/43, with no client
or server transport failures and no helper calls. Minimum observed free RAM
was 1,012.7 MiB; no watchdog abort. Seven CPU samples were high
(99.994–100%); maximum sample gap was 64.1 s. A durable
cpu_process_saturation incident had confidence **88**.

Read-only inspection independently confirmed that incident, all six collector
snapshot domains and quick_check=ok. Fixture:
39fb2b65ace347f78063ed4925e9a2b8. Logs: runtime-cpu-idlefix.txt and
runtime-cpu-idlefix-inspect.json. View replies were generally tens of ms; the
slowest was 1,750 ms, still below the 8-second client limit. The only recorded
slow dispatcher stages were successful process-services (723 ms) and network
(994 ms) reads. This establishes bounded current-runtime CPU diagnosis/View
coexistence. It does not establish SCM behavior, long-term resource stability
or every historical timeout's cause.

**Fresh process restart + short timeout recovery: PASS.** The final separate
test utility installation at
%LOCALAPPDATA%\HostAgent-DEV-evidence\resilience-final-candidate-20261016
passed late accept 5/5 and process restart 2/2. Its injected first dispatch
timed out as expected; the following 5/5 requests succeeded. Each fixture
quick_check was healthy. Evidence:
view-candidate-18c25571722242e1bb98405c30015e0a, isolated-live-final.txt.
Published test utility SHA-256:
022279C29A5EF84B99E339FD2EA619146CEBA801102A3F18325D1BFCED70A0DE.
Core/Infrastructure/Service code in this installation is the corrected source;
the extra utility commands are test-only and are not production entrypoints.

**Disposable process interruption during uncommitted writes: PASS.**
IsolatedPersistenceProbe first committed metric 42 using SqliteHostAgentStore,
then held an actual SQLite transaction containing 512 synthetic 8 KiB rows
(about 4 MiB) before commit. The parent waited for that barrier, force-terminated
only its own fixture process using the original Process handle, and verified
with Python SQLite mode=ro: committed metric 42 retained, zero uncommitted
telemetry rows, baseline origin retained and quick_check=ok. Fixture:
83936d95b2374d9eb298f964a960e126; log disposable-interruption.txt.
This is actual process termination/SQLite rollback, not Windows power loss,
an interrupted production SaveIncidentBundle transaction or a full SCM crash.

~~~powershell
& .\scripts\chaos\run-disposable-persistence-interruption.ps1 -CandidateDirectory "$env:LOCALAPPDATA\HostAgent-DEV-evidence\resilience-final-candidate-20261016"
~~~

Before injection, independent review identified a PID-reuse race in the draft
PID-only kill. It was corrected to retain Process.SafeHandle and use that
Process object's Kill/WaitForExit, including fallback cleanup in the View
harness. The verifier path is checked before starting the fault. VM, plain
candidate path, initial 1 GiB RAM and 20 GiB disk guards remained in place.
The child has a 60-second automatic rollback fallback if not terminated;
the parent bounds barrier wait at 15 seconds. No protected service was stopped.

These results close the bounded View/CPU and disposable SQLite interruption
checks, not the open SCM, physical disk-full, reboot, GUI and endurance gates.

## Final diff findings: creation backoff and shutdown grace

Final checking found that the expanded server catch swallowed listener
creation failures and returned normally, bypassing worker error backoff/health.
Two regressions failed first: sanitized creation rethrow was absent, and a
real FirstPipeInstance collision never reached the worker retry diagnostic.
After sanitized reporting, server_create/endpoint_validation now rethrow
to the existing worker. Connected request errors remain contained.
The real collision test verifies only one call during the configured five-
second retry period. No mutating retries were added.

The first focused run after this correction had 53 passes and one failure:
cooperative cycle cleanup was not finished when Stop returned after the
fixed 500 ms drain. Reproduction with an explicit 600 ms cleanup failed in
542 ms. DrainAsync now permits up to two seconds within the host's fifteen-
second shutdown budget. The test still requires cleaned=true after Stop;
the uncooperative-job test still requires no overlap and stop within three
seconds. This trades up to 1.5 additional seconds of bounded shutdown for
more cooperative cleanup opportunity; it does not guarantee completion of
dependencies ignoring cancellation indefinitely.

Focused admin/view/cold-auth/lifecycle tests then passed **54/54**.
Evidence: creation-backoff-red.txt, shutdown-drain-red.txt,
shutdown-backoff-green.txt. The two fixes received independent read-only
review with no further concrete finding.

The 450-second CPU result above predates these final creation-error/shutdown
changes; healthy collection, diagnosis and accepted-request paths did not
change. It is not represented as a new 450-second run of the final binaries.
The final current-runtime smoke and Release gates are recorded separately.

## Final verified candidate and Release gate

Final separate installation:
%LOCALAPPDATA%\HostAgent-DEV-evidence\resilience-verified-candidate-20261016.
All four Core/Infrastructure/Service/PipeProbe assembly SHA-256 values match
the final Release rebuild; candidate-assembly-manifest.json records them.
The production service entrypoint was not launched by this installation.

Current final runtime smoke: 20.2 s, View 2/2, helper 0, no transport/dispatch
failure, minimum observed free RAM 990 MiB, normal bounded stop. Fixture
511ed5cacd2b45e298582216d15392f7; runtime-verified-observe.txt.
Final process restart/timeout recovery repeat again passed 5/5 late-accept,
2/2 restart and one expected timeout followed by 5/5 successful replies;
all fixture quick checks healthy. Evidence:
view-candidate-4e91c514bb7348cdb2ec61cc4bb4866d,
isolated-live-verified.txt. This is the final code, including creation backoff
and shutdown grace. The earlier long CPU run is scoped as described above.

Commands are the SDK-root-qualified build/test commands listed earlier.
Final build log release-build-verified.txt: **exit 0, 0 warnings, 0 errors,
37.29 seconds**.
Final release-suite-verified.txt: **exit 0, 364/364, no skips**:
Core 62/62, Infrastructure 287/287 (1 min 21 s), Desktop 15/15 (18 s).
No source change followed these runs; only verification documentation was
completed. Final whitespace diff check and parsing of the new PowerShell
harnesses passed. The junction regression passed again.

| Final evidence | SHA-256 |
| --- | --- |
| release-build-verified.txt | 81A622B73F7690B6CCB08B57DC2560EA7ED56D48C7EBB889FD1C2F3D06C8B987 |
| release-suite-verified.txt | 29CF8FC6570C5598440E3BB4D932AED1D0AC403545CAD33346EC26D984D13972 |
| runtime-cpu-idlefix.txt | BBF4ABED36068CDF7EFD9C94D7E86654D311102DC483C256868069A71B2348AC |
| disposable-interruption.txt | BA498726405AFFF126048F0353BF52920AF9CB3DD529554D73497DC8C6E3A5AD |

Final protection check compared the installed executable and all three
HostAgent assemblies against their starting hashes: all unchanged.
Branch/HEAD remain codex/local-dashboard /
6cd0ec8ba61ce1c7c7cd89787f02f86b9b87ed8e; no new commit or host transfer.
No fixture PipeProbe process remained after the completed test runs.

## Function/gate assessment

| Area | Status | Scope/remaining blocker |
| --- | --- | --- |
| View transport and recovery | Ověřeno v uvedeném rozsahu | Real Windows IPC, slow dispatch, timeout recovery, process restart; CPU coexistence verified on idle-fix build; final smoke separately verified |
| Sustained CPU diagnosis | Ověřeno v uvedeném rozsahu | Seven high real samples and durable confidence 88; no indefinite/endurance qualification |
| Service-stop classification | Částečně ověřeno | Real trigger metadata + regression theories; actual privileged stop/crash of current isolated SCM service not run |
| Collectors | Částečně ověřeno | All six ran in isolated current runtime; failure injection covered automatically, not all live WMI/EventLog failures |
| Persistence/storage | Částečně ověřeno | Real SQLite corruption/page limit, rollback and fixture process kill; no physical full volume, mid-SaveIncidentBundle kill, OS power loss or full application baseline recovery |
| Dashboard window | Částečně ověřeno | WPF tests + real View transport; sustained outage/recovery of a live connected dashboard window not manually observed |
| Remediation | Částečně ověřeno | Shadow/no helper calls, simulated failure/timeout/no-retry assertions; no new real privileged mutation |
| Full Windows service / reboot | Neověřeno pro nový kandidát | Token unelevated and elevated native console unavailable to this control surface; separate SCM bootstrap/registration still required; protected installation must stay untouched |
| Hard VM power-off | Neověřeno | Host-side control/current rollback recheck needed; not attempted through a workaround |
| Extended concurrent-fault endurance | Neověřeno pro finální kandidát | Bounded CPU and short timeout repetition do not replace long endurance |

Changed production scope: AdminPipe, ViewPipeClient, DashboardDispatcher/
DashboardWorker/ServiceAdministration, ResilientWorker, service trigger query,
service telemetry and service diagnosis. Added/updated regression tests,
test-only process hosts and guarded fixture scripts. Existing dirty
DiagnosticCycleProcessor, runtime-health/coordinator and desktop test work
was preserved and covered by the fresh suite; it is not all attributed to
this continuation.

Source return point stays inside the VM:
%LOCALAPPDATA%\HostAgent-DEV-evidence\source-return-point-20261016-reviewed.
It contains a tracked diff against the exact HEAD, selected source/test/docs
files including new harnesses and SHA-256 manifest, without SDK, bin/obj,
database, secrets or environment caches. It is a local recovery artifact,
not evidence of host synchronization.

Final verdict: **NOT READY** for a resilience-qualified volunteer pilot.
The confirmed View and cadence/trigger defects have fixes and fresh evidence.
Remaining privileged SCM/reboot, live GUI/remediation and endurance gates
must be completed before a pilot proposal.

## Isolated SCM guard revision (fresh verification 2026-10-16T22:23Z / guest date 2026-10-17)

This update follows the review request to tighten the still-unrun isolated SCM candidate. Existing working-tree changes and the prior candidate were preserved; no commit was created. The protected HostAgent service remained Running and its image/three assembly hashes were checked by the fresh read-only preflight. No service-control operation, service configuration change, database access, ACL mutation, or `-Execute` run occurred in this update.

### Changes prepared for review

- Added a separate `HostAgent.PipeProbe` watchdog process. While the isolated SCM test is active, it samples available physical memory every 250 ms. At <768 MiB it records only bounded run metadata, then calls Stop on the service name generated from the current run GUID (`HostAgentDevScm_<guid>`). It does not accept an arbitrary service name. The parent script checks the marker around SCM calls, after IPC/db-check child processes, in polling waits, and during cleanup. A breach aborts the test, keeps the test registration/data for review, and waits for the watchdog stop result. Existing 768 MiB inline and 20 GiB C: checks remain. The 768 MiB decision unit test verifies that 768 continues, 767.9 selects only the GUID test service, and invalid run IDs do not produce a target. This is a decision/target test, not a forced live memory-breach run; the watchdog's real SCM stop path remains unverified until the separately approved `-Execute` run.
- Removed the test service grant from the package root. The package root is restricted to Administrators/SYSTEM; `app` and each staged binary/canary grant the service ReadAndExecute; only the unique `data` directory grants it FullControl (required by `HostAgentDataDirectory.EnsureSecure`). The service checks effective explicit/inherited service-SID ACLs across the app tree and attempts to overwrite/delete two disposable canary files before starting workers. The policy tests pass; the live service-token ACL/canary check is compiled but not run because `-Execute` is explicitly deferred.
- Wrapped the test pipe's DashboardDispatcher callback in `IsolatedScmDashboardDispatch`: only a valid `overview` request is forwarded; history, incident, investigate, and invalid frames are rejected before dispatch. Focused tests confirm overview reaches the dispatcher and the other operations do not.

### Fresh checks and limits

- `Invoke-Pester -Script tests/chaos/IsolatedScmSafety.Tests.ps1 -EnableExit`: **2/2 passed**. The PowerShell parser accepted `run-isolated-scm-candidate.ps1`; `git diff --check` passed.
- `dotnet test tests/HostAgent.Infrastructure.Tests/HostAgent.Infrastructure.Tests.csproj --configuration Release --no-restore --filter FullyQualifiedName~IsolatedScmDashboardDispatchTests -m:1 -p:UseSharedCompilation=false --logger "console;verbosity=minimal"`: **4/4 passed** (including the two memory target theory cases).
- `dotnet build HostAgent.sln --configuration Release --no-restore -m:1 -p:UseSharedCompilation=false`: **exit 0, 0 warnings, 0 errors**, minimum observed free memory 964.5 MiB. SDK 10.0.401 was installed in the VM user's LocalAppData only, not system-wide.
- The full solution test attempt used `dotnet test HostAgent.sln --configuration Release --no-build --no-restore -m:1 --logger "console;verbosity=minimal"` with an external 768 MiB guard. Core tests completed **62/62**; the guard stopped the test run at **765.2 MiB** after Infrastructure testhost launch, before that project's result. No assertion failure was reported. Infrastructure full-suite and Desktop full-suite results remain unverified; the full Release suite is therefore not marked passed.
- Candidate was published to `%LOCALAPPDATA%\HostAgent-DEV-evidence\scm-release-candidate-20261017-r1`; its manifest covers 73 files, SHA-256 `FDFC573380B5AFC21E28AC06EDC3DEDC7E8C9BDBECD3B5692F12125AF029BDDC`. The prior `scm-release-candidate-20261016-r2` was not overwritten.
- Fresh read-only preflight of `run-isolated-scm-candidate.ps1` exited 0: AGENTPC / VirtualBox / VBoxService Running; 1188.2 MiB free RAM; 29.34 GiB free on C:; candidate manifest and pinned release assemblies matched; protected HostAgent state/path/account/image and assembly hashes matched. It printed the isolated GUID service, distinct View pipe, and distinct ProgramData data path, and explicitly reported no service/file/ACL/database operation. This satisfies preflight only; no SCM lifecycle result is claimed.

Evidence logs are outside the repository under `%LOCALAPPDATA%\HostAgent-DEV-evidence\scm-guard-change-20261017`. Release build stdout SHA-256: `00331420F96F9C3985718AA2D8305CB967FD3F5B17179AC8950B9378AD217B1B`. Full test-attempt stdout SHA-256: `B098D26CFD8FAD6A15EAA9D0AC5E4C1C454678BD6A14A58DC2DF326C630F1B1A`.

Status of the SCM gate: **prepared and read-only preflight passed; live service start/stop/restart, real below-floor stop, runtime ACL canaries, and persistence assertions remain unrun by request**. The overall chaos verdict remains **NOT READY** until those and other open gates are actually exercised.

### Final r2 candidate and focused verification correction (fresh 2026-10-16T22:23Z–22:24Z UTC)

The earlier `r1` candidate and preflight entry above are retained as history. A final root-ACL runtime assertion was added afterward, so `r1` is superseded and must not be used. `r2` was published from the final source to `%LOCALAPPDATA%\HostAgent-DEV-evidence\scm-release-candidate-20261017-r2`; 73 file hashes are covered by manifest SHA-256 `64F9319797BE711587C1B01880D6D767A4F7BC123FF05EA02D6377EB899020D6`. Final PipeProbe SHA-256 is `9EDC9A703EAE4C4B189E4C1BA5ABAE08239096228D0D0A4E8F5C04FA5B8B1A66`; the three pinned product assemblies remain the same hashes listed by the fresh preflight.

After the final code change, the focused Release test was rerun: `IsolatedScmDashboardDispatchTests` **4/4 passed**. The broader `DashboardViewTests` focused run passed **23/23** with minimum observed free memory 1025.5 MiB. The two Pester guard-policy tests passed again; candidate PowerShell parse and `git diff --check` passed. The final incremental Release solution build completed exit 0 with 0 warnings/0 errors and minimum observed free RAM 976.9 MiB. Build stdout SHA-256: `FE937AD3C5723AE9A3DC8A4547132F1AAE13DAAC99B4213C103D99255DF01341`. Candidate publish completed exit 0; minimum observed free RAM 1106.8 MiB.

A final no-`-Execute` preflight against r2 exited 0. It verified AGENTPC/VirtualBox/VBoxService, 1115.4 MiB free RAM, 29.3 GiB free C:, candidate manifest and all pinned assemblies, and the protected HostAgent executable/assembly hashes and Running state. It reported the distinct GUID test service, View pipe and ProgramData data directory, then stated that no service/file/ACL/database operation was performed. The old r1 package was left intact. The full Release test suite was not rerun after the final root-ACL assertion; its earlier attempt on r1 remains an incomplete run that was stopped at 765.2 MiB after Core 62/62 passed. No complete Release-suite pass is claimed.

### Memory-guard stop fallback and final recheck (fresh 2026-10-16T22:27Z UTC)

Final script review found that a watchdog stop failure previously preserved the isolated service and data but did not attempt a second stop. `Ensure-TaskStoppedAfterMemoryBreach` now verifies the exact GUID registration/path, confirms the protected service baseline, and retries Stop only on that isolated service for a bounded 15 seconds. It never deletes the registration/data after a memory-floor breach. If stop cannot be confirmed, the result records that explicitly and leaves the registration/data for manual recovery. This fallback received PowerShell parse and policy-test coverage only; the actual low-memory/SCM stop path remains intentionally unrun.

After that correction, Pester safety policy remained 2/2, PowerShell parse passed, `git diff --check` passed, and the no-`-Execute` preflight against `r2` again exited 0 (1124.7 MiB free RAM, 29.3 GiB free C:, all candidate and protected hashes matched). No candidate binary changed after the r2 manifest was created. No service operation, ACL change, or database operation occurred. The full-suite limitation and focused test results above remain unchanged.

### Malformed request coverage and guarded Release verification (fresh 2026-10-16T22:51Z)

The isolated SCM Dashboard pipe test now covers three malformed frames: invalid JSON, unsupported protocol version, and missing required body. Each is asserted to return `ok=false` with `view_request_rejected`, and the DashboardDispatcher callback must remain uncalled. This addition changes the test source only; it does not change the candidate runtime implementation.

- Focused command: `dotnet test tests/HostAgent.Infrastructure.Tests/HostAgent.Infrastructure.Tests.csproj --configuration Release --no-restore --filter FullyQualifiedName~IsolatedScmDashboardDispatchTests -m:1 -p:UseSharedCompilation=false --logger "console;verbosity=minimal"` using the VM-local .NET SDK 10.0.401. Result: **7/7 passed**, minimum observed free RAM **924.8 MiB**. Captured stdout SHA-256: `DF79B9D17255F4450E40B14A08D6C23656E01360096614D447145EB4417ED84F`.
- Full Release build: `dotnet build HostAgent.sln --configuration Release --no-restore -m:1 -p:UseSharedCompilation=false`. Result: **exit 0, 0 warnings, 0 errors**, minimum observed free RAM **972.8 MiB**. Captured stdout SHA-256: `7ED6DD87869BD00EAA8DC3F28A8CD59B0CD5596BA18FFE88C60991AD0477D06E`.
- Full Release suite was attempted with `dotnet test HostAgent.sln --configuration Release --no-build --no-restore -m:1 --logger "console;verbosity=minimal"` under a 768 MiB memory watchdog. Core passed **62/62**. The watchdog terminated the test process tree when free RAM reached **762.5 MiB**, below the 768 MiB floor, while Infrastructure testhost had launched but before it emitted a result. No Infrastructure or Desktop full-suite result is claimed. The wrapper exited with `MEMORY_WATCHDOG_ABORT`; captured stdout SHA-256: `B098D26CFD8FAD6A15EAA9D0AC5E4C1C454678BD6A14A58DC2DF326C630F1B1A`.
- Read-only post-stop check at 2026-10-16T22:51Z found no remaining `dotnet`/`testhost` processes, protected `HostAgent` still Running at its original service path with executable SHA-256 `04DF669D662BB08BDBD76653E46158E3D701D7E0D48653439F8F836BB417CB87`, and zero `HostAgentDevScm_*` services. No service operation or protected file/configuration change was performed by this run.
- Per the stop-on-watchdog rule, candidate republish and read-only preflight were **not** run after this test attempt. The previous `r2` candidate remains the last preflighted package and does not include this test-source-only addition. `-Execute` was not run. The final SCM candidate/preflight gate remains open.

### Lowered-memory watchdog retry, final candidate r3 and read-only preflight (fresh 2026-10-16T23:07Z)

At the user's request, the full Release suite was repeated with the soft memory watchdog reduced by 20% from 768 MiB to **614.4 MiB**; the hard stop remained **512 MiB**. No source changes occurred between the successful Release build recorded above and these suite runs.

- First retry without an explicit `DOTNET_ROOT`: watchdog did not trip (minimum free RAM **684.6 MiB**). Core passed **62/62**; Infrastructure completed **291/294**, with 3 failures caused by `ArgumentNullException` from `Path.Combine` because the test process had no `DOTNET_ROOT`; Desktop passed **15/15**. This run is not a suite pass.
- The suite was rerun with process-scoped `DOTNET_ROOT=C:\Users\AgentPCdevtest\AppData\Local\HostAgent-dev-sdk\10.0.401` (SDK **10.0.401**). Command: `dotnet test HostAgent.sln --configuration Release --no-build --no-restore -m:1 --logger "console;verbosity=minimal"`. Result: **exit 0**; Core **62/62**, Infrastructure **294/294**, Desktop **15/15**; minimum free RAM **820.5 MiB**, so neither 614.4 MiB soft nor 512 MiB hard guard tripped. Captured stdout SHA-256: `308CD8B53042EF160ACC56CC227E36081082D537EDADC31CC1D62B144A9FEFD8`.
- The isolated PipeProbe Release candidate was freshly published into the new, previously absent `%LOCALAPPDATA%\HostAgent-DEV-evidence\scm-release-candidate-20261017-r3` directory. Publish exited 0; minimum free RAM **1039.4 MiB**. The manifest covers **73 files**, SHA-256 `64F9319797BE711587C1B01880D6D767A4F7BC123FF05EA02D6377EB899020D6`; every r3 file hash matched r2, so the package binaries are unchanged. Pinned hashes: Core `A59AE0BABD875F83025B876CEBA0883A92BB9686FF8C2F65AC6034C1AD268379`; Infrastructure `48A628574834962BE7BB37CDBE08B0E86C8A09D7A7187D35B65AE9DCC2B2672C`; Service `E3E253AADB91932D1061569955E236115CB45A0F4FDC0E772A083A8D00CF6A10`; PipeProbe DLL `9EDC9A703EAE4C4B189E4C1BA5ABAE08239096228D0D0A4E8F5C04FA5B8B1A66`; PipeProbe EXE `6E270A54D2FC3B6E5B69DEEECA9090203BBF8D3BB032FD345CDAA68B2106E39D`. The runner script now pins r3 and this manifest; r1/r2 packages were preserved.
- Final safety checks: candidate PowerShell parse passed; `Invoke-Pester -Script tests/chaos/IsolatedScmSafety.Tests.ps1 -EnableExit` passed **2/2**; `git diff --check` passed. The no-parameter read-only preflight exited 0 and verified AGENTPC/VirtualBox/VBoxService Running, **1163.0 MiB** free RAM, **29.25 GiB** free on C:, r3 manifest and pinned candidate hashes, and protected HostAgent Running with its expected image and assembly hashes. It planned only a GUID service `HostAgentDevScm_67fca13ae1034dca93ed623c9d4b7f89`, distinct pipe `HostAgent.Dev.Scm.View.67fca13ae1034dca93ed623c9d4b7f89`, and isolated ProgramData package/data paths, then reported that no service/file/ACL/database operation occurred.
- Read-only post-check at 2026-10-16T23:07Z found the protected HostAgent still Running with executable hash `04DF669D662BB08BDBD76653E46158E3D701D7E0D48653439F8F836BB417CB87` and **zero** `HostAgentDevScm_*` registrations. No `-Execute` run occurred. The isolated SCM live lifecycle remains unverified and still requires the user's separately controlled elevated one-shot run.

### Candidate visibility root cause and stable staging path (fresh 2026-10-17, Europe/Prague)

The user's standalone elevated Windows PowerShell returned `required_directory_missing` from Assert-TaskPlainTree line 76, called by Assert-TaskManifest line 100 and the initial preflight line 391. User-supplied diagnostics confirmed the same AGENTPC/AgentPCdevtest account, VirtualBox model, VBoxService Running and machine UUID `46543236-8C5E-49DA-953C-DB17F9B1E402`, but Test-Path on the logical AppData r3 path returned false.

Fresh read-only handle inspection confirmed the root cause: opening the logical candidate manifest from Codex resolves physically to `C:\Users\AgentPCdevtest\AppData\Local\Packages\OpenAI.Codex_2p2nqsd0c76g0\LocalCache\Local\HostAgent-DEV-evidence\scm-release-candidate-20261017-r3\candidate-manifest.json`. Earlier Codex/child-PowerShell preflight passes therefore did not establish visibility from the user's standalone PowerShell at the logical AppData path.

The original package and r1/r2 were preserved. The verified r3 was copied, without overwriting or deleting an existing package, to `C:\Users\AgentPCdevtest\Documents\ChatGPT\DevTest VM PC\HostAgent-DEV-SCM-candidates\scm-release-candidate-20261017-r3`, outside the Git checkout. GetFinalPathNameByHandle confirmed that this new path resolves directly to that physical Documents location, without AppData redirection. All 73 manifest-listed files and the exact inventory were verified; manifest SHA-256 remains `64F9319797BE711587C1B01880D6D767A4F7BC123FF05EA02D6377EB899020D6`. No binary changed or republish occurred. Only the runner candidate-path expression was changed to the stable sibling directory relative to PSScriptRoot.

PowerShell parser and `git diff --check` passed. Windows PowerShell 5.1 launched from System32 with the updated runner and no parameters completed read-only preflight successfully: free RAM 2053.5 MiB, C: free 29.69 GiB, candidate manifest/product hashes and protected image/assembly hashes matched. HostAgent remained Running; no HostAgentDevScm_* service registrations existed. No service-control operation or -Execute run was performed by Codex. Verification in the user's original standalone elevated console remains pending; the live isolated SCM gate remains open.

### SCM create command-line defect and bounded diagnostics (fresh 2026-10-17, Europe/Prague)

The user's one-shot elevated run `4297b9c158144b94800ef5258afc645f` failed with `failure_scm_create`, protectedServiceUnchanged=true, empty View/database checks and cleanup=`isolated_guid_package_and_data_removed`. The stored result was read back in the VM. A separate read-only check confirmed HostAgent Running (PID 3540), no registration for the GUID test service and no remaining GUID package. The user's original PowerShell error stack identified `isolated_sc_create_failed` at the native-create exit check, and the user reported native exit **1639** (invalid command line).

Root cause in the test harness: native SCM option/value pairs were grouped as single arguments, e.g. `binPath= <value>`. A behavior-preserving extraction into Get-IsolatedScmCreateArguments retained those original 7 arguments. The new behavioral regression then failed before the fix: expected **12** native arguments, got **7**; Pester exit 1 (2 passed, 1 failed). Command: Windows PowerShell 5.1 `Invoke-Pester -Script tests/chaos/IsolatedScmSafety.Tests.ps1 -EnableExit`.

The builder now emits each option and value separately. The runner uses ProcessStartInfo with explicit Windows native quoting to preserve the embedded image-path quotes independently of PowerShell 5.1's native binder. A harmless argv-printing fixture, compiled and executed only in the VM's temporary test directory, verifies exact native round trips for the complete SCM argument list plus empty strings, spaces, quotes, trailing backslashes and Unicode. It never calls SCM. The same focused Pester command passed **4/4**, exit 0. The runner also records `scmCreateExitCode` and a bounded stable `failureReason` instead of losing the original error; raw process output is not persisted. The create-process wait has a 15-second timeout and checks the existing 768 MiB/C: safety floor while pending. No limits were lowered.

Fresh post-fix Windows PowerShell 5.1 no-parameter preflight passed: free RAM **2150.5 MiB**, C: free **29.57 GiB**, stable Documents candidate path, unchanged 73-file r3 manifest SHA-256 `64F9319797BE711587C1B01880D6D767A4F7BC123FF05EA02D6377EB899020D6`, candidate product hashes and protected installation hashes matched. Parser and `git diff --check` passed. HostAgent remained Running; isolated service count was zero and the failed run's package remained absent.

Only PowerShell harness/module/tests and this record changed. No product binary changed; no candidate republish or C# Release build/suite rerun was performed for this harness-only fix. Earlier Release results remain historical. Codex did not run -Execute or any SCM service operation. Actual post-fix SCM creation/lifecycle remains unverified until the separately controlled elevated run; no pilot-readiness upgrade is claimed.

### Windows PowerShell 5.1 watchdog launcher compatibility (fresh 2026-10-17, Europe/Prague)

The user's run `be39d18917874e54bc730f2013fae027` reported SCM create exit **0**, then `failure_memory_watchdog_start` / `unexpected_exception`, memoryGuard=`not_started`, empty View/database checks, protectedServiceUnchanged=true and completed GUID package/data cleanup. Fresh read-only checks confirmed HostAgent still Running, no isolated SCM registrations, and no remaining package for this run. SCM creation is now demonstrated by the user-run evidence; service start/View/persistence are not.

A regression executes the actual Start-TaskMemoryWatchdog function extracted from the runner, replacing only its read-only service-identity guard and native executable with a harmless temporary argv fixture. It does not execute the runner top-level flow or call SCM. Before the fix, Windows PowerShell 5.1 Pester failed as expected at `$taskInfo.ArgumentList.Add`: `You cannot call a method on a null-valued expression`; **4 passed / 1 failed**, exit 1. Root cause: ProcessStartInfo.ArgumentList is unavailable on the .NET Framework used by Windows PowerShell 5.1.

The runner now sets ProcessStartInfo.Arguments using the previously verified Windows native quoting helper. No memory floor, service target, candidate binary, manifest, or ACL policy changed. The identical Windows PowerShell 5.1 focused command `Invoke-Pester -Script tests/chaos/IsolatedScmSafety.Tests.ps1 -EnableExit` then passed **5/5**, exit 0. The regression checks launcher success, guard invocation, exact five watchdog arguments (including paths with spaces and floor 768), and bounded child exit/cleanup.

An additional safe smoke used the actual unchanged published r3 PipeProbe watchdog mode with a unique nonexistent test-service GUID and a stop-signal file created before launch. Source inspection confirms this returns before memory sampling or any service operation. The actual process exited **0**, no memory-action marker was produced. Fixture: `%LOCALAPPDATA%\HostAgent-DEV-evidence\watchdog-launch-smoke-17475cabbb884af5b0442ce4ef0f56dd`. This verifies candidate process launch/argument acceptance/explicit-stop exit only; it does not verify low-memory SCM stop or sustained watchdog monitoring.

Fresh Windows PowerShell 5.1 no-parameter preflight passed with **2134.5 MiB** free RAM, **29.57 GiB** free C:, stable Documents r3 path and original manifest SHA-256 `64F9319797BE711587C1B01880D6D767A4F7BC123FF05EA02D6377EB899020D6`. Protected image/assembly hashes matched, HostAgent remained Running, isolated service count was zero, and `git diff --check` passed. Only the PowerShell runner/test and this record changed; no C# binary/build/suite rerun or candidate republish occurred. Codex did not run -Execute or any SCM operation. Actual post-fix isolated SCM lifecycle and low-memory service-stop gates remain open.

### Isolated service first-start ACL crash, cleanup ordering, and candidate r4 (fresh 2026-10-17, Europe/Prague)

User-run evidence `ac669f9a314249d2b5955c9ec3031a23` showed successful service creation, active watchdog, then `failure_first_start` / `isolated_sc_start_failed`. The retained result.json was read back. Read-only SCM checks found HostAgent Running (PID 3540) and no remaining isolated service registrations; the GUID data/package remained preserved and was not modified or deleted.

Bounded, run-specific Event Log inspection confirmed an actual test-host crash: SCM events 7009/7000 reported a 30000 ms startup-connect timeout; Application Error 1000 identified the exact GUID app path and managed exception code 0xe0434352. Matching .NET Runtime event 1026, RecordId **22687**, at **2026-10-17 04:22:42** identified UnauthorizedAccessException in GetAccessControl at IsolatedScmServiceProbe.RunServiceAsync line 53. Root cause: the service attempted to read the package-root DACL although that root deliberately grants access only to Administrators and SYSTEM. The system .NET 10.0.13 runtime was present; missing runtime was investigated and excluded.

The root ACL requirement was retained without adding any service access. Its validation now runs in the administrative runner before each start, checking protected inheritance, Administrators ownership and exactly the Administrators/SYSTEM FullControl rules. The service startup checks only app's read/execute ACL and existing overwrite/delete canaries; inaccessible or writable app ACLs fail closed. The original failing root read was first isolated by a behavior-preserving helper extraction. Focused Release regression failed as expected with fixture_root_acl_denied (exit 1); four expanded startup scenarios also failed before the fix. After the fix, combined IsolatedScm focused tests passed **11/11**, minimum free RAM **2209.5 MiB**. InternalsVisibleTo is limited to the existing test assembly; no production service code changed.

A related cleanup-order risk was found: package removal preceded watchdog shutdown even though the watchdog runs from that package. The cleanup orchestrator regression initially failed both required outcomes (deletion before stop returned false; shutdown failure had already invoked deletion), Pester **8 passed / 2 failed**, exit 2. After reversing the order, Windows PowerShell 5.1 Pester passed **10/10**. These cleanup regressions use controlled boundary callbacks, not live service/file deletion; live post-fix cleanup is still unverified. Shutdown failure preserves the package. The old failed-run data remains untouched. Start/stop operation native exit codes are now retained as bounded scmOperations metadata.

Fresh guarded verification used the VM-local .NET SDK 10.0.401 with process-scoped DOTNET_ROOT and an external **768 MiB** abort threshold (no lower floor was used in this update):

- `dotnet build HostAgent.sln --configuration Release --no-restore -m:1 -p:UseSharedCompilation=false`: exit **0**, **0 warnings / 0 errors**, minimum free RAM **2432.4 MiB**; stdout SHA-256 `F3A2BB7521438D804FDCC15DB4453005860AC0B50E97BBF728B24D4A42C5FDA3`.
- `dotnet test HostAgent.sln --configuration Release --no-build --no-restore -m:1 --logger "console;verbosity=minimal"`: exit **0**, Core **62/62**, Infrastructure **298/298**, Desktop **15/15** (total **375**), minimum free RAM **2184.4 MiB**; stdout SHA-256 `EB89CA7C8470ECBFEA4E6B3502E304F8A00ADF45F3C4F41E1B2C0558467351E4`.
- `dotnet publish tests/HostAgent.PipeProbe/HostAgent.PipeProbe.csproj --configuration Release --no-restore -m:1 -p:UseSharedCompilation=false -o <stable Documents r4 path>`: exit **0**, minimum free RAM **2425.8 MiB**. Logs remain under the VM evidence directory `scm-root-acl-fix-20261017`.

New candidate: `C:\Users\AgentPCdevtest\Documents\ChatGPT\DevTest VM PC\HostAgent-DEV-SCM-candidates\scm-release-candidate-20261017-r4`. All **73** files were verified against the fresh manifest SHA-256 **0B42D4DCD736F2EBB3F7736464757E13C82CDEB83A95AA029F03BF12458367AB**. PipeProbe DLL SHA-256 **9286E3D307D75DA499B49D87E4E9CABBDA07C7ACF78567B8DA00DB7E1653D83A** matches the current Release source output; Core/Infrastructure/Service and apphost EXE hashes remain unchanged. The runner pins r4 and the new manifest/DLL hash. r1/r2/r3 were preserved.

Final Windows PowerShell 5.1 no-parameter preflight against r4 passed: **2249.8 MiB** free RAM, **29.51 GiB** free C:, all pinned candidate/protected hashes matched; HostAgent remained Running, isolated service count zero, original failed GUID data still present. `git diff --check` passed. No -Execute or SCM operation was performed by Codex. Actual r4 service start, runtime canaries, View, persistence, restart and live cleanup still require the separately controlled elevated test. The overall pilot verdict remains NOT READY.

### Isolated r4 SCM graceful lifecycle: live PASS (fresh 2026-10-17, Europe/Prague)

The user executed the reviewed runner with `-Execute` in elevated Windows PowerShell inside the DEV VM. Run `d831760581d340e78d970b1dc31e4474` completed with **PASS**, null failureCode/failureReason and SCM create exit 0. Its preflight reported AGENTPC / AgentPC\AgentPCdevtest / VirtualBox / VBoxService Running, **2297.4 MiB** free RAM and **29.51 GiB** free C:. Candidate r4 manifest SHA-256 was `0B42D4DCD736F2EBB3F7736464757E13C82CDEB83A95AA029F03BF12458367AB`.

Codex independently read and validated the stored evidence at `C:\Users\AgentPCdevtest\AppData\Local\HostAgent-DEV-evidence\scm-candidate-d831760581d340e78d970b1dc31e4474\result.json`; file SHA-256 **9187650DA505621E0554E421257C359022333C3743262545992D66346006AA3F**. No SCM operation was performed by Codex during this verification.

- Isolated service `HostAgentDevScm_d831760581d340e78d970b1dc31e4474`: **start / stop / start / stop all exit 0**. First and second process IDs were **2836 / 4364**.
- Isolated View pipe: overview succeeded on the first attempt after each start, in **469 ms / 241 ms**. This verifies graceful restart connection recovery for this run, not sustained saturation or outage recovery.
- Four database checks: **healthy=true / quickCheck=ok**, each with 3 migrations. The first two checks reported 6 snapshots, 16 baseline rows and 217088 bytes; the last two reported 12 snapshots, 32 baseline rows and 266240 bytes. These demonstrate healthy isolated storage across the graceful cycle and increasing persisted counts; exact baseline identity restoration and interrupted-write recovery were not established by these aggregate checks.
- Runner reported `active_250ms_watchdog_and_inline_checks_at_768_mib`. This successful run does not demonstrate a forced low-memory shutdown or establish the minimum memory reached during execution.
- Runner reported protectedServiceUnchanged=true and `isolated_guid_package_and_data_removed`. Independent read-only `Get-Service` / `Get-CimInstance Win32_Service` checks agreed that protected HostAgent remained Running, PID **3540**, account `NT SERVICE\HostAgent`, with its original image path. `Get-FileHash -Algorithm SHA256` confirmed the expected protected EXE and Core/Infrastructure/Service hashes unchanged.
- Independent post-check found **zero** `HostAgentDevScm_*` registrations and no successful-run package at `C:\ProgramData\HostAgent-DEV-SCM-d831760581d340e78d970b1dc31e4474`. Prior failed-run package `C:\ProgramData\HostAgent-DEV-SCM-ac669f9a314249d2b5955c9ec3031a23` remains preserved. Thus live cleanup ordering is now verified for the successful run; earlier retained evidence was not deleted.

This closes the isolated r4 **graceful SCM lifecycle / View reconnect / database health / successful cleanup** gate only. Historical failed attempts above remain part of the record. Current HEAD remains `6cd0ec8ba61ce1c7c7cd89787f02f86b9b87ed8e`; existing uncommitted work was preserved. No code change, new build/suite execution or commit was made for this evidence-only update. The preceding 375/375 Release result remains the latest build/test evidence, not a new run.

Still open: forced watchdog-stop behavior, isolated crash and reboot/interrupted persistence recovery, remaining disposable storage/collector/remediation faults, sustained dashboard outage and bounded fault-injected endurance. Hard power-off remains subject to verified host-side rollback and isolation. This single graceful lifecycle pass does not close those gates; overall pilot verdict remains **NOT READY**.

### Forced watchdog SCM-stop gate: read-only assessment, NOT RUN (fresh 2026-10-17, Europe/Prague)

Fresh checks verified AGENTPC, VirtualBox, VBoxService Running, UUID `46543236-8C5E-49DA-953C-DB17F9B1E402`, account `AgentPC\AgentPCdevtest`, and HEAD `6cd0ec8ba61ce1c7c7cd89787f02f86b9b87ed8e`. The current Codex command token is **not elevated**. Existing working-tree changes were retained. Protected HostAgent was Running, PID 3540, original virtual service account/image path; EXE and all three protected assembly hashes matched the recorded baseline. No isolated SCM registration existed.

Command actually executed: `& '.\scripts\chaos\run-isolated-scm-candidate.ps1'` (no parameters). Read-only preflight passed with **2385.1 MiB** free RAM, **29.51 GiB** free C:, r4 manifest `0B42D4DCD736F2EBB3F7736464757E13C82CDEB83A95AA029F03BF12458367AB` and all candidate/protected hashes matching. No test service, ACL or database operation was performed.

Source inspection covered `IsolatedScmMemoryWatchdog.cs`, the runner's startup, memory checks and failure-preservation/cleanup paths. The watchdog samples GlobalMemoryStatusEx every 250 ms, writes a bounded marker before stop and updates stopConfirmed afterwards; it targets only `HostAgentDevScm_` plus a parsed GUID and waits up to 10 seconds for Stopped. The runner uses 768 MiB and preserves registration/package/data on a recorded memory breach. These are static implementation observations, not a live forced-stop verification. The watchdog currently stops the test service only: it does not control a memory-pressure process, implement an independent 512 MiB pressure-abort supervisor, or bound the overall pressure exercise. The existing graceful-lifecycle runner does not create memory pressure. Therefore running the unchanged runner cannot prove this gate, and adding ad hoc memory allocation without independent release/termination protection would not satisfy the requested safety conditions.

**Blocked / NOT RUN:** no live memory pressure or forced SCM stop was attempted. Required preparation is a separately verified bounded pressure harness and an elevated one-shot execution path; UAC was not bypassed. No threshold was lowered, no candidate/source code was changed, and no further chaos scenario started.

Proposed test design, NOT implemented or verified in this update:

1. Reuse r4's validated GUID-only service, virtual account, private read-only overview IPC, isolated app/data paths and protected-service baseline checks. Start the isolated service and prove overview before creating pressure. Reject any unexpected image/path/account/GUID or candidate hash.
2. Before pressure, establish and test an independent low-overhead supervisor and OS-owned bounded child lifetime for the pressure process. It must terminate only the recorded pressure process on **512 MiB**, parent loss, supervisor failure, or a fixed overall timeout (proposed 60 seconds). Pressure must self-release on stale supervisor heartbeat or cancellation; no arbitrary PID/name termination. Verify these mechanisms on harmless fixtures before real pressure. Keep the 768 MiB service watchdog unchanged.
3. Allocate and touch memory in small bounded increments, stopping further allocation near the 768 MiB crossing. Release all pressure immediately when the watchdog marker appears (do not wait for its up-to-10-second SCM stop), when memory reaches 512 MiB, or on any error/deadline. The 512 MiB value is an abort trigger, not a guarantee against instantaneous OS-wide memory changes; if the available monitoring/bounds cannot make the crossing acceptably safe, do not run real pressure.
4. PASS requires a real below-768 observation, watchdog marker for the exact run GUID with stopConfirmed=true, exit 73, independently observed isolated service Stopped, and preserved registration/package/data/evidence. Controller stop must not be used to claim watchdog success. After pressure is released, recheck protected service state/path/account/hashes and retained isolated data. An abort, unexpected stop source, unconfirmed stop or missing evidence is not PASS. Keep the package/data for review rather than automatically deleting them.

The forced-watchdog gate remains open. Earlier graceful r4 PASS and Release 375/375 evidence are unchanged; no new build/test execution is claimed here.

### Bounded forced-watchdog probe prepared; live execution pending (fresh 2026-10-17, Europe/Prague)

At the user's request, the previously blocked pressure harness was prepared for a separately controlled elevated one-shot run. Existing work was preserved; HEAD remains `6cd0ec8ba61ce1c7c7cd89787f02f86b9b87ed8e`. Codex did not execute SCM operations, start actual memory pressure, change protected HostAgent, or begin another chaos scenario.

Changed test tooling: `scripts/chaos/run-isolated-scm-candidate.ps1`, `scripts/chaos/IsolatedScmSafety.psm1`; new `scripts/chaos/BoundedScmPressure.cs`, `scripts/chaos/Invoke-BoundedScmPressure.ps1`, `tests/chaos/BoundedScmPressure.Tests.ps1`; this record. No production C# source or candidate binary changed. r4's 73-file manifest remains `0B42D4DCD736F2EBB3F7736464757E13C82CDEB83A95AA029F03BF12458367AB`; no republish was necessary.

New opt-in mode: `-WatchdogStopProbe`; without `-Execute` it still performs read-only preflight only. With both switches it stages only a fresh GUID service using the existing isolated account, ACL, data and overview IPC rules, establishes its initial healthy state, then runs bounded memory pressure. The service watchdog remains unchanged at **768 MiB** / 250 ms. The separate pressure child has a **512 MiB** abort condition and independent 25 ms supervisory thread, parent-identity check, cancellation/marker check, 60-second lifetime and 4096 MiB allocation cap. It allocates/touches at most 4 MiB per 100 ms and holds further allocation once available memory is at or below 752 MiB. It releases all memory by process exit immediately on the watchdog marker; it does not wait for SCM stop before releasing pressure. The parent independently checks memory every 50 ms, aborts on unknown memory or at 512 MiB, and kills pressure at a 65-second controller deadline. These polling limits cannot guarantee that unrelated OS activity never transiently crosses 512 MiB; any observed hard abort fails the probe.

Before opening the allocation gate, the parent assigns the exact child to an unnamed Windows Job Object with KILL_ON_JOB_CLOSE and no breakaway. Parent exit/handle close kills the assigned child; exceptions/Ctrl+C dispose the job, with an exact-created-process fallback kill if shutdown is unconfirmed. The pressure helper contains no SCM operation. A harmless live child fixture verified native job-close termination. Another integration fixture launched the real compiled helper with a pre-existing marker and simulated only the SCM boundary: the pressure child exited 0 without allocating, and the controller completed. Neither fixture proves a real low-memory service stop.

PASS attribution requires the exact run GUID/service marker, floor=768, observed available memory strictly between 512 and 768, stopConfirmed=true, watchdog exit 73, pressure exit 0, no hard abort/deadline, and independently observed isolated service Stopped. A controller fallback stop cannot qualify. The probe preserves the GUID registration, app, data and evidence on service-created paths; it does not automatically delete them. On failure, only the validated GUID test service may receive a bounded controller stop, explicitly recorded as fallback and FAIL. Protected HostAgent's PID is now checked in addition to state/configuration/image/assembly hashes so an unnoticed protected-service restart also fails validation.

Fresh verification:

- Windows PowerShell 5.1 `Invoke-Pester -Script tests/chaos/BoundedScmPressure.Tests.ps1 -EnableExit`: initial decision regressions failed **0 passed / 5 failed**, expected allocate-versus-abort/hold/release assertions; after implementation passed **5/5**. Stop-attribution regression then failed **6 passed / 1 failed**, and protected-PID regression later failed **8 passed / 1 failed** before their corresponding fixes. Final combined pressure tests passed **9/9**, exit 0. Native job and no-allocation launch integration tests were added after the initial implementation; no red-first claim is made for those two integration checks. The no-allocation launch initially exposed unreliable Console.Out hosting in Windows PowerShell (7 passed / 1 failed); the controller now uses the child's bounded exit protocol rather than stdout text.
- Windows PowerShell 5.1 `Invoke-Pester -Script tests/chaos/IsolatedScmSafety.Tests.ps1 -EnableExit`: **10/10**, exit 0 after the final runner changes. Tests never controlled real SCM services.
- VM-local SDK 10.0.401, process-scoped DOTNET_ROOT, external 768 MiB build/test watchdog: `dotnet build HostAgent.sln --configuration Release --no-restore -m:1 -p:UseSharedCompilation=false` exited **0**, **0 warnings / 0 errors**, minimum free RAM **2448.9 MiB**. Stdout SHA-256 `BE274C886EE455A685686F5477B11034CD8936C1A5B684F4F0CB65C38FEABBC9`.
- Same guarded environment: `dotnet test HostAgent.sln --configuration Release --no-build --no-restore -m:1 --logger "console;verbosity=minimal"` passed **375/375** (62 Core, 298 Infrastructure, 15 Desktop), exit **0**, minimum free RAM **1939.0 MiB**. Stdout SHA-256 `C1DC2AF9C7216E94B4168D335D237FF99F0CE39618FAC815A2EE16361A6F58DF`. Logs reside in `scm-root-acl-fix-20261017`. No watchdog abort occurred. The subsequent PID/preflight metadata-only PowerShell changes were covered by the final focused runs; product sources/binaries did not change.
- Final Windows PowerShell 5.1 read-only `& '.\scripts\chaos\run-isolated-scm-candidate.ps1' -WatchdogStopProbe` passed: **2491.7 MiB** free RAM, **29.49 GiB** free C:, candidate r4/manifest/helper hashes reported, protected HostAgent Running PID **3540**, all protected hashes matched. Zero isolated SCM services existed. `git diff --check` passed (tracked-diff whitespace check; new untracked files are not covered by that command).

Prepared tooling SHA-256: runner `E2D003A07C9B5786A3BC7684E8F19174562A7C590C54181557656D31D34BCA33`; pressure C# `177CA8300A55584494592B2F4E9F374B1D6C707858E7F4ED2CF9735AB2AAEE2F`; pressure controller `8739EDE4FDA2F14DBB2DD3A39CD7B84A056647FDDAABD03D4FCF4C013CA9C61D`; safety module `CC0CF6F85AEB40A12894F5CD7F910CFE8FD4D6F184DE886C2DD5780AA14A4BA8`.

One-shot operator command, elevated PowerShell inside HostAgent-DEV only:

```powershell
& 'C:\Users\AgentPCdevtest\Documents\ChatGPT\DevTest VM PC\HostAgent-DEV\scripts\chaos\run-isolated-scm-candidate.ps1' -WatchdogStopProbe -Execute
```

Expected successful evidence: scenario `isolated-scm-forced-watchdog-stop`, outcome PASS, memoryGuard.stopConfirmed=true, pressure.confirmed=true, pressure.hardAbort=false, controllerFallbackStopUsed=false, protectedServiceUnchanged=true, cleanup=`watchdog_probe_registration_package_and_data_preserved`. Preserve the complete printed result and retained paths; do not repeat a failed run or manually delete retained evidence before review. Actual forced watchdog service stop and retention remain **unverified pending this live operator run**. No crash/reboot/hard-power-off or other gate is closed by preparation.

### Live forced-watchdog SCM stop: PASS; retained subtree access limitation (fresh 2026-10-17, Europe/Prague)

The operator executed the prepared `-WatchdogStopProbe -Execute` command in the VM. Run **9aa86a18379245f2940c3224ae6c4967** returned PASS. Its preflight reported 2053.8 MiB free RAM, 27.96 GiB free C:, expected r4 manifest `0B42D4DCD736F2EBB3F7736464757E13C82CDEB83A95AA029F03BF12458367AB` and all three expected pressure helper hashes. Protected HostAgent's preflight PID was **3476**; the earlier 3540 PID belongs to a previous session and is not this run's baseline.

Codex freshly verified AGENTPC / VirtualBox / VBoxService Running, machine UUID `46543236-8C5E-49DA-953C-DB17F9B1E402`, account AgentPC\AgentPCdevtest, and read the stored result/marker at `C:\Users\AgentPCdevtest\AppData\Local\HostAgent-DEV-evidence\scm-candidate-9aa86a18379245f2940c3224ae6c4967`. SHA-256: result.json **B5BE4921EC21E471E8E6915D7EB4BBE2D2AFE5C496606A9682ED02A00DA8BFDF**; memory-floor-breach.json **CFB97EC3758E45DB83A4367D85D535BF12514D61F94783B90D2A4A394A5F95C3**. The actual Test-IsolatedWatchdogStopEvidence predicate accepted the stored matching run/service, stop confirmation, exit codes and observed memory; stored outcome/fallback/timeout fields were separately checked. No privileged operation or pressure run was performed by Codex.

Confirmed live results:

- SCM create and the single controller start exited 0. Initial isolated PID 5528; overview succeeded on attempt 1 in **205 ms**. The pre-pressure database check was healthy / quickCheck=ok, 3 migrations, 6 snapshots, 16 baseline rows, 212992 bytes. This is a pre-pressure DB check only; post-stop database integrity was not tested in this gate.
- At **2026-10-17T05:07:40.1511369Z** (07:07:40 Europe/Prague), the real watchdog recorded **766.2 MiB**, floor **768 MiB**, the exact service `HostAgentDevScm_9aa86a18379245f2940c3224ae6c4967`, action stop_isolated_test_service, **stopConfirmed=true**. Watchdog exit **73**, pressure exit **0** / marker, controllerFallbackStopUsed **false**. Thus stop attribution is the watchdog, not a controller fallback.
- Controller minimum observed free memory **758.2 MiB**, hardAbort=false, timeout=false, gateOpened=true, pressure.confirmed=true. Sampling does not establish an absolute continuous minimum. The 512 MiB abort was not triggered and its real low-memory intervention remains untested; no threshold was lowered.
- Fresh read-only Win32_Service inspection independently confirmed this exact isolated service remains **Stopped**, ProcessId=0, with its expected GUID image path and virtual service account. Registration was retained.
- Fresh Get-Service and Win32_Service agreed protected **HostAgent Running, PID 3476**, same as this run's preflight, original image path/account. Its executable and Core/Infrastructure/Service assembly hashes all matched the known protected baseline. Available memory had recovered to **2138.4 MiB** at the fresh post-check.
- Stored cleanup reports `watchdog_probe_registration_package_and_data_preserved`, with the expected GUID root/data paths. Fresh Get-Item confirms root `C:\ProgramData\HostAgent-DEV-SCM-9aa86a18379245f2940c3224ae6c4967` exists as a directory. Direct checks of its app/data subdirectories returned **UnauthorizedAccessException** under Codex's non-elevated token, consistent with the administrative root ACL. Earlier Test-Path false results accompanied access-denied errors and are **not evidence of missing directories**. Subtree/data/database retention is reported by the runner and supported by its no-delete path, but was not independently inspected after the stop. No ACL was changed to gain access and no retained artifact was removed.

The **real below-768 watchdog-stop / correct-service attribution / controller-free stop** gate is PASS. Independent retained-subtree inspection remains limited by elevation; post-stop database health and a live 512 MiB abort are not established here. No crash/reboot/hard-power-off or other chaos scenario started. Only this append-only record was changed for the result; no code change, rebuild, suite rerun or commit was made. Existing working-tree changes and HEAD `6cd0ec8ba61ce1c7c7cd89787f02f86b9b87ed8e` were preserved. Overall pilot verdict remains NOT READY because the other resilience gates remain open.

### Operator retained inventory confirmed; quick_check not executed due to PS 5.1 argument quoting (fresh 2026-10-17)

The operator supplied the output of the elevated read-only follow-up block. It confirms protected HostAgent Running PID 3476 and isolated run 9aa86a18379245f2940c3224ae6c4967 Stopped PID 0. The inventory shows retained app files/canaries and data/host-agent.db, host-agent.db-wal, host-agent.db-shm and notifications.db. Thus retained-subtree existence is now evidenced by the operator's elevated read-only inventory; file lengths were not visible in the formatted output and file hashes were not rechecked by that block.

The Python quick_check did **not** execute. Native argument passing of multiline Python via `-c` in Windows PowerShell 5.1 stripped string quotes, causing SyntaxError at the URI expression before any Python code executed. This is a diagnostic command defect, not evidence of database corruption. The read-only DB file handle was released by finally. No database integrity PASS is claimed.

A fresh harmless Windows PowerShell 5.1 fixture, after DEV VM identity validation, successfully passed quoted Python code through stdin (`$code | & <verified python.exe> -I -B - <argument>`), returning stdin_quoting_ok and preserving an argument containing spaces. It opened no database and wrote no files. The follow-up command will use stdin instead of -c; its real quick_check remains pending. No service operation, cleanup, ACL or source change was made.

### Operator post-watchdog immutable read-only quick_check: PASS (fresh 2026-10-17)

The operator supplied two completed executions of the corrected stdin-based Python check in elevated PowerShell inside the DEV VM. Each printed **quick_check=ok** and returned to the prompt without the wrapper's failure exception. This evidence is operator-provided console output, not a Codex-run database check or a newly saved/hashed result artifact.

Exact database: `C:\ProgramData\HostAgent-DEV-SCM-9aa86a18379245f2940c3224ae6c4967\data\host-agent.db`. Verified existing tool path used: `C:\Users\AgentPCdevtest\.cache\codex-runtimes\codex-primary-runtime\dependencies\python\python.exe`, invoked as `$check | & $python -I -B - $db`. The supplied block validated VM identity, elevation and HostAgent Running / isolated GUID service Stopped before checking. It held an OS read-only FileShare.Read handle and connected using `mode=ro&immutable=1`, connection-local query_only=ON and temp_store=MEMORY, then executed only PRAGMA quick_check with a progress-handler deadline. The successful path also establishes that the nonempty WAL/journal rejection guard did not fire in these executions; it does not provide exact sidecar sizes. No ACL/service mutation, checkpoint, recovery, deletion or database write was requested by the block.

Together with the earlier elevated retained-file inventory and stored watchdog evidence, this closes the **retained database existence and post-watchdog quick_check** follow-up as PASS for run `9aa86a18379245f2940c3224ae6c4967`. Earlier quoting failure is preserved above and superseded only by these successful checks. This does not establish semantic row preservation, notifications.db integrity, a live 512 MiB abort, crash/reboot recovery or endurance. No broader resilience readiness upgrade is claimed.

Codex verified DEV VM identity before this append-only documentation update. HEAD remains `6cd0ec8ba61ce1c7c7cd89787f02f86b9b87ed8e`. No database test was repeated by Codex, no retained artifacts were removed, no product source was changed and no build/test suite or commit was created for this result.

### Operator notifications.db read-only quick_check: PASS (reported 2026-10-17)

The operator reported "Quick check ok" after receiving the prepared elevated read-only command for `C:\ProgramData\HostAgent-DEV-SCM-9aa86a18379245f2940c3224ae6c4967\data\notifications.db`. This is an operator-reported successful result; a complete console transcript or saved/hashed artifact was not supplied for this particular check, and Codex did not execute it independently.

The supplied command uses the verified bundled Python path, stdin code delivery (`-I -B -`), read-only/immutable SQLite URI, query_only=ON, temp_store=MEMORY, a read-only OS file handle and a query progress deadline. Its preconditions require the approved DEV VM, elevation, protected HostAgent Running and the isolated GUID service Stopped; it rejects nonempty WAL/journal before opening SQLite. It requests no service operation, ACL change, repair, deletion, checkpoint or database write.

notifications.db quick_check is recorded as PASS on operator-reported evidence. Together with the previously supplied host-agent.db successful transcript, both retained databases now have reported successful post-watchdog quick_checks. These checks establish no semantic notification delivery/state guarantee and do not close crash/reboot, endurance, live 512 MiB abort or other open resilience gates. Overall pilot verdict remains NOT READY.

Only this append-only record was updated after DEV VM identity validation. No source/service/data change, cleanup, repeated test, build or commit was performed by Codex.

### Separate controlled SCM crash/recovery gate prepared, NOT EXECUTED (fresh 2026-10-17)

The existing operator-reported notifications.db read-only quick_check=ok entry above remains valid; no repeat check was performed. Fresh DEV identity validation confirmed AGENTPC / AgentPC\AgentPCdevtest / VirtualBox / VBoxService Running / UUID 46543236-8C5E-49DA-953C-DB17F9B1E402. Initial free memory was 2349.7 MiB and C: free 27.92 GiB. HEAD remains 6cd0ec8ba61ce1c7c7cd89787f02f86b9b87ed8e; pre-existing dirty work was preserved.

Source assessment: the existing SCM runner only covered graceful lifecycle and watchdog stop. The older IsolatedPersistenceProbe holds an uncommitted transaction in a disposable non-SCM process; it does not prove SCM-service recovery. A separate runner and explicit test-host mode were therefore prepared. The original r4 runner, published candidates and preserved watchdog GUID package were not overwritten or cleaned up. No production service source or protected installed binary/configuration was changed.

Changed/new preparation files:

- tests/HostAgent.PipeProbe/IsolatedScmServiceProbe.cs: only an explicit fourth `crash-proof` argument registers the new test worker; the ordinary three-argument test-host path remains available.
- tests/HostAgent.PipeProbe/IsolatedScmCrashProof.cs and IsolatedScmCrashWorker.cs: bounded test-only transaction proof inside both private databases.
- tests/HostAgent.Infrastructure.Tests/IsolatedScmCrashProofTests.cs: actual SQLite commit/rollback visibility and worker arm/restart behavior.
- scripts/chaos/run-isolated-scm-crash-candidate.ps1, IsolatedScmCrashSafety.psm1, Invoke-IsolatedScmCrash.ps1 and check-isolated-scm-crash.py: separate isolated controller, exact process guard and read-only recovery checker.
- tests/chaos/IsolatedScmCrashSafety.Tests.ps1 and test_isolated_scm_crash_check.py; this append-only record.

Planned one-shot flow (not live evidence):

1. Validate approved VM, administrator token, free RAM >=1024 MiB and C: >=20 GiB; verify r5 manifest and protected HostAgent's currently observed Running/Auto/virtual-account/path/PID/image and assembly hash baseline. Create only a fresh HostAgentDevScm_<GUID> service, private ProgramData GUID root/data, virtual service account and authenticated overview-only GUID pipe. Reuse the established administrative-only root / app read-execute / private-data-write ACL rules. Existing watchdog GUID 9aa86a18379245f2940c3224ae6c4967 is never a target.
2. Start the fresh service, validate View/database readiness and canaries, then arm its test-only worker through a private data flag (no new IPC mutation operation). In each database, create a small dev_scm_crash_proof fixture table, synchronously commit a run-specific `committed` row, then insert a `pending` row inside an explicitly uncommitted held transaction. Only after both transactions exist, write a bounded readiness record with the run identity and 1..64 already-committed telemetry snapshot IDs. No raw telemetry, notification payload or secrets are captured. Arm wait is 90 seconds; DB availability wait 40 seconds; held transactions expire after 60 seconds. Graceful cancellation/expiry writes an invalidation flag and cannot count as crash success.
3. The controller allows <=25 seconds for the barrier. Immediately before injection it validates the service name/account/manual configuration/exact image and command line, PID distinct from the current protected PID, matching Win32_Process identity and start time. It opens and retains the process handle before the final check. Termination targets that exact Process object/handle, never a process-name search, process-tree kill or host/VM power operation. Require observed process exit and SCM Stopped.
4. Perform exactly one controller-managed start of the same isolated service, require a new PID and View/database readiness. This is **controlled recovery**, not verification of automatic SCM recovery policy; no recovery policy is changed. The test worker must not recreate or re-commit its proof on restart. Stop the isolated service gracefully and require the original readiness record unchanged and no expiry flag.
5. Hold read-only OS handles to both database files. Feed the checker to the verified Python through stdin (`-I -B -`), never -c. It uses SQLite mode=ro&immutable=1, query_only=ON, temp_store=MEMORY and bounded quick_check; nonempty WAL/journal is a blocker, not a reason to ignore it or repair/checkpoint. Require quick_check=ok in BOTH databases, exactly the committed proof present, pending proof absent, and every captured committed telemetry snapshot ID retained. Output is bounded metadata/booleans only.
6. Final protected state/PID/configuration/hash check must match this run's baseline. Preserve the new test registration, package/data and evidence, stopped on success. On failure, preserve evidence and attempt only a bounded identity-checked stop of the isolated service; unconfirmed stop is reported for manual recovery. No silent retries, repair or deletion. The existing 768 MiB service watchdog remains active; a memory breach fails this crash gate. No pressure workload, threshold reduction, host operation, VM reboot or hard power-off is included.

Limits: the injected interruption is during known test-only open transactions, not a deterministic physical torn-sector or fsync interruption. The committed proof rows test each database independently; they do not assert cross-database atomicity. Captured telemetry IDs prove retention of those confirmed rows, not all baseline/incident semantics. notifications.db fixture proof/quick_check does not prove external notification delivery or exactly-once delivery. The expiry/restart fixtures use graceful cancellation rather than a real SCM crash; the actual crash/recovery is still unverified until the operator runs the new gate.

Fresh verification, all in DEV VM:

- Regression first: crash-proof focused test exited 1 with SQLite no-such-table dev_scm_crash_proof in valid disposable initialized databases; no proof had yet been implemented. Captured stdout SHA-256 7121D952AA390D9FC835238F346E1E46E462965839F5C6A29A0D0C40F0176D8A, minimum free RAM 2066.6 MiB. After implementation, combined IsolatedScm tests passed 12/12; expanded worker-arm/restart coverage then passed **13/13**, exit 0, minimum 2163.6 MiB, stdout SHA-256 030E5739569DD43A2C7637536F7D58D4088190CC237D6BC482346A6508EA30A0. Command: `dotnet test tests/HostAgent.Infrastructure.Tests/HostAgent.Infrastructure.Tests.csproj --configuration Release --no-restore --filter FullyQualifiedName~IsolatedScm -m:1 -p:UseSharedCompilation=false --logger "console;verbosity=minimal"`.
- Exact process selection Pester regression initially failed 0 passed / 1 failed because the valid target was rejected by the initial fail-closed stub. After implementation it passed, including rejection of protected PID, mismatched PID, HostAgent name and wrong image. Final Windows PowerShell 5.1 combined command `Invoke-Pester -Script @('tests/chaos/IsolatedScmCrashSafety.Tests.ps1','tests/chaos/IsolatedScmSafety.Tests.ps1','tests/chaos/BoundedScmPressure.Tests.ps1') -EnableExit` passed **20/20**, exit 0. No real service was controlled by these tests.
- Verified Python `-I -B tests/chaos/test_isolated_scm_crash_check.py`: **4/4**, exit 0, disposable fixtures only. Tests exercise both healthy database proofs with unchanged file inventory/hashes, reject a committed pending row, reject loss of an acknowledged snapshot and reject nonempty WAL. Initial test teardown encountered fixture connection handles left open; explicit connection closing fixed the test lifecycle and the checker closes its read-only connections explicitly. These checker integration tests were added after implementation; no red-first claim is made for all checker cases.
- Guarded VM-local SDK 10.0.401 with DOTNET_ROOT and external 768 MiB abort watchdog: `dotnet build HostAgent.sln --configuration Release --no-restore -m:1 -p:UseSharedCompilation=false`: exit 0, **0 warnings / 0 errors**, minimum free RAM **2358.0 MiB**; stdout SHA-256 862059412C4DA258F64C7EA614F3A2D2F1BBAFE1A9ED7BC9886E357DE50E0E6B.
- Same guarded environment: `dotnet test HostAgent.sln --configuration Release --no-build --no-restore -m:1 --logger "console;verbosity=minimal"`: exit 0, **377/377** (62 Core / 300 Infrastructure / 15 Desktop), minimum free RAM **1956.1 MiB**, stdout SHA-256 2D9F595D9752B1682D10246EDDFCF40414F1D2B93BD85AF5FC95A517A12181B5.
- New, previously absent r5 publish: `dotnet publish tests/HostAgent.PipeProbe/HostAgent.PipeProbe.csproj --configuration Release --no-restore -m:1 -p:UseSharedCompilation=false -o <stable Documents r5 path>`: exit 0, minimum free RAM **2186.6 MiB**, stdout SHA-256 6F20B8878F1143D60A919C9519722D94D7DE89F841B655901594FEFC62305FB2. No memory watchdog fired. Build/test/publish logs remain in scm-root-acl-fix-20261017. Subsequent controller-only read-lock handling and manifest pin changes do not change product/test-host assemblies; final parser/Pester checks passed.

Candidate: `C:\Users\AgentPCdevtest\Documents\ChatGPT\DevTest VM PC\HostAgent-DEV-SCM-candidates\scm-release-candidate-20261017-r5`, **73 files**, manifest SHA-256 **EBBAD0807899BB8243AE71B2877C4C02B4C3F6024024EE748A179342428CAAAB**. PipeProbe DLL **E0BA2494907A7CAEBA385B8D615077654B638318F9E28EC32CF1AE63206C5B77**. Core/Infrastructure/Service assembly hashes match r4; only the test-host assembly and corresponding symbols changed. The separate crash runner pins r5. r1-r4 remain preserved.

Read-only Windows PowerShell 5.1 preflight of the new runner passed: **2380.2 MiB** free RAM, **27.88 GiB** free C:, candidate manifest/inventory/product hashes matched; protected HostAgent Running PID **3476**, original protected hashes matched. Helper hashes: crash safety B53D8651D3B621F33DBD5FEE3CB77420ED19CE65BDF78813E6A42421688D6E56; controller B2C19705A39A43163695710173D1218AD8956186D3DB35EB1EA6B255B1EE4D7D; Python checker 9D7A0BDD782DA9A3C16A678EF21A51F413E4E72F7117DB7E4CA2261CFEA9151A. Runner hash E5EAA4D7E27BFA935ADE2EE623F75099C3307FE90AABEC03C8B9C431C95FED20. Parser and git diff --check passed; the latter covers tracked diff only. Final read-only SCM state: protected Running PID 3476, prior watchdog GUID service Stopped PID 0; no new crash service was created.

Prepared one-shot command, elevated PowerShell inside HostAgent-DEV only:

```powershell
& 'C:\Users\AgentPCdevtest\Documents\ChatGPT\DevTest VM PC\HostAgent-DEV\scripts\chaos\run-isolated-scm-crash-candidate.ps1' -Execute
```

Preparation is verified; actual SCM crash/recovery remains **NOT RUN / OPEN**. No -Execute, real service crash, SCM operation or new actual memory-pressure injection was performed by Codex, and no commit was created. Pilot verdict remains NOT READY. Preserve complete result JSON and paths after the manual run; on FAIL do not repeat or delete retained evidence before review.

### Operator-run isolated SCM crash/recovery result (2026-10-17)

The operator ran the prepared command once in AGENTPC / VirtualBox as `AgentPC\AgentPCdevtest`. The stored artifact `%LOCALAPPDATA%\HostAgent-DEV-evidence\scm-candidate-3f161b1bbfde4f7e855615b16b9d7b72\result.json` was read and its SHA-256 recomputed as `09C000F0582BF3B29119C04BF7314BED11B790DA7596D39A5C0FDF24DECC2FA5`. Its scenario and run ID match this record and the supplied console output; outcome is `PASS`, with null failure code/reason. The candidate manifest hash in the artifact matches the independently hashed r5 manifest: `EBBAD0807899BB8243AE71B2877C4C02B4C3F6024024EE748A179342428CAAAB`.

The exact recovery mechanism recorded by the crash checker is **`controller_start`**. The controller started the isolated service after killing PID 9852; the recovered process was PID 896. `controllerFallbackStopUsed` is `false`. Therefore this run verifies controller-directed restart after a controlled process crash; it does **not** verify automatic SCM failure-action recovery. The recorded isolated SCM start/start/stop operations all returned 0, both view stages succeeded (148 ms and 142 ms), and the two database checks report `quickCheck=ok`, committed proof preserved, uncommitted proof absent, and acknowledged snapshots preserved. These database statements are credited to the saved checker result; the unelevated verification session could not independently read the retained ProgramData package/data directory. The fixture package and data were intentionally preserved, and the crash test was not repeated.

Fresh read-only SCM inspection after the run found protected `HostAgent` **Running**, PID **3476**, with image `C:\Program Files\HostAgent\Service\HostAgent.Service.exe`; its image hash still matches the pinned protected image hash `04DF669D662BB08BDBD76653E46158E3D701D7E0D48653439F8F836BB417CB87`. The isolated fixture service is **Stopped**, PID 0. No service configuration was changed. The candidate manifest hash and the three helper hashes match the supplied preflight/result output.

This result closes the prepared isolated controlled-crash/controller-recovery gate only. Remaining gates include a distinct isolated test of **automatic SCM failure-action recovery**, the unresolved intermittent live View availability/diagnostics, and broader resilience cases not covered by this fixture (for example real VM power loss/reboot and live storage/remediation behavior). The resilience-qualified pilot verdict remains **NOT READY**. Recommended next standalone step: design and run a separate disposable-service test that proves automatic SCM failure-action recovery, keeping the protected HostAgent service untouched.

### Single operator DEV resilience campaign prepared, NOT EXECUTED (fresh 2026-10-17)

Prepared files: `scripts/chaos/run-dev-resilience-campaign.ps1` (main operator entry), `DevResilienceCampaign.psm1` (sequential execution, bounded native children, transport cycles), `dev-resilience-campaign-manifest.json` (pinned helper/tool catalog), and `tests/chaos/DevResilienceCampaign.Tests.ps1`. Existing source/work and all retained runs/candidates were preserved. No production source/assembly changed in this step, no commit or host-project transfer occurred.

The latest record changed during preparation: stored r5 controlled crash result `3f161b1bbfde4f7e855615b16b9d7b72` already reports PASS. Codex independently read this result and verified SHA-256 **09C000F0582BF3B29119C04BF7314BED11B790DA7596D39A5C0FDF24DECC2FA5**, matching run/scenario/r5 manifest, controller_start recovery, distinct PIDs 9852/896, no controller fallback, and both database proof/quick_check flags. Therefore the main script does **not repeat this proven crash**. Its S1 phase only revalidates the pinned stored evidence and requires the retained test service still Stopped. PASS there means read-only revalidation of an existing live result, never a newly executed crash or automatic SCM failure-action recovery.

The main script itself was **not invoked**, with or without -Execute. No new SCM test, View fault scenario, endurance campaign or physical memory pressure was executed by Codex. Only focused tests with harmless subprocesses/synthetic fixtures and separate read-only artifact/environment checks were performed.

Prepared runtime phase plan:

| Phase | Operator-script behavior / scope |
| --- | --- |
| S0 harness-ready | Validate/compile the existing bounded process-lifetime helper; memory-pressure mode is never called. |
| S1 isolated SCM crash | Read-only revalidation of the pinned already-PASS run above; zero new crashes/recovery starts. Changed/missing evidence or a non-Stopped retained service fails closed. |
| S1 automatic SCM recovery | NOT RUN, manual preparation required: separate GUID service with reviewed bounded failure actions and no controller fallback start. Protected HostAgent recovery settings must not change. |
| S2 live storage faults | NOT RUN: current candidate has no reviewed live storage-failure injector. Prepare a bounded disposable fixture/volume and runtime observations; never fill C: or modify preserved databases. Existing SQLite unit fixtures are not relabeled as new live coverage. |
| S2 collector faults | NOT RUN: no private per-collector injection seam. Prepare a test-host-only substitution; never disable global WMI/EventLog or OS security. |
| S2 remediation faults | NOT RUN: current test host intentionally uses DenyHelper. A separate reviewed GUID-scoped helper/harmless target is needed; protected helper endpoints must not be used. |
| S3 View transport recovery | One fresh GUID fixture, six requests, one deliberately stalled first dispatch followed by five successes, healthy final fixture quick_check. Scope is transport, not live WPF. |
| S3 bounded transport endurance | Three further fresh GUID timeout/recovery cycles, six requests each, bounded resource/storage observations. Fresh-process repetition does not prove long-lived full-runtime leak stability. |
| S3 WPF / full-runtime concurrent endurance | NOT RUN; needs an isolated dashboard endpoint and a separately reviewed bounded full-runtime schedule/UI observation. |
| S4 physical 512 MiB boundary | NOT RUN. Deliberately reaching it would cross the unchanged 768 MiB safety abort. No physical memory-pressure workload is enabled; synthetic abort-logic coverage is not a live-floor claim. |
| S5 reboot/hard power-off | NOT RUN, always separate manual gate. Require fresh host-side VirtualBox VM/snapshot verification, preserved clean-win-base and agreed disposable persistence fixture before any separate operator action. Guest script has no power-control invocation. |

Known manual-only phases are explicitly NOT RUN and do not masquerade as successes. They may be bypassed only to run the declared independent transport checks; they remain open and make the overall report INCOMPLETE. Any automatic phase failure, missing positive success evidence, changed protected identity/hash, resource abort or uncertain runtime state stops all subsequent phases. There is no automatic retry/resume. The engine saves a started-but-unverified state before invoking each action, and preserves PASS/FAIL/NOT RUN plus manual instructions for skipped phases.

Safety and evidence:

- Approved hostname/model/Guest Additions/machine UUID/account and standard VM system/evidence roots are checked. Administrator token is required for -Execute. Initial free RAM >=1024 MiB, C: >=20 GiB; running abort 768 MiB and hard abort 512 MiB are unchanged. No deliberate pressure is generated.
- Protected HostAgent baseline includes identity, Running/Auto state, virtual account, exact image path, current PID and four pinned binary hashes. Full checks run between automatic phases; state/PID is checked at five-second intervals during children. Local CIM calls specify a five-second operation timeout. Candidate's 73 manifest-listed files, exact inventory, no-reparse/path constraints, helper catalog and Python executable identity are checked.
- Native children are exact created processes in parent-held kill-on-close jobs. Guards poll at 250 ms. Cleanup grace is at most 12 seconds, then bounded termination wait five seconds; hard-floor abort skips the grace. These are process supervision limits, not a guarantee of immediate OS scheduling or permanently nonblocking filesystem APIs.
- View server lifetime 60 seconds, round active deadline 70 seconds, client deadline 45 seconds; sampled server working-set cap 256 MiB; each private fixture cap 16 MiB / eight files. Endurance is three cycles / <=210 seconds active budget plus bounded failure cleanup. Data and logs use fresh GUID paths only. No prior artifact or fixture is deleted.
- Per-run summary.json plus per-phase phase.json record status, reasons, limits, campaign/subject IDs, UTC times, exact log paths and bounded result metadata. Transport logs include exact child PIDs and stable diagnostics; no DB contents, raw telemetry/XML or secrets are logged. Streams are capped at 512 KiB each. Artifacts are only in the new guest evidence GUID directory / fresh View fixture GUID directories. Root status is FAIL on error, otherwise INCOMPLETE while manual gates remain open.

Fresh preparation verification:

- Initial module regression: **0 passed / 3 failed**, exit 1, expected missing sequence statuses. Green tests then verified fail-stop, persistence of statuses, explicit manual skips and rejection of missing success evidence/guard failures.
- View result predicate regression: **0/1**, exit 1 (valid recovery initially rejected), then passed after implementation; repeated failures/unhealthy fixture cannot pass. Stored controlled-crash proof predicate similarly failed **0/1** before implementation, then passed, rejecting missing proof and controller fallback.
- Final Windows PowerShell 5.1 focused command: `Invoke-Pester -Script @('tests/chaos/DevResilienceCampaign.Tests.ps1','tests/chaos/IsolatedScmCrashSafety.Tests.ps1','tests/chaos/IsolatedScmSafety.Tests.ps1','tests/chaos/BoundedScmPressure.Tests.ps1') -EnableExit`: **31/31**, exit 0. This includes 11 campaign tests and 20 existing safety tests. Tests exercise harmless native child output/timeout/job shutdown, a synthetic hard-floor guard (no memory depletion), preserved fake-server failure logs, caller callback scope, exact manifest inventory/path traversal rejection, crash proof interpretation and sequential fail-stop. Native/fake-server/manifest integration coverage was added after initial implementation; not all cases are claimed red-first.
- The final main-only addition of explicit CIM operation timeout parameters/common cleanup-limit metadata passed parser checks; the extracted artifact guard test passed **1/1** after the timeout metadata change. The last service-query timeout addition also passed final parsing. The main body, environment gate sequence and operator fault schedule were not executed by these tests.
- Separate read-only checks verified **8 helper hashes**, verified Python executable SHA-256 **B07B03B9BA866A341A3C13EE69710FBC8D43D4CE2523DEE13F6DF5C25378D15B**, and all **73** candidate manifest-listed file hashes. r5 manifest remains **EBBAD0807899BB8243AE71B2877C4C02B4C3F6024024EE748A179342428CAAAB**. Catalog SHA-256 **6085248AC53F52E132A91DF8F9BA738407B37327CED213448FF47D548B98C6A9** is pinned in the main script; final main SHA-256 **61FD6A319F179F11CF605FF36138C4AABF100561A49BCC1B5DA317B17A33BB7D**.
- Latest separate read-only resource/protection check: **2322.1 MiB** free RAM, **27.82 GiB** free C:, protected HostAgent Running PID **3476**, original virtual account/path, all **four** protected hashes matched. Retained watchdog and controlled-crash GUID services were Stopped PID 0. No service operation or policy change occurred. A policy query was read-only; the launch command adds no execution-policy bypass.
- git diff --check passed for tracked diff (new untracked files are outside that check). Current HEAD remains **6cd0ec8ba61ce1c7c7cd89787f02f86b9b87ed8e**. No new .NET Release build/suite was run for this PowerShell-only packaging step; the existing r5 **377/377** Release evidence above is historical and unchanged, not a fresh run.

One operator command, already-elevated PowerShell inside HostAgent-DEV; a clean child avoids inherited profile/type state:

```powershell
& 'C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe' -NoProfile -NonInteractive -File 'C:\Users\AgentPCdevtest\Documents\ChatGPT\DevTest VM PC\HostAgent-DEV\scripts\chaos\run-dev-resilience-campaign.ps1' -Execute
```

Copy back the complete console result and the contents of the printed summary.json path. On failure also retain the referenced phase.json/child/server stdout/stderr logs; do not delete evidence, repeat the campaign, repair data or perform manual power operations before review. Campaign runtime results remain **NOT RUN** in this preparation entry; overall pilot verdict remains NOT READY.

### Operator campaign result independently inspected (2026-10-17)

The operator executed the command above in an elevated VM PowerShell and supplied its final output. Codex did not execute or repeat the campaign. Fresh read-only identity checks confirmed AGENTPC / AgentPC\AgentPCdevtest / VirtualBox / VBoxService Running / UUID 46543236-8C5E-49DA-953C-DB17F9B1E402. HEAD remains 6cd0ec8ba61ce1c7c7cd89787f02f86b9b87ed8e and existing dirty work is preserved.

Stored evidence: `%LOCALAPPDATA%\HostAgent-DEV-evidence\campaign-18fbc36977b94369836e2b87395aa043\summary.json`, SHA-256 **75DF69B9CBDAD9717F76D3098AF78CED95CE4EA51643859D540174918A21106C**, independently read and matched to the operator output. All eleven stored phase.json statuses matched the summary. Recorded execution spans 11:07:42–11:09:04 UTC (about 82 seconds). Overall status is **INCOMPLETE**, with **4 PASS / 0 FAIL / 7 NOT RUN**.

- S0 harness readiness PASS, no memory pressure enabled.
- S1 controlled-crash evidence revalidation PASS: existing run 3f161b1bbfde4f7e855615b16b9d7b72, result SHA-256 09C000F0582BF3B29119C04BF7314BED11B790DA7596D39A5C0FDF24DECC2FA5. This campaign did not inject another crash. Existing evidence distinguishes committed proof retained from pending proof absent in both databases; recovery remains controller_start, not automatic SCM recovery.
- S3 View transport recovery PASS: one new isolated cycle, six requests, one expected injected failure followed by five recovered requests, fixture quick_check=ok. Reported round elapsed 20.5 seconds; sampled peak working set 50,884,608 bytes; fixture 180,245 bytes.
- S3 bounded transport endurance PASS: three additional fresh isolated cycles, each six requests / one expected failure / five recovered requests / quick_check=ok. Reported elapsed 16.2, 16.5 and 16.2 seconds; sampled peak working sets 50,315,264 / 50,216,960 / 50,233,344 bytes. Each fixture was 180,245 bytes. Combined fresh transport coverage is 24 requests, four expected failures and twenty recovered responses. Sampling across short fresh processes does not establish long-running full-agent leak stability.

Seven gates remain NOT RUN: automatic SCM recovery; live storage faults; private collector faults; real isolated remediation faults; live WPF/full-runtime endurance; physical 512 MiB boundary; manual VM reboot/power-off. Their preparation blockers and manual steps above remain applicable. In particular, the physical 512 MiB test must not cross the unchanged 768 MiB safety stop, and no guest power operation is authorized by this campaign.

This run's protected baseline was Running PID **3840**, not the older preparation PID 3476. Fresh post-result read-only Win32_Service inspection confirms Running PID3840; all four protected binary hashes match the run baseline. The successful phase guards report no protected identity mismatch. No service operation, ACL/configuration change, source change, cleanup, new build/test suite or commit was performed during this evidence review; only this record was appended. Existing r5 Release 377/377 remains historical. Verdict remains **NOT READY** for pilot while the required operational gates above are open.

### Remaining independent gates: operator script prepared, NOT EXECUTED (2026-10-17)

The user requested continuation after individual scenario failures. The new separate `scripts/chaos/run-remaining-dev-resilience.ps1` implements that request without changing the earlier stop-first runner or its published candidates. A failed scenario remains FAIL and the next independent fixture can run **only after positive quiescence and safety checks**. Resource/identity/artifact guard failures are latched; uncertain cleanup skips the remaining workload. There is no threshold reduction, automatic VM power action, production transfer, commit or protected HostAgent service/configuration/data mutation. The old dirty work remains preserved at HEAD 6cd0ec8ba61ce1c7c7cd89787f02f86b9b87ed8e.

Fresh identity validation: AGENTPC / AgentPC\AgentPCdevtest / VirtualBox / VBoxService Running / UUID 46543236-8C5E-49DA-953C-DB17F9B1E402. Final read-only preflight passed: **2164.1 MiB** free RAM, **28.46 GiB** C: free, protected HostAgent Running PID **3840**, Auto, NT SERVICE\HostAgent, original exact quoted image path and all four pinned binary hashes. Guards remain initial >=1024 MiB, running abort <768 MiB, hard floor <=512 MiB and C: >=20 GiB. No new elevated fault gate was executed by Codex.

Preparation files: run-remaining-dev-resilience.ps1, RemainingResilience.psm1, remaining-resilience-manifest.json, run-isolated-scm-auto-candidate.ps1, Invoke-IsolatedScmAutoCrash.ps1, IsolatedScmRecovery.cs, Invoke-RemainingFixtureTests.ps1, check-remaining-runtime.py; corresponding PowerShell/Python tests. Test-only C# additions are IsolatedCollectorFaultJob and its test, ChaosFixtureRetention and its test, an explicit `isolated-runtime <GUID> resilience` mode, and additional synthetic WPF outage/recovery assertions. Existing fixture cleanup now honors an explicitly scoped operator scratch root; ordinary tests still clean up their own fixtures. WPF captures use a new GUID directory per test process. No product class under src was changed in this preparation step.

Prepared automatic phases, not an assertion that their complete operator schedule ran:

1. **Live automatic SCM recovery**: fresh GUID service only, with the unchanged r5 product/test-host candidate and its existing app read-execute/data-write isolation. Native policy is read back as exactly one restart after 5000 ms followed by no action; no reboot/command action is configured. Kill uses the existing exact held-process identity guard and committed/pending proof barrier. There is no controller recovery start. Require new PID, View recovery, acknowledged rows preserved/pending transactions absent and read-only quick_check of both private databases. Before final stop, disable failure actions and set **only this test registration** Disabled so a queued recovery cannot create a later workload. Preserve registration/app/data. The parent positively verifies Stopped/PID0/Disabled/no recovery actions; bounded identity-checked fallback cleanup cannot turn a failed scenario into PASS. Native policy writes and this automatic-recovery path remain **live NOT RUN** pending operator execution.
2. **Storage fixtures**: real tiny SQLite corruption rejection, engine page-limit SQLITE_FULL/rollback and cooperative uncommitted transaction interruption. This does not prove a physically full Windows volume or live-runtime storage fault recovery.
3. **Collector contracts**: real coordinator and durable store with injected failed peers/malformed evidence and recovery after backoff. Windows WMI/EventLog services are never disabled.
4. **Remediation contracts**: real dispatcher, approval/audit journal, failure/timeout/indeterminate/no retry, replay/rate/cooldown behavior, **fake executor only**, no protected helper or real privileged operation.
5. **WPF outage/recovery**: actual WPF window with synthetic transport, stale/unavailable data clearing and three fresh recovery cycles. This does not prove the combined WPF-to-GUID-pipe path.
6. **Private full runtime**: a new test-only Release harness mode runs the existing runtime graph with real Windows collectors, private GUID View/SQLite, Shadow/read-only, DenyHelper and no outbound transport. Its explicit test sampling interval is 10 seconds (production default is unchanged); inject two failures only into its CPU collection job, then require two actual successful recoveries after the real coordinator backoff, >=10 View reads, >=3 CPU samples, zero View failures/helper calls and no guard abort. Active duration 190 seconds, child deadline 240 seconds, private fixture cap 32 MiB/64 entries. After process exit, hold read-only OS handles and run immutable/query_only quick_check of fixture.db and notices.db; reject nonempty WAL/journal. This mode remains **live NOT RUN** until operator execution. The wrapper unit tests alone do not prove runtime wiring or long-term stability.

The plan has eleven rows (six automatic, five explicitly NOT RUN/manual): live runtime storage/real privileged helper, combined WPF/private IPC, long-duration qualification, physical 512 MiB pressure, and VM reboot/power-off. Existing fixture and transport evidence does not silently close these gates. Physical depletion to 512 MiB would violate the 768 MiB stop. VM power remains a separate manual gate after fresh VirtualBox-host rollback/identity confirmation; no guest script schedules it. Pilot verdict remains **NOT READY**.

Children are exact created processes in kill-on-close jobs; the parent polls guards at 250 ms and checks protected PID/state at five-second intervals. Existing bounded cleanup grace is 12 seconds (skipped at hard floor), then five-second termination wait; unexpected surviving dotnet/testhost/PipeProbe PIDs block continuation. SCM services are outside ordinary child jobs, so the new parent additionally validates and stops/disables only its own fresh GUID registration when needed. If the controller/console or guest itself is forcibly terminated, no complete cleanup or final result is claimed; retained GUID state must be inspected before another run. Keep the operator console open. No scenario is retried automatically. Every phase has a log directory/status/limits; the main prints RUN and completion progress and final summary path/hash. Logs and new fixture databases are retained, including failures. No earlier evidence/package is deleted or reused.

Fresh verification actually performed:

- Sequence regression: old engine produced **FAIL,NOT RUN** instead of the required **FAIL,PASS**, Pester exit 1 (0/1), before the new engine implementation. SCM policy predicate regression rejected missing bounded policy, exit 1 (0/1), then passed after implementation. Additional safety/result tests were added after implementation; no claim that every case was red-first.
- Final new Windows PowerShell 5.1 focused Pester: `Invoke-Pester -Script @('tests/chaos/RemainingResilience.Tests.ps1','tests/chaos/IsolatedScmRecovery.Tests.ps1') -EnableExit`: **9/9**, exit 0, under the bounded child/memory guard. Evidence `remaining-pester-f539540bac5d4bd58408697a6a183541`. Tests cover continue after scenario failure, stop after unconfirmed cleanup/guard failure, sticky transient safety abort, additional cleanup not counted as PASS, zero/failed test-result rejection and rejection of controller fallback as SCM recovery. No SCM native write was called. An earlier combined invocation passed **38/38** before two additional sequence tests. A guarded PS5 launch from Codex's raw .NET process initially inherited PS7 module search paths and could not discover Get-FileHash (8/9, then setup failure); matching that child process's PSModulePath to standard Windows PowerShell module paths resolved it. No machine/user policy or persistent environment was changed. The operator command uses the standard Windows PowerShell executable; its real read-only preflight passed separately.
- Private collector injection regression: the initial stub did not throw when a failure was requested (0/1, exit 1, stdout SHA-256 **9E2DEAF833B5B41B572F6186635BA139EC2D291B5B639F7CE20E32837ED5656C**); green **1/1**, stdout SHA-256 **4638E82E6755FFBB9FBA6E48660221AD7AA6E5E04575D39F7FE554609995A9B2**. A prior compile error for an unused stub parameter was setup failure, not counted as the red assertion.
- Fixture retention regression: requested retained file was incorrectly deleted (0/1, exit 1), stdout SHA-256 **9FAC252743FDD5413961EE58D4522B960A41F97EB5061765A998839F496B895D**. After scoped cleanup implementation, relevant .NET focused tests **23/23**, exit 0, minimum free **1808.6 MiB**, stdout SHA-256 **70902E3629E2831B5FF7F9BE630EE979443B231DA0DBD1BE300424EDB0AE469C**. This includes retention, injected collector, storage, coordinator and remediation cases.
- WPF synthetic outage/recovery focused test **1/1**, minimum free **1798.6 MiB**, stdout SHA-256 **484848AC99DC8CDA8D629B0D8B2AB2F73487564DD8A7A3CE22C8813471480CE3**. Extra cycles characterize existing behavior; no product fix/red-first claim is made for that expansion.
- `python -I -B tests/chaos/test_remaining_runtime_check.py`: **3/3**, actual disposable SQLite fixtures, unchanged inventory/hashes on successful read-only check, nonempty WAL rejection, corruption rejection without repair. These checker tests were added after implementation.
- Final VM-local SDK 10.0.401 guarded `dotnet build HostAgent.sln --configuration Release --no-restore -m:1 -p:UseSharedCompilation=false`: exit 0, **0 warnings / 0 errors**, minimum free **2094.6 MiB**, stdout SHA-256 **09FF42B2F360E806E3444B3CEEF39CFC1986A1C5330DF27E6EC7E3D2F96D40D3**.
- Final guarded `dotnet test HostAgent.sln --configuration Release --no-build --no-restore -m:1 --logger "console;verbosity=minimal"`: exit 0, **379/379** (62 Core / 302 Infrastructure / 15 Desktop), minimum free **1530.0 MiB**, stdout SHA-256 **AB85237307449E8AD3FEAF254F1D6EF0F643A3F9FF4EA82BF7ACE049D1B88A8B**. Logs remain under scm-root-acl-fix-20261017, names remaining-retained-final-build-20261017 and remaining-retained-final-suite-20261017. No memory abort fired. Earlier intermediate 378/378 was superseded after the retention test was added.
- Final copied bundle's **actual operator fixture helper**, under the bounded child/memory guard but **non-elevated**, passed storage **3/3**, collector **7/7**, remediation **11/11**, WPF **1/1** (22 total). Evidence root `remaining-r2-preparation-9e5976d791c84bdfa23b6726c312b073`. Retained synthetic DB file counts were **3 / 7 / 8 / 0** respectively. TRX SHA-256 values in that order: **955F14281D735D40B5AFE4D0D8229DE4DDDEFFFF0C383673146C3EB95C08E2E0**, **59C71B20D763F7A7AFC5456265C007A618219A1ADB4069EB605F246E70C5E7A2**, **694D9635B90A97DBF7B79ECB3A6EAFFA8BB9DAC1C2C7FEBE8D6788DE74B8D6AC**, **92EE516970B18C3F700DCE68A89EEF843FAB13ED0737601D21E878FBD1F2215B**. This was preparation of safe test-fixture execution, not execution of the main operator schedule, new SCM crash or new runtime endurance.
- All five new/modified PowerShell entry/module files parsed successfully; git diff --check passed. Manual comparison of the new automatic SCM runner/helper against the already used controlled-crash harness confirmed the recovery actor change, bounded policy/disable cleanup and preservation of isolation checks. This is source review, not live proof of native SCM behavior.

Artifact identities independently validated by the final read-only main preflight:

- r5 manifest remains **EBBAD0807899BB8243AE71B2877C4C02B4C3F6024024EE748A179342428CAAAB**, 73 files, unchanged. The original r1–r5 packages and old GUID data were preserved.
- New test-only bundle `C:\Users\AgentPCdevtest\Documents\ChatGPT\DevTest VM PC\HostAgent-DEV-SCM-candidates\resilience-test-fixtures-20261017-r2`, **292 files**, approximately 81.5 MiB; manifest **ECC5B168850D7B10A1F6A0156508D38DDE467D71CA0411C27B92D221D3B43514**. Core/Infrastructure/Service assemblies match r5 exactly; the test-host/fixture assemblies include the new test-only fault mode. Intermediate test bundle r1 is preserved and is not selected.
- Eleven helper hashes are pinned by catalog **8D16115F74017C33DBFBA5CF94D594E60EE02A53D00A9A0A71ACDF713CFD22C6**. SDK host hash **21A46F1E5235CF4E844B9DE5429F0E198B9C97A41F0503A66442F1D639CA3EE6** and existing verified Python hash **B07B03B9BA866A341A3C13EE69710FBC8D43D4CE2523DEE13F6DF5C25378D15B** are checked. Final main script SHA-256 **43F287C4BB1F4A45DD1D52ED6A88C6DEA5D260C0CDE5A0FBECE23B28F221893C**.

The main was run **without parameters only**, read-only preflight, exit 0. Its **-Execute schedule remains NOT RUN**. Expected operator command in already elevated PowerShell inside the approved VM:

```powershell
& 'C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe' -NoProfile -NonInteractive -File 'C:\Users\AgentPCdevtest\Documents\ChatGPT\DevTest VM PC\HostAgent-DEV\scripts\chaos\run-remaining-dev-resilience.ps1' -Execute
```

Copy the complete final console JSON/progress and contents of the printed summary.json. Preserve referenced phase/child/TRX/SCM/database-check evidence. Do not repeat or close the controller mid-run; a final FAIL can coexist with later completed independent phases. NOT RUN manual gates do not count as PASS, and no automatic pilot readiness conclusion is made.

### Complete remaining-gate preparation, superseding preparation blockers (2026-10-17)

The user's subsequent request for actual live storage/helper/WPF integration and a longer bounded soak is implemented in the separate `scripts/chaos/run-complete-dev-resilience.ps1`. See [fresh preparation verification and operator procedure](complete-dev-resilience-preparation-2026-10-17.md) for exact scopes, limits, source-review corrections, failed/intermediate runs, final hashes and commands. Earlier packages, runners and evidence remain preserved.

The new plan contains nine automatic gates and one separately manual VM reboot/power gate. It prepares actual automatic GUID SCM crash recovery/committed-versus-pending persistence, live private storage lock/FULL equivalent/corruption recovery, collector failures, real GUID-service helper operations, actual WPF/private IPC recovery and a fixed 30-minute fault-injected private runtime soak. The 512 MiB boundary uses the actual guard classifier with synthetic inputs; physical pressure still must not cross the unchanged 768 MiB stop. Elevated/live campaign execution remains **NOT RUN** until the operator runs the new main; this paragraph does not close those gates based on unit tests.

Fresh final frozen-source Release build: exit 0, **0 warnings / 0 errors**. Final focused tests: **20/20**. Final full Release suite: **395/395** (62 Core / 318 Infrastructure / 15 Desktop), exit 0; minimum free **1426.9 MiB**, stdout SHA-256 **13BBE656820CE30193D2B3A4524B9A782ACFBC83220945E861EE14E0C784FAE1**. A previous failed full run and WPF restore-assets defect are explicitly retained in the detailed record; a separate pipe timing outlier passed focused and final full reruns without relaxing its assertion, but its independent cause was not established.

New published candidate `scm-release-candidate-20261017-complete-r1` contains **75 manifested files**, manifest **E4F2C033C80278182158E2FC823FAF3A7A008E904B3080472228432F5188492D**; Core/Infrastructure/Service binaries remain the verified r5 hashes. Main SHA-256 **537C06DE1272DF9C9521C5CF8CB1BB04E6A882DD6E3F7D87B53EAFCBA6A95189**; twelve-helper catalog **766261F4D68951BF900F80CEA74E70518B0041D9606A0D7E814995A015E2836A**. Independent final source review matched all 75 candidate and 12 helper hashes and found no P0/P1 blocker or definite orchestration defect; this is not live SCM/helper proof.

Both main and automatic-SCM child Windows PowerShell 5.1 **read-only preflights** passed. Final main preflight observed **1714.9 MiB** free RAM, **28.37 GiB** C: free and protected HostAgent **Running PID 3840**, exact account/path/start mode and all four protected hashes unchanged. No new operator SCM/ACL/fault gate, host transfer or commit was performed during preparation. Pilot verdict remains **NOT READY** pending actual operational results and the separate reboot/persistence gate.

2026-10-17 operator SDK-path failure follow-up: the published runner no longer requires a development SDK. It pins/validates the existing system dotnet host and explicitly selects that verified runtime for its child apphosts. See the appended follow-up in [complete preparation evidence](complete-dev-resilience-preparation-2026-10-17.md). New SDK-unavailable read-only regression is PASS after recorded RED; changed runtime hash is rejected; existing PowerShell checks pass 10/10 and 21/21. Fresh unmocked preflight PASS: **1890.6 MiB** free / **28.36 GiB** C: / protected Running PID **3840**, four protected hashes unchanged. New main SHA-256 **4C93FC88507DE47ED85A1646E10C146F92B54EE0DDA601050DFC10AE20FF5AF4**; candidate and helper catalog unchanged. No new C# build/suite or elevated campaign was run. The SDK was present during this investigation, so the operator's earlier availability difference remains unexplained; no SDK files were modified. The same operator command applies.

2026-10-17 operator campaign **ad3bda51ed314390b8409e232c2e65d8** is now recorded as **FAIL: 0 PASS / 6 FAIL / 4 NOT RUN**, summary SHA-256 **EC772106A96AF0FF904A711900B9F5E1785261B8F994EF578507A203C7E9FBFC**. Fresh deterministic reproduction identified two harness bootstrap defects: nested forced dependency import removed the native argument encoder before the first five child launches; case-sensitive `C:\Windows` validation rejected actual `C:\WINDOWS` during helper setup and cleanup. All four newly planned GUID service registrations/packages are absent on fresh read-only inspection. The rows do not prove HostAgent product failures. Earlier PASS evidence is preserved separately.

Both causes and missing safe failure-stage diagnostics were repaired test-first in PowerShell only; original scenario failure is preserved when helper cleanup also fails. Fresh checks include actual import-order/published noop, diagnostic/redaction/rejection tests, case Pester **8/8**, existing helper **21/21**, controller safety **10/10**, older predicates **9/9**, guard classifiers and runtime/preflight regressions. Final real main read-only preflight PASS: **1765.1 MiB / 28.36 GiB**, protected HostAgent **Running PID 3840**, four hashes unchanged. Separate SCM read-only preflight with fresh mandatory RunId also PASS. No elevated campaign/fault, new C# build/suite, commit or host transfer was performed; historical **395/395** remains historical. A preparation-driver missing-RunId invocation error is retained in the detailed record.

New main SHA-256 **4930AF9E594E30DFBED579AF505EBD9268BD4A3C9F40036D5191537E7EFD42AD**; updated thirteen-helper catalog **60300DBD61286CB6288B2A25A6E945C737A40390B04AD7787174C59723C24070**. Candidate manifest remains **E4F2C033C80278182158E2FC823FAF3A7A008E904B3080472228432F5188492D**, all 75 files freshly verified. See appended [bootstrap diagnosis, red/green commands, bounded evidence, identities and unchanged operator command](complete-dev-resilience-preparation-2026-10-17.md). No operational gate is closed by these preparation checks; **NOT READY**, operator campaign rerun pending and VM reboot/power still separate.


# Chaos / Resilience Verification Record — 2026-10-13

## Current result

Verdict: **NOT READY** for a resilience-qualified controlled pilot.

The exact rollback snapshot is now verified on HostAgent-DEV, the latest Release build and full test suite pass, and two bounded local CPU scenarios were attempted. The short CPU spike completed safely. The sustained-load run reached a measured 100% guest CPU sample but the parent safety guard stopped it after 93.1 seconds when available physical memory fell below 512 MiB. The four-minute diagnosis threshold and endurance criteria therefore remain untested.

One live status-label defect was confirmed and repaired in the DEV source tree test-first, but the protected installed service has not received the fix. Repeated high-confidence service_stopped incidents remain untriaged. No service was stopped, no storage was filled or corrupted, no VM reboot or power-off occurred, no remediation action was executed, and no endurance run was performed.

## Environment, source and rollback

All build, test, probe and fault-worker commands ran inside the approved VirtualBox guest:

- Guest hostname/account: AGENTPC / AgentPC\AgentPCdevtest.
- Guest make/model: innotek GmbH / VirtualBox. Guest UUID: 46543236-8C5E-49DA-953C-DB17F9B1E402.
- VirtualBox Guest Additions services VBoxGuest and VBoxService were Running.
- Repository: C:\Users\AgentPCdevtest\Documents\ChatGPT\DevTest VM PC\HostAgent-DEV.
- Branch: codex/local-dashboard.
- HEAD: 6cd0ec8ba61ce1c7c7cd89787f02f86b9b87ed8e.
- The working tree is intentionally uncommitted. Existing untracked docs/audits content was preserved; no reset, clean, or commit was performed.
- Latest independent whitespace check: git diff --check exited 0.
- Host-provided VBoxManage output directly identifies VM HostAgent-DEV, current snapshot pre chaos snapshot save, UUID dc8af832-31d6-49ac-a8c3-f00d9381597f. The clean-win-base snapshot UUID 3dec8924-291e-40f4-83bc-e424377fc2be remains present and unchanged. Snapshot gate: **PASS**.
- Guest inventory showed one VBOX HARDDISK, local NTFS C: and no mapped shared folders. The physical host, C:\PC Agent, host security settings and secrets were not used or modified. Hypervisor network mode was not independently inspected.

## Release and focused test evidence

The most recent product-source change was the collector-health aggregate correction. It was built and tested after that change:

- dotnet build HostAgent.sln --configuration Release --no-restore -t:Rebuild -m:1 — exit 0; zero warnings and zero errors.
- dotnet test HostAgent.sln --configuration Release --no-build --no-restore -m:1 --logger 'console;verbosity=normal' — exit 0; Core 58/58, Infrastructure 267/267, Desktop 15/15; **340/340 passed**. HOSTAGENT_TEST_LIVE_NETWORK was absent. The logs contain no unhandled-exception or aborted-run marker.
- HealthStabilizationTests — **13/13 passed** after the two new regressions were repaired.
- The aggregate partial-availability test first failed as expected with collectors_unavailable, then passed expecting collectors_partial.
- The empty-cycle test first failed as expected because the aggregate was healthy, then passed expecting collectors_unavailable and degraded.
- The current source diff and test diff were manually re-read; git diff --check passed. A scoped Codex Security working-tree diff scan completed at 10:11:15Z with zero reportable findings. It used the captured patch digest and is not a fresh repository-wide security assessment; the protected installed service still has not received the source fix.

Historical gate failures remain disclosed. The earliest suite path explicitly enabled the live network collector and attempted its configured gateway, public IP and DNS targets. Actual packet delivery was not recorded. The test was changed so the default run is offline and live probes require the exact test-only opt-in HOSTAGENT_TEST_LIVE_NETWORK=1. No Pushover delivery, Healthchecks call, real credential, or secret was used. After that test adjustment, an intermediate Release run had five existing WPF test failures; a test-only serialized STA harness and teardown regression were added. The identical cleanup regression was captured RED then GREEN, three focused Desktop runs passed 15/15, and the latest full suite passed. These earlier failures were not replaced by the latest result.

## Executed scenarios

| Scenario | Expected behavior | Observed result | Verdict |
| --- | --- | --- | --- |
| Snapshot preparation | Verified child snapshot on the exact DEV VM; preserve clean-win-base | Host VBoxManage values match HostAgent-DEV and the current snapshot UUID; base UUID preserved | PASS |
| S01 short CPU spike | A single bounded spike is visible, but does not meet the sustained diagnosis condition | One Lowest-priority worker ran for its 30-second self-expiring window. Parent observation lasted 33.6 seconds. Guest CPU averaged 35.6% across the window; free memory went from 581 MiB to a minimum sampled 539 MiB and ended at 538 MiB. The installed agent recorded CPU 10.7469% at 09:52:17Z and 20.3290% at 09:53:17Z during the spike. The bounded incident projection remained 20 and showed no cpu_sustained code. | PASS for bounded spike and threshold-negative behavior |
| S02 sustained CPU attempt | Four low-priority workers should keep host CPU at least 90% long enough for five qualifying samples across four minutes; confidence would be 85 for a dominant process or 60 for host saturation | Guest CPU reached 100%, including one agent CPU sample at 09:56:17Z. The parent guard stopped the worker at 93.1 seconds when free memory fell to 468 MiB, below the 512 MiB floor. C: remained at 33.87 GiB free. After the worker process exited, its PID was absent, HostAgent service was Running and free memory recovered to 804 MiB. The bounded incident projection still contained 20 entries and no cpu_sustained code. | ABORTED safely; four-minute diagnosis and confidence not verified |
| Installed service overview | Read-only status should be fresh, Shadow/read-only, and clearly distinguish partial telemetry from total unavailability | Fresh overview remained Shadow, ReadOnly=true, KillSwitch=false, Degraded=true. It labeled aggregate collectors_unavailable while system, event-log and network were healthy and cpu-memory, disk and process-services were partial. The installed service binary still has this label defect. | FAIL for aggregate health label |
| Read-only service incident correlation | Distinguish SCM crash records from normal or unexplained service transitions without changing logs | The bounded view contained 16 service_stopped subjects at confidence 88, with recurrence counts up to 137; several corresponding services were currently Stopped and configured Manual. No System events with IDs 7031, 7034 or 7036 matched the last-24-hour read-only query. This does not prove the services did not stop; it leaves the high-confidence “unexpected” interpretation unresolved. | PARTIAL / untriaged |
| Release suite’s Windows/Event Log/storage/security fixtures | Bounded evidence, safe cancellation and fail-closed behavior | The complete Release suite passed, including Windows collector integration, Event Log bootstrap, SQLite persistence and retention, helper/pipe security, remediation guards, lifecycle and WPF tests. These are automated fixtures and do not count as real service, storage-corruption or network-outage injection. | PASS for automated suite only |

S01 and S02 evidence, installed overview captures, the resource inventory, snapshot verification, and the append-only evidence ledger are stored outside the repository at %LOCALAPPDATA%\HostAgent-DEV-evidence\chaos-20261013T074135Z. The read-only service incident analysis stored only validated service identifiers, event IDs, record IDs and timestamps; it contains no raw XML or event messages.

## Confirmed defect and source repair

The fresh installed overview showed a false aggregate health label: collectors_unavailable appeared alongside three healthy and three partial collectors. Overall Degraded=true was still justified by partial telemetry.

In src/HostAgent.Service/AgentRuntimeHealth.cs, SetCollectors previously reduced every degraded aggregate to collectors_unavailable and treated an empty result list as healthy. The two regression tests were added first and failed. The implementation now emits collectors_partial when any collector completed successfully or partially while another is degraded, collectors_unavailable when no collector data is available, and degraded for an empty collection cycle. Exact GREEN runs, the 13-test focused class and the 340-test Release suite passed.

Severity: P2 operational correctness. The label can make an operator believe every collector is down when the live view shows useful telemetry. The correction exists only in the current DEV source tree; the installed service was not updated, so live post-fix behavior is unverified.

## Current operational anomalies and limits

The read-only overview also shows recurring service_stopped incidents (confidence 88, recurrence up to 137) across 16 subjects, plus a memory_pressure incident at confidence 90. The service-state/event-log comparison found no matching SCM 7031/7034/7036 records in the selected 24-hour window. Because state changes can be intentional for demand-start services, and because the investigation does not contain the historical service-start expectation, this is a potential false-positive pattern rather than a confirmed diagnosis bug. No service action was taken.

Guest memory was variable: it measured 352–368 MiB free during an earlier pre-injection check, later recovered above the 512 MiB floor, fell to 468 MiB during S02, then recovered after the worker exited. The 10:04:17Z sample showed 781 MiB free. A later read-only sample at 10:05:57.0799025Z showed 678 MiB free, 33.87 GiB free on C:, and the HostAgent service Running. The final sample at 10:12:09.5290728Z showed 566 MiB free, 33.87 GiB free on C:, and HostAgent Running; memory was approaching the 512 MiB stop floor, so no more pressure testing was started. The VM has 4,077 MiB physical memory and a 3,840 MiB pagefile. Codex/ChatGPT UI processes and MsMpEng were among the largest private-memory processes in one read-only inventory. No application or security process was stopped.

The installed data directory under C:\ProgramData\HostAgent is not readable by the unelevated test account. There is no verified host-side power-control tool attached to this task. These limits, the variable memory headroom, and the protected installed service prevented safe live tests of database lock/full/corruption, hard power-off, reboot during persistence, actual SCM stop/crash-loop, helper execution, or installed remediation. No real DNS/gateway outage was induced because network probes are disabled by default and the prior test-only public probes were removed from the normal suite. No secrets or external notification endpoints were used.

## Remaining scenario coverage

The separate 24-scenario matrix records expectations and ceilings before injection. Only S01 and S02 above were exercised as runtime fault workers. The following remain NOT RUN: pagefile or RAM allocation pressure; low/full disk and I/O latency; DNS/gateway/internet loss; collector/WMI/Event Log access failure; malformed or time-inconsistent evidence against the running service; service stop/crash-loop; persistence failure, corruption or interrupted transaction; baseline restore against installed data; dashboard stale-data behavior under a live outage; remediation success/failure/timeout/retry; VM reboot; hard VM power-off; concurrent faults; and 20–30 minute soak/endurance.

No result from the unit suite substitutes for these live scenarios. There was no production transfer, publication, or commit.

## Prioritized continuation

1. Safely stage the corrected Release build into a disposable DEV installation/data directory, then repeat the read-only health query to verify collectors_partial at runtime without replacing the protected installed state.
2. Resolve VM memory headroom before any further CPU/RAM pressure run. Recheck the 512 MiB free-memory floor immediately before each run and retain the parent cleanup guard.
3. Investigate whether the 16 repeated service_stopped incidents are expected service lifecycle transitions or false positives. Obtain bounded incident evidence and compare it to service start expectations before changing diagnosis semantics.
4. Exercise fixture-only storage, collector, dashboard and remediation failures; preserve the live-installed database and SCM configuration.
5. Only after these gates pass, run VM reboot/persistence and a bounded soak with exact rollback verification, resource samples and deduplication checks.


## Continuation update (fresh evidence captured 2026-10-13T10:30Z–11:05Z)

The guest was rechecked after the operator increased VM RAM: hostname `AgentPC`, account `AgentPCdevtest`, VirtualBox model, `VBoxService` Running, branch `codex/local-dashboard`, HEAD `6cd0ec8ba61ce1c7c7cd89787f02f86b9b87ed8e`. The existing modified/untracked working-tree files were preserved; this continuation made no HostAgent source changes. At the latest check, 1,083 MiB physical memory was free, C: had 33.26 GiB free, and HostAgent remained Running. The operator stated that Home Assistant on the physical host remains off; that host-side state was not independently queried from the guest.

| Scenario | Fresh result | Evidence and limit |
| --- | --- | --- |
| S02 sustained CPU, monitored retry | Workload completed for 298.5 seconds at 100.0% measured host CPU; minimum sampled free RAM was 998 MiB; C: stayed at 33.39 GiB free. The service returned two distinct 100% CPU telemetry samples during the test window, one 60 seconds after the other. No CPU incident appeared in six successful read-only observations. | `s02-sustained-cpu-result-20261013T104100Z.json`, `continuation-summary-20261013T1102Z.json`. The workload and 512 MiB guard are verified; the required five qualifying samples spanning four minutes and their diagnosis/confidence are not. The live collection cadence under saturation is insufficiently observed to identify a source-level root cause. A further CPU repetition is deferred until this collection gap is addressed; prior S02 abort and retry artifacts remain unchanged. |
| S03 bounded physical-memory allocation | A 128 MiB page-touched allocation ran for 30 seconds. Free RAM fell from 1,221 MiB to a minimum of 1,016 MiB, the floor was never approached, C: was unchanged, and the agent recorded memory usage rising from 76.73% to 80.86%. No `memory_pressure` incident was emitted, as expected below the configured threshold. A later fresh cycle showed 77.78% usage and 1,284 MiB free. | `s03-bounded-memory-result-20261013T104900Z.json`; later recovery was checked through the service's owner-verified read-only view. PASS for bounded allocation, telemetry response, and recovery; high-pressure thresholds were not exercised. |
| S10 owned process churn | Twenty inert child processes ran in five waves, at most four concurrent, each with a 16 MiB allocation and 10-second self-expiry. Minimum free RAM was 896 MiB, C: remained at 33.26 GiB, all children exited, and HostAgent stayed Running. | `s10-process-churn-result-20261013T105400Z.json`. The test's first dashboard observer used a synchronous pipe probe and could not reliably report active-process visibility, so that in-run projection is inconclusive. A later source-equivalent asynchronous client verified service identity, a successful Shadow/read-only overview, and no owned PID in the process projection at 11:05:17Z (`s10-post-run-view-source-equivalent.json`). This is partial dashboard coverage, not proof that all 20 children were individually visible. |

The monitored S02 run also showed collector health `collectors_unavailable` together with partial CPU/process/disk telemetry and healthy Event Log/network/system components. This is consistent with the previously documented installed-binary aggregate-health label defect; the source correction remains uninstalled and its live result remains unverified. The synchronous S10 observer failures were instrumentation limitations and are not attributed to HostAgent.

No Release build or test suite was rerun during this continuation because product source and tests were not changed. The existing verified Release suite remains 340/340 from the earlier recorded run. Current pre-production status remains NOT READY: sustained CPU diagnosis/confidence, live process-collector visibility, elevated storage/SCM scenarios, reboot persistence, and endurance remain open.

## Continuation update (fresh evidence 2026-10-13T13:03Z–13:57Z)

The operator reported that HostAgent-DEV was running after restart and that guest RAM had been raised to 6,144 MiB. Read-only checks confirmed hostname AGENTPC, account AgentPC\\AgentPCdevtest, VirtualBox model, VBoxService Running, HostAgent Running, branch `codex/local-dashboard`, HEAD `6cd0ec8ba61ce1c7c7cd89787f02f86b9b87ed8e`, and 33.31 GiB free on C:. The operator-provided snapshot verification remains the rollback gate: `pre chaos snapshot save` / `dc8af832-31d6-49ac-a8c3-f00d9381597f`; `clean-win-base` / `3dec8924-291e-40f4-83bc-e424377fc2be` was not changed. Existing working-tree changes remain uncommitted and preserved. No host file, policy, secret, external endpoint, snapshot, or installed service binary was changed.

| Scenario | Fresh result | Evidence and limits |
| --- | --- | --- |
| S02 sustained CPU, post-reboot 330-second attempt | COMPLETED at 100.0% measured guest CPU. Duration 353.3 s; minimum free RAM 1,217 MiB; C: stayed 33.31 GiB. Three distinct >=85% telemetry samples appeared, spanning 2.65 minutes; no `cpu_*` incident appeared in successful in-run reads. Three overview reads ended with `EndOfStreamException`. | `s02-sustained-cpu-postreboot-20261013T130500Z.json` SHA-256 `904D28A8FBA4B1B7364DC161BF3F00137485BE0D337EB957A539C9894B5B3F4B`. Diagnosis condition of five samples spanning four minutes was not reached; confidence cannot be judged. |
| S02 sustained CPU, extended 480-second attempt | COMPLETED at 99.9% measured guest CPU. Duration 477.2 s; minimum free RAM 758 MiB, final 1,031 MiB; C: stayed 33.31 GiB. HostAgent stayed Running at all 23 sampled checks. Three distinct >=85% telemetry samples appeared, spanning 1.70 minutes; no CPU incident was present in successful in-run reads. Two overview reads ended with `EndOfStreamException`. | `s02-sustained-cpu-8min-20261013T131500Z.json` SHA-256 `ACA9A40B677AAD52A0D901986C9AFF5AEABE234D7A890A9F4257564DB1279B1C`. The 512 MiB RAM and 20 GiB C: guards remained in force. CPU diagnosis remains unverified because the agent did not provide the qualifying sample sequence. |
| Dashboard during CPU saturation | Five failed overview reads were observed across the two runs; successful reads continued. After load ended, an owner-verified read-only overview succeeded in 619 ms with current CPU 6.0% and no CPU incident. | Actual degraded availability under sustained saturation is recorded; the exact internal cause was not instrumented, so no root cause or service crash is claimed. The installed server/client use a bounded 10-second view path. |
| S15 graceful reboot recovery | The corrected post-reboot check confirmed a changed boot time, HostAgent Running, Shadow/read-only overview available, and 15 of 20 incident-ID hashes overlapping the pre-reboot projection. | `s15-post-reboot-result-fixed-20261013T1204Z.json` SHA-256 `8FB3C45E0140145BB4309C281178D60C3891D2384CFE1BE1A9402748D1C539E3`; check SHA-256 `114A6384995540CF5B9470FAB3C0327E12AE68B35D1643DD3272BE500C904B40`. This is partial projection persistence only; SQLite integrity, collector checkpoints, baselines, and actual process owner were not independently verified. |
| S24 idle endurance observation | Completed 1,267.3 s with 81/81 view reads, zero service failures, zero duplicate heads, 20 incident heads throughout, 21 distinct CPU timestamps, and no CPU sample older than 180 s. Free RAM ranged 1,138–1,343 MiB; C: ranged 33.31–33.32 GiB. Handles ranged 394–419 and ended at 398 from 405. After the 300-second warm-up, private bytes ranged 113.4–349.7 MiB; least-squares trend was -0.343 MiB/min and median overlapping 4.5–5.5-minute pair rate was -0.069 MiB/min. | `s24-idle-soak-samples-20261013T124100Z.jsonl` SHA-256 `1F2C01F57319F757AC643FF9307058B5571999A56DAD214254E323293880A6B6`; summary SHA-256 `1711F104E48BC66A0F9A229A02529AE29A8BCA60EEE961849930EACCDFCBCE6E`. Private-byte samples were volatile, with isolated increasing windows; no sustained monotonic growth was established. This was idle-only, with no injected faults, database integrity check, or remediation. It does not close the combined-fault endurance gate. |
| S18 network diagnosis projection | A persisted overview regression test first failed 3/3: `gateway_failure`, `dns_failure`, and `internet_connectivity_failure` all became `other_diagnosis`. After adding the actual diagnostic codes to the safe projection allowlist, the test passed 3/3 with exact confidence 90, 90, and 80. | The source fix is not in the protected installed service. No real DNS, gateway, or external request was made. |
| S09 malformed/incomplete telemetry | The missing-`Value` fixture first raised `KeyNotFoundException`; a second fixture with `Volumes=null` raised `NullReferenceException`, suppressing valid peer CPU diagnosis. The processor now ignores unsupported schemas and malformed DTO shapes, records a stable telemetry failure code, and continues with valid peers. Missing-`Value`, null-collection, and schema-version cases passed 3/3; the valid CPU diagnosis retained confidence 85. | Syntactically invalid JSON could not be written because the SQLite `json_valid(payload_json)` CHECK rejected the attempted update. That integrity guard was not bypassed; raw database-file corruption remains untested. Source fix is not installed. |
| Service stop/crash scenario | NOT RUN. The current VM token was not elevated as an administrator. | No elevation attempt or service-control bypass was used; the installed service remained Running. Protected installed storage remains unreadable from this account. |

The verified SDK was 10.0.401 at `%LOCALAPPDATA%\\HostAgent-DEV-tools\\dotnet-10.0.401`. A first full suite invocation omitted `DOTNET_ROOT` and produced three infrastructure test-harness failures because child processes could not resolve `dotnet.exe`; the three failed cases passed 3/3 after setting the account-local `DOTNET_ROOT`. With `HOSTAGENT_TEST_LIVE_NETWORK` unset, the final commands were:

- `dotnet build HostAgent.sln --configuration Release --no-restore -t:Rebuild -m:1` — exit 0, 0 warnings, 0 errors; log `release-build-final3-20261013T134000Z.log` SHA-256 `5A9B0FAFAD054AADD5F4ED6B67FF58AF28EFFDCE4393D9186BE19EE7C960E883`.
- `dotnet test HostAgent.sln --configuration Release --no-build --no-restore -m:1 --logger "console;verbosity=minimal"` — exit 0; Core 58/58, Infrastructure 273/273, Desktop 15/15; **346/346 passed**; log `release-test-final3-20261013T134000Z.log` SHA-256 `838FD616F8A3470490BEE8AAB794A0E0455AF2F791CA5496F1777099F3C60F44`.
- Focused `CollectionCoordinatorTests` + `DashboardViewTests`: 24/24 passed. Focused `Task2PersistenceTests` + `SqliteHostAgentStoreTests`: 8/8 passed. `git diff --check`: exit 0.

The new source and test changes remain uncommitted in the DEV working tree. The installed `C:\\Program Files\\HostAgent\\Service\\HostAgent.Service.exe` remained unchanged (SHA-256 `04DF669D662BB08BDBD76653E46158E3D701D7E0D48653439F8F836BB417CB87`); therefore neither source correction has a live installed-service verification. The service still reports the old aggregate `collectors_unavailable` label alongside healthy and partial components.

The campaign remains **NOT READY**. The sustained-CPU detection condition and confidence were not reached; dashboard reads were intermittently unavailable during saturation; service stop/crash, protected-storage corruption/full conditions, real network loss, hard power-off, concurrent faults, and fault-injected endurance remain unverified. No test result is inferred from implementation presence alone.

## Restarted-VM continuation update (2026-10-13T14:05:22Z)

After the operator reported that the VM was signed in again, a fresh guest check confirmed hostname AgentPC, account AgentPC\AgentPCdevtest, model VirtualBox, VBoxService Running, HostAgent and HostAgent.Helper Running, 6,124.9 MiB total RAM, 1,124.2 MiB free RAM, and 33.29 GiB free on C:. This is above the 512 MiB memory floor and 20 GiB disk floor. The repo remains on codex/local-dashboard at 6cd0ec8ba61ce1c7c7cd89787f02f86b9b87ed8e with the existing dirty work preserved.

The two post-reboot S02 workload results and S19 dashboard observations remain as recorded in the preceding continuation section. The additional eight-minute run completed at 99.9% measured CPU, with 758 MiB minimum free RAM and no service stop; however, only three distinct qualifying CPU samples spanning 102 seconds appeared, versus the required five samples over four minutes. No CPU diagnosis/confidence conclusion is claimed. Across the two additional S02 attempts, the read-only pipe view failed 5 times across 13 requests with EndOfStreamException under saturation; a post-load overview recovered in 619 ms. Cause remains uninstrumented.

Safe fixture-only focused verification then ran with HOSTAGENT_TEST_LIVE_NETWORK unset:

- dotnet test HostAgent.sln --configuration Release --no-build --no-restore -m:1 --filter "FullyQualifiedName~SqliteHostAgentStoreTests|FullyQualifiedName~Task2PersistenceTests|FullyQualifiedName~UserMaintenanceTests|FullyQualifiedName~OperatingModeTests|FullyQualifiedName~WindowsOperationExecutorTests|FullyQualifiedName~DashboardViewTests" --logger "console;verbosity=minimal" — exit 0, Infrastructure 76/76. These tests use isolated fixture databases/files and synthetic policy/helper transports; they are not live installed-store, service-crash or real remediation tests.
- dotnet test tests/HostAgent.Desktop.Tests/HostAgent.Desktop.Tests.csproj --configuration Release --no-build --no-restore -m:1 --filter "FullyQualifiedName~DashboardUiTests|FullyQualifiedName~MaintenanceUiTests" --logger "console;verbosity=minimal" — exit 0, Desktop 3/3. Synthetic UI data only; no live dashboard outage or privileged operation.
- Free memory remained 1,026.7 MiB after the infrastructure fixture run and 1,168.6 MiB after the WPF run. C: and the installed HostAgent services remained unchanged.

Read-only SCM inspection used sc.exe qc HostAgent and sc.exe qfailure HostAgent. HostAgent is AUTO_START under NT SERVICE\HostAgent and its configured failure actions are two RESTART actions, each delayed 10 seconds, with no recovery command. The current token is not an administrator. No service stop/crash or recovery action was attempted; S13/S14 remain blocked and their actual recovery behavior is unverified. The installed binary hash remains 04DF669D662BB08BDBD76653E46158E3D701D7E0D48653439F8F836BB417CB87.

No source changes, install changes, service actions, external endpoint calls, secrets, host access or snapshot operations were performed in this continuation. The full Release rebuild and 346/346 suite from the immediately preceding source state remain the latest full gates. Fixture-focused checks do not close the live Windows fault scenarios. Campaign verdict remains **NOT READY**.
### S19 transport follow-up (source trace, 2026-10-13T14:07:34Z)

A read-only source trace after the saturation observations found that ViewPipeClient catches IOException (including EndOfStreamException) and returns the fixed view_unavailable response. DashboardWorker recreates its one-request pipe after a failed serve, with a bounded one-second retry delay. AdminPipeServer uses a 10-second request deadline and closes the connection if dispatch is canceled at that deadline; this is a plausible EOF path, not evidence that this caused the five observed failures. The product client is designed to surface unavailability rather than propagate the raw EOF to the UI. The focused WPF dashboard/maintenance tests passed 3/3 with synthetic disconnect/stale data. No live desktop window was faulted during CPU saturation, so user-visible recovery and the cause/rate of transport closures remain unverified. No source change is justified from the available evidence.
## S14 installed-service crash recovery (fresh evidence 2026-10-13T21:30:52Z)

The operator ran the previously prepared elevated test against only the pinned HostAgent service process. Its JSON reported PID 5888 replaced by PID 11344, automatic SCM recovery in 8.9 seconds, no manual fallback, final state Running, minimum free RAM 976.9 MiB, C: 32.62 GiB free, and installed binary SHA-256 04DF669D662BB08BDBD76653E46158E3D701D7E0D48653439F8F836BB417CB87. A direct read-only guest check at 2026-10-13T21:30:52Z confirmed hostname AGENTPC, account AgentPC\AgentPCdevtest, VirtualBox model, service PID 11344/Running under NT SERVICE\HostAgent, 794.8 MiB free RAM, 32.61 GiB free C:, and the same binary hash. S14 is a **partial pass**: one installed-service process crash recovered automatically inside the observed 8.9-second window, with no manual fallback. This does not verify database integrity/persistence, dashboard reconnection, or repeated crash-loop behavior. S13 remains unrun. No source, installed binary, service configuration, snapshot, host state, or security setting was changed.

## Service-stop classification review and current guard (2026-10-13T21:34:44Z)

A read-only trace of DiagnosticEngine confirms that every service transition from a non-Stopped state to Stopped, unless paired with a matching SCM crash event, is emitted as a High service_stopped diagnosis at confidence 88. The service telemetry does not include start mode or an expected-running policy. Existing bounded read-only artifacts show 17 recent service-stop subjects; 12/17 were configured Manual and three additional Auto services had trigger configuration. The recorded 24-hour SCM/Event Log queries found no matching 7031/7034/7036 records. This is a **confirmed classification gap / P2 false-positive risk**: the current rule cannot distinguish a normal demand/trigger-start service stopping after work from an unexpected failure. The individual historical stops remain unattributed; no claim is made that every incident was false.

At 2026-10-13T21:34:44Z, guest free RAM measured 458.1 MiB, below the campaign's 512 MiB floor. No regression test, code change, build, or further fault injection was attempted after this guard tripped. Resume only after a fresh check confirms at least 512 MiB free (prefer >=1 GiB before compilation) and 20 GiB free on C:. S14 remains partial pass as recorded above; S13 and the remaining protected-storage/remediation/concurrent-fault scenarios remain open.

## VM resume preflight (2026-10-13T21:58:02Z)

Read-only check confirmed execution on AGENTPC / VirtualBox under AgentPC\AgentPCdevtest; VBoxService and HostAgent were Running. Guest memory is approximately 6125 MiB total with 637.4 MiB free; C: has 32.75 GiB free. A test-first regression was added to DiagnosticEngineTests for a Manual/demand-start service stopping without crash evidence. It has not yet been executed, and no implementation change was made. Current free RAM is above the 512 MiB hard floor but below the 1 GiB compilation headroom target, so the focused build/test is deferred to protect the floor.

## 2026-10-14 continuation preflight (22:03Z)

Fresh guest check reconfirmed AGENTPC / VirtualBox, HostAgent and VBoxService Running, branch codex/local-dashboard at 6cd0ec8ba61ce1c7c7cd89787f02f86b9b87ed8e, 6,125 MiB total RAM, 651.2 MiB free, and 32.74 GiB free on C:. The existing dirty tree was preserved. The Manual-service regression remains present but has not been executed; no implementation change was made. The hard 512 MiB floor is currently respected, but memory remains below the previously set 1 GiB compile headroom, so no build/test or fault injection was started.

## Continuation update (fresh evidence 2026-10-13T22:56Z–23:43Z; guest local date 2026-10-14)

This continuation supersedes the earlier 22:03Z preflight note that the Manual-service regression had not yet run; the earlier note is retained as historical evidence.

The guest was revalidated as AGENTPC / AgentPC\AgentPCdevtest on VirtualBox with VBoxService Running. Repository branch and HEAD remain codex/local-dashboard / 6cd0ec8ba61ce1c7c7cd89787f02f86b9b87ed8e. The working tree was already dirty and all prior changes were preserved. At the final guard check HostAgent was Running (PID 10424), physical memory free was 2,139.6 MiB, and C: had 29.88 GiB free. The current Codex command token is not elevated. No host, secret, external endpoint, installed binary, snapshot, or security setting was changed. The installed service image remains SHA-256 04DF669D662BB08BDBD76653E46158E3D701D7E0D48653439F8F836BB417CB87.

### Service-stop classification regression

The test-first Manual/demand-start regression first failed as expected: a FrameServer transition from Running to Stopped with Manual StartMode and no SCM crash evidence produced a High service_stopped diagnosis at confidence 88 instead of no diagnosis. A Windows collector assertion also failed before implementation because ServiceTelemetry did not expose StartMode.

The DEV source now captures a bounded enum for service start mode and suppresses the generic stop diagnosis for a Manual service when no matching crash event exists. SCM crash events are evaluated before that suppression and remain eligible for diagnosis. This addresses the confirmed Manual/demand-start false-positive class. It does not establish expected-running policy for Automatic services with trigger configuration; that risk remains open. The protected installed service was not replaced, so this source change has no live installed-service verification.

Fresh focused tests passed: DiagnosticEngineTests 30/30; the specific Windows service collector integration test 1/1; and CollectionCoordinatorTests + DashboardViewTests + WindowsCollectorIntegrationTests 33/33. The targeted test logs are stored outside the repo under %LOCALAPPDATA%\HostAgent-DEV-evidence\chaos-20261013T074135Z.

### Post-restart SCM and view-pipe observations

A read-only System-log query found SCM event 7031, RecordID 3518, at 2026-10-13T22:56:33.702Z naming HostAgent. The current service process began at 22:56:44.849Z and remained Running through the campaign. A bounded Application-log query found no Application Error 1000 or .NET Runtime 1026 event attributable to HostAgent in that window; WER 1001 records contained only report metadata for this query and were not attributed to the service. The restart was observed, but its initiating cause remains unknown; treat it as a P2 follow-up until explained. Sanitized metadata is in s14-postboot-scm-followup-20261014.json; raw event messages were not saved.

After the previously observed S14 process crash, the first owner-verified, read-only overview request connected to the HostAgent-owned pipe but ended in EndOfStreamException before a response. Five immediate serial retries succeeded in 1.45–2.92 seconds. The complete response body was not retained. During the 20-minute soak below, six of 77 overview requests returned view_unavailable (four EndOfStreamException and two OperationCanceledException); 71 requests succeeded, the service stayed Running, and later requests recovered. One failure took 10.4 seconds, near the configured server deadline. This is a P2 availability concern: the failure is repeatable, but the data does not identify whether authentication, dispatch latency, or another pipe/server condition caused it. The source server currently suppresses exception details and the installed service did not emit a diagnostic stage for these requests, limiting root-cause attribution. This is not evidence of a dashboard process crash or data loss.

### Fault-injected endurance

S24 combined low-priority CPU and memory injection ran for 1,200.8 seconds, with five cycles of four Lowest-priority CPU workers and a touched 128 MiB allocation for at most 30 seconds every four minutes. The local owner-verified overview was sampled every 15 seconds. The parent checked memory, disk and service state throughout; it stopped below a 768 MiB soft floor, with the 512 MiB hard floor and 20 GiB disk floor preserved.

The run completed all five cycles with zero thread-cleanup failures. Minimum free memory was 1,837 MiB, final free memory 2,085.8 MiB, and C: stayed at 29.88 GiB free. HostAgent remained Running with the same installed image hash. The projected incident count stayed exactly 20 throughout. The transport produced 71/77 successful responses and six transient failures; successful reads resumed after each observed failure. This verifies bounded CPU+RAM coexistence and resource recovery, but not database integrity/size, service crash-loop behavior, or a live dashboard window. The append-only samples and summary are s24-fault-soak-combined-20261013T232000Z.jsonl and s24-fault-soak-combined-20261013T232000Z-summary.json outside the repository.

### Fresh Release gate and review

The full Release rebuild ran after the service-start-mode source change with the live-network opt-in unset:

\`\`\`powershell
dotnet build HostAgent.sln --configuration Release --no-restore -t:Rebuild -m:1 -p:UseSharedCompilation=false
\`\`\`

It exited 0 with 0 warnings and 0 errors. Log SHA-256: 937D8D56937984FFF04EADE30F7BD9959698ED91C8972F5E9CE8144FE5AA1CA5.

\`\`\`powershell
dotnet test HostAgent.sln --configuration Release --no-build --no-restore -m:1 --logger "console;verbosity=minimal"
\`\`\`

It exited 0: Core 59/59, Infrastructure 273/273, Desktop 15/15; 347/347 passed, no skips. Log SHA-256: A27D7B1D42DC24F0EC7B593A77A0E285CFF35591E1C678A75BFF4CA7FA29276B. The later focused runs also passed. A fresh diff whitespace check passed, and the service-start-mode implementation, collector mapping, and regression tests were reviewed in the working diff. This is not a new repository-wide security scan.

The new S24 result improves the campaign evidence, but the campaign remains NOT READY. Intermittent view-pipe failure under both active and idle periods, the unattributed post-boot service restart, Automatic trigger-start false-positive risk, and the untested live storage/remediation/service-control cases prevent a controlled-pilot verdict.

## S13 elevated stop/storage preflight (user-run, 2026-10-15T01:22:36Z)

The operator ran the bounded elevated S13 harness in AGENTPC / VirtualBox as AgentPC\AgentPCdevtest. VM identity, elevation, memory (1,604.2 MiB free), disk (30.41 GiB free), and installed service image SHA-256 (`04DF669D662BB08BDBD76653E46158E3D701D7E0D48653439F8F836BB417CB87`) matched the guards. The harness safely stopped at preflight because `C:\ProgramData\HostAgent\host-agent.db` was 234,430,464 bytes (about 223.5 MiB), above the harness's initial 128 MiB ceiling. `stopResult`, pipe check, SQLite quick check, and recovery all remained `not_run`; the database was not opened and no service operation was issued. The sanitized artifact was `s13-admin-check-20261015T012238838Z.json`, SHA-256 `E7D8C3B3570D65A6C1250B1C1A7D946EB291247C125FCE4A393F40B97AA87B45`.

This is a **safe preflight abort**, not a HostAgent failure. The harness was updated to allow databases up to 512 MiB and to set a 45-second SQLite progress-handler deadline for read-only `quick_check`, while retaining the 1 GiB free-memory and 20 GiB free-disk floors. The updated harness has passed PowerShell parsing, helper compilation, and static safety checks only; the live S13 stop/recovery and database integrity check remain unrun pending a new operator result.

### S13 baseline overview retry (user-run plus fresh read-only probes, 2026-10-15T01:25Z–01:27Z)

The next elevated harness run again stopped before any service or database operation: its single baseline overview request was unavailable. The service image remained the expected hash, HostAgent was Running, free memory was 1,581.3 MiB, C: had 30.42 GiB free, and the database metadata size was 234,532,864 bytes. `stopResult`, pipe-while-stopped, SQLite check, and recovery were all `not_run`. User-provided artifact SHA-256: `0113DF0A1F4BDD28F160C309B963068CABCD753BA66E4D5A5671F2CC75072AB4`.

Separate read-only overview probes against the still-running service reproduced transient transport failure. The probe verifies the pipe owner before accepting a completed response. Among five 8-second attempts, three completed with bounded frames (8,485–8,617 bytes) in 4.857–6.994 seconds, one ended EOF after 7.037 seconds, and one timed out at 8.048 seconds. A further 12-second attempt ended EOF at 8.166 seconds; a subsequent 15-second attempt completed in 2.277 seconds with `ok=true`, `code=overview`, and 20 incidents. The raw view payload was not saved. This confirms intermittent view availability on an otherwise Running service; the failure stage inside the service remains uninstrumented, so the underlying cause is not yet proven. The 102,400-byte difference between the two database metadata sizes was observed while the agent continued running; it is not attributed to either aborted test.

The harness now retries the baseline overview up to five times, records only each attempt's stage, duration and bounded result code, and still refuses to stop the service unless a valid owner-verified overview succeeds. Its PowerShell parse, pipe/SQLite helper compilation, static safety checks, and whitespace checks pass. The live S13 stop/recovery and SQLite integrity check remain unrun.

## S13 harness result-serialization correction (fresh evidence 2026-10-15)

The user-provided elevated run ended with `ArgumentException: Argument types do not match` while creating the ordered result at line 351 of `scripts/chaos/run-admin-s13-storage-check.ps1`. An isolated PowerShell 7.6.5 reproduction identified `@($baselineAttempts)` where `$baselineAttempts` is `List[object]` as the failing expression; inline conditional fields serialized correctly. The failure occurred after the script's guarded try/catch/finally and recovery-check code. Because the exception prevented the final JSON artifact, the captured output does not establish whether that particular run issued the service stop or completed `quick_check`. A later live state query found HostAgent Running; this does not reconstruct the missing result.

The harness now converts the generic list with `.ToArray()` before constructing the result and precomputes the optional incident-count scalars. The original regression reproducer failed before the fix and the updated shape passed JSON round-trip checks with zero, one, and three attempts. The full script passed PowerShell parsing and a bounded non-elevated end-to-end smoke run: it returned the expected `preflight_not_elevated`, kept stop and storage checks `not_run`, and HostAgent was Running before and after. Sanitized artifact SHA-256: `5ECBDE58F22D876ABFFB34BA77D39192245841485717E031B33BE2E8B43993A2`; the artifact contains no raw pipe payload and is at `%LOCALAPPDATA%\HostAgent-DEV-evidence\chaos-20261013T074135Z\s13-admin-check-20261015T015424316Z.json`.

The revised static review confirms the script only stops/starts the exact `HostAgent` service, opens SQLite with `SQLITE_OPEN_READONLY`, and contains no service-configuration, execution-policy, disk-format, or recursive-delete command. `git diff --check` passed. No product source or installed binary was changed; no Release build/test was repeated because this correction is limited to the untracked test harness. The actual elevated S13 stop, read-only database check, and recovery still need a fresh run; no result from the errored attempt is counted as a pass.

## S13 completed stop/storage check and post-recovery view follow-up (fresh evidence 2026-10-15–16)

The user reran S13 elevated in AGENTPC / VirtualBox as `AgentPC\AgentPCdevtest`. The artifact SHA-256 was independently recomputed and matched `81765D2BF0FBE4C7C1F993FA828ABB6998FABCB4EA867575D56C9EA79C7B05AC`. The installed service image matched the pinned SHA-256. Its owner-verified baseline overview succeeded on attempt 1 in 973 ms and showed 20 incidents. The script stopped only HostAgent; the View pipe was unavailable while stopped as expected. The read-only SQLite `quick_check` completed `healthy:1` on a 237,277,184-byte database; WAL size was 0 and SHM size 32,768 bytes. The service restarted, but the immediate recovery overview ended in EOF, so the script correctly reported `partial` with `recoveryResult=running_but_view_unavailable:eof`. It did not obtain a post-recovery incident count, so recurrence/dedup deltas remain unverified. It requested no database writes, changed no service configuration, and saved no raw pipe payload. This validates a graceful service stop, read-only storage integrity check, and service restart; it does not pass the full recovery-view criterion.

On 2026-10-16, fresh guest checks again confirmed AGENTPC / VirtualBox, VBoxService Running, HostAgent Running, and the same installed image hash. Four bounded owner-verified read-only view attempts were allowed, stopping on success: attempt 1 timed out after 34.2 seconds although the helper timeout was 6 seconds; attempt 2 succeeded in 5.703 seconds and again reported 20 incidents. The helper did not retain raw response data. Free-memory samples remained above the 512 MiB hard floor. The pipe therefore recovered after the immediate EOF, but the long timeout overshoot and intermittent failure remain a reproducible P2 availability concern.

Source inspection found that the View `AdminPipeServer` is created without a rejection callback, while `DashboardWorker` suppresses exceptions during retry. This explains why the service exposes no stage-specific evidence for a failed view request; it does not prove the underlying cause of EOF or the timeout overshoot. No product code was changed in this continuation. The existing Release suite was not rerun. Current branch/HEAD remain `codex/local-dashboard` / `6cd0ec8ba61ce1c7c7cd89787f02f86b9b87ed8e`; the pre-existing dirty worktree was preserved.

## Current assessment after S13 (fresh through 2026-10-16T00:10Z)

**Later continuation:** see [fresh View stabilization evidence](chaos-view-stabilization-2026-10-16.md)
for deadline/cadence/trigger-service fixes, safe diagnostics, separate live
View tests, disposable storage checks and latest Release gates. The S13
assessment below is historical. The protected installed service is unchanged;
the campaign remains NOT READY for a resilience-qualified pilot.

Verdict remains **NOT READY** for a resilience-qualified controlled pilot. S13 now has verified graceful stop, read-only SQLite integrity (`healthy:1`), and service restart. The immediate post-restart View pipe returned EOF; a later owner-verified query timed out and the next query succeeded with 20 incidents. The installed service is currently Running with its pinned image, but intermittent View availability and its hidden failure stage remain unresolved. The S13 artifact did not save incident hashes, so equal pre/post counts do not prove incident identity or deduplication.

The fixed Release suite and S24 bounded endurance results remain historical fresh evidence from 2026-10-13. This continuation changed only the chaos verification record; it did not change product source or installed binaries. The current Codex token is unelevated, so additional live service-control or reboot tests require an operator-run elevated harness. Hard power-off still also requires verified host-side VirtualBox control. Storage corruption/full, interrupted persistence, injected collector failure, dashboard behavior under a sustained outage, and remediation timeout/retry remain unverified; these should be attempted only on disposable fixtures or after their explicit VM safety guards are in place.

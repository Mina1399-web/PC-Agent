# S5 manual normal-reboot persistence preparation — 2026-10-18

**Current operator result: normal-reboot S5 PASS**, retained first Verify result `result-verify-20261018T034035614Z.json`, SHA256 `0550F09BC5FF76599B27DEF0B442995BFCC86ECBFCF93A38E0AA9FCA6EE372DB`. The subsequent repeated Verify failed because the one-shot test had already disabled/stopped its service. See the final evidence entry below. Preparation-time NOT RUN statements are historical.

## State and scope

**Preparation/build/automated checks/read-only preflight PASS. Actual operator Prepare, Windows restart and post-reboot Verify: NOT RUN.** This is a separate normal Windows restart gate. It does not establish abrupt power-loss recovery, physical storage failure, or pilot readiness. Historical successful S1/S2/WPF/soak scenarios were not repeated.

Approved guest freshly observed: AGENTPC / AgentPC\AgentPCdevtest / VirtualBox / UUID `46543236-8C5E-49DA-953C-DB17F9B1E402` / VBoxService Running. Branch `codex/local-dashboard`, HEAD `6cd0ec8ba61ce1c7c7cd89787f02f86b9b87ed8e`; existing dirty work preserved. Before editing, copies of the touched existing test host and verification record plus Git status were saved in `%LOCALAPPDATA%\HostAgent-DEV-evidence\reboot-preparation-20261018T024858Z`. No commit or host transfer was made.

The operator's host-side VBoxManage output confirms current snapshot **pre reboot save / 84c78c1b-61b9-4e41-a8ef-30db3a655ddf**, matching VM UUID, with `clean-win-base / 3dec8924-291e-40f4-83bc-e424377fc2be` preserved. It reports VM running and configured RAM 7072 MiB. No guest or host power operation was executed by this preparation.

## Isolation and procedure

`scripts/chaos/run-manual-reboot-persistence.ps1` has read-only default Preflight and explicit Prepare/Verify stages. Default run ID: `c60759c492a840f7b993afc807e7339a`.

- Service `HostAgentDevScm_c60759c492a840f7b993afc807e7339a`, virtual account `NT SERVICE\HostAgentDevScm_c60759c492a840f7b993afc807e7339a`; separate private overview-only View pipe `HostAgent.Dev.Scm.View.c60759c492a840f7b993afc807e7339a`.
- Package/data root `C:\ProgramData\HostAgent-DEV-SCM-c60759c492a840f7b993afc807e7339a`; data is its `data` child, with `host-agent.db` and `notifications.db`. Evidence is `%LOCALAPPDATA%\HostAgent-DEV-evidence\reboot-c60759c492a840f7b993afc807e7339a`.
- Root ACL permits only Administrators/System; service access is read/execute in app and write access only in data. Existing startup canaries exercise denied write/delete in app. No ACL outside this new root is changed.
- Prepare verifies VM/account, candidate and helper pins, current protected-service configuration/path/account/PID/hashes, initial free RAM >=1024 MiB and C: >=20 GiB. It rejects an existing run root/service/evidence. A reviewed hash-pinned subset of existing runner function definitions provides ACLs and protected-service read-only validation; its executable body is never invoked.
- The fresh GUID service is demand-start during setup, then Auto with only VBoxService as dependency and no SCM failure-action recovery. Its startup checks >=1024 MiB. It hosts current runtime assemblies with Shadow defaults, DenyHelper, local notification sender, disabled heartbeat/network probes and read-only private IPC. No protected HostAgent/HostAgent.Helper control is allowed.
- Before permitting manual restart, actual collected snapshot IDs are acknowledged, both fixture DBs receive a committed test row, and both hold a separate uncommitted transaction. Atomic, write-through, no-overwrite markers distinguish ready, graceful closure, post-start and safety failure. Graceful closure is published only after rollback/disposal of both pending transactions. Existing crash-proof mode is unchanged.
- The test process owns an independent 250 ms safety thread throughout the host lifetime: RAM <768 MiB, hard boundary <=512 MiB, C: <20 GiB or fixed 900-second lease requests its own host stop. After an abort, cancellation, stop callback and marker writing are separate one-shot background actions; blocked callbacks/marker I/O cannot occupy the guard and prevent its 10-second self-process-exit fallback. The abort marker is best-effort; absence is not proof that an abort never occurred. Synchronous DriveInfo disk-space probing before the failure latch remains a limitation; this scenario injects no I/O hang or physical pressure.
- After a manually initiated new Windows boot, the worker requires the unchanged ready proof and graceful-close marker; it does not recreate the old transaction proof. It commits an additional `post_boot` test row into each DB before publishing a unique post-start marker. Repeated unexpected restart state is rejected.
- Verify requires actual newer OS boot time, close time between ready and that boot, a matching newly created running GUID process, automatic start with no controller fallback, private View success, and fresh telemetry/snapshot progress. Protected PIDs may change across boot; configuration/accounts/paths/hashes must remain identical, processes must originate after the new boot, and fresh PIDs must remain stable within each operator stage.
- Before final DB checks, only the exact verified GUID registration is Disabled and Stopped; recovery remains none. Its old process must be gone. Both DB files are held with read-only handles. The pinned Python executable receives checker code through stdin, never `-c`; connections use `mode=ro&immutable=1`, `query_only=ON`, in-memory temp storage and a 10-second SQL progress deadline. Nonempty WAL/journal or reparse paths are rejected, never ignored/repaired. Parent process deadlines are 40 seconds for probes and 30 seconds for Python.
- Both DBs require `quick_check=ok`, original committed proof, actual post-boot commit and no pending row. Host DB additionally requires all acknowledged snapshot IDs and post-boot telemetry. Snapshot preservation is explicitly N/A/null for notifications, which has no telemetry snapshots. Database hashes before/after inspection must match.
- Success and failure preserve registration/package/data/evidence; nothing is deleted. Failure after trusted ownership is established attempts bounded disable/stop even below the resource floor. Identity/trust mismatch never authorizes a guessed service control. Unknown quiescence is FAIL. There is no automatic reboot, shutdown, hard power-off, snapshot restore, security-policy change or old-artifact cleanup.

## Fresh regression and Release evidence

Evidence root: `%LOCALAPPDATA%\HostAgent-DEV-evidence\reboot-preparation-20261018T024858Z`.

Test-first C# RED runs demonstrate missing mode acceptance (2 vs expected 3 outside SCM), double disposal of the independent guard, missing post-boot commits, and two blocked-marker/blocked-stop callback regressions. The last two initially prevented stop/fallback; independent queued actions corrected the cause. Logs `worker-*.stdout.txt` and `worker-*.result.json` retain the observations. These are local behavioral tests with real SQLite fixtures and injected readers/callbacks; no actual SCM, low-memory pressure or reboot was used.

Final `validation-r3` runs used the account-local SDK, DOTNET_ROOT and DOTNET_ROOT_X64 `C:\Users\AgentPCdevtest\AppData\Local\HostAgent-dev-sdk`, under an independent unchanged 250 ms /768/512 MiB /20 GiB monitor. Every final command exited 0; monitor failure=null:

```powershell
& "$env:LOCALAPPDATA\HostAgent-dev-sdk\dotnet.exe" build HostAgent.sln -c Release --no-restore -m:1 /p:UseSharedCompilation=false
& "$env:LOCALAPPDATA\HostAgent-dev-sdk\dotnet.exe" test tests/HostAgent.Infrastructure.Tests/HostAgent.Infrastructure.Tests.csproj -c Release --no-build --no-restore --filter 'FullyQualifiedName~IsolatedScmRebootTests|FullyQualifiedName~IsolatedScmCrashProofTests' --logger 'console;verbosity=minimal'
& "$env:LOCALAPPDATA\HostAgent-dev-sdk\dotnet.exe" test HostAgent.sln -c Release --no-build --no-restore -m:1 --logger 'console;verbosity=minimal'
```

- Release build: 0 warnings/errors.
- Focused: **14/14**, 12 new reboot cases plus 2 existing crash-proof cases; no failures/skips.
- Full suite: **426/426**, Core62 / Infrastructure349 / Desktop15; no failures/skips.
- Windows PowerShell 5.1 `-NoProfile -NonInteractive -File tests/chaos/RebootPersistence.Tests.ps1`: **16 checks PASS**, including actual r8 manifest, exact GUID identity, protected hash continuity and boot acceptance/rejection.
- Same invocation for `tests/chaos/RebootRunnerBoundary.Tests.ps1`: **7 checks PASS**. They execute the real main catch/finally, real identity-only environment function with synthetic trusted preparation and 900 MiB rejection, reject protected cleanup, terminate a harmless timed-out Python child and roundtrip the actual SCM argv encoding. Privileged boundaries are simulated; no SCM operation occurs.
- Pinned Python `-I -B tests/chaos/test_reboot_checker.py -v`: **6 tests PASS**, actual checker over synthetic disposable SQLite DBs. It rejects persisted pending rows, missing ack, missing post-boot write/progress and nonempty WAL, and proves no changes to DB bytes or inventory.

Initial final-driver build attempt did not reach compilation: MSB1009 because ProcessStartInfo inherited a different working directory. Setting the driver's process current directory to the VM repo corrected that orchestration issue. A preliminary suite then passed424 before the two additional blocking-guard tests/fix; it is historical relative to final426. An initial Python test-fixture cleanup error (SQLite context manager did not close handles) was corrected with explicit closing; it was not a product failure or accepted TDD RED.

Host Agent Support performed independent read-only code reviews. Findings in the preparation were corrected: cleanup eligibility before resource rejection and after late monitor failure; bounded child processes; protected within-stage PID continuity; Python provenance; stage canonicalization; actual notifications post-boot proof; and independence of abort stop/fallback from blocked marker/callbacks. This finite review does not establish actual Windows reboot success.

## Final candidate and read-only preflight

New separate candidate `C:\Users\AgentPCdevtest\Documents\ChatGPT\DevTest VM PC\HostAgent-DEV-SCM-candidates\scm-release-candidate-20261018-reboot-r9` has **75 payload files**, manifest SHA256 **AA1F5AAD8F4EC4C4EA70CBF88882E55588F7DE19F04E62B10FA95B2D7A569366**. All payloads were copied from the final tested Release output. Only `HostAgent.PipeProbe.dll` and `.pdb` differ from r8; product/runtime payloads remain byte-identical. R8 and older candidates were preserved.

Helper catalog `scripts/chaos/reboot-resilience-manifest.json` SHA256 **78BA190BD36AA355CEAD38C9B9BCC89C5824A8A33CE5104B8B525E1A8E9BB66E** pins six execution helpers; final runner SHA256 **B8382FCFEE676BDB28F3D02FE5249BDB00052D4AE0735610D6FA93B23779DE3C**. Candidate/catalog pins are embedded in the runner; prepared evidence binds its exact runner hash. The verified Python path remains `C:\Users\AgentPCdevtest\.cache\codex-runtimes\codex-primary-runtime\dependencies\python\python.exe`, SHA256 **B07B03B9BA866A341A3C13EE69710FBC8D43D4CE2523DEE13F6DF5C25378D15B**. System runtime host SHA256 **ABCE6623A6729059CF39BB0F728C4A7804F625895099C88C17E6E704661E09EB**.

Under the independent monitor, r9 no-operation bootstrap returned expected exit2, then the exact read-only command below exited0, with **1745.2 MiB free RAM /26.78 GiB C:**, protected HostAgent Running/PID3488 and HostAgent.Helper Running/PID3296 with pinned configuration/path/account/hashes. No fault or service operation was invoked. Evidence `r9-bootstrap`, `readonly-preflight`, `artifact-checks.json`.

Independent Host Agent Support artifact review rehashed all75 payloads plus manifest, compared SHA/bytes to the tested Release output, rejected extra/reparse inventory, confirmed all six helper/catalog pins and r8 preservation, and found no further blocker in the reviewed scope. Afterward, a one-line early elevated-token check was added before prechecks for Execute Prepare/Verify; this avoids unprivileged cleanup attempts before the existing later check. The seven PS boundary checks were rerun and passed. **Prepare without Execute** read-only preflight also exited0 under the independent monitor: **1605.0 MiB /26.78 GiB**, same protected PIDs and pinned configuration/hashes; monitorfailure=null, preceding runner revision `6E2F158B24D96205DF6CC753A26C31F51A6B0661F2A1A8BC1F40E2CAA1D2B657`. Evidence `final-readonly-prepare/result.json` and stdout. No C# or payload changed after the final426 suite; no actual Execute was run. `git diff --check` passed.

Last startup review replaced the single initial DB read with a bounded90s readiness wait, also used for post-boot progress. Only the child's explicit exit1 (uninitialized/unhealthy read during startup) is retried while the exact service remains Running; other exit codes, identity/resource failures and the remaining parent deadline fail closed. Child timeout is capped by remaining readiness time. Two behavioral regressions exercise initial not-ready then healthy and immediate rejection of a nontransient exit; the real runner boundary script now passes **9 checks**. This is a PowerShell harness correction/validation, not a new C# or product change. Latest **handoff Prepare without Execute** preflight exited0, monitorfailure=null: **1778.3 MiB /26.78 GiB**, unchanged protected PIDs3488/3296, final runner SHA **B8382FCFEE676BDB28F3D02FE5249BDB00052D4AE0735610D6FA93B23779DE3C**. Evidence `handoff-readonly-prepare`. The earlier runner digest in `final-readonly-prepare` belongs to its preceding seven-check revision, not this final readiness revision. Neither invoked Execute.

Host Agent Support additionally reviewed the final readiness changes and independently confirmed final runner/candidate/catalog hashes. It found no new blocker in that finite code review. The90s readiness budget includes integer rounding/poll/provider/child-cleanup overhead; it is not a subsecond absolute timing guarantee. Actual S5 remains unperformed.

```powershell
& 'C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe' -NoProfile -NonInteractive -File 'C:\Users\AgentPCdevtest\Documents\ChatGPT\DevTest VM PC\HostAgent-DEV\scripts\chaos\run-manual-reboot-persistence.ps1' -Stage Preflight
```

## Operator gate, still NOT RUN

Run Prepare once as administrator **inside the DEV VM**. Continue only if it prints `PREPARED_MANUAL_RESTART_REQUIRED` and READY; otherwise preserve output and do not initiate the test restart.

```powershell
& 'C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe' -NoProfile -NonInteractive -File 'C:\Users\AgentPCdevtest\Documents\ChatGPT\DevTest VM PC\HostAgent-DEV\scripts\chaos\run-manual-reboot-persistence.ps1' -Stage Prepare -Execute
```

Within five minutes select Windows Start > Power > Restart **inside HostAgent-DEV**. After sign-in, within ten minutes run Verify as administrator in the VM:

```powershell
& 'C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe' -NoProfile -NonInteractive -File 'C:\Users\AgentPCdevtest\Documents\ChatGPT\DevTest VM PC\HostAgent-DEV\scripts\chaos\run-manual-reboot-persistence.ps1' -Stage Verify -Execute
```

Return both final JSON summaries, especially Verify outcome/failureCode/cleanup/databaseChecks/resultPath. No database contents are needed. An expired or failed run is not reusable and does not become PASS through rerunning Verify. Preserve it for diagnosis. Whole-VM hard power-off is outside this procedure. No readiness verdict is advanced before actual results and final evidence consolidation.

## Operator normal reboot succeeded; second Verify was a repeat — 2026-10-18

Fresh read-only investigation of the operator's later `reboot_service_identity_mismatch` found that the **first** Verify had already completed **PASS**. Nothing was repaired or rerun. All original preparation/result files were preserved.

- Run ID: `c60759c492a840f7b993afc807e7339a`.
- Preparation: `result-prepare-20261018T033146089Z.json` reports `PREPARED_MANUAL_RESTART_REQUIRED`, successful create/start, first PID6136. `prepared.json` SHA256 **C3EC915520CFD438447FDCAC2A53315E82EE8A6938DC90D1E7E2D24CBFF43470** binds final runner B8382FCF…, helper catalog78BA190B… and r9 AA1F5AAD…; freshly rehashed files still match all three exact identities.
- First Verify: `%LOCALAPPDATA%\HostAgent-DEV-evidence\reboot-c60759c492a840f7b993afc807e7339a\result-verify-20261018T034035614Z.json`, SHA256 **0550F09BC5FF76599B27DEF0B442995BFCC86ECBFCF93A38E0AA9FCA6EE372DB**. Outcome PASS, failureCode=null, childExitCode0, cleanup `GUID_service_Stopped_Disabled_package_and_data_preserved`.
- Actual OS boot identity changed from **2026-10-18T02:24:35.5000000Z** to **2026-10-18T03:33:40.5000000Z**. The acknowledged proof was ready at03:31:44.8892066Z. Exact GUID runtime PID changed6136→3516, and the runner's checked new-boot/graceful-close/post-start/process-creation binding succeeded without a controller start after reboot.
- Private View: attempt1, success,206ms. Both DB checks report `quickCheck=ok`, committed proof preserved, pending transaction absent and genuine post-boot write progress. All **6 acknowledged host telemetry snapshot IDs** were preserved. Notifications snapshot-ID preservation is correctly null/N/A; its committed/post_boot/pending and integrity checks passed.
- The original operator Verify reports protected configuration/hashes unchanged. Fresh read-only checks confirm approved VM identity, VBoxService Running, current boot time03:33:40.5Z, protected HostAgent Running/Auto/NT SERVICE\HostAgent/PID3484 and HostAgent.Helper Running/Auto/LocalSystem/PID3300 at their expected paths, all four protected service payload hashes and helper executable hash matching their pins.
- A **second** Verify at03:41:53.058Z produced retained FAIL `result-verify-20261018T034153058Z.json`, SHA256 **C74F16B8AA814CD7DBFD8743E4666D581A0ECD7C4A3AC1572472539A0782C96C**. The script's active Verify phase requires the GUID service startup mode Auto; it was already **Disabled / Stopped / PID0** after the first successful cleanup. Its name/account/image path remain the exact intended GUID identity. This repeat therefore failed the startup-mode guard. It does not invalidate the first completed PASS, and the second FAIL is not relabeled PASS. Its bounded cleanup again confirmed disabled/stopped state with evidence preserved.

The retained GUID data/package require elevation to enumerate from the current ordinary session. Codex did not elevate, reopen DBs, change services, alter ACLs, restart Windows, repeat faults or modify source during this investigation. Read-only integrity outcomes above are the actual operator runner's retained results, bound to its verified source/candidate; they are not claimed as a fresh independent DB quick_check run by Codex.

**S5 normal Windows restart / graceful rollback / automatic isolated service recovery is now PASS.** Abrupt power loss remains NOT RUN. Multi-day endurance, final release/installation qualification and any overall pilot decision remain outside this one-shot result. The last actual full Release suite remains426/426; it was not repeated for this documentation-only investigation.

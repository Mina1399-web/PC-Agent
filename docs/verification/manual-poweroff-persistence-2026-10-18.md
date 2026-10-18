# Manual hard VM power-off / persistence gate — 2026-10-18

## Status

Preparation only. Actual VirtualBox Power Off is **NOT RUN**. The normal Windows reboot run c60759c492a840f7b993afc807e7339a remains PASS; its r9 candidate, script, GUID service and evidence are preserved. No guest or host power API is called by the new script. No elevated service/fault operations were executed during preparation.

## Scope and guards

Approved VM AGENTPC / VirtualBox / UUID 46543236-8C5E-49DA-953C-DB17F9B1E402, account AgentPC\AgentPCdevtest; VBoxService Running. Fresh initial free RAM1851.6MiB. Branch codex/local-dashboard, HEAD6cd0ec8ba61ce1c7c7cd89787f02f86b9b87ed8e; dirty work preserved and original three modified test files backed up under `%LOCALAPPDATA%\HostAgent-DEV-evidence\poweroff-preparation-20261018T035043Z`. No source in src/ was changed by this preparation, no commits or host transfer.

New GUID f31c58c42ae14f9cbf4c083a592c264e, service `HostAgentDevScm_f31c58c42ae14f9cbf4c083a592c264e`, package `C:\ProgramData\HostAgent-DEV-SCM-f31c58c42ae14f9cbf4c083a592c264e`, own app/data/host-agent.db/notifications.db, own virtual service account and View pipe. Package root Admin/System only, service RX in app and write only in data; startup canaries verify no binary write/delete. No helper mutation, external delivery or real secrets; Shadow/DenyHelper graph.

Existing unchanged floors: preflight >=1024MiB free RAM; independent250ms soft<768MiB/hard<=512MiB; C:>=20GiB; arm90s and servicelease900s; safety stop/fallback remains scoped to own GUID. The inherited guard has a synchronous disk-space probe; this test does not qualify indefinite OS I/O hangs. Parent and child deadlines remain bounded.

The host-side operator must freshly verify the named snapshot `pre reboot save` UUID84c78c1b-61b9-4e41-a8ef-30db3a655ddf on exactly the approved VM UUID; clean-win-base3dec8924-291e-40f4-83bc-e424377fc2be is preserved. Prepare requires explicit operator confirmation and UTC verification timestamp <=30minutes old (future tolerance2minutes). This is an operator attestation, not a guest-side proof of VirtualBox state. Snapshot is never altered by this harness.

Hard power-off necessarily abruptly ends **all guest processes**, including the protected VM HostAgent. The script never directly controls that protected service, helper, configuration or data. It records their identity/configuration/hashes before, checks automatic Running state/configuration/hash continuity afterward, and does not open/repair protected databases. The host OS is never a fault target. Production physical power loss is outside scope.

## Evidence contract

A distinct poweroff-proof test mode preserves real private DB journal modes (host WAL, notification DELETE) and requires nonempty host WAL / notification rollback journal before READY, commits FULL-synchronous proof rows and holds two pending write transactions. Ready mode/runID/snapshot IDs are bound; ready JSON is flushed. Prepared record uses CreateNew+WriteThrough+Flush(true) and binds exact runner/catalog/candidate, boot identity and protected baselines.

After manual power-off/start, real ReadWrite SQLite opens perform normal journal/WAL recovery, write post_boot FULL commits and emit a bound startup marker. Verify requires a newer boot than ready, matching actual process/start time, poweroff-proof mode, no safety abort and **absence** of graceful-close marker. No controller start after boot. It verifies live View and new telemetry, stops/disables only exact owned GUID, then runs the reused checker through verified Python stdin (never -c), mode=ro&immutable=1/query_only with read locks. Both quick_checks must be ok, exact committed+post_boot proof set must remain and pending absent; all acknowledged host snapshot IDs and fresh post-boot telemetry must remain. Both database hashes are compared before/after read-only checks. Evidence/package/data/service registration are retained on success and failure; no automatic old artifact deletion or repair.

A guest new boot plus absent close marker alone cannot distinguish VirtualBox Power Off from another unclean stop; the **actual host-side Power Off action must also be supplied by the operator** before reporting the hard-power gate PASS. This does not prove interruption of a particular physical disk-sector write or host power failure. Unit restart fixtures intentionally perform clean transaction rollback; physical failure recovery remains live NOT RUN.

## Operator sequence

1. On the VirtualBox **host**, verify correct VM UUID and current rollback snapshot using VBoxManage. Copy verifiedAtUtc from the host check below. Do not recreate/restore/delete snapshots.
2. Inside DEV VM, elevated PowerShell, run Prepare with that timestamp and explicit confirmation. Continue only for PREPARED_MANUAL_POWEROFF_REQUIRED and READY; preserve failure output otherwise. Do not rerun Prepare on the same GUID.
3. Within5minutes, on the host select **HostAgent-DEV** in VirtualBox, choose **Close > Power Off the machine**. Do not choose Save state or ACPI shutdown; do not use Windows restart. Do not power off the physical PC. Then start the same VM normally (not Restore snapshot).
4. Sign in and within10minutes run Verify once elevated in the VM. Send complete final JSON plus confirmation that you used VirtualBox Power Off on HostAgent-DEV. If FAIL, preserve all evidence and stop; do not rerun Verify or repair databases. On PASS the own GUID is Stopped/Disabled and all evidence retained.

Host read-only check (manual; no power operation):

```powershell
& {
    $ErrorActionPreference='Stop'
    $vb=Join-Path $env:ProgramFiles 'Oracle\VirtualBox\VBoxManage.exe'
    if(-not(Test-Path -LiteralPath $vb -PathType Leaf)){throw 'VBoxManage_missing_on_host'}
    $info=@(& $vb showvminfo 'HostAgent-DEV' --machinereadable)
    if($LASTEXITCODE-ne0){throw 'VM_query_failed'}
    $snap=@(& $vb snapshot 'HostAgent-DEV' list --machinereadable)
    if($LASTEXITCODE-ne0){throw 'snapshot_query_failed'}
    $text=$info -join "`n";$tree=$snap -join "`n"
    if($text-notmatch '(?im)^UUID="46543236-8c5e-49da-953c-db17f9b1e402"$'-or
       $text-notmatch '(?m)^VMState="running"$'-or
       $text-notmatch '(?m)^CurrentSnapshotName="pre reboot save"$'-or
       $text-notmatch '(?im)^CurrentSnapshotUUID="84c78c1b-61b9-4e41-a8ef-30db3a655ddf"$'-or
       $tree-notmatch '(?m)^SnapshotName="clean-win-base"$'-or
       $tree-notmatch '(?im)^SnapshotUUID="3dec8924-291e-40f4-83bc-e424377fc2be"$') {throw 'host_VM_or_rollback_mismatch'}
    [pscustomobject]@{vm='HostAgent-DEV';rollback='pre reboot save';verifiedAtUtc=[datetimeoffset]::UtcNow.ToString('O');result='PASS'}|ConvertTo-Json
}
```

Guest Prepare:

```powershell
$hostVerifiedUtc=Read-Host 'Vloz verifiedAtUtc z uspesne cerstve host-side kontroly snapshotu'
& 'C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe' -NoProfile -NonInteractive -File 'C:\Users\AgentPCdevtest\Documents\ChatGPT\DevTest VM PC\HostAgent-DEV\scripts\chaos\run-manual-poweroff-persistence.ps1' -Stage Prepare -ConfirmHostRollback -HostRollbackVerifiedAtUtc $hostVerifiedUtc -Execute
```

Guest Verify (only after actual manual power-off/start):

```powershell
& 'C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe' -NoProfile -NonInteractive -File 'C:\Users\AgentPCdevtest\Documents\ChatGPT\DevTest VM PC\HostAgent-DEV\scripts\chaos\run-manual-poweroff-persistence.ps1' -Stage Verify -Execute
```

## Fresh preparation results

Initial preparation checkpoint (historical): final validation and candidate publication had not yet run. Final verified results follow below; no live power-off PASS is claimed.
The new PowerShell transition acceptance was observed RED against the original graceful-only logic (`reboot_graceful_rollback_not_proved`), then GREEN for true absent-close abrupt transition plus rejection cases. Rollback attestation and durable-record guards have behavioral validation after implementation; they are not claimed as separate test-first RED cycles. PowerShell5.1 checks:14 module cases and11 real runner boundary cases (missing host proof before mutation, owned/untrusted cleanup, late resource abort, native argv/deadline, readiness retry, durable create-only record). Original normal-reboot PS16 +boundary9 and Python checker6 are retained regression checks.

Test-only C# test-first evidence includes parser/behavior RED, actual SQLite BUSY contention RED (writer held4.5s, Error5) and notification journal-default RED (expected delete, previous unpublished proof wrongly forced wal). The final poweroff fixture preserves the real journal modes. The confirmed defect was in the new test harness startup proof, not an independently reproduced product defect. Its fix retries only SQLite BUSY/LOCKED5/6 per database with shared monotonic20s retry window,1s SQLite attempt timeout and100ms cancellable delay; a final in-progress attempt can overshoot by approximately1.1s, so this is not an absolute20s wall-clock guarantee. Successful first-DB insert is not replayed when the second DB is busy. All21 focused cases passed under the unchanged monitor; actual physical power-off remains NOT RUN. Earlier unpublished first validation had431/431 PASS; subsequent contention regressions require the final433 suite below.

RED/GREEN worker evidence remains in `%LOCALAPPDATA%\HostAgent-DEV-evidence\reboot-preparation-20261018T024858Z\poweroff-*`; parent final evidence is `%LOCALAPPDATA%\HostAgent-DEV-evidence\poweroff-preparation-20261018T035043Z\validation-r2`. Both initial validation-r1 and originals are preserved.

Independent Host Agent Support review found and parent reproduced one further harness race: SCM Running can precede the after-start proof marker while startup writes are still busy. `marker-readiness-red.txt` records the old immediate-read failure (`reboot_marker_missing_or_large`). Final runner waits bounded60s for SCM Running and bounded45s for the atomic after-start marker with identity/Running/safety checks each poll, immediate graceful-close rejection and a fresh no-reparse check before reading. Four safe PS5.1 behavioral cases passed (temporary missing marker, Stopped, graceful marker, hard safety rejection); no actual service was controlled by those fixtures. The marker checker was added during the final C# suite; no C# or candidate binary changed after that suite's build. Final PS checks and preflight are run on the final runner separately below.

### Final verified preparation and immutable candidate

Final build exited0,0warnings/0errors. Focused21/21 PASS. Full Release suite **433/433 PASS** (Core62, Infrastructure356, Desktop15),0failed/0skipped; all eight final validation steps exit0, independent monitorfailure=null. Final PS handoff reran14module guards,11runner boundaries and4marker-readiness cases; allPASS and monitorfailure=null. Reused Python checker6 cases passed. Candidate no-operation bootstrap returned expected exit2, not a service start.

Exact Release commands (account-local verified SDK10.0.401 at `%LOCALAPPDATA%\HostAgent-dev-sdk\dotnet.exe`, DOTNET_ROOT and DOTNET_ROOT_X64 set accordingly):

```powershell
dotnet build HostAgent.sln -c Release --no-restore -m:1 /p:UseSharedCompilation=false
dotnet test tests/HostAgent.Infrastructure.Tests/HostAgent.Infrastructure.Tests.csproj -c Release --no-build --no-restore --filter 'FullyQualifiedName~IsolatedScmRebootTests|FullyQualifiedName~IsolatedScmCrashProofTests' --logger 'console;verbosity=minimal'
dotnet test HostAgent.sln -c Release --no-build --no-restore -m:1 --logger 'console;verbosity=minimal'
```

Each script regression was run in Windows PowerShell5.1 via `powershell.exe -NoProfile -NonInteractive -File tests/chaos/<test>.ps1`: PowerOffPersistence.Tests.ps1, PowerOffRunnerBoundary.Tests.ps1, PowerOffMarkerReadiness.Tests.ps1, RebootPersistence.Tests.ps1, RebootRunnerBoundary.Tests.ps1. Checker tests used the pinned Python `-I -B tests/chaos/test_reboot_checker.py -v`. Build/focused/full driver and exact output/exit/durations are in validation-r2; final PS/source-pinned read-only handoff checks in final-handoff-checks.

Published DEV-only candidate:
`C:\Users\AgentPCdevtest\Documents\ChatGPT\DevTest VM PC\HostAgent-DEV-SCM-candidates\scm-release-candidate-20261018-poweroff-r10`.
Manifest SHA256 **9575B42059B48BED39076241878060753A604019B5D399AAB5D83392836F6E08**,75payloads+manifest. All75 payload SHA/bytes/inventory/reparse bounds verified and compared with the final tested Release output. Only PipeProbe.dll/PDB differ from r9; product payloads identical. R9 and earlier candidates unchanged.

`poweroff-resilience-manifest.json` SHA256 **E0CD094A873F342053770329EBD5A02FD7C5665BAD57908752FD6B9791E785AE** pins six helpers; runner `run-manual-poweroff-persistence.ps1` SHA256 **55BD8147833ACEFE6A4C5827D18D245876DABC21A0E84883F69480570B3603C5** pins candidate/catalog and prepared evidence binds its source.

Final actual read-only `-Stage Prepare` **without Execute** exited0 under the independent monitor: **2051.8MiB free RAM /26.78GiB C:**, protected HostAgent Running/PID3484 and helper Running/PID3300, same pinned paths/accounts/startup/hashes. New GUID registration/package/evidence remain absent afterward; no elevated SCM, power, ACL or database operation took place. No production host changes. Historical normal-reboot runner SHA256 remains B8382FCFEE676BDB28F3D02FE5249BDB00052D4AE0735610D6FA93B23779DE3C; r9 manifest remains AA1F5AAD8F4EC4C4EA70CBF88882E55588F7DE19F04E62B10FA95B2D7A569366.

**Prepared for operator handoff; hard power-off gate still NOT RUN.** Fresh host-side rollback attestation is required immediately before actual Prepare; the guest preflight cannot establish it. No overall READY/pilot verdict is advanced by preparation. `git diff --check` passed; no commits or host transfer.


Host Agent Support final independent read-only review confirmed all75 r10 payload hashes/bytes against final Release, full76-file inventory/no reparse/extras, catalog andsixhelperpins, runnerhash andfinalmarkerwait guards. R9full75payloads andhistoricrebootrunner independently rehashed unchanged. No new actionable finding in that finite review. It performed no builds/tests/faults/mutations. Parent final preflight above is separate actual evidence, not inferred from review.

## Fresh post-power-event verification — 2026-10-18

**Guest recovery Verify PASS; exact host-side Power Off action still awaits explicit operator confirmation.** Fresh read-only inspection found `result-verify-20261018T044414870Z.json` for run f31c58c42ae14f9cbf4c083a592c264e. SHA256 **51127969B12B20E967580CE870B5D304CF05202E0DFA705BBFFAD43476DC041F**. Prepared record SHA256 **17CE001A740CE33CB411C2516839FC625CFBA51B9E42C52E9544ABC7DD932DAE**. Original evidence was preserved; no Verify or fault was repeated.

Actual Verify reports PASS/failureCode=null/childExit0. Boot changed03:33:40.5Z→04:39:15.5Z, ready04:36:21.5564139Z, own runtime PID6492→3480, private View attempt1 successful201ms. Both databases report quickCheck=ok, committed proof preserved, pending absent and post-boot writes present; all6 acknowledged host snapshots preserved (notifications snapshot preservation null/N/A). Cleanup confirms GUID service Stopped/Disabled with package/data/evidence retained. These are retained actual elevated operator-run results, not newly rerun SQLite checks by Codex.

Fresh ordinary-session read-only checks confirm approved VM UUID/VirtualBox/VBoxService and actual boot04:39:15.5Z; protected HostAgent Running/Auto/PID3536, helper Running/Auto/PID3264; exact own GUID Stopped/Disabled/PID0 and matching image/account. All75 candidate payloads and manifest, runner/catalog/six helper hashes match the prepared bindings. Protected executable/three service assemblies/helper executable hashes match their prepared pins. HEAD6cd0ec8ba61ce1c7c7cd89787f02f86b9b87ed8e, branch codex/local-dashboard; dirty work preserved. No service controls, elevated command, DB open/repair, source change, build/test rerun or host operation performed during this inspection.

The successful guest proof establishes a new boot without the graceful-close marker and recovery of the prepared transactions. It cannot independently distinguish VirtualBox Power Off from another unclean stop plus reboot. Obtain the operator's explicit confirmation of the host-side action before closing the hard-power gate. No overall release/pilot verdict is inferred.

## Operator confirmation closes hard-power gate — 2026-10-18

The operator answered affirmatively to the explicit question whether the VM was powered off through VirtualBox Power Off rather than a normal Windows restart (reply: "Ano přes vm"). This is operator-provided host-action evidence, not an independently observed VirtualBox host event. Combined with the retained Verify PASS for f31c58c42ae14f9cbf4c083a592c264e (SHA25651127969B12B20E967580CE870B5D304CF05202E0DFA705BBFFAD43476DC041F, freshly unchanged), the **manual VirtualBox hard power-off / isolated persistence recovery gate is PASS within its declared scope**. Earlier preparation NOT RUN / host confirmation pending statements above are historical checkpoints, now superseded by this entry.

No fault, build/test, database check or service operation was repeated for this confirmation. No source or protected installation was changed. This result does not qualify physical host power loss, interruption of a specific disk write, multi-day endurance or overall release/installation readiness. No overall pilot verdict is advanced by this confirmation alone.

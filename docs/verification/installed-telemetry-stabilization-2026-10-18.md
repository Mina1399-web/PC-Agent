# Installed telemetry stabilization — 2026-10-18

## Scope and environment

Fresh work on `codex/installed-telemetry-stabilization`, source base
`37f749032984453a38677ea8c58b5ecbd47fbe4a`. AGENTPC / AgentPC\\AgentPCdevtest,
VirtualBox UUID `46543236-8C5E-49DA-953C-DB17F9B1E402`, VBoxService Running.
Initial fresh free memory 1411.5 MiB. Builds/tests use SDK 10.0.401 in the DEV
VM only; initial 1024 MiB, independent 250 ms monitor with soft <768 MiB,
hard <=512 MiB and C: >=20 GiB remain unchanged. The complete source bundle
at the source base was verified before creating the new branch. No reset,
installed service control, elevated operation, policy change or host transfer.

## Fresh installed acceptance before source fixes

Operator reported `installed=true`, `restartRequired=false`. Read-only evidence
in `%LOCALAPPDATA%\\HostAgent-DEV-evidence\\installed-acceptance-20261018T145618799Z`:

- Installed version 1.2.2; all 1200 installed payload files match the R3 pinned
  manifest SHA-256 `27D0DD2D565915EBA648AAF4C4DB428E48B10B1B8C721B3550B8ADE9C7F88B02`.
- HostAgent Running/PID6592/NT SERVICE\\HostAgent; helper Running/PID1656/LocalSystem.
- Three authenticated production View `overview` requests succeeded in
  1379/1064/1082 ms. CPU/RAM timestamps advanced and pre-upgrade history remained
  accessible. This does not prove preservation of every incident or journal row.
- Shadow/read-only remained active. Degraded was true: process metrics/paths and
  disk I/O were partial. SMART is explicitly optional. No false full-health claim.

## Confirmed defect and test-first correction

The specification requires executable paths only when accessible. The CPU
collector already permits null paths, but a path-only process/service warning
degraded aggregate health. `AgentRuntimeHealth` now treats exclusively the known
path warning on a Partial process-services result as an informational capability
code `optional_process_paths_unavailable`. The original bounded reason remains
visible. Mixed/unknown/mandatory failures and other statuses remain degrading.

Exact focused command (pinned SDK executable, Release):

```powershell
dotnet test tests/HostAgent.Infrastructure.Tests/HostAgent.Infrastructure.Tests.csproj -c Release --no-restore -m:1 /p:UseSharedCompilation=false --filter FullyQualifiedName~HealthStabilizationTests --logger 'console;verbosity=detailed'
```

RED 22 PASS / 1 expected FAIL (actual SQLite/Dashboard projection reported
Degraded=true); GREEN 23/23 PASS, both exit/monitor receipts retained. Fresh logs
are `installer-validator/telemetry-path-{red,green}-20261018.*` under the preserved
`predeployment-20261018T045333Z` evidence parent; these labels are new runs, not
historical predeployment results. Both monitorFailure=null.

## Precise process failure evidence

Both process metric catch sites previously collapsed native access denial,
other Win32 errors, exited-process races and unsupported APIs into one code.
New internal classifier maps only native Win32 code 5 to
`some_process_metrics_access_denied`; other causes keep the original generic
warning. No exception message or process identity is added. The precise warning
remains Partial and degrading; no permission or authorization change.

```powershell
dotnet test tests/HostAgent.Infrastructure.Tests/HostAgent.Infrastructure.Tests.csproj -c Release --no-restore -m:1 /p:UseSharedCompilation=false --filter FullyQualifiedName~ProcessMetricFailureTests --logger 'console;verbosity=detailed'
```

RED 2 PASS / 6 expected FAIL (classifier contract missing, no compile/setup
failure); GREEN 8/8 PASS. Both monitorFailure=null. Live read-only collectors in
the ordinary test process returned usable memory/process/service data (98
processes, 301 services in that run). CPU produced both exact access-denied and
generic warnings: access denial must not be assumed to explain every failure.
This is not execution under the installed service account.

Evidence root for these and final source validation:
`%LOCALAPPDATA%\\HostAgent-DEV-evidence\\installed-stabilization-20261018T151255642Z`.
Classifier receipts/logs: `process-classification-{red,green}`. Independent
read-only review of the optional-path fix found no actionable defect.

## Disk investigation and operator probe preparation

Host Agent Support's fresh non-elevated native PDH comparison succeeded for
the exact three English PhysicalDisk(_Total) paths, including open/add/two
samples/format/CStatus/close. This disproves a currently missing or disabled
counter in that interactive context; it does not prove service-token access.
Querying the installed service process from the ordinary token was denied
with Win32 5; that is a process-query error, not a measured service PDH error.
No group membership, privilege, counter registration or ACL was changed.

`scripts/diagnostics/Test-InstalledDiskCounters.ps1` is an operator-run read-only
probe, not an executed installed acceptance result. It checks VM/account/admin,
exact running service identity/PID before and after, pinned Python, resources,
then compares native PDH stages and reads only the allowlisted latest persisted
disk reason. It quick-checks the three fixed installed databases with mode=ro,
query_only, memory temp store, existing WAL/SHM guards, size/time limits and
parent memory cancellation. It never uses immutable for live databases, never
requests SQL data/schema/checkpoint/recovery writes and never controls services.
SQLite may internally coordinate existing WAL SHM; no absolute filesystem
immutability claim is made.

Script SHA-256 `DE8A80421C5ADD8538AC3DC4D963CB95E1C978458D2CF5189F06D94FA763535C`.
PS5.1/PS7 syntax parsing and embedded Python AST checks passed. Six isolated
fixture checks passed: missing DB without creation; read-only known reason
and unchanged DB; unknown reason redaction; existing WAL+SHM; absent sidecars
without creation; size cap. Reproduction:
`%LOCALAPPDATA%\\HostAgent-DEV-evidence\\disk-probe-script-review-20261018\\validate-syntax-and-fixtures.ps1`,
with `validation.log` and `validate-fixtures.py` retained. Main also verified the
final script rejects a non-elevated token before any native/DB probe (expected
exit1/elevated_operator_required); receipt `probe-unelevated-guard.json`.

## Final validation and limits

Release build completed with zero warnings/errors (27.0 s, exit0). The complete
Release suite passed 466/466 (Core62, Infrastructure389, Desktop15), zero failed
or skipped, 341.9 s, exit0. Both ran under monitorFailure=null. Tested product
source hashes were captured before the run and checked afterward unchanged;
the separately validated diagnostic script was excluded from that source ledger.
The plan/specification and this result record were finalized afterward.

```powershell
dotnet build HostAgent.sln -c Release --no-restore -m:1 /p:UseSharedCompilation=false
dotnet test HostAgent.sln -c Release --no-build --no-restore -m:1 --logger 'console;verbosity=minimal'
```

Actual pinned executable `%LOCALAPPDATA%\\HostAgent-dev-sdk\\dotnet.exe`, with
DOTNET_ROOT/DOTNET_ROOT_X64 set to its directory and serialized build servers.
Reproduction wrapper `run-release-validation.ps1`; receipts `release-validation/
results.json`, `monitor.json`, `tested-source-files.json`, and each child stdout/
stderr. All validation was unelevated in the approved VM, not on the host.

The installed R3 candidate is deliberately unchanged; six key installed image/
assembly hashes were rechecked against the pinned manifest and matched, with
the same two Running service PIDs. Receipt `installed-after-source-tests.json`.
The original manifest import probe hit PowerShell data-file evaluation limits;
the completed check uses bounded literal-entry parsing and does not treat the
import error as an installed hash mismatch.

Open: elevated installed DB quick-checks and persisted disk failure stage;
exact disk native status under service identity; new versioned candidate and
operator-controlled upgrade/live acceptance of these source fixes; explicit
target allowlists and operation approval acceptance before Confirmation.
Global health is not itself a RemediationCoordinator authorization gate: fixing
an indicator does not enable actions. No blanket full-access mode, no silent
switch, no assertion that every warning has been eliminated or product is 100%.
Earlier chaos PASS records remain historical evidence for their own candidates.

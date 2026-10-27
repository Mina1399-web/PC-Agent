# DEV completion and Confirmation preparation — 2026-10-27

## Scope and preserved state

The owner authorized completing the remaining application work, excluding
signing, and moving the approved DEV installation from Shadow/read-only to
the supported writable **Confirmation** mode. This is not authorization for
unattended approval, arbitrary targets, or production-host changes. New
installations retain the safe default; each typed operation still needs its
own registered target and exact, fresh approval.

Environment: AGENTPC, AgentPC\\AgentPCdevtest, VirtualBox UUID
46543236-8C5E-49DA-953C-DB17F9B1E402, VBoxService Running. The working token
was not elevated. Initial source was codex/desktop-localization at
4ee4dc176ccee29244ef2ea02eb3052261481146. The existing three modified/untracked
paths were copied to an external evidence return point before edits. The
prior final-acceptance document's uncommitted change remains preserved.

Evidence and durable progress are outside the repository at
`%LOCALAPPDATA%\HostAgent-DEV-evidence\completion-20261027`. Detailed child
stdout/stderr and watchdog receipts for the runs below are in the existing
`desktop-localization-20261027` evidence directory. Tools used actual SDK
10.0.401 and process-local DOTNET_ROOT/DOTNET_ROOT_X64/PATH. The authorized
build/test guards require more than 768 MiB initially, warn below 768 MiB,
abort only their own child tree at/below 512 MiB, and require 10 GiB disk.

## Confirmed recovery defects and changes

1. The earlier offline-copy diagnostic attempted
   `Start-Service -NoWait`. That parameter is unsupported by Start-Service.
   Both failed restorations in the owner-supplied result are consistent with
   this exact binding failure; subsequent native SCM starts restored them.
   The unsupported argument has been removed.
2. The diagnostic formerly checked service identity only after starting it.
   It now checks the fixed name/account/path/start mode and pinned executable
   hash immediately before restoration, including after a pending stop. A
   changed identity is not started. Final identity and Running checks remain.
3. The diagnostic formerly restored based on a preflight Running snapshot
   even if it had not requested a stop. Per-service StopRequested is now set
   immediately before that run's SCM stop request; only those services are
   eligible for restoration. A partial stop failure still receives recovery.
4. The product CLI's service lifecycle aborted startup after the first helper
   open/start/wait failure, leaving the independent monitor unattempted. It
   now tries both fixed product services and then reports an aggregate of
   failures. Stop remains non-cascading and fail-fast, and cancellation is
   still honored. This fixes CLI configuration/recovery behavior without
   broadening any operation or approval.

The PowerShell regression executes the actual diagnostic's finally AST with
only privileged SCM/file-hash boundaries replaced. It covers original service
states, a failed start, an accepted-but-incomplete start, account/path/hash
drift, and a preflight that did not request any stop. It does not execute the
whole privileged diagnostic or prove native SCM recovery. The CLI tests use
the real lifecycle with fake service handles and assert the second service
is attempted and every failure is retained.

## Recorded red/green and current checks

The focused command was:

```powershell
dotnet test tests/HostAgent.Infrastructure.Tests/HostAgent.Infrastructure.Tests.csproj -c Release --no-restore --filter 'FullyQualifiedName~InstalledDatabaseRecoveryTests|FullyQualifiedName~ProductServiceLifecycleTests' -m:1 -p:UseSharedCompilation=false -nr:false -p:BuildProjectReferences=false
```

Dependencies were built before this command. After changing the CLI source,
its Release project was rebuilt before rerunning the same focused command.

| Check | Actual result |
| --- | --- |
| Initial attempt | Watchdog stopped the child at 511.1 MiB before tests; not behavioral RED |
| First focused compile | Test-only ambiguous TimeoutException reference; corrected, not behavioral RED |
| Focused RED `daa1bb067fab46279d1c6ee6d40d7b4c` | 7 failed / 3 passed, exit 1; rejected restart parameters and missing monitor restart attempt |
| Parameter-only fix `fc05c2b10f2a4d66b11312b0e6cbdd24` | 5 failed / 5 passed, exit 1; account drift started before checking; no-stop preflight unexpectedly restored services; CLI recovery failures retained as RED |
| Focused GREEN `230ac1f5135a42cc8d01e245cd59789e` | 10 passed / 0 failed/skipped, exit 0; min RAM 711.0 MiB |
| Release solution build `f100cce5035d440aaa7b65d431639da8` | Exit 0, zero warnings/errors; min RAM 529.8 MiB; no intervention |
| Expanded recovery/configuration/approval/process-metric focused run `cb8c9e9a26674834be778bdb5ed1e7f5` | 64 passed / 0 failed/skipped, exit 0; min RAM 547.1 MiB; no intervention |
| Complete three-assembly Release suite `214c58fe1eaf477bb485f52e9de574dd` | **681 passed / 0 failed / 0 skipped**, exit 0: Core62 + Infrastructure581 + Desktop38;362.4s, min RAM580.6MiB, min C:14.592GiB; no intervention |

The expanded run includes additional guard coverage added after the first
green run; these extra cases are coverage, not separately observed RED cycles.
Independent read-only review of the recovery diff found no remaining blocking
P1/P2 finding. The review explicitly noted that these tests are not full native
SCM acceptance. The complete current suite passed, including the fixture
builder previously blocked by its old15GiB floor. Later operator results
must still be recorded separately when they actually finish.

## Current installed baseline and metric limits

A fresh read-only query found installed version 1.2.8: HostAgent Running,
Auto, NT SERVICE\HostAgent, PID10448; Helper Running, Auto, LocalSystem,
PID10776, both at the expected Program Files paths. The executable hashes
were A4C8FEAD…FFB8C and 96E804D8…B8A99D; the corresponding service assembly
hashes were A7C67CA2…B3B04B and 38F5337D…E94B. The complete values are in the
external `installed-baseline.json`. No service control was performed. A fresh
authenticated installed View overview succeeded on attempt1 in103ms
(`368e2274a952486aa57ebc5229c0fc92`, exit0, min965.8MiB). This bounded probe
printed transport status only, without the snapshot payload; it establishes
current View availability, not the completeness of its metric values.

The previously documented main/notifications database integrity results and
subsequent service recovery remain in
[the installed diagnostic record](installed-readonly-diagnostics-2026-10-26.md).
This source run has not reopened those databases. The absent, lazily created
helper journal stays NOTRUN; enabling a mode does not create or validate it.

The access-denied reasons identify individual process CPU-time reads, and
best-effort executable-path reads. They do not establish failed system CPU or
physical-memory APIs. Independent support source review confirmed that the
runtime's CPU-time read already uses QUERY_LIMITED_INFORMATION; replacing it
with the same native API would not reduce access rights. The exact process
and protection causing the installed denial are not established. These
limitations remain honestly partial/degraded; no debug privilege, broader
service identity, ACL change, or warning suppression was introduced. A
potential QueryFullProcessImageName fallback is an unproven improvement,
not a claimed fix for the installed warning.

## Remaining live boundaries

- New localized package/installed-language acceptance is not established by
  the source WPF fixtures. The installed preview is still1.2.8.
- Writable Confirmation is authorized by the owner but has not been applied
  in this non-elevated session. Its acceptance must verify both protected
  configurations, unchanged target registrations/kill switches/network
  choices, installed hashes, fresh mode readback, and both Running services.
- Mode acceptance does not prove a real installed helper action. Exact
  allowlisted-target approval, audit/journal and recovery remain a separate
  live result; historical private-GUID helper PASS has its own scope.
- Historical reboot, power-off, storage, collector and30-minute soak gates
  retain their scoped PASS results; see
  [the final resilience assessment](resilience-final-2026-10-18.md). They were
  not repeated here and do not imply days-long or final installed endurance.
- External notification delivery remains disabled/unverified. Signing is
  excluded from this increment. No production-host deployment is performed.

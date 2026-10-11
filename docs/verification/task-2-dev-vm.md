# Task 2 verification in the approved DEV VM

Date: 2026-10-11 (Europe/Prague). Status: Task 2 code, tests and source-review gates passed in the VM; synchronization to the host is blocked. Task 3 was not started.

## Environment and source provenance

- Execution hostname `AGENTPC`, account `AgentPC\AgentPCdevtest`, model `VirtualBox`, manufacturer `innotek GmbH`, Windows 11 Pro. User explicitly confirmed this is the intended AgentPCtestdev / HostAgent-DEV environment before changes.
- VM UUID `46543236-8C5E-49DA-953C-DB17F9B1E402`. VirtualBox Guest Additions `7.2.20r175154`; `VBoxService` and `VBoxGuest` both running. No VM configuration, snapshot, host security policy or service installation was changed.
- Initial workspace `C:\Users\AgentPCdevtest\Documents\ChatGPT\DevTest VM PC` contained an empty Git repository with no commits or remotes. No solution/project copy was found in accessible VM filesystem locations.
- Working copy created at `C:\Users\AgentPCdevtest\Documents\ChatGPT\DevTest VM PC\HostAgent-DEV`. Current private source was retrieved through the connected GitHub reader from `Mina1399-web/PC-Agent`, pinned commit `188e3e259519e8713cee7aed84fcec7e40aa408c`. All 69 original blob hashes were verified locally, including original solution bytes. GitHub `main` was rechecked on this date and still referenced that commit.
- Return point `ab0bc9b3352a68b206b92b64e53e267b38d7691b` preserves the exact imported source. Implementation branch is `codex/task-2-dev`; subsequent local commits preserve the work. No existing host or guest working tree was reset, stashed or overwritten.
- This establishes correspondence to the current connected repository. The host working tree and any newer uncommitted host changes are not accessible, so equality with the live host `C:\PC Agent` cannot be asserted.
- .NET SDK `10.0.100` installed only in `C:\Users\AgentPCdevtest\AppData\Local\HostAgent-DEV-tools\dotnet` using Microsoft's `dotnet-install.ps1`, with process-local PATH. No global SDK, test policy exception, signing workaround or security weakening was used. Fresh focused tests loaded locally built assemblies successfully.

## Implemented and verified behavior

- SCM 7031/7034 evidence requires a known timestamp between the observation time and exactly 24 hours earlier. Old, future and undated evidence is rejected. Affected service metadata is extracted only from bounded structured SCM `param1` data; XML DTD/external resolution is prohibited. Raw event XML is not persisted.
- Known services use normalized service correlation keys; display names resolve through collected service metadata when available. Crash and stop evidence for the same service do not duplicate diagnoses. Different services are distinguished even with equal or missing record IDs. Unidentified events use event-scoped digest subjects/keys; reordering different events does not change their keys. Exact indistinguishable metadata without IDs/digests is separated by occurrence ordinal rather than merging different entries.
- Disk confidence is exactly 82 without dated corroboration and 94 with contemporaneous update/pagefile error metadata. Missing timestamps create neither confidence uplift nor a secondary error symptom. Tests cover the exact 24-hour boundary and future/expired evidence.
- Incident lifecycle supports Detected, Active, Acknowledged, RemediationProposed, RemediationPendingApproval, Remediating, Failed, Escalated and Resolved. Tests assert all 81 state pairs against the explicit legal graph. This implements state progression only; no approval system, policy engine, helper operation or privileged remediation was introduced.
- Repeated diagnoses update the same incident; replayed observation times are idempotent, resolved issues can recur under the existing identity, and independent issue keys remain separate. Snapshots are detached from mutable caller collections. Original snapshots preserve contemporaneous system/boot, CPU/RAM, disk, process/service, network, event, baseline and diagnostic evidence.
- SQLite schema 3 preserves original baseline learning start per metric/scope, including across raw-data retention. Recent raw metric samples restore robust baseline statistics. Startup restoration reads each series independently (latest 50,000 samples per series; maximum 4,096 adaptive series); later cycles process new observations incrementally. Adaptive restoration failure exposes a bounded health code and does not stop fixed critical rules.
- Incident, snapshots and evidence persist in one transaction. Injected evidence/snapshot insertion failures roll back the complete bundle; retry succeeds without losing the detected snapshot. Operational restore reads only the latest 64 snapshots per incident; full history remains separately readable and is not removed by telemetry retention.
- CollectionCoordinator invokes diagnosis after isolated collector jobs. A failed peer collector does not prevent an incident from a successful collector. Persisted collection feeds baseline, diagnosis and incident creation, and restart restores the existing incident identity.
- Retention preserves raw data at the exact 30-day boundary, rejects shorter retention, aggregates older metrics into hourly/daily count/sum/min/max, and is idempotent. Existing audit immutability/preservation tests still pass. Incident snapshots remain readable after telemetry retention.

## Fresh executions

The [command ledger](task-2-dev-vm-runs.md) contains all 34 executions, UTC timestamps, exact arguments, exit codes and observed counts, including failures. These are fresh VM results, separate from the historical protected-host record.

| Gate | Fresh result |
| --- | --- |
| Original BaselineEngine filter before changes | 2/2 passed, exit 0 |
| Original DiagnosticEngine filter before changes | 4/4 passed, exit 0 |
| Final BaselineEngine / DiagnosticEngine / IncidentEngine filter | 30/30 passed (2 baseline, 22 diagnostic, 6 incident), exit 0 |
| Storage / diagnostic cycle / coordinator focused filter | 12/12 passed, exit 0 |
| SCM collector metadata filter | 9/9 passed, exit 0 |
| Final Release build, run 33 | 0 warnings, 0 errors, exit 0 |
| Final complete Release suite, run 34 | 38 Core + 35 Infrastructure = 73 passed; 0 failed, 0 skipped; exit 0 |

Test-first red runs demonstrated the SCM time-window defects (3 failures), missing service subjects/key collisions (4), missing SCM metadata reader/propagation (2), disk undated confidence uplift (1), missing incident engine (5), missing persistence/restore APIs (2), missing cycle processor (2), missing coordinator diagnostic seam (1), order-dependent unknown-event keys (1), and the two independent review defects (2). Three fixture-only failed executions are explicitly identified in the ledger and are not counted as behavioral red evidence.

Reproduction from the VM checkout with the locally installed SDK on PATH:

```powershell
$env:DOTNET_ROOT = Join-Path $env:LOCALAPPDATA 'HostAgent-DEV-tools\dotnet'
$env:PATH = "$env:DOTNET_ROOT;$env:PATH"
dotnet test tests/HostAgent.Core.Tests/HostAgent.Core.Tests.csproj --configuration Release --filter "FullyQualifiedName~BaselineEngine|FullyQualifiedName~DiagnosticEngine|FullyQualifiedName~IncidentEngine"
dotnet build HostAgent.sln --configuration Release
dotnet test HostAgent.sln --configuration Release
```

## Independent review and additional checks

The requesting-code-review workflow dispatched a read-only reviewer for `ab0bc9b3352a68b206b92b64e53e267b38d7691b..7138d71da2bf9a8fd4e53f8a247363680ff34b8a`. It found P1 global baseline-read capacity stopping all diagnostics and P2 unbounded operational snapshot restoration. Both findings were reproduced by fresh tests before fixes. Focused re-review of `7138d71da2bf9a8fd4e53f8a247363680ff34b8a..1b21ea9e57cf80eedd7ddacf9ef2fb7bff7d2479` confirmed both resolved and found no new actionable defects in that range. The reviewer did not execute tests; the primary run performed all listed verification. Later additions were confidence assertions/tests and this documentation, with no further production-code changes.

A bounded secret-like assignment/key scan of 76 source/test/docs/config files found no matches before documentation updates; generated `bin`/`obj` were excluded. `git diff --check` passed. This scan is a heuristic, not a claim that all possible secret formats were exhaustively detected.

## Transfer blocker and limits

`VBoxControl sharedfolder list` reports zero mappings. Guest `C:\PC Agent` does not exist. No safe available VM-to-host project path was found, so nothing was transferred to host, pushed to GitHub or deployed. A source-only diff/patch is prepared outside the checkout against the imported source return point, together with a changed-file manifest; SDK, runtimes, `bin`, `obj`, secrets, VM configuration, caches and raw DEV logs are excluded. Before applying on host, its actual Git status and rollback point must be checked; this patch must not overwrite newer host work.

Actual Windows Service installation/startup/identity/SCM recovery, real service-crash injection, production signing/deployment and a long-duration soak are not verified. Read-only Windows collector integration tests ran successfully in the full VM suite; fixture-based lifecycle/metadata/failure tests are not claims of destructive or installed-service end-to-end testing. Event correlation operates on supplied/collected metadata; it does not infer service identity where Windows provided none.

Task 2 implementation and VM verification are complete. Host synchronization remains blocked. Task 3 remains unstarted and requires a separate explicit instruction.

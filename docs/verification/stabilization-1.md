# Stabilization 1 Verification Record

Date: 2026-10-12, Europe/Prague. This record contains fresh evidence from this stabilization only; prior verification and the independent audit are context, not replacement evidence.

## Environment and preserved work

Commands, builds and tests ran inside the approved DEV VM: hostname `AgentPC`, account `agentpc\agentpcdevtest`, innotek GmbH / VirtualBox, UUID `46543236-8C5E-49DA-953C-DB17F9B1E402`, VBoxGuest and VBoxService Running. Repository: `C:\Users\AgentPCdevtest\Documents\ChatGPT\DevTest VM PC\HostAgent-DEV`. Account-local SDK: .NET `10.0.401`.

Initial branch was `codex/diagnostic-self-service`, HEAD `94ce71b2374ecfe9bb78ca302df129056ee6bfae`; `git merge-base --is-ancestor 94ce71b2374ecfe9bb78ca302df129056ee6bfae HEAD` returned 0. Tracked files were clean. The existing untracked `docs/audits/2026-10-12-host-agent-v1-independent-audit.md` was preserved and backed up outside the checkout. Its SHA-256 before and after work is `63268A0FE5D734BA4D5484C87004A2F702F8B031BC93B83E56DB6A6CF46C414D`. Work continued on new branch `codex/stabilization-1`; no reset, history rewrite or checkpoint reset occurred.

AGENTS, specification, implementation/development/testing documentation, Task 2–4 records, security reviews, current code and tests were inspected. No tool installation, production-host access, security-policy change, VM/snapshot change, real external delivery or secret access was needed.

## Changes verified

- The first System Event Log query includes timestamped WER 1001 from `Microsoft-Windows-WER-SystemErrorReporting` in the inclusive previous 24 hours, excludes future events, and retains the ordinary ten-minute warning/error window. Per-log/total limits remain 100/200. Selective bootstrap pagination survives restart without collecting old unrelated events; completion resumes by scan time before normal bookmarks. Optional version-1 checkpoint fields preserve older checkpoint compatibility. Only bounded existing metadata is persisted, not raw event XML/messages.
- Collector warning codes reach runtime health. Disk Partial with only `smart_health_unavailable` yields non-degrading `optional_smart_unavailable`. Missing required disk data, other partial results and required collector failures still degrade health. `status --verbose` requests a bounded fixed component/code view; ordinary status is unchanged. Arbitrary warnings, exception text, paths and secrets are excluded from that view. Status remains read-only and uses the existing administration authorization.
- Specification, architecture and operations documentation describe these behaviors and limitations.

## Executed commands and results

Working directory for every dotnet command was the VM repository above. SDK selection was process-local:

```powershell
$sdk = 'C:\Users\AgentPCdevtest\AppData\Local\HostAgent-DEV-tools\dotnet-10.0.401'
$env:DOTNET_ROOT = $sdk
$env:PATH = $sdk + ';' + $env:PATH
```

Each command below actually executed, with stdout/stderr retained outside the repository under `C:\Users\AgentPCdevtest\AppData\Local\HostAgent-DEV-evidence\stabilization-1`. All test assemblies were locally built and executed in the VM.

| Log | Command | Result |
| --- | --- | --- |
| 01-event-red.log | `dotnet test tests/HostAgent.Infrastructure.Tests/HostAgent.Infrastructure.Tests.csproj -c Release --filter 'FullyQualifiedName~EventLogBootstrapTests' --logger 'console;verbosity=normal'` | Before implementation: exit 1, 6 failed / 1 passed. Included missing 11-minute/24-hour WER, future-event exclusion and continuation assertions. |
| 02-event-focused.log | `dotnet test tests/HostAgent.Infrastructure.Tests/HostAgent.Infrastructure.Tests.csproj -c Release --filter 'FullyQualifiedName~EventLogBootstrapTests\|FullyQualifiedName~WindowsEventLogCollectorTests\|FullyQualifiedName~WindowsCollectorIntegrationTests' --logger 'console;verbosity=normal'` | After Event Log implementation: exit 0, 27 passed. |
| 03-health-red.log | `dotnet test tests/HostAgent.Infrastructure.Tests/HostAgent.Infrastructure.Tests.csproj -c Release --filter 'FullyQualifiedName~HealthStabilizationTests' --logger 'console;verbosity=normal'` | Before implementation: exit 1, 6 failed / 3 passed. SMART-only health expected false, actual true; verbose CLI/dispatch unsupported. Three malformed-protocol rejection cases already passed. |
| 04-health-green.log | Same command as 03 | After health implementation: exit 0, 9 passed. |
| 05-combined-focused.log | `dotnet test tests/HostAgent.Infrastructure.Tests/HostAgent.Infrastructure.Tests.csproj -c Release --filter 'FullyQualifiedName~EventLogBootstrapTests\|FullyQualifiedName~WindowsEventLogCollectorTests\|FullyQualifiedName~HealthStabilizationTests\|FullyQualifiedName~CollectionCoordinatorTests\|FullyQualifiedName~AdminDispatcherTests\|FullyQualifiedName~CliTests\|FullyQualifiedName~AdministrationTests\|FullyQualifiedName~TelemetryCollectionJobTests' --logger 'console;verbosity=minimal'` | Exit 0, 61 passed, including the subsequently added native Windows query smoke test. |
| 06-release-build.log | `dotnet build HostAgent.sln --configuration Release` | Exit 0; 0 warnings, 0 errors. |
| 07-release-suite.log | `dotnet test HostAgent.sln --configuration Release --no-build -m:1 --logger 'console;verbosity=normal'` | Exit 0; Core 50 + Infrastructure 193 = **243 passed**, 0 failed, 0 skipped. |

The backslashes before pipes in this Markdown table only escape table separators; the actual filter argument contains ordinary `|` characters without backslashes.

New regression coverage comprises 8 Event Log cases and 9 health/protocol/CLI cases. Event cases include 11 minutes, exact 24 hours, one tick older, future, missing timestamp, unrelated history, repeat checkpoint reads, and 101-record pagination across collector restart. The native smoke test executed the real Windows query with an in-memory checkpoint store and checked available relevant WER IDs when the bounded result was not truncated; it did not write Event Log or installed checkpoints. Health integration tests use real collection jobs/runner/coordinator and temporary SQLite, with synthetic collectors at the OS boundary. They distinguish SMART-only Partial from additional disk errors and unavailable/partial mandatory system telemetry, and assert the sanitized verbose response.

SHA-256 of evidence logs:

```text
01-event-red.log         53295692728481269259BDA9859E8788C7BAFFEE1E191D67C255E8A1B58A7D63
02-event-focused.log     78F3AF042CAE922C15CFA650755DC45625864F3C90EE365DEC77DDA84DA34668
03-health-red.log        208ADFB5DFB96C3C3BECDBDD34F744DD042224C13A98ADD7BCBAC5E634F72D74
04-health-green.log      8FE7729B183FDEABDB56751154924B8E8020869EF15489F10019D2FDCAF61DFD
05-combined-focused.log  708F816C36FC3E61C44135A4C552B0861388EFE8A2F0C456A3146BC2756986C7
06-release-build.log     ECBDDD07EBDFF224D7D3A3919CAEC68B43C1781AD09396A2C97D3B44A4359C23
07-release-suite.log     A7D144388030BC5A75CA93C501AE7D040F8527FEE471275C174485AE0D443313
```

## Read-only investigation of the three WER events

Fresh elevated read used this limited query; only RecordID and UTC time were exported:

```powershell
Get-WinEvent -FilterHashtable @{LogName='System';ProviderName='Microsoft-Windows-WER-SystemErrorReporting';Id=1001;StartTime=[datetime]'2026-10-11T18:50:00Z';EndTime=[datetime]'2026-10-12T06:00:00Z'}
```

`inspect-wer.ps1` and `inspect-wer.py`, retained in the evidence directory, completed with exit 0. The latter opened `C:\ProgramData\HostAgent\host-agent.db` with SQLite URI `mode=ro`, `PRAGMA query_only=ON`, and a read transaction; it compared all retained event-log telemetry, incident snapshots, diagnostic evidence and all 36 incidents. Connection total_changes was 0. No Event Log/checkpoint write or installed-service restart was performed; the already-running service continued its normal activity independently.

| System RecordID | Event UTC | Telemetry matches | Incident snapshot matches | Parsed bugcheck codes / bugcheck-specific evidence matches |
| --- | --- | --- | --- | --- |
| 2671 | 2026-10-12T05:54:05.9869589Z | 1, first collected 05:54:17.5724185Z | 3 | 0 / 0 |
| 2580 | 2026-10-12T00:25:36.2602722Z | 1, first collected 00:25:47.4020393Z | 4 | 0 / 0 |
| 2297 | 2026-10-11T18:57:58.9271410Z | 0 | 0 | 0 / 0 |

At the read transaction, System checkpoint LastRecordId was 2722 and LastScannedAtUtc/updatedAtUtc `2026-10-12T08:28:42.4245402+00:00`, beyond all three IDs. The 723 retained event-log telemetry rows spanned `2026-10-11T20:01:16.8586888+00:00` through `2026-10-12T08:28:42.4245402+00:00`. No bugcheck diagnosis was present among all 36 stored incidents.

Two events were demonstrably collected, so a claim that all three were lost is false. Record 2297 is absent from retained data and predates the earliest retained collection by about 63 minutes; this is consistent with the previous initial ten-minute window, but does not prove permanent loss or its unique cause. All three original events remain in System Event Log. The initial-scan fix deliberately preserves the existing checkpoint and does not replay these historical events or reparse old stored metadata. No driver attribution, dump analysis or system repair was attempted.

## Independent review and remaining limits

An independent read-only reviewer inspected all 9 changed source files and both new test files, plus supporting reader/checkpoint/disk/protocol code, against immutable base `94ce71b2374ecfe9bb78ca302df129056ee6bfae` and source/test tree `992ef471666ef1b80ebc3c9d8a2827846f084c65`. Verdict: approve, no confirmed actionable correctness/security finding. The reviewer specifically checked selective pagination/restart, backward-compatible checkpoints, optional versus mandatory health, bounded fixed codes, strict verbose protocol and unchanged privilege/approval boundaries. The reviewer did not rerun tests or native operations; the test evidence above is the implementer's executed evidence. Later edits only added documentation. Follow-up independent review approved all four documentation changes, checked all seven log hashes/test totals and the WER summary, and found no material overclaim or unsafe advice. Staged source/tests exactly matched the reviewed tree. `git diff --check` and `git diff --cached --check` passed.

Direct regression coverage for log reset during an unfinished bootstrap and healthy→failed→healthy health transitions could be expanded. Existing reset/backward-compatibility tests pass, but those exact combinations were not separately exercised. Installed binaries/service were not upgraded or restarted, so live installed `status --verbose` and recovery of the prior installed degraded state remain unverified. New health output covers current collector/runtime-worker results, not per-notification delivery history. No real external notification/heartbeat, physical-host operation, retrospective WER rediagnosis, destructive remediation or production deployment was tested or performed. These changes remain in the DEV VM; no push or host transfer is part of this step.

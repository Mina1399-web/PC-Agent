# Installed 1.2.8 read-only diagnostics — 2026-10-26

This record contains only observations made on 2026-10-26 in the approved DEV VM. It does not replace or reinterpret the operator-provided 1.2.8 acceptance JSON or earlier offline database checks.

## Environment and service state

- Verified computer: `AGENTPC`, model `VirtualBox`, VM UUID `46543236-8C5E-49DA-953C-DB17F9B1E402`; VirtualBox Guest Service Running.
- Current account: `AgentPC\AgentPCdevtest`, token not elevated. Available memory 2,318.8 MiB; C: free 16.643 GiB.
- `HostAgent` Running, PID 9836; `HostAgent.Helper` Running, PID 9912. No service control was performed.
- The owner reports that installed version 1.2.8 is the target. This run did not independently verify installed file hashes/version.

## Database locations and access

Source inspection of `src/HostAgent.Service/Program.cs` declares `C:\ProgramData\HostAgent\host-agent.db` and `C:\ProgramData\HostAgent\notifications.db`; `src/HostAgent.Helper/Program.cs` declares `C:\ProgramData\HostAgent.Helper\helper-journal.db`. These are source-declared locations, not runtime-confirmed file existence.

The current non-elevated account received Access Denied enumerating `C:\ProgramData\HostAgent` and `C:\ProgramData\HostAgent.Helper`, including `Test-Path` for the two service database files. No database was opened, no database contents or secrets were read, and no integrity result was produced. The helper database's current existence is unknown.

The previously operator-reported `host-agent.db quick_check=ok` was run offline after stopping HostAgent, and the previously reported notifications check was `quick_check=ok`; those checks predate this record and do not establish the current installed 1.2.8 database state. The last live acceptance output reporting `OperationalError`/`not_run` likewise does not establish corruption.

A consistent integrity check of live WAL databases cannot be claimed from this run. Opening a live SQLite WAL database can require shared-memory coordination writes; immutable mode can omit committed WAL content. No service was stopped in this run. Current integrity remains **not verified**.

## Current limited metric observations

- `HostAgent` remains Running and is currently listed in the local `Performance Monitor Users` group (`S-1-5-32-558`). This verifies group membership only; it does not prove the effective token of PID 9836 or the service's ability to read PDH counters.
- A non-elevated `HostAgent.Cli.exe status --verbose` attempt exited 1 and required authorization/elevation. No raw reply was recorded. Current component health could not be refreshed by this token.
- The VM's localized counter-set names are Czech (`Fyzický disk`, `Procesor`, `Paměť`, `Proces`). English paths through `Get-Counter` returned PDH `0xC0000BB8` (object not found). Localized read-only paths were readable for physical-disk read/write bytes, average transfer latency, total CPU, and available memory. Two samples showed nonzero disk traffic, total CPU 82.86% and 92.64%, and available memory about 2.46 GB and 2.40 GB. This establishes that those host counters are present/readable to the interactive account under Czech names; it does not establish the installed service's effective-token access or explain the product's persisted warning.
- The product source uses `PdhAddEnglishCounterW` for disk I/O; therefore the interactive `Get-Counter` English/localized-name difference alone is not a confirmed product defect. `WindowsCpuMemoryCollector` uses `GetSystemTimes` for system CPU and `GlobalMemoryStatusEx` for memory. It reports `cpu_baseline_not_ready` on the first CPU observation, then requires a later observation to calculate a delta. Per-process access/path failures are classified separately and can leave process metrics partial.
- The last operator-supplied status had partial `cpu-memory` and `process-services` health. This run did not obtain a fresh privileged status payload, service-token probe, or current persisted warning details. The cause of the installed partial CPU/process metrics therefore remains **unresolved**.

Microsoft defines `0xC0000BB8` as `PDH_CSTATUS_NO_OBJECT`; this is consistent with the observed English-path lookup failure on this Czech-localized system, not by itself evidence that all counters are absent.

## Owner-run read-only preview — was pending

The reviewed handoff probe `HostAgent-DEV-releases/SignPath-1.2.8-82465b4-handoff/Read-InstalledPreview.ps1` exists. It requires an elevated PowerShell 7 session and checks VM identity, installed-file hashes, both service identities, and a bounded sanitized `status --verbose` response. It does not open databases or change services/configuration. Run it once in PowerShell 7 as administrator inside the DEV VM and return the complete JSON. Its guards require initial free RAM above 1,024 MiB, at least 768 MiB during the run (512 MiB hard abort), at least 15 GiB free on C:, and a 300-second overall deadline. At the time of this record, free disk was 16.643 GiB.

A current database integrity check remains deferred because it would require an approved, consistent access method for the protected live WAL files; this record did not stop services or invoke such a method.

## Changes made

Only this new verification document was created. No source, installer, service, permissions, configuration, database, or operating mode was changed. No tests/builds were run.

## Operator-supplied 1.2.8 preview result — 2026-10-26

The operator supplied the complete JSON from `Read-InstalledPreview.ps1` after running it in the DEV VM. The report identifies source commit `82465b48d6137b4f81a57f33900f3da69c194658`, version `1.2.8`, candidate manifest SHA-256 `5DCF79DC3284B4937EBA64D4F50A7831A1FFE4F0891AE4D4B190A500B136B25A`, and MSI SHA-256 `F3CAA8EBCD8EBE9F163FE2D2E196F83D5B948DEC93EC6BADC2BA0452EF0AA197`.

The scope-specific installed checks returned `PASS`: artifact-only validation passed; 1,225 installed files matched, with zero unexpected files; both services remained Running with identical identities and PIDs before/after; Shadow and `readOnly=true` remained active; configuration was available; configuration/ACL changes, service control, database opening and installer execution were all false. The run's reported minimum free memory was 1,745.7 MiB and minimum free disk was 16.646 GiB.

The overall status is still `INSTALL_ACCEPTANCE_INCOMPLETE`, and `degraded=true`. `cpu-memory` and `process-services` remain `collector_partial`; `disk` reports `optional_smart_unavailable` with `degraded=false`. This JSON confirms the health state but does not include persisted collector warning details or validate the effective service token. It does not identify the root cause of the two partial collectors.

The remaining gates reported by the probe were live dashboard acceptance, database integrity acceptance, and partial/degraded telemetry review. The database was not opened. No database integrity result is inferred from `readOnlyInstalledChecks=PASS`.

The previously requested preview step is now complete. The next useful owner evidence is the exact sanitized warning codes for the most recent `cpu-memory` and `process-services` collection results, if an existing read-only dashboard/View display exposes them. Do not run a database reader against the live service until it is confirmed to avoid WAL/SHM writes; no currently reviewed script in this record performs a consistent live integrity check without opening the database.

## Operator-supplied dashboard component reasons — 2026-10-26

The operator supplied a dashboard screenshot of the Components table. The visible `cpu-memory` row is `collector_partial`, degraded=true, with exactly the displayed reasons `some_process_metrics_access_denied` and `process_cpu_time_access_denied`. The visible `disk` row is `optional_smart_unavailable`, with `smart_health_unavailable`, degraded=false. The `process-services` row is `collector_partial`, degraded=true; its visible text begins `some_process_metrics_access_denied, process_cpu_time_access_denied, some...` but is clipped at the right edge, so the remaining reason codes are not established by this image.

The reason `process_cpu_time_access_denied` is produced when reading an individual process's CPU time raises access denied; the collector also emits the aggregate `some_process_metrics_access_denied` code and returns partial telemetry. Both CPU/memory and process/services use process enumeration and per-process CPU-time reads. The displayed finding therefore confirms denied access to at least some per-process CPU data. It does not show that system-wide CPU time or physical-memory collection failed. No process identity is disclosed by the dashboard, so the specific inaccessible process cannot be attributed from this evidence. This is an observed partial result, not proof of corruption or a need to broaden privileges.

The screenshot's `process-services` reasons are incomplete because of UI clipping. No additional codes are inferred. No application setting, permission, service, database, or operating mode was changed.

## Operator clarification — complete `process-services` reason — 2026-10-26

The operator clarified that the clipped additional reason is “some executable paths unavailable”. The source uses the bounded code `some_executable_paths_unavailable` when one or more process executable paths cannot be read. The visible `process-services` result is therefore confirmed to include `some_process_metrics_access_denied`, `process_cpu_time_access_denied`, and `some_executable_paths_unavailable`; the screenshot and clarification do not establish whether further reasons follow.

This is partial per-process metadata access. It does not establish failure of system-wide CPU/memory counters. No permissions or service settings were changed.

## Fresh preflight and offline integrity procedure prepared — 2026-10-26

The current session again confirmed DEV VM `AGENTPC`, account `AgentPC\AgentPCdevtest`, VirtualBox UUID `46543236-8C5E-49DA-953C-DB17F9B1E402`, and running `VBoxService`. The current shell is **not elevated**. Fresh readings were 2,243.8 MiB free RAM and 16.724 GiB free on C:. Both installed services were Running: `HostAgent` PID 3904 (`NT SERVICE\HostAgent`) and `HostAgent.Helper` PID 3384 (`LocalSystem`), with the expected installed image paths and Auto start mode.

An unprivileged metadata-only attempt to inspect files under `C:\ProgramData\HostAgent` was denied by Windows. Because `Test-Path` suppresses this access error as false, that output was not treated as evidence that databases were absent. No database was opened and neither service was controlled by that attempt.

The earlier `Test-InstalledDiskCounters.ps1` was reviewed but not run: it has a 20 GiB initial disk requirement, exceeds current free space, and refuses a WAL-mode database when WAL/SHM sidecars are missing. Its Python hash pin also differs from the Python runtime currently present. A separate bounded offline-copy procedure was prepared at `scripts/diagnostics/Run-InstalledDatabaseIntegrityOffline.ps1` (SHA-256 `5D7862B428272C2A955BAC9E97DA504B58E69D9432016BDA19B73DCE86B2037F`). PowerShell's parser accepted the script with zero syntax errors; it has **not** been executed. Its preflight verifies the pinned 1.2.8 manifest SHA-256 and both installed service executable hashes; it requires the approved elevated VM/account, at least 1,024 MiB initial free RAM, unchanged 768/512 MiB soft/hard floors, and at least 10 GiB disk free after accounting for copies. It verifies fixed service identities, stops only those two services, copies fixed database files plus existing SQLite sidecars into a unique local evidence directory, runs `quick_check` using SQLite `mode=ro` on the copies, and attempts to restore the originally running services in `finally`. It does not request source SQL writes, schema changes, checkpoint, recovery, or ACL/configuration changes; normal graceful service shutdown can itself perform SQLite's normal close/checkpoint behavior. Successful runs remove only their own temporary database copies; failed runs preserve them for review. No live integrity result is claimed until the owner runs it elevated and returns its JSON.

The current pinned local Python runtime was independently hashed as `73DBF1F40507275D6FA636B49EB80B4753E313F649F52591ABBA86B31F1A1BCE`; the prepared script pins that exact runtime. No focused or Release tests/builds were run because no product source was changed. The working tree's earlier modifications remain preserved; the only new working files are this procedure and this verification note.

## Partial process telemetry diagnosis — current evidence

The displayed codes map to per-process `TotalProcessorTime` access denial (`process_cpu_time_access_denied`, plus aggregate `some_process_metrics_access_denied`) and `Process.MainModule.FileName` access failure (`some_executable_paths_unavailable`). The process/service collector runs inside the limited virtual service account `NT SERVICE\HostAgent`; it intentionally leaves inaccessible values absent and reports bounded partial telemetry. This evidence does not indicate a failure to read system-wide CPU or RAM, nor does it name the process responsible. The limitations are consistent with Windows denying a service token access to selected processes, but the exact process-level cause is not proven by the current sanitized evidence. No privileges, ACLs, services, configuration, application mode, or product source were changed to suppress the warnings.




## Operator-run offline database check and service recovery failure — 2026-10-26

The owner supplied the JSON from offline integrity run `3ea953be08924214a0456b21939cef40`. The copied `host-agent.db` and `notifications.db` each returned `quick_check=ok`; their copies were hash-verified before inspection. `helper-journal.db` was missing and remains `NOTRUN`; no helper operation was invoked to create it. The run reports both services were Running before inspection and that temporary copies were preserved.

The run failed its service-restoration gate: both `HostAgent` and `HostAgent.Helper` remained Stopped, with their expected Auto start mode, service account, and image path. A fresh read-only query in the current session confirmed both are still Stopped (PIDs 0); the current shell is not elevated. The restoration exception details were not included in the sanitized run output, so the exact cause of `Start-Service` failure is not established. No further service or database operation was performed in this session. The owner must start only these two services from an elevated PowerShell in the DEV VM and return the resulting sanitized state JSON before the acceptance can be considered complete.

## Post-failure service image inspection — 2026-10-26

A fresh query still found both services Stopped. Their configuration remains Auto with the expected service accounts and executable paths. The installed 1.2.8 executable files are present and their SHA-256 hashes match the verified candidate manifest: `HostAgent.Service.exe` `A4C8FEADE850E35DFB0FD4D3E0B5FC5DB428BD6E3E88A05AEDC973C4900FFB8C`; `HostAgent.Helper.exe` `96E804D80DBFF3F09822C32DDADD5204AFE737EFD81898887AB519A555B8A99D`. This rules out missing/mismatched service binaries but does not establish why SCM start requests failed. The owner-run recovery block suppressed the underlying exception, and recent System log inspection contained no HostAgent start-failure event, so the exact failure code remains unknown. The current Codex shell is not elevated; no additional service operation was attempted.

## Operator-run service recovery succeeded — 2026-10-26

After the previous recovery block did not expose the underlying start failure, the owner ran the direct SCM start diagnostic. `sc.exe start` returned native exit code 0 for both services; the immediate SCM output showed START_PENDING, and the subsequent state check confirmed both Running with Win32/service-specific exit codes 0: `HostAgent` PID 10448 and `HostAgent.Helper` PID 10776. A separate fresh CIM query in this session independently confirmed both are still Running with their expected Auto start modes, accounts, and image paths. Service recovery is now PASS.

The database results remain limited to the prior offline-copy run: `host-agent.db=quick_check ok`, `notifications.db=quick_check ok`; `helper-journal.db` did not exist at that run and remains NOTRUN. No database repair or source SQL write was performed by the diagnostic. Graceful service shutdown may have performed normal close-time SQLite behavior. The per-process metric warnings remain unresolved; no privilege or ACL changes were made.

## Owner confirmation of post-recovery dashboard — 2026-10-26

The owner confirmed that after both services were restored, the Host Agent dashboard refreshed with a progressing current timestamp and remained in Shadow/read-only mode. No screenshot or status JSON was requested or supplied, so this is recorded as owner-confirmed behavior only; individual health rows and metric freshness details were not independently captured in this step. The partial per-process metric warnings and absent helper-journal integrity result remain open as described above.

# Host Agent V1 Specification

## Authorized local desktop extension — 2026-10-12

The owner authorized the local Czech desktop dashboard, evidence-based
recommendations and manual operator workflows. This supersedes the original
dashboard exclusion below, which describes the original V1 scope. See
[implementation plan](../plans/2026-10-12-dashboard-maintenance.md).

The desktop runs without elevation, using a separate authenticated local
read-only pipe. It shows fresh bounded CPU/RAM/disk/process/health data,
24-hour charts, incident confidence and redacted evidence. Missing or stale
telemetry remains unavailable. WER metadata investigation is read-only and
does not attribute a driver without dump analysis. No inbound HTTP service.

Recommendations include evidence, expected qualitative benefit, impact and
rollback limits. Maintenance opens fixed Windows settings for operator
review and offers a bounded user-temp preview/quarantine/restore workflow.
That workflow only moves eligible top-level files from the current user's
`%LOCALAPPDATA%\Temp` into a private, same-volume quarantine; exact preview
confirmation is required, the operation rechecks fresh Confirmation-mode
service state, and restore never overwrites. It does not free space, delete
files, recurse or install packages. Manual allowlisted service/process
operations use the existing elevated CLI, exact request approval and
independent helper checks. Shadow remains default.

The kill switch can be explicitly set in both configurations while
preserving outbound choices. A post-action observation requires successful
CLI completion and a later telemetry timestamp; it does not prove causality.

## Purpose

Host Agent is a long-running local Windows mini-PC monitor and incident assistant. Version 1 collects host telemetry, stores local history, learns robust adaptive baselines, correlates evidence into likely root causes, creates deduplicated incidents, recommends remediation, records decisions, and sends outbound notifications and watchdog heartbeats. All privileged changes require explicit policy authorization and approval. Shadow mode is the default.

The initial deliverable is a local C#/.NET 10 LTS solution. No public inbound HTTP endpoint, cloud control plane, dashboard, MCP implementation, VirtualBox, Home Assistant, Zigbee, Tailscale, GPU/firmware/BIOS control, Windows Update automation, or arbitrary remote command facility is included in V1.

## Architecture and privilege boundaries

- HostAgent.Core targets cross-platform net10.0 and contains domain models, typed contracts, policies, diagnoses, incidents, remediation descriptions, audit records, and collector/storage/notification interfaces. It has no Windows Service hosting dependency. A future MCP adapter remains outside Core business logic; V1 does not implement MCP.
- HostAgent.Service is the non-privileged Windows Service. It coordinates isolated collectors, scheduling, baseline/diagnostic/incident/policy engines, notifications, outbound heartbeat, and audit orchestration. It starts automatically with Windows, works without an interactive login, shuts down gracefully, logs structured events, and has SCM recovery configuration documented.
- HostAgent.Helper is a separate, minimal Windows Service intended to run as LocalSystem only where a capability requires it. It accepts only versioned typed requests for explicitly implemented operations. It has no PowerShell, CMD, general process launch, arbitrary command, general registry, or general file access interface.
- HostAgent.Infrastructure contains Windows collectors, SQLite persistence and migrations, Named Pipe transport, Event Log access, service/process/network adapters, secure secret adapters, notification adapters, and heartbeat transport.
- Unit and integration test projects exercise deterministic core behavior, SQLite on temporary databases, and read-only Windows collectors. Destructive operations are simulated; no test changes real host state.

Host-to-helper communication uses Windows Named Pipes only. Protocol messages have a version, explicit request/response types, strict size limits, validation, timeout and cancellation, and reject unknown operation types. The server enforces a restrictive pipe ACL and independently verifies the connecting client's Windows identity against a local allowlist/policy. A process or service name supplied in a request is never identity proof. The helper revalidates an operation ID and all typed parameters against its own whitelist and policy before execution.

## V1 telemetry and collector behavior

The sampling interval defaults to one minute and is configurable without tying storage shape to the interval. The six isolated collector domains are:

1. System: Windows edition/version/build, boot time and uptime, observed shutdown/restart information, and relevant system status.
2. CPU and memory: total CPU, used/available memory, pressure, supported commit/pagefile values, and top processes by CPU and memory. A short CPU spike alone cannot open an incident.
3. Disk: volume capacity/use/free/percentage, supported I/O and latency. SMART is queried only through a supported safe API; unavailable or denied SMART is a partial collector result, never a collector crash.
4. Processes and services: running process ID, executable path when accessible, resource use, Windows service status and observed changes/crashes/restarts. Process names alone never authorize an action.
5. Network: active interfaces, addresses, gateways, DNS servers, gateway reachability, public-IP reachability, DNS resolution, latency and bounded diagnostic packet-loss probes. Diagnoses distinguish host link/configuration, gateway, DNS, and internet failure.
6. Event Log: only relevant System/Application Warning, Error and Critical metadata and diagnostic events are retained. Do not bulk-copy event logs or store unrelated event payloads. Access failures, malformed fields, and unexpected records are isolated.

Every collector receives a cancellation token and has a configured timeout. A collector exception or timeout is converted to a health result and does not stop other collectors or the service. Retry uses bounded exponential backoff; no tight retry loop or empty catch is permitted. The service health state degrades when collectors repeatedly fail.

Expected SMART unavailability remains a partial disk capability result but does not by itself degrade overall agent health. Likewise, a process-services Partial result caused exclusively by unavailable optional executable paths preserves its capability reason without degrading overall health. Other missing disk data, process metrics, mixed/unknown warnings and required-collector failures remain degrading. The elevated local CLI `status --verbose` exposes only a bounded component list with stable health codes; ordinary `status` preserves its response shape.

## Local storage

SQLite is local-only. Schema changes use ordered migrations recorded in a schema-version table. Raw telemetry rows are time-stamped independently of the sampling period. Store telemetry snapshots, baseline samples/statistics, incidents, incident snapshots, diagnostic evidence, append-oriented audit records, remediation attempts, and service health. Keep detailed metrics for at least 30 days, then preserve hourly and daily aggregates. Retention never removes audit rows. All SQL values use parameters. Database unavailability degrades health and disables remediation while read-only monitoring continues when possible.

## Baselines and diagnostics

Use fixed safety thresholds immediately plus a robust adaptive baseline. For each metric/scope, preserve typical value, robust variability (median and median absolute deviation or an equivalent robust estimator), percentile bounds, trend, duration, recurrence, and sample count. Do not use arithmetic mean alone.

- Days 0–7: collect baseline; adaptive alerts are informational. Fixed critical rules remain active.
- Days 8–30: adaptive rules are active with reduced confidence.
- After day 30: full adaptive mode uses a rolling window primarily covering the latest 30 days.

Diagnostic output includes severity, deterministic/statistical confidence from 0–100, timestamp, evidence references, probable root cause, secondary symptoms, recommended remediation and incident state. No LLM contributes to confidence. Multi-signal rules must identify the likely cause rather than emit unrelated warnings. Examples include sustained CPU plus a dominating process, gateway and IP reachability plus failed DNS, and critically full storage followed by update/pagefile errors. A brief isolated signal or uncertain diagnosis never starts automatic remediation.

For Windows System log event 1001 from `Microsoft-Windows-WER-SystemErrorReporting`, retain only a parsed bugcheck code, its four hexadecimal parameters, and a validated minidump filename; never persist the raw event XML/message or an arbitrary path. Correlate only timestamped events within 24 hours. Bugcheck `0xA` is classified as an invalid kernel memory access at elevated IRQL. The diagnosis must say explicitly that Event Log does not identify the offending driver; its confidence describes stop-code classification, not driver attribution. Show the referenced dump and recommend read-only WinDbg analysis (`!analyze -v`) before any driver change. Driver update, rollback, disable, or reboot is never an automatic fix for an unattributed bugcheck.

Without a checkpoint, the first System scan selectively includes timestamped WER 1001 in the inclusive preceding 24 hours and excludes future timestamps. Ordinary diagnostic events retain a ten-minute initial window. Bounded bootstrap pages preserve the selective query across restart; subsequent reads do not replay collected records or bulk-copy older unrelated events. Existing checkpoints are preserved; the change does not reparse or rediagnose old stored metadata automatically.

The elevated local CLI exposes `incident <incident-id>` as a read-only detail view of the durable diagnosis, up to 16 evidence items, an explicit truncation flag, confidence scope, and recommendation. It does not expose full telemetry snapshots or add an action/approval. Any remediation still uses the existing typed, allowlisted operation and exact per-operation approval flow; Shadow/read-only remain defaults, and no unattended approval is added.

## Incidents and snapshots

Incident lifecycle: Detected, Active, Acknowledged, RemediationProposed, RemediationPendingApproval, Remediating, Resolved, Failed, Escalated. Correlation keys and deduplication update an existing incident instead of creating one every collection cycle. On relevant incident transitions, capture a durable snapshot of system, CPU/memory, disk, top processes, affected services, network, relevant Event Log metadata, baseline comparisons and diagnostic evidence. Snapshots remain analyzable after recovery.

## Policy and remediation

Policy decisions are exactly READ_ONLY, AUTO_ALLOWED, CONFIRM_REQUIRED, or DENIED. Policy evaluation errors and uncertainty resolve to DENIED. Default policy is monitoring and diagnosis enabled, shadow mode enabled, and remediation requiring confirmation. Shadow mode records what would have happened but executes no privileged change. Confirmation mode allows only an approved operation on the explicit helper whitelist. Limited autonomous mode is not enabled by default.

Potential typed helper capabilities are individually enabled by policy: restart/start/stop an allowlisted service, terminate an allowlisted process, flush DNS, renew DHCP on an allowlisted adapter, and restart Windows after confirmation. Presence in the protocol does not itself enable an operation. Each attempt records an operation ID, typed validated parameters, policy requirement, timeout, approval, audit references, start/end and result.

Protections include a global kill switch, global read-only mode, per-operation rate limits and cooldowns, bounded attempts, and a restart-loop guard with exponential or explicitly defined backoff. Repeated failure stops further attempts and escalates for human intervention.

## Audit, notifications, secrets, and heartbeat

Every proposed or privileged action produces an append-oriented audit entry with timestamp, initiator, incident and diagnosis, confidence, evidence reference, requested operation, policy decision, approval status, execution times, outcome and error. Audits are not automatically deleted.

Notifications use an interface with local history by default. INFO stays local; WARNING sends a normal push; HIGH sends an elevated-priority push; CRITICAL supports acknowledgement/retry. V1 includes a Pushover adapter. Credentials are never in source or repository history. Development and production settings are separated. Production credentials use Windows-protected storage scoped to the service identity and documented setup/recovery.

The external watchdog adapter supports Healthchecks.io-compatible outbound HTTPS heartbeats. No inbound port is opened. Heartbeat health distinguishes agent stopped/crashed, host stopped, and internet loss as far as an external watchdog can observe.

## Fail-safe behavior

- SQLite unavailable: mark degraded health; continue safe collection if possible; disable privileged/autonomous operations.
- Helper unavailable: monitoring and diagnosis continue; record the denied/unavailable operation.
- Policy engine failure: DENIED.
- Uncertain diagnosis: no autonomous remediation.
- Notification failure: record it and continue.
- Repeated remediation failure: stop retrying and escalate.
- Unknown protocol version/type, malformed/oversized request, unauthorized client, timeout, or invalid parameter: reject without side effects.

## Implementation stages and acceptance gates

### Task 1 — Foundation, collectors, SQLite

Create solution and projects, build Core contracts and telemetry models, six independently failing-safe Windows collectors, collection orchestration seams, SQLite migrations/schema/store and Task 1 unit/integration tests. Gate: full build, relevant unit/integration tests, warning review, secret scan, file/diff review.

### Task 2 — Baseline, diagnostic, incident, snapshot

Implement fixed/adaptive baseline with learning modes, deterministic confidence, multi-signal root-cause rules, incident lifecycle/deduplication, durable incident snapshots and retention aggregation. Tests cover brief and sustained CPU, process root cause, memory pressure, near-full disk, DNS and gateway separation, service crash, shared root cause, learning stages, confidence and incident deduplication. Gate: same full verification as Task 1.

### Task 3 — Policy, Named Pipes, helper, audit and protections

Implement fail-closed policy, typed versioned pipe contracts, strict parsing/size/time limits, client authorization, independent helper whitelist, confirmation path, append-only audit, rate limits/cooldowns/retry bounds/kill switch/read-only. Tests use fakes and pipes; they never trigger a destructive host change. After implementation perform an explicit security review for privilege boundaries, ACL, deserialization, whitelist, paths, process/service identity, secrets/logging, command injection and arbitrary execution. Gate: all security tests plus full build/test/review.

### Task 4 — Notifications, heartbeat, Windows Service production readiness

Implement Pushover and Healthchecks.io-compatible adapters, secret loading, shadow/confirmation operating modes, resilient scheduling, automatic Windows Service startup without interactive login, graceful stop, structured logs and documented SCM recovery/install configuration. Gate: fake-server/transport tests, configuration/secrets review, full build and test suite. Live credentials, real helper installation and external endpoint delivery are not claimed without explicit end-to-end evidence.

## Out of scope

No dashboard, Home Assistant, Zigbee, VirtualBox-specific manager, Tailscale integration, other-host management, advanced GPU metrics, BIOS/UEFI or firmware changes, Windows Update automation, arbitrary remote shell, full MCP adapter, public API, or default fully autonomous mode. Keep only clean extension boundaries needed for future modules.

## Development, testing, and production signing

Normal development and automated unit/integration testing run in an approved Windows environment that permits local development assemblies to execute. WSL does not verify Windows Service, Event Log, Named Pipe ACL/security, Service Control Manager, or other Windows-specific runtime behavior. Preserve the protected production mini PC's existing Smart App Control and Code Integrity posture; never weaken it to run tests.

Before deployment to that protected host, production Host Agent binaries must use a trusted Windows code-signing strategy accepted by the active policy. Protect signing keys and document release verification and rotation. This is a future requirement only: do not purchase or implement signing infrastructure in the current task.

See [Windows Development and Testing Environment](../development-and-testing.md) for the approved verification environment and commands.

## Documentation deliverables

Subsequent authorization on 2026-10-11 permits completing the V1 product with safe local administration, MSI packaging and signing support for an existing owner-provided certificate. This supersedes the earlier Task-4-only signing implementation limit above; it does not authorize purchases, new certificate trust or weakening production policies. Current completion follows [the product plan](../plans/2026-10-11-v1-product-completion.md).

Document architecture and data flow, privilege boundaries, protocol and helper whitelist, setup/configuration, protected secrets, Windows Service installation and recovery, SQLite path/migrations/retention, operating modes, testing commands, security assumptions, and known limitations. Documentation must match implemented behavior.

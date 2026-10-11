# Host Agent V1 Implementation Plan

> **For agentic workers:** Use the host's available task-by-task implementation workflow. Steps use checkbox syntax for tracking.

**Goal:** Build Host Agent V1 as a secure local Windows monitoring service with robust diagnosis, incident history, approval-gated typed remediation, notifications, and outbound heartbeat.

**Architecture:** A platform-neutral HostAgent.Core defines telemetry and safety contracts. Windows collectors, SQLite, Named Pipes, OS adapters, notifications and heartbeat live in HostAgent.Infrastructure; an unprivileged Windows Service orchestrates them and a separate narrowly scoped helper executes only typed allowlisted operations.

**Tech Stack:** C# 14 / .NET 10 LTS, Windows Services, Windows Named Pipes, SQLite via Microsoft.Data.Sqlite, xUnit, PowerShell/.NET CLI, Git.

## Global Constraints

- Core targets net10.0 and has no Windows Service hosting dependency.
- HostAgent.Service runs with ordinary service rights; HostAgent.Helper is separate and uses LocalSystem only where required.
- No public HTTP listener, general command/process launcher, PowerShell/CMD executor, arbitrary registry/file API, or default autonomous destructive action.
- Collectors are isolated, cancellable, timeout-bounded, backoff-limited, and report failures without taking down peers.
- One-minute default sampling is configurable without making database keys depend on the interval. System collection records Windows version, boot/uptime and observed shutdown/restart facts; CPU/memory records total usage, pressure, supported commit/pagefile and top processes; disk records capacity/free/use and supported I/O/latency while treating SMART access failure as partial; process/service collection records PID, optional executable path, resource use and service changes/crashes/restarts; network collection records adapters, addresses, gateway/DNS and bounded gateway/IP/DNS/latency/loss probes; Event Log collection stores only bounded relevant System/Application warning/error/critical metadata.
- SQLite uses explicit schema migrations and parameterized SQL. Keep detailed telemetry at least 30 days, aggregate hourly/daily, and never automatically delete audit entries.
- Fixed critical rules are active immediately; adaptive learning windows are days 0–7 informational, days 8–30 lower-confidence active, and after day 30 full 30-day rolling baseline. Adaptive statistics are robust and never arithmetic mean alone.
- Diagnostic confidence is deterministic/statistical; uncertain evidence means no automatic remediation.
- Incident creation is correlated/deduplicated. Incident snapshots preserve contemporaneous telemetry and evidence.
- Incident lifecycle includes Detected, Active, Acknowledged, RemediationProposed, RemediationPendingApproval, Remediating, Resolved, Failed and Escalated. Snapshots include system, CPU/RAM, disk, top processes, affected services, network, relevant event metadata, baseline comparisons and diagnostic evidence.
- Policy decisions are READ_ONLY, AUTO_ALLOWED, CONFIRM_REQUIRED, or DENIED; errors fail closed. Shadow mode is the default.
- Named Pipe requests are versioned, typed, size/time/parameter bounded, authenticated, ACL restricted, and reject unknown types. The helper independently validates its whitelist; client-supplied process names are not identity.
- Typed helper capabilities are individually allowlisted: start/stop/restart a service, terminate an approved process, flush DNS, renew DHCP on an approved adapter, and restart Windows only after confirmation. No generic executor exists.
- All proposed/privileged actions are audited append-only. Kill switch, global read-only, rate/cooldown/attempt and restart-loop protections fail safe.
- Secrets are not stored in source or repository. Production secrets use Windows-protected storage scoped to the service identity.
- Notifications and heartbeat failures cannot stop monitoring. Only outbound HTTPS heartbeat; no inbound port.
- INFO is local-only, WARNING is a normal push, HIGH is elevated priority, and CRITICAL supports acknowledgement/retry. V1 includes Pushover and an outbound Healthchecks.io-compatible heartbeat.
- Do not implement dashboard, HA, Zigbee, VirtualBox, Tailscale, other hosts, advanced GPU, BIOS/firmware, Windows Update automation, arbitrary remote shell, full MCP or fully autonomous mode.
- Every stage requires fresh build, focused and integration tests, warning review, secret scan, changed-file review and an honest list of unverified runtime surfaces.

---

### Task 1: Foundation, six read-only collectors, and SQLite

**Status:** Task 1 build, test, and independent source-review gates pass. Git status/diff/commit commands remain unavailable because Code Integrity blocks the Git executable. Task 2 was started but remains incomplete; Task 3 has not started. Task 2 does not depend on a Task 1 Git commit.

**Files:**

- Create: HostAgent.sln, global.json, .gitignore, Directory.Build.props, Directory.Packages.props
- Create: src/HostAgent.Core/HostAgent.Core.csproj
- Create: src/HostAgent.Core/Models/Telemetry/*.cs and Models/Health/*.cs
- Create: src/HostAgent.Core/Contracts/ITelemetryCollector.cs and IHostAgentStore.cs
- Create: src/HostAgent.Infrastructure/HostAgent.Infrastructure.csproj
- Create: src/HostAgent.Infrastructure/Collectors/{System,CpuMemory,Disk,ProcessesServices,Network,EventLog}Collector.cs
- Create: src/HostAgent.Infrastructure/Storage/SqliteHostAgentStore.cs and Migrations/*.cs
- Create: src/HostAgent.Service/HostAgent.Service.csproj, Program.cs and CollectionCoordinator.cs
- Create: src/HostAgent.Helper/HostAgent.Helper.csproj and Program.cs with no generic execution endpoint
- Create: tests/HostAgent.Core.Tests/HostAgent.Core.Tests.csproj
- Create: tests/HostAgent.Infrastructure.Tests/HostAgent.Infrastructure.Tests.csproj
- Test: Core collector-result/health behavior and SQLite temporary-database migration/read/write/reopen integration.

**Interfaces:**

- ITelemetryCollector<T>.CollectorId and CollectAsync(CancellationToken) -> ValueTask<CollectorResult<T>>.
- CollectorResult<T> carries sample timestamp, status, optional value, and bounded failure metadata.
- IHostAgentStore exposes schema initialization, telemetry append/read, service-health append, and baseline/incident/audit/remediation append seams used by later stages.
- Each collector returns a typed V1 payload for its domain; storage persists versioned JSON payloads plus indexed timestamps and collector IDs.

- [x] **Step 1: Add failing Core contract tests**

Cover success, partial/unavailable collector results and cancellation through ITelemetryCollector<T>; assert unavailable results carry no fabricated payload. The tests must fail for wrong status/payload combinations or swallowed cancellation.

- [x] **Step 2: Verify expected red**

Run: dotnet test tests/HostAgent.Core.Tests/HostAgent.Core.Tests.csproj --filter FullyQualifiedName~CollectorResult
Expected: compile/test failure because the typed models and contracts do not exist.

- [x] **Step 3: Add solution and minimum Core contracts**

Define immutable records for six typed telemetry payload families, collector status/health, telemetry snapshot envelope and ITelemetryCollector<T>. Keep Core free of Windows namespaces and service-hosting packages.

- [x] **Step 4: Verify focused green**

Run the same CollectorResult filter. Expected: focused result and cancellation tests pass.

- [x] **Step 5: Add SQLite migration/store tests before storage**

Against a fresh temporary file assert migration version, required table creation, parameterized round trip of telemetry containing quotes and Unicode, persisted UTC timestamp, migration idempotency and successful reopen. Assert audit insertion is append-only through the public store interface. Expected red is missing schema/repository behavior, not fixture setup failure.

- [x] **Step 6: Implement initial SQLite schema and store**

Create ordered migrations for telemetry, baseline, incident, incident snapshots, diagnostic evidence, audit, remediation attempts, service health, schema versions and hourly/daily aggregates. Use transactions, foreign keys, parameters, fixed table/column names, payload schema versions and UTC timestamps. Add telemetry append/read and health append methods; do not implement audit deletion.

- [x] **Step 7: Verify SQLite focused green**

Run: dotnet test tests/HostAgent.Infrastructure.Tests/HostAgent.Infrastructure.Tests.csproj --filter FullyQualifiedName~SqliteHostAgentStore
Expected: all fresh-db, reopen, migration and data-boundary assertions pass.

- [x] **Step 8: Add collector contract tests and implement collectors**

Use safe read-only OS APIs. Assert stable domain IDs and valid result shape for System, CPU/memory, Disk, Processes/Services, Network and Event Log. System tests assert OS version/build and non-negative uptime; CPU/memory tests assert finite 0–100 percentages and non-negative available/commit values when supported; disk tests assert used+free does not exceed capacity and SMART denial is partial; process/service tests assert positive PIDs and inaccessible executable paths remain null while a service query failure is reported; network tests assert adapters/DNS/gateway shape and bounded probes; Event Log tests assert metadata-only results and persisted bookmarks for restart-safe catch-up. Integration tests accept access-denied/unavailable fields as partial results. Every module respects cancellation/timeout and its failure does not suppress a peer's result or service-health record. No test restarts, kills or modifies host state.

- [x] **Step 9: Run Task 1 full verification**

Run: dotnet build HostAgent.sln --configuration Release
Run: dotnet test HostAgent.sln --configuration Release
Expected: all projects compile, unit and Task 1 integration tests pass, and no unexpected compiler warnings remain. Review full output, scan tracked files for secret-like values, inspect git diff --check and all changed files. Mark real Windows Service installation and privileged host runtime unverified.

Current result: Release build passes with 0 warnings and 0 errors; all 34 tests pass (8 Core and 26 Infrastructure). Secret-like-value and trailing-whitespace scans found no matches. Event Log recovery tests cover truncated batches, repopulated log resets with both valid and invalid bookmarks, live EventRecordID equality/greater-than queries, and a two-cycle legacy raw bookmark migration. The independent read-only review found and helped resolve checkpoint cursor and compatibility issues, then confirmed no remaining concrete findings. Git is denied by Code Integrity, so git diff --check is unavailable.

- [ ] **Step 10: Commit the independently passing stage**

Run: git add .gitignore Directory.Build.props Directory.Packages.props global.json HostAgent.sln src tests docs/specs/host-agent-v1.md docs/plans/2026-10-06-host-agent-v1.md
Run: git commit -m "feat: add host agent foundation and telemetry"
Expected: one local commit contains only the verified Task 1 foundation.

Current result: blocked; Git cannot load its libpcre2-8-0.dll under the active Code Integrity policy. No commit was created. Task 2 does not depend on a Git commit; keep the stage boundary documented.

### Task 2: Robust baseline, correlated diagnosis, incident lifecycle and snapshots

**Status:** In progress; focused test execution is blocked by Windows Code Integrity after the diagnostic test assembly was rebuilt. The baseline engine and its focused tests are implemented; diagnostic rules are present but currently unverified. Independent review found three unresolved P2 issues in event correlation/attribution. Incident lifecycle/deduplication and durable snapshot/coordinator integration remain incomplete. See [Task 2 verification](../verification/task-2.md). Do not advance to Task 3 until Task 2 passes its test and review gates.

**Environment requirement:** Resume behavioral verification in an approved Windows development/test environment that permits local development assemblies to execute. Do not change the production mini PC's Smart App Control/Code Integrity policy. WSL does not satisfy Windows-specific integration verification. Production deployment will require a trusted Windows signing strategy accepted by the protected host; signing infrastructure is future work and is not being purchased or implemented now. See [Windows Development and Testing Environment](../development-and-testing.md).

**Files:**

- Create: src/HostAgent.Core/Models/Diagnosis/*.cs, Models/Incidents/*.cs, Models/Baseline/*.cs
- Create: src/HostAgent.Core/Diagnostics/{BaselineEngine,DiagnosticEngine,IncidentEngine}.cs
- Create: Infrastructure storage migrations/repositories for aggregation and durable incident data
- Modify: src/HostAgent.Service/CollectionCoordinator.cs to feed engines and snapshot creation
- Test: tests/HostAgent.Core.Tests/{BaselineEngine,DiagnosticEngine,IncidentEngine}Tests.cs and storage snapshot/aggregation integration tests.

**Interfaces:**

- IBaselineEngine.ObserveAsync(metric, scope, timestamp, value) -> BaselineAssessment.
- IDiagnosticEngine.EvaluateAsync(HostEvidence, CancellationToken) -> IReadOnlyList<Diagnosis>.
- IIncidentEngine.ApplyAsync(Diagnosis, IncidentSnapshot, CancellationToken) -> Incident.
- Diagnosis contains severity, confidence 0–100, evidence, primary cause, secondary symptoms, recommended typed remediation description, timestamp and incident state.

- [ ] **Step 1: Add failing deterministic tests**

Use literal time-series fixtures: one isolated high CPU minute creates no sustained-CPU diagnosis; sustained high CPU with one dominating process points to that process; high memory pressure yields the asserted diagnosis; critically full disk plus update/pagefile event selects disk capacity as primary cause; gateway/IP success plus DNS failure selects DNS; gateway failure is distinguished from DNS; service failure/restart events point to the affected service; multiple symptoms with one shared cause produce one primary cause and secondary symptoms; repeated same diagnosis updates one incident. Exercise every legal incident transition through Acknowledged, RemediationProposed, RemediationPendingApproval, Remediating, Resolved, Failed and Escalated, rejecting invalid transitions. Days 0–7 adaptive signal is informational, days 8–30 confidence is reduced, day 31 uses a 30-day robust window. Assert hand-calculated median/MAD and deterministic confidence.

- [ ] **Step 2: Verify focused expected red**

Run dotnet test for each Core test project filter FullyQualifiedName~BaselineEngine, then DiagnosticEngine and IncidentEngine. Expected: missing engine/types or missing asserted behavior; no setup or compilation-only false red.

- [x] **Step 3: Implement robust baseline and fixed-rule precedence**

Maintain bounded per-metric/scope samples, median, MAD/robust deviation, percentiles, trend, duration, recurrence and count. Implement exact learning periods and confidence reduction. Keep critical fixed rules active independently of adaptive readiness.

Current result: baseline implementation compiles and its two focused tests passed before the test DLL was rebuilt for diagnostic fixtures. The baseline filter was not rerun after that rebuild; the rebuilt assembly was subsequently blocked during focused DiagnosticEngine runs. Treat the earlier 2/2 result as historical, not as the final Task 2 gate.

- [ ] **Step 4: Implement root-cause correlation and incident state transitions**

Implement deterministic rules for CPU/process, memory pressure, disk plus related errors, DNS vs gateway vs internet, and service crash. Correlate by normalized issue key and update existing incidents. Uncertain or brief evidence cannot request autonomous action.

- [ ] **Step 5: Add durable snapshot and aggregation integration**

Assert that an incident snapshot preserves Windows/boot facts, CPU/RAM, disk, top processes, affected services, network interfaces/reachability, relevant event metadata, baseline comparison and evidence after later telemetry changes. Assert hourly/daily aggregates preserve metrics when detailed telemetry ages beyond 30 days, raw retention remains at least 30 days, and audit records are not removed by retention.

- [ ] **Step 6: Run Task 2 full verification**

Run dotnet build HostAgent.sln --configuration Release and dotnet test HostAgent.sln --configuration Release. Expected: full unit/integration suite passes, warning review is clean, and no host remediation was run.

- [ ] **Step 7: Commit the independently passing stage**

Run git add src tests docs and git commit -m "feat: add baseline diagnostics and incidents". Expected: commit contains Task 2 changes only.

### Task 3: Fail-closed policy, authenticated typed Named Pipes, helper, audit, and guardrails

**Files:**

- Create: src/HostAgent.Core/Models/Policy/*.cs, Models/Remediation/*.cs, Models/Audit/*.cs, Contracts/IHelperClient.cs
- Create: src/HostAgent.Core/Policy/PolicyEngine.cs
- Create: src/HostAgent.Infrastructure/Pipes/{HelperProtocol,NamedPipeHelperClient,NamedPipeHelperServer}.cs
- Create: src/HostAgent.Infrastructure/Security/{PipeAuthorization,HelperAllowlist,RemediationGuard}.cs
- Modify: src/HostAgent.Helper/Program.cs and typed operation handlers only
- Modify: Infrastructure storage for append-only audit and remediation repositories
- Test: Core PolicyEngineTests and Infrastructure HelperProtocol, NamedPipeSecurity and RemediationGuard tests.

**Interfaces:**

- IPolicyEngine.Evaluate(PolicyContext) -> PolicyDecision; exception or unavailable configuration becomes DENIED.
- IHelperClient.ExecuteAsync(HelperOperationRequest, CancellationToken) -> HelperOperationResponse.
- Versioned protocol defines a closed discriminator set and typed parameter records for each independently enabled operation; no type-name handling or arbitrary JSON object activation.
- IAuditStore.AppendAsync(AuditRecord, CancellationToken) never exposes update/delete methods.

- [ ] **Step 1: Add failing policy and protocol tests**

Assert READ_ONLY denies every mutating operation; explicit approval permits only the matching operation/incident; unknown operation/version, extra/invalid parameters, malformed and oversized frames are rejected; unauthorized SID is rejected; caller-supplied process name cannot bypass helper allowlist; timeout and helper-unavailable map to no execution; policy exception means DENIED; repeated failures trip cooldown/attempt/restart-loop limits; audit records append for proposal and execution outcome.

- [ ] **Step 2: Verify expected red**

Run focused Core PolicyEngine and Infrastructure HelperProtocol tests. Expected: missing or incorrect public behavior is the failure reason.

- [ ] **Step 3: Implement fail-closed policy and typed protocol**

Define fixed protocol v1 request/response records, maximum frame size, explicit serializer options, exact validation, timeouts and cancellation. Reject unknown versions and operations. Pipe ACL admits only the configured Host Agent service identity; server also checks the authenticated Windows client token and local allowlist.

- [ ] **Step 4: Implement helper-local allowlist and guardrails**

Add individually registered service/process/adapter identifiers and typed handlers for only explicitly enabled operations. Revalidate all parameters inside Helper. No generic process launch, command parser, arbitrary path or registry handler. Apply global kill switch/read-only, per-operation rate/cooldown/attempt, and restart-loop protections before invoking a fakeable executor.

- [ ] **Step 5: Add append-only audit and approval binding**

Persist proposal, decision, approval identity/time, execution start/end, result and error as distinct append-only events linked to incident/diagnosis/evidence and operation request. A stale or mismatched approval is denied.

- [ ] **Step 6: Run Task 3 tests and explicit security review**

Run dotnet build HostAgent.sln --configuration Release and dotnet test HostAgent.sln --configuration Release. Review privilege boundary, ACL, framing, serializer, whitelist, path handling, process identity, service identity, secret handling, sensitive log fields, injection surfaces and absence of arbitrary execution. Record each finding and evidence in docs/security/task-3-review.md.

- [ ] **Step 7: Commit the independently passing stage**

Run git add src tests docs/security/task-3-review.md and git commit -m "feat: add approved helper operations". Expected: Task 3 code and explicit security review are verified.

### Task 4: Notifications, outbound heartbeat, service readiness and operating modes

**Files:**

- Create: src/HostAgent.Infrastructure/Notifications/PushoverNotifier.cs
- Create: src/HostAgent.Infrastructure/Heartbeat/HealthchecksHeartbeat.cs
- Create: src/HostAgent.Infrastructure/Security/ProtectedSecretStore.cs
- Modify: src/HostAgent.Service/Program.cs, CollectionCoordinator.cs, workers and configuration
- Modify: src/HostAgent.Helper/Program.cs only for Windows Service hosting configuration
- Create: docs/architecture.md, docs/operations/{install,configuration,recovery,storage}.md and docs/security/assumptions.md
- Test: notification, heartbeat and configuration with fakes or in-process HTTP handlers; no real credentials required.

**Interfaces:**

- INotificationSender.SendAsync(Notification, CancellationToken) is best effort and reports failures to local health/audit.
- IHeartbeatSender.SendAsync(AgentHealth, CancellationToken) makes only outbound HTTPS requests with bounded timeout.
- Operating mode config is explicit: Shadow by default; Confirmation requires a fresh approval; LimitedAutonomous remains disabled.

- [ ] **Step 1: Add failing adapter/host tests**

Assert INFO is local-only; WARNING/HIGH map to Pushover priorities; CRITICAL stores acknowledgement/retry state; failed HTTP delivery degrades adapter health without stopping collection; heartbeat uses injected outbound transport with timeout/cancellation; secrets are absent from logs/config samples; service worker schedules bounded cycles and shuts down on cancellation; Shadow mode records a proposal without calling Helper; Confirmation requires matching approval.

- [ ] **Step 2: Verify expected red**

Run focused Notification, Heartbeat and OperatingMode test filters. Expected: missing adapter/host behavior, not external network or credential setup.

- [ ] **Step 3: Implement notification, secret and heartbeat adapters**

Use typed options, OS-protected secrets under the service identity, TLS validation, bounded request timeouts and redacted structured logs. Provide offline tests through injected HTTP transport. Live API credentials are not required for tests.

- [ ] **Step 4: Implement service startup and recovery configuration**

Configure automatic Windows Service hosting without interactive login, graceful cancellation, structured logging, restart/backoff and documented SCM recovery. Keep the service unprivileged. Tests never install or restart a service.

- [ ] **Step 5: Complete operations and security docs**

Document actual service identities, helper protocol/allowlist, SQLite path/schema/retention, protected credential provisioning/recovery, configuration precedence, Shadow and Confirmation behavior, Windows service recovery, testing and known unverified surfaces.

- [ ] **Step 6: Run full Task 4 verification**

Run dotnet build HostAgent.sln --configuration Release, dotnet test HostAgent.sln --configuration Release, warning review, secret scan and changed-file review. Clearly distinguish fake transport tests from live delivery and simulated pipe tests from installed services.

- [ ] **Step 7: Commit the independently passing stage**

Run git add src tests docs and git commit -m "feat: add host agent notifications and service readiness". Expected: Task 4 is committed only after full verification.

## Plan self-review and execution route

The four task boundaries match the user-defined stage gates. Every requested V1 domain maps to Task 1–4 or a global constraint. Exact interfaces are repeated where later tasks may be implemented independently. Tests state observable outcomes and avoid real credentials or destructive host changes. Paths are proposed because the inspected workspace was empty. No externally observable product choice remains unresolved; conservative defaults are defined in this plan and the specification.

Available modes are Subagent-Driven Development for independently safe tasks, with a review between each task, and Inline Execution for task-by-task work in this thread. The current run is using Inline Execution; delegation will be considered only after shared contracts are stable and an independent seam is demonstrably safe.

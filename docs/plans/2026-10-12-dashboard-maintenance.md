# Local dashboard and maintenance implementation plan

> **For agentic workers:** Use the host's available task-by-task implementation workflow. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Provide a Czech desktop overview, incident history, evidence-based recommendations and safe operator workflows, then extend maintenance capabilities only with bounded previews and approvals.

**Architecture:** A WPF desktop client runs as the interactive user. A separate read-only Named Pipe serves bounded, redacted projections from the monitor's SQLite store. The existing administrator pipe and typed helper retain exclusive mutation authority. Maintenance starts with guided Windows settings; file/package operations are separate capabilities, never arbitrary commands.

**Tech Stack:** .NET 10.0.401 / runtime 10.0.12, C# 14, WPF, Windows Forms tray icon, SQLite, xUnit, authenticated Named Pipes.

**Current checkpoint:** Tasks 1–4 source implementation and automated checks
are complete as one integrated increment (shared contracts make the proposed
individual commits less useful). Real synthetic WPF open/render/disconnect/
close passed. Final Release build: zero warnings/errors; full unchanged
repeat: 277/277 passed. The first final run had one preserved CLI timing
failure; see verification/dashboard-1.md. Independent immutable review
approved ea8a54b6f6b0206b43d6d7d7a631f7d249307c0b. Installed/tray/UAC/DPI
acceptance remains separate. The final integrated design received exclusive
build0 warnings/errors and **282/282 passed** (58/218/6), independent approval
at tree d8d5359dcaf8939ad0678641f66cab708a3e0f41. Task 5 is disabled subsequent
scope, not a completed capability. Task 6 source gates passed; one integrated
source commit replaces the proposed per-file milestone commits below.

## Global Constraints

- Work only in verified VirtualBox DEV VM UUID 46543236-8C5E-49DA-953C-DB17F9B1E402. Base d2168957753c3d7ad313b6f18c1aa4400831d546; branch codex/local-dashboard.
- Preserve the existing untracked audit and all prior commits. Do not change production host, security policy or clean-win-base.
- Use synthetic tests/transports; no real secrets, notification delivery or destructive smoke operations.
- Never give the desktop direct database/secret access. Read access is local interactive identities only; network logons denied, service SID pinned, frames/deadlines bounded.
- No operation, setting or recommendation auto-approves execution. Shadow remains default. Existing CLI approval still binds exact request, diagnosis and target.
- Missing/stale data is explicitly unavailable. No invented speed gain, confidence, driver attribution or safe-to-delete claim.
- All new behavior follows observed red/green tests. Record actual commands/results separately from historical evidence.
- Deliver in usable increments; unfinished later capabilities must stay visibly unavailable and be reported honestly.

---

### 1. Bounded view and recommendation model

Files: create Core/Dashboard/DashboardModels.cs, RecommendationEngine.cs; tests/Core.Tests/DashboardRecommendationTests.cs.
Interfaces: DashboardSnapshot with timestamped optional metrics, bounded process/service/disk rows and incident summaries; RecommendationEngine.Evaluate(snapshot, now) returns deterministic Recommendation cards with code/evidence/benefit/impact/rollback/action.

- [x] Add failing tests for sustained CPU/RAM, disk shortage, stale/future/missing evidence and crash confidence scope; execute `dotnet test tests/HostAgent.Core.Tests/HostAgent.Core.Tests.csproj -c Release --filter DashboardRecommendation` and observe assertions.
- [x] Implement conservative rules with stable identities; no executable paths, arbitrary text or mutation delegates in models.
- [x] Run identical focused test and affected Core suite.
- [ ] Commit explicit model/test files after milestone verification: `feat: add evidence-based dashboard recommendations`.

### 2. Read-only local IPC and durable data projection

Files: create Infrastructure/Dashboard/ViewProtocol.cs, ViewPipeClient.cs; Service/DashboardDispatcher.cs; extend ServiceAdministration.cs; tests/Infrastructure.Tests/DashboardViewTests.cs.
Interfaces: v1 exact commands overview {}, history {metric} with fixed cpu/memory choices, incident {incidentId}, investigate {}. View dispatcher has no secret/helper dependency (read-only mode projection uses configuration). Responses max16KiB; rolling 24h history max97 bucket boundaries; incident data exposes codes/numbers/times, not raw observations/XML/paths/IPs.

- [x] Add failing protocol, projection/redaction, stale-data and actual local pipe tests; `dotnet test tests/HostAgent.Infrastructure.Tests/HostAgent.Infrastructure.Tests.csproj -c Release --filter DashboardView`.
- [x] Implement exact schemas, fixed errors, store queries with limits, fresh timestamps, independent interactive-only pipe using existing authenticated server. Keep admin endpoint authorization unchanged.
- [x] Run focused + Administration/NamedPipe/Health tests; inspect actual serialized frame sizes and rejection of mutation commands.
- [ ] Commit explicit IPC/projection/test files with `feat: expose bounded read-only dashboard views`.

### 3. Czech desktop, charts, detail, recommendations, export and tray

Files: create src/HostAgent.Desktop/{csproj,Program.cs,MainWindow.cs,DashboardPresentation.cs}; extend solution; tests/Infrastructure.Tests/DashboardDesktopTests.cs.
Interfaces: normal-user WPF UI consumes view model over pinned pipe; status/incident/recommendations tabs; history graphs; local redacted JSON export selected by operator. Tray hide/show, refresh cancellation, stale/disconnected state, single refresh at a time.

- [x] Test public presentation/controller seams for errors, stale data, selection and redacted exports before implementation; focused `--filter DashboardDesktop`.
- [ ] Implement accessible Czech text, keyboard navigation and DPI-resizable layout; no raw database reads or automatic external actions.
- [x] Run focused tests and safe real desktop open/refresh/close smoke in VM. Record limitations if interactive automation is unavailable.
- [ ] Commit explicit UI/tests/solution files: `feat: add local Czech Host Agent desktop`.

### 4. Safe guided operations and before/after review

Files: extend Desktop presentation/commands; tests/DashboardDesktopTests.cs; docs/operations/dashboard.md.
Interfaces: fixed maintenance destinations Storage/Apps/Startup; exact existing CLI proposal and approve flow launched from a protected installed CLI only, UAC on operator request. Kill switch uses existing typed configure command. No user-selected executable or free command string. Capture view before operation; refresh afterward and distinguish pending/unknown from observed improvement.

- [x] Add failing operation-argument/target/Shadow tests; observe focused assertion failures.
- [x] Implement fixed typed argument building and trust checks; explicit consequences and CLI's own final exact approval. Guided settings allow operator review of OS-managed cleanup/app choices.
- [x] Test no automatic launch from recommendations, invalid IDs/PIDs rejected, unsafe CLI paths denied. Run focused regression and document manual operation limits.
- [ ] Commit explicit files: `feat: add bounded dashboard operator workflows`.

### 5. Independently scoped maintenance expansion (subsequent increment)

Files: new Core/Models/Maintenance models, Infrastructure/Maintenance preview/quarantine adapters, helper protocol/journal/security tests; exact file map to be expanded after reviewing OS/user ownership boundaries from the first increment.

- [ ] Define temporary-file preview pinned to current user's approved temp root, per-file identity/hash/age/size and one-use manifest approval; default quarantine with bounded retention and restore. Deny reparse points, changed files, system directories and other users.
- [ ] Add red tests for substitution/races/expired approval/partial failure, then implement one bounded cleanup capability. No permanent erase in first version.
- [ ] Add application inventory/recommendations only where measured evidence exists. Installation/update/removal requires verified package identity, source/publisher/hash, exact version and operator approval; unsupported packages remain guided-only.
- [ ] Independently review each capability and run full VM verification before enabling it. No generic shell or broad filesystem helper API.

### 6. Integration and release gates

Files: docs/specs/host-agent-v1.md, docs/verification/dashboard-1.md, docs/operations/dashboard.md; release packaging once new desktop acceptance passes.

- [x] Fresh `dotnet build HostAgent.sln -c Release`, `dotnet test HostAgent.sln -c Release`; diff/secret/boundary review and immutable independent milestone review.
- [ ] Record fresh runs, counts and live unverified areas. Update plan checkboxes only for demonstrated completion.
- [ ] Commit exact reviewed increment files; retain original audit untracked. No production transfer. Package/deploy only after tests and explicit package-manifest/trust gates; distinguish build from installed acceptance.

Engineering recommendation: keep telemetry/diagnostic explanations local and deterministic first. An optional explanatory assistant may be added later only with redaction, opt-in data egress and zero authority to approve operations. This is not part of the current implementation.

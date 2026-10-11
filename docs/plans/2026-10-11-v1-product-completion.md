# Host Agent V1 product completion Implementation Plan

> **For agentic workers:** Use the host's available task-by-task implementation workflow. Steps use checkbox (`- [x]`) syntax for tracking.

**Goal:** Deliver an installable and operable V1 Windows product with no source-code edits required for ordinary installation, secret provisioning or per-operation approval.

**Architecture:** Preserve Core, protected monitoring service and independent SYSTEM helper. Add an elevated local CLI and a bounded, authenticated administrator-only local Named Pipe for status, proposals, execution and service-identity secret provisioning; helper authority and its four-operation allowlist remain independent. Package self-contained win-x64 binaries with safe install/uninstall, checksums and a production signature gate.

**Tech Stack:** C#/.NET 10.0.12, SDK 10.0.401, existing SQLite/Windows Services packages, Windows ACL/DPAPI/Named Pipes, PowerShell 7 release tooling, WiX 5.0.2 MSI, xUnit.

## Global Constraints

All builds/tests run only inside verified DEV VM AgentPC/VirtualBox UUID 46543236-8C5E-49DA-953C-DB17F9B1E402. Preserve clean-win-base, host project/security policies and existing history. Base 2932cb44749283af54cc2f2f52472405eec29c6d is preserved by task-4-verified-vm; new branch codex/v1-release-readiness. Explicit user authorization on 2026-10-11 supersedes the former Task-4-only limit. No dashboard, public HTTP API, generic shell, default autonomous mode or new helper mutation capability. Real secrets/destructive operations are not used in verification. Full Release gates and independent source review precede publication. Production execution requires accepted trusted signatures; no policy bypass or certificate purchase.

## Observable product choices

Engineering recommendations adopted under the user's delegated implementation discretion: elevated local CLI with help/JSON status and fixed error codes (exit 0 success, 1 unavailable/rejected, 2 invalid usage); no secrets in arguments/files/logs, interactive masked secret entry only; per-operation approval requires typing the request GUID after reviewing complete parameters/confidence/evidence and resolved local target; no unattended approval switch. Installer defaults Shadow/read-only, outbound disabled and empty helper registrations. Repair/upgrade preserves existing configuration/data; uninstall stops/deletes only the two registered product services and removes package files, preserving all ProgramData. Published release is Windows x64, self-contained, non-single-file for native SQLite compatibility. Explicit DEV-only unsigned install is bound to the verified VM; production signature checks cannot be disabled by a generic switch.

### Task A: Authenticated local administration and service-identity provisioning

**Files:** Proposed src/HostAgent.Infrastructure/Administration/{AdminProtocol,AdminPipe,ApprovalIssuer}.cs; src/HostAgent.Service/{AdminDispatcher,AdminWorker}.cs; modify Program.cs; tests/HostAgent.Infrastructure.Tests/AdministrationTests.cs.

**Interfaces:** AdminProtocol encodes exact version/command/body JSON (16 KiB framing reused from HelperProtocol); AdminPipeServer/Client expose one bounded request with pinned server SID and authenticated local administrator token; AdminDispatcher.DispatchAsync(byte[], token) returns fixed-code JSON; ApprovalIssuer issues a <=10-minute digest-bound OperationApproval into the helper-owned journal using the trusted local caller identity.

- [x] Add compiled contract stubs and behavioral tests for malformed/duplicate/unknown envelopes, version/size rejection, wrong server identity, untrusted token, real local pipe cancellation and round-trip; status/incident summaries, Shadow proposal never calling helper, secret write under serving identity with synthetic DPAPI round-trip, exact request approval/digest and stale/changed requests.
- [x] Run focused AdministrationTests with the existing VM evidence runner; record actual expected runtime failures before implementation.
- [x] Implement fixed command dispatch (status, incidents, propose, execute, secret-set), bounded local transport, no raw exception/payload logs, and a separately hosted worker. Secret-set cannot read/exfiltrate secrets; helper approvals cannot be created through this monitoring pipe. Proposals derive confidence/diagnosis/evidence from durable current incidents; execute still uses RemediationCoordinator and helper-owned approvals.
- [x] Rerun focused tests after each behavior change and integration against existing OperatingMode/NamedPipeSecurity/ServiceLifecycle tests.

### Task B: Usable elevated CLI

**Files:** Proposed src/HostAgent.Cli/{HostAgent.Cli.csproj,Program,CliApplication}.cs, solution/project references, tests/HostAgent.Infrastructure.Tests/CliTests.cs.

**Interfaces:** CliApplication.RunAsync(args, input/output seams, local transport, approval provisioning, token) exposes help, status, incidents, propose, approve-execute and secret-set. Runtime composition supplies actual elevated token, pinned virtual service SID, protected helper paths and masked console input.

- [x] Add tests for no/unknown args, status/incident JSON output, synthetic masked-secret boundary without argument echo, refusal on mismatched confirmation, approval persisted before one exact execute, and redacted errors. Observe expected focused failures.
- [x] Implement strict command parsing and bounded protected proposal file loading/writing. Propose saves typed request only; approve-execute displays exact request/digest, requires GUID, validates helper configuration/approver and records exact immutable approval before dispatch. No remote hosts, arbitrary endpoints or native mutation handlers in CLI.
- [x] Run focused CliTests plus administration/remediation tests. Add operator quickstart without source-code provisioning requirements.

### Task C: Reproducible release, installer and acceptance tooling

**Files:** tools/{Build-Release,Build-Installer,Test-Installer,Install-HostAgent,Test-Release}.ps1, Infrastructure/Administration/{InstallationBootstrap,OperatorConfiguration}.cs, CLI/ConfigurationWorkflow.cs, focused tests and docs/operations/quickstart.md. Uninstallation uses native Windows Installer.

**Interfaces:** Build-Release publishes Service, Helper and CLI with win-x64 --self-contained true, copies docs/tooling, builds MSI, hashes final files and creates a source-external directory/ZIP. Test-Release validates signed manifest paths/hashes and publisher signatures. MSI registers virtual-account monitor and SYSTEM helper, recovery/Event Log sources and protected defaults using a deferred SYSTEM-only bootstrap, then starts services. MSI rollback controls its registered resources; bootstrap data remain preserved. Test-Installer reads actual MSI tables. Install-HostAgent is an optional PowerShell 7 validation/installation wrapper; native MSI installation requires no PowerShell execution-policy changes.

Implementation refinement: child Windows PowerShell 5.1 tests encountered the existing Restricted script policy. No policy was changed or bypassed. PowerShell 7 was already available and permitted normal local script execution. Native MSI was selected so production users do not need to install/enable a scripting runtime. Operator configuration commands stop/restart only product services, create validated atomic configuration replacements and retain protected previous versions.

- [x] Execute scripts through child PowerShell with fake SCM/platform boundaries where privileges are required; assert effects, not script text. Establish failing artifact tamper/path escape/existing-install/refusal tests before implementation.
- [x] Implement safe explicit paths and native PowerShell file operations, no force deletion/policy changes. Production gate requires valid trusted signatures for first-party binaries/scripts; unsigned VM acceptance is fixed to this DEV VM identity. Configuration templates are safe parsed examples, not credentials.
- [ ] Actually publish self-contained artifacts and verify apphost help, package checksums, tamper refusal, non-admin install refusal and zip contents. Attempt installed-service smoke only through authorized elevation if available; otherwise retain ready commands and report exact privilege blocker, not a pass.

### Task D: Final acceptance and delivery

**Files:** docs/verification/v1-release.md, docs/security/v1-release-review.md, update README/AGENTS/current operations docs; external release manifest and handoff.

- [ ] Fresh full Release build and suite, focused administration/CLI/tooling, source/diff/secret/dependency review, bounded read-only DEV monitoring smoke/soak and process cancellation where runnable.
- [x] Independent read-only requesting-code-review over exact base to immutable final source tree; preserve and fix verified findings test-first, repeat affected gates.
- [ ] Commit verified completion, publish only its feature branch fast-forward, prepare SHA-256 source/release handoff and verify remote commit. Do not merge main or deploy production without concrete accepted signing/host prerequisites.

## External prerequisites and evidence limits

Real Pushover/Healthchecks accounts and credentials, trusted production signing material/policy acceptance and target-host access cannot be fabricated. Current VM account is not elevated; installed service acceptance requires administrator authentication. These are deployment prerequisites, not permission to weaken policies. Implementation proceeds independently and reports exactly which live gates were actually reached. Inline execution is selected under the user's autonomous-work authorization; a read-only reviewer is dispatched through requesting-code-review at the completed stage.

# Host Agent V1

Windows-first local host monitoring and incident-assistance service built with C# and .NET 10.

First-party source is [MIT licensed](LICENSE); third-party components retain
their own [licenses and notices](docs/legal/dependencies.md).
Read the [privacy and network disclosure](docs/operations/privacy.md) before
installation. Connectivity probes, Pushover and heartbeat are off by default; connectivity diagnostics require an explicit local operator opt-in.

## Code signing policy

The project is **not enrolled or signed by SignPath**. The unsigned GitHub
binary-review archive contains self-contained executables, but no MSI or
installation script; it is not a supported product release. See the
[Code signing policy](docs/operations/code-signing-policy.md),
[current readiness and application draft](docs/operations/signpath-submission-draft-2026-10-25.md),
and [historical preparation evidence](docs/verification/signpath-publication.md).
There is not yet a public release suitable for submission as the release to be
signed.

## Project status

The current application includes the local Windows dashboard, evidence-based recommendations and bounded reversible maintenance. See the [dashboard guide](docs/operations/dashboard.md) and [predeployment checkpoint](docs/verification/predeployment-2026-10-18.md). The resilience records distinguish each tested candidate and its limits; an unsigned DEV package is not a production release.

V1 product completion adds an elevated local CLI, authenticated administrator IPC, service-identity secret provisioning, safe configuration commands, and a self-contained Windows x64 MSI/release pipeline. Shadow/read-only remains the default. Start with the [operator guide](docs/operations/quickstart.md); the [current verification record](docs/verification/v1-release.md) distinguishes executed checks from live deployment prerequisites.

Historical V1 acceptance was completed for executable source commit0a13ea1dd92a2b8cc7eaa7df3d045b85cd1f2b5a: Release build0 warnings/errors,206/206 tests, reviewed privilege boundaries, verified self-contained1.0.2 package and installed service/CLI, synthetic secret provisioning, telemetry progress, graceful restart, upgrade/repair/uninstall/reinstall with retained configuration/data. The unsigned candidate is restricted to the approved VM. Production deployment still requires the owner's trusted signing material, target-policy acceptance and optional outbound credentials; these were not fabricated or bypassed.

Historical Task 4 gates passed in the approved VM (Release build 0 warnings/errors, suite 150/150, focused tests 43/43). [Task 4 verification](docs/verification/task-4.md), [ledger](docs/verification/task-4-runs.md) and [review](docs/security/task-4-review.md) remain unchanged historical evidence. Pushover/Healthchecks verification uses fake transports and synthetic secrets. Trusted production signing and actual endpoint credentials must come from the owner.

- [Specification](docs/specs/host-agent-v1.md)
- [Development and testing](docs/development-and-testing.md)
- [Implementation plan](docs/plans/2026-10-06-host-agent-v1.md)
- [Task 1 verification](docs/verification/task-1.md)
- [Task 2 verification](docs/verification/task-2.md)
- [Task 3 verification](docs/verification/task-3.md)
- [Task 4 verification](docs/verification/task-4.md)
- [Architecture](docs/architecture.md)

Ordinary builds and tests run in the approved Windows DEV VM described in the development guide. The owner separately authorized GitHub-hosted builds/tests for the SignPath path; the hosted review workflow produces explicitly unsigned, non-installing review payloads, with actual run status recorded in the publication continuation. Keep production Code Integrity, VBS, Memory Integrity, and other security policies unchanged. The user subsequently authorized the local dashboard and bounded maintenance. Public APIs, a general executor and unattended approval remain outside scope.

## Continue in Codex on the DEV VM

When opening this repository in Codex inside the approved VM, read [AGENTS.md](AGENTS.md) and current verification records. The [Task 2 continuation prompt](docs/prompts/agentpc-task-2.md) is historical context, not the current stage authorization.

## Solution

The solution separates core behavior, Windows infrastructure, a non-privileged service, and the helper service:

- `src/HostAgent.Core`
- `src/HostAgent.Infrastructure`
- `src/HostAgent.Service`
- `src/HostAgent.Helper`
- `src/HostAgent.Cli`
- `src/HostAgent.Desktop`
- `tests/HostAgent.Core.Tests`
- `tests/HostAgent.Infrastructure.Tests`
- `tests/HostAgent.PipeProbe` (test-only cold-process regression runner; never included in the product)

See [development-and-testing.md](docs/development-and-testing.md) for the pinned SDK and verification commands.

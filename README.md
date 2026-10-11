# Host Agent V1

Windows-first local host monitoring and incident-assistance service built with C# and .NET 10.

## Project status

V1 product completion adds an elevated local CLI, authenticated administrator IPC, service-identity secret provisioning, safe configuration commands, and a self-contained Windows x64 MSI/release pipeline. Shadow/read-only remains the default. Start with the [operator guide](docs/operations/quickstart.md); the [current verification record](docs/verification/v1-release.md) distinguishes executed checks from live deployment prerequisites.

Historical Task 4 gates passed in the approved VM (Release build 0 warnings/errors, suite 150/150, focused tests 43/43). [Task 4 verification](docs/verification/task-4.md), [ledger](docs/verification/task-4-runs.md) and [review](docs/security/task-4-review.md) remain unchanged historical evidence. Pushover/Healthchecks verification uses fake transports and synthetic secrets. Trusted production signing and actual endpoint credentials must come from the owner.

- [Specification](docs/specs/host-agent-v1.md)
- [Development and testing](docs/development-and-testing.md)
- [Implementation plan](docs/plans/2026-10-06-host-agent-v1.md)
- [Task 1 verification](docs/verification/task-1.md)
- [Task 2 verification](docs/verification/task-2.md)
- [Task 3 verification](docs/verification/task-3.md)
- [Task 4 verification](docs/verification/task-4.md)
- [Architecture](docs/architecture.md)

Builds and tests must run in the approved Windows DEV/test environment described in the development guide. Keep production Code Integrity, VBS, Memory Integrity, and other security policies unchanged. The user subsequently authorized completing the V1 product; no dashboard, public API, general executor or unattended approval is included.

## Continue in Codex on the DEV VM

When opening this repository in Codex inside the approved VM, read [AGENTS.md](AGENTS.md) and current verification records. The [Task 2 continuation prompt](docs/prompts/agentpc-task-2.md) is historical context, not the current stage authorization.

## Solution

The solution separates core behavior, Windows infrastructure, a non-privileged service, and the helper service:

- `src/HostAgent.Core`
- `src/HostAgent.Infrastructure`
- `src/HostAgent.Service`
- `src/HostAgent.Helper`
- `src/HostAgent.Cli`
- `tests/HostAgent.Core.Tests`
- `tests/HostAgent.Infrastructure.Tests`

See [development-and-testing.md](docs/development-and-testing.md) for the pinned SDK and verification commands.

# Host Agent V1

Windows-first local host monitoring and incident-assistance service built with C# and .NET 10.

## Project status

Task 4 implementation and source review pass in the approved DEV VM on 2026-10-11: Release build 0 warnings/errors, complete suite 150/150, focused Task 4 tests 43/43. Pushover/Healthchecks use fake transports in verification; DPAPI uses synthetic DEV-account values. Shadow/read-only remains the default. See [Task 4 verification](docs/verification/task-4.md), [command ledger](docs/verification/task-4-runs.md), [security review](docs/security/task-4-review.md) and [operations](docs/operations/install.md). Tasks 2/3 records remain historical evidence. Host handoff is prepared, but transfer/deployment is blocked; installed services, production identity/secrets, live delivery and SCM recovery are unverified.

- [Specification](docs/specs/host-agent-v1.md)
- [Development and testing](docs/development-and-testing.md)
- [Implementation plan](docs/plans/2026-10-06-host-agent-v1.md)
- [Task 1 verification](docs/verification/task-1.md)
- [Task 2 verification](docs/verification/task-2.md)
- [Task 3 verification](docs/verification/task-3.md)
- [Task 4 verification](docs/verification/task-4.md)
- [Architecture](docs/architecture.md)

Builds and tests must run in the approved Windows DEV/test environment described in the development guide. Keep production Code Integrity, VBS, Memory Integrity, and other security policies unchanged. Task 4 was separately authorized after Task 2/3 gates passed. No later feature/stage is authorized.

## Continue in Codex on the DEV VM

When opening this repository in Codex inside the approved VM, read [AGENTS.md](AGENTS.md) and current verification records. The [Task 2 continuation prompt](docs/prompts/agentpc-task-2.md) is historical context, not the current stage authorization.

## Solution

The solution separates core behavior, Windows infrastructure, a non-privileged service, and the helper service:

- `src/HostAgent.Core`
- `src/HostAgent.Infrastructure`
- `src/HostAgent.Service`
- `src/HostAgent.Helper`
- `tests/HostAgent.Core.Tests`
- `tests/HostAgent.Infrastructure.Tests`

See [development-and-testing.md](docs/development-and-testing.md) for the pinned SDK and verification commands.

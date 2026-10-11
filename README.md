# Host Agent V1

Windows-first local host monitoring and incident-assistance service built with C# and .NET 10.

## Project status

Task 3 implementation and source security review pass in the approved DEV VM on 2026-10-11: Release build has 0 warnings/errors, full suite 108/108, final focused security tests 35/35. The typed helper supports independently allowlisted service start/stop/restart and process termination; defaults remain read-only/shadow with no enabled registrations. See [Task 3 verification](docs/verification/task-3.md), [command ledger](docs/verification/task-3-runs.md), and [security review/limits](docs/security/task-3-review.md). Host synchronization remains blocked; no deployment or installed-service verification is claimed. Task 4 has not started.

- [Specification](docs/specs/host-agent-v1.md)
- [Development and testing](docs/development-and-testing.md)
- [Implementation plan](docs/plans/2026-10-06-host-agent-v1.md)
- [Task 1 verification](docs/verification/task-1.md)
- [Task 2 verification](docs/verification/task-2.md)
- [Task 3 verification](docs/verification/task-3.md)

Builds and tests must run in the approved Windows DEV/test environment described in the development guide. Keep production Code Integrity, VBS, Memory Integrity, and other security policies unchanged. Task 3 was separately authorized after Task 2 gates passed. Do not start Task 4 without a new instruction.

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

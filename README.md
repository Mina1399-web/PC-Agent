# Host Agent V1

Windows-first local host monitoring and incident-assistance service built with C# and .NET 10.

## Project status

Task 2 implementation and verification passed in the approved DEV VM on 2026-10-11: Release build has 0 warnings/errors and the full suite passes 73/73 tests. Host synchronization is blocked because no safe host project path is available from the VM. No deployment is claimed; Task 3 has not started. See the [fresh VM verification record](docs/verification/task-2-dev-vm.md) and [command ledger](docs/verification/task-2-dev-vm-runs.md).

- [Specification](docs/specs/host-agent-v1.md)
- [Development and testing](docs/development-and-testing.md)
- [Implementation plan](docs/plans/2026-10-06-host-agent-v1.md)
- [Task 1 verification](docs/verification/task-1.md)
- [Task 2 verification](docs/verification/task-2.md)

Builds and tests must run in the approved Windows DEV/test environment described in the development guide. Keep production Code Integrity, VBS, Memory Integrity, and other security policies unchanged. Do not start Task 3 until every Task 2 verification gate passes.

## Continue in Codex on the DEV VM

When opening this repository in Codex inside the approved VM, read [AGENTS.md](AGENTS.md). The detailed, ready-to-use continuation prompt is [docs/prompts/agentpc-task-2.md](docs/prompts/agentpc-task-2.md).

## Solution

The solution separates core behavior, Windows infrastructure, a non-privileged service, and the helper service:

- `src/HostAgent.Core`
- `src/HostAgent.Infrastructure`
- `src/HostAgent.Service`
- `src/HostAgent.Helper`
- `tests/HostAgent.Core.Tests`
- `tests/HostAgent.Infrastructure.Tests`

See [development-and-testing.md](docs/development-and-testing.md) for the pinned SDK and verification commands.

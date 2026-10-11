# Host Agent V1

Windows-first local host monitoring and incident-assistance service built with C# and .NET 10.

## Project status

Task 2 is **incomplete**. Its current verification record documents the last completed checks and the outstanding work. This repository snapshot does not claim fresh build or test results from the publication date.

- [Specification](docs/specs/host-agent-v1.md)
- [Development and testing](docs/development-and-testing.md)
- [Implementation plan](docs/plans/2026-10-06-host-agent-v1.md)
- [Task 1 verification](docs/verification/task-1.md)
- [Task 2 verification](docs/verification/task-2.md)

Builds and tests must run in the approved Windows DEV/test environment described in the development guide. Keep production Code Integrity, VBS, Memory Integrity, and other security policies unchanged. Do not start Task 3 until every Task 2 verification gate passes.

## Solution

The solution separates core behavior, Windows infrastructure, a non-privileged service, and the helper service:

- `src/HostAgent.Core`
- `src/HostAgent.Infrastructure`
- `src/HostAgent.Service`
- `src/HostAgent.Helper`
- `tests/HostAgent.Core.Tests`
- `tests/HostAgent.Infrastructure.Tests`

See [development-and-testing.md](docs/development-and-testing.md) for the pinned SDK and verification commands.

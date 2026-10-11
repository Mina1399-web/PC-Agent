# Instructions for coding agents

This repository contains Host Agent V1, a Windows-first C#/.NET 10 project.

## Source of truth

Before changing code, inspect the current working tree and read:

- `docs/specs/host-agent-v1.md`
- `docs/development-and-testing.md`
- `docs/plans/2026-10-06-host-agent-v1.md`
- `docs/verification/task-2.md`

Treat current code and fresh evidence as authoritative over historical notes. Task 2 and separately authorized Task 3 implementation/verification passed in the approved DEV VM; host synchronization remains blocked. Read [fresh Task 3 evidence](docs/verification/task-3.md) and [security review](docs/security/task-3-review.md). The [Task 2 VM continuation prompt](docs/prompts/agentpc-task-2.md) is historical. Task 4 requires a separate explicit user instruction.

## Required boundaries

- Verify that the current execution environment is the approved Windows DEV/test VM before running any build or test. A connected device label, remote-control status, or account name alone does not prove that commands run in the VM. If the environment cannot be verified, stop before builds/tests and report the blocker.
- Run all builds and tests in the VM working copy. Never use the production host as a substitute.
- Do not disable, bypass, weaken, replace, or change production Code Integrity, Smart App Control, VBS, Memory Integrity, Hyper-V, or other security policies.
- Preserve the `clean-win-base` snapshot. Never overwrite or alter it.
- Task 3 is explicitly authorized by the user on 2026-10-11; keep its implementation and verification in the approved VM. Do not begin Task 4. Do not transfer changes to the host without a safe available path and preservation of its current work.
- Update verification records only with test runs actually performed in the current environment. Keep historical results clearly identified as historical.
- Do not commit credentials, chat attachments, VM configuration, SDK caches, or build outputs.

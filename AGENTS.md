# Instructions for coding agents

This repository contains Host Agent V1, a Windows-first C#/.NET 10 project.

Subsequent user authorization on 2026-10-11 permits public open-source
publication and preparation for SignPath Foundation. The user explicitly
allows GitHub-hosted builds/tests for this signing path as an exception to
the ordinary DEV-VM-only rule below. This does not permit production-host
builds/tests, policy changes, trust exceptions, signing approval bypass,
purchases, or publishing secrets/environment artifacts. See
docs/plans/2026-10-11-signpath-preparation.md and
docs/operations/signpath-preparation.md. SignPath enrollment/signing and
cloud integration are not yet complete. Preserve existing VM build guards
until a separately tested hosted path is implemented.

On 2026-10-11 the user explicitly authorized completing the V1 product, including installation, safe local administration and release packaging. This supersedes historical Task-4-only restrictions below. Follow docs/plans/2026-10-11-v1-product-completion.md; all existing VM/host/security boundaries remain in force. No dashboard/public API/new helper mutation capability or unattended approval.

## Source of truth

Before changing code, inspect the current working tree and read:

- `docs/specs/host-agent-v1.md`
- `docs/development-and-testing.md`
- `docs/plans/2026-10-06-host-agent-v1.md`
- `docs/verification/task-2.md`

Treat current code and fresh evidence as authoritative over historical notes. Tasks 2–4 implementation/verification passed in the approved DEV VM; host synchronization remains blocked. Read [Task 4 evidence](docs/verification/task-4.md) and [security review](docs/security/task-4-review.md), with the historical Task 2/3 records. The Task 2 VM continuation prompt and Task-4-only authorization limit are historical. Current V1 product completion is explicitly authorized as stated above.

## Required boundaries

- Verify that the current execution environment is the approved Windows DEV/test VM before running any build or test. A connected device label, remote-control status, or account name alone does not prove that commands run in the VM. If the environment cannot be verified, stop before builds/tests and report the blocker.
- Run all builds and tests in the VM working copy. Never use the production host as a substitute.
- Do not disable, bypass, weaken, replace, or change production Code Integrity, Smart App Control, VBS, Memory Integrity, Hyper-V, or other security policies.
- Preserve the `clean-win-base` snapshot. Never overwrite or alter it.
- Task 4 is explicitly authorized by the user on 2026-10-11; keep implementation/verification in the approved VM. No dashboard or other out-of-scope feature. Do not transfer unless an existing verified safe path is available and the host is clean at the exact declared source base.
- Update verification records only with test runs actually performed in the current environment. Keep historical results clearly identified as historical.
- Do not commit credentials, chat attachments, VM configuration, SDK caches, or build outputs.

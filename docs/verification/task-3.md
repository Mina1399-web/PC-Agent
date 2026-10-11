# Task 3 Verification Record

Date: 2026-10-11, Europe/Prague. Task 3 was separately and explicitly authorized after Task 2 VM verification. Task 4 is not authorized and was not started.

## Environment, provenance and rollback

Before changes, fresh read-only commands confirmed hostname `AGENTPC`, account `AgentPC\AgentPCdevtest`, working directory `C:\Users\AgentPCdevtest\Documents\ChatGPT\DevTest VM PC\HostAgent-DEV`, manufacturer `innotek GmbH`, model `VirtualBox`, VBoxService/VBoxGuest both Running and Guest Additions `7.2.20r175154`. This is the same user-confirmed DEV VM used for Task 2. Builds/tests ran only here. SDK remains the account-local .NET `10.0.100` under LocalAppData/HostAgent-DEV-tools/dotnet. No global SDK or security-policy change was required.

Initial Git tree was clean at Task 2 final commit `8ced5bee26bf8daba3fb6d509cf816122e906c3a`. Local tag `task-2-verified-vm` preserves that point; work continued on `codex/task-3-dev`. Task 3 production/tests commit is `17bd236a9b75c1937d206b7f4986350fda20211d`. Source provenance remains the exact verified import described in [Task 2 evidence](task-2-dev-vm.md); correspondence with newer inaccessible host uncommitted work cannot be asserted.

No snapshot, VM configuration, production host, `C:\PC Agent`, production security setting, signing strategy, account or installed service was changed. Temporary test databases/files/pipes exist only in the VM; build outputs and raw logs stay outside source delivery.

## Implemented scope

- Fail-closed Core policy, fresh exact approval binding and no autonomous mode.
- Strict typed version-1 16-KiB Named Pipe framing/parsing, ACL and token authentication, pinned helper owner and bounded client/server waits.
- Independent helper-local service/process allowlist, typed Windows handlers, process identity checks and cancellation between restart steps.
- Separate helper-owned SQLite journal schema 1, append-only linked audit events, exact request digest, one-time approvals/claims, durable guard/replay state and uncertain-attempt blocking.
- SYSTEM-only executable bootstrap with protected local configuration/data and a default empty allowlist/read-only/shadow policy. Actual Windows Service hosting/installation remains Task 4.

Supported capabilities are StartService, StopService, RestartService and TerminateProcess. DNS flush, DHCP renewal and Windows restart are not implemented/enabled. Approval provisioning is a trusted local API; a live approval UI/CLI was not added. See the [security review](../security/task-3-review.md) for boundaries and explicit limits.

## Fresh executions and test-first evidence

The [command ledger](task-3-runs.md) records every actual dotnet command, UTC start, exit code and observed outcome. Filenames containing `green` describe intended runs; the ledger preserves their actual failures as well.

- Initial missing policy/protocol tests failed in runs 1/2; defaults and invalid requests passed in 3/4.
- Exact approval and valid typed parsing failed in 5/6 and passed in 7/8.
- Missing boundary/framing contracts failed in 9. Behavioral guard/journal tests then failed in 12 before their implementation and passed in 14.
- Real pipe security tests failed in 15 and passed 5/5 in 16; later malformed/partial real-frame coverage passed with the suite.
- Typed service/process handler behavior failed in 17 and passed 5/5 in 18. Missing native adapter/configuration failed in 19 and passed in 20. The additional asynchronous-timeout test already passed in run 19; it is additional coverage, not an observed red-green behavior claim.
- Protected helper file checks failed in 21 and passed in 22.
- Self-review regression tests failed in 23, 25, 26 and 28 and passed after fixes in 24, 27 and 29. All findings/resolutions are preserved in the security review.
- Run 10's failure was a fixture exception-hierarchy assumption, run 11 was an xUnit analyzer fixture error, and run 13 had two temporary SQLite cleanup failures from test-only pooling. These are explicitly **not** counted as behavioral red evidence. Fixtures were corrected without weakening behavior assertions.

| Gate | Executed result |
| --- | --- |
| Core PolicyEngine focused run 7 | 4 passed, exit 0 |
| Guard focused run 29 | 11 passed, exit 0 |
| Release build run 30 | exit 0, 0 warnings, 0 errors |
| Complete Release suite run 31 | 42 Core + 66 Infrastructure = **108 passed**, 0 failed, 0 skipped, exit 0 |
| Final focused security run 32 | 4 Core + 31 Infrastructure = **35 passed**, 0 failed, 0 skipped, exit 0 |
| Independent source security review | Approved, no unresolved actionable findings; read-only review of base..17bd236a |

Full verification commands actually executed from the VM checkout:

```powershell
dotnet build HostAgent.sln --configuration Release
dotnet test HostAgent.sln --configuration Release
```

The account-local SDK was put on PATH only for each execution. No installed service or real native mutation was invoked by the suite. Actual Windows pipe token/owner/ACL-shape tests ran under the DEV account; native operation behavior uses fake OS adapters, with only real self/system-PID rejection checked without side effects.

Final source/test/docs/config heuristic secret-like assignment/key scan inspected 110 files and found zero matches; generated bin/obj and environment artifacts were excluded. This is not an exhaustive claim for every possible secret format. `git diff --check` passed; the final source review found no generic execution or sensitive exception/payload logging surface. Documentation-only changes followed the tested/reviewed source commit; no production code changed after run 31/32.

## Transfer and remaining limits

Fresh `VBoxControl sharedfolder list` again reported zero mappings. No safe accessible host project path exists; nothing was copied to host, pushed or deployed. Source-only Task 3 and combined Task 2+3 patches and hash manifests are prepared outside the checkout. They exclude SDK/runtime, bin/obj, caches, raw logs, credentials and VM configuration. Any later host import must first inspect/preserve the live host Git tree and check applicability; the VM cannot verify that state.

No installed-service startup/identity/recovery, real privileged operation, distinct-account production deployment, live approval UI, notification, signing or soak run is claimed. Target-limit denials persist human-review codes; monitoring incident escalation/notification wiring is deferred to Task 4. Task 3 implementation/tests/review pass in the stated capability scope; host synchronization remains blocked. Task 4 remains unstarted.

# DEV completion and Confirmation preparation — 2026-10-27

## Scope and preserved state

The owner authorized completing the remaining application work, excluding
signing, and moving the approved DEV installation from Shadow/read-only to
the supported writable **Confirmation** mode. This is not authorization for
unattended approval, arbitrary targets, or production-host changes. New
installations retain the safe default; each typed operation still needs its
own registered target and exact, fresh approval.

Environment: AGENTPC, AgentPC\\AgentPCdevtest, VirtualBox UUID
46543236-8C5E-49DA-953C-DB17F9B1E402, VBoxService Running. The working token
was not elevated. Initial source was codex/desktop-localization at
4ee4dc176ccee29244ef2ea02eb3052261481146. The existing three modified/untracked
paths were copied to an external evidence return point before edits. The
prior final-acceptance document's uncommitted change remains preserved.

Evidence and durable progress are outside the repository at
`%LOCALAPPDATA%\HostAgent-DEV-evidence\completion-20261027`. Detailed child
stdout/stderr and watchdog receipts for the runs below are in the existing
`desktop-localization-20261027` evidence directory. Tools used actual SDK
10.0.401 and process-local DOTNET_ROOT/DOTNET_ROOT_X64/PATH. The authorized
build/test guards require more than 768 MiB initially, warn below 768 MiB,
abort only their own child tree at/below 512 MiB, and require 10 GiB disk.

## Confirmed recovery defects and changes

1. The earlier offline-copy diagnostic attempted
   `Start-Service -NoWait`. That parameter is unsupported by Start-Service.
   Both failed restorations in the owner-supplied result are consistent with
   this exact binding failure; subsequent native SCM starts restored them.
   The unsupported argument has been removed.
2. The diagnostic formerly checked service identity only after starting it.
   It now checks the fixed name/account/path/start mode and pinned executable
   hash immediately before restoration, including after a pending stop. A
   changed identity is not started. Final identity and Running checks remain.
3. The diagnostic formerly restored based on a preflight Running snapshot
   even if it had not requested a stop. Per-service StopRequested is now set
   immediately before that run's SCM stop request; only those services are
   eligible for restoration. A partial stop failure still receives recovery.
4. The product CLI's service lifecycle aborted startup after the first helper
   open/start/wait failure, leaving the independent monitor unattempted. It
   now tries both fixed product services and then reports an aggregate of
   failures. Stop remains non-cascading and fail-fast, and cancellation is
   still honored. This fixes CLI configuration/recovery behavior without
   broadening any operation or approval.

The PowerShell regression executes the actual diagnostic's finally AST with
only privileged SCM/file-hash boundaries replaced. It covers original service
states, a failed start, an accepted-but-incomplete start, account/path/hash
drift, and a preflight that did not request any stop. It does not execute the
whole privileged diagnostic or prove native SCM recovery. The CLI tests use
the real lifecycle with fake service handles and assert the second service
is attempted and every failure is retained.

## Recorded red/green and current checks

The focused command was:

```powershell
dotnet test tests/HostAgent.Infrastructure.Tests/HostAgent.Infrastructure.Tests.csproj -c Release --no-restore --filter 'FullyQualifiedName~InstalledDatabaseRecoveryTests|FullyQualifiedName~ProductServiceLifecycleTests' -m:1 -p:UseSharedCompilation=false -nr:false -p:BuildProjectReferences=false
```

Dependencies were built before this command. After changing the CLI source,
its Release project was rebuilt before rerunning the same focused command.

| Check | Actual result |
| --- | --- |
| Initial attempt | Watchdog stopped the child at 511.1 MiB before tests; not behavioral RED |
| First focused compile | Test-only ambiguous TimeoutException reference; corrected, not behavioral RED |
| Focused RED `daa1bb067fab46279d1c6ee6d40d7b4c` | 7 failed / 3 passed, exit 1; rejected restart parameters and missing monitor restart attempt |
| Parameter-only fix `fc05c2b10f2a4d66b11312b0e6cbdd24` | 5 failed / 5 passed, exit 1; account drift started before checking; no-stop preflight unexpectedly restored services; CLI recovery failures retained as RED |
| Focused GREEN `230ac1f5135a42cc8d01e245cd59789e` | 10 passed / 0 failed/skipped, exit 0; min RAM 711.0 MiB |
| Release solution build `f100cce5035d440aaa7b65d431639da8` | Exit 0, zero warnings/errors; min RAM 529.8 MiB; no intervention |
| Expanded recovery/configuration/approval/process-metric focused run `cb8c9e9a26674834be778bdb5ed1e7f5` | 64 passed / 0 failed/skipped, exit 0; min RAM 547.1 MiB; no intervention |
| Complete three-assembly Release suite `214c58fe1eaf477bb485f52e9de574dd` | **681 passed / 0 failed / 0 skipped**, exit 0: Core62 + Infrastructure581 + Desktop38;362.4s, min RAM580.6MiB, min C:14.592GiB; no intervention |

The expanded run includes additional guard coverage added after the first
green run; these extra cases are coverage, not separately observed RED cycles.
Independent read-only review of the recovery diff found no remaining blocking
P1/P2 finding. The review explicitly noted that these tests are not full native
SCM acceptance. The complete current suite passed, including the fixture
builder previously blocked by its old15GiB floor. Later operator results
must still be recorded separately when they actually finish.

## Current installed baseline and metric limits

A fresh read-only query found installed version 1.2.8: HostAgent Running,
Auto, NT SERVICE\HostAgent, PID10448; Helper Running, Auto, LocalSystem,
PID10776, both at the expected Program Files paths. The executable hashes
were A4C8FEAD…FFB8C and 96E804D8…B8A99D; the corresponding service assembly
hashes were A7C67CA2…B3B04B and 38F5337D…E94B. The complete values are in the
external `installed-baseline.json`. No service control was performed. A fresh
authenticated installed View overview succeeded on attempt1 in103ms
(`368e2274a952486aa57ebc5229c0fc92`, exit0, min965.8MiB). This bounded probe
printed transport status only, without the snapshot payload; it establishes
current View availability, not the completeness of its metric values.

The previously documented main/notifications database integrity results and
subsequent service recovery remain in
[the installed diagnostic record](installed-readonly-diagnostics-2026-10-26.md).
This source run has not reopened those databases. The absent, lazily created
helper journal stays NOTRUN; enabling a mode does not create or validate it.

The access-denied reasons identify individual process CPU-time reads, and
best-effort executable-path reads. They do not establish failed system CPU or
physical-memory APIs. Independent support source review confirmed that the
runtime's CPU-time read already uses QUERY_LIMITED_INFORMATION; replacing it
with the same native API would not reduce access rights. The exact process
and protection causing the installed denial are not established. These
limitations remain honestly partial/degraded; no debug privilege, broader
service identity, ACL change, or warning suppression was introduced. A
potential QueryFullProcessImageName fallback is an unproven improvement,
not a claimed fix for the installed warning.

## Remaining live boundaries

- New localized package/installed-language acceptance is not established by
  the source WPF fixtures. The installed preview is still1.2.8.
- Writable Confirmation is authorized by the owner but has not been applied
  in this non-elevated session. Its acceptance must verify both protected
  configurations, unchanged target registrations/kill switches/network
  choices, installed hashes, fresh mode readback, and both Running services.
- Mode acceptance does not prove a real installed helper action. Exact
  allowlisted-target approval, audit/journal and recovery remain a separate
  live result; historical private-GUID helper PASS has its own scope.
- Historical reboot, power-off, storage, collector and30-minute soak gates
  retain their scoped PASS results; see
  [the final resilience assessment](resilience-final-2026-10-18.md). They were
  not repeated here and do not imply days-long or final installed endurance.
- External notification delivery remains disabled/unverified. Signing is
  excluded from this increment. No production-host deployment is performed.

## Source publication and package attempt

Recovery source/tests and these dated diagnostic records were committed as
`6e02d3e5f92135fa43ca730efef32ea4c7274c33`. The GitHub specialist pushed only
`codex/desktop-localization` by a normal new-branch push, then verified its
remote SHA matches that exact commit. The public preview tag, release assets,
default branch and main were not changed. This publishes source; it does not
publish a new installer or claim installed acceptance.

A separate clean detached checkout at that commit was created under
`HostAgent-DEV-builds/1.2.9-6e02d3e-c630070d`; the original dirty working tree
was preserved. The app's managed worktree tool could not resolve the nested
repository's local ref, so native `git worktree add --detach` was used with
the explicit repository and a new absent target. No existing checkout was
reset or overwritten.

`tools/Build-Release.ps1 -DevVm -Version1.2.9` was attempted from that clean
source with the pinned local SDK/WiX. The independent parent watchdog stopped
its own child tree at509.7MiB available RAM (`ae87aa3a0afa4c6082f89e6fd7b337a5`,
elapsed40.6s, min C:14.389GiB, child exit-1, outer command exit1). The partial
output `HostAgent-DEV-releases/HostAgent-1.2.9-dev-6e02d3e-r1` is preserved.
It has no completed/validated release manifest and **must not be installed**.
This is a resource-blocked package attempt, not a failed application test or
a completed release. No MSI, service/configuration change or mode transition
was performed. A later attempt must use a new output path and sufficient
memory headroom; the512MiB emergency limit remains unchanged.

## Read-only mode plan and bounded publication follow-up

The support thread prepared the new operator plan, now named
`scripts/diagnostics/Read-InstalledDevConfirmationPlan.ps1` so its purpose is
unambiguous. It validates a pinned standard `manifest.psd1`, the candidate's
payloads, installed EXE/DLL sizes and hashes, VM/account/admin identity,
service identities/state, protected config JSON/ACL and the mode/marker/
allowlist/outbound gates. The config checks reject duplicate fields and
wrong numeric types; ordinary PowerShell7 Int64 JSON integers are accepted
within the product's supported ranges. SYSTEM as both helper service SID
and owner is correctly deduplicated in the ACL check. It does not open DBs,
read secret files, execute CLI, start/stop services or write protected data.

Its default result is a plan. `-Execute` deliberately returns a manual-step
result/exit2 rather than automating a configuration child whose interruption
could strand services. The exact interactive existing CLI command is emitted
only after a valid plan. This does not add another approval mechanism or
prove live mode acceptance. In particular, the source plan cannot itself
establish runtime readback: it leaves ModeOnlyAcceptance, ActionAcceptance
and JournalIntegrity NOTRUN. Initial resource checks are768/512MiB and10GiB;
this read-only tool is not a fault/pressure runner.

During source integration, a real-fixture regression additionally reproduced
pipeline pollution from an ACL assertion's `$true` output. The wrapper now
suppresses that helper result, preserving the single facts object. RED
`d86e923dd9e840ecb5d76b8c9b56cdd3` failed the exact pipeline assertion; GREEN
`243f2c12564044fe88c4dabe5ce68af5` passed25 assertions. After the read-only
filename change, final standalone PS validation `a519226706f046839d86218da60a9844`
passed25/25, exit0, min805.2MiB/13.882GiB, no intervention. The test exercises
real JSON/ACL parsing only on its private temporary fixtures; it does not
exercise the installed protected configs. The independent final source review
found no blocking P1/P2 in that scoped plan and explicitly retained these
live-acceptance limits.

With the failed partial output preserved, publication was split into the
remaining CLI and Desktop projects in the clean source checkout. CLI warm
publish `5461e8d2742c4fec9ceda4e9dc6d9a28` passed (13.4s,min692.0MiB);
Desktop `a9a3efd192c14889ab39bd815c44e62f` passed (18.9s,min765.1MiB).
Neither is by itself a complete validated MSI release. Reusing the verified
PowerShell7.6.5 execution shell also removes one redundant PowerShell parent;
resource thresholds were not lowered. Final packaging/installation results
must be added only after those separate checks finish.

## Completed candidate and artifact-only acceptance

The clean detached build checkout was advanced to
`842378c5e2caa31b0a40c9502dbf7e990cb899b5`. No C# application or compiled-test
source changed after the 681/681 Release suite above; this commit adds the
read-only operator-plan script/tests and documentation. A second package
attempt (`c64bd7c1ce564dca98aa5b82bba67c38`) was interrupted at the unchanged
512 MiB emergency floor: child exit -1, minimum 484.6 MiB, elapsed 9.9s.
That failed attempt/evidence was preserved and was not installed. Sampling
can observe below the threshold before cancellation completes; the watchdog
is not a guarantee that available RAM never crosses 512 MiB.

After fresh VM verification showed approximately 2.7 GiB available RAM,
`tools/Build-Release.ps1 -DevVm -Version 1.2.9` completed into a new path:

`C:\Users\AgentPCdevtest\Documents\ChatGPT\DevTest VM PC\HostAgent-DEV-releases\HostAgent-1.2.9-dev-842378c-r3`

Receipt `completion-package-1-2-9-r3-dc90c09826c6448c86d01af145186f8d`:
exit 0, 129.5s, minimum RAM 2231.8 MiB, minimum C: 13.024 GiB,
monitorFailure null. No previous candidate was overwritten.

| Artifact | SHA-256 |
| --- | --- |
| manifest.psd1 | E2103A77A181A13D8D07178FA349A2E92ADA40A7460687FBE3C6B80A3466FC21 |
| HostAgent.msi | 2F7B2F4E87AE537F0E4BBBD2DBB64121311D57F876591565122AAF4A66D95E27 |
| sibling release ZIP | C50C67364743B6A9A3AD0D5F48880378524C6EC54E117D7EC2431FA3A3ED9ECA |

`completion-20261027/candidate-validation.json` records PASS for all 1239
manifest entries and all 1240 ZIP entries (including the manifest), comparing
lengths and SHA-256 of the actual archive bytes. `Test-Release.ps1 -DevVm`,
installer table inspection and `Install-HostAgent.ps1 -PlanOnly -DevVm` passed;
PlanOnly reported changesPerformed=false. The MSI remains unsigned DEV-only.
The verifier initially rejected the documented enclosing archive directory;
after correcting that verifier assumption it required the exact expected
root prefix and every normalized payload byte. The archive was not changed.
Final artifact check receipt `368d0e90bdb24341bc4fd8a86e1ee7cc`: exit 0,
25s, minimum 2443 MiB, no intervention. This is artifact validation, not MSI
execution or installed acceptance.

The packaged self-contained `Cli/HostAgent.Cli.exe --help` also ran successfully:
receipt `cf1f34b84bc142b69337e89c2cfc2ac5`, exit 0, 3.2s, minimum 2596.6 MiB.
No privileged CLI command, MSI or service operation was performed by this run.

## Fresh installed state and operator preparation

A later SCM read showed **HostAgent Stopped, PID 0**, Auto,
`NT SERVICE\HostAgent`, expected fixed image path and exit codes 0.
**HostAgent.Helper remained Running, PID 10776**, Auto, LocalSystem, expected
path and exit codes 0. This work did not issue a stop/start. Bounded SCM-event
metadata did not establish a fresh cause; the cause of the stopped monitor
is **unresolved**, not attributed to user action or a product crash.

Host Agent Support independently compared the installed 1.2.8 critical
EXE/DLL/deps/runtimeconfig payload against the original pinned manifest
`5DCF79DC3284B4937EBA64D4F50A7831A1FFE4F0891AE4D4B190A500B136B25A`:
1139/1139 lengths and hashes matched, zero mismatched critical paths.
This establishes the checked critical-component baseline, not a full
installed-file inventory. It does not prove service health or DB integrity.

The external, version-pinned operator handoff is:

`C:\Users\AgentPCdevtest\Documents\ChatGPT\DevTest VM PC\HostAgent-DEV-releases\HostAgent-1.2.9-DEV-handoff`

It contains `Complete-HostAgent-1.2.9-DEV.ps1`, the exact read-only loader
copy, `README.md`, and `handoff-manifest.json`. Handoff manifest SHA-256:
`ADC78440559389AE8C44AFFD0E3DD16300A5FBB820847A029B06C236B1DC9E91`;
its three entries were checked against lengths and hashes. Wrapper SHA-256:
`02F55B2ED4C14715AA39A4BE53AEC3C528FB29BD285645E6090F131F2E8452A3`.
Loader SHA-256 remains
`4BB381E09F7782AF0F3BE6024008D0AC0E1DFB8F41C0A58257BCE0BBF82D6097`.
Both scripts parsed with zero errors.

The reviewed operator path validates the VM, interactive elevated account,
package pins, old critical payload and protected configuration gates before
requesting recovery of either fixed service. Only a Stopped service is
started, with immediate metadata and EXE/DLL hash guards, helper first; no
retry or arbitrary target is allowed. Running services are not restarted by
this baseline step. MSI configuration hashes are compared even at restart
code 3010; that path explicitly leaves mode/post-reboot acceptance NOTRUN.
Only after installed checks does the existing CLI request the operator's
literal APPLY for `configure Confirmation off off`. Readback must prove the
monitor and helper flags, current approver and actual runtime status. Target
allowlists, selected safety flags and outbound-off settings must be preserved.
The CLI deliberately adds the approving operator, so the wrapper does not
claim byte-for-byte configuration preservation across that mode change.

A newly reproduced **operator harness defect** was fixed before any native
execution: dot-sourcing the loader bound its optional Execute=false into the
wrapper scope, silently losing the operator's Execute switch. The wrapper
now captures a uniquely named original boolean before loading shared code.
The regression evaluates the actual wrapper prologue, dot-source statement
and branch conditions with default and Execute requests; it performs no
privileged operation. Behavioral RED `e5eb5032ad864e60ad69ae309040e34c`
failed `operator_execute_switch_lost_after_loader`; GREEN
`bf9d51ea2dd74e41afb347a89714e287` passed both cases, exit 0, minimum
2322 MiB. An earlier fixture attempt failed its own absent PSScriptRoot
setup and is not counted as behavioral RED.

The fixed-service fixture additionally passed seven cases: restoring only
the stopped monitor, no repeat for Running, metadata drift rejection, image
drift rejection, helper-first order when both are stopped, first-start failure
without retry, and rejection of ambiguous pending state. Receipt
`completion-operator-recovery-final-70f1a5a6195848aea3940a20b3af60ba`:
exit 0, minimum 2350.9 MiB, no native service operations. These fakes do not
prove native SCM recovery or native start timeout behavior. The wrapper's
45s Running wait starts after synchronous Start-Service returns; it is not
a deadline for that underlying call. MSI/configuration children are not
force-killed by a parent watchdog during service recovery.

Both existing support threads independently reviewed the final scope and
found no further blocking issue after the switch fix, while retaining the
critical-inventory and native-timeout limitations. Final default invocation
(no Execute) passed under the memory watchdog:
`completion-operator-final-readonly-f2059aa6e4584a98920e31ec66e8db9f`,
exit 0, 31.4s, minimum RAM 1897.2 MiB, minimum disk 12.601 GiB,
monitorFailure null. It returned PACKAGE_VERIFIED_ADMIN_EXECUTION_PENDING,
configurationAttempted=false, no service-restoration attempt and no result
file write. It still observed the stopped monitor and running helper.

### Explicit remaining gates

- Native installation/recovery of 1.2.9 and mode transition: **NOTRUN**;
  current agent token is not elevated. The owner has a single prepared
  interactive PowerShell7 command; no further authorization is requested.
- Installed 1.2.9 live dashboard/language switching and persistence:
  **NOTRUN**. Existing source localization tests are not installed acceptance.
- Live registered-target action/approval/journal acceptance: **NOTRUN**.
  The mode-only step neither registers a target nor executes remediation.
- Exact inaccessible processes/protection mechanism: **unresolved**;
  bounded access-denied/path-unavailable reasons stay visible. No permissions
  were broadened and partial telemetry is not relabeled healthy.
- No new installed database integrity run occurred here. Prior owner-supplied
  host-agent/notifications quick_check results and absent lazy helper journal
  remain historical, not fresh 1.2.9 proof. The historical version-pinned
  offline script must not be run unchanged against a new installed payload.
- No production host, security policy, released tag/assets, signing workflow
  or host-project synchronization was changed. No universal readiness claim
  follows from the source suite or operator-preparation PASS results.

# PR #5 Windows administration validation (2026-11-09)

## Scope

Validated PR #5 branch `codex/windows-administration` at `c97b17e61bc89bdb63c30490ccab68761d969b32` in the approved Windows 11 DEV VM. The source archive was downloaded from that exact public commit and verified with SHA-256 `675EE98EEFAF2344A550362F99735930712A3BBF277CEA4CD2E34122B40A9CC4`. The VM had .NET SDK 10.0.401. No product or security configuration was changed during validation.

## Automated tests

Isolated Release test runs passed for every solution test project:

- Core: 64/64 passed.
- Infrastructure: 572/572 passed.
- Desktop: 53/53 passed.
- Total across isolated project runs: 689 passed.

The complete solution did not produce one clean run. An initial Infrastructure fixture parse was affected by ANSI color bytes; the focused fixture passed with process-scoped `NO_COLOR=1`, `TERM=dumb`, and `CI=true`, and the full Infrastructure project then passed 572/572. In a later full-solution run, Core had one 10-second thread-pool scheduling timeout (`Collector_timeout_cancels_the_collector_and_prevents_a_tight_retry`), while Infrastructure passed 572/572 and Desktop passed 53/53. The isolated Core project rerun passed 64/64. These outcomes are reported separately; the isolated passes do not erase the full-solution transient failures.

## GitHub and security review

At the time of validation, PR #5's Windows review check for this commit passed (Actions run `42530567998`). The bounded diff security review covered all nine changed source files and direct IPC/operation controls; it recorded zero reportable findings. It is not an independent human review or proof that no vulnerability exists.

## Runtime and release limits

The VM had Host Agent 1.2.11 installed and both `HostAgent` and `HostAgent.Helper` services running when checked. This validation built and tested PR source; it did not replace the installed 1.2.11 package with a PR #5 build. The VirtualBox display was black during the attempted interactive check, so live dashboard rendering and click-through actions were not verified. No production deployment, live Pushover/Healthchecks delivery, physical SMART test, or SignPath enrollment/signing was performed. Do not treat PR CI or these tests as a signed public release.

## Read-only installation and CLI follow-up (2026-11-09)

The approved DEV VM was checked again without changing product configuration or services. Windows reports installed version `1.2.11`; both services were Running. `tools/Test-Release.ps1 -DevVm` accepted the separate `HostAgent-1.2.11-GuidedActions-c97b17e-DEV` candidate: 1,246 manifest entries, source `c97b17e61bc89bdb63c30490ccab68761d969b32`. All 1,242 files under the installed `C:\Program Files\HostAgent` matched the candidate by relative path and SHA-256; all 18 first-party `HostAgent.*.exe`/`.dll` files matched and reported ProductVersion `1.2.11`. This establishes the installed payload's match to that local, unsigned DEV candidate. It does not make the candidate a GitHub release or a signed production build.

An additional focused CLI regression test was run against the same source commit in the approved VM under the unelevated `vmdev` token. It first failed with the reproduced bare output `administrator_required`. After changing the non-admin response to retain that stable code and explain the dashboard/elevated-terminal paths, the identical test passed 1/1. No service, configuration or installed files were changed by this test. The full Infrastructure suite for this new change still awaits the next GitHub-hosted workflow run.

An independent read-only UI tester later reported that the installed 1.2.11 dashboard rendered. The tester also found that the opening view did not prioritize the strongest current problem and that CLI/maintenance entry points need clearer onboarding. This is a limited observation, not full click-through acceptance; the in-progress dashboard redesign has not yet passed its live acceptance criteria. The initial black-display observation above is retained as historical context.

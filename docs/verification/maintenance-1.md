# Bounded user maintenance: fresh DEV verification

Date: 2026-10-13, Europe/Prague. This record contains fresh evidence for the
bounded temporary-file maintenance increment. Earlier dashboard and V1
records remain historical and are not re-counted here.

## Environment and preserved state

Commands ran inside `AGENTPC`, account `AgentPC\AgentPCdevtest`, model
`innotek GmbH / VirtualBox`, VM UUID
`46543236-8C5E-49DA-953C-DB17F9B1E402`. `VBoxService` and `VBoxGuest` were
both Running. Repository:
`C:\Users\AgentPCdevtest\Documents\ChatGPT\DevTest VM PC\HostAgent-DEV`.
Branch `codex/local-dashboard`; starting HEAD
`a1b25e86fe0df830df67e6aede5cdbcb3f471ea1`. Existing staged/unstaged work and
the prior untracked audit were preserved. The audit SHA-256 remains
`63268A0FE5D734BA4D5484C87004A2F702F8B031BC93B83E56DB6A6CF46C414D`.

The account-local .NET SDK was `10.0.401`; `DOTNET_ROOT` pointed to
`%LOCALAPPDATA%\HostAgent-DEV-tools\dotnet`. No build ran on the production
host. This increment did not change security policies, VM configuration,
snapshots, service configuration, secrets, or external endpoints.

## Test-first evidence

Evidence is stored outside the source tree at
`%LOCALAPPDATA%\HostAgent-DEV-evidence\maintenance-1`.

- Preview and quarantine boundaries, identity/content checks, root substitution,
  exact one-use approval, Shadow/read-only/kill-switch/stale-state rejection,
  restore conflicts, interrupted-intent recovery, bounded quotas and the
  cross-instance mutation lock passed their focused tests. Earlier red/green
  TRX files are retained in the evidence directory.
- Strict malformed/duplicate journal fields were first observed failing in
  `journal-strict-red.trx`, then passed in `journal-strict-green.trx`.
- A new test with a null required journal field failed with a reproduced
  `NullReferenceException` in `null-journal-red/null-journal-red.trx`. The
  parser now rejects missing or null required strings before validation;
  `null-journal-green/maintenance-focused-final.trx` passed 34/34 related
  infrastructure tests.
- The actual WPF maintenance window was exercised with synthetic fixture files:
  exact preview ID, quarantine, explicit restore ID, blank confirmation
  rejection and Shadow denial. `maintenance-ui-focused-4.trx` passed 2/2.
- Test-first crash-recovery and quota regressions are in the evidence directory.
  `maintenance-journal-red.trx` failed because changed source metadata still
  triggered hashing and a valid leftover restore journal temp caused recovery
  to throw; `maintenance-journal-green.trx` passed all 35 selected maintenance
  tests. `actual-payload-quota-red.trx` demonstrated acceptance of another file
  after payloads on disk exceeded 128 MiB; `actual-payload-quota-green.trx`
  passed all 36 maintenance tests. `approval-warning-red.trx` showed a lost
  journal warning if approval was withdrawn mid-batch; the corrected focused
  solution run passed Infrastructure 48/48 and Desktop 2/2.
- The final focused command was:

  ```powershell
  dotnet test HostAgent.sln -c Release --no-restore -m:1 --filter 'FullyQualifiedName~UserMaintenance|FullyQualifiedName~HealthStabilization|FullyQualifiedName~MaintenanceUiTests' --logger 'trx;LogFileName=focused-final.trx' --results-directory "$env:LOCALAPPDATA\HostAgent-DEV-evidence\maintenance-1\focused-final"
  ```

  After the last code change, Infrastructure passed 48/48 and Desktop passed
  2/2. The Core project had no tests matching that focused filter; its full
  suite ran below.

## Final Release gate

With account-local `DOTNET_ROOT` set, the exact final commands were:

```powershell
dotnet build HostAgent.sln -c Release -m:1
dotnet test tests/HostAgent.Core.Tests/HostAgent.Core.Tests.csproj -c Release --no-build --no-restore -m:1 --logger 'trx;LogFileName=core-release-final.trx' --results-directory "$env:LOCALAPPDATA\HostAgent-DEV-evidence\maintenance-1\final-3\core"
dotnet test tests/HostAgent.Infrastructure.Tests/HostAgent.Infrastructure.Tests.csproj -c Release --no-build --no-restore -m:1 --logger 'trx;LogFileName=infrastructure-release-final.trx' --results-directory "$env:LOCALAPPDATA\HostAgent-DEV-evidence\maintenance-1\final-3\infrastructure"
dotnet test tests/HostAgent.Desktop.Tests/HostAgent.Desktop.Tests.csproj -c Release --no-build --no-restore -m:1 --logger 'trx;LogFileName=desktop-release-final.trx' --results-directory "$env:LOCALAPPDATA\HostAgent-DEV-evidence\maintenance-1\final-3\desktop"
```

The final build exited 0 with **0 warnings and 0 errors**. All three test
commands exited 0: Core **58/58**, Infrastructure **264/264**, Desktop
**14/14**; total **336 passed, 0 failed, 0 skipped**. Separate TRX files are
stored below `final-3` to avoid result-file overwrites.

An independent read-only security review initially requested changes for
candidate metadata hashing, stale journal-temp recovery and actual payload
quota accounting. Test-first fixes addressed each finding. A final review found
no blocking or outstanding confirmed findings; it also identified a low
severity journal-warning count issue, fixed with a failing regression test
before the final build and suite above.

## Scope and limits

All maintenance file tests used generated files below unique current-user
`LocalAppData\Temp\HostAgent-Maintenance-Test-*` or
`HostAgent-Maintenance-UI-*` fixture directories and cleaned only those exact
fixtures. The implementation proposes top-level regular files only, at least
seven days old, at most 16 MiB each, at most 100 files/500 inspected, and a
128 MiB / 256 journal quarantine limit. It moves the entire approved preview;
the current UI does not offer per-row selection. Exact approval expires after
ten minutes and is consumed once. Quarantine does not reclaim space and has
no automatic or permanent deletion. Restoration requires exact explicit
confirmation and never replaces an existing file.

No real user Temp cleanup, real Confirmation-mode service mutation, actual
installed 1.2 candidate, MSI upgrade, long-duration soak, external push or
heartbeat, production-host operation or signed release was exercised in this
increment. Package generation and installed acceptance still require their
own verified steps. These results establish source/build/test readiness for
this increment, not complete live deployment acceptance.

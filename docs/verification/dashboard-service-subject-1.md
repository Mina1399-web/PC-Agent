# Validated service identity on incident cards: fresh DEV verification

Date: 2026-10-13, Europe/Prague. This record covers the service-incident
identity projection and the 1.2.1 DEV package built from it. Earlier records
remain historical and are not re-counted here.

## Environment and preserved state

All builds, tests, packaging, installation and interactive checks described
here ran inside the approved `AgentPC` VirtualBox DEV VM, account
`AgentPC\AgentPCdevtest`, model `innotek GmbH / VirtualBox`, UUID
`46543236-8C5E-49DA-953C-DB17F9B1E402`. `VBoxService` and `VBoxGuest` were
Running. The repository is
`C:\Users\AgentPCdevtest\Documents\ChatGPT\DevTest VM PC\HostAgent-DEV`,
branch `codex/local-dashboard`. Source commit:
`4c5d1a05d2d72987d56e7cc8963b1c49a62b6ded`.

At verification start the working tree contained only the existing untracked
`docs/audits/` directory. Its audit document SHA-256 remained
`63268A0FE5D734BA4D5484C87004A2F702F8B031BC93B83E56DB6A6CF46C414D`; it was
not staged or edited. The production host and its security settings were not
used. No secrets or external notification endpoints were used.

## Change and test-first evidence

The dashboard now exposes the affected service for `service_stopped` and
`service_crash` incidents. Projection accepts only a non-empty identifier up
to 128 characters containing ASCII letters, digits, dot, underscore or
hyphen. Other diagnosis subjects and path-like or otherwise invalid values
are omitted. The identifier appears in the incident table and selected
incident details; no raw diagnostic text was added to this view.

Before implementation,
`DashboardViewTests.Service_incident_exposes_only_a_validated_affected_service_identifier`
failed because the response omitted `AffectedService`. After the implementation
it passed, asserting the service identity in overview and detail as well as
redaction of a private path and an unrelated private subject. Focused dashboard
tests passed 19/19; the final targeted regression rerun passed 1/1.

## Release and package results

In the VM, the exact full-solution Release build command was:

```powershell
dotnet build HostAgent.sln --configuration Release --no-restore -m:1
```

It exited 0 with 0 warnings and 0 errors. The full Release suite command was:

```powershell
dotnet test HostAgent.sln --configuration Release --no-build --no-restore -m:1 --logger 'console;verbosity=normal'
```

It exited 0: Core 58/58, Infrastructure 265/265, Desktop 14/14; total
337 passed, 0 failed. These results are from the source commit above.

A clean detached worktree at that commit produced the unsigned DEV-only
package with `tools\Build-Release.ps1 -DevVm -Version 1.2.1`. Package
validation by `tools\Test-Release.ps1 -DevVm` and `tools\Test-Installer.ps1`
passed; the release manifest validated 1,195 files. MSI SHA-256:
`98B1D11EA5A633FA6B59343AD0F05D656910DCC20F6AB0090ED21EEF7F5F5AFC`.
The ZIP SHA-256 recorded during packaging was
`DF2A4A5BB16B96AB9F340C09D5EF7982064BC207E733999D314492775A8ACDC8`.

## Installed VM acceptance and live dashboard

The installed DEV application reports version 1.2.1 / file version 1.2.1.0.
The current VM check found both services Running and Automatic:
`HostAgent` as `NT SERVICE\HostAgent` and `HostAgent.Helper` as LocalSystem.
The accepted upgrade record is
`%LOCALAPPDATA%\HostAgent-DEV-evidence\dashboard-completion-2026-10-13\installed-result-1.2.1-final.json`.
It reports completion, 1,191 installed files verified, preserved
configuration, preserved database records and checkpoints, and no source DB
changes. The pre-upgrade configuration SHA-256 stayed
`0D0DCCF65BDCCDED91AD1C1BF7B4E7AB5F738FF2E04D9D7A80E91BC1F2670F`.

The desktop was opened unelevated and observed after its initial data load.
The incident list visibly included a `Služba` column and current
`service_stopped` items. Selecting the `AppXSvc` incident displayed
`Dotčená služba: AppXSvc`, its 88% confidence estimate and bounded evidence.
The recommendations tab rendered its explicit message that current evidence
does not call for a new recommendation and that missing measurements do not
mean a healthy state. The component tab showed Event Log and network as
healthy, while collector/CPU-memory/disk components remained partial or
unavailable with bounded reason codes. The maintenance tab explicitly showed
Shadow mode and blocked interventions; its action button was disabled. No
mutation, approval, actual cleanup, service restart or external delivery was
triggered through the UI.

One first screenshot capture showed a blank client area while the app was
loading; after closing and relaunching, the dashboard rendered normally and
remained interactive. A read-only Application event-log search over the
preceding two hours found no HostAgent Desktop runtime error. It did return an
older `testhost.exe` failure from the maintenance UI test involving
cross-thread WPF access; that is test-process evidence, not evidence of a
HostAgent Desktop crash. The transient first-frame behavior was not root-caused
or reproduced after relaunch, so it remains an observation rather than a
confirmed defect.

## Remaining limits

Live health remains degraded in this DEV VM: CPU/process metrics are partial,
some collectors are unavailable, and disk I/O / SMART telemetry is partial.
The dashboard exposes those bounded reasons; this verification did not alter
permissions or security policy to obtain more data. Pushover and heartbeat
remain disabled and were not tested against real external endpoints. The
package is unsigned and intended only for this DEV VM. This work does not
establish production signing or production-host readiness.

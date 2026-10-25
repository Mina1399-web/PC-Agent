# Final acceptance evidence — 2026-10-25

This record captures only checks executed on 2026-10-25 in the approved DEV VM. It does not replace earlier verification records.

## Environment and source

- VM identity: `AGENTPC`, `AgentPC\\AgentPCdevtest`, VirtualBox UUID `46543236-8C5E-49DA-953C-DB17F9B1E402`; `VBoxService` was Running.
- Checkout: branch `codex/installed-telemetry-stabilization`, source commit `fbfe055a911419e35c39ade079cc3eeb65501c60`; working tree clean before and after builds/tests.
- Build SDK: pinned .NET SDK `10.0.401`; WiX `5.0.2` and DTF `5.0.2` user-local tooling.
- Installed product remained HostAgent `1.2.6`; services `HostAgent` and `HostAgent.Helper` remained Running with the previously observed service identities. No service control, install, or configuration change was performed.
- Resource floors: test/build start required >1,024 MiB free RAM; watchdog stopped its own child below 768 MiB and at/below 512 MiB; C: free-space floor 15 GiB.

## Release build and tests

- Release solution build command: `dotnet build HostAgent.sln --configuration Release --no-restore -m:1 /p:UseSharedCompilation=false --verbosity minimal` — PASS, exit 0, 0 warnings, 0 errors, 2m36s.
- Core tests: `dotnet test tests/HostAgent.Core.Tests/HostAgent.Core.Tests.csproj --configuration Release --no-build --no-restore -m:1 --logger 'console;verbosity=minimal'` — PASS, 62/62.
- Infrastructure tests: `dotnet test tests/HostAgent.Infrastructure.Tests/HostAgent.Infrastructure.Tests.csproj --configuration Release --no-build --no-restore -m:1 --logger 'console;verbosity=minimal'` — PASS, 562/562.
- Desktop tests: `dotnet test tests/HostAgent.Desktop.Tests/HostAgent.Desktop.Tests.csproj --configuration Release --no-build --no-restore -m:1 --logger 'console;verbosity=minimal'` — PASS, 15/15.
- Total across the three freshly executed test projects: 639 passed, 0 failed, 0 skipped. The tests were run individually under the resource watchdog; a single whole-solution `dotnet test` invocation was not captured as successful. Earlier diagnostic attempts of the aggregate run were stopped by the operator after prolonged silent output; they are not treated as test failures.
- Resource receipts: build min RAM 2,076.9 MiB / min C: 16.81 GiB; Infrastructure min 1,944.2 MiB / 16.64 GiB; Core min 2,342.0 MiB / 16.75 GiB; Desktop min 2,079.0 MiB / 16.75 GiB.

## DEV VM release candidate

- Built outside the source checkout at `HostAgent-DEV-releases/HostAgent-1.2.7-dev-stabilization-fbfe055-r1` using `tools/Build-Release.ps1 -DevVm -Version 1.2.7`.
- Candidate validation: `tools/Test-Release.ps1 -DevVm` — PASS, `valid=true`, 1,214 manifested files, source commit matches the checkout.
- Installer validation ran as part of release build: `tools/Test-Installer.ps1 -DevVm`; it inspected MSI database tables and confirmed service identities/start modes, lifecycle/recovery configuration, guarded counter-permission transaction, uninstall data preservation, DEV machine guard, and shortcut metadata. No MSI installation was performed.
- Operator installer preflight: `Install-HostAgent.ps1 -DevVm -PlanOnly` — PASS; reports `mode=Shadow`, `readOnly=true`, `changesPerformed=false`.
- ZIP archive contains exactly the 1,214 manifest payload entries plus `manifest.psd1`; path-set comparison PASS.
- SHA-256: manifest `4020A1E7E8F9855F8E8A0161442B5EDA16519FB3A45A3B9193EC69A80A90E754`; MSI `48ABE89407459387074CA3A00D4C8ADF1FD5451008CC16B568553437513F2DDA`; ZIP `AEA20CDDAF60AAD567C5FFDE43C705FA3D290B5F7B482AF41B2C9681FC82B011`.
- MSI Authenticode status is `NotSigned`, as expected for a machine-bound DEV VM artifact. This candidate is not for production or general distribution.
- Guarded packaging receipt: exit 0, minimum free RAM 1,293.1 MiB, minimum C: 15.87 GiB; no guard triggered.

## Remaining acceptance gates

- At the time this record was first written, 1.2.7 had not been installed. The later operator installation and read-only checks are recorded in the follow-up below; live telemetry/database acceptance is still incomplete.
- Production-grade signing/provenance is not available in this checkout: no eligible signing certificate/private key was present in the user certificate store, and no completed SignPath signing workflow/enrollment was verified. Do not distribute the unsigned DEV VM MSI.
- Shadow/read-only remains the only approved operating mode. Confirmation/full-access mode, real remediation, reboot acceptance for this candidate, and external notification delivery were not performed or approved by this record.
- Do not interpret source tests, artifact validation, or installer-table inspection as installed-product acceptance.

## Operator installation follow-up — 2026-10-25

- Operator-reported installer result: `installed=true`, `restartRequired=false`. Independently read from Windows uninstall metadata: product `Host Agent V1`, version `1.2.7`, install date `20261025`.
- Fresh VM identity check: `AGENTPC`, account `AgentPC\\AgentPCdevtest`, VirtualBox UUID `46543236-8C5E-49DA-953C-DB17F9B1E402`; VBox Guest Additions service Running. At the check, free RAM was 1,823.5 MiB and C: had 15.78 GiB free.
- Fresh service check: `HostAgent` Running (PID 3060, `NT SERVICE\\HostAgent`) and `HostAgent.Helper` Running (PID 3076, LocalSystem); installed paths matched the expected Program Files directories.
- Fresh installed executable SHA-256 checks matched the corresponding entries in the reviewed candidate manifest (manifest SHA-256 `4020A1E7E8F9855F8E8A0161442B5EDA16519FB3A45A3B9193EC69A80A90E754`): Service `AE37E41FD05FB89CB1E820A9CB22C4FD83CDA06475A9216DE1CE6767C8C8064F`; Helper `B326AE31BEF7F1551E2D4C762373FED2BE48D31344D939C887A0D27559DA6607`.
- `HostAgent.Desktop` process was present (PID 1104) during the check. This confirms the dashboard client process is running, but this follow-up did not independently re-check its rendered telemetry values.
- This follow-up's non-elevated shell could not run installed `status --verbose` (`administrator_required`) or open protected database files (access denied). The operator subsequently supplied a successful elevated `status --verbose` response; database integrity checks remain pending. No permissions, services, or databases were changed by this follow-up.
- The operator-reported install response and read-only checks do not establish telemetry/database acceptance, service-token PDH acceptance, or product health. Keep Shadow/read-only enabled. MSI signing is still absent, so the DEV package remains unsuitable for production/general distribution.

## Operator status response — 2026-10-25

- Operator supplied the elevated CLI response: `ok=true`, `code=ready`, `ConfigurationAvailable=true`, `mode=Shadow`, `ReadOnly=true`, `degraded=true`, Pushover and heartbeat disabled.
- Health was reported healthy for administration, collection_worker, diagnostics, publication, event-log, network, and system; optional SMART was unavailable with `degraded=false`. Collectors aggregate, CPU/memory, and process/services were partial/degraded. This is CLI health evidence, not proof that all telemetry is complete or that storage is healthy.
- The operator output did not contain database integrity results. No database quick_check is claimed by this section.

## Evidence locations

Detailed stdout/stderr and watchdog receipts are under `%LOCALAPPDATA%\\HostAgent-DEV-evidence\\final-acceptance-20261025`. The release package and ZIP are under `HostAgent-DEV-releases` outside the repository.

# Host Agent1.2.2 predeployment source checkpoint — 2026-10-18

## Boundary

Prepare to the last step before deployment. No installation, service control, secret provisioning, production transfer, signing submission or release publication is performed. Installed1.2.1 remains unchanged. The build candidate is explicitly unsigned and restricted to the approved DEV VM. A production release still needs an independently trusted publisher, target-policy acceptance and separately reviewed installed acceptance.

VM identity freshly confirmed: AGENTPC, AgentPC\AgentPCdevtest, VirtualBox UUID46543236-8C5E-49DA-953C-DB17F9B1E402, VBoxService Running. Initial freeRAM1728.9MiB/C:26.74GiB. Source base6cd0ec8ba61ce1c7c7cd89787f02f86b9b87ed8e on codex/local-dashboard; all pre-existing dirty work retained. A verified before-predeployment Git bundle, tracked/index diffs and later source snapshot/manifest are retained outside the checkout in `%LOCALAPPDATA%\HostAgent-DEV-evidence\predeployment-20261018T045333Z`.

## Fresh verified corrections

- P2 malformed optional telemetry isolation: actual null-element disk/network/EventLog inputs raised NRE and suppressed a valid CPU diagnosis. Required collection members are validated before evidence assembly; CPU diagnosis confidence85 remains exact. P3 health: two stable service metadata reason codes now survive allowlist filtering, while arbitrary warning text remains excluded. Focused RED19pass/7fail then GREEN26/26, watchdognull. Only two product files changed in this final continuation; earlier dirty product fixes are preserved and included in the final review range.
- P2 MSI validator: deleting the real existing-service condition, deleting the DEV condition and replacing it with a nonmatching real ComputerName/MachineGuid condition each produced erroneous exit0 before the fix. Validator now throws fixed errors; Build-Release forwards its DevVm mode. Non-DEV MSI structure does not require a DEV guard. Focused22/22 PASS with real disposable MSI mutation checks and no installation. Setup-only unsupported MSI primary-key UPDATE was corrected before obtaining RED evidence; not presented as a product defect.
- Current README, quickstart and recovery documentation now reflect the dashboard, version1.2.2, two-second cooperative drain and existing MSI ten-second/two-restart configuration. Historical records are retained.

## Final gates

| Check | Fresh actual result |
| --- | --- |
| Release build | exit0,0warnings/0errors |
| Complete Release suite |450/450 PASS after the final upgrade correction: Core62, Infrastructure373, Desktop15;0failed/skipped. Earlier444/444 runs remain preserved. |
| Final product focused |26/26 PASS after19pass/7fail RED |
| Final MSI-tooling focused |28/28 PASS, real MSI negative/positive cases; includes six new upgrade sequence cases |
| NuGet transitive vulnerability query | exit0, ten projects, no vulnerable package reported by current NuGet.org query; not a guarantee against unknown/native issues |
| Current-source private runtime |20.1s, View2/2, helper0, transport/dispatch failures0, minfree1680.6MiB, bounded normal stop |
| Memory/disk watchdog | unchanged250ms, initial1024MiB, soft768/hard512MiB and20GiB C:; final monitorFailure=null |

Exact final commands (verified local SDK path, DOTNET_ROOT and PATH explicitly set):

```powershell
dotnet build HostAgent.sln -c Release --no-restore -m:1 /p:UseSharedCompilation=false
dotnet test HostAgent.sln -c Release --no-build --no-restore -m:1 --logger 'console;verbosity=minimal'
dotnet list HostAgent.sln package --vulnerable --include-transitive --no-restore --format json
```

The real current-runtime command was `tests/HostAgent.PipeProbe/bin/Release/net10.0-windows/HostAgent.PipeProbe.exe isolated-runtime e3742ef255c1483abcc84104d9e25dcd observe` (fresh user-owned disposable DB/private IPC, no pressure/injection). The20s smoke had no persisted CPU samples and does not count as a repeated sustained-CPU/soak result. Full command/exit/duration/stdout/stderr/monitor receipts are under final-validation; the completed heavy fault runs remain separately hash-bound.

## Candidate and source identity

A separate local release branch codex/predeployment-1.2.2 preserves the reviewed work without resetting or rewriting the original branch. Build-Release requires that immutable clean source; the actual source commit, complete package/MSI/ZIP/manifest SHA-256 identities and validation receipts are emitted after the source checkpoint to source-external release/evidence files. They are not guessed into a circular self-referencing source manifest.

Expected DEV candidate directory (must not already exist):
`..\HostAgent-DEV-releases\HostAgent-1.2.2-dev-predeployment-r3`.
The matching receipt is outside the package: `..\HostAgent-DEV-releases\HostAgent-1.2.2-predeployment-receipt.json`. Only an actual successful receipt closes artifact preparation. No candidate is production-signed by this record.

Packaging uses explicit Version1.2.2 (installed1.2.1 cannot be upgraded using the builder's old1.0.0 default), verified account-local SDK10.0.401, runtime10.0.12, existing PowerShell7, WiX5.0.2 and verified existing DTF layout. PATH and DOTNET_ROOT are explicitly set to that SDK; the system dotnet installation has no SDK. No build-tool install or security change is needed.

## Review and handoff

Bounded independent product review covered all current src differences from base6cd0ec8 plus ServiceTriggerQuery and related tests. No P0/P1 or authorization/replay/approval regression was identified; the actionable final findings above were reproduced and fixed. Host Agent Support independently reviewed packaging/tooling, verified installedVersion1.2.1 from read-only MSI metadata, found the validator issue and requested exact final artifact checking. Source credential/artifact scan and source-input SHA manifests are retained outside source; heuristic scan is not proof against arbitrary secrets.

The [final resilience assessment](resilience-final-2026-10-18.md) preserves multi-candidate phase provenance, original failures, scope limits and the operator's power-off confirmation. The current verdict remains CONDITIONAL until installed-release and external production gates are satisfied. Successful old faults are not repeated only to obtain a new run ID, and old candidate hashes are not equated with newly compiled product binaries.

## Deliberately unperformed deployment steps

1. In a separately approved installation window, take/verify a current operator rollback point and a consistent backup of protected configuration/DBs/sidecars. Do not copy secrets into source or logs.
2. Inspect the exact new package/manifest/MSI and trusted scope. A DevVm package may install only in the approved VM; it cannot serve as a production package. Review UAC normally; never disable policies.
3. An actual install/upgrade will stop/restart protected product services; it is **not authorized in this predeployment run**. An ordinary MSI transaction rollback is not a promise to roll back application data; a tested backup/VM snapshot remains the fallback.
4. After a future approved install, verify installed file hashes, both service identities/recovery, default Shadow/outbound-off state, telemetry/View/dashboard and data continuity. Preserve a failed result; never retry an uncertain remediation or erase its journal.
5. Production distribution additionally requires signing/provider approval, final signature/hash regeneration and target-policy acceptance. This task does not submit the pending SignPath application, purchase a certificate, change GitHub visibility or deploy to the physical host.
An earlier finite source re-review found no new actionable issue in the two product fixes, then-current MSI validator change and current operator docs. Subsequent independent actual-artifact review found the upgrade sequencing issue below. The earlier changed-doc relative-link check52/52 passed; bounded credential/artifact scan380sourcefiles/375textfiles reported zero forbidden paths and zero strong-token/private-key patterns. Values are never printed by that scanner; this is a bounded heuristic, not a guarantee. Git whitespace check passed. Old r6 byte-count metadata caveat remains explicit in the resilience summary; no retained artifact was repaired or deleted.

The first complete staged check additionally exposed six pre-existing blank-at-EOF warnings in two historical chaos records and four hash-pinned historical harness files. Their exact bytes were preserved. Standard CRLF-aware whitespace validation passed for all other staged files; those six passed with only blank-at-EOF excluded (other whitespace checks retained). The original six warnings are archived as staged-whitespace.txt. This bounded exception is not a skipped product test or a changed runtime/security guard.


The first real package build from checkpoint b1cc800100fc925a8d9bb82acf3849bec7543b25 was safely aborted by the unchanged soft768MiB watchdog while publishing CLI. No MSI/installation completed. No SDK/compiler processes remained after bounded job cleanup; protected HostAgent/helper remained Running/PIDs3536/3264. Partial output HostAgent-1.2.2-dev-predeployment and package-validation failure evidence are preserved. Release publishing now explicitly uses one MSBuild node and disables shared compilation; this is a build-resource configuration change, verified by the actual subsequent build/receipt rather than a mock of command strings. A fresh -r2 output is required; the first partial directory is not reused or called a release. The final package receipt determines success. No thresholds or security policies changed.

Historical resource stop before package qualification: the subsequent serial-publish tooling recheck was also aborted by the unchanged soft768MiB monitor (exit-1); it is NOT a new22/22 PASS. Its prior22/22 and complete444/444 source-suite results remain explicitly dated before this build-configuration-only edit. No SDK/test process remained, protected services stayedRunning/PIDs3536/3264, and no install occurred. The operator was asked for the single minimal step of closing unnecessary Chrome windows inside the DEV VM. No application was force-closed and no threshold/security setting lowered.

A verified380-file source ZIP, Git bundle, full patch against6cd0ec8, per-source SHA-256 manifest and separate then-pending build-memory patch are retained under the source-handoff evidence directory. They contain no runtime DB, SDK, cache, binary or signing material; nothing was transferred to the physical host. The exact current status is in source-external predeployment-current-status.json.

After the operator closed Chrome, fresh VM identity/Guest Additions verification passed with1704.2MiB free RAM and26.37GiB free C:. The actual tooling recheck `dotnet test tests/HostAgent.Infrastructure.Tests/HostAgent.Infrastructure.Tests.csproj -c Release --no-restore -m:1 /p:UseSharedCompilation=false --filter FullyQualifiedName~ReleaseToolingTests --logger 'console;verbosity=normal' --no-build` completed22/22 PASS in1.5176minutes, exit0, monitorFailure=null. Evidence: `installer-validator/serial-publish-tooling-after-memory.{stdout.log,stderr.log,result.json}`. The source change adds only serial MSBuild and disabled shared compilation to publishing; runtime/security thresholds remain unchanged. Free RAM after the run was1933.5MiB. At that checkpoint, the fresh-r2 package build was still unexecuted; its later actual results and superseding upgrade finding are recorded below.

## Final artifact finding and verified correction

The real R2 build at source95d9e7ee7da7180ad8a154314a7bc3d9dcd11bb7 completed all nine declared package checks under monitorFailure=null. Its private host matched the published Core/Infrastructure/Service hashes and completed20.2s/2View successes/0failures/0helper calls/minfree1134.1MiB. Source build and complete444/444 suite were repeated successfully. These are preserved facts, not final upgrade acceptance: Host Agent Support's independent read-only ZIP/MSI review found P2 `RemoveExistingProducts=1401` before `InstallInitialize=1500`. R2 is preserved and superseded, not repaired in place or approved for upgrade. Its1204payload SHA/length entries and1205ZIP files had matched; the defect was upgrade scheduling.

Test-first actual-MSI evidence in `installer-validator`: `red-upgrade-transaction`5expectedFAIL/27.3900s, then `green-upgrade-transaction`5/5PASS/23.8404s. The validator previously accepted missing removal and removal before Initialize, after ProcessComponents or after Finalize. `Build-Installer.ps1` now explicitly uses `MajorUpgrade Schedule="afterInstallInitialize"`; `Test-Installer.ps1` rejects missing/unsafe action sequences with `release_installer_upgrade_transaction`. A similar disabled `InstallInitialize=0` fixture then reproduced a second gap: `red-upgrade-disabled-initialize`1expectedFAIL/6.7387s, then `green-upgrade-disabled-initialize`1/1PASS/5.3830s after requiring all five required sequence values to be positive. Final `focused-all-release-tooling-upgrade-final`28/28PASS,0failed/skipped,exit0,1.1411minutes, empty stderr. Every monitorFailure=null. Actual read-only MSI table: Initialize1500 < RemoveExistingProducts1501 < ProcessComponents1600 < InstallFiles4000 < Finalize6600.

The final Release build and complete suite after these changes passed450/450: Core62, Infrastructure373, Desktop15,0failed/skipped; build0warnings/0errors. Exact commands above; receipts, stdout/stderr and hashes of the three tested changed source files are in `final-installer-suite`. The read-only installed-target check confirmed1.2.1 product `{F541B3CC-1A08-4123-A4EC-96C7279EC7D2}` with matching UpgradeCode `{E839C131-FD8D-45AA-927E-A2364BD39AE2}`. No Windows Installer installation action or service control was run.

[Primary WiX scheduling documentation](https://docs.firegiant.com/wix/schema/wxs/majorupgrade/) explains why afterInstallInitialize brings removal inside the installer transaction. These checks prove real MSI structure and validator behavior; they do not prove an executed installation-failure rollback or application-data rollback. Final R3 build/hash-bound artifact audit and exact-published-runtime smoke are recorded only in the source-external final receipt/report after this clean source checkpoint. Actual upgrade/installed acceptance, signing and production policy acceptance remain separate unexecuted gates.

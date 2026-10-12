# Stabilization 2: installed DEV acceptance and crash analysis

Date: 2026-10-12, Europe/Prague. Fresh continuation of stabilization 1; historical results are not relabeled as current evidence.

## Environment and preservation

Approved DEV VM reverified before changes: AgentPC / agentpc\agentpcdevtest, innotek GmbH VirtualBox, UUID 46543236-8C5E-49DA-953C-DB17F9B1E402, VBoxGuest/VBoxService Running. Repository `C:\Users\AgentPCdevtest\Documents\ChatGPT\DevTest VM PC\HostAgent-DEV`, branch `codex/stabilization-1`, starting HEAD `c7f2afacf260939741db12740c7b42bbe1add4b4`. Tracked tree was clean; the existing untracked independent audit was preserved, SHA-256 `63268A0FE5D734BA4D5484C87004A2F702F8B031BC93B83E56DB6A6CF46C414D`. SDK 10.0.401 selected through process DOTNET_ROOT/PATH. No production host, security policy, VM snapshot, real secret or application delivery endpoint was used.

## Added regression coverage and Release gates

Two tests were added before any further production-code change. Both passed on the existing implementation; these are coverage additions, not a newly demonstrated red/green fix. Production code is unchanged from stabilization 1.

- Reset during a persisted, truncated bootstrap: lower RecordIDs are admitted after reset, unrelated hour-old and expired WER records are excluded, restart/repeated reads do not duplicate, and a subsequent new event is collected. Follow-up review prompted a stronger synthetic 30-hour-old bootstrap anchor, so reusing the stale anchor would fail the selectivity assertion.
- Real collection jobs/SQLite/runner/health: healthy → mandatory system Unavailable → backoff → healthy, with optional SMART informational throughout. Recovery clears collector degradation while preserving an independently failing diagnostics worker until that worker recovers.

Evidence directory outside source: `C:\Users\AgentPCdevtest\AppData\Local\HostAgent-DEV-evidence\stabilization-2`.

```powershell
$sdk = 'C:\Users\AgentPCdevtest\AppData\Local\HostAgent-DEV-tools\dotnet-10.0.401'
$env:DOTNET_ROOT = $sdk
$env:PATH = $sdk + ';' + $env:PATH
dotnet test tests/HostAgent.Infrastructure.Tests/HostAgent.Infrastructure.Tests.csproj -c Release --filter 'FullyQualifiedName~EventLogBootstrapTests|FullyQualifiedName~HealthStabilizationTests|FullyQualifiedName~WindowsEventLogCollectorTests|FullyQualifiedName~CollectionCoordinatorTests' --logger 'console;verbosity=normal'
dotnet build HostAgent.sln --configuration Release
dotnet test HostAgent.sln --configuration Release --no-build -m:1 --logger 'console;verbosity=normal'
```

Initial runs 01/02/03: focused 33 passed; build 0 warnings/errors; full Core 50 + Infrastructure 195 = 245 passed. After the anchor-strengthening test change, the exact commands were repeated in 04-final-focused.log / 05-final-build.log / 06-final-suite.log: exit 0 for all, 33 focused passed, build 0 warnings/errors, **245 full-suite passed, 0 failed, 0 skipped**. `git diff --check` passed. Independent read-only review approved the initial test tree `ab05a4e707095bb47d19526cb828ec77c57a5040`; review of the strengthened tree `fe686a3be170a4c8aeba7b8fe0cc478bd3f75b05` is recorded at the later acceptance checkpoint.

## Read-only dump analysis

Installed Microsoft WinDbg 1.2610.1001.0 supplies KD 10.0.29661.1004; no debugger installation was needed. Five local `C:\Windows\Minidump` files were hashed before and after each analysis and remained byte-identical. Raw debugger output stays in Administrators/SYSTEM-only `C:\ProgramData\HostAgent.StabilizationEvidence\2026-10-12`, outside source; no dump or raw stack is published.

An initial CDB invocation was unsuitable and returned E_INVALIDARG for every dump; it is not counted as successful analysis despite its launcher exit 0. Corrected KD offline runs exited 0 but reported WRONG_SYMBOLS; their synthetic analyzer bucket/code is not accepted as a real diagnosis. KD was then rerun with the matching public Microsoft symbols, using only the official symbol server documented at [Microsoft public symbols](https://learn.microsoft.com/en-us/windows-hardware/drivers/debugger/microsoft-public-symbols). Only symbol retrieval was networked; no dump upload, application endpoint or live notification was used.

Commands per dump were equivalent to:

```text
kd.exe -noshell -nosqm -sins -y "srv*C:\ProgramData\HostAgent.StabilizationEvidence\2026-10-12\microsoft-symbols*https://msdl.microsoft.com/download/symbols" -z "C:\Windows\Minidump\<file>.dmp" -logo "<protected-log>" -c ".reload /f nt; .bugcheck; !analyze -v; lmvm nt; lmvm VBoxGuest; q"
```

All five matching-symbol KD analyses exited 0 without WRONG_SYMBOLS in the allowlisted analysis summary:

| Dump | SHA-256 | Stop code | Analyzer bucket |
| --- | --- | --- | --- |
| 100926-12953-01.dmp | C5D976A10FFE9CCF9B1F68C986FA45964322FFC5BBEA8FCBC16181C2212D0FCC | 0xA | AV_nt!KiDpcInterrupt |
| 101126-15921-01.dmp | 5741C2A06533742111CEB0A622364C0500C417B31101EFA33028865107021125 | 0xA | AV_nt!KiIpiInterrupt |
| 101126-19015-01.dmp | 44DCFF925F53BF38409F06AAF94C00FB769FE1D7B80660CDB5E04D547EC91FA6 | 0xA | AV_nt!KiIpiInterrupt |
| 101226-14265-01.dmp | F54806A3D46E6780443350F8B587A438108760044B1FEC2D9357B5A9E8F747EE | 0xA | AV_nt!KiIsrThunk |
| 101226-15000-01.dmp | 28A8A97E906B894B497E8A8EDAA7FC9CC6A2626A1C4828968FC2C0F20D3BB110 | 0xA | AV_nt!KiDpcInterrupt |

The three previously investigated WER records correspond by dump filename/time to 101126-15921-01 (RecordID 2297), 101226-14265-01 (2580) and 101226-15000-01 (2671). This does not alter their prior collection/storage findings. The matching-symbol analyzer identifies kernel interrupt-handler sites and module nt/ntkrnlmp.exe; that alone does not attribute the originating defect to the kernel, Guest Additions or another driver. No specific driver or Host Agent fault is proven. No Driver Verifier, driver update/rollback, forced crash, reboot, host inspection or security change was performed. Cause-specific repair remains unsafe without stronger attribution; retain the original dumps and protected analysis.

## Installed acceptance checkpoint

Before deployment, the existing 1.0.4 DEV package was freshly validated with its Test-Release.ps1 -DevVm: exit 0, 705 files, source c9c39d09593eb59de61fc7060a7e66df18de3e71. Installed CLI file version was freshly read as 1.0.4.0; both services were Running/Auto with monitor NT SERVICE\HostAgent and helper LocalSystem. Backup, upgrade and installed acceptance are pending at this pre-deployment source checkpoint; later evidence will be appended only after actual execution.

## Executed installed acceptance

The strengthened test tree was independently approved, resolving the reanchoring coverage gap. The scratch deployment harness was also reviewed before execution. Initial review found backup ancestor/parent protection, cascading dependent-service stop and partial-stop recovery blockers; all were corrected and independently approved before any installation operation. Stop(false), unrelated-active-dependent rejection, recovery finally blocks that attempt both product services, protected parent/leaf ownership/ACL validation and reparse ancestor rejection were used. These scratch harnesses remain outside source and are not product capabilities.

A detached clean VM worktree at `HostAgent-DEV-stabilization-package` preserved the original untracked audit and built source commit **4dc9bb91c0ba03a7d55625ef88344d59928dc63d**, using:

```powershell
git worktree add --detach 'C:\Users\AgentPCdevtest\Documents\ChatGPT\DevTest VM PC\HostAgent-DEV-stabilization-package' 4dc9bb91c0ba03a7d55625ef88344d59928dc63d
pwsh -NoProfile -File tools/Build-Release.ps1 -PackagePath 'C:\Users\AgentPCdevtest\Documents\ChatGPT\DevTest VM PC\HostAgent-DEV-releases\HostAgent-1.0.5-stabilization-dev' -WixPath 'C:\Users\AgentPCdevtest\Documents\ChatGPT\DevTest VM PC\HostAgent-DEV-tools\wix\wix.exe' -DevVm -Version 1.0.5
pwsh -NoProfile -File tools/Test-Release.ps1 -PackagePath 'C:\Users\AgentPCdevtest\Documents\ChatGPT\DevTest VM PC\HostAgent-DEV-releases\HostAgent-1.0.5-stabilization-dev' -DevVm
pwsh -NoProfile -File tools/Test-Installer.ps1 -PackagePath 'C:\Users\AgentPCdevtest\Documents\ChatGPT\DevTest VM PC\HostAgent-DEV-releases\HostAgent-1.0.5-stabilization-dev'
```

The build used the equivalent direct script invocation from that clean worktree; validation/inspection used the copies inside the package. All exited 0. Manifest validation passed 707 files; independent MSI inspection passed 703 installed files, two services with exact virtual monitor/LocalSystem identities, existing-service guard and matching DEV VM guard. MSI SHA-256 **6CE6E141166860EF05E9E73B7FD6A9E8C25C1649B44922B198A9389030DA67CB**; ZIP SHA-256 **860DF14FF6D934DAC51957E1E8080C994FEB1FF319C193F27CD009E88F7E4C41**. This is an unsigned VM-only candidate, not a signed production release.

Normal UAC launched `Upgrade-And-Smoke.ps1 -PackagePath <the verified package>` under an administrator token. Both the launcher exit and `installed-result.json` confirmed success (exit 0, completed=true). The harness executed the package's standard Install-HostAgent.ps1 -DevVm wrapper; it did not manually replace running binaries or change security policy.

| Installed acceptance | Fresh result |
| --- | --- |
| Consistent pre-upgrade backup | Both product services stopped; SQLite read-only backup API produced integrity_check=ok. Backup contained 4800 telemetry rows, 36 incidents, 494 snapshots and 494 evidence rows, 65 audit entries and 2 checkpoints. No secret store was copied. |
| Protected return point | `C:\ProgramData\HostAgent.StabilizationBackups\2026-10-12-1.0.4`, protected Administrators/SYSTEM parent/leaf. Preserved installed files, old MSI/manifest/wrapper and consistent DB. Separate verification exit 0 checked all 701 old installed files against their original manifest, old MSI hash and unchanged DB backup hash. |
| Upgrade | Actual 1.0.4.0 → 1.0.5.0; 703 installed files matched new manifest hashes. Configuration SHA-256 remained 0D0DCCF65BDCCDED91AD1C1BF7B4E7AB5F738FF2E04E9D7D9A80E91BC1F2670F. |
| Graceful stop/start | Initial two-service stop 1079 ms; explicit post-upgrade stop 526 ms; both product services returned Running/Auto with exact original accounts. These are measured requested stops, not forced-crash recovery evidence. |
| Admin CLI | Installed `status --verbose` exited 0 before and after explicit restart; Shadow, ReadOnly=true, configuration available, Pushover/heartbeat disabled. Active network probes remained disabled in unchanged config. |
| Database preservation | Read-only comparison retained all 36 incident IDs, all 494 historical snapshots/evidence rows byte-identically, all 65 audit rows byte-identically; both checkpoint keys/updates/RecordIDs preserved or advanced. New snapshot/evidence counts were 495 each. Comparison made 0 source changes. |

Backup DB SHA-256 is **863041a6d51f3f18549cedf490b981030b161441557d31dbc89b0041c924024b**. Preserved old MSI SHA-256 is **817812FC5078E305CDFA0DED5246353BBC522EE250BBBC9D941333C44FA8ACFA**. A protected ROLLBACK.txt records the exact new product code and old MSI reinstall procedure. Rollback/DB restoration was prepared but not executed; normal uninstall preserves ProgramData, and an actual DB restore would require a separate stopped-service recovery procedure preserving later data.

## Observed runtime health and limits

The installed verbose response correctly exposes `collector_partial` for cpu-memory, disk and process-services, while system/event-log/network are healthy. Overall degraded remains true. A read-only, allowlisted latest-telemetry query explains the observed initial warnings:

- Disk: `disk_io_counters_unavailable` plus `smart_health_unavailable`. This is not SMART-only, so remaining disk degradation is expected under the current contract.
- CPU/memory: startup `cpu_baseline_not_ready` and `some_process_metrics_unavailable`.
- Processes/services: `some_process_metrics_unavailable` and `some_executable_paths_unavailable`.

No additional service privileges, ACL grants, counter policy changes or disabled protection were introduced to hide these partial results. SMART-only nondegradation and mandatory-failure/recovery behavior are verified through real jobs/SQLite/runner/health with synthetic OS-boundary collectors; this installed VM cannot demonstrate a SMART-only live result while its required I/O counters are unavailable. The distinction is explicit rather than falsely claiming the installed agent is wholly healthy.

The live two-minute collection observation is recorded below after completion. Cold boot without login, forced-crash SCM recovery, multi-hour soak, actual rollback/DB restore and real notification delivery remain outside the executed checks. Existing checkpoints were preserved; no retrospective WER replay/rediagnosis or driver modification occurred. There is no host transfer or GitHub push in this five-point continuation.

`Observe-Installed.ps1` completed with exit 0 and allProgressed=true after 130 seconds, from 2026-10-12T09:49:43.351869+00:00 to 09:51:55.891424+00:00. Each of all six collectors advanced from **802 to 804 persisted samples**. Installed `status --verbose` again exited 0; diagnostics/administration/publication and system/network/event-log were healthy. The transient CPU baseline warning disappeared; the process-access and disk-I/O warnings above remained. A separate saved CIM service-state check at 2026-10-12T09:55:10.9935683+00:00 confirmed both services Running/Auto with original accounts (`final-services.json`). This is short post-restart observation, not a multi-hour soak or proof that restart crashes are cured.

## Evidence manifest and five-point result

The following fresh local evidence hashes identify the actual runs. Raw debugger logs, database backup, manifests of installed files and scratch operational harnesses remain outside Git; no environment artifact or secret is committed.

```text
04-final-focused.log      C2C462CB504BA55C04FB57A0F75036BC231DEB15593F93A708DE09531BCB67C3
05-final-build.log        732EA4E0CE7917DD1AB03F8C133F956C3EAAB5704F4FDAFD29018F07EAD70723
06-final-suite.log        1661B17B7364E59D8EA6486A452032166048B8A0207F4B952650DD029A80736E
07-package-build.log      CE8A85E6301666C9A16F4630C105E36950D90C519A438B0968AC1DB865CBB513
08-package-validation.log 5879D6FB044D31FF181378F6F0E9A6A63A67B0A925C0C688E5BCFD48AB58AF6C
09-msi-inspection.log     DCD3010B64233C87928E501669B9C730E93CC607122F4EC854E012630164E41D
installed-result.json     964F5E8B4ED80C07E38CD5C5804F269EBDA2D73F5C30CCEBF989A012F42EE0FE
return-point-verification.json 0EA196040480E5948CD0CEC30762B4945676F5BCDA4778DB3B71FC83A34366E4
live-observation.json     E53CDE4A520E45E8568D01AD47365041EA94F72725898FA96A4D8ABE9F2B7128
dump-symbols-analysis-summary.json FC6C85EDAAAF9444C834AA01FE014268AA39600CAB14984AB79C776458FA1018
final-services.json       DB08511BCF8A2FD15B8C84720686357C94B566B211F197ABD161EB7B3376D5D4
```

| Authorized point | Result |
| --- | --- |
| 1. Preserve return version and DB | Verified protected original installation/MSI and consistent DB backup, including independent post-copy hash/integrity checks; actual restore not exercised. |
| 2. Upgrade and verify service lifecycle/CLI | Verified installed 1.0.5, requested stop/start, exact accounts, manifest hashes, unchanged safe config, verbose status and continuing persistence. No forced-crash or cold-boot claim. |
| 3. Distinguish optional SMART from required errors | Verified SMART-only and required failure/backoff/recovery through synthetic OS-boundary integration tests; installed verbose health and allowlisted DB reads confirm actual additional missing telemetry stays degraded. Live SMART-only scenario not available in this VM. |
| 4. Add missing reset/recovery tests | Both regressions added and independently reviewed; final focused 33/33 and full suite 245/245 pass. Existing production behavior already passed; no new implementation fix is claimed. |
| 5. Analyze available crash dumps read-only | All five dumps analyzed with matching Microsoft symbols, byte-identical afterwards; repeated 0xA in kernel interrupt handlers established. Specific driver/root cause and safe repair remain unproven. |

The next engineering decisions are how to expose/deal with the remaining partial collector capabilities while retaining least privilege, and how to obtain stronger crash attribution without changing host policies. Neither missing telemetry nor the debugger's nt bucket justifies an automatic driver change. DEV installation/short acceptance is complete; production signing, deployment and broader operational acceptance remain separate gates.

Final independent read-only documentation/evidence review approved the record with no material overclaim or blocker; the ten evidence hashes then present were independently checked against files. Its two minor precision suggestions were applied: distinguish snapshot/evidence row counts and retain a separate final service-state record (the additional eleventh evidence hash above). No reviewer execution of tests, installers, native operations or service changes occurred. Final executable source remained identical to the tested/reviewed package commit; only this evidence document changed afterwards. Original untracked audit hash was rechecked unchanged.

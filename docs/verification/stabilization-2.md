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

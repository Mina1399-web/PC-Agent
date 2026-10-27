# Process telemetry / degraded recovery — 2026-10-27

## Scope

Approved DEV VM AGENTPC, account AgentPC\AgentPCdevtest, VirtualBox UUID
46543236-8C5E-49DA-953C-DB17F9B1E402, VBoxService Running. Source base
d9a09f5cae0e01a6ff59ac29d64d290b1757214d on codex/desktop-localization.
The pre-existing final-acceptance-2026-10-25.md edit was copied to private
evidence before changes and is preserved/excluded from this packet.

The installed 1.2.9 receipt c85991b5 remains the previous successful
action/journal/three-database acceptance. These collector changes do not alter
database schemas, helper approval/allowlist/executor rules, credentials,
Windows permissions, service configuration or the installed binaries.
Current installed service-token acceptance of the new collector is **pending**.
No MSI, elevation, service control or production-host operation was performed
as part of the source verification below.

## Confirmed causes and correction

1. .NET 10 TotalProcessorTime opens individual process handles. Exact native
   access denial can affect processes whose resource counters are available
   through the supported read-only Process V2 counterset. A direct C# DEV
   probe found 94 denied non-Idle processes; all 94 had valid counter CPU and
   lifetime data. PowerShell property access had swallowed getter exceptions
   in an earlier supplementary probe: its zero-denial count is not accepted.
2. Both collectors discarded the whole process on a CPU read failure,
   including readable memory/name fields. They now retain those fields with
   CPU=null when recovery is unavailable, preserving genuine degrading reasons.
3. The new fixed four-counter reader validates type, 100ns timebase, CStatus,
   bounded buffers/items/names, unique PID and exact instance joins. It takes
   one sample without a deliberate delay. CPU access fallback is only for
   Win32 code5 with a matching fresh counter row; other errors are not hidden.
4. Recovered CPU, working set, label and lifetime come from the same counter
   row. They are not mixed with a separately enumerated generation's fields.
   A native creation FILETIME and an opaque performance lifetime have different
   clock tags. PID/generation/source changes or missing samples reset CPU
   baselines. Rates use the metric observation time, not pre-query wall time.
5. The performance lifetime has no validated UTC epoch. Recovered rows keep
   StartedAtUtc/path null. Only the explicit optional exact-identity capability
   is informational, and only for Partial process-services results without
   any other required-data failure. The limitation remains visible in all
   three UI languages; process actions still need separately verified exact
   identity and normal approval. Actual CPU/memory loss, other access denials,
   service metadata faults, unknown warnings, timeout and unavailable collectors
   continue to degrade health.
6. A process positively confirmed exited during a failed read is omitted.
   Denied/unknown exit state is never accepted as proof. CPU baselines are
   retained only for completed observations, including exit after CPU but
   before memory/name reads. No missing value is replaced with a fake zero.

This is a narrow forward specification change for recovered data plus optional
identity, not a blanket access-denial exception. Defaults and the installed
operator-selected Confirmation mode are unchanged.

## Fresh verification

Primary logs: `%LOCALAPPDATA%\HostAgent-DEV-evidence\desktop-localization-20261027`.
All accepted primary runs used Run-Guarded.ps1: start>768MiB, warning768,
emergency<=512, disk10GiB, cancellation of only the owned child. No guard
intervention occurred in the final runs.

| Verification | Actual result | Receipt directory suffix |
| --- | --- | --- |
| Original focused health/process suite | 53/53 | degraded-baseline-focused-3d013349b1274f86b71c481bcd35f430 |
| Read-only OS boundary refactor | build0 warnings/errors; focused53/53 | degraded-read-boundary-refactor-focused-fixed-test-12ba64e9c48849c19333e3b018f34031 |
| Readable-memory regression RED | 2failed/4passed: expected retained process, actual empty | degraded-partial-memory-behavior-red-1efdce2a16384974820d3b4cb612ed4c |
| Memory preservation GREEN | 59/59 | degraded-partial-memory-green-59d62066b94f4f96962c31df26f98717 |
| PID-generation RED | 2failed: expected CPU null, actual0 | degraded-pid-generation-red-1f615aa3203f478aa330388257bef59e |
| Counter recovery / identity health RED | 2failed/29passed: unrecovered access exception and old degrading code | degraded-counter-adapter-health-red-256f824d0a054732a6f64ddff6d9ec17 |
| Counter/health coverage GREEN | 98/98, including 12 reader cases | degraded-counter-recovery-focused-green-fbb5ca266eaf49e1966162d8d852e306 |
| UI localization RED | 3failed: unmapped technical code | degraded-identity-localization-red-77955d3df2c24dd5b48dd44cba792844 |
| UI localization/catalog GREEN | 18/18 (later wording clarified by review) | degraded-identity-localization-green-0c07ac3e043a41eca662401bc298abfc |
| Confirmed exit RED | 2failed: ended process incorrectly retained | degraded-confirmed-exit-red-55625c3fdac14ea694bd718967d35ea3 |
| Observation time / stage RED | 4failed: expected50%, actual100%; identity error mislabeled CPU | degraded-sample-time-and-stage-red-7d2593e97b744c778baf520646ee6b3e |
| Late exit baseline RED | CPU1failed/service1passed: old baseline retained | degraded-late-exit-baseline-red-b1c15c5fd8a74485a8f012ca51cce8f1 |
| Final focused GREEN | 106/106; min1480.7MiB | degraded-final-focused-green-d99acc3493fa47738483ea3591bba036 |
| Final Release build | exit0, 0 warnings/errors; min1381MiB | degraded-final-release-build-30f25fd2107f4b568de8cea6d6c5353e |
| Complete Release suite | **746/746**, Core62 + Infrastructure643 + Desktop41; 0 failed/skipped; 311.7s; min1314.1MiB/C:11.298GiB | degraded-final-release-suite-d41ea25eb9864a58a71c6eefce3c00db |

Exact primary command forms (SDK10.0.401/runtime10.0.12):

```powershell
dotnet build HostAgent.sln -c Release --no-restore -m:1 -p:UseSharedCompilation=false -nr:false
dotnet test tests/HostAgent.Infrastructure.Tests/HostAgent.Infrastructure.Tests.csproj -c Release --no-restore --filter <named focused classes above> -m:1 -p:UseSharedCompilation=false -nr:false -p:BuildProjectReferences=false
dotnet vstest tests/HostAgent.Core.Tests/bin/Release/net10.0/HostAgent.Core.Tests.dll tests/HostAgent.Infrastructure.Tests/bin/Release/net10.0-windows/HostAgent.Infrastructure.Tests.dll tests/HostAgent.Desktop.Tests/bin/Release/net10.0-windows/HostAgent.Desktop.Tests.dll
```

Support's preliminary counter-reader RED/GREEN runs were unguarded console-only
runs; no filesystem receipt/TRX exists for them. They are not described as
guarded verification. The initial missing-PID RED ran against an empty scaffold,
not a confirmed existing positional-join defect. The subsequent reported
malformed-instance parser defect was corrected; the authoritative primary
guarded 106 and 746 runs include all 12 counter-reader tests. Earlier wrong
Warnings/WarningCodes or fake-interface compile errors are setup failures,
not behavioral RED evidence.

## Safe live probes

Evidence source/outputs remain outside Git in
`degraded-fix-20261027-350ddd32a2ab4eb782a4341bfa7a8e31`.

- `degraded-raw-process-counter-feasibility-v2-9ffa46d91d4b4c02a4679cfa8d7c41b6`:
  direct C# getters, 238 counter rows, 94 non-Idle native5 denials and 94
  available counter CPU/lifetime rows, min1743.3MiB. The Process V2 English
  counter is Process ID, not the legacy ID Process. Earlier wrong-name probe
  returned NO_COUNTER and is not a successful recovery proof.
- `degraded-process-counter-timebase-check-72b7de9695e5495587c3a6c03c2e24dd`:
  timebase10M but raw elapsed marker did not match exact own creation FILETIME;
  no approximate timestamp is used in product identity.
- `degraded-live-own-child-confirmed-exit-56c84123b329416e90c136df03e2e507`:
  only the probe's bounded child was launched/exited; real product wrapper
  confirmed exit after CPU read failure. Exit0, no guard failure.
- `degraded-live-fixed-source-three-cycles-530657e37e104cf7acedb3889068ea8b`:
  actual current source collectors, three ordinary-user cycles. After warmup,
  CPU healthy in cycles2/3; valid memory and top10, 94 counter-only identities
  kept unknown. Overall user-context health still degraded because that token
  could not read some service startup/trigger metadata. Those faults were
  retained, not hidden. The previously inspected installed-service View had
  not shown these user-context metadata faults. **This is not service-token
  acceptance and not a global degraded=false claim.**

The first external DLL-only probe loaded the portable ServiceController stub
and failed before the live collector result. The private probe was corrected
to reference the actual Windows runtime asset; this was a fixture setup error,
not an installed-product failure. No raw process payload or secrets were
saved; accepted outputs contain only counts, codes and booleans.

## Review and remaining gate

Independent GitHub specialist source review found and helped reproduce the
observation-time error, incorrect stage attribution and late-exit baseline
retention. The final fixes were reviewed read-only; no source blocker remained.
Source/VM verification passes. A hash-pinned DEV update and actual installed
service-account health after at least two completed collection samples remain
required before declaring the installed degraded issue resolved. No current
1.2.9 runtime status is relabeled as fixed by the source tests.

Primary API references: [Process V2 and CPU normalization](https://learn.microsoft.com/en-us/windows/win32/perfctrs/collecting-performance-data),
[raw array contract](https://learn.microsoft.com/en-us/windows/win32/api/pdh/nf-pdh-pdhgetrawcounterarrayw),
[raw status/type handling](https://learn.microsoft.com/en-us/windows/win32/perfctrs/displaying-performance-data),
[elapsed counter semantics](https://learn.microsoft.com/en-us/previous-versions/ms938598%28v%3Dmsdn.10%29).

## DEV package and operator handoff — fresh follow-up

Source fix commit: `584020ff7e184a276886aad757f7a9d9eb53190e`, pushed by
fast-forward to `codex/desktop-localization`. The original working tree still
contains only the preserved historical final-acceptance-2026-10-25.md edit.
The package was built from a separate clean local DEV clone at exactly that
commit, without resetting or including the preserved edit.

Guarded `tools/Build-Release.ps1 -DevVm -Version 1.2.10` with the verified
10.0.401 SDK and WiX5.0.2 succeeded. The normal build includes artifact-only
MSI table validation and complete release file validation; no MSI execution.
Receipt `degraded-1-2-10-dev-package-build-da0e0a78c91c4f25aaed9ab7c22d9d90`:
exit0, no monitor failure, 164.3s, min1042.3MiB RAM and10.118GiB disk.

Private DEV package:
`HostAgent-DEV-releases/HostAgent-1.2.10-dev-584020f-r1`.

- Manifest1241 files, total543415513 bytes,
  SHA256 `0E3B9CC4BC61A1F18B5787891D669528B5A5CBE7533484AC3E9E3A09C6AC0748`.
- MSI103195628 bytes,
  SHA256 `81B454E84009136858D1CE4A0B244D03C28B867DAFC49465CBB02CF89B556477`.
- ZIP SHA256
  `F828353376B870200CBB9F29057550AF45163812FDEF970BC0E5D774CD2AB96B`.

Only standard NTFS compression was used to obtain installation headroom:
3067 older DEV binary files, 533 new candidate binary files and1256 binary
files in private ZIP-extraction fixtures. Every affected file's SHA256,
logical length and ACL was compared before/after and preserved. No file was
deleted, no database/SDK/installed product/source was compressed, and no WOF
or reparse compression was used. Guard receipts respectively:
`degraded-preserve-dev-artifact-headroom-2e76be2661ba428d9db3688226a8778d`,
`degraded-preserve-candidate-headroom-ed38888083684f7a96ab5bca457359b9`,
`degraded-preserve-extraction-fixtures-headroom-f0a054cb80384a558939da4facd573b8`.
All exit0, no monitor failure; free disk increased from10.118 to11.370GiB.
Individual preservation inventories are private evidence, not release assets.

The external handoff `HostAgent-DEV-releases/HostAgent-1.2.10-DEV-handoff`
contains a read-only-by-default entry point, filtered authenticated View
reader and unchanged protected configuration loader. Its manifest SHA256 is
`0B70E77814390B271CD8A7DB88FC7D57B6D894E8F609AD127F96FBD30C89053E`.
The entry point verifies exact old/new package identities, installed payload,
VM, account, resources and fixed service accounts/paths. Only explicit
operator `-Execute` invokes the existing standard DEV MSI installer. It never
configures a new mode, changes permissions, retries MSI, initiates a reboot,
or kills the MSI on a watchdog deadline. Configuration bytes and protected
configuration ACLs are checked before/after; settings/targets are preserved.
Only its own read-only View child can be cancelled by its bounded monitor.

Acceptance requires two distinct actual `Cpu.AtUtc` samples strictly newer
than the post-install anchor, fresh valid CPU/memory, required health
components and no degrading health. It keeps optional identity/SMART reasons
visible and does not suppress genuine failures. The wait limit is derived
from the unchanged collection interval: max180s or2*interval+120s (at the
supported3600s interval,7320s), not a600s cap that makes that interval fail.
Normal60s interval gives240s. No raw overview/process payload or database is
stored/opened by this handoff.

Fresh handoff verification:

- Guarded live read-only filtered View reader:
  `degraded-handoff-readonly-health-filter-f3bafd7b57084b1fa4f175348ca3c5fe`:
  exit0, actual ISO metric timestamps, existing1.2.9 degraded reasons preserved.
- Pure handoff syntax/parsing/acceptance validation20/20, no native actions:
  `degraded-handoff-final-pure-validation-65b2bfd0f47042dea3b462e904d62e1b`.
  Includes missing/stale/future/pre-update/nonfinite metrics, missing/duplicate
  components, mode/degrading errors and long interval bounds.
- Final read-only preflight:
  `degraded-final-dev-update-readonly-preflight-7990069dc86d45899f3e9c530fd66e7d`:
  `READ_ONLY_PACKAGE_AND_BASELINE_PASS_ADMIN_UPDATE_PENDING`, exit0,
  71.6s, min1164.0MiB RAM,11.370GiB disk against11.012GiB conservative
  installation reserve,1141 current1.2.9 critical files verified. HostAgent
  PID2360 and helperPID6780 remained Running; Confirmation/readOnly=false,
  kill switch=false, installed degraded=true with the known access reasons.
- Independent specialist static review verified all3 handoff lengths/hashes,
  corrected long-interval deadline and post-MSI protected ACL checks. No
  remaining static blocker reported. This is not an installed runtime PASS.

**Installed1.2.10 update and two-new-sample service-token health acceptance:
NOT RUN, operator elevation required.** The agent did not elevate, execute an
MSI, control services, change configuration or open the installed databases.
The successful source build746/746 suite and package checks do not relabel
the still-installed1.2.9 degradation. Published1.2.8 tag/assets, SignPath and
production host remain outside this change.

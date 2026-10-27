# Installed DEV action and database acceptance preparation — 2026-10-27

## Scope and current result

Preparation and non-elevated verification passed in AGENTPC, VirtualBox UUID
46543236-8C5E-49DA-953C-DB17F9B1E402, account AgentPC\AgentPCdevtest,
VBoxService Running. The installed product remains version 1.2.9 from
842378c5e2caa31b0a40c9502dbf7e990cb899b5. No MSI, native service operation,
permission change, configuration change or approval was executed by this
preparation. Production, immutable release tags/assets and SignPath are outside
this change. The pre-existing edit to final-acceptance-2026-10-25.md is preserved
and excluded from this change.

The owner's preceding mode receipt proved Confirmation/readOnly=false only.
At the preparation cutoff, actual installed remediation, exact linked journal
and fresh integrity of all three installed databases were **NOT RUN**. They
are closed by the later actual operator run documented below. Historical
integrity results are not reused as fresh
acceptance. The owner's “Funguje” remains owner-reported UI acceptance; this
preparation does not independently prove every installed language/persistence
combination.

## New tools and isolation

`Complete-InstalledDevAcceptance.ps1` defaults to a read-only plan. It verifies
VM/account, pinned handoff entries, the product manifest, installed critical
files and absence of unexpected critical files, and the two fixed product
services' account/path/start mode/state. `-Execute` additionally requires an
interactive elevated operator and protected Confirmation configuration. It
checks the actual collection interval (5..120 seconds) and the database tool's
read-only storage plan before creating any fault.

The only remediation target is a newly generated
`0HostAgentDevAcceptance_<GUID>` service with its own virtual account and
`C:\ProgramData\HostAgent-DEV-Acceptance-<GUID>` package/data. Creation is
Disabled; app/root allow service read/execute, data alone allows service writes.
The benign target has no executor, network or database access. Its own lifetime
is bounded to 15 minutes; it checks memory/disk and writes only lifecycle
metadata under its own data directory. Its initial start occurs after the
normal product configuration restart, so old persisted telemetry cannot prove
that it was Running.

The real View pipe must observe Running then Stopped, and the real diagnostic
engine must produce an exact-subject, fresh service_stopped incident with
confidence 88 and service.state_change evidence. No incident, diagnosis,
approval or installed journal row is inserted by the harness. The operator
must type APPLY to enroll the exact alias, type the exact request GUID in the
normal CLI approval flow, and type APPLY to remove that alias. Inputs are not
supplied by the harness. The helper must start the target with a different PID,
resolve the incident, and return fresh View Running evidence.

Cleanup retires only this target to demand-start before stopping it, to avoid
creating a new Auto-service outage during retirement. It observes Stopped,
disables and proves Stopped/Disabled/PID0 before removing the alias. An
unconfirmed stop/identity preserves the alias and failure evidence. Registration,
package and evidence are retained; no previous test artifacts are deleted.
Product services are restarted only through the supported interactive CLI and
the explicitly selected database snapshot stage; their accounts, security and
monitoring configuration are not rewritten by this harness.

`Read-InstalledDatabaseSnapshot.ps1` has a separate read-only default. Its
operator stage gracefully stops the two fixed services, rechecks bounded actual
database/sidecar sizes, copies with hash verification, then opens only the
copies through Python stdin (`-I -B -`, not `-c`) with mode=ro/query_only.
It checks quick_check and the actual product schema for host-agent,
notifications and helper-journal. Empty/lazy missing journal is NOT RUN,
never an integrity PASS. Recovery is attempted in finally, helper before
monitor, and monitor recovery is attempted even if helper recovery fails.
Source SQL writes are never requested; ordinary service shutdown may checkpoint
the source databases. SQLite may coordinate sidecars in the private copies.

Journal proof requires the exact request/incident/digest/operation, alias and
SCM target, matching claim/approval ID, exactly one approval/claim and ordered
Approval → Proposed → Started → succeeded Ended events. Only bounded counts,
codes, GUIDs and hashes are returned, no database contents or secrets.

## Fresh test evidence

All logs below are under `%LOCALAPPDATA%\HostAgent-DEV-evidence\desktop-localization-20261027`.
Guard policy: initial >768 MiB; warning 768; emergency <=512; disk 10 GiB.
No guard intervention occurred in these final accepted runs.

| Command / check | Fresh result | Evidence directory |
| --- | --- | --- |
| `dotnet build HostAgent.sln -c Release --no-restore -m:1 -p:UseSharedCompilation=false -nr:false` (SDK 10.0.401) | exit 0, 0 warnings/errors; minimum 1747.4 MiB | final-gates-complete-release-build-8134b1f5ec2e40dea800cb9591bb907b |
| `dotnet vstest tests/HostAgent.Core.Tests/bin/Release/net10.0/HostAgent.Core.Tests.dll tests/HostAgent.Infrastructure.Tests/bin/Release/net10.0-windows/HostAgent.Infrastructure.Tests.dll tests/HostAgent.Desktop.Tests/bin/Release/net10.0-windows/HostAgent.Desktop.Tests.dll` | **690/690**, 0 skipped/failed; Core62, Infrastructure590, Desktop38; 278.2s; min991.7 MiB, C:12.142 GiB | final-gates-complete-release-suite-18e78b22ef27451da6669680fd99a63c |
| Focused InstalledActionViewTests, InstalledDatabaseSchemaTests, InstalledDatabaseRecoveryTests, ProductServiceLifecycleTests | 24/24, exit0; min924 MiB | final-acceptance-focused-final-2271167bdc31400a85f4ef78032f7425 |
| `pwsh -NoProfile -NonInteractive -File tests/diagnostics/Test-InstalledActionAcceptance.ps1` | PASS scoped receipts, real controller finally branches and constructed ACL descriptors; native_operations=0; min1300.4 MiB | final-controller-recheck-671ba16644294c7789edcf13ba37c5cb |
| `pwsh -NoProfile -NonInteractive -File tests/diagnostics/Test-InstalledDatabaseSnapshot.ps1` | PASS 36 assertions, real SQLite fixtures, recovery branches; min1101 MiB | final-database-recheck-548b5c8f4b4243c8943954bebca9094a |
| `pwsh -NoProfile -NonInteractive -File tests/diagnostics/Test-DevActionTarget.ps1` | PASS compile, invalid arguments, actual private-temp ACL rejection and distinct lifecycle markers; no SCM | final-acceptance-target-final-tests-a4c8993477d1445f89050eab588b18c9 |
| Self-contained PipeProbe publish | exit0, min1480.5 MiB, 23.2s | final-acceptance-probe-publish-5b506e54922f454fba2275eab34ed542 |
| Final target Framework csc compile under guard | exit0, min1854.6 MiB | final-acceptance-target-guarded-build-9a47794c7a4e42f990043cb0ed71b50c |

InstalledDatabaseSchemaTests creates private databases through the three actual
product stores and invokes the diagnostic SQL against those fixtures. This
establishes schema compatibility, not installed integrity or a native action.

## Test-first findings fixed before the operator gate

- View parsing initially rejected the actual product's PascalCase payload.
  Actual-product serialization regression failed (1 failed/7 passed), then
  passed 8/8 after exact-single case-insensitive payload field lookup. Envelope
  validation, exact target, mode and time checks remain strict. Evidence:
  final-action-actual-view-contract-red-4d5b999a28c247269055451a04d3c698,
  final-action-actual-view-contract-green-43d00d0d749141d7b717c7f9aafed346.
- Target ACL validation used composite Modify/FullControl flags that include
  read bits; a legitimate RX target was therefore rejected. Actual private ACL
  regression reproduced it; primitive write/delete/ACL-change masks fixed it.
- Reusing fixed lifecycle filenames failed a second target start. Private
  repeated-lifecycle regression now proves four distinct preserved phase files.
- Database harness guard/empty inventory and pending-helper recovery defects
  were reproduced by fixture tests. The final tests prove monitor restoration
  is still attempted after helper failure and both running services are hash
  checked.
- Journal target aliases differ from SCM service names. Regression binds both
  explicitly and validates claim target_key/ParametersJson and approval_id.
- Setup/compiler/missing-source failures encountered while constructing tests
  are setup failures, not claimed behavioral red runs. A preliminary unguarded
  target compile was preserved separately; the accepted final binary was rebuilt
  under the guard as recorded above.

These are acceptance-tool defects; no production C# implementation or installed
binary was changed by this packet. Source-only independent Support review
found no remaining blocker in the reviewed delta. Exact copied bundle hashes
and default preflight are recorded separately after preparation, not assumed.

## Pinned operator bundle and default preflight

Bundle (outside Git):
`HostAgent-DEV-releases\HostAgent-1.2.9-final-acceptance-20261027-r1`.
`handoff-manifest.json` contains 529 file entries; SHA-256:
`6BB2AFC3BFA3BB49A3CC8A11FA0C21A4DC5B7D433049C5C25D79550E81D40DD5`.
The benign target executable SHA-256 is
`0E21DD855CD232C6CB27D2AC8260A0265E783E7F8933D41839BA3C4AC46635B2`.
The manifest explicitly records that tool files were prepared from the reviewed
working tree based on 2fb024e25e0cd3720fef76d37f322f8ca794ba19; it does not
misidentify that base as the commit containing the new tools. Individual tool
bytes are hash-bound, and the product pin remains 842378c.

Default command was actually run with the exact manifest hash above and
**without -Execute**, under Run-Guarded.ps1 (240-second outer deadline):

```powershell
pwsh -NoProfile -NonInteractive -File <bundle>/Complete-InstalledDevAcceptance.ps1 -ManifestPath <bundle>/handoff-manifest.json -ExpectedManifestSha256 6BB2AFC3BFA3BB49A3CC8A11FA0C21A4DC5B7D433049C5C25D79550E81D40DD5
```

Receipt `final-bundle-readonly-preflight-55c248084b4242fb9fffb0aab527f235`:
exit0, READ_ONLY_PLAN_READY_ADMIN_RUN_PENDING, no SCM operations, 1141 installed
critical files verified, HostAgent Running7460 and Helper Running9052, no
monitor failure, minimum795.6 MiB/C:12.261 GiB, elapsed162.3s. Planned GUID
d1d204c4fcaa4cc1bb1aca90cc522b27 was not created. This default plan does not
claim protected configuration or database access available only with elevation.

The GitHub specialist independently verified all 529 entries, no extra files,
four byte-identical repository script copies, the exact product pin and the
reviewed cleanup/linkage contracts. No artifact/source blocker was reported;
this independent review did not execute the native gate. Preparation source,
tests and this evidence were committed as
54526de62e47b211fbf348406e3a6893f418e6d4. The bundle's recorded working-tree
base and per-file hashes remain unchanged; no product binary was rebuilt after
the installed pin was selected.

Support additionally created a journal through the actual ApprovalIssuer,
SqliteHelperJournal and HelperDispatcher on private temporary data, replacing
only IOperationExecutor with a no-op boundary. One execution returned Succeeded;
the real diagnostic SQL returned quickCheck=ok, schemaValid=true,
journalLinkage=PASS. Approval, claim, approval audit, proposed, started, ended,
succeeded and endedAnyCode each counted one; sequenceValid=true. Actual request
serialization digest and alias/SCM binding matched; DB hash before/after probe
was identical. This is a private integration proof, **not native or installed
action acceptance**.

Evidence root:
`completion-20261027\journal-linkage-supplement-e8312273660c44d3a6d498cb4620ad0b`,
proof-result.json SHA-256
`369F652CEDF8ADB7744BAEB4EE351A8F3AF0E1615E72B494BB9DD0CFCC2D83A3`.
Guarded bridge build: exit0, no warnings/errors, min1172.8 MiB;
`journal-linkage-bridge-build-sdk-5ec60cebd3884a3aa4215071eaefc6d2`.
Guarded probe: exit0, min1356.5 MiB;
`journal-linkage-probe-execute-r2-07ba3d9a67eb4178b9fcf966a78292fa`.
Earlier external bridge compiler/native-library setup failures are preserved
and are not relabeled as product defects or successful native runs.

## Remaining gates / limitations

1. Operator-only installed target action plus linked journal and three-DB
   integrity: **closed by the native run below**. The preparation above retains
   its historical NOT RUN labels; they are not source-test claims of native
   success. Current runtime degraded=true is explained at limited reason-code
   level by the authenticated View read below; per-process visibility remains
   partial and is not reported as repaired.
2. The gate has bounded read probes and target lifetime, but an operator may
   leave an interactive CLI prompt waiting. Do not close/kill it during APPLY;
   finish or cancel its prompt and preserve the receipt.
3. Service restoration is attempted despite a resource guard failure. Native
   Start-Service itself is synchronous; the following state observation has a
   deadline but is not a kernel-level service-start cancellation guarantee.
4. A fresh quick_check proves structural SQLite integrity, not semantic truth
   of every historical incident or all future crash/power-loss outcomes.
5. No new installation is justified by these test-tool-only changes. The
   existing pinned 1.2.9 product passed the native gate below. This does not
   establish unrestricted automation, every remediation operation, production
   deployment readiness or signing. Signing is separate.

## Actual operator-run installed acceptance — 2026-10-27

The owner supplied the complete JSON for run
`c85991b5883e4fc1b24988a80e03e38a`. The primary independently read the saved
`final-installed-acceptance-c85991b5883e4fc1b24988a80e03e38a/result.json`
under the current account's DEV evidence directory. SHA-256:
`18F2F8214DCADB3414E379FC933B3ECB76BEF372B28D132BA4F16F9208F1C49E`.
It agrees with the supplied result. The operator, not the primary's
non-elevated process, executed the native steps and exact approval.

| Gate | Fresh native result |
| --- | --- |
| Overall receipt | INSTALLED_DEV_ACTION_AND_INTEGRITY_PASS; failureCode/failureStage null |
| Installed identity | packageVerified=true; 1141 critical files verified; pinned 1.2.9 |
| Real evidence | target Running observed 07:31:26.2917756+01:00, Stopped observed 07:32:19.2039768+01:00; collection interval60s; confidence88 |
| Exact action | StartService for devaccept-c85991b5883e4fc1b24988a80e03e38a; target PID5404 before stop, PID7604 after approved helper start |
| Request binding | request e7815c14-c699-4f70-b3d3-fd12f73a1d6f; incident508786e8404c4c36a780f90f9056e0dd; digestEC9761D197521E68270800D1A8D060D85244457DAA19593B6A599DD211C44BED |
| Journal | PASS; approval/claim/approvalAudit/proposed/started/ended/succeeded/endedAnyCode each1; sequenceValid=true |
| Cleanup | PASS_target_stopped_disabled_alias_removed_all_evidence_preserved; cleanupFailures empty |
| Mode | preserved Confirmation, readOnly=false; configuration changes not requested by DB stage |
| Service recovery | both originally Running, both stop requested, both restored Running; HostAgent2360, Helper6780 |
| Resources | no low-memory warning; main minimum1289.1MiB; ending2088.6MiB; floors unchanged start>768/warn768/emergency512/disk10GiB |

The helper-journal SQL link to this request was explicitly tested. The
host-agent/notifications JournalLinkage=NOTRUN fields are expected: they are
not the helper approval journal and their structural/schema checks passed.

| Database copy | Source bytes recorded | Copy hash verified | quick_check | Schema |
| --- | --- | --- | --- | --- |
| host-agent | 822276096 | true | ok | valid |
| notifications | 135168 | true | ok | valid |
| helper-journal | 65536 | true | ok | valid |

Snapshot evidence remains at
`installed-db-snapshot-b1e0f35eead947c8922606f36be49e27`. The result reports
DatabaseSourceOpenedBySqlite=false, ConfigurationPerformed=false and
ModePreserved=true. SQLite read only preserved copies; the ordinary graceful
shutdown is allowed to checkpoint source databases. No extra database check,
source SQL write or repair was performed by the primary after receipt review.

A fresh primary read-only CIM check confirmed HostAgent Running/Auto/PID2360,
Helper Running/Auto/PID6780, and exactly this test target Stopped/Disabled/PID0.
The approved VM UUID/model/account/VBoxService were freshly reverified. The
pre-existing historical documentation edit remains unmodified.

The preceding run `badc469d343b4671b0a9e060a04471dc` is separately preserved
as FAIL_OR_INCOMPLETE: the operator entered Start at the exact-GUID prompt,
approval_cancelled was reported, and the harness eventually timed out waiting
for the unapproved start. Its cleanup passed; it is not relabeled as a
successful action. The successful run above used a new request and GUID.

The successful receipt still reports initialDegraded=true and final
degraded=true. That flag is **not resolved by the database PASS**. A fresh
primary read-only CLI `status --verbose` attempt returned administrator_required
(exit1; no mutation), evidence
`installed-final-native-pass-health-129fe3b014944a95beafc261e19a1ab6`.
Further limited health reason evidence must be obtained through the supported
read-only View path or the operator's elevated CLI; permissions/configuration
must not be silently changed to hide this limitation. No build/test rerun was
needed for this documentation-only receipt update; the earlier recorded
690/690 source suite is not misrepresented as a new run.

### Follow-up read-only View health reasons

Support used the existing Release ViewPipeClient with authenticated server
identity on HostAgent.View.v1 and filtered the received overview in memory.
The successful call returned Confirmation/readOnly=false/degraded=true and
only bounded component codes/reasons were saved; no raw overview, process list,
database payload or secrets were stored. No elevation, configuration, service,
ACL or direct database operation was performed.

| Component | Code | Limited reasons | Degraded |
| --- | --- | --- | --- |
| collectors | collectors_partial | collectors_partial | true |
| cpu-memory | collector_partial | some_process_metrics_access_denied, process_cpu_time_access_denied | true |
| process-services | collector_partial | some_process_metrics_access_denied, process_cpu_time_access_denied, some_executable_paths_unavailable | true |
| disk | optional_smart_unavailable | smart_health_unavailable | false |

Administration, collection_worker, diagnostics, event-log, network, publication
and system were healthy/nondegraded in this response. Aggregate degraded is
therefore associated with limited process telemetry/access/path visibility in
the received health state; optional SMART is not its cause. This does not
identify the affected PIDs or prove that all unavailable paths have the same
cause. No permission escalation or suppression of partial status was attempted.
Per-process CPU/path visibility remains an explicit operational limitation;
these values must remain unavailable rather than zero or fabricated.

Filtered artifact:
`bounded-view-health-c85991b5883e4fc1b24988a80e03e38a/filtered-health.json`,
SHA-256 `6AAC3CC97AAE62B428D2055792FB2E815F14D48B5EA5071827E923C52EEED073`.
Guard receipt `view-health-filtered-read-r4-dbc25e82c83b42418a1211fc3c713cab`:
exit0, monitorFailure null, minimum987.8MiB/C:11.475GiB. The primary read the
saved bounded artifact and independently verified its SHA-256.

The filtered generatedAtUtc is null: ConvertFrom-Json materialized the timestamp
as DateTime while the external filter accepted only a string. This is a
diagnostic-filter limitation; no claim of timestamp freshness is made from
that field, and no additional request was issued to fill it. Earlier filter
attempts failed locally on ValueTask/byte parsing, not proof of a failed
product View transport. The successful authenticated read supplies reasons
without needing another elevated CLI request from the operator. Full product
or production readiness is not inferred from this single health read.

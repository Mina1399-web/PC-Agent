# SignPath submission preparation — current continuation

This continues [the earlier checkpoint](signpath-readiness-2026-10-25.md).
Historical results there remain tied to their original commits and binary-only
review workflow. No application, legal consent or signing request has been sent.

## Scope and requirements

The source base is `c1653468531e3cfd4f1ce5a636d277a8f75180fa` on
`codex/signpath-submission-prep`. Preparation adds a separate GitHub-hosted
unsigned MSI candidate path; it preserves the original exact-VM and
certificate gates. Candidate preparation performs no MSI installation,
service control, trust change or production-host operation.

| Gate | Current evidence / remaining action |
| --- | --- |
| Open source and component provenance | MIT first-party source; dated mapping and fresh candidate inventory generated/validated in successful CI. |
| Maintained project | Current source and preserved development history are public; provider reputation assessment remains external. |
| Released in intended form and download description | Functionality/download page and release text prepared. Public MSI release and native candidate acceptance remain pending. |
| Owned signing scope and metadata | 17 exact owned PE paths and product/version checks passed in CI; upstream runtime/SQLite/DTF excluded from owned signing. |
| Privacy and announced changes | Outbound defaults off; operator opt-in preserved. Full disclosure/plan reader and explicit preview MSI acknowledgement prepared. Provider must assess conditional native-UI requirement. |
| Uninstall and retention | Existing MSI transaction/removal design preserved; current native preview install/removal not executed. |
| Team MFA and roles | Proposed owner roles documented; actual account verification is owner-only. |
| Home/download signing policy | Current source pages link policy/privacy; default GitHub landing branch still requires owner administration. |
| Hosted origin and artifact | New MSI workflow 40230180663 succeeded; artifact/run/source linkage independently confirmed. SignPath connector still requires onboarding. |
| Provider configuration/manual approval | Owned-stage configuration generated with candidate; composite DTF treatment and final signing stages await provider acceptance. |

Sources: [Foundation conditions](https://signpath.org/terms.html),
[trusted GitHub builds](https://docs.signpath.io/trusted-build-systems/github),
[artifact configuration examples](https://docs.signpath.io/artifact-configuration/examples).
These conditions do not guarantee admission or replace provider decisions.

## Fresh preparation checks

- Approved DEV VM identity and account were rechecked directly; branch and
  working tree were clean before this continuation. Changes are confined to
  source, test harnesses and documentation.
- Lossless NTFS compression of fourteen named older source-external release
  directories preserved every file and its SHA-256. No files were deleted.
  C: free space increased from 14.86 GiB to 16.29 GiB, retaining the 15 GiB
  local build/test floor. The installed product and repository were not
  compression targets.
- Dependency generator: meaningful red on unimplemented behavior, followed by
  identical-command green **8/8**, exit 0; watchdog did not intervene, minimum
  RAM 1,231.2 MiB and C: 15.410 GiB.
- Data-only manifest reader: red on unimplemented behavior, then **39/39**
  green, exit 0; external SHA checked before parsing, duplicate/path/schema
  and size-limit cases included; minimum RAM 909.1 MiB, C: 15.704 GiB.
- Shared MSI source/preview-table contract: red
  `preview_requires_explicit_ack`, then **11 synthetic groups PASS**, exit 0;
  minimum RAM 1,093.4 MiB, C: 15.515 GiB. This establishes XML/table-rule
  behavior on synthetic fixtures, not a native MSI installation.
- Hosted-candidate refusal guard: red `submission_not_implemented`, then
  **4 checks PASS**, exit 0; unapproved environment rejected before output or
  tools execute; minimum RAM 1,203.6 MiB, C: 16.293 GiB.
- PowerShell parser check over tooling passed. The pinned upload-artifact
  action's actual `action.yml` confirms `archive: false` uploads one file
  directly, preserving the candidate ZIP's root for signing configuration.
- A local Release solution build was stopped at 767.4 MiB free RAM by the
  unchanged 768 MiB soft watchdog (96.9 seconds). It is incomplete, not a
  build PASS. The subsequent memory-conserving attempt was refused by its
  initial resource guard before build; no thresholds were lowered.
- A complete three-assembly Release `dotnet vstest` attempt on existing
  assemblies was also stopped by the soft watchdog at 757.7 MiB (140.6
  seconds); no completed suite result is claimed. C# source/test trees are
  unchanged from the prior verified base, but that does not turn an
  interrupted run into a PASS. Both installed services remained Running with
  their previously observed PIDs after these tool-only attempts.
- A local optional YAML-parser check could not run because that Python
  environment lacks PyYAML. No dependency was installed for it; actual
  GitHub workflow parsing/execution remains the relevant integration gate.
- Initial independent read-only source review found no actionable P1/P2
  defect in inspected guards/MSI/signing scope, and required final docs to
  reflect the new MSI scope. Exact final candidate/evidence review is pending.

## Candidate and local integration results

The first MSI-workflow run
[40229564002](https://github.com/Mina1399-web/PC-Agent/actions/runs/40229564002),
source `51e066f68bd1b3e261960a6f24467b6fc2eeeb22`, passed the 62 new synthetic
checks (8 inventory + 39 manifest + 11 MSI-source groups + 4 host-refusal
checks), Release build, Core 62/62 and filtered Infrastructure 449/449.
Desktop produced 13 PASS and 2 FAIL, both `MaintenanceUiTests` constructing
real `UserMaintenance` under the elevated hosted runner token. The normal-user
guard rejected that token with `maintenance_scope`, as designed. The workflow
stopped; no MSI candidate/archive was built or uploaded. The hosted Desktop
filter now excludes precisely that two-test class; the full 15-test class set
remains required in the non-elevated DEV VM suite. No product guard or test was
weakened. The workflow also logs installed runtime inventory to distinguish
framework-dependent test runtimes from the self-contained 10.0.12 payload.

The corrected workflow
[40230180663](https://github.com/Mina1399-web/PC-Agent/actions/runs/40230180663)
completed **SUCCESS** at source
`34fe3baf0b37b9cda1db1a2ac95c34f854efbaba`:

- Windows Server 2025 GitHub-hosted runner; SDK 10.0.401; compiler 0 warnings
  and 0 errors. Runtime inventory was printed. Host was 10.0.13 and shared
  runtimes included both 10.0.12 and 10.0.13; tests were not forced to the
  exact 10.0.12 self-contained payload patch.
- All 62 preparation checks passed; Core **62/62**, filtered Infrastructure
  **449/449**, filtered Desktop **13/13**, with 0 failed/skipped in each run.
  The documented six Infrastructure classes and two Desktop maintenance
  cases remain in the complete DEV suite. No complete current DEV suite is
  claimed.
- Pinned DTF nupkg hashes/NuGet signatures and immutable WiX source archive
  hash passed. A fresh metadata-only inventory from restored assets and
  published dependencies was included. This is separate from the earlier
  observed 49-package local graph; no uninspected new graph count is invented.
- Custom-action compilation, DTF assembly and WiX MSI build completed.
  Directory and ZIP validation both returned PASS: **1,230 manifested files**,
  **17 exact owned PE entries**, metadata, complete lengths/hashes, notices,
  source archive, MSI identities/transaction/acknowledgement and exact
  installed-path table membership. Inspection used database tables only.
- Final receipt: `PREPARED_NATIVE_ACCEPTANCE_PENDING`,
  `signingPerformed=false`, `installerExecuted=false`.

| Artifact | SHA-256 |
| --- | --- |
| ZIP, 322,777,962 bytes | `722CAA759CD0E249C8F27549849148F524F1248DF05939AEAA901B417E566E9A` |
| `HostAgent.msi` | `78241E9B8EE79C9B9CFFD89D4F1634616CF946BC18644010162B5750F29A2272` |
| `manifest.json` | `444D696AC0C879A1446A8FC85D8C62DA6AD6D15878B1FED8FC1F63C360189E93` |

[Candidate artifact 12612639952](https://github.com/Mina1399-web/PC-Agent/actions/runs/40230180663/artifacts/12612639952)
expires 2026-11-08. Its API size/digest and source/run linkage independently
match the builder receipt. Test reports were uploaded as artifact
`12612891030`, SHA-256
`3947baa10b8bcad55b497e4138a1d135517198338fd05338dbf9047291b5bae3`.
The setup action logged a 409 warning for its attempted latest-runtime
download but then installed the pinned SDK successfully; that warning did
not fail the tests/build.

A fresh read-only reviewer confirmed the source diff, run/job logs, artifact
API linkage and these scopes, with no actionable P1/P2 finding. The reviewer
did not locally download/rehash MSI/manifest bytes or certify native
acceptance or SignPath eligibility.

The separate candidate reference
[`codex/signpath-candidate-1.2.8`](https://github.com/Mina1399-web/PC-Agent/tree/codex/signpath-candidate-1.2.8)
was created and re-read through the GitHub API at exact source `34fe3baf...`.
It preserves source for an owner-created release; it is not an enforced
branch-protection claim or an existing public binary release.

## Evidence and limits

Detailed test stdout/stderr and watchdog receipts are retained outside source
under the DEV user's `HostAgent-DEV-evidence/signpath-submission-20261025`.
The source archive pin was computed by streaming the official immutable WiX
archive: 12,377,495 bytes; SHA-256
`AC593BF21864D9AD4B44720A702DAE3651A51D199CFD5AEE080AAA1286AB72F1`.
The hosted builder must verify those bytes before distribution.

The public application embed was identified on the official apply page, but
the browser control surface failed to attach it. A subsequently opened Chrome
window could not be controlled because current-page identification was
insufficient for policy enforcement; browser input stopped. No form fields,
settings, account or release were changed. An alternative non-interactive
existing-Git-auth API probe was rejected **before execution** by automatic
approval review (`blocked by policy`); no credentials were read. That method
was not retried or disguised.

Local candidate staging was refused by its resource guard before downloading
or creating the target directory. Later free RAM was only 559.9 MiB; the owner
must close unneeded applications before local checks. The CI artifact is
available, but no locally extracted candidate/path/hash result is claimed.
The local handoff contains source-check orchestration, exact CI checksum
sums, release text and owner instructions; it contains no installer bytes.

Final documentation-only review: `git diff --check` passed and all ten
changed/new documentation/example files passed local Markdown-link and known
secret-pattern checks. A subsequent read showed 460.1 MiB available RAM;
no further local build, test, extraction or installation was attempted.
Independent final handoff review found no P1/P2 issue in its bounded static
scope. A P3 wording ambiguity was corrected: full Infrastructure tests open
isolated SQLite fixtures; the orchestration does not request opening installed
product databases. Tool paths were confirmed present without executing them.

The [owner checklist](../operations/signpath-owner-steps.md) lists resource
recovery/full VM suite, local candidate verification/native acceptance,
default-branch administration, public release, MFA/roles/contact/consent and
provider decisions. The [post-admission design](../signing/after-admission.md)
is inactive and does not represent a tested signer.

**Status: independently executable preparation and hosted candidate checks
completed within the recorded scope. Submission remains gated on listed
owner/provider work; no admission, native acceptance, public release or
signature is claimed.**

## Operator source verification and harness follow-up

The operator's newly supplied console output records a Release build with
0 warnings/0 errors and Core62/62 PASS. Build receipt
`operator-release-build-53be94ba00ae4b27ad3feeb73cab916d`: exit0,
monitorFailure=null, minimumRAM1145.9MiB/disk15.784GiB. Core receipt
`operator-release-core-2ce7a4e96cd6468796f753f66b27effb`: exit0,
monitorFailure=null, minimumRAM1324.5MiB/disk15.790GiB.

That same run's full Infrastructure project returned559PASS/3FAIL/0skipped
of562; Desktop was NOT RUN because the runner stopped on the failure.
Receipt `operator-release-infrastructure-621d68cfee694cb0894cfb1a06f00b0c`:
exit1, monitorFailure=null, minimumRAM1262.4MiB/disk15.644GiB.
These operator-supplied results are separate from agent-executed follow-ups.

All three failures were `ArgumentNullException(path1)` before application
startup: `ColdAdminPipeTests.cs:17` (two cases) and
`OperatorHostingTests.cs:19` read `Environment.GetEnvironmentVariable("DOTNET_ROOT")`
and combine the result with `dotnet.exe`. The portable operator harness did
not set that variable. This finding is test-environment orchestration, not
evidence of a product failure.

Actual agent-executed focused reproduction used the same Release assemblies,
approved DEV VM/non-elevated context, SDK10.0.401 with runtime10.0.12,
`DOTNET_ROOT`/`DOTNET_ROOT_X64` absent and:

```powershell
dotnet test tests/HostAgent.Infrastructure.Tests/HostAgent.Infrastructure.Tests.csproj --configuration Release --no-build --no-restore -m:1 --filter "FullyQualifiedName~ColdAdminPipeTests|FullyQualifiedName~OperatorHostingTests.Actual_cli_process_shows_help_without_installation_or_elevation" --logger "console;verbosity=minimal"
```

Here `dotnet` denotes the explicitly addressed, hash-verified portable SDK,
invoked through the unchanged memory/disk watchdog. RED:3FAIL/0PASS, same
null-path exception, receipt `dotnet-root-red-bf39590796b4465c88d06fee5a4d1d68`,
exit1/null monitor/minimumRAM1282.2MiB/disk15.775GiB/56.5s. GREEN: identical
command/filter with both roots set to the verified SDK directory,3PASS/0FAIL/
0skipped, receipt `dotnet-root-green-1d0591d5c9664bf8abc4bad0927bbbd6`,
exit0/null monitor/minimumRAM966.6MiB/disk15.773GiB/37.1s.

The source-external operator runner now sets both roots only in its process
and restores the previous values in `finally`. Its PATH/Git setup remains
process-local and checked. Independent file-only review found no actionable
issue in that setup/restoration and confirmed full project calls still have
no test filters. No product/test source was changed and no test was removed.
Fresh complete Infrastructure/Desktop results remain pending at this entry;
focused3PASS alone is not complete suite success. No installation or signing
was performed by this correction.

### Subsequent unfiltered failure: lifecycle-test cleanup

The agent's full Infrastructure rerun completed561PASS/1FAIL/0skipped of562,
exit1, monitorFailure=null, minimumRAM923.1MiB/disk15.621GiB/569.0s.
Receipt: `rootfix-full-infrastructure-3c4d62ec41e747028ccdd1a60205abe5`.
All three formerly null-runtime cases passed within that full run. Desktop
remained NOT RUN because the controller stopped after Infrastructure failed.

The remaining failure was an IOException deleting the isolated fixture's
`mode-<GUID>.db` in `OperatingModeTests.Fixture.DisposeAsync`, following
`Slow_notification_does_not_delay_heartbeat_worker_or_service_shutdown`.
The full failure is retained; no retry changed it to PASS. A subsequent
single-test detailed run passed1/1, receipt
`lifecycle-lock-repro-0505456119df4ba7b06039eeb7868337`, exit0/null monitor,
minimumRAM1384.9MiB/disk15.752GiB/14.4s. That does not close the full-suite gate.

Independent source review established a test cleanup defect: readiness
assertions could bypass awaited `host.StopAsync`, and its two-second
`WaitAsync` could abandon an ongoing shutdown. Synchronous host disposal
then did not join worker cleanup before deleting SQLite fixtures, potentially
masking the original assertion/timeout. The host contract allows15s and an
individual worker may drain for2s. The specific original trigger is unproven;
this result alone does not establish a product deadlock. Deterministic
test-harness regression/correction and subsequent full verification remain
open at this entry. No product-source fix or candidate acceptance is claimed.

### 2026-10-26 test-only correction

Added `TestHostLifetime` in the test project and three deterministic tests:
a real Generic Host holds a private GUID file until explicitly released
cooperative cancellation cleanup; the original synthetic assertion is
re-thrown only after cleanup; stop/dispose failures are aggregated without
masking the primary failure; observed successful stop remains distinguished
from disposal failure. No real installed service or database is targeted.

The decisive RED used a happy-path-only helper and produced1PASS/2FAIL,
exit1/null monitor/minimumRAM1461.9MiB/disk15.748GiB/12.6s, receipt
`lifetime-helper-red-8fbceb7a22dd4ff4920b37069eef138d`. GREEN on the same
command/filter produced3PASS/0FAIL, exit0/null monitor/minimumRAM1717.1MiB/
disk15.750GiB/11.9s, receipt
`lifetime-helper-green-15348d5a97d84127b0f57cd9a7bc7463`. An earlier fixture
setup mistake used an externally owned worker registration and timed out;
receipt `f811f1f4c7cd49b3922a48ab1a0ca011` is retained but is not the decisive
RED. Both decisive runs used `dotnet test` Release/no-restore/single MSBuild
node/no shared compiler, filter `FullyQualifiedName~TestHostLifetimeTests`.

The existing slow-notification test now uses that lifetime, a signalled
readiness deadline aligned with the15s host contract, explicit assertions
that the sender is still blocked while the heartbeat has occurred and is
finished after shutdown, and DB cleanup only after observed stop. If stop is
unconfirmed the isolated DBs are retained and their paths reported. Multiple
assertion/cleanup failures remain visible together. This replaces scheduler
races and error masking, not product timeouts or authorization controls.

Actual integrated Release compilation/focused test command:

```powershell
dotnet test tests/HostAgent.Infrastructure.Tests/HostAgent.Infrastructure.Tests.csproj --configuration Release --no-restore -m:1 -nr:false -p:UseSharedCompilation=false --filter "FullyQualifiedName~ServiceLifecycleTests|FullyQualifiedName~TestHostLifetimeTests" --logger "console;verbosity=minimal"
```

Result14/14PASS,0failed/0skipped, exit0/null monitor, minimumRAM1571.9MiB/
disk15.749GiB/32.3s, receipt
`lifecycle-integrated-focused-77a045e1cfa64be3ac7e030056c96063`. The same
verified portable SDK/watchdog and process-local ROOT/X64/network-off context
were used. Product `src` and existing security/runtime guards are unchanged.
The previous full-suite failure remains recorded; fresh complete Release
build/project suites are required after this test-source correction. A fresh
hosted candidate must retain its own source/artifact anchors rather than
relabel the preserved34fe candidate as a new-source build.

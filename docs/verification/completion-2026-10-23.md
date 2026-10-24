# Completion continuation — 2026-10-23

This extends [the previous verification record](completion-2026-10-22.md).
Prior results retain their original dates and scope. Nothing here authorizes
production deployment, a mode change, or unattended remediation.

## Fresh environment and preserved state

Execution was inside AGENTPC / AgentPC\AgentPCdevtest, VirtualBox,
SMBIOS UUID `46543236-8C5E-49DA-953C-DB17F9B1E402`, VBoxService Running.
Initial free RAM was 2121.3 MiB. The branch was
`codex/installed-telemetry-stabilization`, HEAD
`c90e31d258571ab444d5317edb6b1abd6de03a20`, with a clean working tree.
Before changing the installer, a separate hashed source copy was retained
under the account-local completion evidence directory. No existing artifact,
commit, snapshot, installed service, configuration or database was replaced.

All runs below used the existing independent 250 ms monitor: start strictly
above 768 MiB, warning below 768 MiB, emergency abort at or below 512 MiB,
15 GiB disk floor, and recovery above 768 MiB within 30 seconds. The parent
checked both protected services' state, PID, account and image path afterward.
No service control or native MSI execution was performed.

Evidence root:
`C:\Users\AgentPCdevtest\AppData\Local\HostAgent-DEV-evidence\completion-20261022`.

## Embedded MSI payload comparison

The immutable 1.2.7 DEV package from source dfa14d6 was not modified.
The MSI SHA256 remained
`3098A857005F8FD5073FB80990A256AEB636D8193076AF9E0BE9BC51DCE0D043`.

The external audit opened the MSI database read-only, mapped File/Component/
Directory rows to package paths, and ran the existing WiX 5.0.2 tool only as
`wix msi decompile <pinned MSI> -o <new evidence source.wxs> -x <new export>
-intermediateFolder <new evidence directory>`. It did not install or execute
the MSI or exported payload. Exported files were retained as evidence.

| Run | Actual result |
|---|---|
| candidate127-cab-audit | exit1, 4.7s; audit rejected three Media rows because its first draft incorrectly required one. No package defect or mutation; failed evidence/script retained |
| candidate127-cab-audit-r2 | exit0, 22.2s, PASS_CAB_PAYLOAD_ONLY: all 1208 installed File rows matched extracted CAB member length and SHA256 against the reviewed package, across three CABs; 1210 exported files included two non-payload embedded binaries |

The successful run started at 2083.3 MiB, observed minimum 1521.56 MiB and
ended at 1980.89 MiB. No monitor failure; memory recovered and protected
services were unchanged. Embedded custom-action binary semantic/runtime
acceptance does not follow from this file-payload comparison.

## Confirmed installer PlanOnly defect, test-first correction

The first combined read-only handoff failed in `Install-HostAgent.ps1
-PlanOnly` with `installation_failed`. The validator itself succeeded. The
installer read `$LASTEXITCODE`, which successful PowerShell script completion
does not initialize or reset. Under inherited StrictMode Latest an unset
variable threw; a stale native exit37 falsely rejected the valid package.
This is an installer tooling defect, separate from privileged MSI behavior.

New behavioral tests invoke the actual installer with a disposable manifest/
files, StrictMode Latest and unset/stale native exit status. They also tamper
with a manifest-listed service executable and require nonzero exit and no
successful plan output. No MSI is executed by these tests.

The correction initializes a local exit code immediately before the validator,
checks its current exit and exactly one JSON receipt, and requires actual
Boolean `valid=true` plus Boolean `devOnly` matching the requested mode.
Existing signature/VM/integrity checks and the installation branch are
unchanged. The original immutable 1.2.7 package still contains its original
installer script: this source fix must not be substituted into that package
without a new manifest/artifact review.

Exact focused command, using account-local SDK 10.0.401:

```powershell
dotnet test tests/HostAgent.Infrastructure.Tests/HostAgent.Infrastructure.Tests.csproj -c Release --no-restore -m:1 /p:UseSharedCompilation=false --filter 'FullyQualifiedName~Installer_plan_uses_current_validation_result' --logger 'console;verbosity=normal'
```

| Run | Actual result |
|---|---|
| installer-plan-strict-red | exit1, 52.9s; two expected assertion failures for valid packages (unset and stale37); tampered-package refusal passed, total 1/3 PASS |
| installer-plan-strict-green | exit0, 6.0s; 4/4 PASS, including existing read-only/Shadow plan test; same regression cases plus existing test, `--no-build`, source scripts loaded at runtime |
| readonly-handoff-20261023 | exit1, 7.3s; original StrictMode failure, followed by the external audit's missing-output assertion; no mutation |
| readonly-handoff-20261023-r2 | exit0, 12.9s; all85 current source/tool pins and6 installed hashes matched; absent GUID scope; actual default fixture preflight and corrected source installer PlanOnly both passed |

Support independently reviewed the source and new test diff read-only and
reported no blocker for this narrow correction. This does not close the
separate operator native or installed acceptance gates.

The read-only handoff started at1661.2 MiB, observed minimum1325.85 MiB and
ended at1597.34 MiB; no warning/abort. C: had17.00 GiB at the final child
observation. HostAgent remained Running PID3984 and HostAgent.Helper Running
PID3520. Native fixture result is still NOT RUN, not a presumed failure or PASS.

The same operator entrypoint SHA256
`80838B12A68442B83CC868DA6179598F1A9F6C30D77745140239A25CE90D4352`
and manifest SHA256
`027A72468D4F10703001E35410CB67519AD3B5D1A8510338086F485E9DDA7259`
remain valid. The single next privileged step is the unchanged reviewed
operator block in the preceding record, once, inside the approved VM.

## Remaining gates

1. Operator-only GUID native fixture remains NOT RUN without a new operator
   receipt. The previously reviewed entrypoint and external manifest are not
   changed by this PlanOnly fix. Require actual fault receipts, state proofs,
   quiescence, cleanup and memory recovery; never infer PASS from unit tests.
2. A separately reviewed guarded 1.2.6-to-next-DEV upgrade wrapper is still
   needed after that gate. Existing generic `Install-HostAgent.ps1` uses an
   unbounded `Start-Process -Wait` for msiexec and unconditional staging cleanup
   in finally. It is not a bounded failure-preserving acceptance harness.
   Never kill SYSTEM msiexec or weaken guards to use it prematurely.
3. Fresh installed service-context process-denial-stage, authenticated View/
   WPF freshness, configuration preservation and database acceptance remain
   open. Current installed services were not replaced or restarted here.
4. Exact Confirmation approval/allowlist/audit/replay/cooldown/kill-switch
   acceptance on harmless isolated targets remains open. Shadow/read-only
   was not switched, and no action through the installed helper was requested.
5. Signing, provider enrollment/provenance and target-policy acceptance remain
   open. No signing application, production deployment or host transfer.

## Fresh final Release verification

Both commands ran inside the VM through `run-check-warning768.ps1`, using
the existing local SDK and heap limit256 MiB. No network test opt-in or
native/elevated MSI matrix was requested.

```powershell
dotnet build HostAgent.sln -c Release --no-restore -m:1 /p:UseSharedCompilation=false
dotnet test HostAgent.sln -c Release --no-build --no-restore -m:1 --logger 'console;verbosity=minimal'
```

| Run | Actual result |
|---|---|
| installer-plan-final-build | exit0, 7.9s child elapsed; build output 0 warnings/0 errors; minimum1747.93 MiB, end1963.34 MiB |
| installer-plan-final-suite | exit0, 271.0s; Core62 + Infrastructure557 + Desktop15 = **634/634 PASS**, 0 failed/0 skipped; start2035.3 MiB, minimum1334.93 MiB, end1878.98 MiB |

Both monitor failures were null; memory recovery was confirmed and protected
service identities/PIDs were unchanged. Relevant focused and final tests
passed; no additional physical chaos, reboot or protected-service operation
was performed. The native operator fixture remains the next blocking gate.

Diff review and a credential-pattern scan of the four intended source/test/
documentation files found no credential-pattern matches or whitespace errors.
No binaries, databases, SDK/runtime or extracted CAB evidence are staged for
Git. Only the installer source, its new regression tests and dated verification
documentation are intended for the normal work-branch commit/push.

## Counter-read fixture failure diagnostics addendum (2026-10-24)

The operator supplied a read-only inventory for failed fixture run
`079cce4df06f46a6b5c36e7818a25e2f`. It confirmed the approved VirtualBox VM,
elevated operator identity, protected `HostAgent` Running, and absence of the
GUID-scoped fixture service, local group, and registry key. The preserved
fixture root contained only `result.json`; there was no `build` directory or
tool log. The earlier receipt said `failureBeforeFirstMsi=true` and
`nativeMatrixRun=false`, while `changesPerformed=true`. Those facts narrow the
failure to fixture setup before the first MSI attempt, after the root had been
created, but do not establish the exact failing command or exception. No
database, secrets, exception text, or protected service state was changed by
the read-only inventory.

To make a fresh, separately reviewed attempt diagnosable without persisting
exception messages or stacks, the fixture runner now records a bounded stage,
a fixed failure category, CLR exception type, and integer HRESULT. Explicit
stages cover local-group creation, fixture build, each named MSI phase,
cleanup, memory recovery, final protected-service identity check, and receipt
write. A receipt-write failure now also makes the process exit nonzero. This is
harness instrumentation only; it does not imply native MSI acceptance or
identify the cause of the prior run.

Regression coverage was added for sanitized failure details, setup-stage
placement, per-phase attribution, cleanup attribution, late memory failure,
protected-identity mismatch, and receipt-write exit state. Test-first checks
observed the expected failures before the corresponding harness corrections.
After the final edits, the complete focused fixture guard class passed 21/21.
The Release build passed with 0 warnings and 0 errors. The full Release suite
passed 638/638 (Core 62, Infrastructure 561, Desktop 15) with `DOTNET_ROOT`
set to the account-local SDK directory; the initial retry without that
process-local variable had three environment-dependent test failures and is
not counted as a product test failure.

Fresh focused rerun command:

```powershell
$env:DOTNET_ROOT = Join-Path $env:LOCALAPPDATA 'HostAgent-DEV-tools\dotnet-10.0.401'
& (Join-Path $env:DOTNET_ROOT 'dotnet.exe') test tests/HostAgent.Infrastructure.Tests/HostAgent.Infrastructure.Tests.csproj --configuration Release --no-build --no-restore -m:1 --filter 'FullyQualifiedName~CounterReadFixtureGuardTests' --logger 'console;verbosity=minimal'
```

The earlier full Release verification used:

```powershell
& (Join-Path $env:DOTNET_ROOT 'dotnet.exe') build HostAgent.sln --configuration Release --no-restore -m:1 --verbosity minimal
& (Join-Path $env:DOTNET_ROOT 'dotnet.exe') test HostAgent.sln --configuration Release --no-build --no-restore -m:1 --logger 'console;verbosity=minimal'
```

An independent read-only review found no remaining issue in the changed
failure-attribution paths. The original fixture failure remains unexplained
because the old receipt did not record stage or exception type. The old GUID
scope is preserved and must not be reused. A future native run needs a fresh
GUID, rebuilt input pins and newly reviewed manifest because the fixture
runner source hash changed. No elevated fixture execution, MSI matrix, service
registration, host-project transfer, or production installation was performed
for this addendum.

## Fresh counter-read fixture manifest preflight (2026-10-24)

A new run scope was prepared read-only as
`b3f8fc8ad562409e996948f4e10c6da0`. The default preparation script returned
`READ_ONLY_PLAN`, `changesPerformed=false`, and `nativeMatrixRun=false`; all 85
current source/tool input pins were included. The separately written external
manifest is
`HostAgent-DEV-releases/counter-fixture-b3f8fc8ad562409e996948f4e10c6da0/review-manifest.json`,
SHA256 `FDA33BD1623EE0111DF2D136AB4D87BB1111142504821450D8C3DB1F7C8AD5E4`.
Its six installed-file pins were read from the running DEV installation,
version 1.2.6. The manifest was then validated read-only against all 85 source
inputs and all six installed-file hashes; result:
`MANIFEST_INPUTS_AND_INSTALL_PINS_MATCH`. The pinned runner SHA256 is
`FBEFA6D8FEEA1C07D423908126B2DF699B0D19B90196D9B4477B7BDEB076A74E`.

The approved VM identity and account matched. At final preflight, HostAgent
(PID 3928) and HostAgent.Helper (PID 3388) were both Running with the expected
automatic start configuration and image paths. Free memory was 1934.2 MiB and
C: free space was 15.78 GiB, above the unchanged 768 MiB start and 15 GiB disk
guards. The new ProgramData fixture root did not exist. The actual fixture
runner's default read-only preflight also returned `READ_ONLY_PREFLIGHT`,
`changesPerformed=false`, `nativeMatrixRun=false`, and the same six installed
hashes. No service, group, registry, ACL, database, MSI or product-file
operation was performed. This manifest is prepared and hash-validated, but
native matrix acceptance remains NOT RUN pending the operator's one-time
elevated invocation.

## Counter fixture group-creation diagnosis and corrected candidate (2026-10-25)

The operator's run of the preceding manifest failed at
`failureStage=local_group_create` with
`failureExceptionType=System.Management.Automation.ValidationMetadataException`,
before the first MSI. Protected identity was unchanged, memory recovered from
1571.4 MiB to 1845.7 MiB (minimum 1838.8 MiB), and native acceptance remained
NOT RUN. Read-only inspection of the actual `New-LocalGroup` command metadata
showed `Description` has a `ValidateLength` maximum of 48 characters; the
runner's supplied description was 51 characters. This is the confirmed
harness defect. The operator's preserved GUID directory remains intact and
must not be reused.

A regression test extracts the actual `-Description` argument from the runner
and compares it with the installed cmdlet's real validation metadata. Its red
run failed with `Fixture description length 51 exceeds New-LocalGroup's real
48-character limit`. The runner description was shortened to
`HostAgent counter-read fixture`. The same focused test then passed 1/1 and
the full fixture guard class passed 22/22.

Release build after the fix passed with 0 warnings and 0 errors. The solution
test run reported Core 62/62 and Infrastructure 562/562; the tool session was
lost after Desktop started and before the overall summary was captured.
Desktop was then run independently and passed 15/15. Thus all three Release
test projects passed, 639 total; an overall solution-process exit code was
not captured. Resource checks remained above the 768 MiB start and 15 GiB disk
floors.

Because the runner hash changed, a fresh run was prepared as
`76600fa6de6c47c5b890441dee38140e`. Its external manifest is
`HostAgent-DEV-releases/counter-fixture-76600fa6de6c47c5b890441dee38140e/review-manifest.json`,
SHA256 `C6A17492D5E1129570683197D35111B5DEB4E6E3BDDE5DCA7E5F272DEB16C061`.
Read-only validation matched all 85 current inputs and 6 installed-file pins;
the pinned runner hash is
`9C266F465F96A8906DCA903B422F3959D373A22A79D75A83BFB6CF6AC18BC928`.
Installed version remained 1.2.6. The actual runner returned
`READ_ONLY_PREFLIGHT`, `changesPerformed=false`, and `nativeMatrixRun=false`;
HostAgent PID 3996 and HostAgent.Helper PID 3248 remained Running. Free memory
was 2325.3 MiB, C: had 16.86 GiB free, and the new GUID ProgramData scope was
absent. No group, service, registry, ACL, database or MSI operation was
performed during this preparation. The native MSI matrix is still NOT RUN.

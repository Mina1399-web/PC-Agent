# V1 product completion verification

Fresh work on 2026-10-11 in the approved Windows DEV VM. Historical Task 2–4 records remain unchanged; their prior conclusions were rechecked against the current source and a fresh 150-test baseline. Preserved base: 2932cb44749283af54cc2f2f52472405eec29c6d, tag task-4-verified-vm. Completion branch: codex/v1-release-readiness. Original imported source base: ab0bc9b3352a68b206b92b64e53e267b38d7691b; host-source reference was 188e3e259519e8713cee7aed84fcec7e40aa408c. No production host transfer/deployment occurred.

## Environment and tools

Repeated direct verification: hostname AgentPC, account agentpc/agentpcdevtest, cwd HostAgent-DEV, manufacturer innotek GmbH, model VirtualBox, UUID 46543236-8C5E-49DA-953C-DB17F9B1E402; VBoxGuest/VBoxService Running. The current token is not elevated; the account is a member of local Administrators. No security policy/snapshot/host file was changed.

Runs 01–33 used account-local SDK 10.0.100. Final build/suite use SDK 10.0.401 and runtime 10.0.12. The official Microsoft SDK ZIP was verified against its release-metadata SHA-512 before extraction into LocalAppData/HostAgent-DEV-tools/dotnet-10.0.401. Previous SDK remains preserved. WiX 5.0.2 and Util extension 5.0.2 are account-local build tools outside the repository. PowerShell 7 is the existing bundled runtime; its RemoteSigned policy was left unchanged. Windows PowerShell 5.1 Restricted was not bypassed.

## Executed tests

The source-external runner run-hostagent-check.ps1 records UTC, exact dotnet arguments, result and exit code in sibling HostAgent-DEV-evidence/v1-NN-*.log. Its SDK path was updated after installing 10.0.401. Raw logs, SDKs, caches, MSI probe directories and signing material are excluded from source delivery. See [command ledger](v1-release-runs.md).

| Check | Actual result |
| --- | --- |
| 01 fresh base full Release suite | 42 Core +108 Infrastructure =150 passed |
| 02→03 administrative protocol/pipe/approval | 9 expected runtime failures →9 passed |
| 04→05 dispatcher | 4 expected failures →13 combined passed |
| 06→07 CLI | 5 expected failures →18 combined passed |
| 08→09 real CLI process/host listener | 2 expected failures →20 combined passed |
| 10 tooling setup | 5 failures because Windows PowerShell script execution was restricted; not a behavioral red |
| 11→12 corrected PowerShell 7 tooling | 2 expected missing-behavior failures, 3 rejection tests passed →5 passed |
| 13→14 protected installer bootstrap | 2 expected failures →4 including operator hosting passed |
| 15–18 MSI | expected missing build, then actual COM inspector issues (null returns/recovery table strings), corrected →8 tooling/bootstrap passed |
| 19 administrator denial/frame bounds coverage | 11 Administration passed |
| 20→22 builder/recursive MSI/collision guard | missing signature gate and 3-vs-4 MSI payload regression observed →9 tooling/bootstrap passed |
| 23→25 configuration | missing configuration behavior observed (2 then3 failures) →3 passed |
| 26 | compile-only unused-stub-parameter failure; not behavioral red |
| 27→28 configuration workflow | 2 expected runtime failures →12 operator/configuration tests passed |
| 29–31 resolved target | expected missing display failure, then a compile-only ValueTask conversion correction →37 combined focused passed |
| 32→33 unsigned DEV MSI gate | expected absent gate failure →7 release tooling passed |
| 34 pre-review Release build, SDK10.0.401 | exit0; 0 warnings, 0 errors |
| 35 pre-review full Release suite, --no-build | exit0; 42 Core +145 Infrastructure =187 passed; 0 failures/skips |
| 36 NuGet vulnerable transitive dependency audit | exit0; no known vulnerable packages reported by NuGet for all seven projects |

Pre-review gate commands (repeated final gates are recorded below):

```powershell
dotnet build HostAgent.sln --configuration Release
dotnet test HostAgent.sln --configuration Release --no-build
dotnet list HostAgent.sln package --vulnerable --include-transitive --no-restore
```

Tests use actual local Named Pipes/ACL/token checks, synthetic CurrentUser DPAPI, real SQLite journals, actual CLI child processes and actual MSI compilation/table inspection. HTTP notifications/heartbeat and target-mutating Windows operations remain test doubles. No production secret or destructive target operation was used.

## Review and delivery checkpoints

Initial independent A/B review approved exact base2932 to tree175b21e2619856a61f835a63ef3cc7c4ac1b3d7d with no actionable finding. Additional negative privilege/frame-bound coverage was added and passed in19. Integrated source review, self-contained package publication/acceptance and final branch publication are pending at this checkpoint; results will be appended only after execution.

## Live limits at this checkpoint

Installed virtual/SYSTEM identities, service-profile DPAPI, SCM recovery/startup without login and MSI install/uninstall/upgrade have not yet been executed. No trusted Code Signing certificate was present in CurrentUser/My. Real Pushover/Healthchecks credentials/delivery and target-host policy acceptance remain external prerequisites. There is no shared-folder path and no guest C:/PC Agent; host-source synchronization is not claimed. An unsigned DEV candidate is not a production release.

## Integrated review corrections and repeated gates

Independent reviews retained three findings in the [accumulator](../security/v1-release-review.md). Exact final source tree ae2864935c4a58232d0b989a912a097cae325bf1 was approved against base2932 with no unresolved actionable finding. Fresh behavioral red/green evidence includes actual concrete-target approval binding, protected MSI staging, and pre-copy ACL/reparse checks including inherit-only rights affecting future payload. Run48 passed47 focused tests. Run51 observed2 expected inherited-rights failures; run52 passed17 installer/security/tooling tests after correction, with0 skips/failures. Run52's test phase took5m38s on the constrained VM.

Run49 built successfully, but run50 full-suite attempt had3 timing failures (Core42 passed; Infrastructure154 passed,3 failed). Infrastructure is now serialized without changing assertions or product timeouts. The repeated final full-suite result is still pending at this checkpoint. Runs38/39 were interrupted, run41 matched no tests, and run42 was an invalid build argument; none is counted as verification. See the ledger for exact commands and exits.

Re-fetched origin/main remains188e3e259519e8713cee7aed84fcec7e40aa408c. Its tree exactly equals imported VM base ab0bc9b3352a68b206b92b64e53e267b38d7691b: a9ba1d22bef2f1a8df957c2b04eb86c1fbcc53db. This is a verified source-handoff base, not evidence about the current production host working tree. Host transfer remains unauthorized by state because no safe host path/state can be verified.

Repeated final gates: run53 full Release build exit0,0 warnings/errors; run54 full Release suite exit0,42 Core +160 Infrastructure =202 passed,0 failures/skips. Exact commands were dotnet build HostAgent.sln --configuration Release -m:1 -p:UseSharedCompilation=false and dotnet test HostAgent.sln --configuration Release --no-build -m:1 --logger 'console;verbosity=normal'. Source/diff review is approved as recorded above; CRLF-aware diff whitespace check and credential-pattern scan passed (0 matches). Packaging and live installed-service acceptance remain pending at this source checkpoint.

Run55 repeated the NuGet transitive vulnerability audit against api.nuget.org: exit0; no known vulnerable packages reported for all seven solution projects. WiX DTF package signatures were separately verified with dotnet nuget verify --all before extraction; this is a signature check, not a claim of complete toolchain vulnerability coverage.

## Actual package and installation continuation

Run56 built the self-contained unsigned DEV candidate HostAgent-1.0.0-dev1 from clean source commit b8472927d98e72a0d7c7bf1b4456a536b6f0818b. Run57's acceptance harness stopped on an uninitialized LASTEXITCODE after a successful manifest check; it is not a full acceptance pass. Corrected run58 passed actual manifest validation (688 files), MSI table inspection (659 payload files, two services), self-contained CLI apphost help, ZIP membership/extraction (689 entries), extracted integrity validation, unsigned-production refusal and tampered-artifact refusal. Original dev1 MSI SHA-256: D706C625D9ED7D78A6B5F11E1D8E2750A9948C2C5FE1469344CB4130EAD46CC4; ZIP: 1204C4FAB650DEB55F498475F9312D37A5711D5A4BA6B7FDB15482FC01BE972A. These identify the historical dev1 artifact, not a subsequently corrected candidate.

Run59 used standard Windows UAC successfully and independently reverified the DEV VM under an elevated token. Installation failed at the DEV launch condition before payload/service installation. Windows Installer ComputerName was AGENTPC; the condition's case-sensitive comparison with AgentPC evaluated false. Its registry search was already correctly 64-bit (RegLocator Type18). No security policy was changed.

Run60 added an actual read-only Windows Installer condition-engine regression and observed its expected failure. The correction changes only ComputerName equality to case-insensitive equality; the exact MachineGuid pin remains. Run61 passed all7 ReleaseTooling focused tests. Independent read-only review approved the three-file correction against b8472927, with no actionable findings. Microsoft documents the [case-insensitive condition operator](https://learn.microsoft.com/en-us/windows/win32/msi/conditional-statement-syntax) and [OpenPackage flag1 safe sessions](https://learn.microsoft.com/en-us/windows/win32/msi/installer-openpackage); the inspector performs no installation actions.

Repeated post-correction gates: run62 full Release build exit0,0 warnings/errors; run63 full Release suite exit0,42 Core +160 Infrastructure =202 passed,0 failures/skips. Commands match runs53/54. A rebuilt candidate and repeated installed acceptance remain pending at this checkpoint.

Run64 rebuilt HostAgent-1.0.0-dev2 from clean commit5448a09. Run65 repeated package acceptance successfully: 688 manifest files,659 MSI payload files,689 ZIP entries, actual apphost help, non-admin refusal, extracted checksum validation, unsigned-production refusal and altered-copy refusal. MSI SHA-256:5B4BC20062A2C2A6D6BD036ED08D1C96F7E91C25791978EF7E2269F082640101; ZIP:4D204E67F55ABF2BA355ED5BB1E0B5B901880C3106DD987B0D0D5EE71C3698A3.

Run66 actual elevated installation succeeded (MsiInstaller11707/1033 status0). Both product services were Running/Auto with exact virtual monitor and LocalSystem helper identities. Elevated CLI status returned control_outcome_uncertain, so the installed acceptance as a whole FAILED; secret provisioning, telemetry progress, maintenance and restart were not reached. Subsequent read-only diagnostics reproduced the status failure while monitoring continued. No production target operation or live external request was used.

Run67's two added delayed-message/group tests passed even before the ordering change; this is additional coverage, NOT behavioral-red evidence. The prior actual installed status failure in run66 is the integration regression to repeat. Source correction now reads a bounded frame before RunAsClient, following Windows' last-message-read contract; authentication still precedes parsing and dispatch. Run68 focused19 passed; run69 full build0 warnings/errors and run70 full suite42+162=204 passed. These checks do not yet prove the installed cross-account issue fixed.

Run71 strengthened the denial test to require a fixed rejection diagnostic and observed the expected null-versus-control_unauthorized failure. Run72 then passed19 focused tests after controlled phase-code reporting was added. No raw request, secret, SID or exception text is logged. Independent review approved the three-file source/test correction against5448a09 with no actionable findings. A possible cross-account token-query problem remains a hypothesis to distinguish with actual installed diagnostics; impersonation level and privileges were not raised.

Final diagnostic checkpoint: run73 full Release build exit0,0 warnings/errors; run74 full Release suite exit0,42 Core +162 Infrastructure =204 passed,0 failures/skips. These are fresh source gates; installed status must still be repeated using the next candidate.

## Cold-process authorization continuation

Run75 built unsigned DEV candidate HostAgent-1.0.1-dev1 from clean commit67720e3eb5cf4b58965e06eb99d6283414e1150f. Run76 package acceptance passed:688 manifest files,659 MSI payload files,689 ZIP entries, actual CLI help, non-admin installation refusal, extracted integrity and tamper/unsigned-production refusals. MSI SHA-256:9AA74160E1C3AE5B14226C16C339D8D41FF97BA91FB2EB0FC28CC1573DC4346C; ZIP:6B1CE810322C27419683ECC12F560EA88A602150EE6E3F15CD89D0D5C9AE7674.

Run77 used normal UAC to upgrade1.0.0 to1.0.1 successfully, preserved the existing configuration hash and verified both Running/Auto service identities. Elevated CLI status still failed; overall installed acceptance FAILED at status. Secret provisioning, telemetry progress and maintenance were not reached. Application event properties reported control_impersonation_failed.

Run78 used an external read-only EventPipe probe with the actual service and CLI. Its initial attempt stopped on a mixed-separator path comparison in the probe (exit2); the corrected probe completed exit0 and reproduced CLI exit1, with a FileNotFoundException HRESULT0x80070002. No raw trace, exception message, stack or secret was saved. Fixed framework-name/loader-event checks identified no specific missing assembly. These observations alone do not distinguish native impersonation failure from callback JIT/loading failure; the phase marker can remain unchanged if JIT fails before the callback's first statement. Earlier token-query and pre-callback interpretations are hypotheses, not established causes.

Run79 added a test-only cold server process which does not initialize WindowsIdentity/WindowsPrincipal before the first production pipe request. It reproduced the expected behavior failure:control_impersonation_failed,dispatches=0,1 failed. The product correction prepares the exact authorization delegate with RuntimeHelpers.PrepareDelegate before RunAsClient, without executing authorization under the service token. Run80 repeated that cold-process test successfully,1 passed. Run81 passed21 focused tests, including a fresh-process unauthorized role with0 dispatches and the exact control_unauthorized rejection. Identification level, ACL, server-owner pin, actual client-token checks, deadlines, auth-before-dispatch and payload clearing are unchanged. No particular missing framework assembly is claimed. An actual installed retry is still required.

Post-correction gates:run82 full Release build exit0,0 warnings/errors; run83 complete Release suite exit0,42 Core +164 Infrastructure =206 passed,0 failures/skips. Commands match runs73/74. The additional test-only PipeProbe project is excluded from the product publisher (which publishes only Service/Helper/CLI). Actual installed acceptance remains pending.

## Final DEV acceptance and delivery checkpoint

Executable source:0a13ea1dd92a2b8cc7eaa7df3d045b85cd1f2b5a. Final documentation updates do not alter that executable source. V1 implementation and the following DEV acceptance are complete; this is not a signed production release or host deployment.

- Run84 built HostAgent-1.0.2-dev1 from that clean commit, exit0. Run85 package acceptance passed, exit0:688 manifest files,659 MSI payload files,689 ZIP entries; actual CLI help/non-admin refusal, extracted integrity, tamper and unsigned-production refusals.
- MSI SHA-256:13A873DB4921FB5D186F073BED5FF444E870C4A0DA205EB63177A5D75651F461. ZIP SHA-256:5F1CD5B93D0E0CFFDEDAD098C18518BB85F464B7C0E043B23CDA6B90D6849E2E. Both are unsigned DEV-only artifacts pinned to this VM.
- Run86 used normal UAC for actual1.0.1→1.0.2 upgrade, preserving the configuration SHA-256. Both services were Running/Auto at the exact virtual monitor/LocalSystem helper identities. Elevated CLI status passed; Shadow/read-only/outbound-disabled defaults passed. Synthetic PushoverToken provisioning under the monitor identity succeeded and its file was removed. No live credentials or outbound delivery were used.
- Read-only telemetry count increased222→234 over130 seconds at the default60-second interval. CPU/memory, disk and process/services reported Partial under the nonprivileged identity; these were not silently treated as full success and no permissions were raised. Both product services stopped/started gracefully and status passed afterward.
- MSI repair `/fomus {80514D8F-A7B8-4526-BAE1-6E680A30EE20} /qn /norestart` passed with preserved configuration and working status. This tested intact installed payload, not offline repair of deliberately removed files. Both actual SCM recovery configurations reported two10-second restarts and86400-second reset. No forced crash/reboot was performed.
- The same elevated harness then uninstalled only the product, verified both services removed and configuration hash/database retained, reinstalled the verified package, and verified status/configuration again. Archived acceptance and maintenance JSON both report completed=true. The asynchronous UAC launcher did not capture a process exit code; the master harness checked worker exit0 before maintenance. These JSON outcomes, rather than the launcher exit, establish acceptance.
- Run88 independently hashed all659 installed product files against the verified manifest and passed the exact service identities/status/defaults check. Actual CLI exit0; synthetic secret absent; completed=true. No DPAPI decrypt after restart or recovery is claimed from the write-only provisioning check.
- Run87 NuGet transitive audit exit0: no known vulnerable packages reported for all8 solution projects. Source security review remains approved; final tracked credential-pattern scan found0 matching files and CRLF-aware cumulative diff check passed. These are bounded checks, not exhaustive security certification.

Run89 prepared a source-only137-file cumulative patch from required host-source base188e3e259519e8713cee7aed84fcec7e40aa408c/treea9ba1d22bef2f1a8df957c2b04eb86c1fbcc53db to source0a13ea1. Forward application in an isolated Git index reproduced the exact target tree, reverse applicability and every file/metadata SHA-256 passed, real index/tree remained clean. Git apply's default CRLF whitespace warnings did not alter bytes or the verified resulting tree. The final delivery refresh uses CRLF-aware whitespace classification. No runtime/SDK/bin/obj/secret/database/log/VM payload is included.

Run90 published codex/v1-release-readiness to the configured HTTPS origin using an ordinary push, then verified its exact0a13ea1 HEAD with ls-remote. main remained188e3e259519e8713cee7aed84fcec7e40aa408c and codex/task-4-dev remained2932cb44749283af54cc2f2f52472405eec29c6d. Final evidence-only changes receive a refreshed source manifest and ordinary fast-forward publication check outside this source snapshot. No merge or force push occurred.

Host C:\PC Agent synchronization was NOT performed: no existing verified safe path or current host working-tree evidence is available. The exact-base patch/manifest is the handoff; a GitHub push does not prove host synchronization. Trusted production signing material, target-policy acceptance and optional live endpoint credentials remain owner prerequisites. Live delivery/acknowledgement, actual native remediation, forced-crash recovery, cold-boot/no-login acceptance and long-duration soak remain unverified. No production host/security policy/snapshot change or protection bypass was used.

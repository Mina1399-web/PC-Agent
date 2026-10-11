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

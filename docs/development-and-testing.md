# Windows Development and Testing Environment

Current resilience and source/package preparation: [2026-10-18 predeployment checkpoint](verification/predeployment-2026-10-18.md), [scoped final resilience assessment](verification/resilience-final-2026-10-18.md). Historical totals below remain dated checkpoints. An unsigned DEV candidate is not production acceptance.

SignPath preparation update, 2026-10-11: the owner explicitly authorized
GitHub-hosted builds/tests for this signing path. Ordinary development stays
in the approved DEV VM; production-host builds/tests and security-policy
changes remain prohibited. The binary-review workflow passed on 2026-10-25;
the later unsigned MSI candidate path is tracked in the
[current continuation](verification/signpath-submission-2026-10-25.md).
See [signing preparation](operations/signpath-preparation.md); historical
VM-only statements below remain historical stage evidence.

Current V1 completion uses account-local SDK **10.0.401** and runtime **10.0.12**, pinned in global.json/Directory.Build.props. Earlier SDK 10.0.100 results below are historical. The SDK archive was downloaded from Microsoft's official release metadata and its SHA-512 was verified before extraction. Build/release tooling additionally requires PowerShell 7 and account-local WiX **5.0.2** with WixToolset.Util.wixext/5.0.2. Windows PowerShell 5.1 script execution is Restricted in the VM; no execution policy was changed or bypassed. See [operator/release instructions](operations/quickstart.md) and [current verification](verification/v1-release.md).

## Development host

The requested SDK version is not an exact hosted selection lock:
`global.json` allows `latestFeature`. The2026-10-26 r2 GitHub run selected
10.0.402 even though setup requested10.0.401. Record actual SDK/runtime
inventory per run. The separately completed642-test DEV gate used actual
SDK10.0.401/runtime10.0.12; do not infer a framework-dependent hosted test's
runtime from the self-contained package pin alone.

The embedded MSI preflight uses WiX DTF 5.0.2 packages, verified using dotnet nuget verify --all and extracted outside the repository beside the wix tool directory. See the release guide for the required layout. Infrastructure integration tests run serially to avoid parallel PowerShell/MSI compiler resource contention in the DEV VM; each child process has a bounded 90-second deadline and is terminated on timeout. This changes the test harness, not product security timeouts.

Builds and automated unit/integration tests for Host Agent must run on a Windows development or test environment whose approved execution policy permits locally built development assemblies to load. The protected production mini PC is not a development test host and must retain its existing Smart App Control and Code Integrity posture.

Do not disable, bypass, weaken, replace, or modify Code Integrity or Smart App Control to make a test pass. If a Windows environment blocks a local test assembly, record the policy evidence and move the verification to an approved Windows development/test environment.

WSL may be used for platform-neutral editing or Core-only analysis, but it does not verify Windows-specific behavior. Run the following integration coverage on Windows:

- Windows Service startup, identity, stop/recovery, and Service Control Manager behavior;
- Event Log access and event metadata extraction;
- Windows Named Pipe ACLs, client identity, and authorization;
- other Windows-specific APIs used by collectors or the service.

## Standard verification commands

Run the complete suite with a non-elevated token: real user-maintenance
fixtures intentionally reject elevation. When using a portable SDK, set
`DOTNET_ROOT` and `DOTNET_ROOT_X64` to the directory containing the verified
`dotnet.exe` for the test process and its children. Merely invoking an
absolute `dotnet.exe` does not populate these variables. Cold-process pipe
and CLI integration tests use `DOTNET_ROOT` to start a separate runtime;
without it they fail before product startup. Keep these settings local to
the test process and restore prior values in `finally`; do not write machine
or user environment settings. The guarded SignPath operator wrapper handles
this setup/restoration. See the [fresh reproduction and correction](verification/signpath-submission-2026-10-25.md#operator-source-verification-and-harness-follow-up).

From the repository root in PowerShell, use the repository's pinned .NET SDK and execute:

```powershell
dotnet build HostAgent.sln --configuration Release
dotnet test tests/HostAgent.Core.Tests/HostAgent.Core.Tests.csproj --configuration Release
dotnet test tests/HostAgent.Infrastructure.Tests/HostAgent.Infrastructure.Tests.csproj --configuration Release
dotnet test HostAgent.sln --configuration Release
```

Task-specific work should also run its focused filters first, recording the exact command, exit code, test count, and failures. A successful build establishes compilation only; it does not establish runtime or behavioral verification. Use an approved Windows test runner that actually loads and executes the built assemblies.

### Explicit live-network test opt-in

The Windows network collector integration test inspects real adapter metadata
but leaves connectivity probes disabled during the normal suite. To run its
live probe path in an explicitly authorized DEV VM session, the test process
must receive `HOSTAGENT_TEST_LIVE_NETWORK=1`. That path attempts the configured
gateway, public ICMP target `1.1.1.1` and DNS resolution of `example.com`; it is
not a simulated outage. Any other value, including absence, keeps the test
offline and asserts unavailable probe records with no target. This variable
is test-only and does not configure the installed service or grant actions.
Transport/diagnosis tests continue to use their existing synthetic fixtures.
Report whether the opt-in was enabled; a passing offline suite does not
establish live public-network connectivity or outage behavior.

Desktop test collections run serially. Concurrent independent STA fixtures
reproduced a WPF PackagePart resource-stream race in this DEV VM; a separate
minimal WPF program reproduced the same stream-list boundary without HostAgent
code. The installed desktop uses one UI thread. Keep collection serialization
enabled while retaining each test's asynchronous stale/reordered/cancelled
response assertions. See the [chaos preflight record](verification/chaos-resilience-2026-10-13.md)
for the failed gate, reproduction rates and fresh verification limits.
All WPF fixtures also use the shared `StaTest` lifetime: completion follows
window cleanup, dispatcher shutdown and STA thread termination. Teardown
exceptions must fail the test; never signal success before a `finally` block
that closes a window or shuts down its dispatcher.

## Future production code signing

Before deploying Host Agent binaries to a protected production host, select and document a trusted Windows code-signing strategy whose signatures and trust chain satisfy that host's active Smart App Control and Code Integrity policy. Protect signing keys and document release signing, verification, rotation, and recovery. Development/test signing and production release signing are separate concerns.

This is a future deployment requirement. No signing certificate, service, purchase, policy exception, or signing implementation is part of the current task.

## Historical Task 2–4 checkpoints

The following records describe earlier stages, including SDK10.0.100 and tests before installed V1 acceptance. Current SDK401/runtime12,206-test gates and actual installation results are in [V1 verification](verification/v1-release.md).

The protected production host's policy historically blocked the rebuilt unsigned `HostAgent.Core.Tests.dll`. Its original event evidence is preserved in [Task 2 verification](verification/task-2.md). The approved DEV VM now loads locally built assemblies: [fresh results](verification/task-2-dev-vm.md) record a passing Release build and 73/73 tests on 2026-10-11. Do not use the production mini PC as a substitute test machine or change its security posture.

The VM uses SDK 10.0.100 installed under the DEV account's LocalAppData directory. This runtime, NuGet/build caches and generated outputs are environment artifacts; exclude them from source transfer. No host transfer path is currently available. Before importing the prepared source patch on host, preserve its current Git state and review the patch against any newer uncommitted work.

Task 3 was subsequently authorized and verified in the same VM: Release build 0 warnings/errors, complete suite 108/108, final focused security tests 35/35. See [Task 3 evidence](verification/task-3.md) and [security review](security/task-3-review.md). Real Named Pipe tests run under the DEV account; destructive native operations use fakes. The SYSTEM helper bootstrap and installed Windows Service identities/SCM recovery were not executed. No host transfer or Task 4 work occurred.

The preceding paragraph records the historical Task 3 stage. Task 4 was later explicitly authorized. Fresh VM baseline reproduced 108/108 before edits; final Task 4 Release build passed with 0 warnings/errors, suite 150/150 and focused tests 43/43. See [Task 4 evidence](verification/task-4.md) and [review](security/task-4-review.md). HTTP adapters use fake handlers only; in-process Generic Host lifecycle tests do not install services. Synthetic CurrentUser DPAPI tests run under the DEV account, not a production virtual account. Host transfer remains blocked.

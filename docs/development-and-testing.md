# Windows Development and Testing Environment

## Development host

Builds and automated unit/integration tests for Host Agent must run on a Windows development or test environment whose approved execution policy permits locally built development assemblies to load. The protected production mini PC is not a development test host and must retain its existing Smart App Control and Code Integrity posture.

Do not disable, bypass, weaken, replace, or modify Code Integrity or Smart App Control to make a test pass. If a Windows environment blocks a local test assembly, record the policy evidence and move the verification to an approved Windows development/test environment.

WSL may be used for platform-neutral editing or Core-only analysis, but it does not verify Windows-specific behavior. Run the following integration coverage on Windows:

- Windows Service startup, identity, stop/recovery, and Service Control Manager behavior;
- Event Log access and event metadata extraction;
- Windows Named Pipe ACLs, client identity, and authorization;
- other Windows-specific APIs used by collectors or the service.

## Standard verification commands

From the repository root in PowerShell, use the repository's pinned .NET SDK and execute:

```powershell
dotnet build HostAgent.sln --configuration Release
dotnet test tests/HostAgent.Core.Tests/HostAgent.Core.Tests.csproj --configuration Release
dotnet test tests/HostAgent.Infrastructure.Tests/HostAgent.Infrastructure.Tests.csproj --configuration Release
dotnet test HostAgent.sln --configuration Release
```

Task-specific work should also run its focused filters first, recording the exact command, exit code, test count, and failures. A successful build establishes compilation only; it does not establish runtime or behavioral verification. Use an approved Windows test runner that actually loads and executes the built assemblies.

## Future production code signing

Before deploying Host Agent binaries to a protected production host, select and document a trusted Windows code-signing strategy whose signatures and trust chain satisfy that host's active Smart App Control and Code Integrity policy. Protect signing keys and document release signing, verification, rotation, and recovery. Development/test signing and production release signing are separate concerns.

This is a future deployment requirement. No signing certificate, service, purchase, policy exception, or signing implementation is part of the current task.

## Current limitation

The protected production host's policy historically blocked the rebuilt unsigned `HostAgent.Core.Tests.dll`. Its original event evidence is preserved in [Task 2 verification](verification/task-2.md). The approved DEV VM now loads locally built assemblies: [fresh results](verification/task-2-dev-vm.md) record a passing Release build and 73/73 tests on 2026-10-11. Do not use the production mini PC as a substitute test machine or change its security posture.

The VM uses SDK 10.0.100 installed under the DEV account's LocalAppData directory. This runtime, NuGet/build caches and generated outputs are environment artifacts; exclude them from source transfer. No host transfer path is currently available. Before importing the prepared source patch on host, preserve its current Git state and review the patch against any newer uncommitted work.

Task 3 was subsequently authorized and verified in the same VM: Release build 0 warnings/errors, complete suite 108/108, final focused security tests 35/35. See [Task 3 evidence](verification/task-3.md) and [security review](security/task-3-review.md). Real Named Pipe tests run under the DEV account; destructive native operations use fakes. The SYSTEM helper bootstrap and installed Windows Service identities/SCM recovery were not executed. No host transfer or Task 4 work occurred.

The preceding paragraph records the historical Task 3 stage. Task 4 was later explicitly authorized. Fresh VM baseline reproduced 108/108 before edits; final Task 4 Release build passed with 0 warnings/errors, suite 150/150 and focused tests 43/43. See [Task 4 evidence](verification/task-4.md) and [review](security/task-4-review.md). HTTP adapters use fake handlers only; in-process Generic Host lifecycle tests do not install services. Synthetic CurrentUser DPAPI tests run under the DEV account, not a production virtual account. Host transfer remains blocked.

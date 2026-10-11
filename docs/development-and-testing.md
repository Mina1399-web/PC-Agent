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

The current host's policy blocks the rebuilt unsigned `HostAgent.Core.Tests.dll`. See [Task 2 verification](verification/task-2.md) for the original event evidence, failed test command, and exact policy identifier. Do not use the production mini PC as a substitute test machine or change its security posture.

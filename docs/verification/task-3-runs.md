# Task 3 DEV VM command ledger

Date: 2026-10-11. UTC start timestamps below come from actual VM execution logs. Working directory: HostAgent-DEV; account-local .NET SDK 10.0.100. Raw logs remain outside source delivery. Exit codes/outcomes are authoritative even when a run filename says green.

Fixture-only failures: run 10 assumed InvalidDataException derives from IOException; run 11 violated xUnit2031; run 13 retained temporary database handles through test-only SQLite pooling. These are not behavioral red evidence. Run 19 included an additional asynchronous-timeout test that already passed; it is not claimed as an observed red-green cycle. Initial missing-type tests are contract-existence evidence; subsequent behavioral tests exercise real implementations.

## task3-01-policy-red

UTC: 2026-10-11T10:04:34.0330809+00:00

```powershell
dotnet test tests/HostAgent.Core.Tests/HostAgent.Core.Tests.csproj --configuration Release --filter "FullyQualifiedName~PolicyEngine"
```

```text
Neúspěšné!  - Neúspěšné:     3, Úspěšné:     0, Přeskočeno:     0, Celkem:     3, Doba trvání: 215 ms - HostAgent.Core.Tests.dll (net10.0)
ExitCode: 1
```

## task3-02-protocol-red

UTC: 2026-10-11T10:05:03.2327517+00:00

```powershell
dotnet test tests/HostAgent.Infrastructure.Tests/HostAgent.Infrastructure.Tests.csproj --configuration Release --filter "FullyQualifiedName~HelperProtocol"
```

```text
Neúspěšné!  - Neúspěšné:     3, Úspěšné:     0, Přeskočeno:     0, Celkem:     3, Doba trvání: 36 ms - HostAgent.Infrastructure.Tests.dll (net10.0)
ExitCode: 1
```

## task3-03-policy-green

UTC: 2026-10-11T10:05:42.2961553+00:00

```powershell
dotnet test tests/HostAgent.Core.Tests/HostAgent.Core.Tests.csproj --configuration Release --filter "FullyQualifiedName~PolicyEngine"
```

```text
Úspěšné!    - Neúspěšné:     0, Úspěšné:     3, Přeskočeno:     0, Celkem:     3, Doba trvání: 151 ms - HostAgent.Core.Tests.dll (net10.0)
ExitCode: 0
```

## task3-04-protocol-green

UTC: 2026-10-11T10:05:54.5462925+00:00

```powershell
dotnet test tests/HostAgent.Infrastructure.Tests/HostAgent.Infrastructure.Tests.csproj --configuration Release --filter "FullyQualifiedName~HelperProtocol"
```

```text
Úspěšné!    - Neúspěšné:     0, Úspěšné:     3, Přeskočeno:     0, Celkem:     3, Doba trvání: 307 ms - HostAgent.Infrastructure.Tests.dll (net10.0)
ExitCode: 0
```

## task3-05-approval-red

UTC: 2026-10-11T10:06:52.4956219+00:00

```powershell
dotnet test tests/HostAgent.Core.Tests/HostAgent.Core.Tests.csproj --configuration Release --filter "FullyQualifiedName~PolicyEngine"
```

```text
Neúspěšné!  - Neúspěšné:     1, Úspěšné:     3, Přeskočeno:     0, Celkem:     4, Doba trvání: 404 ms - HostAgent.Core.Tests.dll (net10.0)
ExitCode: 1
```

## task3-06-typed-protocol-red

UTC: 2026-10-11T10:07:14.7535742+00:00

```powershell
dotnet test tests/HostAgent.Infrastructure.Tests/HostAgent.Infrastructure.Tests.csproj --configuration Release --filter "FullyQualifiedName~HelperProtocol"
```

```text
Neúspěšné!  - Neúspěšné:     1, Úspěšné:     3, Přeskočeno:     0, Celkem:     4, Doba trvání: 279 ms - HostAgent.Infrastructure.Tests.dll (net10.0)
ExitCode: 1
```

## task3-07-approval-green

UTC: 2026-10-11T10:08:20.0924844+00:00

```powershell
dotnet test tests/HostAgent.Core.Tests/HostAgent.Core.Tests.csproj --configuration Release --filter "FullyQualifiedName~PolicyEngine"
```

```text
Úspěšné!    - Neúspěšné:     0, Úspěšné:     4, Přeskočeno:     0, Celkem:     4, Doba trvání: 300 ms - HostAgent.Core.Tests.dll (net10.0)
ExitCode: 0
```

## task3-08-typed-protocol-green

UTC: 2026-10-11T10:08:38.5577162+00:00

```powershell
dotnet test tests/HostAgent.Infrastructure.Tests/HostAgent.Infrastructure.Tests.csproj --configuration Release --filter "FullyQualifiedName~HelperProtocol"
```

```text
Úspěšné!    - Neúspěšné:     0, Úspěšné:     4, Přeskočeno:     0, Celkem:     4, Doba trvání: 289 ms - HostAgent.Infrastructure.Tests.dll (net10.0)
ExitCode: 0
```

## task3-09-boundaries-red

UTC: 2026-10-11T10:09:26.9401887+00:00

```powershell
dotnet test tests/HostAgent.Infrastructure.Tests/HostAgent.Infrastructure.Tests.csproj --configuration Release --filter "FullyQualifiedName~HelperProtocol|FullyQualifiedName~RemediationGuard|FullyQualifiedName~NamedPipeSecurity"
```

```text
Neúspěšné!  - Neúspěšné:     3, Úspěšné:     4, Přeskočeno:     0, Celkem:     7, Doba trvání: 209 ms - HostAgent.Infrastructure.Tests.dll (net10.0)
ExitCode: 1
```

## task3-10-boundary-contracts-green

UTC: 2026-10-11T10:10:46.9822458+00:00

```powershell
dotnet test tests/HostAgent.Infrastructure.Tests/HostAgent.Infrastructure.Tests.csproj --configuration Release --filter "FullyQualifiedName~HelperProtocol|FullyQualifiedName~RemediationGuard|FullyQualifiedName~NamedPipeSecurity"
```

```text
Neúspěšné!  - Neúspěšné:     1, Úspěšné:     6, Přeskočeno:     0, Celkem:     7, Doba trvání: 132 ms - HostAgent.Infrastructure.Tests.dll (net10.0)
ExitCode: 1
```

## task3-11-guard-journal-red

UTC: 2026-10-11T10:12:20.8885409+00:00

```powershell
dotnet test tests/HostAgent.Infrastructure.Tests/HostAgent.Infrastructure.Tests.csproj --configuration Release --filter "FullyQualifiedName~RemediationGuard|FullyQualifiedName~HelperProtocol"
```

```text
C:\Users\AgentPCdevtest\Documents\ChatGPT\DevTest VM PC\HostAgent-DEV\tests\HostAgent.Infrastructure.Tests\RemediationGuardTests.cs(110,9): error xUnit2031: Do not use a Where clause to filter before calling Assert.Single. Use the overload of Assert.Single that accepts a filtering function. (https://xunit.net/xunit.analyzers/rules/xUnit2031) [C:\Users\AgentPCdevtest\Documents\ChatGPT\DevTest VM PC\HostAgent-DEV\tests\HostAgent.Infrastructure.Tests\HostAgent.Infrastructure.Tests.csproj]
ExitCode: 1
```

## task3-12-guard-journal-red

UTC: 2026-10-11T10:12:52.7390573+00:00

```powershell
dotnet test tests/HostAgent.Infrastructure.Tests/HostAgent.Infrastructure.Tests.csproj --configuration Release --filter "FullyQualifiedName~RemediationGuard|FullyQualifiedName~HelperProtocol"
```

```text
Neúspěšné!  - Neúspěšné:     7, Úspěšné:     6, Přeskočeno:     0, Celkem:    13, Doba trvání: 578 ms - HostAgent.Infrastructure.Tests.dll (net10.0)
ExitCode: 1
```

## task3-13-guard-journal-green

UTC: 2026-10-11T10:17:07.3168294+00:00

```powershell
dotnet test tests/HostAgent.Infrastructure.Tests/HostAgent.Infrastructure.Tests.csproj --configuration Release --filter "FullyQualifiedName~RemediationGuard|FullyQualifiedName~HelperProtocol"
```

```text
Neúspěšné!  - Neúspěšné:     2, Úspěšné:    11, Přeskočeno:     0, Celkem:    13, Doba trvání: 6 s - HostAgent.Infrastructure.Tests.dll (net10.0)
ExitCode: 1
```

## task3-14-guard-journal-green

UTC: 2026-10-11T10:19:07.7358956+00:00

```powershell
dotnet test tests/HostAgent.Infrastructure.Tests/HostAgent.Infrastructure.Tests.csproj --configuration Release --filter "FullyQualifiedName~RemediationGuard|FullyQualifiedName~HelperProtocol"
```

```text
Úspěšné!    - Neúspěšné:     0, Úspěšné:    13, Přeskočeno:     0, Celkem:    13, Doba trvání: 1 s - HostAgent.Infrastructure.Tests.dll (net10.0)
ExitCode: 0
```

## task3-15-pipe-security-red

UTC: 2026-10-11T10:19:44.0192903+00:00

```powershell
dotnet test tests/HostAgent.Infrastructure.Tests/HostAgent.Infrastructure.Tests.csproj --configuration Release --filter "FullyQualifiedName~NamedPipeSecurity"
```

```text
Neúspěšné!  - Neúspěšné:     4, Úspěšné:     1, Přeskočeno:     0, Celkem:     5, Doba trvání: 1 s - HostAgent.Infrastructure.Tests.dll (net10.0)
ExitCode: 1
```

## task3-16-pipe-security-green

UTC: 2026-10-11T10:21:00.4720651+00:00

```powershell
dotnet test tests/HostAgent.Infrastructure.Tests/HostAgent.Infrastructure.Tests.csproj --configuration Release --filter "FullyQualifiedName~NamedPipeSecurity"
```

```text
Úspěšné!    - Neúspěšné:     0, Úspěšné:     5, Přeskočeno:     0, Celkem:     5, Doba trvání: 1 s - HostAgent.Infrastructure.Tests.dll (net10.0)
ExitCode: 0
```

## task3-17-typed-handlers-red

UTC: 2026-10-11T10:23:14.1734749+00:00

```powershell
dotnet test tests/HostAgent.Infrastructure.Tests/HostAgent.Infrastructure.Tests.csproj --configuration Release --filter "FullyQualifiedName~WindowsOperationExecutor"
```

```text
Neúspěšné!  - Neúspěšné:     5, Úspěšné:     0, Přeskočeno:     0, Celkem:     5, Doba trvání: 162 ms - HostAgent.Infrastructure.Tests.dll (net10.0)
ExitCode: 1
```

## task3-18-typed-handlers-green

UTC: 2026-10-11T10:25:03.3692131+00:00

```powershell
dotnet test tests/HostAgent.Infrastructure.Tests/HostAgent.Infrastructure.Tests.csproj --configuration Release --filter "FullyQualifiedName~WindowsOperationExecutor"
```

```text
Úspěšné!    - Neúspěšné:     0, Úspěšné:     5, Přeskočeno:     0, Celkem:     5, Doba trvání: 125 ms - HostAgent.Infrastructure.Tests.dll (net10.0)
ExitCode: 0
```

## task3-19-native-config-red

UTC: 2026-10-11T10:25:56.8784520+00:00

```powershell
dotnet test tests/HostAgent.Infrastructure.Tests/HostAgent.Infrastructure.Tests.csproj --configuration Release --filter "FullyQualifiedName~WindowsOperationExecutor|FullyQualifiedName~HelperConfiguration|FullyQualifiedName~Execution_timeout"
```

```text
Neúspěšné!  - Neúspěšné:     2, Úspěšné:     6, Přeskočeno:     0, Celkem:     8, Doba trvání: 1 s - HostAgent.Infrastructure.Tests.dll (net10.0)
ExitCode: 1
```

## task3-20-native-config-green

UTC: 2026-10-11T10:26:34.2440618+00:00

```powershell
dotnet test tests/HostAgent.Infrastructure.Tests/HostAgent.Infrastructure.Tests.csproj --configuration Release --filter "FullyQualifiedName~WindowsOperationExecutor|FullyQualifiedName~HelperConfiguration|FullyQualifiedName~Execution_timeout"
```

```text
Úspěšné!    - Neúspěšné:     0, Úspěšné:     8, Přeskočeno:     0, Celkem:     8, Doba trvání: 1 s - HostAgent.Infrastructure.Tests.dll (net10.0)
ExitCode: 0
```

## task3-21-protected-files-red

UTC: 2026-10-11T10:27:18.8975223+00:00

```powershell
dotnet test tests/HostAgent.Infrastructure.Tests/HostAgent.Infrastructure.Tests.csproj --configuration Release --filter "FullyQualifiedName~HelperProtectedFiles"
```

```text
Neúspěšné!  - Neúspěšné:     1, Úspěšné:     0, Přeskočeno:     0, Celkem:     1, Doba trvání: 42 ms - HostAgent.Infrastructure.Tests.dll (net10.0)
ExitCode: 1
```

## task3-22-protected-files-green

UTC: 2026-10-11T10:28:33.7433827+00:00

```powershell
dotnet test tests/HostAgent.Infrastructure.Tests/HostAgent.Infrastructure.Tests.csproj --configuration Release --filter "FullyQualifiedName~HelperProtectedFiles"
```

```text
Úspěšné!    - Neúspěšné:     0, Úspěšné:     1, Přeskočeno:     0, Celkem:     1, Doba trvání: 81 ms - HostAgent.Infrastructure.Tests.dll (net10.0)
ExitCode: 0
```

## task3-23-self-review-red

UTC: 2026-10-11T10:29:14.6786092+00:00

```powershell
dotnet test tests/HostAgent.Infrastructure.Tests/HostAgent.Infrastructure.Tests.csproj --configuration Release --filter "FullyQualifiedName~RemediationGuard|FullyQualifiedName~HelperConfiguration|FullyQualifiedName~NamedPipeSecurity"
```

```text
Neúspěšné!  - Neúspěšné:     2, Úspěšné:    15, Přeskočeno:     0, Celkem:    17, Doba trvání: 3 s - HostAgent.Infrastructure.Tests.dll (net10.0)
ExitCode: 1
```

## task3-24-self-review-green

UTC: 2026-10-11T10:30:22.4854083+00:00

```powershell
dotnet test tests/HostAgent.Infrastructure.Tests/HostAgent.Infrastructure.Tests.csproj --configuration Release --filter "FullyQualifiedName~RemediationGuard|FullyQualifiedName~HelperConfiguration|FullyQualifiedName~NamedPipeSecurity"
```

```text
Úspěšné!    - Neúspěšné:     0, Úspěšné:    17, Přeskočeno:     0, Celkem:    17, Doba trvání: 5 s - HostAgent.Infrastructure.Tests.dll (net10.0)
ExitCode: 0
```

## task3-25-crash-reservation-red

UTC: 2026-10-11T10:31:43.8776941+00:00

```powershell
dotnet test tests/HostAgent.Infrastructure.Tests/HostAgent.Infrastructure.Tests.csproj --configuration Release --filter "FullyQualifiedName~Crash_after_reservation"
```

```text
Neúspěšné!  - Neúspěšné:     1, Úspěšné:     0, Přeskočeno:     0, Celkem:     1, Doba trvání: 598 ms - HostAgent.Infrastructure.Tests.dll (net10.0)
ExitCode: 1
```

## task3-26-audit-parameters-red

UTC: 2026-10-11T10:32:05.8567616+00:00

```powershell
dotnet test tests/HostAgent.Infrastructure.Tests/HostAgent.Infrastructure.Tests.csproj --configuration Release --filter "FullyQualifiedName~Exact_approval_executes_once"
```

```text
Neúspěšné!  - Neúspěšné:     1, Úspěšné:     0, Přeskočeno:     0, Celkem:     1, Doba trvání: 777 ms - HostAgent.Infrastructure.Tests.dll (net10.0)
ExitCode: 1
```

## task3-27-crash-audit-green

UTC: 2026-10-11T10:33:06.9736634+00:00

```powershell
dotnet test tests/HostAgent.Infrastructure.Tests/HostAgent.Infrastructure.Tests.csproj --configuration Release --filter "FullyQualifiedName~RemediationGuard"
```

```text
Úspěšné!    - Neúspěšné:     0, Úspěšné:    10, Přeskočeno:     0, Celkem:    10, Doba trvání: 4 s - HostAgent.Infrastructure.Tests.dll (net10.0)
ExitCode: 0
```

## task3-28-sync-timeout-red

UTC: 2026-10-11T10:33:44.2928317+00:00

```powershell
dotnet test tests/HostAgent.Infrastructure.Tests/HostAgent.Infrastructure.Tests.csproj --configuration Release --filter "FullyQualifiedName~Synchronously_blocked_executor"
```

```text
Neúspěšné!  - Neúspěšné:     1, Úspěšné:     0, Přeskočeno:     0, Celkem:     1, Doba trvání: 5 s - HostAgent.Infrastructure.Tests.dll (net10.0)
ExitCode: 1
```

## task3-29-sync-timeout-green

UTC: 2026-10-11T10:34:21.3532350+00:00

```powershell
dotnet test tests/HostAgent.Infrastructure.Tests/HostAgent.Infrastructure.Tests.csproj --configuration Release --filter "FullyQualifiedName~RemediationGuard"
```

```text
Úspěšné!    - Neúspěšné:     0, Úspěšné:    11, Přeskočeno:     0, Celkem:    11, Doba trvání: 5 s - HostAgent.Infrastructure.Tests.dll (net10.0)
ExitCode: 0
```

## task3-30-release-build

UTC: 2026-10-11T10:34:50.2290537+00:00

```powershell
dotnet build HostAgent.sln --configuration Release
```

```text
0 upozornění
Počet chyb: 0
ExitCode: 0
```

## task3-31-release-suite

UTC: 2026-10-11T10:35:36.8304594+00:00

```powershell
dotnet test HostAgent.sln --configuration Release
```

```text
Úspěšné!    - Neúspěšné:     0, Úspěšné:    42, Přeskočeno:     0, Celkem:    42, Doba trvání: 727 ms - HostAgent.Core.Tests.dll (net10.0)
Úspěšné!    - Neúspěšné:     0, Úspěšné:    66, Přeskočeno:     0, Celkem:    66, Doba trvání: 6 s - HostAgent.Infrastructure.Tests.dll (net10.0)
ExitCode: 0
```

## task3-32-final-focused

UTC: 2026-10-11T10:37:48.9512610+00:00

```powershell
dotnet test HostAgent.sln --configuration Release --filter "FullyQualifiedName~PolicyEngine|FullyQualifiedName~HelperProtocol|FullyQualifiedName~NamedPipeSecurity|FullyQualifiedName~RemediationGuard|FullyQualifiedName~WindowsOperationExecutor|FullyQualifiedName~HelperConfiguration|FullyQualifiedName~HelperProtectedFiles"
```

```text
Úspěšné!    - Neúspěšné:     0, Úspěšné:     4, Přeskočeno:     0, Celkem:     4, Doba trvání: 125 ms - HostAgent.Core.Tests.dll (net10.0)
Úspěšné!    - Neúspěšné:     0, Úspěšné:    31, Přeskočeno:     0, Celkem:    31, Doba trvání: 5 s - HostAgent.Infrastructure.Tests.dll (net10.0)
ExitCode: 0
```

# Task 2 DEV VM command ledger — 2026-10-11

All runs below were actually executed inside AGENTPC (approved HostAgent-DEV VM). Times are recorded in UTC exactly as written by the runner; Europe/Prague was UTC+02:00. SDK 10.0.100 was selected through a process-local PATH. Raw logs remain outside the source checkout in the sibling `HostAgent-DEV-evidence` directory.

Run labels are filenames, not outcome claims: some early files named `green` still contain a failed run. Fixture errors below are not counted as test-first behavioral evidence. Filters are quoted here so these exact arguments can be pasted into PowerShell.

## 01-baseline-original

UTC: 2026-10-11T02:06:54.7228973+00:00; ExitCode: 0.

```powershell
dotnet test tests/HostAgent.Core.Tests/HostAgent.Core.Tests.csproj --configuration Release --filter "FullyQualifiedName~BaselineEngine"
```

Úspěšné!    - Neúspěšné:     0, Úspěšné:     2, Přeskočeno:     0, Celkem:     2, Doba trvání: 226 ms - HostAgent.Core.Tests.dll (net10.0)

## 02-diagnostic-original

UTC: 2026-10-11T02:07:33.5672057+00:00; ExitCode: 0.

```powershell
dotnet test tests/HostAgent.Core.Tests/HostAgent.Core.Tests.csproj --configuration Release --filter "FullyQualifiedName~DiagnosticEngine"
```

Úspěšné!    - Neúspěšné:     0, Úspěšné:     4, Přeskočeno:     0, Celkem:     4, Doba trvání: 124 ms - HostAgent.Core.Tests.dll (net10.0)

## 03-scm-window-red

UTC: 2026-10-11T02:07:54.8550923+00:00; ExitCode: 1.

```powershell
dotnet test tests/HostAgent.Core.Tests/HostAgent.Core.Tests.csproj --configuration Release --filter "FullyQualifiedName~DiagnosticEngine"
```

Neúspěšné!  - Neúspěšné:     3, Úspěšné:     6, Přeskočeno:     0, Celkem:     9, Doba trvání: 213 ms - HostAgent.Core.Tests.dll (net10.0)

## 04-scm-window-green

UTC: 2026-10-11T02:08:14.1977360+00:00; ExitCode: 0.

```powershell
dotnet test tests/HostAgent.Core.Tests/HostAgent.Core.Tests.csproj --configuration Release --filter "FullyQualifiedName~DiagnosticEngine"
```

Úspěšné!    - Neúspěšné:     0, Úspěšné:     9, Přeskočeno:     0, Celkem:     9, Doba trvání: 119 ms - HostAgent.Core.Tests.dll (net10.0)

## 05-scm-subject-red

UTC: 2026-10-11T02:08:50.1402240+00:00; ExitCode: 1.

```powershell
dotnet test tests/HostAgent.Core.Tests/HostAgent.Core.Tests.csproj --configuration Release --filter "FullyQualifiedName~DiagnosticEngine"
```

Neúspěšné!  - Neúspěšné:     4, Úspěšné:     9, Přeskočeno:     0, Celkem:    13, Doba trvání: 150 ms - HostAgent.Core.Tests.dll (net10.0)

## 06-scm-subject-green

UTC: 2026-10-11T02:09:41.0246219+00:00; ExitCode: 0.

```powershell
dotnet test tests/HostAgent.Core.Tests/HostAgent.Core.Tests.csproj --configuration Release --filter "FullyQualifiedName~DiagnosticEngine"
```

Úspěšné!    - Neúspěšné:     0, Úspěšné:    13, Přeskočeno:     0, Celkem:    13, Doba trvání: 142 ms - HostAgent.Core.Tests.dll (net10.0)

## 07-scm-metadata-red

UTC: 2026-10-11T02:09:57.0032428+00:00; ExitCode: 1.

```powershell
dotnet test tests/HostAgent.Infrastructure.Tests/HostAgent.Infrastructure.Tests.csproj --configuration Release --filter "FullyQualifiedName~WindowsEventLogCollector"
```

Neúspěšné!  - Neúspěšné:     2, Úspěšné:     7, Přeskočeno:     0, Celkem:     9, Doba trvání: 591 ms - HostAgent.Infrastructure.Tests.dll (net10.0)

## 08-disk-time-red

UTC: 2026-10-11T02:10:07.7992739+00:00; ExitCode: 1.

```powershell
dotnet test tests/HostAgent.Core.Tests/HostAgent.Core.Tests.csproj --configuration Release --filter "FullyQualifiedName~DiagnosticEngine"
```

Neúspěšné!  - Neúspěšné:     1, Úspěšné:    17, Přeskočeno:     0, Celkem:    18, Doba trvání: 82 ms - HostAgent.Core.Tests.dll (net10.0)

## 09-disk-time-green

UTC: 2026-10-11T02:10:20.0565723+00:00; ExitCode: 0.

```powershell
dotnet test tests/HostAgent.Core.Tests/HostAgent.Core.Tests.csproj --configuration Release --filter "FullyQualifiedName~DiagnosticEngine"
```

Úspěšné!    - Neúspěšné:     0, Úspěšné:    18, Přeskočeno:     0, Celkem:    18, Doba trvání: 79 ms - HostAgent.Core.Tests.dll (net10.0)

## 10-scm-metadata-green

UTC: 2026-10-11T02:10:23.1196483+00:00; ExitCode: 0.

```powershell
dotnet test tests/HostAgent.Infrastructure.Tests/HostAgent.Infrastructure.Tests.csproj --configuration Release --filter "FullyQualifiedName~WindowsEventLogCollector"
```

Úspěšné!    - Neúspěšné:     0, Úspěšné:     9, Přeskočeno:     0, Celkem:     9, Doba trvání: 131 ms - HostAgent.Infrastructure.Tests.dll (net10.0)

## 11-incident-red

UTC: 2026-10-11T02:11:03.1422154+00:00; ExitCode: 1.

```powershell
dotnet test tests/HostAgent.Core.Tests/HostAgent.Core.Tests.csproj --configuration Release --filter "FullyQualifiedName~IncidentEngine"
```

Neúspěšné!  - Neúspěšné:     5, Úspěšné:     0, Přeskočeno:     0, Celkem:     5, Doba trvání: 64 ms - HostAgent.Core.Tests.dll (net10.0)

## 12-incident-green

UTC: 2026-10-11T02:11:43.4682157+00:00; ExitCode: 1.

```powershell
dotnet test tests/HostAgent.Core.Tests/HostAgent.Core.Tests.csproj --configuration Release --filter "FullyQualifiedName~IncidentEngine"
```

Fixture failure: reflection wrapped the expected InvalidOperationException; 4 failed / 5. Fixture changed to call the typed interface; run 13 passed.

## 13-incident-green

UTC: 2026-10-11T02:12:05.2297255+00:00; ExitCode: 0.

```powershell
dotnet test tests/HostAgent.Core.Tests/HostAgent.Core.Tests.csproj --configuration Release --filter "FullyQualifiedName~IncidentEngine"
```

Úspěšné!    - Neúspěšné:     0, Úspěšné:     5, Přeskočeno:     0, Celkem:     5, Doba trvání: 237 ms - HostAgent.Core.Tests.dll (net10.0)

## 14-persistence-red

UTC: 2026-10-11T02:13:13.9471731+00:00; ExitCode: 1.

```powershell
dotnet test tests/HostAgent.Infrastructure.Tests/HostAgent.Infrastructure.Tests.csproj --configuration Release --filter "FullyQualifiedName~Task2Persistence"
```

Neúspěšné!  - Neúspěšné:     2, Úspěšné:     0, Přeskočeno:     0, Celkem:     2, Doba trvání: 76 ms - HostAgent.Infrastructure.Tests.dll (net10.0)

## 15-persistence-green

UTC: 2026-10-11T02:14:08.4286809+00:00; ExitCode: 0.

```powershell
dotnet test tests/HostAgent.Infrastructure.Tests/HostAgent.Infrastructure.Tests.csproj --configuration Release --filter "FullyQualifiedName~Task2Persistence|FullyQualifiedName~SqliteHostAgentStore"
```

Úspěšné!    - Neúspěšné:     0, Úspěšné:     7, Přeskočeno:     0, Celkem:     7, Doba trvání: 2 s - HostAgent.Infrastructure.Tests.dll (net10.0)

## 16-cycle-red

UTC: 2026-10-11T02:14:59.4481823+00:00; ExitCode: 1.

```powershell
dotnet test tests/HostAgent.Infrastructure.Tests/HostAgent.Infrastructure.Tests.csproj --configuration Release --filter "FullyQualifiedName~DiagnosticCycle"
```

Neúspěšné!  - Neúspěšné:     2, Úspěšné:     0, Přeskočeno:     0, Celkem:     2, Doba trvání: 35 ms - HostAgent.Infrastructure.Tests.dll (net10.0)

## 17-cycle-green

UTC: 2026-10-11T02:15:52.8110149+00:00; ExitCode: 0.

```powershell
dotnet test tests/HostAgent.Infrastructure.Tests/HostAgent.Infrastructure.Tests.csproj --configuration Release --filter "FullyQualifiedName~DiagnosticCycle"
```

Úspěšné!    - Neúspěšné:     0, Úspěšné:     2, Přeskočeno:     0, Celkem:     2, Doba trvání: 690 ms - HostAgent.Infrastructure.Tests.dll (net10.0)

## 18-coordinator-red

UTC: 2026-10-11T02:16:28.1132660+00:00; ExitCode: 1.

```powershell
dotnet test tests/HostAgent.Infrastructure.Tests/HostAgent.Infrastructure.Tests.csproj --configuration Release --filter "FullyQualifiedName~CollectionCoordinator"
```

Fixture compilation error CS1503 (network probe argument position); no test executed. Corrected before behavioral red run 19.

## 19-coordinator-red

UTC: 2026-10-11T02:16:46.0270679+00:00; ExitCode: 1.

```powershell
dotnet test tests/HostAgent.Infrastructure.Tests/HostAgent.Infrastructure.Tests.csproj --configuration Release --filter "FullyQualifiedName~CollectionCoordinator"
```

Neúspěšné!  - Neúspěšné:     1, Úspěšné:     0, Přeskočeno:     0, Celkem:     1, Doba trvání: 576 ms - HostAgent.Infrastructure.Tests.dll (net10.0)

## 20-coordinator-green

UTC: 2026-10-11T02:17:09.5489198+00:00; ExitCode: 1.

```powershell
dotnet test tests/HostAgent.Infrastructure.Tests/HostAgent.Infrastructure.Tests.csproj --configuration Release --filter "FullyQualifiedName~CollectionCoordinator"
```

Fixture NullReferenceException obtaining NullLogger.Instance via reflection; 1 failed / 1. Fixture construction corrected; run 22 passed.

## 21-core-focused

UTC: 2026-10-11T02:17:40.2317017+00:00; ExitCode: 0.

```powershell
dotnet test tests/HostAgent.Core.Tests/HostAgent.Core.Tests.csproj --configuration Release --filter "FullyQualifiedName~BaselineEngine|FullyQualifiedName~DiagnosticEngine|FullyQualifiedName~IncidentEngine"
```

Úspěšné!    - Neúspěšné:     0, Úspěšné:    25, Přeskočeno:     0, Celkem:    25, Doba trvání: 289 ms - HostAgent.Core.Tests.dll (net10.0)

## 22-coordinator-green

UTC: 2026-10-11T02:17:49.8856398+00:00; ExitCode: 0.

```powershell
dotnet test tests/HostAgent.Infrastructure.Tests/HostAgent.Infrastructure.Tests.csproj --configuration Release --filter "FullyQualifiedName~CollectionCoordinator"
```

Úspěšné!    - Neúspěšné:     0, Úspěšné:     1, Přeskočeno:     0, Celkem:     1, Doba trvání: 509 ms - HostAgent.Infrastructure.Tests.dll (net10.0)

## 23-order-key-red

UTC: 2026-10-11T02:18:29.9898918+00:00; ExitCode: 1.

```powershell
dotnet test tests/HostAgent.Core.Tests/HostAgent.Core.Tests.csproj --configuration Release --filter "FullyQualifiedName~DiagnosticEngine|FullyQualifiedName~IncidentEngine"
```

Neúspěšné!  - Neúspěšné:     1, Úspěšné:    23, Přeskočeno:     0, Celkem:    24, Doba trvání: 306 ms - HostAgent.Core.Tests.dll (net10.0)

## 24-order-key-green

UTC: 2026-10-11T02:18:46.8258177+00:00; ExitCode: 0.

```powershell
dotnet test tests/HostAgent.Core.Tests/HostAgent.Core.Tests.csproj --configuration Release --filter "FullyQualifiedName~DiagnosticEngine|FullyQualifiedName~IncidentEngine"
```

Úspěšné!    - Neúspěšné:     0, Úspěšné:    24, Přeskočeno:     0, Celkem:    24, Doba trvání: 270 ms - HostAgent.Core.Tests.dll (net10.0)

## 25-release-build

UTC: 2026-10-11T02:19:03.0776178+00:00; ExitCode: 0.

```powershell
dotnet build HostAgent.sln --configuration Release
```

0 upozornění

Počet chyb: 0

## 26-release-suite

UTC: 2026-10-11T02:19:21.9045470+00:00; ExitCode: 0.

```powershell
dotnet test HostAgent.sln --configuration Release
```

Úspěšné!    - Neúspěšné:     0, Úspěšné:    34, Přeskočeno:     0, Celkem:    34, Doba trvání: 539 ms - HostAgent.Core.Tests.dll (net10.0)

Úspěšné!    - Neúspěšné:     0, Úspěšné:    33, Přeskočeno:     0, Celkem:    33, Doba trvání: 4 s - HostAgent.Infrastructure.Tests.dll (net10.0)

## 27-review-capacity-red

UTC: 2026-10-11T02:22:02.8786778+00:00; ExitCode: 1.

```powershell
dotnet test tests/HostAgent.Infrastructure.Tests/HostAgent.Infrastructure.Tests.csproj --configuration Release --filter "FullyQualifiedName~Fixed_critical_rules_survive|FullyQualifiedName~Operational_restore_is_bounded"
```

Neúspěšné!  - Neúspěšné:     2, Úspěšné:     0, Přeskočeno:     0, Celkem:     2, Doba trvání: 531 ms - HostAgent.Infrastructure.Tests.dll (net10.0)

## 28-review-capacity-green

UTC: 2026-10-11T02:23:14.0654662+00:00; ExitCode: 0.

```powershell
dotnet test tests/HostAgent.Infrastructure.Tests/HostAgent.Infrastructure.Tests.csproj --configuration Release --filter "FullyQualifiedName~DiagnosticCycle|FullyQualifiedName~Task2Persistence|FullyQualifiedName~CollectionCoordinator"
```

Úspěšné!    - Neúspěšné:     0, Úspěšné:     7, Přeskočeno:     0, Celkem:     7, Doba trvání: 1 s - HostAgent.Infrastructure.Tests.dll (net10.0)

## 29-storage-focused

UTC: 2026-10-11T02:23:40.0857789+00:00; ExitCode: 0.

```powershell
dotnet test tests/HostAgent.Infrastructure.Tests/HostAgent.Infrastructure.Tests.csproj --configuration Release --filter "FullyQualifiedName~Task2Persistence|FullyQualifiedName~SqliteHostAgentStore|FullyQualifiedName~DiagnosticCycle|FullyQualifiedName~CollectionCoordinator"
```

Úspěšné!    - Neúspěšné:     0, Úspěšné:    12, Přeskočeno:     0, Celkem:    12, Doba trvání: 1 s - HostAgent.Infrastructure.Tests.dll (net10.0)

## 30-final-core-focused

UTC: 2026-10-11T02:24:24.0388925+00:00; ExitCode: 0.

```powershell
dotnet test tests/HostAgent.Core.Tests/HostAgent.Core.Tests.csproj --configuration Release --filter "FullyQualifiedName~BaselineEngine|FullyQualifiedName~DiagnosticEngine|FullyQualifiedName~IncidentEngine"
```

Úspěšné!    - Neúspěšné:     0, Úspěšné:    26, Přeskočeno:     0, Celkem:    26, Doba trvání: 376 ms - HostAgent.Core.Tests.dll (net10.0)

## 31-final-release-build

UTC: 2026-10-11T02:24:42.7439659+00:00; ExitCode: 0.

```powershell
dotnet build HostAgent.sln --configuration Release
```

0 upozornění

Počet chyb: 0

## 32-final-core-focused

UTC: 2026-10-11T02:25:14.3622607+00:00; ExitCode: 0.

```powershell
dotnet test tests/HostAgent.Core.Tests/HostAgent.Core.Tests.csproj --configuration Release --filter "FullyQualifiedName~BaselineEngine|FullyQualifiedName~DiagnosticEngine|FullyQualifiedName~IncidentEngine"
```

Úspěšné!    - Neúspěšné:     0, Úspěšné:    30, Přeskočeno:     0, Celkem:    30, Doba trvání: 263 ms - HostAgent.Core.Tests.dll (net10.0)

## 33-final-release-build

UTC: 2026-10-11T02:25:24.7665937+00:00; ExitCode: 0.

```powershell
dotnet build HostAgent.sln --configuration Release
```

0 upozornění

Počet chyb: 0

## 34-final-release-suite

UTC: 2026-10-11T02:25:28.2396351+00:00; ExitCode: 0.

```powershell
dotnet test HostAgent.sln --configuration Release
```

Úspěšné!    - Neúspěšné:     0, Úspěšné:    38, Přeskočeno:     0, Celkem:    38, Doba trvání: 591 ms - HostAgent.Core.Tests.dll (net10.0)

Úspěšné!    - Neúspěšné:     0, Úspěšné:    35, Přeskočeno:     0, Celkem:    35, Doba trvání: 3 s - HostAgent.Infrastructure.Tests.dll (net10.0)

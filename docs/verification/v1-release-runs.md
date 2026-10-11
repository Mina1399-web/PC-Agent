# V1 completion command ledger

Fresh source-external runner records; SDK100 for01–33, SDK401 from34. Raw logs remain in the VM and are not source artifacts.

## v1-01-baseline-suite

- UTC: 2026-10-11T17:15:20.2560827+00:00
- Command: dotnet test HostAgent.sln --configuration Release
- Úspěšné!    - Neúspěšné:     0, Úspěšné:    42, Přeskočeno:     0, Celkem:    42, Doba trvání: 2 s - HostAgent.Core.Tests.dll (net10.0)
- Úspěšné!    - Neúspěšné:     0, Úspěšné:   108, Přeskočeno:     0, Celkem:   108, Doba trvání: 8 s - HostAgent.Infrastructure.Tests.dll (net10.0)
- ExitCode: 0

## v1-02-admin-red

- UTC: 2026-10-11T17:17:00.7611863+00:00
- Command: dotnet test tests/HostAgent.Infrastructure.Tests/HostAgent.Infrastructure.Tests.csproj --configuration Release --filter FullyQualifiedName~AdministrationTests
- Neúspěšné!  - Neúspěšné:     9, Úspěšné:     0, Přeskočeno:     0, Celkem:     9, Doba trvání: 178 ms - HostAgent.Infrastructure.Tests.dll (net10.0)
- ExitCode: 1

## v1-03-admin-green

- UTC: 2026-10-11T17:19:11.4842533+00:00
- Command: dotnet test tests/HostAgent.Infrastructure.Tests/HostAgent.Infrastructure.Tests.csproj --configuration Release --filter FullyQualifiedName~AdministrationTests
- Úspěšné!    - Neúspěšné:     0, Úspěšné:     9, Přeskočeno:     0, Celkem:     9, Doba trvání: 437 ms - HostAgent.Infrastructure.Tests.dll (net10.0)
- ExitCode: 0

## v1-04-dispatch-red

- UTC: 2026-10-11T17:20:14.0912929+00:00
- Command: dotnet test tests/HostAgent.Infrastructure.Tests/HostAgent.Infrastructure.Tests.csproj --configuration Release --filter FullyQualifiedName~AdminDispatcherTests
- Neúspěšné!  - Neúspěšné:     4, Úspěšné:     0, Přeskočeno:     0, Celkem:     4, Doba trvání: 1 s - HostAgent.Infrastructure.Tests.dll (net10.0)
- ExitCode: 1

## v1-05-dispatch-green

- UTC: 2026-10-11T17:21:15.6926336+00:00
- Command: dotnet test tests/HostAgent.Infrastructure.Tests/HostAgent.Infrastructure.Tests.csproj --configuration Release --filter FullyQualifiedName~AdminDispatcherTests|FullyQualifiedName~AdministrationTests
- Úspěšné!    - Neúspěšné:     0, Úspěšné:    13, Přeskočeno:     0, Celkem:    13, Doba trvání: 1 s - HostAgent.Infrastructure.Tests.dll (net10.0)
- ExitCode: 0

## v1-06-cli-red

- UTC: 2026-10-11T17:23:16.7314923+00:00
- Command: dotnet test tests/HostAgent.Infrastructure.Tests/HostAgent.Infrastructure.Tests.csproj --configuration Release --filter FullyQualifiedName~CliTests
- Neúspěšné!  - Neúspěšné:     5, Úspěšné:     0, Přeskočeno:     0, Celkem:     5, Doba trvání: 4 s - HostAgent.Infrastructure.Tests.dll (net10.0)
- ExitCode: 1

## v1-07-cli-green

- UTC: 2026-10-11T17:24:56.8794302+00:00
- Command: dotnet test tests/HostAgent.Infrastructure.Tests/HostAgent.Infrastructure.Tests.csproj --configuration Release --filter FullyQualifiedName~CliTests|FullyQualifiedName~AdminDispatcherTests|FullyQualifiedName~AdministrationTests
- Úspěšné!    - Neúspěšné:     0, Úspěšné:    18, Přeskočeno:     0, Celkem:    18, Doba trvání: 1 s - HostAgent.Infrastructure.Tests.dll (net10.0)
- ExitCode: 0

## v1-08-operator-hosting-red

- UTC: 2026-10-11T17:25:56.1310703+00:00
- Command: dotnet test tests/HostAgent.Infrastructure.Tests/HostAgent.Infrastructure.Tests.csproj --configuration Release --filter FullyQualifiedName~OperatorHostingTests
- Neúspěšné!  - Neúspěšné:     2, Úspěšné:     0, Přeskočeno:     0, Celkem:     2, Doba trvání: 4 s - HostAgent.Infrastructure.Tests.dll (net10.0)
- ExitCode: 1

## v1-09-operator-hosting-green

- UTC: 2026-10-11T17:27:49.3866648+00:00
- Command: dotnet test tests/HostAgent.Infrastructure.Tests/HostAgent.Infrastructure.Tests.csproj --configuration Release --filter FullyQualifiedName~OperatorHostingTests|FullyQualifiedName~CliTests|FullyQualifiedName~AdminDispatcherTests|FullyQualifiedName~AdministrationTests
- Úspěšné!    - Neúspěšné:     0, Úspěšné:    20, Přeskočeno:     0, Celkem:    20, Doba trvání: 10 s - HostAgent.Infrastructure.Tests.dll (net10.0)
- ExitCode: 0

## v1-10-release-tooling-red

- UTC: 2026-10-11T17:31:28.6677602+00:00
- Command: dotnet test tests/HostAgent.Infrastructure.Tests/HostAgent.Infrastructure.Tests.csproj --configuration Release --filter FullyQualifiedName~ReleaseToolingTests
- Neúspěšné!  - Neúspěšné:     5, Úspěšné:     0, Přeskočeno:     0, Celkem:     5, Doba trvání: 4 s - HostAgent.Infrastructure.Tests.dll (net10.0)
- ExitCode: 1

## v1-11-release-tooling-red-shell-corrected

- UTC: 2026-10-11T17:34:44.6135093+00:00
- Command: dotnet test tests/HostAgent.Infrastructure.Tests/HostAgent.Infrastructure.Tests.csproj --configuration Release --filter FullyQualifiedName~ReleaseToolingTests
- Neúspěšné!  - Neúspěšné:     2, Úspěšné:     3, Přeskočeno:     0, Celkem:     5, Doba trvání: 2 s - HostAgent.Infrastructure.Tests.dll (net10.0)
- ExitCode: 1

## v1-12-release-tooling-green

- UTC: 2026-10-11T17:38:13.3912903+00:00
- Command: dotnet test tests/HostAgent.Infrastructure.Tests/HostAgent.Infrastructure.Tests.csproj --configuration Release --filter FullyQualifiedName~ReleaseToolingTests
- Úspěšné!    - Neúspěšné:     0, Úspěšné:     5, Přeskočeno:     0, Celkem:     5, Doba trvání: 4 s - HostAgent.Infrastructure.Tests.dll (net10.0)
- ExitCode: 0

## v1-13-bootstrap-red

- UTC: 2026-10-11T17:39:09.9731985+00:00
- Command: dotnet test tests/HostAgent.Infrastructure.Tests/HostAgent.Infrastructure.Tests.csproj --configuration Release --filter FullyQualifiedName~InstallationBootstrapTests
- Neúspěšné!  - Neúspěšné:     2, Úspěšné:     0, Přeskočeno:     0, Celkem:     2, Doba trvání: 72 ms - HostAgent.Infrastructure.Tests.dll (net10.0)
- ExitCode: 1

## v1-14-bootstrap-green

- UTC: 2026-10-11T17:40:26.1569416+00:00
- Command: dotnet test tests/HostAgent.Infrastructure.Tests/HostAgent.Infrastructure.Tests.csproj --configuration Release --filter FullyQualifiedName~InstallationBootstrapTests|FullyQualifiedName~OperatorHostingTests
- Úspěšné!    - Neúspěšné:     0, Úspěšné:     4, Přeskočeno:     0, Celkem:     4, Doba trvání: 2 s - HostAgent.Infrastructure.Tests.dll (net10.0)
- ExitCode: 0

## v1-15-msi-red

- UTC: 2026-10-11T17:41:15.1089833+00:00
- Command: dotnet test tests/HostAgent.Infrastructure.Tests/HostAgent.Infrastructure.Tests.csproj --configuration Release --filter FullyQualifiedName~Built_msi
- Neúspěšné!  - Neúspěšné:     1, Úspěšné:     0, Přeskočeno:     0, Celkem:     1, Doba trvání: 1 s - HostAgent.Infrastructure.Tests.dll (net10.0)
- ExitCode: 1

## v1-16-msi-green-attempt

- UTC: 2026-10-11T17:42:14.1145375+00:00
- Command: dotnet test tests/HostAgent.Infrastructure.Tests/HostAgent.Infrastructure.Tests.csproj --configuration Release --filter FullyQualifiedName~Built_msi
- Neúspěšné!  - Neúspěšné:     1, Úspěšné:     0, Přeskočeno:     0, Celkem:     1, Doba trvání: 5 s - HostAgent.Infrastructure.Tests.dll (net10.0)
- ExitCode: 1

## v1-17-msi-inspection-diagnostic

- UTC: 2026-10-11T17:42:40.6097895+00:00
- Command: dotnet test tests/HostAgent.Infrastructure.Tests/HostAgent.Infrastructure.Tests.csproj --configuration Release --filter FullyQualifiedName~Built_msi
- Neúspěšné!  - Neúspěšné:     1, Úspěšné:     0, Přeskočeno:     0, Celkem:     1, Doba trvání: 3 s - HostAgent.Infrastructure.Tests.dll (net10.0)
- ExitCode: 1

## v1-18-msi-green

- UTC: 2026-10-11T17:44:37.2116655+00:00
- Command: dotnet test tests/HostAgent.Infrastructure.Tests/HostAgent.Infrastructure.Tests.csproj --configuration Release --filter FullyQualifiedName~ReleaseToolingTests|FullyQualifiedName~InstallationBootstrapTests
- Úspěšné!    - Neúspěšné:     0, Úspěšné:     8, Přeskočeno:     0, Celkem:     8, Doba trvání: 5 s - HostAgent.Infrastructure.Tests.dll (net10.0)
- ExitCode: 0

## v1-19-admin-negative-coverage

- UTC: 2026-10-11T17:45:03.3206269+00:00
- Command: dotnet test tests/HostAgent.Infrastructure.Tests/HostAgent.Infrastructure.Tests.csproj --configuration Release --filter FullyQualifiedName~AdministrationTests
- Úspěšné!    - Neúspěšné:     0, Úspěšné:    11, Přeskočeno:     0, Celkem:    11, Doba trvání: 380 ms - HostAgent.Infrastructure.Tests.dll (net10.0)
- ExitCode: 0

## v1-20-release-builder-red

- UTC: 2026-10-11T17:45:28.4602904+00:00
- Command: dotnet test tests/HostAgent.Infrastructure.Tests/HostAgent.Infrastructure.Tests.csproj --configuration Release --filter FullyQualifiedName~Release_builder
- Neúspěšné!  - Neúspěšné:     1, Úspěšné:     0, Přeskočeno:     0, Celkem:     1, Doba trvání: 849 ms - HostAgent.Infrastructure.Tests.dll (net10.0)
- ExitCode: 1

## v1-21-msi-recursive-guard-red

- UTC: 2026-10-11T17:46:18.0761163+00:00
- Command: dotnet test tests/HostAgent.Infrastructure.Tests/HostAgent.Infrastructure.Tests.csproj --configuration Release --filter FullyQualifiedName~Built_msi
- Neúspěšné!  - Neúspěšné:     1, Úspěšné:     0, Přeskočeno:     0, Celkem:     1, Doba trvání: 2 s - HostAgent.Infrastructure.Tests.dll (net10.0)
- ExitCode: 1

## v1-22-release-tools-green

- UTC: 2026-10-11T17:47:34.0962548+00:00
- Command: dotnet test tests/HostAgent.Infrastructure.Tests/HostAgent.Infrastructure.Tests.csproj --configuration Release --filter FullyQualifiedName~ReleaseToolingTests|FullyQualifiedName~InstallationBootstrapTests
- Úspěšné!    - Neúspěšné:     0, Úspěšné:     9, Přeskočeno:     0, Celkem:     9, Doba trvání: 20 s - HostAgent.Infrastructure.Tests.dll (net10.0)
- ExitCode: 0

## v1-23-configuration-red

- UTC: 2026-10-11T17:49:15.0528038+00:00
- Command: dotnet test tests/HostAgent.Infrastructure.Tests/HostAgent.Infrastructure.Tests.csproj --configuration Release --filter FullyQualifiedName~OperatorConfigurationTests
- Neúspěšné!  - Neúspěšné:     2, Úspěšné:     0, Přeskočeno:     0, Celkem:     2, Doba trvání: 85 ms - HostAgent.Infrastructure.Tests.dll (net10.0)
- ExitCode: 1

## v1-24-configuration-process-red

- UTC: 2026-10-11T17:49:58.1147376+00:00
- Command: dotnet test tests/HostAgent.Infrastructure.Tests/HostAgent.Infrastructure.Tests.csproj --configuration Release --filter FullyQualifiedName~OperatorConfigurationTests
- Neúspěšné!  - Neúspěšné:     3, Úspěšné:     0, Přeskočeno:     0, Celkem:     3, Doba trvání: 120 ms - HostAgent.Infrastructure.Tests.dll (net10.0)
- ExitCode: 1

## v1-25-configuration-green

- UTC: 2026-10-11T17:50:39.0667720+00:00
- Command: dotnet test tests/HostAgent.Infrastructure.Tests/HostAgent.Infrastructure.Tests.csproj --configuration Release --filter FullyQualifiedName~OperatorConfigurationTests
- Úspěšné!    - Neúspěšné:     0, Úspěšné:     3, Přeskočeno:     0, Celkem:     3, Doba trvání: 427 ms - HostAgent.Infrastructure.Tests.dll (net10.0)
- ExitCode: 0

## v1-26-configuration-workflow-red

- UTC: 2026-10-11T17:51:13.5463556+00:00
- Command: dotnet test tests/HostAgent.Infrastructure.Tests/HostAgent.Infrastructure.Tests.csproj --configuration Release --filter FullyQualifiedName~ConfigurationWorkflowTests
- ExitCode: 1

## v1-27-configuration-workflow-red-runtime

- UTC: 2026-10-11T17:51:23.1128032+00:00
- Command: dotnet test tests/HostAgent.Infrastructure.Tests/HostAgent.Infrastructure.Tests.csproj --configuration Release --filter FullyQualifiedName~ConfigurationWorkflowTests
- Neúspěšné!  - Neúspěšné:     2, Úspěšné:     0, Přeskočeno:     0, Celkem:     2, Doba trvání: 516 ms - HostAgent.Infrastructure.Tests.dll (net10.0)
- ExitCode: 1

## v1-28-configuration-workflow-green

- UTC: 2026-10-11T17:52:23.3492888+00:00
- Command: dotnet test tests/HostAgent.Infrastructure.Tests/HostAgent.Infrastructure.Tests.csproj --configuration Release --filter FullyQualifiedName~ConfigurationWorkflowTests|FullyQualifiedName~OperatorConfigurationTests|FullyQualifiedName~CliTests|FullyQualifiedName~OperatorHostingTests
- Úspěšné!    - Neúspěšné:     0, Úspěšné:    12, Přeskočeno:     0, Celkem:    12, Doba trvání: 3 s - HostAgent.Infrastructure.Tests.dll (net10.0)
- ExitCode: 0

## v1-29-resolved-target-red

- UTC: 2026-10-11T17:53:27.3416881+00:00
- Command: dotnet test tests/HostAgent.Infrastructure.Tests/HostAgent.Infrastructure.Tests.csproj --configuration Release --filter FullyQualifiedName~Resolved_local_target
- Neúspěšné!  - Neúspěšné:     1, Úspěšné:     0, Přeskočeno:     0, Celkem:     1, Doba trvání: 739 ms - HostAgent.Infrastructure.Tests.dll (net10.0)
- ExitCode: 1

## v1-30-resolved-target-green

- UTC: 2026-10-11T17:54:11.1411743+00:00
- Command: dotnet test tests/HostAgent.Infrastructure.Tests/HostAgent.Infrastructure.Tests.csproj --configuration Release --filter FullyQualifiedName~CliTests|FullyQualifiedName~OperatorHostingTests|FullyQualifiedName~OperatorConfigurationTests|FullyQualifiedName~ConfigurationWorkflowTests
- ExitCode: 1

## v1-31-operator-final-focus

- UTC: 2026-10-11T17:54:24.0182618+00:00
- Command: dotnet test tests/HostAgent.Infrastructure.Tests/HostAgent.Infrastructure.Tests.csproj --configuration Release --filter FullyQualifiedName~CliTests|FullyQualifiedName~OperatorHostingTests|FullyQualifiedName~OperatorConfigurationTests|FullyQualifiedName~ConfigurationWorkflowTests|FullyQualifiedName~AdministrationTests|FullyQualifiedName~AdminDispatcherTests|FullyQualifiedName~ReleaseToolingTests|FullyQualifiedName~InstallationBootstrapTests
- Úspěšné!    - Neúspěšné:     0, Úspěšné:    37, Přeskočeno:     0, Celkem:    37, Doba trvání: 7 s - HostAgent.Infrastructure.Tests.dll (net10.0)
- ExitCode: 0

## v1-32-dev-msi-gate-red

- UTC: 2026-10-11T17:55:23.8197706+00:00
- Command: dotnet test tests/HostAgent.Infrastructure.Tests/HostAgent.Infrastructure.Tests.csproj --configuration Release --filter FullyQualifiedName~Built_msi
- Neúspěšné!  - Neúspěšné:     1, Úspěšné:     0, Přeskočeno:     0, Celkem:     1, Doba trvání: 2 s - HostAgent.Infrastructure.Tests.dll (net10.0)
- ExitCode: 1

## v1-33-dev-msi-gate-green

- UTC: 2026-10-11T17:56:18.9773060+00:00
- Command: dotnet test tests/HostAgent.Infrastructure.Tests/HostAgent.Infrastructure.Tests.csproj --configuration Release --filter FullyQualifiedName~ReleaseToolingTests
- Úspěšné!    - Neúspěšné:     0, Úspěšné:     7, Přeskočeno:     0, Celkem:     7, Doba trvání: 8 s - HostAgent.Infrastructure.Tests.dll (net10.0)
- ExitCode: 0

## v1-34-release-build-sdk401

- UTC: 2026-10-11T17:59:32.0564498+00:00
- Command: dotnet build HostAgent.sln --configuration Release
-     0 upozornění
-     Počet chyb: 0
- ExitCode: 0

## v1-35-release-full-suite-sdk401

- UTC: 2026-10-11T17:59:58.4968776+00:00
- Command: dotnet test HostAgent.sln --configuration Release --no-build
- Úspěšné!    - Neúspěšné:     0, Úspěšné:    42, Přeskočeno:     0, Celkem:    42, Doba trvání: 648 ms - HostAgent.Core.Tests.dll (net10.0)
- Úspěšné!    - Neúspěšné:     0, Úspěšné:   145, Přeskočeno:     0, Celkem:   145, Doba trvání: 28 s - HostAgent.Infrastructure.Tests.dll (net10.0)
- ExitCode: 0

## v1-36-dependency-audit

- UTC: 2026-10-11T17:59:58.4785375+00:00
- Command: dotnet list HostAgent.sln package --vulnerable --include-transitive --no-restore
- ExitCode: 0

## Integrated review corrections

### v1-37-approval-target-red

```text
UTC: 2026-10-11T18:10:58.0771161+00:00
Command: dotnet test tests/HostAgent.Infrastructure.Tests/HostAgent.Infrastructure.Tests.csproj --configuration Release --filter FullyQualifiedName~ApprovalTargetTests
Neúspěšné!  - Neúspěšné:     3, Úspěšné:     0, Přeskočeno:     0, Celkem:     3, Doba trvání: 2 s - HostAgent.Infrastructure.Tests.dll (net10.0)
ExitCode: 1
```

### v1-38-approval-target-green

```text
UTC: 2026-10-11T18:21:20.4217294+00:00
Command: dotnet test tests/HostAgent.Infrastructure.Tests/HostAgent.Infrastructure.Tests.csproj --configuration Release --filter FullyQualifiedName~ApprovalTargetTests|FullyQualifiedName~AdministrationTests|FullyQualifiedName~CliTests|FullyQualifiedName~AdminDispatcherTests|FullyQualifiedName~RemediationGuardTests
No recorded completion/exit code; interrupted, not verified.
```

### v1-39-installer-tree-red

```text
UTC: 2026-10-11T18:23:21.4845997+00:00
Command: dotnet test tests/HostAgent.Infrastructure.Tests/HostAgent.Infrastructure.Tests.csproj --configuration Release --filter FullyQualifiedName~InstallationTreeSecurityTests
No recorded completion/exit code; interrupted, not verified.
```

### v1-40-approval-target-green-retry

```text
UTC: 2026-10-11T19:06:00.2972009+00:00
Command: dotnet test tests/HostAgent.Infrastructure.Tests/HostAgent.Infrastructure.Tests.csproj --configuration Release --no-restore -m:1 --filter FullyQualifiedName~ApprovalTargetTests|FullyQualifiedName~AdministrationTests|FullyQualifiedName~CliTests|FullyQualifiedName~AdminDispatcherTests|FullyQualifiedName~RemediationGuardTests
Úspěšné!    - Neúspěšné:     0, Úspěšné:    35, Přeskočeno:     0, Celkem:    35, Doba trvání: 10 s - HostAgent.Infrastructure.Tests.dll (net10.0)
ExitCode: 0
```

### v1-41-installer-tree-red-retry

```text
UTC: 2026-10-11T19:07:11.1334127+00:00
Command: dotnet test tests/HostAgent.Infrastructure.Tests/HostAgent.Infrastructure.Tests.csproj --configuration Release --no-build --filter FullyQualifiedName~InstallationTreeSecurityTests
Žádný test neodpovídá danému filtru testovacích případů FullyQualifiedName~InstallationTreeSecurityTests v: C:\Users\AgentPCdevtest\Documents\ChatGPT\DevTest VM PC\HostAgent-DEV\tests\HostAgent.Infrastructure.Tests\bin\Release\net10.0-windows\HostAgent.Infrastructure.Tests.dll
ExitCode: 0
```

### v1-42-installer-regressions-red

```text
UTC: 2026-10-11T19:07:48.6627267+00:00
Command: dotnet test tests/HostAgent.Infrastructure.Tests/HostAgent.Infrastructure.Tests.csproj --configuration Release --no-restore -m:1 -t:Rebuild -p:UseSharedCompilation=false --filter FullyQualifiedName~InstallationTreeSecurityTests|FullyQualifiedName~Built_msi_has_only_product_services_and_transactional_installation_actions
ExitCode: 1
```

### v1-43-installer-test-rebuild

```text
UTC: 2026-10-11T19:07:59.2447138+00:00
Command: dotnet build tests/HostAgent.Infrastructure.Tests/HostAgent.Infrastructure.Tests.csproj --configuration Release --no-restore -m:1 -t:Rebuild -p:UseSharedCompilation=false
    0 upozornění
    Počet chyb: 0
ExitCode: 0
```

### v1-44-installer-regressions-red

```text
UTC: 2026-10-11T19:09:47.4276244+00:00
Command: dotnet test tests/HostAgent.Infrastructure.Tests/HostAgent.Infrastructure.Tests.csproj --configuration Release --no-build --filter FullyQualifiedName~InstallationTreeSecurityTests|FullyQualifiedName~Built_msi_has_only_product_services_and_transactional_installation_actions
Neúspěšné!  - Neúspěšné:     4, Úspěšné:     1, Přeskočeno:     0, Celkem:     5, Doba trvání: 44 s - HostAgent.Infrastructure.Tests.dll (net10.0)
ExitCode: 1
```

### v1-45-installer-tree-green-staging-red

```text
UTC: 2026-10-11T19:11:55.7510715+00:00
Command: dotnet test tests/HostAgent.Infrastructure.Tests/HostAgent.Infrastructure.Tests.csproj --configuration Release --no-restore -m:1 -p:UseSharedCompilation=false --filter FullyQualifiedName~InstallationTreeSecurityTests|FullyQualifiedName~InstallerStagingTests|FullyQualifiedName~Built_msi_has_only_product_services_and_transactional_installation_actions
Neúspěšné!  - Neúspěšné:     6, Úspěšné:     2, Přeskočeno:     0, Celkem:     8, Doba trvání: 30 s - HostAgent.Infrastructure.Tests.dll (net10.0)
ExitCode: 1
```

### v1-46-staging-and-confirmation-red

```text
UTC: 2026-10-11T19:14:09.1274709+00:00
Command: dotnet test tests/HostAgent.Infrastructure.Tests/HostAgent.Infrastructure.Tests.csproj --configuration Release --no-restore -m:1 -p:UseSharedCompilation=false --filter FullyQualifiedName~InstallerStagingTests|FullyQualifiedName~Issuer_refuses_target_rebound|FullyQualifiedName~InstallationTreeSecurityTests
Neúspěšné!  - Neúspěšné:     5, Úspěšné:     4, Přeskočeno:     0, Celkem:     9, Doba trvání: 4 s - HostAgent.Infrastructure.Tests.dll (net10.0)
ExitCode: 1
```

### v1-47-review-fixes-green

```text
UTC: 2026-10-11T19:15:59.1073167+00:00
Command: dotnet test tests/HostAgent.Infrastructure.Tests/HostAgent.Infrastructure.Tests.csproj --configuration Release --no-restore -m:1 -p:UseSharedCompilation=false --filter FullyQualifiedName~InstallerStagingTests|FullyQualifiedName~ApprovalTargetTests|FullyQualifiedName~InstallationTreeSecurityTests|FullyQualifiedName~ReleaseToolingTests|FullyQualifiedName~AdministrationTests|FullyQualifiedName~CliTests|FullyQualifiedName~RemediationGuardTests
Neúspěšné!  - Neúspěšné:     1, Úspěšné:    46, Přeskočeno:     0, Celkem:    47, Doba trvání: 56 s - HostAgent.Infrastructure.Tests.dll (net10.0)
ExitCode: 1
```

### v1-48-review-fixes-green-serial

```text
UTC: 2026-10-11T19:18:24.9816370+00:00
Command: dotnet test tests/HostAgent.Infrastructure.Tests/HostAgent.Infrastructure.Tests.csproj --configuration Release --no-restore -m:1 -p:UseSharedCompilation=false --filter FullyQualifiedName~InstallerStagingTests|FullyQualifiedName~ApprovalTargetTests|FullyQualifiedName~InstallationTreeSecurityTests|FullyQualifiedName~ReleaseToolingTests|FullyQualifiedName~AdministrationTests|FullyQualifiedName~CliTests|FullyQualifiedName~RemediationGuardTests
Úspěšné!    - Neúspěšné:     0, Úspěšné:    47, Přeskočeno:     0, Celkem:    47, Doba trvání: 57 s - HostAgent.Infrastructure.Tests.dll (net10.0)
ExitCode: 0
```

### v1-49-release-build

```text
UTC: 2026-10-11T19:20:12.0412767+00:00
Command: dotnet build HostAgent.sln --configuration Release -m:1 -p:UseSharedCompilation=false
    0 upozornění
    Počet chyb: 0
ExitCode: 0
```

### v1-50-release-suite

```text
UTC: 2026-10-11T19:21:26.1011518+00:00
Command: dotnet test HostAgent.sln --configuration Release --no-build -m:1
Úspěšné!    - Neúspěšné:     0, Úspěšné:    42, Přeskočeno:     0, Celkem:    42, Doba trvání: 1 s - HostAgent.Core.Tests.dll (net10.0)
Neúspěšné!  - Neúspěšné:     3, Úspěšné:   154, Přeskočeno:     0, Celkem:   157, Doba trvání: 1 m 6 s - HostAgent.Infrastructure.Tests.dll (net10.0)
ExitCode: 1
```

### v1-51-inherited-install-acl-red

```text
UTC: 2026-10-11T19:23:26.5334404+00:00
Command: dotnet test tests/HostAgent.Infrastructure.Tests/HostAgent.Infrastructure.Tests.csproj --configuration Release --no-restore -m:1 -p:UseSharedCompilation=false --filter FullyQualifiedName~InstallationTreeSecurityTests
Neúspěšné!  - Neúspěšné:     2, Úspěšné:     5, Přeskočeno:     0, Celkem:     7, Doba trvání: 387 ms - HostAgent.Infrastructure.Tests.dll (net10.0)
ExitCode: 1
```

Runs38/39 were interrupted. Run41 matched no tests and is not verification. Run42 was an invalid dotnet-test rebuild argument, not behavioral red. Run43 rebuilt the test assembly. Run44 observed the expected missing preflight/refusal behavior. Run45 also exposed test-only ACL snapshot normalization and harness-newline errors; these were corrected before run46. Run46 observed five intended issuer/staging failures. Run47 passed46/47 with one child-process timeout; run48 passed47/47 after serialized tooling scheduling. Run49 built successfully. Run50 attempted the full suite: Core42 passed; Infrastructure154 passed and3 timing failures under parallel resource contention. TestExecution now serializes Infrastructure without changing assertions or product deadlines. Run51 observed two expected inherit-only ACL failures and five passes.

### v1-52-inherited-install-acl-green

```text
UTC: 2026-10-11T19:26:51.8244390+00:00
Command: dotnet test tests/HostAgent.Infrastructure.Tests/HostAgent.Infrastructure.Tests.csproj --configuration Release --no-restore -m:1 -p:UseSharedCompilation=false --filter FullyQualifiedName~InstallationTreeSecurityTests|FullyQualifiedName~ReleaseToolingTests|FullyQualifiedName~InstallerStagingTests
Úspěšné!    - Neúspěšné:     0, Úspěšné:    17, Přeskočeno:     0, Celkem:    17, Doba trvání: 5 m 38 s - HostAgent.Infrastructure.Tests.dll (net10.0)
ExitCode: 0
```

### v1-53-final-release-build

```text
UTC: 2026-10-11T19:39:21.9954296+00:00
Command: dotnet build HostAgent.sln --configuration Release -m:1 -p:UseSharedCompilation=false
    0 upozornění
    Počet chyb: 0
ExitCode: 0
```

### v1-54-final-release-suite

```text
UTC: 2026-10-11T19:39:49.3644611+00:00
Command: dotnet test HostAgent.sln --configuration Release --no-build -m:1 --logger console;verbosity=normal
Testovací běh byl úspěšný.
Celkový počet testů: 42
     Úspěšné: 42
Testovací běh byl úspěšný.
Celkový počet testů: 160
     Úspěšné: 160
ExitCode: 0
```

### v1-55-final-dependency-audit

```text
UTC: 2026-10-11T19:41:49.8386657+00:00
Command: dotnet list HostAgent.sln package --vulnerable --include-transitive --no-restore
Command: dotnet list HostAgent.sln package --vulnerable --include-transitive --no-restore
Daný projekt HostAgent.Core nemá při aktuálních zdrojích žádné zranitelné balíčky.
Daný projekt HostAgent.Infrastructure nemá při aktuálních zdrojích žádné zranitelné balíčky.
Daný projekt HostAgent.Service nemá při aktuálních zdrojích žádné zranitelné balíčky.
Daný projekt HostAgent.Helper nemá při aktuálních zdrojích žádné zranitelné balíčky.
Daný projekt HostAgent.Core.Tests nemá při aktuálních zdrojích žádné zranitelné balíčky.
Daný projekt HostAgent.Infrastructure.Tests nemá při aktuálních zdrojích žádné zranitelné balíčky.
Daný projekt HostAgent.Cli nemá při aktuálních zdrojích žádné zranitelné balíčky.
ExitCode: 0
```

### v1-56-self-contained-package

```text
UTC: 2026-10-11T19:44:40.8631939+00:00
Command: pwsh -NoProfile -File tools/Build-Release.ps1 -PackagePath ../HostAgent-DEV-releases/HostAgent-1.0.0-dev1 -WixPath ../HostAgent-DEV-tools/wix/wix.exe -DevVm
{"devOnly":true,"sourceCommit":"b8472927d98e72a0d7c7bf1b4456a536b6f0818b","package":"C:\\Users\\AgentPCdevtest\\Documents\\ChatGPT\\DevTest VM PC\\HostAgent-DEV-releases\\HostAgent-1.0.0-dev1","zipSha256":"1204C4FAB650DEB55F498475F9312D37A5711D5A4BA6B7FDB15482FC01BE972A","zip":"C:\\Users\\AgentPCdevtest\\Documents\\ChatGPT\\DevTest VM PC\\HostAgent-DEV-releases\\HostAgent-1.0.0-dev1.zip"}
ExitCode: 0
```


### v1-57-package-acceptance

```text
UTC: 2026-10-11T19:48:55.4668653+00:00
Checks: actual package manifest/MSI tables/self-contained CLI apphost/help/ZIP membership
{"files":688,"devOnly":true,"valid":true,"sourceCommit":"b8472927d98e72a0d7c7bf1b4456a536b6f0818b"}
```


### v1-58-package-acceptance

```text
UTC: 2026-10-11T19:50:18.9512146+00:00
Checks: manifest/MSI/apphost/ZIP extraction/tamper and production refusal
{"files":688,"sourceCommit":"b8472927d98e72a0d7c7bf1b4456a536b6f0818b","devOnly":true,"valid":true}
{"bootstrapIsSystemDeferred":true,"recoveryServiceCount":2,"precopyValidationIsSystemDeferred":true,"monitorAccount":"NT SERVICE\\HostAgent","valid":true,"devVmGuard":true,"fileCount":659,"helperAccount":"LocalSystem","uninstallPreservesData":true,"existingServicesGuarded":true,"serviceCount":2}
{"files":688,"sourceCommit":"b8472927d98e72a0d7c7bf1b4456a536b6f0818b","devOnly":true,"valid":true}
Production refusal: publisher_signature_required; actual extracted artifact tamper refusal: release_integrity_failed
ExitCode: 0
```


### v1-60-msi-identity-red

```text
UTC: 2026-10-11T19:56:04.7337900+00:00
Command: dotnet test tests/HostAgent.Infrastructure.Tests/HostAgent.Infrastructure.Tests.csproj --configuration Release --filter FullyQualifiedName~Built_msi_has_only_product_services_and_transactional_installation_actions --logger console;verbosity=normal
Celkový počet testů: 1
     Neúspěšné: 1
ExitCode: 1
```


### v1-61-msi-identity-green

```text
UTC: 2026-10-11T19:56:56.8431998+00:00
Command: dotnet test tests/HostAgent.Infrastructure.Tests/HostAgent.Infrastructure.Tests.csproj --configuration Release --no-build --filter FullyQualifiedName~ReleaseToolingTests --logger console;verbosity=normal
Celkový počet testů: 7
     Úspěšné: 7
ExitCode: 0
```


### v1-62-post-msi-fix-build

```text
UTC: 2026-10-11T19:57:33.7279344+00:00
Command: dotnet build HostAgent.sln --configuration Release -m:1 -p:UseSharedCompilation=false
    0 upozornění
    Počet chyb: 0
ExitCode: 0
```


### v1-63-post-msi-fix-suite

```text
UTC: 2026-10-11T19:57:44.2303711+00:00
Command: dotnet test HostAgent.sln --configuration Release --no-build -m:1 --logger console;verbosity=normal
Celkový počet testů: 42
     Úspěšné: 42
Celkový počet testů: 160
     Úspěšné: 160
ExitCode: 0
```

Run59: standard UAC Start-Process pwsh.exe -Verb RunAs launched external Invoke-HostAgentInstalledAcceptance.ps1 -PackagePath ../HostAgent-DEV-releases/HostAgent-1.0.0-dev1. Worker verified VM/elevation; installation failed at DEV launch condition (MsiInstaller10005/11708); no installed service accepted. Read-only OpenPackage evaluation: ComputerName AGENTPC; equality AgentPC=0, case-insensitive equality=1. See verification record for limits. Run57 stopped on a harness null-LASTEXITCODE check; its missing final exit is not a pass.

### v1-64-corrected-package

```text
UTC: 2026-10-11T19:59:03.1486985+00:00
Command: & ./tools/Build-Release.ps1 -PackagePath ../HostAgent-DEV-releases/HostAgent-1.0.0-dev2 -WixPath ../HostAgent-DEV-tools/wix/wix.exe -DevVm
{"package":"C:\\Users\\AgentPCdevtest\\Documents\\ChatGPT\\DevTest VM PC\\HostAgent-DEV-releases\\HostAgent-1.0.0-dev2","devOnly":true,"zip":"C:\\Users\\AgentPCdevtest\\Documents\\ChatGPT\\DevTest VM PC\\HostAgent-DEV-releases\\HostAgent-1.0.0-dev2.zip","zipSha256":"4D204E67F55ABF2BA355ED5BB1E0B5B901880C3106DD987B0D0D5EE71C3698A3","sourceCommit":"5448a09cfbc5166b9c1ef83d7632ee3ea0e038e7"}
ExitCode: 0
```


### v1-65-corrected-package-acceptance

```text
UTC: 2026-10-11T20:00:17.5478262+00:00
Command: Test-HostAgentDevPackage.ps1 -PackagePath C:\Users\AgentPCdevtest\Documents\ChatGPT\DevTest VM PC\HostAgent-DEV-releases\HostAgent-1.0.0-dev2 -SourceCommit 5448a09cfbc5166b9c1ef83d7632ee3ea0e038e7 -Label v1-65-corrected-package-acceptance
{"sourceCommit":"5448a09cfbc5166b9c1ef83d7632ee3ea0e038e7","valid":true,"devOnly":true,"files":688}
{"serviceCount":2,"devVmGuard":true,"valid":true,"precopyValidationIsSystemDeferred":true,"devVmIdentityMatches":true,"uninstallPreservesData":true,"existingServicesGuarded":true,"monitorAccount":"NT SERVICE\\HostAgent","bootstrapIsSystemDeferred":true,"recoveryServiceCount":2,"fileCount":659,"helperAccount":"LocalSystem"}
Actual non-administrator installer refusal: administrator_required
{"sourceCommit":"5448a09cfbc5166b9c1ef83d7632ee3ea0e038e7","valid":true,"devOnly":true,"files":688}
ZIP entries: 689; extracted integrity valid before tamper; unsigned production and tampered copy refused.
ExitCode: 0
```


### v1-67-admin-first-message-red

```text
UTC: 2026-10-11T20:03:39.4827274+00:00
Command: dotnet test tests/HostAgent.Infrastructure.Tests/HostAgent.Infrastructure.Tests.csproj --configuration Release --filter FullyQualifiedName~Authentication_waits_for_first_bounded_message --logger console;verbosity=normal
Celkový počet testů: 2
     Úspěšné: 2
ExitCode: 0
```


### v1-68-admin-message-order-focused

```text
UTC: 2026-10-11T20:06:24.5672305+00:00
Command: dotnet test tests/HostAgent.Infrastructure.Tests/HostAgent.Infrastructure.Tests.csproj --configuration Release --filter FullyQualifiedName~AdministrationTests|FullyQualifiedName~OperatorHostingTests|FullyQualifiedName~AdminDispatcherTests --logger console;verbosity=normal
Celkový počet testů: 19
     Úspěšné: 19
ExitCode: 0
```


### v1-69-admin-order-release-build

```text
UTC: 2026-10-11T20:07:23.9938629+00:00
Command: dotnet build HostAgent.sln --configuration Release -m:1 -p:UseSharedCompilation=false
    0 upozornění
    Počet chyb: 0
ExitCode: 0
```


### v1-70-admin-order-release-suite

```text
UTC: 2026-10-11T20:07:33.0774015+00:00
Command: dotnet test HostAgent.sln --configuration Release --no-build -m:1 --logger console;verbosity=normal
Celkový počet testů: 42
     Úspěšné: 42
Celkový počet testů: 162
     Úspěšné: 162
ExitCode: 0
```


### v1-71-admin-rejection-diagnostic-red

```text
UTC: 2026-10-11T20:09:23.5399336+00:00
Command: dotnet test tests/HostAgent.Infrastructure.Tests/HostAgent.Infrastructure.Tests.csproj --configuration Release --filter FullyQualifiedName~Unprivileged_authenticated_owner_cannot_dispatch_administrator_command --logger console;verbosity=normal
Celkový počet testů: 1
     Neúspěšné: 1
ExitCode: 1
```


### v1-72-admin-rejection-diagnostic-green

```text
UTC: 2026-10-11T20:10:04.8776400+00:00
Command: dotnet test tests/HostAgent.Infrastructure.Tests/HostAgent.Infrastructure.Tests.csproj --configuration Release --filter FullyQualifiedName~AdministrationTests|FullyQualifiedName~OperatorHostingTests|FullyQualifiedName~AdminDispatcherTests --logger console;verbosity=normal
Celkový počet testů: 19
     Úspěšné: 19
ExitCode: 0
```


### v1-73-admin-diagnostics-release-build

```text
UTC: 2026-10-11T20:10:44.6777595+00:00
Command: dotnet build HostAgent.sln --configuration Release -m:1 -p:UseSharedCompilation=false
    0 upozornění
    Počet chyb: 0
ExitCode: 0
```


### v1-74-admin-diagnostics-release-suite

```text
UTC: 2026-10-11T20:10:52.8955661+00:00
Command: dotnet test HostAgent.sln --configuration Release --no-build -m:1 --logger console;verbosity=normal
Celkový počet testů: 42
     Úspěšné: 42
Celkový počet testů: 162
     Úspěšné: 162
ExitCode: 0
```

Run66: elevated external installed harness -PackagePath ../HostAgent-DEV-releases/HostAgent-1.0.0-dev2. Actual MSI install passed; exact service identities Running/Auto passed; CLI status failed control_outcome_uncertain, overall FAILED. JSON archived as v1-66-installed-acceptance/maintenance. Run67's historical label ends red, but both tests actually passed before implementation; it is NOT red evidence. Run71 is an actual expected denial-diagnostic assertion failure.

### v1-79-cold-admin-process-red

```text
UTC: 2026-10-11T20:28:54.1242496+00:00
Command: dotnet test tests/HostAgent.Infrastructure.Tests/HostAgent.Infrastructure.Tests.csproj --configuration Release --filter FullyQualifiedName~ColdAdminPipeTests --logger console;verbosity=normal -p:UseSharedCompilation=false
Celkový počet testů: 1
     Neúspěšné: 1
ExitCode: 1
```


### v1-80-cold-admin-process-green-attempt

```text
UTC: 2026-10-11T20:30:31.9507575+00:00
Command: dotnet test tests/HostAgent.Infrastructure.Tests/HostAgent.Infrastructure.Tests.csproj --configuration Release --filter FullyQualifiedName~ColdAdminPipeTests --logger console;verbosity=normal -p:UseSharedCompilation=false
Celkový počet testů: 1
     Úspěšné: 1
ExitCode: 0
```


### v1-81-cold-admin-authorization-focused

```text
UTC: 2026-10-11T20:31:26.2221744+00:00
Command: dotnet test tests/HostAgent.Infrastructure.Tests/HostAgent.Infrastructure.Tests.csproj --configuration Release --filter FullyQualifiedName~ColdAdminPipeTests|FullyQualifiedName~AdministrationTests|FullyQualifiedName~OperatorHostingTests|FullyQualifiedName~AdminDispatcherTests --logger console;verbosity=normal -p:UseSharedCompilation=false
Celkový počet testů: 21
     Úspěšné: 21
ExitCode: 0
```


### v1-82-cold-admin-release-build

```text
UTC: 2026-10-11T20:32:06.5563111+00:00
Command: dotnet build HostAgent.sln --configuration Release -m:1 -p:UseSharedCompilation=false
    0 upozornění
    Počet chyb: 0
ExitCode: 0
```


### v1-83-cold-admin-release-suite

```text
UTC: 2026-10-11T20:32:18.9556541+00:00
Command: dotnet test HostAgent.sln --configuration Release --no-build -m:1 --logger console;verbosity=normal
Celkový počet testů: 42
     Úspěšné: 42
Celkový počet testů: 164
     Úspěšné: 164
ExitCode: 0
```

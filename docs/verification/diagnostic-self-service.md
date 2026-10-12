# Incident diagnosis detail and Windows bugcheck evidence

Date: 2026-10-12. Work was performed only in the approved DEV VM `AgentPC` (VirtualBox model; `VBoxGuest` and `VBoxService` running) using SDK 10.0.401. The source tree was clean at base commit `435a525` on `codex/signpath-preparation`; work is isolated on `codex/diagnostic-self-service`. The existing commits were preserved. This record contains fresh results for this enhancement only; prior V1/Task 2–4 records remain historical.

## Behavior delivered

- `HostAgent.Cli incident <incident-id>` retrieves a read-only durable incident explanation, confidence scope, at most 16 evidence entries, truncation status, recommendation and whether unattended repair is safe. It does not return complete snapshots or invoke the Helper.
- The System Event Log collector parses only WER event 1001's stop code, four hexadecimal parameters and a minidump filename matching the Windows Minidump naming pattern. Raw XML, localized message text, report identifiers and arbitrary paths are not persisted.
- DiagnosticEngine recognizes timestamped bugchecks in the 24-hour window and groups repeated stop codes. For `0xA`, it explains an invalid kernel-memory access at elevated IRQL, preserves the parameters and dump reference, and recommends WinDbg `!analyze -v`. Confidence refers to classifying the stop code; it does not identify a driver.
- The detail response explicitly disables automatic driver changes when the driver is unknown. Existing exact, typed, allowlisted and separately approved operations remain the only execution route; Shadow/read-only defaults and Helper permissions are unchanged.

## Test-first evidence

Each listed feature behavior had a focused test fail before its implementation and pass after it. Observed red/green assertions included: missing `incident` CLI command (exit 2 vs expected 0); missing persisted incident-detail response (rejected vs expected success); no WER diagnosis (empty vs one diagnosis); absent bugcheck XML parser; lost parsed metadata in the collector (null vs stop code 10); generic instead of `0xA` classification; uppercase ID lookup failure; missing confidence scope; and oversized detail response falling back to `control_response_limit` instead of returning bounded evidence. Follow-up boundary cases cover malformed/untrusted XML, unsafe dump paths, timestamps outside/future of 24 hours, repeated-event correlation, invalid IDs, and evidence truncation.

Focused Release verification in the VM:

```powershell
dotnet test tests/HostAgent.Core.Tests/HostAgent.Core.Tests.csproj --configuration Release --no-restore --filter 'FullyQualifiedName~Wer_bugcheck_is_classified_without_claiming_a_faulty_driver_and_recommends_dump_analysis|FullyQualifiedName~Irql_not_less_or_equal_explains_stop_code_parameters_and_dump_without_identifying_a_driver|FullyQualifiedName~Bugcheck_correlation_uses_only_timestamped_events_inside_the_24_hour_window|FullyQualifiedName~Repeated_same_stop_code_is_one_diagnosis_with_stable_correlation_and_recurrence_evidence'
```

Result: exit 0; 7/7 tests passed.

```powershell
dotnet test tests/HostAgent.Infrastructure.Tests/HostAgent.Infrastructure.Tests.csproj --configuration Release --no-restore --filter 'FullyQualifiedName~Help_and_invalid_usage_never_contact_service_or_read_secret|FullyQualifiedName~Incident_detail_returns_durable_explanation_evidence_recommendation_and_repair_safety|FullyQualifiedName~Bugcheck_detail_scopes_confidence_to_stop_code_and_refuses_automatic_driver_repair|FullyQualifiedName~Incident_command_requests_one_detail_record_and_prints_explanation_and_safe_recommendation|FullyQualifiedName~Wer_parser_extracts_only_structured_bugcheck_code_parameters_and_safe_dump_filename|FullyQualifiedName~Collected_bugcheck_metadata_preserves_only_parsed_parameters_and_dump_leaf'
```

Result: exit 0; 6/6 tests passed. A separate bounded-response regression passed 1/1; a combined incident-detail safety rerun passed 3/3.

The exact live WER format was also read from the DEV VM's System log and passed through the newly built parser. It returned bugcheck `0xA`, four parameters, and a validated minidump filename. This read-only parser smoke does not establish which driver caused the crash; WinDbg analysis is still required for that attribution.

## Full Release gates

```powershell
dotnet build HostAgent.sln --configuration Release
```

Result: exit 0; 0 warnings, 0 errors.

The first full-suite invocation omitted `DOTNET_ROOT`; three pre-existing process-launch tests could not construct the SDK path and failed before exercising their assertions. No source was changed for that environment issue. The full suite was rerun with the SDK root exported for child processes:

```powershell
$sdk = 'C:\Users\AgentPCdevtest\AppData\Local\HostAgent-DEV-tools\dotnet-10.0.401'
$env:DOTNET_ROOT = $sdk
$env:PATH = $sdk + ';' + $env:PATH
dotnet test HostAgent.sln --configuration Release --no-build -m:1 --logger 'console;verbosity=normal'
```

Result: exit 0; Core 50/50 and Infrastructure 176/176, total 226 passed, 0 failed, 0 skipped.

## Limits

No installed service was replaced or restarted, no driver/service mutation was proposed or executed, no automatic repair was enabled, and no host transfer or publication occurred. The currently installed VM service therefore still uses its prior binaries; the new implementation is built and tested from source, not deployed to that service. A real cause-specific repair remains unavailable until a dump identifies a driver and a safe, supported operation is independently validated. The root cause of the observed restart is not claimed as verified by this enhancement.

# Task 2 Verification Record

## Current result — 2026-10-11, approved DEV VM

Task 2 implementation and VM verification gates pass. Final Release build: 0 warnings, 0 errors, exit 0. Final full suite: 73 passed (38 Core, 35 Infrastructure), 0 failed, 0 skipped, exit 0. Focused baseline/diagnostic/incident tests: 30 passed. The original three review findings and two new long-run restoration findings were fixed test-first; focused independent re-review found no remaining actionable defect in the fix range.

Host transfer is blocked: no VirtualBox shared-folder mappings and no accessible host checkout from this VM. Changes remain in the VM; no host synchronization or deployment is claimed. Task 3 has not started and remains explicitly outside this request.

See [fresh environment, implementation and review evidence](task-2-dev-vm.md) and the [exact command/result ledger](task-2-dev-vm-runs.md). The following 2026-10-06 record is historical; its Code Integrity failures describe the protected production host, not the fresh VM runs.

## Historical record — 2026-10-06

**Date:** 2026-10-06
**Status:** Incomplete; focused test execution is blocked by Windows Code Integrity.

## Implemented so far

- Added an in-memory baseline engine with bounded series, median, MAD, interpolated p05/p95, robust deviation, sampled Theil-Sen trend, anomaly duration/recurrence, and learning modes.
- Added deterministic diagnostic rules for sustained CPU with dominant-process attribution, fixed memory pressure, full-disk correlation with Windows Update/pagefile errors, gateway/DNS/internet distinction, service-stop evidence, and adaptive metric observations.
- Added baseline and diagnostic test fixtures. The incident lifecycle engine, incident deduplication, durable incident snapshots, baseline restore/persistence integration, and CollectionCoordinator wiring are not implemented yet.

## Test-driven evidence

1. Baseline expected red: `dotnet test tests/HostAgent.Core.Tests/HostAgent.Core.Tests.csproj --configuration Release --filter FullyQualifiedName~BaselineEngine` exited 1 because the public `BaselineEngine` type was missing (`Assert.NotNull` at `BaselineEngineTests.cs:16`).
2. After implementation, the focused baseline run first exposed an incorrect test-fixture expectation: the day-31 rolling 30-day window contains 30 prior daily samples. The fixture was corrected to that hand-counted value; the identical focused command then passed 2/2 baseline tests.
3. Diagnostic expected red: `dotnet test tests/HostAgent.Core.Tests/HostAgent.Core.Tests.csproj --configuration Release --filter FullyQualifiedName~DiagnosticEngine` exited 1 because `HostEvidence` did not exist (`Assert.NotNull` in the test fixture). This was the expected missing public behavior, not a compiler/setup error.
4. After adding diagnostic models and engine, focused tests could not execute. The test host reports `FileLoadException` with `0x800711C7` while loading `HostAgent.Core.Tests.dll`; retrying the same test with `--no-build` failed identically before test discovery.

## Confirmed environment blocker

- `Microsoft-Windows-CodeIntegrity/Operational` recorded events 3077 and 3033 for `testhost.exe` loading `C:\PC Agent\tests\HostAgent.Core.Tests\bin\Release\net10.0\HostAgent.Core.Tests.dll`.
- Event 3077 reports that the DLL did not meet Enterprise signing-level requirements or violated policy `{0283ac0f-fff1-49ae-ada1-8a933130cad6}`; event 3033 specifically reports that it did not meet Enterprise signing-level requirements.
- `Get-AuthenticodeSignature` reports `NotSigned` for the test assembly. Its SHA-256 at investigation time was `16578962D1B776EDDCEAE5B3277DA58619081CA961A3AFCE2544E39C6A666087`.
- Reproduction: `dotnet test ... --configuration Release --no-build --filter FullyQualifiedName~DiagnosticEngine` exits 1 with the same Code Integrity `FileLoadException`.
- No policy, signing, trust, or test-runner bypass was attempted.

## Other verification

- `dotnet build HostAgent.sln --configuration Release`: passed, 0 warnings, 0 errors. This confirms compilation only; it does not confirm diagnostic behavior.
- Secret-like value scan of `src/HostAgent.Core` and `tests/HostAgent.Core.Tests`: no matches.
- Trailing-whitespace scan of the changed source and test files: no matches.
- Task 2 incident tests, storage snapshot/retention integration tests, and final full solution tests remain unverified. An independent source review was completed and returned three unresolved findings, so the review gate does not pass.
- Git status/diff/commit remains unavailable because Code Integrity blocks Git from loading `libpcre2-8-0.dll`.

## Independent source review

The bounded review confirmed that Task 2 is not ready to sign off and identified three unresolved P2 findings, verified against the current source:

1. Service Control Manager crash events are not restricted to the 24-hour correlation window, so old crash metadata can be diagnosed again while it is present in `HostEvidence` (`DiagnosticEngine.cs:350-355`).
2. Crash-event diagnoses have no affected-service subject. Their correlation key uses only nullable `RecordId`, so unrelated records without IDs collapse to `service_crash:event:` (`DiagnosticEngine.cs:363-370`). The current metadata contract does not expose an affected service name for these events.
3. Disk correlation treats missing event timestamps as recent and raises corroborated confidence without evidence that the event is contemporaneous (`DiagnosticEngine.cs:209-216`).

The review also noted missing incident-engine/lifecycle, deduplication, snapshot/storage integration tests and an exact confidence assertion. These findings have not been changed because the only available test runner cannot load the rebuilt test assembly under the active Code Integrity policy; any source correction would remain unverified here.

## Resolution needed to resume

Resume the focused tests in a separate approved Windows development/test environment whose existing policy permits local development assemblies to execute. Do not change the protected production host's policy or create a test-only policy exception there. Once tests can load, run the three focused Core filters (BaselineEngine, DiagnosticEngine, IncidentEngine), finish incident lifecycle and durable snapshot integration, then rerun the full Release build and test suite before advancing to Task 3.

## Approved environment requirement for resumption

The project now documents a separate Windows development/test environment in [development-and-testing.md](../development-and-testing.md). Normal development and automated unit/integration tests must execute there under an approved policy that permits local development assemblies. Do not use WSL for Windows-specific integration verification, and keep the protected production mini PC's current security posture unchanged. Before production deployment, select an appropriate trusted Windows signing strategy; signing infrastructure is not being acquired or implemented in Task 2.

The existing Code Integrity events and failed-run evidence above are preserved. Task 2 remains incomplete, and Task 3 must not start until every Task 2 verification gate passes in an environment that actually loads the test assemblies.

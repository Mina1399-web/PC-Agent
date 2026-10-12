# Incident detail and bugcheck parser security review

Review date: 2026-10-12. Scope: `incident <id>` operator command, admin-protocol dispatch, WER event parsing and bugcheck diagnosis.

- **Privilege boundary:** `incident` is read-only and uses the existing local administrator-only Named Pipe. The server validates an exact GUID-N identifier. The new dispatch reads the durable incident and does not call `RemediationCoordinator`, Helper, or Windows mutation APIs.
- **Bounded response:** return only diagnosis fields and at most 16 evidence observations, each length-limited. Set `evidenceTruncated` when additional evidence exists. Full snapshots and raw telemetry are excluded; the normal protocol frame limit remains enforced.
- **Event input:** only `Microsoft-Windows-WER-SystemErrorReporting` event 1001 is parsed. XML has a strict character limit, DTDs and external resolution are disabled, duplicate/malformed fields fail closed, bugcheck code/parameter counts and numeric widths are checked, and dump output is reduced to a constrained basename below `Windows\Minidump`. The raw event XML/message and report ID are not retained.
- **Diagnosis honesty:** only timestamped errors inside the 24-hour window participate. `0xA` confidence describes stop-code classification, not a responsible driver. The evidence names the driver attribution limit and recommends dump analysis. An unknown driver yields `automaticRepair.available=false`.
- **Execution safety:** no helper capability, allowlist, approval, or policy mode was broadened. A user-approved repair remains subject to the existing exact operation digest, target binding, helper allowlist, rate limits and audit. Bugcheck analysis never enables driver update/rollback/disable or restart.

The Release suite passed 226/226 tests in the approved DEV VM after setting the pinned SDK's `DOTNET_ROOT`; see [verification](../verification/diagnostic-self-service.md). This source review is not a production deployment or live driver attribution.

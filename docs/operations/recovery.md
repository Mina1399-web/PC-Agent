# Stop, failure and recovery

Worker loops observe cancellation, avoid overlapping cycles, and use bounded exponential backoff up to one hour for cycle failures. A timed-out non-cooperative job stays in flight and cannot be reinvoked; heartbeat remains independent of push delivery and collection. Cooperative cleanup drains up to two seconds before dependency disposal. Host shutdown budget is 15 seconds, and the outbound worker attempts a five-second-bounded /fail shutdown heartbeat.

The helper accepts only the existing typed requests. Its listener waits are bounded, normal stop cancels pending accepts, and transient listener errors back off from 250 ms to ten seconds. Five consecutive listener failures terminate the helper process with exit code 1 so SCM can apply failure recovery. The listener-failure exit callback is covered by automated tests. Actual GUID-isolated SCM crash/recovery, normal VM restart and manual VM power-off have separate scoped records in the [resilience summary](../verification/resilience-final-2026-10-18.md); they do not prove every installed helper failure path. A clean stop intentionally does not trigger failure recovery.

The MSI configures two automatic restart attempts ten seconds apart and then no further restart, with a one-day failure-count reset. Inspect the actual configuration with read-only commands:

```powershell
sc.exe qfailure HostAgent
sc.exe qfailure HostAgent.Helper
```

No reboot or shell recovery action is configured. After repeated failures, preserve evidence and investigate. Do not change recovery settings or restart an uncertain remediation operation merely to clear an error. A VM snapshot is an operator rollback point; it is not proof that database transactions span multiple files atomically.

SQLite failure leaves monitoring degraded and typed remediation denied when the incident/audit cannot be read/written. Collection retries safe work; no fallback executes without audit. Helper absence or an uncertain operation never causes an automatic service-side mutation retry. Helper's durable unresolved claims still block the target after a crash. Preserve the helper journal, identify the real outcome and obtain trusted human review; do not delete claims or clear the database to retry.

Notification state is durable. A claim is persisted as Uncertain before send; crash/cancellation/ambiguous send cannot automatically duplicate it. Explicit rejected requests retry at most three times. Next due is one minute after the recorded cycle timestamp, and worker invocations are separated by heartbeatIntervalSeconds; a late batch completion can therefore be followed sooner than one minute later when that interval is 30 seconds. Accepted emergency messages persist receipts and poll on this same schedule until acknowledgement/expiry; after one day without confirmation, local state expires with a review-required code. Failed/uncertain/expired history degrades health until the recorded condition is reviewed; no automatic deletion/reset endpoint is provided.

Use a watchdog period/grace exceeding heartbeatIntervalSeconds plus the configured request timeout and scheduling jitter. A received /fail establishes reported agent degradation/stop. Missing heartbeats alone cannot uniquely distinguish agent crash, host power loss and internet loss. Compare an independent host/network watchdog to narrow these causes. No secondary external watchdog or live notification/heartbeat endpoint was configured. The isolated fault-injected soak covers local runtime behavior, not real external delivery.

# Stop, failure and recovery

Worker loops observe cancellation, avoid overlapping cycles, and use bounded exponential backoff up to one hour for cycle failures. A timed-out non-cooperative job stays in flight and cannot be reinvoked; heartbeat remains independent of push delivery and collection. Cooperative cleanup drains up to 500 ms before dependency disposal. Host shutdown budget is 15 seconds, and the outbound worker attempts a five-second-bounded /fail shutdown heartbeat.

The helper accepts only the existing typed requests. Its listener waits are bounded, normal stop cancels pending accepts, and transient listener errors back off from 250 ms to ten seconds. Five consecutive listener failures terminate the helper process with exit code 1 so SCM can apply failure recovery. This callback is simulated in tests; no process-exit or live SCM recovery test was run. A clean stop intentionally does not trigger failure recovery.

Suggested future administrator-controlled SCM settings for each installed service:

```powershell
sc.exe failure HostAgent reset= 86400 actions= restart/60000/restart/300000/none/0
sc.exe failureflag HostAgent 1
sc.exe failure HostAgent.Helper reset= 86400 actions= restart/60000/restart/300000/none/0
sc.exe failureflag HostAgent.Helper 1
sc.exe qfailure HostAgent
sc.exe qfailure HostAgent.Helper
```

No reboot, shell command or run-process recovery action is configured. After two restart attempts, investigate rather than loop indefinitely. These commands were documented only. See [Microsoft Windows Service recovery guidance](https://learn.microsoft.com/en-us/dotnet/core/extensions/windows-service).

SQLite failure leaves monitoring degraded and typed remediation denied when the incident/audit cannot be read/written. Collection retries safe work; no fallback executes without audit. Helper absence or an uncertain operation never causes an automatic service-side mutation retry. Helper's durable unresolved claims still block the target after a crash. Preserve the helper journal, identify the real outcome and obtain trusted human review; do not delete claims or clear the database to retry.

Notification state is durable. A claim is persisted as Uncertain before send; crash/cancellation/ambiguous send cannot automatically duplicate it. Explicit rejected requests retry at most three times. Next due is one minute after the recorded cycle timestamp, and worker invocations are separated by heartbeatIntervalSeconds; a late batch completion can therefore be followed sooner than one minute later when that interval is 30 seconds. Accepted emergency messages persist receipts and poll on this same schedule until acknowledgement/expiry; after one day without confirmation, local state expires with a review-required code. Failed/uncertain/expired history degrades health until the recorded condition is reviewed; no automatic deletion/reset endpoint is provided.

Use a watchdog period/grace exceeding heartbeatIntervalSeconds plus the configured request timeout and scheduling jitter. A received /fail establishes reported agent degradation/stop. Missing heartbeats alone cannot uniquely distinguish agent crash, host power loss and internet loss. Compare an independent host/network watchdog to narrow these causes. No such secondary watchdog, live endpoint, outage or soak test was configured.

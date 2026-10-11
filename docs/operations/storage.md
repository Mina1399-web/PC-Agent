# Storage and retention

All paths are local protected Windows data directories, outside source delivery:

| Database | Owner/boundary | Schema | Data |
| --- | --- | --- | --- |
| %ProgramData%/HostAgent/host-agent.db | non-privileged monitoring service | 3 | telemetry, metrics/baselines, incidents/snapshots/evidence, append-oriented audit, remediation attempts, health/checkpoints |
| %ProgramData%/HostAgent/notifications.db | monitoring service | 1 | deduplicated notification history, receipt/current delivery state, attempts/next due time, append-only delivery events |
| %ProgramData%/HostAgent.Helper/helper-journal.db | SYSTEM/Administrators only | 1 | immutable helper audit/approvals/claims and durable guard state |

Monitoring migrations retain Task 1–3 semantics. Notification schema is initialized at version 1; a newer unsupported version is refused. Each pre-send claim and completion/event transaction is durable with SQLite synchronous=FULL, no pooling, bounded busy timeout and parameterized values. A claim survives process restart as Uncertain. Current notification state is mutable delivery bookkeeping; notification event rows reject update/delete. Monitoring and notification writes are separate transactions/databases; the publication recovery test covers the interruption boundary.

Detailed telemetry/metrics/health retention remains at least 30 days, with hourly/daily aggregates preserved. Retention never removes monitoring audit, helper audit/approvals/claims, incident snapshots/evidence or notification history/events. Review disk growth and preserve evidence before any future administrator-approved archival policy; no notification purge feature is implemented.

Back up databases and required sidecars consistently while services are stopped or through a SQLite-consistent backup mechanism. Never transfer runtime databases, DPAPI files, logs or credentials as source patches. Durable receipts are sensitive local delivery handles and stay inside the protected data directory.

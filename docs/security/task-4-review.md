# Task 4 source/security review

Date: 2026-10-11. Read-only independent review in the approved DEV VM using requesting-code-review. No reviewer tests, builds, native actions, services, repository mutations or external publication occurred.

Base commit: 66b59ae332b8ca68ed9c340acf7ec7491e62e390. Initial immutable reviewed tree: 3f69d1884f98f1188dac5125b337970a19aae958. Rereviewed source/docs tree: f4a8a54d02b1b2a02c1cd74754b16a6d19c67d99. Trees were used before the final commit because the user required commit only after full verification. Final documentation/evidence updates do not change the reviewed implementation.

Final source verdict: **approve; all three accumulated P2 findings resolved; no unresolved actionable code/security finding.** No P0/P1 finding was identified.

| Finding in initial tree | Resolution and evidence |
| --- | --- |
| P2 IncidentPublisher.cs:15: dedupe suppressed same-severity reopened occurrences because identity/CreatedAtUtc were preserved | Optional durable OccurrenceStartedAtUtc is set at initial detection/reopening, preserved across ordinary samples/transitions, and used in the notification key. Old JSON falls back to creation time. Persistent reopen regression failed 2 notices vs expected 3 in run 33, passed in 34, also verifies ordinary samples do not spam. |
| P2 OutboundCoordinator.cs:32: notification health refreshed only after a slow batch, hiding errors from concurrent heartbeat | Read durable failures before any network send and after each completion. Regression blocks the second send after the first failure and checks concurrent heartbeat: red 33, green 34. |
| P2 Helper Program.cs:38: ExitCode plus StopApplication could report clean stop before SCM failure recovery | Fatal callback directly calls Environment.Exit(1), while requested cancellation stays graceful. Listener callback/backoff/shutdown tests pass 2/2 in run 32; direct process termination/SCM behavior remains live-unverified. This wiring fix was source-reviewed, without an isolated process-level red/green claim. |

Rereview also requested two factual doc corrections: server emergency retries begin on acceptance even if the client never persists its receipt; receipts enable polling. Retry/poll due time derives from cycle timestamp, so a late batch can have less than one minute wall-clock spacing at a 30-second configured interval. Both statements were corrected; neither correction changes code.

Reviewed boundaries include default Shadow/read-only/disabled autonomous mode, exact fresh approval/durable incident binding and typed audit, helper authority/allowlist/ACL/protocol preservation, uncertainty blocking/no resend, protected CurrentUser DPAPI, strict safe configuration, HTTPS/redirect/TLS behavior, secret/exception/URI redaction, durable outbox/history, independent workers and shutdown/recovery.

Primary self-review also reproduced/fixed audit-to-outbox interruption, approval/typed-parameter audit omission, post-send cancellation persistence, recovery health latching/restart uncertainty, duplicate/unsafe receipt parsing, malformed-config fallback, unsupported notification schema and premature dependency disposal. Their actual runs and fixture/setup exceptions remain in [verification](../verification/task-4.md) and the full ledger.

Live delivery, actual service identities/DPAPI profiles, installation/SCM recovery, real privileged actions, signing and soak remain open deployment checks. See [security assumptions](assumptions.md) and [Microsoft recovery guidance](https://learn.microsoft.com/en-us/dotnet/core/extensions/windows-service); source review does not establish those runtime results.

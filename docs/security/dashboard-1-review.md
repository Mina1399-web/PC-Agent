# Local dashboard boundary review

Date: 2026-10-12. Base d2168957753c3d7ad313b6f18c1aa4400831d546.
Independent reviewer inspected immutable Git trees read-only, without running
tests or changing the VM. Runtime results are in the separate verification
record; review approval is not installed acceptance.

| Review finding | Resolution and fresh regression |
| --- | --- |
| P1: dashboard parsed bare telemetry instead of actual CollectorResult envelope | Parse schema/version/collector/status/Value from real persisted collection job; two observed red assertions, then 14 focused passes. |
| P2: raw sample limits truncate five-second interval history/sustained evidence | 128 recent samples, conservative minute minima; 20,000-query ceiling and 15-minute history buckets. Seeded 17,281 samples in actual SQLite; red then 14 passes. |
| P2: before/after accepted measurements from before CLI completion and allowed another pending action | Track pending, confirmed completion timestamp and reject concurrent begin; red then seven focused Core passes. |
| P2: current CPU/RAM showed minute minimum with latest timestamp | Use last raw current metric separately from recommendation series; observed 42 vs 2 assertion failure, then resumed focused run 42 passes. |
| P2: one fresh after-metric incorrectly labeled both metrics as post-operation | Independently filter each metric after confirmed completion; observed stale RAM assertion failure, then eight Core passes and WPF smoke. |

Initial tree b57281dd6177ab0b4425eb6acb2e270645291250 received request-changes.
Second tree 222b1eb69affe7c9f42172f38236ee13f683597c confirmed earlier fixes
and identified the final two P2 findings. Final tree
ea8a54b6f6b0206b43d6d7d7a631f7d249307c0b received **approve, no confirmed
blockers in the bounded review**. It also covered product-only service
lifecycle and Desktop payload/shortcut gates. Findings remain recorded here
even after resolution.

The integrated design handoff received a further read-only approval at tree
d8d5359dcaf8939ad0678641f66cab708a3e0f41, against the preceding approved
functional tree. No new confirmed blocker: mode/ReadOnly/kill-switch/pending
gates, independent metric timing, stale clearing, cancellation and fixed CLI
flows remain intact. The five original findings remain in this accumulator.
An outside-source deployment harness was separately approved at exact SHA-256
E0A6128C2BF5016C57C93922808C160B60EA7417D23514507A473EE6EA442CA5;
this is code inspection only, not proof of backup/installation execution.

## Retained boundaries

- Normal-user WPF client; separate read-only local interactive Named Pipe,
  network identity denied, service SID pinned, deadlines and 16KiB frames.
  Administrator endpoint retains its existing identity check.
- Strict command allowlist: overview, cpu/memory history, incident, stored
  investigation metadata. Dispatcher has no helper or secret dependency.
- UI receives bounded projections, not database access, raw XML, arbitrary
  observations, executable paths, IP addresses or secret values. Machine-wide
  process names/health/incident codes are explicitly disclosed to local
  interactive users in the privacy documentation.
- Mutations use only the protected installed fixed CLI path through UAC.
  Existing exact proposal/target/digest/GUID approval is retained. Shadow,
  read-only and kill switch deny actions. No general shell/executor.
- Kill switch preserves mode and outbound configuration. Service lifecycle
  preflights unrelated active dependents before any stop, calls Stop(false),
  starts the helper first and has cancellable per-service waits.
- Settings use three fixed Windows URIs. No automatic file deletion, package
  execution, approval, external transport or remediation from recommendations.
- DEV UUID guards and production signing gates remain present. Hosted review
  packaging was updated but not executed; its VM rejection test passed.

Live cross-account/installed/UAC checks and crash/soak/signing limits remain
explicit. Local GUI availability is not a guarantee of OS stability or a
proof that recommended actions repair a crash. No security policy was weakened.

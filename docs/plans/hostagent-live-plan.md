# HostAgent product roadmap and milestones

**Status snapshot:** 26 November 2026

This page summarizes product implementation and acceptance at a public, product level. Integration, installed-system acceptance, and release readiness are separate milestones.

## Integrated

- The initial product slices P1 through P8.1 are integrated on `main`. This does not mean every broader capability within those priorities is complete.
- The P4.1 Hardware Inventory desktop surface and its UI acceptance are complete. Physical-hardware validation is tracked separately.

## In progress

- Desktop sign-in autostart acceptance for HostAgent 1.2.19 is in progress.
- Broader installed-system acceptance remains open.
- Release readiness remains open. Passing CI or preparing an unsigned development package does not by itself constitute a release.

## Next milestones

1. Complete desktop autostart acceptance, including sign-in behavior and the user’s ability to manage startup.
2. Complete broader installed-system acceptance and resolve any evidence-based gaps.
3. Reassess the remaining scope and dependencies before selecting the next product slice.

## Future scope candidates

These areas remain future work; they have no committed dates or fixed order yet:

- Incident timelines that connect recommendations to evidence and later recovery checks.
- More reliable relationships between hardware, storage, and network devices.
- Per-device performance and GPU metrics where supported, trustworthy data sources are available.
- Better context linking processes and services to performance and incidents.
- A read-only Windows security posture view.
- Deeper network and storage diagnostics.

## Product principles

- Preserve the source, time, completeness, and availability of diagnostic data. Unknown, partial, stale, or unavailable data is not reported as healthy.
- Keep diagnostics read-only by default. System-changing actions remain explicit, scoped, and followed by verification.
- Treat automated checks, installed acceptance, and real-device validation as separate evidence.

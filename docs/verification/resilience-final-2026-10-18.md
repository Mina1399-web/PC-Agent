# Chaos / Resilience final assessment — 2026-10-18

## Verdict: CONDITIONAL

The declared DEV chaos gates have scoped successful runtime evidence, including the operator's normal Windows reboot and VirtualBox Power Off. This is not unconditional production or installed-MSI acceptance. The next release contains two additional narrowly tested source corrections and must retain its own build/runtime/package provenance. Deployment remains a separate explicitly stopped gate.

This assessment freshly rehashed eight retained summaries/results and450 payload hashes across six candidates. It did not repeat already successful faults. Old FAIL attempts remain FAIL; later successful individual phases are selected with their own run IDs rather than treating an entire earlier failed campaign as PASS. Exact read-only audit output is `resilience-evidence-audit.json` under `%LOCALAPPDATA%\HostAgent-DEV-evidence\predeployment-20261018T045333Z`.

## Actual gate evidence

| Gate | Result and evidence | Practical limit |
| --- | --- | --- |
| View pipe EOF/timeout/reconnect | PASS for actual isolated IPC; corrected idle accept lifetime, request budget after connect, creation backoff and shutdown grace; source/live ledger in [View stabilization](chaos-view-stabilization-2026-10-16.md) | A stalled client remains deadline-bound after connection; no public listener |
| CPU sustained / service-stop classification | Real high-CPU run produced seven high samples and confidence88; regression tests cover start-to-start cadence and Manual/Automatic trigger-start services | Bounded CPU evidence; not unlimited saturation qualification |
| Automatic SCM crash recovery | PASS phase S1 in7df9273fc92f4002888aa4eea543080c; summary312EAFD698B759E0BEAF5C8D5738ECF5A59B4BBAB457AA3AE5D07A576BAE3E76 | Exact GUID test graph, original r5; protected installed service not the fault target |
| Unavailable / FULL / corrupt storage | PASS three S2 phases in7df9273fc92f4002888aa4eea543080c | Real disposable SQLite lock/quota/corruption and recovery; no C: exhaustion or physical torn-sector claim |
| Collector injection/recovery | PASS59f607f3d6d9409cb7c6edcee69858d1; summaryBE3E3D161D0A67D2EB3271CDA363C2CF375C87AFB17647EEB52F8947DFB89D80 | Private injected collector; not disabling OS WMI/EventLog services |
| Private real helper/remediation and cleanup | PASS14checks in cdab506f11d149b3a62d65e4b2811767; summary79A73E210494AE2A53D60F9716B25B37121F00E3CF972992BC1E515FD39448F8 | Success/failure/timeout/replay/cooldown on GUID test services; no unrestricted or production target |
| Live WPF partial/stale/outage/recovery | PASS a0e02d2045724bcfa5a797d8334fc8b3 WPF phase; summary8EA4F3C41D04EBA0A2A00BC708CA9C6F67DC65D24C17F4A2A3A1B2E6AF7461E9 | Actual test-host window/private IPC; that campaign's later old soak remains FAIL |
| Combined fault endurance | PASS326f1db1d72441e7bb0a94e5496dc03b; summary1EF9287887727A5D860383BC7B73062F46129289F73138ECF73F3943EBA5D192 |1800.8s, six cycles; not days-long or final installed-product qualification |
| 768MiB stop | Operator-observed PASS9aa86a18379245f2940c3224ae6c4967: stop confirmed at766.2MiB, minimum758.2MiB, protected service unchanged | Retained operator output and later read-only DB checks; no new pressure repeated |
| 512MiB hard floor | PASS safe classifier equivalent in58c1c270cd1247b0813628eb7605302a; summary3C891340EA3E06463E9EEBBCA20EBB9367F4F34DFC9C0AB3E1777121CF5B784E | Physical depletion NOT RUN; crossing768MiB stop is prohibited |
| Normal Windows reboot/persistence | PASSc60759c492a840f7b993afc807e7339a; result0550F09BC5FF76599B27DEF0B442995BFCC86ECBFCF93A38E0AA9FCA6EE372DB | Automatic private service start, committed/pending distinction, six acknowledged snapshots; repeated second Verify remains separately FAIL |
| Manual VirtualBox Power Off | PASSf31c58c42ae14f9cbf4c083a592c264e; result51127969B12B20E967580CE870B5D304CF05202E0DFA705BBFFAD43476DC041F; operator confirmed host action | Both DB quick_check ok/committed retained/pending absent/post-boot progress; not host electrical power loss or cross-DB atomicity |

The soak recorded321 successful View reads,19 expected unavailable reads and zero unexpected unavailable,12 injected/recovered collector failures,68 write failures, six simultaneous three-fault overlaps and six fresh-data progress checks. It ended with789 snapshots,10 incidents/42recurrences, no duplicate active correlation and zero helper calls. Both DB quick checks were ok; minimum free839.4MiB and no resource abort. Thirty-minute accepted resource trends do not establish leak freedom over days.

## Product fixes versus harness corrections

Confirmed P2 product fixes in the campaign address View request/idle accept lifetime, connect/retirement race, collection cadence under slow work and false service-stop evidence for automatic trigger-start services. Regression and actual live repetitions are recorded in their original ledgers. Diagnostic confidence remains scope-specific; a bugcheck confidence is not a driver attribution.

Final predeployment review additionally reproduced a P2: null elements in malformed disk/network/Event Log snapshots raised NullReferenceException and suppressed valid CPU diagnosis. Required collections now reject null elements and isolate invalid optional telemetry. Five malformed-element regressions and two bounded health-reason regressions were RED then GREEN; focused26/26 preserved CPU confidence85 and rejected unallowlisted warning text. The associated P3 hid the two fixed service start-mode/trigger health reasons; both are now allowlisted without exposing arbitrary exceptions.

The final P2 release-tooling correction rejects missing service-collision conditions, missing DEV identity conditions and actual false identity expressions in real disposable MSI mutations. These are validator defects, not evidence that the old installed MSI lacked its guards. The corrected validator keeps production structural checks independent of DEV identity.

Numerous historical campaign FAILs were harness defects: nested module import/scope loss, command-line quoting, weak failure classification, fixture-array cleanup ownership, WMI cleanup delays, incomplete soak phase scheduling, and early proof reads before post-boot completion. Corrected runs have their own evidence; no cleanup or safety gate was bypassed to manufacture PASS.

## Provenance caveat discovered during final audit

R5, r6 and r7–r10 are different artifacts. Core stayed identical across those candidates, Infrastructure changed at r6 and Service changed at r7; subsequent r8/r9/r10 product payloads match r7. Test Probe payloads changed for the declared scenarios. The audit did not transfer one artifact's runtime success to another merely because its source branch was the same.

All450 retained payload hashes still match their manifests and inventories contain no extras/reparse points. **Four r6 byte-count fields are stale** (Infrastructure.dll/PDB, PipeProbe.dll/PDB), despite matching payload SHA-256 identities. This is a historical manifest-metadata defect, preserved rather than rewritten; it does not demonstrate byte tampering or negate the exact-hash-bound runtime result. New release validation requires both size and hash; r6 is not a deployable release package.

## Open operational limits

- Final1.2.2 MSI install/upgrade/repair/rollback has not run. Installed protected1.2.1 remains unchanged. Scoped source/runtime evidence is not current installed SYSTEM-helper/virtual-account acceptance.
- Network outage classifications and Pushover/Healthchecks use synthetic transports; real external delivery, actual internet/DNS/gateway interruption and external watchdog configuration remain unverified/off. Monitoring can run with these optional features disabled.
- Physical disk exhaustion, torn sectors, host electrical failure, indefinite WMI/I/O hangs, real512MiB depletion and multi-day soak were not performed. The unsafe physical cases were intentionally replaced with bounded equivalents.
- Power proofs verify per-DB committed/pending behavior and selected acknowledged telemetry rows, not every possible application transaction or atomic commits across databases. Baseline/incident behavior has automated integration evidence and scoped SCM counts, not an assertion of every interruption point.
- Production signing/target-policy acceptance and a safe host transfer remain external gates. No security policy, UAC, production service or snapshot was weakened/changed. No release was deployed or published by this assessment.

The complete final source/build/package checkpoint is [predeployment-2026-10-18.md](predeployment-2026-10-18.md). **CONDITIONAL** is the final campaign verdict; it is not authorization to install an unsigned DEV package on the production host.
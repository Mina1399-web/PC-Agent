# SignPath unsigned candidate 1.2.8 r2 — 2026-10-26

This replaces the earlier preparation candidate for future owner acceptance;
it does not relabel or delete the preserved `34fe3baf...` build. No release,
installation, SignPath admission or signature is implied by an Actions artifact.

- Exact source: `82465b48d6137b4f81a57f33900f3da69c194658`.
- Preserved reference: [`codex/signpath-candidate-1.2.8-r2`](https://github.com/Mina1399-web/PC-Agent/tree/codex/signpath-candidate-1.2.8-r2).
- Successful [run40243225067](https://github.com/Mina1399-web/PC-Agent/actions/runs/40243225067), job121319287226.
- [Candidate artifact12618056110](https://github.com/Mina1399-web/PC-Agent/actions/runs/40243225067/artifacts/12618056110), ZIP322,802,124bytes.
- Expires2026-11-09 at00:11:58 Prague time (API2026-11-08T23:11:58Z).

| Exact item | SHA-256 |
| --- | --- |
| `HostAgent-1.2.8-submission-preview.zip` | `C68119D7F394174FBBB49AC915BCF2B287CABCF59CBF14FF0557321BDA51E033` |
| `HostAgent.msi` | `F3CAA8EBCD8EBE9F163FE2D2E196F83D5B948DEC93EC6BADC2BA0452EF0AA197` |
| `manifest.json` | `5DCF79DC3284B4937EBA64D4F50A7831A1FFE4F0891AE4D4B190A500B136B25A` |

Actual hosted checks: Release build0warnings/0errors;62 PowerShell preparation
checks; Core62/62, filtered Infrastructure452/452 and filtered Desktop13/13,
no failed/skipped tests in those scopes. Directory/ZIP/MSI-table validation
passed for1,234 manifest files and17 owned signing entries. The builder
verified DTF signatures/package hashes and the pinned corresponding WiX source.
Final receipt: `PREPARED_NATIVE_ACCEPTANCE_PENDING`, installerExecuted=false,
signingPerformed=false. GitHub's artifact digest independently matches the ZIP.

SDK nuance: setup requests10.0.401 but `global.json` permits `latestFeature`;
this hosted run actually selected SDK10.0.402. Host runtime was10.0.14 and
10.0.12 was also installed; an installed-runtime listing does not prove the
exact runtime selected for each framework-dependent test. The self-contained
payload remains pinned to10.0.12. DEV verification uses actual SDK10.0.401/
runtime10.0.12 and is recorded separately, not inferred from this hosted subset.

Test-result artifact12617976304 is136,983bytes, SHA-256
`EAF9E1D6DD9C5A0E9AB3C8E896977DBD330C18BDC509FF9B9F7C9F28C306A159`.
The full DEV suite and local/native candidate gates are tracked in the
[verification record](../verification/signpath-submission-2026-10-25.md).
Follow the [owner checklist](signpath-owner-steps.md) before submission.

The fresh complete DEV gate passed642/642tests (62Core/565Infrastructure/
15Desktop) and Release0warnings/0errors at this exact source. The exact ZIP
was then downloaded and hash-checked locally in the approved VM; all1,234
files/17 owned entries and MSI tables passed guarded local Read-SubmissionPlan,
exit0/no watchdog intervention. No MSI session/install/service control ran.
Native acceptance still requires owner action: a fresh host-verified rollback
snapshot and transaction/cache space above the15GiB baseline. No old evidence,
source, protected data or snapshot was deleted to stage this candidate.

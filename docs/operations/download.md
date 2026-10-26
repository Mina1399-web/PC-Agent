# Host Agent V1 downloads

Host Agent V1 is a local Windows monitoring and incident-assistance program
with a Czech desktop dashboard. It monitors system/boot state, CPU/RAM,
volumes and supported disk I/O, processes/services, network configuration and
bounded relevant Event Log metadata. Local SQLite history supports baselines,
correlated diagnoses, confidence, deduplicated incident history and read-only
recommendations. Privileged actions require the separate local helper and
operation-specific policy/approval; there is no public HTTP listener or cloud
remote-command service.

## Download the unsigned preview

[Host Agent V1 1.2.8 unsigned preview](https://github.com/Mina1399-web/PC-Agent/releases/tag/v1.2.8-preview.1)
was published on 2026-10-26 as **pre-release** `v1.2.8-preview.1`.
All four uploaded assets were subsequently downloaded anonymously through their
public URLs and matched the approved hashes and byte lengths.

This is an **unsigned preview for authorized testing in the approved Windows
DEV VM**. It has no SignPath signature, sponsorship or production approval.
Source: [`82465b48d6137b4f81a57f33900f3da69c194658`](https://github.com/Mina1399-web/PC-Agent/commit/82465b48d6137b4f81a57f33900f3da69c194658).
The successful [candidate workflow 40243225067](https://github.com/Mina1399-web/PC-Agent/actions/runs/40243225067)
and separate complete DEV build/test results remain historical source evidence,
not complete installed-product acceptance.

| Public asset | Bytes | SHA-256 |
| --- | ---: | --- |
| [HostAgent-1.2.8-submission-preview.zip](https://github.com/Mina1399-web/PC-Agent/releases/download/v1.2.8-preview.1/HostAgent-1.2.8-submission-preview.zip) | 322,802,124 | `C68119D7F394174FBBB49AC915BCF2B287CABCF59CBF14FF0557321BDA51E033` |
| [HostAgent.msi](https://github.com/Mina1399-web/PC-Agent/releases/download/v1.2.8-preview.1/HostAgent.msi) | 117,670,676 | `F3CAA8EBCD8EBE9F163FE2D2E196F83D5B948DEC93EC6BADC2BA0452EF0AA197` |
| [manifest.json](https://github.com/Mina1399-web/PC-Agent/releases/download/v1.2.8-preview.1/manifest.json) | 222,921 | `5DCF79DC3284B4937EBA64D4F50A7831A1FFE4F0891AE4D4B190A500B136B25A` |
| [SHA256SUMS.txt](https://github.com/Mina1399-web/PC-Agent/releases/download/v1.2.8-preview.1/SHA256SUMS.txt) | 265 | `3ECBD83C4189FFC99339035BD26BCC32C69BE3DF1A36D9E8B33F42D6D6FF49E5` |

The ZIP contains the MSI, manifest, self-contained payload, documentation,
license notices and the immutable WiX source archive required by the component
mapping. The standalone MSI and manifest are the same approved candidate.
`SHA256SUMS.txt` lists the ZIP, MSI and manifest; its own hash is recorded above.
GitHub also generates two source-code archives automatically; they are not
additional uploaded installer assets.

The owner-supplied installed preview confirmed preserved configuration,
matching critical hashes and both services Running during its read-only probe.
Initial acceptance nevertheless returned **INSTALL_ACCEPTANCE_INCOMPLETE**:
CPU/process telemetry remained partial due to denied access to some process
CPU values and executable paths. Offline-copy SQLite checks returned
`quick_check=ok` for `host-agent.db` and `notifications.db`; the absent helper
journal was not checked. A later owner-run recovery returned both services to
Running. These bounded facts do not establish complete application acceptance.

See the [publication verification](../verification/signpath-publication-2026-10-26.md)
and the [candidate record](signpath-candidate-1.2.8-r2.md).
Temporary Actions artifacts remain build-origin evidence with a retention
deadline; use the public release links above for the preserved preview.

## Installation, privacy and limits

Review [installation changes and removal](submission-preview.md),
[privacy/network behavior](privacy.md), [operator instructions](quickstart.md)
and [third-party licensing/source mapping](../legal/distribution-components-2026-10-25.md).
A fresh installation uses Shadow/read-only; outbound probes, Pushover and
heartbeat are off. Upgrades preserve existing choices. Some protected Windows
process metrics can remain unavailable without broader permissions; optional
SMART can be unsupported. These states remain visible rather than fabricated.
External delivery, unrestricted remediation and universal Windows policy
acceptance are not established by the source tests or candidate MSI.

## Code signing policy

See the [Code signing policy](code-signing-policy.md) for proposed roles,
manual approval, signing scope, privacy and the conditional SignPath credit.
Host Agent is not enrolled, sponsored or signed by SignPath at this stage.
The owner must verify both GitHub and SignPath MFA and actual role assignments.

# Host Agent V1 downloads

Host Agent V1 is a local Windows monitoring and incident-assistance program
with a Czech desktop dashboard. It monitors system/boot state, CPU/RAM,
volumes and supported disk I/O, processes/services, network configuration and
bounded relevant Event Log metadata. Local SQLite history supports baselines,
correlated diagnoses, confidence, deduplicated incident history and read-only
recommendations. Privileged actions require the separate local helper and
operation-specific policy/approval; there is no public HTTP listener or cloud
remote-command service.

## Download status

The project has built and artifact-verified a **1.2.8 unsigned submission
preview** in [run 40230180663](https://github.com/Mina1399-web/PC-Agent/actions/runs/40230180663).
No public
installer download is claimed until a release is actually published in
[GitHub Releases](https://github.com/Mina1399-web/PC-Agent/releases).
Temporary Actions artifacts are candidate evidence and require a GitHub
login; they are not a permanent public download page.

Before a public preview is published, its page must identify the immutable
source commit and tested scope, include the MSI/ZIP and SHA-256 receipt, and
state remaining limits. Native installation acceptance for this new preview
is still an owner-run DEV VM gate. The existing DEV MSI must not be relabelled
or distributed as that public preview.

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

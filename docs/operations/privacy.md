# Host Agent data and network behavior

This describes the current V1 source. A trusted signature is not proof of
privacy, safety or acceptance by a particular Windows security policy.

## Local monitoring and storage

The monitor collects Windows/boot facts, CPU/memory/disk measurements,
process/service identifiers and accessible executable paths, network adapter
addresses/configuration, and bounded relevant System/Application Event Log
metadata. It stores telemetry, diagnoses, baselines, incidents, snapshots and
action audits in protected local ProgramData directories. Do not publish
these databases or assume diagnostic data are anonymous.

Detailed telemetry is retained for at least 30 days with hourly/daily
aggregation. Action audits are not automatically removed. Uninstallation
preserves protected configuration, data and secrets; it does not erase them.
See [storage](storage.md) and [operator instructions](quickstart.md).

## Connectivity diagnostics: explicit opt-in

New builds prepared on 2026-10-12 disable active network probes by default,
independently of Pushover and heartbeat. The monitor still reads local adapter
configuration. Disabled probes report Unavailable/network_probes_disabled,
with no target, request, latency or packet-loss measurement. They do not
pretend that connectivity succeeded or failed. Legacy service configuration
without enableNetworkProbes also defaults to off after upgrading to this build.

The elevated local `network-probes on` command displays the targets and asks
the operator to type APPLY before enabling bounded ICMP probes to the selected
gateway and **1.1.1.1**, plus DNS resolution of **example.com** using the
machine's configured resolver. Once enabled, these run during collection,
including Shadow mode. Network operators and receiving services can observe
the source address, DNS query, timing and ordinary protocol metadata. The
probes do not upload telemetry databases or incident evidence. Use
`network-probes off` to disable them; the command restarts only product services.
Pushover/heartbeat settings do not change this independent choice.

Historical unsigned 1.0.2 builds performed these probes automatically and had
no operator switch. Those artifacts were not retroactively changed.

## Optional outbound services

After the operator supplies credentials and explicitly enables Pushover,
the monitor sends an application token, recipient key and a fixed incident
message (incident ID, severity, lifecycle state and numeric confidence) to
api.pushover.net over HTTPS. It does not include raw evidence, executable
paths, event payloads or local account names in that message. Emergency
acknowledgement polling sends the receipt and application token to Pushover.
The provider handles notification delivery to the operator's devices.
Review [Pushover privacy terms](https://pushover.net/privacy).

After the operator supplies an HTTPS heartbeat URL and enables heartbeats,
the monitor sends an empty POST to that URL; /fail denotes degraded/stopping
health. The URL contains the check identifier and is treated as a secret.
The provider can observe check identity, source IP, health and timing.
For Healthchecks.io, see [its privacy policy](https://healthchecks.io/privacy/).
For another compatible provider, review that provider's terms.

There is no product analytics uploader, public listener or cloud command
channel. Optional outbound failure does not stop local monitoring.

## Secrets, publication and signing

Secrets are entered through masked local prompts and protected using Windows
DPAPI under the monitoring service identity. They must never be entered in
source files, GitHub issues, application forms, CI logs or release manifests.
GitHub/SignPath signing preparation concerns reviewed source and build
artifacts only; it must not upload ProgramData, VM configuration or runtime
credentials. The signing service may receive unsigned distributable files
and build-origin metadata, not live host telemetry.

The disclosure and applicable license notices are included in the new MSI
payload and source-review archive; read this document before installation or
enabling a network function. The native MSI has no privacy-consent UI. The
new default does not contact the fixed probe targets; optional network
features require operator action. Provider acceptance of these arrangements
remains unverified; no SignPath approval or production signature is claimed.

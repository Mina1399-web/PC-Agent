# Questions prepared for SignPath — not sent

Project: Host Agent V1, public source
https://github.com/Mina1399-web/PC-Agent/tree/codex/signpath-submission-prep.
This is an application attachment draft. No message or request has been sent.

1. **Initial release and reputation.** This is a new maintained Windows
   monitoring application. We can supply a GitHub-hosted unsigned MSI candidate
   and verifiable build/test evidence. The [1.2.8 unsigned public preview](https://github.com/Mina1399-web/PC-Agent/releases/tag/v1.2.8-preview.1)
   is now published with independently checked ZIP/MSI/manifest/checksum
   downloads. Initial installed acceptance remains incomplete with disclosed
   partial CPU/process telemetry; this is not a production-approved release.
   We have no adoption or independent reputation to claim. Please assess
   whether this project can qualify, and what additional evidence you require.
2. **DTF composite custom action.** The MSI embeds Host Agent's owned managed
   `Preflight.dll` inside a native `Preflight.CA.dll` assembled by WiX DTF 5.0.2.
   Its upstream SfxCA shim and `WixToolset.Dtf.WindowsInstaller.dll` come from
   WiX commit `aa65968c419420d32e3e1b647aea0082f5ca5b78` under MS-RL.
   Source, license and the [input hashes](../legal/distribution-components-2026-10-25.md)
   accompany the installer. We do not request that upstream files be signed
   as Host Agent. May the composite remain unsigned inside the signed MSI,
   with only the owned managed action and owned program files signed? If a
   different composition is required, please specify the supported workflow.
3. **Privacy and system changes.** Fresh installation has all outbound
   functions disabled. Connectivity probes require an operator to approve
   the announced fixed targets; Pushover and heartbeat require operator
   setup. The package includes a full privacy policy and a read-only plan
   reader; the new MSI requires explicit acknowledgement of its two services,
   accounts, Program Files/ProgramData, read-only counter membership and
   retained-data uninstall behavior. There is no new native privacy-consent
   wizard. Please confirm whether this meets your conditional privacy-display
   requirement or whether a native installer UI is required for this design.

The proposed first signing stage enumerates only exact Host Agent-owned PE
paths with product name/version restrictions. It does not select upstream
.NET, SQLite or WiX files. A later MSI/final-manifest stage will be enabled
only after provider configuration and manual approval are in place. No
private keys or production telemetry will be supplied to the build or form.

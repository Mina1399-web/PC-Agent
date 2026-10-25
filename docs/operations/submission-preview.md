# Unsigned submission preview: installation disclosure

This is a Windows x64 installation candidate prepared for a possible SignPath
application. It has no SignPath approval or trusted production signature.
Windows 11 x64 in an authorized test environment is the reviewed target.
Keep Windows security protections enabled. If Windows blocks the package,
stop; do not add trust exceptions or disable protection to install it.

## What installation changes

Installation requires a local administrator. It copies self-contained .NET
programs and legal/privacy documents into `Program Files/HostAgent`, creates a
Start menu **Host Agent** shortcut, and registers two automatic Windows
services: `HostAgent` as `NT SERVICE/HostAgent`, and `HostAgent.Helper` as
LocalSystem. It creates product Event Log sources and protected local
ProgramData directories. The monitor receives read-only Performance Monitor
Users membership through the existing rollback-protected MSI transaction.
The helper accepts only typed allowlisted actions behind independent policy,
identity, approval, cooldown and audit checks.

A fresh installation starts in **Shadow/read-only**. Connectivity probes,
Pushover and heartbeat are disabled. Upgrades preserve existing configuration,
including any previously enabled outbound feature; verify the current settings
before upgrading. No password, notification token or heartbeat URL is required
to install. Do not put these in command lines, MSI properties or CI.

Read [the full privacy disclosure](privacy.md), including local process/event
metadata, retention, optional destinations and the data retained by uninstall.
The full source/third-party license information is in `LICENSE` and
`docs/legal/`; the corresponding WiX source archive is included there.

## Validate before choosing to install

First compare the downloaded ZIP's SHA-256 with the separately trusted build
receipt and extract it to a new directory. Run `tools/Test-SubmissionPreview.ps1`
with the exact expected source commit, version and manifest SHA-256 from that
receipt. Its operations are artifact reads, including MSI `OpenDatabase(...,0)`;
it does not install, evaluate an MSI session or control services.

`tools/Read-SubmissionPlan.ps1` performs that validation and displays this
disclosure and the full privacy document. It has no execution/elevation switch.
For the current preparation, native installation acceptance is an operator
gate in the approved DEV VM and remains pending until its result is recorded.

## Explicit installation and removal

Only after reading the disclosures and approving the exact package, an
administrator may invoke Windows Installer on `HostAgent.msi` with the explicit
property `HOSTAGENT_INSTALL_ACK=YES` and `/norestart`. The MSI refuses
installation/repair/upgrade without that acknowledgement; it never enables
outbound features as part of this acknowledgement. Windows Installer rollback
must remain enabled. Uninstallation does not require the acknowledgement.

Use Windows **Installed apps > Host Agent V1 > Uninstall**, or Windows
Installer `/x` on the installed version's MSI. Uninstall stops/removes the two
product services and installed program files and reverses only the product's
owned counter-membership change when safe. Protected configuration, databases,
audit history, quarantine and secrets remain for deliberate operator handling.
Do not delete those directories as a routine cleanup step.

The existing signed production installer/validator and the exact-machine DEV
installer retain their original gates. This preview does not grant unattended
remediation or change those operating modes.

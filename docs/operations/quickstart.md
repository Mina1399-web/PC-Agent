# Host Agent V1 operator guide

Host Agent has two Windows services and an elevated local CLI. No dashboard, public listener, separate .NET installation or source-code editing is required. The MSI installs self-contained Windows x64 binaries under Program Files/HostAgent. Service startup does not require an interactive login.

## Install

Use a production release whose HostAgent.msi signature is valid, whose publisher thumbprint matches the independently trusted publisher, and whose binaries are accepted by the target's existing Code Integrity policy. A DEV package is unsigned and its MSI is restricted to the approved VM; it is not a production release. Never weaken a policy or import an untrusted certificate to make installation work.

Open HostAgent.msi and approve the normal Windows administrator prompt. The MSI registers HostAgent as NT SERVICE/HostAgent and HostAgent.Helper as LocalSystem, creates Event Log sources, configures bounded crash recovery and starts both services. The SYSTEM bootstrap creates protected ProgramData/HostAgent, HostAgent.Helper and HostAgent.Operator directories and safe configuration only when missing. An unsafe existing data ACL causes installation to fail; it is never silently repaired. Unmanaged existing services with either product name block a new installation.

Initial mode is Shadow/read-only; outbound adapters are disabled, helper registrations and approver lists are empty. Existing configuration/data survive repair and uninstall. A failed MSI installation rolls back its Windows Installer changes; protected data created by bootstrap are preserved for diagnosis.

Before copying or executing product payload, an embedded SYSTEM preflight rejects untrusted owners, writable ACLs and reparse points in the installation tree, including repair and upgrade. The optional elevated PowerShell 7 Install-HostAgent wrapper copies only the MSI and manifest to an Administrators-owned protected staging directory, verifies that copy's publisher and hash, and holds read locks through msiexec. It never installs from the caller-writable source path. Normal cleanup removes its two staged files; an interrupted run may leave a protected staging directory for administrator inspection.

In an elevated terminal:

```powershell
$agent = "$env:ProgramFiles\HostAgent\Cli\HostAgent.Cli.exe"
& $agent --help
& $agent status
& $agent incidents
```

Exit codes: 0 success, 1 rejected/unavailable/cancelled, 2 invalid command usage. Status reports configuration/mode and worker degradation. Each CLI transport has a deadline, pins the server service SID, and sends once.

## Notifications and heartbeat

Acquire your own Pushover application token/user key and Healthchecks-compatible HTTPS ping URL. Enter them only at the masked interactive prompts; never place values in command arguments, JSON configuration, transcripts or support records.

```powershell
& $agent secret-set PushoverToken
& $agent secret-set PushoverUser
& $agent secret-set HeartbeatUrl
& $agent configure Shadow on on
```

Configuration arguments are mode, Pushover on/off, heartbeat on/off. Type APPLY to save and restart only the product services. Secrets are encrypted by DPAPI under the serving monitor identity and stored within its protected directory; the CLI cannot retrieve them. Provisioning secrets does not enable adapters automatically. Cancellation at the prompt has no effect. Replacing a secret takes effect on subsequent reads.

## Permit and approve an operation

Register only an explicitly chosen local target and required operation. Registration does not execute that operation. The current administrator SID is added to the protected approver list. For example, substitute a service you own:

```powershell
& $agent allow-service myservice MyService RestartService
& $agent configure Confirmation off off
& $agent incidents
& $agent propose <incident-id> RestartService myservice
& $agent approve-execute <request-guid>
```

The CLI displays the exact request, deterministic confidence/evidence, SHA-256 request digest and resolved local target before asking you to type the exact request GUID. Approval is durable, digest-bound and expires after five minutes. The monitor rechecks the current durable incident and the independent SYSTEM helper rechecks policy, approval, allowlist, rate/cooldown/retry limits and target identity. Stale/uncertain/low-confidence or mismatched evidence is denied. No automatic approval or unattended switch exists.

For a process, use allow-process alias C:\absolute\program.exe to pin its SHA-256, then propose incident-id TerminateProcess alias pid start-utc-ticks. PID and exact UTC process start ticks are required to prevent PID reuse; no process is selected by a fuzzy name. Executable changes require explicit re-registration. Do not use a production process for acceptance tests.

Each approval also persists the displayed actual service name or executable path/hash. A changed registration during confirmation is refused by the CLI; a change after approval is independently refused by the helper. Legacy approvals without a concrete target are refused. Request a new proposal/approval after reviewing the new target.

Remove a registration with remove-service alias or remove-process alias. Return to safe monitoring with configure Shadow off off. Configuration retains protected timestamp-independent .previous backups beside each file. It preserves existing kill-switch settings. For an emergency stop, an administrator may create the protected ProgramData/HostAgent.Helper/disable-remediation marker; its presence prevents helper mutation. Remove it only after reviewing the cause.

Never repeat an execute after an uncertain outcome: inspect the protected helper journal and service audit first. The CLI does not automatically resend. Audit entries are retained; data retention applies to telemetry/snapshots, not action audit.

## Maintenance and removal

Monitor HostAgent status and Windows Application Event Log. Partial/unavailable telemetry is reported as such; it is not invented. Outbound failure does not stop local monitoring. SCM recovery restarts a crashed product service after ten seconds twice; subsequent failures stop automatic restart until the failure count resets after one day. Requested stops remain graceful.

Use Windows Settings > Apps > Host Agent V1 to uninstall. Windows Installer stops/removes only the registered product services and package files. ProgramData configuration, DPAPI secrets, proposals, SQLite data and audits remain protected and preserved. Archive them under administrator control according to your retention policy; reinstall/repair does not erase them. Review and verify a newer signed MSI before upgrading. Never replace a running service from a user-writable checkout.

## Release engineering

Build/test only in the approved VM using SDK 10.0.401, PowerShell 7 and account-local WiX 5.0.2 plus WixToolset.Util.wixext/5.0.2. Install build tools outside the source tree. The release builder requires a clean source commit and a new output directory outside the repository:

MSI preflight additionally uses WixToolset.Dtf.CustomAction and WixToolset.Dtf.WindowsInstaller **5.0.2**, verified with dotnet nuget verify --all before extraction. Beside the wix tool directory, create dtf-5.0.2/wixtoolset.dtf.customaction and dtf-5.0.2/wixtoolset.dtf.windowsinstaller from the corresponding official NuGet packages. Build-Installer uses the existing Windows .NET Framework compiler and x64 DTF shim; the target Windows installation requires .NET Framework 4.8 or later. No framework/security-policy installation or modification is performed by the MSI. Production signing also signs the embedded managed preflight and its native container.

```powershell
pwsh -NoProfile -File tools/Build-Release.ps1 -PackagePath C:\DEV-Releases\HostAgent-1.0.0 -WixPath C:\DEV-Tools\wix\wix.exe -SigningCertificateThumbprint <trusted-signing-certificate-thumbprint>
pwsh -NoProfile -File tools/Test-Release.ps1 -PackagePath C:\DEV-Releases\HostAgent-1.0.0 -TrustedPublisherThumbprint <independently-verified-publisher-thumbprint>
```

Signing reads an existing CurrentUser certificate with a private key and Code Signing usage; it neither exports keys nor creates trust exceptions. First-party binaries/scripts and MSI are signed before final hashes. The manifest is itself Authenticode-signed and parsed as restricted data, never executed. Verification checks exact file membership, SHA-256/length, path/reparse boundaries and signatures against the pinned publisher. The signed manifest covers bundled runtime/native dependencies as well. Build-Release -DevVm creates an explicitly unsigned VM-only candidate; it cannot produce a signed production release.

Production signing/rotation and policy acceptance require the owner's trusted certificate infrastructure. A successful DEV test or source review is not proof of live delivery, cross-account DPAPI or SCM acceptance. Consult the current [verification record](../verification/v1-release.md) for actual results.

Primary references: [self-contained .NET deployment](https://learn.microsoft.com/en-us/dotnet/core/deploying/), [Authenticode signing](https://learn.microsoft.com/en-us/powershell/module/microsoft.powershell.security/set-authenticodesignature), [WiX service recovery](https://docs.firegiant.com/wix/schema/util/serviceconfig/), [.NET 10 release metadata](https://github.com/dotnet/core/blob/main/release-notes/10.0/releases.json).

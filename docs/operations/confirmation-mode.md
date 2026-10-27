# Writable Confirmation in the DEV VM

The supported writable mode is **Confirmation**, with `readOnly=false` in
the monitor and `shadow=false`, `readOnly=false` in the helper. It permits
reviewed typed operations; it does not enable arbitrary administration or
unattended actions. New installations still default to Shadow/read-only.

Before changing mode, accept the exact installed DEV package: verify VM
identity, candidate manifest, installed executable **and assembly** hashes,
the fixed service accounts/paths, Running state, protected configuration,
and the current kill-switch/allowlist/outbound settings. Preserve the return
point and data. An old preview acceptance that explicitly requires Shadow
cannot validate the post-change state.

After those checks, the existing CLI provides the explicit interactive step
from an administrator PowerShell inside the approved DEV VM:

```powershell
& 'C:\Program Files\HostAgent\Cli\HostAgent.Cli.exe' configure Confirmation off off
```

Read the displayed arguments and type `APPLY` only for this reviewed mode
change. It restarts the two product services, preserves registrations and
kill switches, and adds the current operator SID as an approver. This example
keeps Pushover and heartbeat off; the independent network-probe choice is
preserved. A present disable-remediation marker continues to block execution.

Then obtain fresh `status --verbose`, both service identities/hashes/Running
states, and protected monitor/helper mode readback. Confirmation on disk alone
does not prove that the running services loaded it. A pending restart or a
failed recovery must remain a failed/incomplete acceptance.

Every operation still needs an explicit allowlisted target, current eligible
incident/evidence, the required confidence, the displayed exact request/target,
and a fresh request-GUID approval. The helper independently checks its policy,
approval, journal, cooldown and retry bounds. Do not register all Windows
services or processes to imitate unrestricted access. Test a real operation
only against a separately reviewed disposable DEV target.

The helper journal is lazy: it may legitimately be absent before the first
normal journal operation. Mode switching does not create it or establish its
integrity. Do not fabricate an approval or operation just to create a file.

To return to observation mode, use the same interactive workflow:

```powershell
& 'C:\Program Files\HostAgent\Cli\HostAgent.Cli.exe' configure Shadow off off
```

Current source and actual execution limits are tracked in the
[2026-10-27 completion record](../verification/completion-confirmation-2026-10-27.md).
Those source tests do not imply that the newly built package is installed or
that writable mode is already active.

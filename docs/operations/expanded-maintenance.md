# Expanded local maintenance and disk prediction

Development work from 2026-10-30, on branch codex/expanded-local-maintenance.
These source changes require a new build; configuring the existing installed
services alone does not install them.

## Selected folders

The maintenance window can select a local folder owned by the current user.
This view scans direct files only. It removes the seven-day minimum age and
raises the per-file limit to 1 GiB and quarantine capacity to 10 GiB for this
explicitly selected scope. The ordinary Temp view retains its existing limits.
Every move still needs a current preview ID and fresh Confirmation state.
The folder must be on the same volume as the user profile. Protected Windows,
Program Files, ProgramData, volume roots, reparse paths, shared/writable files,
locked files and overlapping quarantine trees are rejected. This is not a
recursive system cleanup. User file ownership and private ACL checks remain.

Each selected root has its own stable quarantine directory under
LocalAppData/HostAgent/SelectedQuarantine. Re-select the same folder to recover
its pending files. Opening another folder never discards existing quarantine.

## Permanent deletion

The selected quarantined file can be permanently deleted only after entering
DELETE, a space and the exact entry ID. This is separate from the preview
and restore confirmations. The operation verifies the opened file's identity,
size, modification timestamp and hash. Deletion uses the validated handle,
with a durable DeleteIntent before mutation and a Deleted receipt afterward.
Interrupted or ambiguous records remain NeedsReview. Deleted payload cannot
be restored by Host Agent. No automatic purge or recursive deletion occurs.
Metadata audit records remain and record-count bounds still apply. Bounded
preview/hash work protects the constrained VM; limits are not unlimited.

## Disk prediction

The Storage card has a localized Read SMART failure prediction button. Its read runs under the interactive account and is independent of the service token; availability may differ. Both readers cache for five minutes. The UI shows unavailable instead of treating absent data as a healthy disk. WindowsDiskHealthReader attempts the fixed read-only Windows
IOCTL_STORAGE_PREDICT_FAILURE operation. It does not write SMART settings,
run disk self-tests or use vendor commands. A successful response reports
NoFailurePredicted or FailurePredicted. No prediction is not a guarantee of
hardware health. Unsupported/denied requests remain unavailable.

The reader probes at most 64 physical device slots, caches for five minutes,
waits at most three seconds per caller and retains at most one pending probe
when a driver stalls. This adds no command runner or elevated service rights.
VirtualBox's VBOX HARDDISK does not prove visibility of the physical host's
SMART sensors. NVMe wear, temperature, raw vendor attributes and self-tests
are outside this initial failure-prediction implementation.

## Automatic outbound adapters

Run tools/Configure-AutomaticOutbound.ps1 in an elevated PowerShell 7 session.
It uses the installed CLI's masked prompts for PushoverToken, PushoverUser and
HeartbeatUrl, then enables both adapters after APPLY. The existing operating
mode is retained. Do not put secrets in chat, scripts or Git. No transcript is
started. Automatic delivery still requires actual credentials and verification
in the receiving Pushover app / heartbeat provider.

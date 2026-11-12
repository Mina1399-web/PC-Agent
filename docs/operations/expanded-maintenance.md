# Expanded local maintenance and disk prediction

This note records product behavior for the Host Agent V1 maintenance surface.
The Stage 5 implementation passed an independent static review on 2026-11-13.
It has not been built or transferred to the DEV VM; runtime behavior remains
unverified.

## File maintenance in the public V1 surface

Standard mode uses only the current user's `%LOCALAPPDATA%\Temp` directory.
It previews direct files, reports each skipped file with a readable reason,
and can move eligible files to reversible quarantine after a current preview
and an explicit, count-specific action. It can restore one selected
quarantined file to its exact original path. Existing destination files are
never overwritten. Standard hides internal IDs, hashes, and technical paths.

Developer mode adds one explicitly selected local folder and technical
identifiers, hashes, volume data, and full source/destination paths. It does
not raise service permissions or bypass the current service mode, read-only
state, kill switch, snapshot freshness, or the per-operation checks.

Both scopes scan direct files only. The Temp scope requires files to be at
least seven days old, limits an individual file to 16 MiB and a preview to
128 MiB. Developer-selected folders have no minimum age, a 1 GiB per-file
limit and a 10 GiB quarantine limit. Both scopes cap a scan at 500 inspected
files, 100 candidates, 15 seconds, a ten-minute preview lifetime, and 256
quarantine records. An incomplete or expired preview disables the move.

Quarantine is reversible storage on the same volume. It does not free disk
space. Any free-space reading is a separate measurement with its own timestamp
and is not attributed to a quarantine move. Results retain a receipt for each
item, including moved, restored, skipped, blocked, failed, cancelled, and
unknown outcomes. Restore summaries use the restored count.

## Superseded development-only deletion

An October 2026 development branch described permanent deletion after typing
`DELETE` and an entry ID. That behavior is superseded for the public V1
surface: the desktop UI, callable `UserMaintenance` API, and native delete
boundary are removed. There is no public permanent-delete action. Older
`DeleteIntent` and `Deleted` journal entries remain readable for recovery, but
are surfaced as `NeedsReview`; they are not treated as successful or
restorable entries.

## Developer selected folders

The selected-folder scope accepts one local folder owned by the current user.
It scans direct files only, uses a separate stable quarantine directory under
`LocalAppData/HostAgent/SelectedQuarantine`, and rejects protected Windows,
Program Files, ProgramData, volume roots, reparse paths, shared/writable files,
locked files, and overlapping quarantine trees. Source and quarantine must
be on the same volume. Re-selecting the same folder makes its quarantine
available again; selecting another folder does not discard old entries.

## Disk prediction

The Storage card has a localized read-only SMART failure-prediction action.
It runs under the interactive account and is independent of the service
token; availability may differ. Both readers cache for five minutes. The UI
shows unavailable instead of treating absent data as healthy. The Windows
reader attempts the fixed read-only `IOCTL_STORAGE_PREDICT_FAILURE` operation.
It does not write SMART settings, run disk self-tests, or use vendor commands.
A successful response reports `NoFailurePredicted` or `FailurePredicted`; no
prediction is not a guarantee of hardware health. Unsupported or denied
requests remain unavailable.

The reader probes at most 64 physical device slots, caches for five minutes,
waits at most three seconds per caller, and retains at most one pending probe
when a driver stalls. It adds no command runner or elevated service rights.
VirtualBox's `VBOX HARDDISK` does not prove visibility of physical-host SMART
sensors. NVMe wear, temperature, raw vendor attributes, and self-tests are
outside this initial failure-prediction implementation.

## Automatic outbound adapters

Run `tools/Configure-AutomaticOutbound.ps1` in an elevated PowerShell 7
session. It uses the installed CLI's masked prompts for `PushoverToken`,
`PushoverUser`, and `HeartbeatUrl`, then enables both adapters after `APPLY`.
The existing operating mode is retained. Do not put secrets in chat, scripts,
or Git. No transcript is started. Automatic delivery still requires actual
credentials and verification in the receiving Pushover app or heartbeat
provider.

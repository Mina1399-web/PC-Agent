# Current VM guards and health summary — 2026-10-30

Working branch: `codex/telemetry-explanations`, starting at
`9f538b9b875ba75b2fddfcd98bd7c9905ea8b955`, including the previously verified
Czech telemetry explanations. Changes remain local and uncommitted.

## Implemented

Active scripts, modules, fixture expectations and campaign JSON now use
the rebuilt VM UUID/account. Current agent/development instructions identify
the new environment; dated historical verification documents are preserved.
An active-source search found no previous VM UUID, deleted checkpoint UUID,
old account name or old checkpoint name in `.ps1/.psm1/.cs/.json/.py` files.

`tools/Confirm-DevSnapshot.ps1` queries the approved VM's actual host snapshot
inventory. The current `clean-win-base` UUID
`3b3a6a0e-d861-4d25-9997-b39c407058a5` was found; the deleted checkpoint was
correctly rejected. The guest validator accepted the real transferred host
receipt. It rejects missing, stale, future, wrong-VM and empty-UUID receipts.
The manual runners require a fresh receipt before their executed stages;
read-only preflight no longer invents a verified checkpoint. Manifest fields
declare a rollback candidate instead of claiming operator verification.

Receipts attest only to an inventory observation, not backup recoverability.
The current baseline predates restored tools. Historical campaign package
paths/hashes remain guarded; fresh campaign candidates/dependencies are still
required before an actual destructive resilience campaign. No such campaign
or snapshot restoration was executed. See the [operator guide](../operations/dev-vm-snapshots.md).

The overview now summarizes reported component limitations, missing current
CPU/RAM measurements and where to find details. Missing health rows do not
become a healthy result. Stale/future individual metric timestamps are
recognized even inside a fresh overview. Disconnect/stale overview clears
the prior summary. IPC, collector rights and remediation capabilities were
not extended.

## Executed checks

All builds/tests ran in the verified VirtualBox guest `AGENTPC`, UUID
`0bb543d1-7bb0-43ed-9c94-596d63ef3310`, non-elevated `AgentPC\vmdev`,
VBoxService Running, pinned SDK `10.0.401`. Guest test copy:
`C:\Users\vmdev\source\PC-Agent-telemetry` (archive-based working tree).

| Check | Result |
| --- | --- |
| Parse all PowerShell scripts/modules | PASS |
| Snapshot receipt fixtures | 6/6 PASS, no power operations |
| Existing pure power-off transition/rollback fixtures | 14/14 PASS, no service control |
| Release solution build | PASS, 0 warnings/errors, 3m49.55s |
| Desktop real-window suite | 22/22 PASS, 0 failed/skipped |
| Core suite | 62/62 PASS, 0 failed/skipped |
| Infrastructure suite | 565/565 PASS, 0 failed/skipped, 10m28s |
| Source whitespace check allowing existing CRLF convention | PASS |

The first valid-receipt test exposed PowerShell's automatic ISO JSON-to-DateTime
conversion: localized text lost timezone information. The validator now
preserves DateTime Kind and uses invariant parsing for string timestamps;
the rerun and real host receipt passed.

Tested code overlay SHA-256:
`22B895585C0923918F0686425170F213EAF7C0E42F245E44A162DEE3F0951BEE`.
Later documentation-only edits do not change tested executable source.
Logs/TRX: `C:\Users\vmdev\Downloads\next-verification-feature`, read back
outside source to `HostAgent-Rebuild-Work\verification-20261030`.

## Installed service observation

After overnight idle, Guest Control required a normal ACPI shutdown/start;
the shutdown completed, so the attempted reset did not execute. Host/guest
security policies, the original VM, Home Assistant and baseline checkpoints
were preserved.

The installed 1.2.8 services remain registered as Automatic but were observed
Stopped. System events 7000/7009 record both services exceeding the 45-second
SCM connection deadline during cold boot. No installed binaries were replaced
by this feature. A normal remote Start-Service lacked permission; RunAs was
rejected because Guest Control has no interactive desktop. The VM console
was locked, so the owner was asked to unlock it for interactive service
administration. This observation did not invalidate the executed source
and WPF fixture results.

### Service-start completion after owner unlock

After the owner unlocked the guest, the existing Windows Services console
was opened through Explorer. HostAgent.Helper and then HostAgent were
started using the console's ordinary Start control. No installed binary,
service registration, startup type, privilege or security policy was changed.

Independent SCM samples at `2026-10-30T11:18:30.7409984Z` and
`2026-10-30T11:20:26.6382287Z` both reported Running/Auto, exit code 0 and
unchanged PIDs: HostAgent 8772, HostAgent.Helper 5524. Read-back JSON files
are in the external host evidence directory, `agent-services-20261030-sample*.json`.

An authenticated, bounded read-only request using the same ViewPipeClient
and pinned service SID as the desktop returned `ok=true`, `code=overview`,
fresh timestamp, `mode=Shadow`, `readOnly=true`, CPU/RAM present and 10 health
components. Degraded health remains specifically due to protected-process
metrics: `some_process_metrics_access_denied` and `process_cpu_time_access_denied`
in cpu-memory/process-services and the corresponding collectors aggregate.
Global CPU/RAM availability is not treated as complete per-process coverage.

This completes manual recovery and current monitoring readback. Another
cold-boot acceptance run was not performed, so future automatic-start
reliability is not claimed by this record.

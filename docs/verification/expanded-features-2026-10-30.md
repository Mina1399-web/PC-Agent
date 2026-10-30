# Expanded local features verification — 2026-10-30

Base 8d9acc15772f1dea7c01669c8289b506c4d083d0. Branch
codex/expanded-local-maintenance. Execution verified inside AGENTPC,
VirtualBox UUID 0bb543d1-7bb0-43ed-9c94-596d63ef3310, non-elevated
AgentPC/vmdev, VBoxService Running, SDK 10.0.401. Ordinary builds and tests
ran only in the VM. Host work was editing, Git and evidence transfer.

The first overlay build failed because its older guest base lacked the
localization files. A full current source archive was transferred to the
separate C:/Users/vmdev/source/PC-Agent-expanded directory. It built with
zero warnings and zero errors. Its 53 focused infrastructure checks passed.

The expanded UI initially exposed overlapping refresh/action timing. The
window now disables input during an action and retains selection by entry
ID. Its tests wait for real control readiness without increasing deadlines.
A later resource-name collision was corrected by assigning a distinct
SmartPredictionUnavailable key. Failed logs remain in r3/r4/r6; later
successes do not erase them.

Final scopes: Core 62/62; Infrastructure 572/572; Desktop 51/51. Zero failed
or skipped tests in these final runs (685 total). The final desktop build
had zero warnings/errors. HOSTAGENT_TEST_LIVE_NETWORK=1 was explicitly set.
This exercises real bounded connectivity requests and unavailable-result
handling; it does not establish outage classification in every network.
The Infrastructure suite used synthetic targets/files for mutating operations.
The WPF suite includes actual quarantining and permanent deletion of its
own synthetic file after the separate DELETE+entry-ID confirmation.

Raw evidence remains in the guest Downloads directories
expanded-features-verification-r2, r3, r4, r5, r6, r7 and
expanded-features-regression. Selected TRX/logs are read back outside the
repository to HostAgent-Rebuild-Work/expanded-feature-evidence.

Native SMART prediction under the interactive VM account returned zero
predictions; no physical-disk health claim follows. The synthetic UI covers
unavailable, failure predicted and no failure predicted, including language
switching without an extra probe. No vendor writes or disk self-tests ran.

The owner approved elevation to update the installed 1.2.8 configuration.
Readback confirmed Confirmation, readOnly=false, killSwitch=false,
enableNetworkProbes=true, both product services Running. Pushover and
heartbeat remain false because the owner does not yet have credentials.
The local masked setup shortcut is prepared for automatic outbound delivery.
No credentials were requested in chat or added to the repository.

An isolated Documents/HostAgent-Feature-Sandbox-* folder contains only three
created demonstration files, including a recent 17 MiB file. User data were
not used for deletion tests. Native installation of the forthcoming new
package remains pending at this source checkpoint. An unsigned DEV package
is restricted to the approved VM and is not a signed production release.

bd prime was attempted; bd is unavailable on this host, so no Beads issue
or persistent Beads note was created. No substitute task tracker was used.

## Native DEV upgrade and installed readback — 2026-10-30

The clean source checkpoint 1515ec19c2b6c06f5b2e1f4ab22268a41075bd1a was
cloned from a verified local Git bundle inside the approved guest. The actual
SDK was 10.0.401. Build-Release -DevVm -Version 1.2.9 completed successfully,
including MSI-table/package/manifest validation. The new unsigned package is
DEV-VM-bound; no SignPath signing, public release or host installation ran.

Exact MSI: 103171036 bytes, SHA256
6FD5CE0ECC9C977D11908C8E1EF24EC8C2C48D45DF32699D7BFF46F7CC397F80.
Manifest SHA256:
1E95F2AA30EA4F17D27DA24856FE58224B9465C998DB44462A922380DC47DDEA.
ZIP: 297517950 bytes, SHA256
EDF84F35B89BC82EBBCFB3ACC4B9EA2CFAC7613909B266657A07971BA38E8235.
All three were copied to a separate host evidence folder and their hashes
matched independently; none was executed on the physical host.

A live VirtualBox snapshot stalled during machine-state saving and was
canceled via its uniquely identified cancelable IProgress operation. The
result was VERR_SSM_CANCELLED and the VM returned to Running. The subsequent
ACPI shutdown did not complete within 60 seconds. The earlier owner approval
for a hard restart was used, followed by an offline disk snapshot:
before-expanded-features-1.2.9-offline-20261030,
UUID f9b28b52-44b2-4e38-9a6e-37174de0f62c. This is inventory evidence, not a
successful restore test; its disk state followed an unclean guest shutdown.
The VM then booted, Guest Control responded and both old services were
observed Running. No original snapshot or disk was removed.

After the owner unlocked Windows, launched the prepared 1.2.9 shortcut and
approved UAC, the fixed installer wrapper stopped only product services,
created a protected configuration/database-copy directory at
ProgramData/HostAgent.UpgradeBackups/before-1.2.9-20261030-172505 and invoked
the existing protected-staging installation path. Its receipt reports
installed=true, restartRequired=false.

Installed acceptance observed at 2026-10-30T16:28:28Z:

- Desktop ProductVersion 1.2.9; all 18 selected installed first-party files
  match the reviewed package SHA256 values.
- Both product services Running. Before/after service/helper configuration
  file hashes match exactly. Confirmation, readOnly=false, killSwitch=false
  and active network probes remain enabled.
- The installed authenticated View pipe returned overview successfully.
  Its generation timestamp was current; CPU/RAM sample age was about one
  minute. This establishes live installed IPC/telemetry, not an exhaustive
  manual GUI acceptance or approval of a real user-target repair.
- Administration, diagnostics, publication, system, network and Event Log
  reported healthy. CPU/process collectors remain partial because Windows
  denies some per-process CPU metrics and executable paths. Optional SMART
  prediction remains unavailable under the installed service identity.
- Pushover and heartbeat remain disabled pending the owner's credentials.
  Their prepared local setup workflow is not a real-delivery PASS.
- One idle process snapshot showed Service working set 195.3 MiB/private
  166.6 MiB and Helper working set 41.5 MiB/private 9.4 MiB. No Desktop was
  running in that sample. These are instantaneous per-process measurements,
  not unique physical RAM totals, a soak test or a performance comparison.

Readback JSON, installer receipt, hashes and final TRX remain outside source
under HostAgent-Rebuild-Work/expanded-feature-evidence. No new runtime code
changed after the tested checkpoint. GitHub publication of this branch has
not been performed in this continuation.

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

# Read-only performance counter permission

The 1.2.7 DEV candidate introduces installer-managed membership of the fixed
virtual monitor account `NT SERVICE\HostAgent` in Performance Monitor Users
(`S-1-5-32-558`). This is counter-read access, not administrator/debug access.
The monitor's configured service account and Shadow/read-only defaults remain.
The account/group names are never arbitrary installer parameters. Windows
resolves the localized group from SID558.

The MSI requires rollback enabled. SYSTEM deferred actions grant after
InstallServices and before StartServices. They release installer-owned
membership on final uninstall after StopServices and before DeleteServices.
Removal of the old product during a major upgrade preserves that membership
and its provenance for the new product. Repair is a new attempt, not a replay
of the original ProductCode's transaction.

If membership already exists without installer ownership, installation does
not adopt it. Neither rollback nor uninstall removes it. This includes the
membership explicitly added by the operator in this DEV VM on2026-10-22.
The native "already member" result likewise does not establish ownership.

Installer metadata is separate from application configuration and databases:
64-bit `HKLM\SOFTWARE\HostAgent.InstallerCounterRead`, writable only by SYSTEM
and Administrators. `OwnedMember` identifies only the fixed monitor SID.
`TransactionJournal` is one bounded record containing a fresh attempt GUID,
fixed SID and before/after/confirmation flags. It has no credentials or
telemetry. A committed record retains rollback evidence until the next
serialized MSI attempt may replace it. A rollback only selects its own ID.

SAM membership and registry persistence are not a single atomic transaction.
If an interruption leaves an unconfirmed change, installation fails closed
and retains its journal instead of guessing whether it may remove membership.
Keep the MSI log and bounded result; obtain read-only service, membership and
journal metadata before planning recovery. Do not delete the journal, change
ACLs or repeat a failed mutation merely to clear the gate.

The action's commit/rollback uses the fixed name-derived HostAgent SID even
after service registration has been removed. Before a forward mutation it
also verifies Windows' registered account translation matches that exact SID.

## Process metric diagnostics

Fixed read-stage reasons distinguish identity, name, CPU-time, working-set and
start-time access denial. They describe already attempted reads and add no OS
queries, process identities, paths or exception text. The exact known Idle
PID0/native5 limitation remains excluded. Other/unknown process denials remain
partial/degraded; a stage reason alone cannot identify a protected process or
justify broader service rights. See `status --verbose` / authenticated View.

## Verification boundary

This source/candidate preparation does not establish native elevated MSI
install/upgrade/repair/uninstall/rollback acceptance. Unit fakes, actual
disposable MSI structure/mutations and read-only DTF preparation sessions are
separate evidence. Follow the current verification record for completed
runs and the operator gate before installing a candidate.

## Isolated native transaction gate

`scripts/diagnostics/Test-CounterReadMsiFixture.ps1` prepares a GUID-scoped,
operator-only test of these native actions. Its default invocation is read-only.
Execute requires a separately reviewed external JSON manifest and its exact
SHA256, covering the current source, compiler/WiX/DTF tools and six expected
critical files from the reviewed installed release. A newly observed hash alone
is not trusted provenance. Obtain the exact reviewed command from the current
verification record; do not reuse a command for changed source or installation.

The fixture has its own local group, virtual demand-start service (never started),
ProgramData/ProgramFiles package and protected HKLM journal. A build-time copy of
the native adapter changes exactly four target literals; product sources and
the protected HostAgent/builtin558 membership are not changed. The private MSI
exercises missing/preexisting membership, deferred grant rollback, preservation
of an unowned seed, a later commit failure, owned grant and uninstall rollback/
release. These cases do not substitute for a real product upgrade/repair matrix.
Every expected1603 must also contain exactly one fixed deferred/commit fault
receipt from the intentional failure action; an unrelated early1603 with the
same before-state is rejected. The client saves only counts of two exact INFO
markers, never raw MSI UI text. Checked action ordering and the final state
proof remain separate requirements. Deferred failure is after Apply, whereas
the late commit failure is after the checked commit action.

Owner-authorized limits for this gate are now: initial free RAM strictly above
768MiB; runtime below768MiB is warning-only; at/below512MiB is an emergency abort.
Disk10GiB,250ms monitoring,90s cooperative MSI deadline and120s client deadline
remain. The script records start/minimum/end memory and verifies post-run
recovery above768MiB within30s. The MSI client can be cancelled cooperatively;
on hard timeout only that owned client can be terminated. It never kills the
SYSTEM Windows Installer server. Unconfirmed completion preserves evidence and
stops subsequent phases/cleanup.

Successful cleanup removes only the fixture's empty group and stopped service
registration. Its bounded files, receipts and registry journal remain for review;
old evidence is never automatically removed. If post-cleanup memory recovery
fails, the already removed group/service need not remain, but file/journal
evidence is retained. A failure before cleanup preserves the new fixture state
for exact inspection. Do not rerun, delete its journal or perform broad cleanup
to turn an incomplete result into PASS.

References: [limited-user counter access](https://learn.microsoft.com/en-us/windows/win32/perfctrs/limited-user-access-support),
[MSI rollback actions](https://learn.microsoft.com/en-us/windows/win32/msi/rollback-custom-actions),
[commit-action failures](https://learn.microsoft.com/en-us/windows/win32/msi/commit-custom-actions).

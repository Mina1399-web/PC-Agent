# Installed telemetry stabilization and Confirmation readiness — 2026-10-18

The operator upgraded the approved DEV VM from1.2.1 to1.2.2 R3 and reported
installed=true/restartRequired=false. Fresh read-only acceptance confirmed
version1.2.2, both services Running/Auto under expected accounts, all1200
installed files matching R3 SHA-256 and lengths, configured two ten-second
SCM restart attempts, three authenticated View successes, advancing CPU/RAM
timestamps and pre-upgrade CPU/memory history still available. Exact evidence:
`%LOCALAPPDATA%\HostAgent-DEV-evidence\installed-acceptance-20261018T145618799Z`.

This is partial installed acceptance, not a fully healthy result: disk I/O
counters are unavailable and process metrics/paths are partial.
The View correctly reports degraded=true. Root causes are not inferred from
warning names alone. The installed native SQLite version read without opening
a database is3.53.3; operator Python SQLite is3.53.1.

## Boundaries and return point

Only HostAgent-DEV UUID46543236-8C5E-49DA-953C-DB17F9B1E402. The verified
complete Git bundle/source handoff at37f749032984453a38677ea8c58b5ecbd47fbe4a
preserves the current source and commits; work continues on
codex/installed-telemetry-stabilization. The operator confirmed the new
pre-upgrade-1.2.2-r3 snapshot; its host-side UUID was not independently read
in this turn. Do not overwrite old evidence, snapshots or candidates.

Installed services/configuration/data are not edited by the agent. Any later
elevated deployment or protected administrative inspection is an explicit
operator-run step. No UAC/security weakening, arbitrary executor or generic
full-access mode. Shadow/read-only remains until readiness is demonstrated.
Keep initial1024MiB/250ms soft768/hard512MiB/C:20GiB guards for validation.

## Work sequence and falsifiable gates

1. Determine exact PDH failure stage/status without changing counters or groups.
   Distinguish access, absent/disabled counters, localization and product errors.
   If elevated comparison is necessary, provide only a read-only operator probe.
2. Trace partial process evidence against the specification. Expected inaccessible
   optional details may remain informational only if mandatory host data is
   demonstrably usable. Retain bounded reasons and partial telemetry; never hide
   real mandatory collector failures or mark fabricated data healthy.
3. For each confirmed defect: meaningful failing regression, minimal fix, focused
   tests, read-only live comparison and full Release build/suite. Serialize heavy
   work. Preserve raw failure classification and stop on unresolved safety state.
4. Complete administrator-only configuration/SQLite read-only acceptance through
   one reviewed operator script; do not dump secrets, database rows or raw logs.
   Live WAL reads must include committed WAL state; never use immutable mode on
   a database being modified by the running service or discard its sidecars.
5. Publish one new versioned DEV candidate only after fixes/verification. Installed
   1.2.2 must not be overwritten from the checkout. Actual new upgrade is operator
   controlled with a return point; recheck hashes, identity, telemetry and View.
6. Assess Confirmation readiness separately: valid configuration, reachable
   helper, healthy required storage/collectors, exact operation approvals,
   allowlisted targets, retained audit, bounded retry/cooldown and kill switch.
   Outbound remains disabled unless separately configured. No blanket targets,
   automatic approvals or silent switch. Unsupported features remain explicit.

No new product capability is added merely to make health green. Existing broad
chaos PASS scenarios are not repeated unless a specific change justifies it.

## Verified progress

The optional executable-path health defect was reproduced and corrected with
23/23 focused tests. Native process metric access denial now has a precise
bounded reason, tested RED/GREEN 8/8; it remains degrading, and actual ordinary
collector execution also observed non-access-denied failures. Final Release
build and complete suite passed466/466 with unchanged resource guards.
See ../verification/installed-telemetry-stabilization-2026-10-18.md for exact
commands, fresh receipts, independent review and explicit installed limitations.
The operator-only read-only disk/three-database probe is prepared and validated
on disposable fixtures; its elevated installed run remains pending. No installed
source replacement, permission/group changes or mode switch has occurred.

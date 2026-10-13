# User maintenance and test readiness implementation plan

> **For agentic workers:** Use the host's available task-by-task implementation workflow. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Complete bounded, reversible user-file maintenance and verify the installed application for DEV acceptance testing.

**Architecture:** The unelevated desktop manages only top-level files in the current user's fixed LocalAppData/Temp directory. It uses handle-pinned file identities and atomic no-replace rename into a private same-volume quarantine; the privileged helper and its protocol remain unchanged. Fresh service state, exact one-use confirmation, durable per-file intent and verified restore guard every mutation.

**Tech Stack:** .NET 10.0.401 / Windows, C#, WPF, Win32 file handles, JSON journals, xUnit.

## Global Constraints

- Approved VirtualBox DEV UUID 46543236-8C5E-49DA-953C-DB17F9B1E402 only; source base a1b25e86fe0df830df67e6aede5cdbcb3f471ea1, branch codex/local-dashboard.
- Preserve prior commits, original untracked audit, clean-win-base and all production security policies. Return branch codex/dashboard-1-verified and outside-source audit backup preserve the starting state.
- No arbitrary paths, recursive deletion, elevation, helper filesystem capability, permanent erase, external delivery or real secrets. Actual filesystem tests use only generated fixture files.
- Shadow, read-only, kill switch, disconnected/stale state and elevated desktop tokens deny mutation. Preview is read-only. Recheck state before each move.
- Engineering defaults chosen under the owner's autonomous implementation authorization: top-level regular single-link files at least seven days old; at most 500 inspected / 100 proposed files; each at most 16 MiB; total quarantine 128 MiB / 256 journals. Ten-minute preview expiry and exact GUID confirmation apply to the entire immutable preview. Restore requires its own exact entry GUID and never replaces an existing original.
- Quarantine does not reclaim disk space. No automatic expiry erases content; recommend review within 30 days and stop accepting files at the quota. Package install/remove/update remains guided Windows UI: no unattended installer or unsupported-package executor is added.
- Failures have stable codes and explicit per-file partial results. No raw file content or exception/path text enters logs, source or verification records. Recovery classifies durable intents; it never automatically moves/erases a file.

---

### Task 1: File preview and native safety boundary

**Files:** Create Infrastructure/Maintenance/{UserMaintenance.cs,MaintenanceFiles.cs}; tests/HostAgent.Infrastructure.Tests/UserMaintenanceTests.cs.
**Interfaces:** UserMaintenance.CreateForCurrentUser(stateReader); internal constructor accepts fixture roots, clock and fresh-state reader. PreviewAsync returns immutable GUID/expiry/bounded names, sizes, hashes and age; native handles retain volume/file identity and deny write/delete sharing.

- [x] Add and run behavioral preview tests; observed red runs and passing focused runs are in the account-local `HostAgent-DEV-evidence\maintenance-1` directory.
- [x] Implement age/size/single-link/reparse exclusions, pinned directory ancestors, bounded enumeration and no preview mutation. Focused tests cover changed contents, replacement, locked files and resource bounds.
- [x] Focused preview/quarantine tests pass; independent review fixes are recorded in this task's handoff and fresh review is pending final code stabilization.

### Task 2: One-use quarantine, journal and restore

**Files:** Same maintenance implementation/tests.
**Interfaces:** QuarantineAsync(previewId,confirmation,token) and RestoreAsync(entryId,confirmation,token) return stable code/count/bytes; ListAsync returns bounded recovery states.

- [x] Add failing tests for fresh Confirmation success, Shadow/readOnly/kill/stale/missing denial, wrong/expired/replayed confirmation, changed identity/content, root substitution, quota and partial failure.
- [x] Persist flushed intent before atomic handle rename with replace=false. Pin handles; validate journal/payload identity and size before restore; never overwrite. Interrupted intents remain explicitly restorable after validation.
- [x] Add restart/restore/replay/conflict tests and observe red then focused green. A final null-field regression was also observed red and fixed; full release review and commit remain pending.

### Task 3: Desktop integration and acceptance

**Files:** Create src/HostAgent.Desktop/MaintenanceWindow.cs; extend MainWindow.xaml.cs/MainWindow.xaml with a maintenance entry only; tests/HostAgent.Desktop.Tests/MaintenanceUiTests.cs; docs/operations/dashboard.md, docs/specs/host-agent-v1.md, new docs/verification/maintenance-1.md and docs/security/maintenance-1-review.md.
**Interfaces:** Separate Czech window shows preview, complete approval target, limits, quarantine entries and explicit restore confirmation. Queries real authenticated service state immediately before mutation. Main dashboard styling remains unchanged.

- [x] Implement the separate Czech window and test preview/exact confirmation, Shadow blocking, restore target and manual confirmation using synthetic fixture files. Stale clearing/cancellation UI tests and actual Temp execution remain unverified.
- [x] Run final focused suites, complete Release build/suite after the last code change, independent review and diff/secret checks. Latest Release result: 0 warnings/errors; Core 58/58, Infrastructure 264/264, Desktop 14/14. Independent read-only review approved with no outstanding findings.
- [ ] Commit verified files; build a unique clean-source 1.2.0 DEV candidate and validate release/MSI/shortcut. Preserve installed 1.1.0/DB before any normal UAC product-only upgrade. Verify live view pipe, desktop lifecycle and fixture-only maintenance; record remaining live limits without claiming host deployment or crash repair.

Fresh execution evidence and exact release gate results are tracked in
[`maintenance-1.md`](../verification/maintenance-1.md). Do not treat historical
dashboard acceptance as verification of the new maintenance feature.

No unresolved product decisions block this bounded implementation. General application execution, permanent deletion, automatic driver repair and production signing remain outside this capability.

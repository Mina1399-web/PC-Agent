# Counter-read fixture disk floor follow-up — 2026-10-27

## Change

The active isolated counter-read MSI fixture guard now uses a 10 GiB free-space
floor. Its preparation check, PowerShell parent monitor, and native MSI client
use the same threshold and stable `*_10_gib` failure codes. The native client
reports a failed disk-space probe separately from a confirmed low-space result.
The operations guide reflects the current 10 GiB value.

Historical verification records still describe the limits and results that
were actually in force during those earlier runs; they were not rewritten.
Other release transaction space calculations remain separate because they
include candidate and installer working-copy sizes, rather than this fixture's
disk floor.

## Fresh verification

Environment was checked before running builds/tests: computer `AGENTPC`,
account `AgentPC\AgentPCdevtest`, model `VirtualBox`, VM UUID
`46543236-8C5E-49DA-953C-DB17F9B1E402`, and `VBoxService` running. The checkout
was `codex/desktop-localization` at `552fbca565865faee3ab03a25181f9fec3196c73`.
Pre-existing edits in `docs/verification/final-acceptance-2026-10-25.md`,
`docs/verification/installed-readonly-diagnostics-2026-10-26.md`, and
`scripts/diagnostics/Run-InstalledDatabaseIntegrityOffline.ps1` were preserved.

### Red/green boundary tests

Before changing the implementation, the focused Release test
`Actual_resource_guard_distinguishes_initial_headroom_from_runtime_emergency`
was changed to supply exactly 10 GiB. The test failed for both memory-accepted
cases (`768.1 MiB` initial and `700 MiB` during-run), because the existing
15 GiB guard rejected them. The other two memory-abort cases passed.

After the implementation change, this command passed 7/7 tests, including
exactly 10 GiB accepted, 9.999 GiB rejected, the memory guard cases, and the
native controller boundary at 10 GiB minus one byte, exactly 10 GiB, and plus
one byte:

```powershell
dotnet test tests/HostAgent.Infrastructure.Tests/HostAgent.Infrastructure.Tests.csproj -c Release --no-restore --filter 'FullyQualifiedName~Actual_resource_guard_distinguishes_initial_headroom_from_runtime_emergency|FullyQualifiedName~Actual_controller_disk_floor_accepts_exactly_ten_gib_and_rejects_less' -m:1 -p:UseSharedCompilation=false -nr:false
```

The previously blocked isolated builder test was then run directly and passed
1/1:
`Private_builder_compiles_CSharp5_and_real_Wix4_MSI_without_installing_or_creating_native_targets`.
It compiles a disposable fixture MSI and inspects its MSI tables; it did not
execute the installer or create native targets. Its first attempt reached the
JSON receipt with an expected resource-warning stream preceding it; the
test-only wrapper was adjusted to suppress that warning stream, and the rerun
passed. The runtime safety checks themselves were not lowered.

### Release build

```powershell
dotnet build HostAgent.sln -c Release --no-restore -m:1 -p:UseSharedCompilation=false -nr:false
```

Result: PASS, 0 warnings, 0 errors. The build ran in the verified DEV VM with
1,145.1 MiB free RAM and 14.77 GiB free on C: at start.

`git diff --check` passed. Search of the active counter-read diagnostics,
guard tests, and operations guide found no remaining 15 GiB counter-fixture
guard. Earlier verification records retain their historic 15 GiB statements.

## Scope limits

The complete solution test suite was not rerun. The actual elevated fixture
matrix, MSI installation, Windows service operations, and installed-product
acceptance were not run. The change updates only the DEV fixture's disk-space
preflight/monitor threshold; it does not modify product monitoring behavior,
Shadow/read-only mode, permissions, services, or installation configuration.

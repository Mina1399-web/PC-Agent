# Telemetry explanations — 2026-10-29

Source base: `9f538b9b875ba75b2fddfcd98bd7c9905ea8b955`, branch
`codex/signpath-submission-prep`. Work is isolated on `codex/telemetry-explanations`.

The desktop health table displays Czech explanations for the process, CPU,
memory and service collector reason codes. Technical health codes remain
visible and raw reason codes are retained in the cell tooltip. Unknown
reasons remain visible; missing reasons do not become a healthy result.
The converter does not modify telemetry, collector permissions or IPC.

New focused tests cover missing explanations, mixed known/unknown reasons,
preservation of input diagnostics and the distinction between unavailable
system CPU and restricted per-process CPU measurements. A real-window WPF
regression also verifies the explanation binding, raw tooltip, technical
health code and preserved degraded flag in the health table.

All builds/tests below ran in the verified rebuilt VM under the non-elevated
`AgentPC\vmdev` account with VBoxService Running and pinned SDK `10.0.401`.

| Check | Result |
| --- | --- |
| Baseline Release solution build | PASS, 0 warnings/errors, 54.01 s |
| Baseline Desktop tests | PASS, 15/15, 0 failed/skipped |
| Feature Release solution build | PASS, 0 warnings/errors, 44.70 s |
| Feature Desktop tests | PASS, 19/19, 0 failed/skipped |
| Feature Core tests | PASS, 62/62, 0 failed/skipped |
| Host `git diff --check` and XAML XML parse | PASS |

The guest feature directory `C:\Users\vmdev\source\PC-Agent-telemetry` is an
archive-based test copy, not a Git checkout. SHA-256 of the transferred base
archive is `09AF81A10AA557E7D8345DF18770C37EFF57308816368818E10072C1CFF92BE3`;
the feature overlay is `1CAC9F98685C3AC5C113AA8D2CF64248E7E70943BB799967E96040AA486B5E16`.
Guest log/TRX directories are `C:\Users\vmdev\Downloads\telemetry-verification-baseline`
and `telemetry-verification-feature`. Uniquely named build logs and TRX files
were read back outside source to `HostAgent-Rebuild-Work\verification-20261029`;
the transferred TRX counters were independently parsed. WPF fixture tests
are not installed-feature MSI acceptance or production deployment.

DEV VM `HostAgent-DEV-Rebuild` UUID `0bb543d1-7bb0-43ed-9c94-596d63ef3310`
was force-restarted with owner approval after a graceful ACPI request did
not stop it. Process/file operations remained slow and the VM log contained
repeated `AHCI#0: Port 0 reset` events. A subsequent cold start with SATA host
I/O cache enabled improved observed response and the checks above completed.
This improvement does not establish the root cause. The prior `.vbox` was
saved outside source; original VM disks and Home Assistant were preserved.
See Oracle's [cache documentation](https://docs.oracle.com/en/virtualization/virtualbox/7.1/user/storage.html)
for the performance/delayed-write tradeoff. No host security policy changed.

Official NuGet WiX `5.0.2`, Util extension `5.0.2` and both DTF `5.0.2` packages
passed SHA-256 transfer checks and `dotnet nuget verify --all` in the VM.
WiX/DTF were installed outside source in
`C:\Users\vmdev\source\HostAgent-DEV-tools`; Util extension was added for the
current user. The original candidate Release rebuild passed with 0 warnings
and 0 errors (26.11 s). Its full Infrastructure suite then passed 565/565,
0 failed, 0 skipped, in 10 minutes 28 seconds. This run used original
candidate source `82465b48d6137b4f81a57f33900f3da69c194658` plus the 20 approved
local VM guard changes, not the archive-based feature copy. Its logs/TRX
are in `C:\Users\vmdev\Downloads\foundation-verification` and were read back
outside source to the host evidence directory.

The original candidate checkout retains its 20 local UUID/account guard
changes. All 20 guest file hashes matched the preserved host copies. They
were not applied to this feature branch. The rebuilt VM's
`clean-win-base` snapshot was independently listed as UUID
`3b3a6a0e-d861-4d25-9997-b39c407058a5`; historical resilience scripts contain
old snapshot references and must not be treated as current rollback proof.
None of those destructive campaigns was run.

Both installed services, `HostAgent` and `HostAgent.Helper`, were independently
observed Running after the restart; Windows uninstall metadata reports
Host Agent V1 `1.2.8`. All 16 installed first-party EXE/DLL entries selected
from the preserved preview manifest matched their expected SHA-256 hashes.
This verifies the installed preview bytes, not installation of the new
desktop feature, complete live collectors, or production policy acceptance.

Beads was located using its installed absolute path; tracking is
`vmrebuild-av2`. No commit, push or PR was made.

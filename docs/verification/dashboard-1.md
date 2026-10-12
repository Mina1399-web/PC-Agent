# Local dashboard: fresh DEV verification

Date: 2026-10-12, Europe/Prague. This record contains only runs from this
dashboard increment. Earlier V1/stabilization records remain historical.

## Environment and preservation

Before changes and again after the interruption: AgentPC,
AgentPC\AgentPCdevtest, innotek GmbH / VirtualBox, UUID
46543236-8C5E-49DA-953C-DB17F9B1E402, VBoxGuest and VBoxService Running.
Repository: `C:\Users\AgentPCdevtest\Documents\ChatGPT\DevTest VM PC\HostAgent-DEV`.
Branch `codex/local-dashboard`, base `d2168957753c3d7ad313b6f18c1aa4400831d546`.
Previous commits were preserved. The pre-existing untracked audit remains
excluded from this change; SHA-256
63268A0FE5D734BA4D5484C87004A2F702F8B031BC93B83E56DB6A6CF46C414D.
Recovery patches were saved outside Git before further work.

Account-local SDK 10.0.401 / runtime 10.0.12 was used. No security policy,
production host, VirtualBox snapshot, secret or external endpoint was changed.
The installed DEV service was still 1.0.5.0 after the restart; this dashboard
had not been deployed. A read-only WER query found RecordID 2774 at
2026-10-12T13:27:07.925139+02:00. This does not establish the crash cause.

## Test-first ledger

Evidence directory (outside source):
`C:\Users\AgentPCdevtest\AppData\Local\HostAgent-DEV-evidence\dashboard-1`.
All following TRX files were read after execution. Red counts describe
assertion failures, not setup/compilation failures. Initial missing imports
and a local variable name collision were corrected before behavioral runs.

| Behavior / evidence file | Observed red | Observed green |
| --- | --- | --- |
| Recommendation rules, `recommendations-{red,green}.trx` | 3 failed / 5 | 5 passed |
| Strict view protocol/projection, `view-{red,green}.trx` | 6 failed / 11 | 11 passed |
| Local pipe transport, `view-pipe-red.trx` | 1 failed / 13 | `view-security-green.trx`: 42 passed |
| Desktop controller/export, `desktop-{red,green}.trx` | 3 failed / 3 | 3 passed |
| Exact operator workflow/kill switch, `operator-{red,green}.trx` | 3 failed / 3 | 19 passed |
| MSI Desktop payload, `packaging-red.trx` | 1 failed / 1 | `review-envelope-packaging-green.trx`: 14 passed |
| Real persisted collector envelope, `review-envelope-red.trx` | 2 failed / 2 | same 14 passed |
| Five-second collection interval, `review-fast-{red,green}.trx` | 1 failed / 1 | 14 passed |
| Pending/completion observation, `review-observation-{red,green}.trx` | 2 failed / 2 | 7 passed |
| Missing Desktop release/shortcut gates, `package-gate-red.trx` | 2 failed / 2 | package missing-payload test passed in next run; final focused run below |
| Latest current metric and MSI shortcut normalization, `current-metric-red-package-green.trx` | 2 failed / 3; remaining gate passed | final focused run below |
| Independently fresh post-operation RAM, `review-partial-red.trx` | 1 failed / 1 | 8 Core tests passed in interrupted focused run |
| Product-only non-cascading service lifecycle, `service-scope-{red,green}.trx` | 2 failed / 2 | 17 passed |

Focused commands used this form, with the named TRX file/directory from the
table and account-local DOTNET_ROOT/PATH set:

```powershell
dotnet test tests/HostAgent.Core.Tests/HostAgent.Core.Tests.csproj -c Release --filter DashboardRecommendation --logger 'trx;LogFileName=recommendations-green.trx' --results-directory $evidence
dotnet test tests/HostAgent.Infrastructure.Tests/HostAgent.Infrastructure.Tests.csproj -c Release --filter 'DashboardView|AdministrationTests|NamedPipeSecurity|HealthStabilization' --logger 'trx;LogFileName=view-security-green.trx' --results-directory $evidence
dotnet test tests/HostAgent.Infrastructure.Tests/HostAgent.Infrastructure.Tests.csproj -c Release --filter 'DashboardDesktop|CliTests|OperatorConfiguration|ConfigurationWorkflow' --logger 'trx;LogFileName=operator-green.trx' --results-directory $evidence
dotnet test tests/HostAgent.Infrastructure.Tests/HostAgent.Infrastructure.Tests.csproj -c Release --no-build --filter 'Dashboard|CliTests|OperatorConfiguration|ReleaseToolingTests' --logger trx --results-directory "$evidence\resumed-focused"
dotnet test tests/HostAgent.Infrastructure.Tests/HostAgent.Infrastructure.Tests.csproj -c Release --filter 'ProductServiceLifecycle|ConfigurationWorkflow|OperatorConfiguration|CliTests' --logger 'trx;LogFileName=service-scope-green.trx' --results-directory $evidence
```

The resumed focused run passed **42/42 Infrastructure tests**. The earlier
focused solution run was interrupted: Core 8/8 and Desktop 1/1 completed,
Infrastructure did not produce a result. It is not counted as a passing suite.

The real STA WPF smoke opened the actual window with synthetic data, rendered
42% CPU/64% RAM, checked Shadow blocks operations, refreshed after simulated
disconnection, cleared old data and closed. `ui-smoke.trx`: 1/1 passed.
`dashboard-synthetic.png` is a synthetic screenshot outside Git. This visual
smoke was additional coverage, not a claimed red/green cycle. Transport tests
use actual local pipes under the test account; they do not establish live
service-to-interactive cross-account acceptance.

## Final build and suite

```powershell
dotnet build HostAgent.sln -c Release -m:1
dotnet test HostAgent.sln -c Release --no-build -m:1 --logger trx --results-directory "$evidence\suite-final"
dotnet test tests/HostAgent.Infrastructure.Tests/HostAgent.Infrastructure.Tests.csproj -c Release --no-build --filter FullyQualifiedName~OperatorHostingTests --logger 'trx;LogFileName=cli-timeout-investigation.trx' --results-directory $evidence
dotnet test HostAgent.sln -c Release --no-build -m:1 --logger trx --results-directory "$evidence\suite-recheck"
git -c core.whitespace=cr-at-eol diff --cached --check
```

`release-build-final.log`: exit 0, **0 warnings / 0 errors**.
First final suite: exit 1, Core 58 passed, Infrastructure 217 passed / 1
failed, Desktop 1 passed. The existing actual CLI `--help` test exceeded its
10-second deadline. A separate unchanged CLI run exited 0 in 205 ms with
expected usage and empty stderr; unchanged focused OperatorHosting tests
then passed 2/2. The timeout's cause is unconfirmed; no timeout was relaxed
or test disabled. The full unchanged repeat (`suite-recheck`,
`release-suite-recheck.log`) exited 0: **277/277 passed**, Core 58,
Infrastructure 218, Desktop 1; zero failed or skipped. Each project has its
own uniquely named TRX. This repeat does not erase the initial timing failure.

Diff whitespace check exited 0 with CRLF accepted for the byte-exact upstream
WindowsDesktop LICENSE. Solution line endings were normalized to LF (a larger
text diff without unrelated solution semantics). Evidence SHA-256 CSV at the
evidence root: 786DB03C8F0A0DE475E17A8BC6FB3F4FD53DCE8ADF2A07CCCCA73CD6F227EEF0.

An earlier full run passed 273 tests before the last review fixes. Its fixed
TRX filename was overwritten between projects; the text log retains counts.
It does not replace the final separate TRX results above.

## Review and live limitations

### Integrated design handoff and exclusive re-verification

The owner explicitly assigned design to the concurrent dashboard task and
functional integration/deployment to this task. That task handed off five
stable UI files without staging/committing/installing. Its raw TRX, review
notes and synthetic captures were inspected from the separate
`HostAgent-DEV-evidence\dashboard-design` directory. Converter tests started
with absent-type compilation failures; these are API setup red, not behavioral
assertion failures. The foreground-inheritance regression had an actual
failing assertion (`contrast-red.trx`) followed by a passing render test.
Its initial missing DOTNET_ROOT setup failures are retained in its notes.

After all concurrent edits/builds stopped, this task independently ran:

```powershell
dotnet build HostAgent.sln -c Release -m:1
dotnet test HostAgent.sln -c Release --no-build -m:1 --logger trx --results-directory "$evidence\integrated-suite"
```

`integrated-release-build.log`: exit 0, zero warnings/errors.
`integrated-release-suite.log` and three unique TRX: exit 0, **282/282 passed**
(58 Core, 218 Infrastructure, 6 Desktop), zero failed/skipped. Real WPF checks
include incident/detail selection, PID/start ticks, keyboard focus, chart gaps,
Shadow blocking, missing/stale clearing and synthetic render captures at
actual content widths 1264, 984 and 834 pixels. Actual heights were constrained
by the VM work area; full high-DPI/screen-reader/high-contrast acceptance is
not claimed.

The integrated source tree `d8d5359dcaf8939ad0678641f66cab708a3e0f41`
received independent read-only approval with no new confirmed blocker. Source
and documentation were checked for forbidden environment/secret/build
artifacts; none were staged. Original audit SHA-256 was rechecked unchanged.

An account-local, outside-source read-only view probe compiled with zero
warnings/errors. Against installed 1.0.5 it exited 4 (expected unavailable),
returning no invented snapshot. An independently approved upgrade harness
was prepared outside Git (SHA-256
E0A6128C2BF5016C57C93922808C160B60EA7417D23514507A473EE6EA442CA5);
it has not yet executed at this source checkpoint. Its unchanged Python
backup/comparison helpers and protected product-only stop/recovery flow do
not copy the secret store. The latest read-only WER metadata has stop code
0x0000000A and dump basename 101226-13937-01.dmp, without driver attribution.

Independent read-only review approved immutable source tree
`ea8a54b6f6b0206b43d6d7d7a631f7d249307c0b`; see
[security review](../security/dashboard-1-review.md). All five substantive
review findings were fixed with observed failing tests before implementation.
The original audit was outside the reviewed/staged tree.

No live mutation, external delivery, real secret, file cleanup, package
installation or production transfer was performed. Multi-user pipe acceptance,
installed Desktop/shortcut/tray lifecycle, real UAC approval and kill-switch
restart, crash recovery, multi-hour soak and production signing remain live
acceptance limits at this source checkpoint. The repeated VM crashes are not
claimed repaired. Direct cleanup/quarantine/package automation remains the
separate disabled next increment in the plan; guided Windows settings are
available now.

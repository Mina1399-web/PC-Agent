# Desktop language selection verification — 2026-10-27

## Scope and preserved state

Development branch: `codex/desktop-localization`, based on `9f538b9`.
The approved VM was freshly verified as `AGENTPC`, model `VirtualBox`, UUID
`46543236-8C5E-49DA-953C-DB17F9B1E402`, account
`AgentPC\AgentPCdevtest`, VBoxService Running. The token was non-elevated.
Actual portable SDK was 10.0.401; .NET/Core/WindowsDesktop runtimes were
10.0.12. No dependency installation was needed.

Pre-existing work was preserved and excluded from this feature commit:
`docs/verification/final-acceptance-2026-10-25.md`,
`docs/verification/installed-readonly-diagnostics-2026-10-26.md`, and
`scripts/diagnostics/Run-InstalledDatabaseIntegrityOffline.ps1`.
The public `v1.2.8-preview.1` tag was read from origin at
`82465b48d6137b4f81a57f33900f3da69c194658`; no tag or published asset was changed.

This increment changes Desktop source, Desktop tests and documentation only.
No service/helper/Core policy, monitoring configuration, IPC schema,
permissions or approval requirement was changed. No MSI installation,
elevation, service start/stop, real external delivery or production-host
operation was performed. A final read-only query found HostAgent Running
PID 10448 and HostAgent.Helper Running PID 10776; no service-control command
was used in this work.

## Implemented and exercised

- Czech `cs-CZ`, English `en`, German `de`, with accessible selection in the
  header and immediate resource/format refresh. The choice is per-user JSON
  `uiLanguage` in `%LOCALAPPDATA%\HostAgent\desktop.json`; it never writes
  `service.json`, `helper.json` or the quarantine inventory.
- Supported Windows UI language families select their corresponding language
  on first run; unsupported families use English. Saved choices survive a
  new localization instance and take precedence over Windows language.
- Atomic preference saves preserve unrelated JSON properties. Unsupported
  choices are rejected; write failure retains the previous language. Invalid
  and duplicate-key settings fall back without crashing and are preserved.
- 332 matching named resource entries per catalog, neutral English fallback,
  placeholder-contract checks and translated navigation, statuses, dialogs,
  tray items, recommendations, maintenance text and accessibility labels.
  Stable technical identifiers/filenames remain data.
- Real WPF fixtures switched repeatedly through all three languages with
  loaded incident details and minimum-width layout. They asserted localized
  grid headers, numbers/dates, no extra View request, unchanged Shadow/read-only
  state, and preservation of selection and deliberately edited PID/ticks/alias.
- A delayed-overview regression verifies that switching while IPC is in
  flight does not clear the last rendered selection or typed identities.
- Maintenance fixtures verify that an existing preview/approval ID survives
  switching; all four maintenance buttons use the selected catalog. Existing
  safe quarantine/restore tests still pass on isolated temporary files.
- A long product-owned dialog has scrollable evidence and visible localized
  controls. A German export-path dialog was exercised, saving only a synthetic
  report in its temporary fixture; the JSON format and incident identity
  remained unchanged.

Real WPF renders are retained outside source in the evidence directory's
`final-renders` folder. Main overview/incident renders for cs-CZ/en/de and a
German maintenance render were inspected. The German header overlap was fixed;
the cause/code column remains reachable at minimum width and long grid text
has full-text tooltips. These are synthetic UI checks, not acceptance of a
newly installed package.

## Commands and current results

Commands ran from the repository in the VM, with the verified portable
`dotnet.exe`, process-local DOTNET_ROOT/DOTNET_ROOT_X64, and an external
watchdog. Current authorized guard policy: start above 768 MiB, warn below
768 MiB, abort own child processes at/below 512 MiB; disk floor 10 GiB and
bounded deadlines. Earlier attempts used the historical 1024/768-stop wrapper.

```powershell
dotnet build HostAgent.sln -c Release --no-restore -m:1 -p:UseSharedCompilation=false -nr:false
dotnet vstest tests/HostAgent.Core.Tests/bin/Release/net10.0/HostAgent.Core.Tests.dll tests/HostAgent.Infrastructure.Tests/bin/Release/net10.0-windows/HostAgent.Infrastructure.Tests.dll tests/HostAgent.Desktop.Tests/bin/Release/net10.0-windows/HostAgent.Desktop.Tests.dll
dotnet build tests/HostAgent.Desktop.Tests/HostAgent.Desktop.Tests.csproj -c Release --no-restore -m:1 -p:UseSharedCompilation=false -nr:false
dotnet vstest tests/HostAgent.Desktop.Tests/bin/Release/net10.0-windows/HostAgent.Desktop.Tests.dll
```

| Run | Result | Guard evidence |
| --- | --- | --- |
| Final solution Release build, `final-layout-release-build-859b0008803d49709397bcc2fb8bdaa6` | PASS, 0 warnings/errors, exit 0 | min RAM 1046.2 MiB; disk 14.220 GiB; no intervention |
| Broader three-assembly suite, `full-release-suite-23d5199e9410445b80161b5f435bc36b` | **FAIL**, 664 PASS / 1 FAIL / 0 skipped: Core 62 PASS; Infrastructure 564 PASS + 1 resource-guard failure; Desktop 38 PASS | min RAM 628.5 MiB, warning observed; hard abort not reached; disk 14.399 GiB |
| Last test-assembly build, `acceptance-test-build-cc50f9d7edd44d138d6c6541193c7be9` | PASS, 0 warnings/errors, exit 0 | min RAM 870.2 MiB; no intervention |
| Final post-follow-up Desktop suite, `locale-acceptance-final-725aef9ec71e458984da9efa5059e5ff` | **PASS 38/38**, 0 failed/skipped, exit 0 | min RAM 816.8 MiB; disk 14.216 GiB; no warning/intervention |

The broader suite predates the final RefreshList/layout/export-test follow-up;
Core and Infrastructure sources were unchanged. Only the final Desktop run is
claimed as post-follow-up UI acceptance. The complete current solution suite
is **not claimed fully passing**.

The one broader-suite failure is the unchanged
`CounterReadFixtureGuardTests_ReleaseToolingTests.Private_builder_compiles_CSharp5_and_real_Wix4_MSI_without_installing_or_creating_native_targets`.
Its `Prepare-CounterReadMsiFixture.ps1:49` refuses less than 15 GiB free, before
building its disposable fixture. That unrelated source guard was not lowered.
The scenario remains unverified in this run; no MSI installation occurred.

## Red checks, corrections and review

- Initial en/de state assertions failed on the previous Czech-only converter.
  That first combined attempt was then interrupted at 763.6 MiB by the old
  soft watchdog; it was not a completed suite.
- Delayed-refresh selection test: 1 expected FAIL before the rendered-snapshot
  fix; subsequently PASS. The controller's transient null snapshot is no
  longer used for language-only rendering.
- Long-dialog check: expected FAIL on absent scrolling before the dialog
  layout fix; subsequently PASS.
- Duplicate settings: 2 expected FAIL on ArgumentException before the bounded
  load fallback; subsequently PASS.
- Real grid-header check exposed null headers on DataGridColumn, which does
  not inherit ordinary resource context. Explicit HeaderKey refresh fixed it.
- German maintenance refresh-list assertion: expected FAIL (`Obnovit seznam`)
  before the new key/translations; subsequently PASS.
- Additional language/persistence/format/side-effect tests were added as
  coverage; not every such test had a separately observed red run. Earlier
  test setup corrections (a non-GUID synthetic incident, old uptime wording,
  and a mistyped test-only resource key) are not product-defect claims.
- Independent read-only support review found the in-flight selection race,
  dialog overflow, mutable comparison capture, duplicate-settings startup
  edge, and remaining refresh-list text. These were addressed; its bounded
  final source review reported no remaining actionable P1/P2 finding.
- Final static inventory validation found 332 matching entries in each
  catalog, 106 XAML references and 120 direct code references with zero
  missing keys. Neutral English values contained no Czech-specific letters.
- Final staged diff whitespace, artifact-path and known secret-pattern checks
  passed; exactly the 23 feature files were staged. Resource EOL/comment-only
  normalization after the last test run preserved every resource key/value
  dictionary; it did not change translated output or implementation behavior.

An optional NTFS compression attempt targeted only repository `bin` outputs:
2,341 files retained their SHA-256 bytes and zero files were deleted. It did
not recover the fixture's required disk headroom (free space 14.527 to
14.226 GiB). No SDK, evidence, source, installed service data or published
asset was deleted; the separate 15 GiB fixture gate remains open.

All detailed receipts/stdout/stderr and renders are retained outside source
under `%LOCALAPPDATA%\HostAgent-DEV-evidence\desktop-localization-20261027`.

## Remaining boundaries

No known product-owned desktop prose remains untranslated in en/de after
these checks. Native language names, brands, units, filenames and diagnostic
codes intentionally stay stable. Windows Settings, UAC and the separately
launched CLI use their own language; approval contracts are unchanged.

New MSI packaging, installation, installed-product language acceptance and
any public release are NOT RUN for this increment. The published 1.2.8
preview and its SignPath description do not acquire this feature. See
[desktop language instructions](../operations/desktop-languages.md).

## Changed files

`src/HostAgent.Desktop/`: `MainWindow.xaml`, `MainWindow.xaml.cs`,
`MaintenanceWindow.cs`, `Program.cs`, `DashboardDisplay.cs`,
`DesktopLocalization.cs`, `DesktopPreferences.cs`, `LocalizedMessages.cs`,
`RecommendationText.cs`, `TechnicalText.cs`, `Properties/AssemblyInfo.cs`,
`Resources/UiText.resx`, `Resources/UiText.cs-CZ.resx`, `Resources/UiText.de.resx`.

`tests/HostAgent.Desktop.Tests/`: `DashboardDisplayTests.cs`,
`DashboardInteractionTests.cs`, `DashboardUiTests.cs`, `MaintenanceUiTests.cs`,
`DesktopLocalizationTests.cs`, `LocalizationSwitchTests.cs`.

Documentation: `docs/operations/dashboard.md`,
`docs/operations/desktop-languages.md`, and this verification record.

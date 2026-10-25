# Distribution components checkpoint — 2026-10-25

This supplements the preserved [2026-10-11 inventory](dependency-inventory.json)
and [historical license review](dependencies.md). It records a read-only
inspection of source commit `c1653468531e3cfd4f1ce5a636d277a8f75180fa` and
existing restored metadata. It does not claim a fresh restore, exhaustive
source audit, provider approval, or inspection of a later hosted artifact.
Every distributed candidate needs its own generated inventory and file hashes.

## Restored project graph

The ten existing `project.assets.json` graphs comprise the six product projects
under `src/` and four test/probe projects under `tests/`. They contain 49 unique
NuGet ID/version pairs; 36 are referenced by product projects. Comparison with
the historical 49-package inventory found no ID/version difference and no
difference in the SHA-256 of any cached package nuspec. Project scope has grown
since the historical eight-project inventory and must be regenerated.

Declared package-license metadata is MIT or Apache-2.0 except test-only
`xunit.abstractions/2.0.3`, whose nuspec records a legacy upstream license URL.
That missing SPDX expression remains explicit. It is not product payload.
No proprietary product package was found in this metadata inspection.

| Component family | Version | Product distribution / license evidence |
| --- | --- | --- |
| Microsoft.Data.Sqlite and Microsoft.Extensions/System references | 10.0.12 | Product runtime dependencies; nuspecs declare MIT. Preserve applicable Microsoft/.NET license and notices. |
| SQLitePCLRaw bundle/core/provider/native package | 2.1.12 | Product runtime dependencies; nuspecs declare Apache-2.0. Preserve the [Apache license](sqlitepclraw-2.1.12-LICENSE.txt), attribution and SQLite public-domain notice. |
| Microsoft.NET.Test.Sdk and test-platform packages | 17.14.1 | Test-only; MIT. Do not include test tooling in the product. |
| xUnit / VS runner | 2.9.3 / 3.1.5 | Test-only; Apache-2.0 declarations, with the abstractions exception above. |
| Newtonsoft.Json | 13.0.3 | Restored test/tooling graph only; MIT. |

`tools/New-SigningDependencyInventory.ps1` reads restored assets, package nuspec
metadata, and optional published `.deps.json` files. Its JSON records relative
project/payload names, product/test scope, package versions, license evidence,
nuspec hashes and runtime packs. It does not execute package scripts or record
local cache paths, account names, environment variables or package contents.
Generate the inventory after the hosted restore/publish and bind it to that
exact source commit; the observed local metadata is not a substitute.

## Self-contained Microsoft runtime

`Directory.Build.props` pins runtime 10.0.12. Service/Helper/CLI redistribute the
.NET runtime; the desktop also redistributes WindowsDesktop runtime 10.0.12.
These framework packs are additional to the 49-package graph. Their exact
published presence must be recorded from each candidate's `.deps.json`.

The checked-in legal texts match the cached official 10.0.12 packages:

| Legal text | SHA-256 |
| --- | --- |
| [Runtime license](dotnet-runtime-10.0.12-LICENSE.txt) | `D7A68596AB69B06F51CA278A6545148E4269A9381C26D597C13DF5D88E08CF5B` |
| [Runtime third-party notices](dotnet-runtime-10.0.12-THIRD-PARTY-NOTICES.txt) | `6D15E10A101C6BFFF2AB4429ED061BF76C456FC4B23AD6B03E0D0F8377148A21` |
| [WindowsDesktop license](windowsdesktop-runtime-10.0.12-LICENSE.txt) | `A89886665765362EB77E0F8E26602C924520041D1711B2EEDC136434FE4D01AB` |

Preserve upstream runtime and SQLite signatures and notices. Do not request a
Host Agent signature on upstream DLLs merely because they are in the package.

## WiX DTF source mapping for MSI distribution

The current unsigned binary-review path does not contain the MSI custom action
and does not redistribute WiX DTF. The MSI builder does: it creates a composite
`Preflight.CA.dll` from Host Agent's managed custom action, WiX's native SfxCA
shim and `WixToolset.Dtf.WindowsInstaller.dll`.

Both inspected DTF 5.0.2 nuspecs identify the immutable upstream repository
commit `aa65968c419420d32e3e1b647aea0082f5ca5b78` in
[`wixtoolset/wix`](https://github.com/wixtoolset/wix/tree/aa65968c419420d32e3e1b647aea0082f5ca5b78).
Their ProductName is `WiX Toolset` and ProductVersion is
`5.0.2+aa65968c419420d32e3e1b647aea0082f5ca5b78`.

| Redistributed upstream input | SHA-256 | Source in that commit |
| --- | --- | --- |
| `WixToolset.Dtf.CustomAction/5.0.2`, `tools/x64/SfxCA.dll` | `2F25295967A1FEB5899AF2F2FB7CEAFE54CE146ED84A9E44BBDE774F3CF627A9` | `src/dtf/SfxCA/` and referenced WiX source/build inputs |
| `WixToolset.Dtf.WindowsInstaller/5.0.2`, `lib/net20/WixToolset.Dtf.WindowsInstaller.dll` | `A614884EA627DA1925131EBF41E8AE202CAEAC0FE543B86384F5EB2BFAF1AA75` | `src/dtf/WixToolset.Dtf.WindowsInstaller/` and referenced WiX source/build inputs |

Both declare [MS-RL](wix-5.0.2-LICENSE.txt), an
[OSI-approved license](https://opensource.org/license/ms-rl). Its reciprocal
condition requires recipients receive the source for distributed files that
contain its code, together with the license, while retaining upstream notices.
For an MSI release, provide the complete immutable
[WiX source archive](https://github.com/wixtoolset/wix/archive/aa65968c419420d32e3e1b647aea0082f5ca5b78.zip)
alongside the release, record its SHA-256 after retrieval, and retain this
input-to-source mapping. A mutable branch link alone is insufficient release
evidence. The later preparation streamed that official archive and pinned its
12,377,495 bytes to SHA-256
`AC593BF21864D9AD4B44720A702DAE3651A51D199CFD5AEE080AAA1286AB72F1`.
The hosted submission builder must retrieve and verify it before including it
under `docs/legal/` in the installer and distribution. A streamed hash alone
does not establish that a particular candidate actually contains those bytes.

Host Agent's own custom-action source is in `tools/installer/` with its shared
installation-tree validation in
`src/HostAgent.Infrastructure/Security/InstallationTreeSecurity.cs`.
Do not describe the composite native custom-action file as wholly first-party.
Its exact treatment in the provider artifact configuration remains a SignPath
decision. The [SignPath conditions](https://signpath.org/terms.html) allow
unsigned upstream libraries inside signed packages but restrict directly signed
binaries to the project's own code. Preserve the local signing gates until the
provider-approved path is separately implemented and verified.

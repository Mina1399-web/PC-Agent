# Dependency and license review for SignPath

Reviewed 2026-10-11 against source base
3024c16dfdcadb5f35405aa6d9ebf9640b3090eb, SDK 10.0.401 and runtime 10.0.12.
This is a metadata/provenance inventory, not an exhaustive legal or source audit.

## First-party source

The owner approved public open-source distribution. First-party source and
documentation use the root [MIT license](../../LICENSE). This does not relicense
upstream dependencies, generated third-party notices, Windows, GitHub or
optional external services. No commercial dual-license scheme is introduced.

## NuGet graph

[dependency-inventory.json](dependency-inventory.json) records 49 unique
id/version entries from all eight restored project.assets.json files, project
scope and the SHA-256 of each local package nuspec. Generated assets and caches
are not published. Declared licenses are MIT or Apache-2.0 except
xunit.abstractions 2.0.3, whose old nuspec provides only an upstream license URL.
The [upstream abstractions repository](https://github.com/xunit/abstractions.xunit)
declares Apache-2.0; this is not a version-specific license-expression proof.
That package is test-only, not product payload. The inventory preserves the
missing field instead of inventing package metadata.

| Components | Declared license and attribution | Release treatment |
| --- | --- | --- |
| Microsoft.Data.Sqlite, Microsoft.Extensions.*, System.* and Microsoft test tooling | MIT; Microsoft / .NET Foundation and contributors as identified by upstream notices | Runtime references included as required; test tooling is not product payload. Preserve upstream notices/signatures. |
| SQLitePCLRaw 2.1.12 bundle/core/provider/native package | Apache-2.0; SourceGear, LLC / Eric Sink and contributors. SQLite engine has its own public-domain dedication. | Preserve Apache license and applicable upstream notices; do not re-sign the native DLL as Host Agent. |
| xUnit and VS runner | Apache-2.0; xUnit.net contributors | Tests only; never package PipeProbe or test runner. |
| Newtonsoft.Json 13.0.3 | MIT; James Newton-King and contributors | Restored test/tool graph only at this base; preserve scope in inventory. |

Primary upstream references: [.NET](https://github.com/dotnet/runtime),
[Microsoft.Data.Sqlite](https://github.com/dotnet/efcore),
[SQLitePCLRaw](https://github.com/ericsink/SQLitePCL.raw),
[SQLite public domain](https://sqlite.org/copyright.html),
[xUnit abstractions](https://github.com/xunit/abstractions.xunit),
[Json.NET license](https://github.com/JamesNK/Newtonsoft.Json/blob/master/LICENSE.md).

## Self-contained runtime and MSI dependencies

The published 1.0.2 Service deps.json explicitly identifies
runtimepack.Microsoft.NETCore.App.Runtime.win-x64/10.0.12, outside the 49-entry
project graph. The corresponding cached official NuGet package supplies the
following files, copied byte-for-byte into this source preparation:

| File | SHA-256 |
| --- | --- |
| [Runtime LICENSE](dotnet-runtime-10.0.12-LICENSE.txt) | D7A68596AB69B06F51CA278A6545148E4269A9381C26D597C13DF5D88E08CF5B |
| [Runtime third-party notices](dotnet-runtime-10.0.12-THIRD-PARTY-NOTICES.txt) | 6D15E10A101C6BFFF2AB4429ED061BF76C456FC4B23AD6B03E0D0F8377148A21 |

Those third-party notices include additional licenses and attributions;
MIT at the package level does not replace them. No SDK/runtime binaries or
NuGet archives are included in this source branch.

WiX Toolset 5.0.2 and Util extension are build tools. In addition,
WixToolset.Dtf.CustomAction 5.0.2 contributes the native SfxCA shim to the MSI,
and WixToolset.Dtf.WindowsInstaller 5.0.2 supplies the managed DTF assembly in
that composite action. Their inspected nuspecs declare **MS-RL**, which is
[OSI-approved](https://opensource.org/license/ms-rl). These are upstream
components, not Host Agent-owned code. Preserve their license/notices and
provide the corresponding source as required by MS-RL:
[WiX source v5.0.2](https://github.com/wixtoolset/wix/tree/v5.0.2).
The exact redistributable/source mapping and SignPath's treatment of the
composite action remain release gates, not a completed compliance claim.

## Distribution gaps to close before production signing

2026-10-12 continuation: new MSI packaging now requires and includes the root
MIT license, privacy disclosure and runtime notices. The actual candidate
ZIP/MSI verification is recorded separately in
[publication evidence](../verification/signpath-publication.md); the historical
1.0.2 artifact below was not changed. Additional byte-exact upstream texts:
[EF Core 10.0.12](efcore-10.0.12-LICENSE.txt),
[SQLitePCLRaw 2.1.12 Apache license](sqlitepclraw-2.1.12-LICENSE.txt), and
[WiX 5.0.2 MS-RL license](wix-5.0.2-LICENSE.txt). The new hosted review payload
does not include the DTF/MSI action; its source/license treatment remains an
explicit production signing gate.

The existing unsigned 1.0.2 ZIP had no LICENSE/NOTICE files in its enumerated
payload. Its builder copies docs but not the root LICENSE, and its MSI
packages only Service/Helper/Cli files, not the docs tree. This preparation
adds source-side notices; it does not retrofit or rebuild that historical
artifact. A new pipeline must test notice presence in both the ZIP and the
installed MSI payload, retain applicable Apache/WiX notices, and include
final documents in the signed manifest.

Do not bulk-sign Microsoft, SQLite, WiX or other upstream binaries using the
Foundation certificate. Confirm third-party signature/chain acceptance by
the target's active policy. Dependency metadata and successful NuGet
vulnerability checks do not prove that every redistributed file is licensed,
signed, vulnerability-free or accepted on the production host.

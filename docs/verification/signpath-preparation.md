# SignPath preparation verification

Date: 2026-10-11. Starting commit:
3024c16dfdcadb5f35405aa6d9ebf9640b3090eb, clean codex/v1-release-readiness.
Work continues on codex/signpath-preparation. This record describes fresh
preparation checks; earlier Task 2-4/V1 results remain historical in their
unchanged records. No new signer or production acceptance is claimed.

## Environment and preserved state

Direct read-only checks confirmed hostname AGENTPC, account
AgentPC/AgentPCdevtest, VirtualBox/innotek GmbH, UUID
46543236-8C5E-49DA-953C-DB17F9B1E402, VBoxGuest and VBoxService Running.
Working copy: C:/Users/AgentPCdevtest/Documents/ChatGPT/DevTest VM PC/HostAgent-DEV.
SDK 10.0.401, runtime 10.0.12, PowerShell 7.6.5 and existing WiX/DTF 5.0.2.
No new tool installation was necessary.

Before edits, git bundle create/verify --all succeeded to the sibling
HostAgent-DEV-delivery/before-signpath-3024c16d.bundle, SHA-256
C2065BD6D7DDB4C4C7E4633655A3E5CA17CB54E8F44143712E9CA336BC31ECC4.
Existing commits, tags, release files and snapshot clean-win-base are preserved.
No production host/project, policy, trusted certificates, VM configuration,
installed service or runtime secret was changed.

The user explicitly authorized public open source and then GitHub-hosted
builds/tests for the SignPath path. All executions in this preparation still
ran inside the approved VM. No hosted workflow was created or run.

## Fresh review and execution ledger

Raw logs and JSON evidence remain outside source in HostAgent-DEV-evidence.
Only controlled summaries and hashes are in this record.

| ID | Actual action / result |
| --- | --- |
| 01 | Eight restored assets graphs inventoried: 49 distinct NuGet id/version entries; 37 MIT, 11 Apache-2.0, one older xunit.abstractions entry with only upstream URL. Public JSON excludes local cache paths. Supplemental runtime/DTF licenses and gaps recorded separately. |
| 02 | Read-only git grep -l -I -i -E over all 16 reachable --all commits, six rule groups (private keys, GitHub/OpenAI/Slack tokens, AWS IDs, quoted credential assignments): zero matching revision paths. Initial 181 tracked paths: zero bin/obj/cache/key/database/VM-artifact paths. This is a bounded heuristic, not exhaustive secret detection; unreachable/ignored/untracked files were outside that history scan. |
| 03 | Focused Release baseline: 29 passed, 0 failed/skipped, exit 0, 20.98 seconds. Actual locally built assemblies executed. |
| 04 | Fresh full Release build: exit 0, 0 warnings/errors, 34.95 seconds. |
| 05 | Fresh full Release suite: 42 Core + 164 Infrastructure = 206 passed, 0 failed/skipped, exit 0. Reported test-run durations 3.26 and 34.29 seconds. |
| 06 | NuGet vulnerability check including transitive references: all eight projects reported no known vulnerable package from the configured NuGet source; exit 0. Not a guarantee against unknown vulnerabilities or native/system issues. |
| 07 | Pre-stage preparation review inspected all 16 changed/new files, including 11 Markdown files: no missing relative links, six secret-pattern groups found no matches, no forbidden artifact paths, no implementation/build/test changes. Tracked CRLF-aware git diff --check passed; this did not check untracked-file whitespace. |
| Initial staged check | Failed before commit on four Markdown hard-break trailing spaces and the upstream runtime notice's blank EOF line. Own formatting was corrected. The vendor notice remains byte-identical; its EOF is explicitly excluded only from the vendor whitespace check. No commit/push occurred on failure. |
| GitHub metadata | Read-only connector confirmed owner Mina1399-web, repository PC-Agent, private visibility, default main, owner permission admin. Account MFA, branch protections and SignPath roles are not verified. |
| Initial document-link check | Failed on three links to this not-yet-created record. This was documentation setup, not a behavioral red or product failure; a final recheck is required. |

Exact dotnet commands, executed through the existing external evidence runner:

```powershell
dotnet test tests/HostAgent.Infrastructure.Tests/HostAgent.Infrastructure.Tests.csproj --configuration Release --no-build -m:1 --filter 'FullyQualifiedName~ReleaseToolingTests|FullyQualifiedName~InstallerStagingTests|FullyQualifiedName~OperatingModeTests|FullyQualifiedName~OutboundTransportTests' --logger 'console;verbosity=normal'
dotnet build HostAgent.sln --configuration Release -m:1 -p:UseSharedCompilation=false
dotnet test HostAgent.sln --configuration Release --no-build -m:1 --logger 'console;verbosity=normal'
dotnet list HostAgent.sln package --vulnerable --include-transitive --no-restore
```

External evidence SHA-256:

| Log | SHA-256 |
| --- | --- |
| signpath-03-focused-baseline.log | 4197F82066E50FBA0970D2B25DD3A915C50AAB63F560A0E70A8B2BD056334592 |
| signpath-04-release-build.log | DF9CDB0D3DC072ABB040F3BB657FE3219100B8B18F70CC172C25392454473634 |
| signpath-05-release-suite.log | 302207220426664E7924B15135B9C90168740D1BDA598BD5C793FD48AB601C7F |
| signpath-06-dependency-audit.log | 833B501BD19D46FA00E0067253EA009F9A8DBF90E30DAB902BCAACD58F6B8FFC |
| signpath-07-document-review.json | B537D32A5BD4554ED5706031C0B12C138516B56E22D279E09B184A73727353DF |

All 49 recorded nuspec hashes were independently rechecked against the
current cache. The WiX source tag v5.0.2 was verified with git ls-remote at
aa65968c419420d32e3e1b647aea0082f5ca5b78. The web browser fetch of its tree
failed; tag verification does not complete the pending DTF source mapping.

This is the primary agent's source/document review, not an independent
third-party security audit or provider decision. Publication is pending at
this checkpoint and will be recorded only after actual execution.

## Scope and review findings

Only licensing, inventory, owner/contribution guidance and documentation are
changed. No product/build/test behavior changes; no red/green claim applies.
Existing VM release guards, exact publisher pins, approval and secret controls
remain unchanged. Source-side .NET notices were copied byte-for-byte from the
official cached runtime package; their hashes are in the dependency review.

The review found real preparation gaps: current private visibility/default
source, no public released application/reputation proof, absent distribution
license/notices, differing product metadata, VM-bound packaging/tests,
local-certificate-only signer, vendor WiX composite-action treatment and
pre-install privacy disclosure. Source licensing/policy/privacy/application
documents are prepared; those runtime/provider gates are not marked resolved.

Default network ICMP to gateway/1.1.1.1 and DNS example.com were confirmed in
the collector source. The privacy document does not claim disabling optional
Pushover/heartbeat makes the product offline. Optional HTTP adapters were
reviewed against their actual request construction and fixed message template.

No SignPath application, account, API credential, GitHub App installation,
certificate, manual signing approval, signed artifact, production deployment
or host transfer occurred. Foundation acceptance and target-policy acceptance
remain external gates; preparing an application does not guarantee either.

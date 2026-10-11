# SignPath Foundation preparation

This is the historical 2026-10-11 preparation checkpoint. Subsequent work and
the current publication/application status are in the
[continuation record](../verification/signpath-publication.md) and
[publication plan](../plans/2026-10-12-signpath-publication.md).

Date: 2026-10-11. Starting source: 3024c16dfdcadb5f35405aa6d9ebf9640b3090eb.
This is an eligibility review and application package, not an implemented
cloud signer or a claim that SignPath approved Host Agent.

The owner approved public open-source licensing and, separately, builds/tests
on GitHub-hosted servers for SignPath. Production host policies, data and
project remain protected. Existing release commits and DEV packages remain
unchanged. Work uses codex/signpath-preparation.

## Eligibility assessment

| Requirement | Current evidence / remaining gate |
| --- | --- |
| OSI-approved license | MIT added for first-party source/docs; upstream licenses remain separate. |
| Publicly accessible source | GitHub API reports the repository is currently private, default branch main. The owner has admin permission and authorized open source, but this preparation does not change repository visibility or main. Provider review requires a public reviewed source/ref and release page; private-branch publication is not public OSS publication. |
| Open source dependencies | 49 restored NuGet packages inventoried; declared MIT/Apache-2.0 plus an older xunit.abstractions license URL resolved against upstream. Runtime and embedded WiX DTF require additional notices/source treatment. See dependency review. |
| Owned, maintained and documented project | Owner/repository Mina1399-web/PC-Agent; current V1 docs and preserved development commits. No independent reputation or long-term maintenance claim. |
| Existing released application | There is a locally verified VM-only unsigned 1.0.2 candidate. No public signed release exists; the source branch alone does not establish SignPath's released-application requirement. |
| Verifiable reputation | New executable project; acceptance risk. No stars/users/history of public releases or third-party endorsements are fabricated. SignPath decides eligibility. |
| No exploit/circumvention tools | V1 diagnoses performance/connectivity/service incidents and has a closed approval-gated helper; no vulnerability scanner, exploit or security-policy bypass. SignPath must assess privileged helper behavior. |
| Privacy/system-change disclosure and uninstall | Privacy document now discloses default ICMP/DNS, optional Pushover/heartbeat and preserved ProgramData. MSI installs two services and supports uninstall. Pre-install disclosure/consent treatment still needs review with SignPath. |
| MFA, team roles, signing approval | Proposed owner/approver documented; actual account MFA, permission and approval settings unverified. |
| Trusted build origin | User authorized GitHub-hosted build/test exception. No workflow is active. Local VM output or a self-hosted runner cannot be represented as GitHub-hosted OSS provenance. |
| Metadata restrictions | Existing app ProductName values differ (HostAgent.Service/Helper/Cli); managed Preflight lacks explicit product/version attributes. Uniform first-party product/version metadata must be tested before requesting production signing. |
| Signed distribution integrity | Current VM/local-certificate gate is intact. Cloud signing, final hash regeneration, publisher pinning and real signed-artifact validation remain unimplemented/unverified. |

## Integration design to implement after eligibility/provider decisions

1. Use a manually dispatched, reviewed workflow on GitHub-hosted Windows
   runners, pinned actions and pinned SDK/runtime/WiX. Use read-only repository
   permissions and persist-credentials false; never use pull_request_target,
   forks, repository write credentials or a self-hosted runner for signing.
   Scope the GitHub App to this repository only. No runtime secrets are needed.
2. Compile/test reviewed source. Existing VM-bound packaging tests and their
   fixed local tool paths require explicit CI support and coverage accounting;
   do not silently skip them or claim an equivalent full suite. Preserve actual
   installed Windows service acceptance as a separate DEV/target gate.
3. Publish first-party components and compile managed Preflight. Normalize
   product/version metadata. Prepare upstream DTF wrapper separately and agree
   with SignPath how to treat the composite/native custom action and its vendor
   code; do not claim ownership of upstream binaries.
4. Submit only explicitly listed first-party PE and release PowerShell files
   using the trusted GitHub connector and restricted artifact configuration.
   Require manual approval. Preserve upstream signatures/notices. Verify
   returned files' provenance, signatures and pinned signer before packaging.
5. Build the production MSI from final signed payload and verified custom
   actions; sign it. Compare extracted MSI payload to the distributable tree.
   Do not use -DevVm or remove its machine guard to manufacture a production MSI.
6. Copy license/third-party/privacy documents into both the ZIP and installed
   MSI payload. Generate manifest.psd1 from final signed file bytes, then sign
   that manifest in a final request. PSD1, PE, PowerShell and MSI formats are
   supported by SignPath, but this exact configuration has not been tested.
7. Run the existing strict release verification (hashes/membership, signature
   chain and independently pinned publisher) and appropriate package tests.
   Validate in the DEV VM and then the protected target without policy changes.
   Publish only after all relevant gates; no unattended/manual-approval bypass.

The current local builder interleaves private-key signing, custom-action
packaging, MSI generation and manifest hashing. Merely uploading its unsigned
ZIP and signing the outer MSI would not meet these requirements. Deep-signing
after hashing would invalidate the manifest and can produce inconsistent
ZIP/MSI payloads. This preparation intentionally leaves that code unchanged.

## Owner application draft (not submitted)

Application entry point: <https://signpath.org/apply>.
Review this draft after the remaining eligibility gates; it is not a claim
that the current project already meets every condition.

**Project:** Host Agent V1

**Repository:** https://github.com/Mina1399-web/PC-Agent

**Preparation branch:** codex/signpath-preparation

**License:** MIT for first-party source; third-party components retain their licenses.

**Maintainer / proposed approver:** GitHub @Mina1399-web; account/MFA confirmation pending.

Host Agent is a Windows x64 local monitoring and incident-assistance service.
It collects bounded host telemetry, persists local diagnoses/incidents,
uses deterministic confidence and defaults to Shadow/read-only. Optional
Pushover notifications and Healthchecks-compatible HTTPS heartbeat are
operator-enabled. Default bounded connectivity diagnostics use ICMP and DNS,
as disclosed in the privacy document. There is no inbound cloud control
channel, arbitrary shell, vulnerability scanner or security-policy bypass.

A separate LocalSystem helper accepts only typed allowlisted service
start/stop/restart and process termination, with exact short-lived human
approval, independent policy checks and durable audits. Monitoring runs as
NT SERVICE/HostAgent. The MSI creates these two services and protected data
directories; uninstall removes services/package files and preserves data.

We seek an eligibility review for a new executable project and guidance on
GitHub-hosted verifiable builds, MSI composite WiX DTF custom actions,
first-party metadata restrictions, third-party dependencies and pre-install
network/privacy disclosure. The existing tested candidate is unsigned and
restricted to one DEV VM. We do not claim a signed release or established
public reputation. The cloud integration is not yet implemented. Please
confirm whether the project can qualify and the required remaining steps.

Do not attach VM configuration, raw logs, credentials or operational databases.
Owner email/account details belong only in the provider's authenticated form,
not the repository. No application was sent, account created, GitHub App
installed, billing enabled or token generated during this preparation.

## Primary references

- [Foundation conditions](https://signpath.org/terms.html)
- [GitHub-hosted origin requirement](https://docs.signpath.io/trusted-build-systems/github)
- [Supported formats and metadata restrictions](https://docs.signpath.io/artifact-configuration/reference)
- [Artifact nesting/deep signing](https://docs.signpath.io/artifact-configuration/syntax)
- [OSI MIT license](https://opensource.org/license/mit)
- [Fresh preparation verification](../verification/signpath-preparation.md)

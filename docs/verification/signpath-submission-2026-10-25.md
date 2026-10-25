# SignPath submission preparation — current continuation

This continues [the earlier checkpoint](signpath-readiness-2026-10-25.md).
Historical results there remain tied to their original commits and binary-only
review workflow. No application, legal consent or signing request has been sent.

## Scope and requirements

The source base is `c1653468531e3cfd4f1ce5a636d277a8f75180fa` on
`codex/signpath-submission-prep`. Preparation adds a separate GitHub-hosted
unsigned MSI candidate path; it preserves the original exact-VM and
certificate gates. Candidate preparation performs no MSI installation,
service control, trust change or production-host operation.

| Gate | Current evidence / remaining action |
| --- | --- |
| Open source and component provenance | MIT first-party source; dated mapping and fresh candidate inventory generated/validated in successful CI. |
| Maintained project | Current source and preserved development history are public; provider reputation assessment remains external. |
| Released in intended form and download description | Functionality/download page and release text prepared. Public MSI release and native candidate acceptance remain pending. |
| Owned signing scope and metadata | 17 exact owned PE paths and product/version checks passed in CI; upstream runtime/SQLite/DTF excluded from owned signing. |
| Privacy and announced changes | Outbound defaults off; operator opt-in preserved. Full disclosure/plan reader and explicit preview MSI acknowledgement prepared. Provider must assess conditional native-UI requirement. |
| Uninstall and retention | Existing MSI transaction/removal design preserved; current native preview install/removal not executed. |
| Team MFA and roles | Proposed owner roles documented; actual account verification is owner-only. |
| Home/download signing policy | Current source pages link policy/privacy; default GitHub landing branch still requires owner administration. |
| Hosted origin and artifact | New MSI workflow 40230180663 succeeded; artifact/run/source linkage independently confirmed. SignPath connector still requires onboarding. |
| Provider configuration/manual approval | Owned-stage configuration generated with candidate; composite DTF treatment and final signing stages await provider acceptance. |

Sources: [Foundation conditions](https://signpath.org/terms.html),
[trusted GitHub builds](https://docs.signpath.io/trusted-build-systems/github),
[artifact configuration examples](https://docs.signpath.io/artifact-configuration/examples).
These conditions do not guarantee admission or replace provider decisions.

## Fresh preparation checks

- Approved DEV VM identity and account were rechecked directly; branch and
  working tree were clean before this continuation. Changes are confined to
  source, test harnesses and documentation.
- Lossless NTFS compression of fourteen named older source-external release
  directories preserved every file and its SHA-256. No files were deleted.
  C: free space increased from 14.86 GiB to 16.29 GiB, retaining the 15 GiB
  local build/test floor. The installed product and repository were not
  compression targets.
- Dependency generator: meaningful red on unimplemented behavior, followed by
  identical-command green **8/8**, exit 0; watchdog did not intervene, minimum
  RAM 1,231.2 MiB and C: 15.410 GiB.
- Data-only manifest reader: red on unimplemented behavior, then **39/39**
  green, exit 0; external SHA checked before parsing, duplicate/path/schema
  and size-limit cases included; minimum RAM 909.1 MiB, C: 15.704 GiB.
- Shared MSI source/preview-table contract: red
  `preview_requires_explicit_ack`, then **11 synthetic groups PASS**, exit 0;
  minimum RAM 1,093.4 MiB, C: 15.515 GiB. This establishes XML/table-rule
  behavior on synthetic fixtures, not a native MSI installation.
- Hosted-candidate refusal guard: red `submission_not_implemented`, then
  **4 checks PASS**, exit 0; unapproved environment rejected before output or
  tools execute; minimum RAM 1,203.6 MiB, C: 16.293 GiB.
- PowerShell parser check over tooling passed. The pinned upload-artifact
  action's actual `action.yml` confirms `archive: false` uploads one file
  directly, preserving the candidate ZIP's root for signing configuration.
- A local Release solution build was stopped at 767.4 MiB free RAM by the
  unchanged 768 MiB soft watchdog (96.9 seconds). It is incomplete, not a
  build PASS. The subsequent memory-conserving attempt was refused by its
  initial resource guard before build; no thresholds were lowered.
- A complete three-assembly Release `dotnet vstest` attempt on existing
  assemblies was also stopped by the soft watchdog at 757.7 MiB (140.6
  seconds); no completed suite result is claimed. C# source/test trees are
  unchanged from the prior verified base, but that does not turn an
  interrupted run into a PASS. Both installed services remained Running with
  their previously observed PIDs after these tool-only attempts.
- A local optional YAML-parser check could not run because that Python
  environment lacks PyYAML. No dependency was installed for it; actual
  GitHub workflow parsing/execution remains the relevant integration gate.
- Initial independent read-only source review found no actionable P1/P2
  defect in inspected guards/MSI/signing scope, and required final docs to
  reflect the new MSI scope. Exact final candidate/evidence review is pending.

## Candidate and local integration results

The first MSI-workflow run
[40229564002](https://github.com/Mina1399-web/PC-Agent/actions/runs/40229564002),
source `51e066f68bd1b3e261960a6f24467b6fc2eeeb22`, passed the 62 new synthetic
checks (8 inventory + 39 manifest + 11 MSI-source groups + 4 host-refusal
checks), Release build, Core 62/62 and filtered Infrastructure 449/449.
Desktop produced 13 PASS and 2 FAIL, both `MaintenanceUiTests` constructing
real `UserMaintenance` under the elevated hosted runner token. The normal-user
guard rejected that token with `maintenance_scope`, as designed. The workflow
stopped; no MSI candidate/archive was built or uploaded. The hosted Desktop
filter now excludes precisely that two-test class; the full 15-test class set
remains required in the non-elevated DEV VM suite. No product guard or test was
weakened. The workflow also logs installed runtime inventory to distinguish
framework-dependent test runtimes from the self-contained 10.0.12 payload.

The corrected workflow
[40230180663](https://github.com/Mina1399-web/PC-Agent/actions/runs/40230180663)
completed **SUCCESS** at source
`34fe3baf0b37b9cda1db1a2ac95c34f854efbaba`:

- Windows Server 2025 GitHub-hosted runner; SDK 10.0.401; compiler 0 warnings
  and 0 errors. Runtime inventory was printed. Host was 10.0.13 and shared
  runtimes included both 10.0.12 and 10.0.13; tests were not forced to the
  exact 10.0.12 self-contained payload patch.
- All 62 preparation checks passed; Core **62/62**, filtered Infrastructure
  **449/449**, filtered Desktop **13/13**, with 0 failed/skipped in each run.
  The documented six Infrastructure classes and two Desktop maintenance
  cases remain in the complete DEV suite. No complete current DEV suite is
  claimed.
- Pinned DTF nupkg hashes/NuGet signatures and immutable WiX source archive
  hash passed. A fresh metadata-only inventory from restored assets and
  published dependencies was included. This is separate from the earlier
  observed 49-package local graph; no uninspected new graph count is invented.
- Custom-action compilation, DTF assembly and WiX MSI build completed.
  Directory and ZIP validation both returned PASS: **1,230 manifested files**,
  **17 exact owned PE entries**, metadata, complete lengths/hashes, notices,
  source archive, MSI identities/transaction/acknowledgement and exact
  installed-path table membership. Inspection used database tables only.
- Final receipt: `PREPARED_NATIVE_ACCEPTANCE_PENDING`,
  `signingPerformed=false`, `installerExecuted=false`.

| Artifact | SHA-256 |
| --- | --- |
| ZIP, 322,777,962 bytes | `722CAA759CD0E249C8F27549849148F524F1248DF05939AEAA901B417E566E9A` |
| `HostAgent.msi` | `78241E9B8EE79C9B9CFFD89D4F1634616CF946BC18644010162B5750F29A2272` |
| `manifest.json` | `444D696AC0C879A1446A8FC85D8C62DA6AD6D15878B1FED8FC1F63C360189E93` |

[Candidate artifact 12612639952](https://github.com/Mina1399-web/PC-Agent/actions/runs/40230180663/artifacts/12612639952)
expires 2026-11-08. Its API size/digest and source/run linkage independently
match the builder receipt. Test reports were uploaded as artifact
`12612891030`, SHA-256
`3947baa10b8bcad55b497e4138a1d135517198338fd05338dbf9047291b5bae3`.
The setup action logged a 409 warning for its attempted latest-runtime
download but then installed the pinned SDK successfully; that warning did
not fail the tests/build.

A fresh read-only reviewer confirmed the source diff, run/job logs, artifact
API linkage and these scopes, with no actionable P1/P2 finding. The reviewer
did not locally download/rehash MSI/manifest bytes or certify native
acceptance or SignPath eligibility.

The separate candidate reference
[`codex/signpath-candidate-1.2.8`](https://github.com/Mina1399-web/PC-Agent/tree/codex/signpath-candidate-1.2.8)
was created and re-read through the GitHub API at exact source `34fe3baf...`.
It preserves source for an owner-created release; it is not an enforced
branch-protection claim or an existing public binary release.

## Evidence and limits

Detailed test stdout/stderr and watchdog receipts are retained outside source
under the DEV user's `HostAgent-DEV-evidence/signpath-submission-20261025`.
The source archive pin was computed by streaming the official immutable WiX
archive: 12,377,495 bytes; SHA-256
`AC593BF21864D9AD4B44720A702DAE3651A51D199CFD5AEE080AAA1286AB72F1`.
The hosted builder must verify those bytes before distribution.

The public application embed was identified on the official apply page, but
the browser control surface failed to attach it. A subsequently opened Chrome
window could not be controlled because current-page identification was
insufficient for policy enforcement; browser input stopped. No form fields,
settings, account or release were changed. An alternative non-interactive
existing-Git-auth API probe was rejected **before execution** by automatic
approval review (`blocked by policy`); no credentials were read. That method
was not retried or disguised.

Local candidate staging was refused by its resource guard before downloading
or creating the target directory. Later free RAM was only 559.9 MiB; the owner
must close unneeded applications before local checks. The CI artifact is
available, but no locally extracted candidate/path/hash result is claimed.
The local handoff contains source-check orchestration, exact CI checksum
sums, release text and owner instructions; it contains no installer bytes.

Final documentation-only review: `git diff --check` passed and all ten
changed/new documentation/example files passed local Markdown-link and known
secret-pattern checks. A subsequent read showed 460.1 MiB available RAM;
no further local build, test, extraction or installation was attempted.
Independent final handoff review found no P1/P2 issue in its bounded static
scope. A P3 wording ambiguity was corrected: full Infrastructure tests open
isolated SQLite fixtures; the orchestration does not request opening installed
product databases. Tool paths were confirmed present without executing them.

The [owner checklist](../operations/signpath-owner-steps.md) lists resource
recovery/full VM suite, local candidate verification/native acceptance,
default-branch administration, public release, MFA/roles/contact/consent and
provider decisions. The [post-admission design](../signing/after-admission.md)
is inactive and does not represent a tested signer.

**Status: independently executable preparation and hosted candidate checks
completed within the recorded scope. Submission remains gated on listed
owner/provider work; no admission, native acceptance, public release or
signature is claimed.**

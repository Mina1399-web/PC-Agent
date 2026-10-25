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
| Open source and component provenance | MIT first-party source; dated dependency/source mapping prepared. Fresh candidate inventory pending hosted run. |
| Maintained project | Current source and preserved development history are public; provider reputation assessment remains external. |
| Released in intended form and download description | Functionality/download page and release text prepared. Public MSI release and native candidate acceptance remain pending. |
| Owned signing scope and metadata | Exact owned PE paths and product/version checks prepared; upstream runtime/SQLite/DTF excluded from owned signing. |
| Privacy and announced changes | Outbound defaults off; operator opt-in preserved. Full disclosure/plan reader and explicit preview MSI acknowledgement prepared. Provider must assess conditional native-UI requirement. |
| Uninstall and retention | Existing MSI transaction/removal design preserved; current native preview install/removal not executed. |
| Team MFA and roles | Proposed owner roles documented; actual account verification is owner-only. |
| Home/download signing policy | Current source pages link policy/privacy; default GitHub landing branch still requires owner administration. |
| Hosted origin and artifact | Existing successful binary-review run is historical. New MSI workflow execution pending; actual SignPath connector still requires onboarding. |
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

Pending: fresh hosted Release build/tests, restored dependency inventory,
actual MSI artifact tables, exact payload membership/hashes, owned-file
metadata, ZIP/manifest checks and final independent evidence review.
No new candidate hash or successful native installation is claimed yet.

## Evidence and limits

Detailed test stdout/stderr and watchdog receipts are retained outside source
under the DEV user's `HostAgent-DEV-evidence/signpath-submission-20261025`.
The source archive pin was computed by streaming the official immutable WiX
archive: 12,377,495 bytes; SHA-256
`AC593BF21864D9AD4B44720A702DAE3651A51D199CFD5AEE080AAA1286AB72F1`.
The hosted builder must verify those bytes before distribution.

The embedded public application form was identified on the official apply
page, but the browser control surface failed to attach the page. No fields
were filled. Repository administration/default branch, public release
publication, personal contact information, MFA, provider onboarding and
legal consent remain owner actions if unavailable through current tools.

**Status: preparation in progress; not ready to submit until applicable
owner/provider gates and the remaining candidate checks are completed.**

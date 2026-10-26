# Unsigned preview publication verification — 2026-10-26

**Public preview published and independently downloaded; application not sent.**
This record supersedes the earlier landing/public-download blockers, not
historical tests, installed limitations or provider requirements.

## Repository and exact release

- Public repository: <https://github.com/Mina1399-web/PC-Agent>.
- Default branch changed from `main` to `codex/signpath-submission-prep`.
  All eight existing branches retained their original heads at publication;
  `main` remained `188e3e259519e8713cee7aed84fcec7e40aa408c`.
  Subsequent documentation-only changes advance the preparation branch.
  No force-push, deletion or history replacement was used.
- [Host Agent V1 1.2.8 unsigned preview](https://github.com/Mina1399-web/PC-Agent/releases/tag/v1.2.8-preview.1)
  is public, `draft=false`, `prerelease=true`.
- Tag `v1.2.8-preview.1` resolves directly to approved source commit
  `82465b48d6137b4f81a57f33900f3da69c194658`. The candidate branch remains
  `codex/signpath-candidate-1.2.8-r2` at that same commit.
- Release text came from
  [the approved draft at documentation commit 81394793](https://github.com/Mina1399-web/PC-Agent/blob/81394793cfaa12d074fd13c854c23f841c730f01/docs/releases/1.2.8-preview.1-public-draft-2026-10-26.md).
  Only the draft label, editorial publication instructions and relative
  disclosure links were changed. Published text was read back and compared.
- The repository root was inspected anonymously in a browser and showed the
  new default branch and README links. The release page visibly showed the
  pre-release state, exact commit and truthful limitations.
  Root, release, functionality/download, privacy, installation disclosure and
  Code signing policy URLs returned HTTP 200.

## Four uploaded assets: independent public downloads

After publication, all four files were downloaded again without credentials
through their public release URLs into a separate evidence directory.
Comparison completed at 2026-10-26 19:02:49 UTC.

| Asset | Bytes | SHA-256 | Result |
| --- | ---: | --- | --- |
| `HostAgent-1.2.8-submission-preview.zip` | 322,802,124 | `C68119D7F394174FBBB49AC915BCF2B287CABCF59CBF14FF0557321BDA51E033` | PASS |
| `HostAgent.msi` | 117,670,676 | `F3CAA8EBCD8EBE9F163FE2D2E196F83D5B948DEC93EC6BADC2BA0452EF0AA197` | PASS |
| `manifest.json` | 222,921 | `5DCF79DC3284B4937EBA64D4F50A7831A1FFE4F0891AE4D4B190A500B136B25A` | PASS |
| `SHA256SUMS.txt` | 265 | `3ECBD83C4189FFC99339035BD26BCC32C69BE3DF1A36D9E8B33F42D6D6FF49E5` | PASS |

The downloaded checksum receipt contains exactly three entries and each
matches the downloaded ZIP, MSI and manifest. Its own checksum is recorded
separately above. The REST release asset list contains exactly those four
uploaded files. GitHub's two automatically generated source-code downloads
are separate and were not supplied as additional installer assets.

The [download page](../operations/download.md) provides the direct public URLs.

## Bounded read-only license/configuration inspection

The following entries were streamed from the independently downloaded ZIP
without extraction/execution. Each path occurred exactly once and its length
and SHA-256 matched the downloaded approved manifest:

| ZIP entry | Bytes | SHA-256 |
| --- | ---: | --- |
| `docs/legal/wix-source-aa65968c419420d32e3e1b647aea0082f5ca5b78.zip` | 12,377,495 | `AC593BF21864D9AD4B44720A702DAE3651A51D199CFD5AEE080AAA1286AB72F1` |
| `docs/legal/dependency-inventory-2026-10-25.json` | 70,624 | `97325D1916097AA151C756A733392B8AE163CAD72B412311E5189B839FD15FA1` |
| `docs/legal/distribution-components-2026-10-25.md` | 6,628 | `8274F08B5E59E327B1FB7080D594B3E41BE0963E0F60E9EE18A89CAFDAAA5957` |
| `signing-input/owned-payload.xml` | 2,835 | `C28B5672E9F3CE4ACFAFD0000D6F58A45D90732717D5986754D0A0113CBA6FC2` |

The preserved candidate's corresponding files also matched these manifest
entries. Its generated inventory identifies the approved source
`82465b48d6137b4f81a57f33900f3da69c194658`.
This establishes presence and byte identity of these selected entries, not
an exhaustive legal/security review, provider approval or signed provenance.

## Installed/runtime and build evidence remain scoped

The published preview preserves the owner-supplied findings: configuration
and critical installed hashes matched, both services were Running during the
read-only probe, and initial acceptance was incomplete with partial
CPU/process telemetry. Offline-copy `quick_check=ok` covers only
`host-agent.db` and `notifications.db`; the absent helper journal is
`NOTRUN`. A later owner-run recovery returned both services to Running.

The prior full DEV Release result was 0 warnings/errors and 642/642 tests;
the successful hosted run used its documented narrower scope. No build,
test, installation, service operation or Windows security change was
performed for publication or documentation synchronization.
No production acceptance, complete runtime acceptance or signature is claimed.

## Account/provider checks

Authenticated GitHub API access confirmed login `Mina1399-web` and repository
admin permission. Its user response omitted `two_factor_authentication`.
That is **NEOVĚŘENO**, not proof MFA is on or off. Read-only browser verification
could not proceed because computer control could not reliably establish the
current Chrome URL. No browser authentication setting was changed.

SignPath MFA, actual organization/project roles, admission and signing
configuration remain **NEOVĚŘENO**. Proposed author/reviewer/approver membership
is recorded in [Code signing policy](../operations/code-signing-policy.md);
provider assignments are not inferred from GitHub admin rights.

The [Foundation conditions](https://signpath.org/terms.html) and
[trusted GitHub build documentation](https://docs.signpath.io/trusted-build-systems/github)
were read on 2026-10-26. A follow-up read-only account check found GitHub
two-factor enrollment still requires an owner action before the MFA
requirement can be considered satisfied. SignPath MFA and project roles
remain unverified. Public release/functionality preparation is complete;
the provider's reputation, DTF and privacy decisions remain external
requirements. Actual trusted signing is a post-admission step, not a result
of publishing an unsigned preview.

The current instruction is to leave the application unfilled and unsubmitted
and not accept legal terms. No provider message or signing request was sent,
and no credentials, telemetry, databases or other operational files were
published. See [remaining owner steps](../operations/signpath-owner-steps.md).

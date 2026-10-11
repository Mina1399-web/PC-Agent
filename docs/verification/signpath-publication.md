# SignPath publication continuation

Date: 2026-10-12, Europe/Prague. Base:
5aca8f634f32d09a19ab094ea679c78d3aef16ef. The owner authorized the remaining
preparation/publication steps and explicitly required stopping before sending
the application. This is separate from the [historical preparation](signpath-preparation.md).

## Environment and rollback

Fresh checks: AGENTPC, AgentPC/AgentPCdevtest, innotek GmbH/VirtualBox,
UUID 46543236-8C5E-49DA-953C-DB17F9B1E402, VBoxGuest/VBoxService Running.
The codex/signpath-preparation tree was clean at the base above. SDK 10.0.401,
runtime 10.0.12, PowerShell 7, WiX/DTF 5.0.2 remain account-local/external.
The verified all-ref rollback bundle before-signpath-publication-5aca8f6.bundle
has SHA-256 14CB6EED88A1EBEE4E960CF02CFEA4852CB3A727EE619151FA39CDD636BCC083.
No existing commit, main, Task 4/release branch, snapshot, production project
or host security policy was overwritten or changed.

## Changes and actual test-first evidence

Raw logs are outside the checkout under HostAgent-DEV-evidence, prefix
signpath-public-. They contain UTC, exact command, all output and exit code.

| Run | Actual outcome |
| --- | --- |
| 01 focused packaging/staging baseline | 10 passed, exit 0 |
| 02 metadata/MSI notices red | 2 failed: product HostAgent.Core instead of Host Agent V1; 4 MSI files instead of 8 |
| 03 same focused command | 2 passed, exit 0 |
| 04 managed action metadata red | 1 failed: empty ProductName |
| 05 same focused command | 1 passed, exit 0 |
| 06 hosted guard red | 1 failed: separate public build entry point missing |
| 07 same focused command | 1 passed; non-hosted invocation refuses without creating output |
| 08 uniform product version red | 1 failed: commit suffix made .NET ProductVersion differ from the action version |
| 09 same focused command | 1 passed; source commit remains independently recorded in release manifests |
| 10 privacy tests | compiler error in new test's property name; fixture correction only, not behavioral red |
| 11 corrected privacy red | 2 failed: default collector contacted targets; explicit on/off command rejected |
| 12 strict config red | 1 failed: optional explicit probe property rejected |
| 13 privacy/config focused green | 3 passed, exit 0 |
| 14 source scan | 18 reachable commits, 198 non-ignored files, zero six-format secret matches/forbidden artifacts; heuristic, not exhaustive |
| 15 focused regression | 32 passed, exit 0 |
| 16 complete Release build | 0 warnings/errors, exit 0 |
| 17 complete Release suite | 42 Core + 170 Infrastructure = 212 passed; 0 failed/skipped, exit 0 |
| 18 actual clean-source 1.0.3 release packaging red | exit 1, release_missing_notices: root LICENSE absent from Build-Release output, caught by the new MSI requirement |
| 19 same packaging after root LICENSE copy | exit 0, source b39be933f2a79cefc3996800db86cb6207dff228; intermediate DEV ZIP SHA-256 708B6A28E78068E1BF2E2B7E2DE3C26D201D950DE692E53A5CD29C0C09A59A60 |
| 20 unavailable-probe metric test | xUnit2031 analyzer error in the new test; fixture correction only, not behavioral red |
| 21 corrected metric red | 1 failed: Unavailable emitted a fabricated network.probe.success=0 sample |
| 22 same metric test after projector fix | 1 passed, exit 0; unavailable probes emit no metrics, genuine failure/success remain 0/1 |
| 23 final complete Release build | source 6b326058; 0 warnings/errors, exit 0 |
| 24 final complete Release suite | 43 Core + 170 Infrastructure = 213 passed; 0 failed/skipped, exit 0 |
| 25 clean-source final 1.0.3 release packaging | source 6b326058ab294b3e1cdc442c32f280c89a027918; exit 0 |
| 26 final executable-source history scan | 18 reachable commits, 201 tracked files; zero six-format secret matches/forbidden artifacts; whitespace and four PowerShell script parses passed |
| 27 final read-only package verification | 703 manifest files; all 704 ZIP file hashes/lengths match; 699 MSI file entries, required license/privacy notices present, 12 first-party binary metadata checks passed; exact VM guard matches; production gate refused unsigned package with exit 1/publisher_signature_required |

Independent review of 5aca8f6..b39be933 identified one P2: unavailable
probes were recorded as observed failures by the metric projector. The
test-first fix skips unavailable results without changing measured failures
or successful results. Packaging run 19 predates this fix and is therefore
an intermediate artifact, not the final candidate.
Read-only independent rereview of b39be933..6b326058 confirmed the P2 resolved,
with no new actionable findings. The reviewer did not execute builds/tests.

Exact final VM commands (account-local SDK 10.0.401 on PATH):

```powershell
dotnet build HostAgent.sln --configuration Release -m:1 -p:UseSharedCompilation=false
dotnet test HostAgent.sln --configuration Release --no-build -m:1
pwsh -NoProfile -NonInteractive -File tools/Build-Release.ps1 -PackagePath '..\HostAgent-DEV-releases\HostAgent-1.0.3-signpath-final' -WixPath '..\HostAgent-DEV-tools\wix\wix.exe' -Version 1.0.3 -DevVm
pwsh -NoProfile -NonInteractive -File tools/Test-Release.ps1 -PackagePath '..\HostAgent-DEV-releases\HostAgent-1.0.3-signpath-final' -DevVm
pwsh -NoProfile -NonInteractive -File tools/Test-Installer.ps1 -PackagePath '..\HostAgent-DEV-releases\HostAgent-1.0.3-signpath-final'
pwsh -NoProfile -NonInteractive -File tools/Test-Release.ps1 -PackagePath '..\HostAgent-DEV-releases\HostAgent-1.0.3-signpath-final'
```

The last command intentionally omits DevVm and was verified to refuse the
unsigned candidate. No installation or protection bypass was performed.
ZIP SHA-256: 17C7224D36FD5AE9B14080AD9C26D5DE7649E630E303392B08AF37B3B3B2DC1B.
MSI SHA-256: 6787FAE8C50BF97273419921FC084581F2DBAB49BB21502AC9FA5247035D205D.
The final candidate predates subsequent evidence-only documentation commits.

The hosted positive publish path is not established by the VM refusal test.
Its GitHub run will be recorded only after execution.
The cloud workflow deliberately excludes ReleaseToolingTests, which build and
evaluate exact-VM MSI conditions. These remain in the complete VM suite, with
no spoofed VM/runner identity. Cloud runs are not called the complete VM suite.

## Publication/application status

GitHub initially remains private with default main at
188e3e259519e8713cee7aed84fcec7e40aa408c. The browser reached the final
visibility action but GitHub redirected to Confirm access/Verify via email.
Visibility change has not been confirmed; no public state is claimed.
The owner must complete GitHub's own reauthentication in the open browser;
no code/password/token is requested in chat and none was extracted.
Fresh connector metadata after the final code review still reports private,
default main. No new workflow push, cloud run or public release is claimed;
private CI was not triggered and billing was not enabled.
The authenticated ruleset read returned 403 because private/free repositories
do not support that feature; no rules were disabled by us.

The actual application form was inspected without submission. It requires
project/repository/homepage, tagline, category, description, reputation,
build system, first/last name, email and discovery channel. The form also has
Code of Conduct and personal-data consent checkboxes and reCAPTCHA. Neither
legal consent, CAPTCHA nor Submit was operated. Personal owner details are
missing and must remain outside this public repository. Reputation is not
invented; this is a new project without established usage evidence.
A private application working draft is preserved outside the checkout under
HostAgent-DEV-delivery/SignPath-application-private-draft.md. It marks missing
owner identity, public download and hosted execution explicitly; it is not
ready to send. No personal contact data was guessed or published.

Still open: confirmed public source/default branch/download page, successful
hosted execution, provider eligibility/reputation decision, account MFA,
SignPath App/policy/certificate, staged MSI signing and final hash/signature
verification, upstream DTF treatment, production policy acceptance and live
optional outbound endpoints. The new MSI was inspected, not installed in this
continuation. Native MSI has no privacy-consent UI; active probes are now off
by default and operator opt-in announces the targets. Provider acceptance
of this treatment is not assumed.

No application/email was sent, account created, signing token generated,
real secret used, cloud billing enabled, or production installation performed.

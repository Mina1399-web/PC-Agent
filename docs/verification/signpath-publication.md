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
The authenticated ruleset read returned 403 because private/free repositories
do not support that feature; no rules were disabled by us.

The actual application form was inspected without submission. It requires
project/repository/homepage, tagline, category, description, reputation,
build system, first/last name, email and discovery channel. The form also has
Code of Conduct and personal-data consent checkboxes and reCAPTCHA. Neither
legal consent, CAPTCHA nor Submit was operated. Personal owner details are
missing and must remain outside this public repository. Reputation is not
invented; this is a new project without established usage evidence.

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

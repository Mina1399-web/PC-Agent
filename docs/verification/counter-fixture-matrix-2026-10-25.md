# Counter-read native MSI fixture matrix — 2026-10-25

## Result

The operator supplied the elevated runner receipt for run
`76600fa6de6c47c5b890441dee38140e` from the isolated evidence root
`C:\ProgramData\HostAgentCounterFixture-76600fa6de6c47c5b890441dee38140e`.
The receipt reports `status=PASS_FIXTURE_ONLY`,
`nativeAcceptance=PASS_FIXTURE_ONLY`,
`nativeFixtureIsInstalledProductAcceptance=false`,
`sourceAndProductPackageModified=false`, `rebootPerformed=false`, and
`protectedIdentityUnchanged=true`.

All eight declared MSI fixture phases report PASS:

1. Missing-grant rollback.
2. Preexisting-grant rollback.
3. Preexisting-grant install.
4. Preexisting-grant uninstall preserves the preexisting grant.
5. Late-commit grant rollback.
6. Owned-grant install.
7. Owned uninstall rollback.
8. Owned-grant uninstall/removal.

The three intentional fault-injection phases returned MSI 1603 and recorded
their expected callback and post-state proofs. Successful phases returned MSI
0. The receipt reports quiescence confirmed and its GUID-scoped temporary
artifacts cleaned (`preservedForManualReview=false`).

## Resource and identity evidence

The operator receipt reports free memory of 2282.9 MiB at start, 2049.6 MiB
minimum, and 2121.2 MiB at completion; no low-memory warning occurred and
post-run recovery passed. The configured start, warning, and emergency-abort
limits remained 768, 768, and 512 MiB. The receipt reports
`protectedIdentityUnchanged=true` and that source/product package files were
not modified. These are operator-supplied receipt claims; this session could
not independently read the protected `C:\ProgramData` result file.

The receipt names manifest SHA256
`C6A17492D5E1129570683197D35111B5DEB4E6E3BDDE5DCA7E5F272DEB16C061`. The
manifest had previously been read-only validated against 85 source/tool input
pins and six installed-file pins. The pinned runner SHA256 was
`9C266F465F96A8906DCA903B422F3959D373A22A79D75A83BFB6CF6AC18BC928`.

## Scope limit

This is a native pass for the disposable GUID-isolated MSI custom-action
fixture only. It does not establish installed HostAgent product acceptance,
service-token PDH acceptance, signed release provenance, or permission to
change the installed Shadow/read-only configuration. No protected service or
product file was changed by this documentation update.

The related source correction and verification are recorded in
[`completion-2026-10-23.md`](completion-2026-10-23.md): the fixture group
description was reduced to the real 48-character Windows validation limit;
its regression test passed, the Release build passed, and the three Release
test projects reported 639 passing tests in total. The overall solution test
process exit code was not captured, so the record does not claim it.

# V1 release review accumulator

Base: `2932cb44749283af54cc2f2f52472405eec29c6d`. Reviews are read-only source inspection in the approved DEV VM. No production deployment or real secret/target operation is implied.

Initial A/B tree `175b21e2619856a61f835a63ef3cc7c4ac1b3d7d`: independent reviewer reported no actionable findings.

Integrated tree `e8b5afc3b824c44e38d82137fe324e9d34a301ea`: three verified findings; none dismissed. Corrections are in progress and require final tests and independent re-review.

| ID | Finding | Correction and evidence |
| --- | --- | --- |
| V1-R1 / P1 | MSI could copy privileged payload into a pre-existing untrusted writable installation tree. | Embedded SYSTEM deferred preflight validates owners, mutation ACLs, reparse points and descendants before CreateFolders/InstallFiles, including repair/upgrade. Shared validator refuses instead of repairing unsafe ACLs. Run44 observed three missing-refusal failures and missing MSI action. Run45 actual MSI action/sequence inspection passed; refusal assertions passed, but the test's before-ACL snapshot required correction for Windows persistence normalization. Final gate pending. |
| V1-R2 / P2 | An allowlist alias could be rebound while confirmation was pending, or after durable approval, without changing the request digest. | Approval persists actual service name or executable path/hash. CLI captures displayed binding, issuer compares current binding, helper independently compares stored target with its current immutable allowlist. Legacy unbound approvals fail closed. Run37 observed three unexpected successes; run40 passed35 focused tests. Additional confirmation-time regression pending. |
| V1-R3 / P2 | Wrapper validated a mutable source MSI and later reopened it for privileged installation. | Protected staging and post-copy signature/hash verification are in progress. Run45 observed source-path reuse. Test-only harness setup errors are not behavioral evidence. Final gate pending. |

All target-execution tests use a fake executor. Installer compilation/table inspection does not constitute a live SYSTEM install, repair or upgrade test. The final reviewed tree and any remaining live prerequisites will be appended after execution.

Final source re-review approved immutable tree ae2864935c4a58232d0b989a912a097cae325bf1 against base2932, with no unresolved actionable findings. R1's inherited-permission gap was retained, reproduced by two expected failures in run51, then corrected; future descendants are checked, with a constrained CreatorOwner exception. R2 and R3 remain resolved. Final repeated test gates and live acceptance remain separate requirements.

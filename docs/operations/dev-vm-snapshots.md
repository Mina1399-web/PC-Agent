# Current DEV VM and rollback evidence

The rebuilt DEV VM is `HostAgent-DEV-Rebuild`, UUID
`0bb543d1-7bb0-43ed-9c94-596d63ef3310`, account `AgentPC\vmdev`.
Active scripts, modules, fixture expectations and campaign manifests use
this identity. Historical verification documents retain their original facts.

The current `clean-win-base` checkpoint was listed on the host on 2026-10-30
with UUID `3b3a6a0e-d861-4d25-9997-b39c407058a5`. This baseline predates the
restored development tools. Its existence is not a fresh backup of today's
source or installed application, nor proof that restoration has been tested.

Before an operator uses a manual reboot/power-off runner, run on the host:

```powershell
./tools/Confirm-DevSnapshot.ps1 -SnapshotUuid 3b3a6a0e-d861-4d25-9997-b39c407058a5 |
    Set-Content -LiteralPath (Join-Path $env:USERPROFILE 'Downloads\DevSnapshotReceipt.json') -Encoding utf8
```

The command queries only the approved VM's actual VirtualBox snapshot list.
It fails if the selected snapshot is absent. Transfer the JSON to the guest
and supply its path as `-SnapshotReceiptPath` to the runner's executed
Prepare/Verify stages. The guest accepts only a well-formed receipt for the
approved VM, no more than 60 minutes old, with a nonempty snapshot UUID.
The receipt is operator-supplied inventory evidence, not a signed attestation.
Do not delete the checkpoint between verification and the campaign.

Read-only preflight reports `snapshotVerification=not_verified` and does not
invent a verified snapshot. Campaign manifests call their declared snapshot
`rollbackSnapshotCandidate`; that field alone is not verification. The
power-off runner additionally retains its existing explicit rollback
confirmation and timestamp requirements, bound to the same checkpoint.

Old campaign candidate paths, candidate hashes and catalog hashes remain
strictly checked. Those historical binaries are not made current by changing
the VM identity: rebuild/review campaign candidates and satisfy their runtime
dependencies before actual resilience campaigns. No snapshot restore,
reboot/power-off campaign or service mutation is performed by the receipt
producer or its validation fixtures.

$ErrorActionPreference='Stop'
Import-Module (Join-Path $PSScriptRoot '..\..\scripts\chaos\PowerOffPersistence.psm1') -Force
$id='f31c58c42ae14f9cbf4c083a592c264e'
$prepared=[pscustomobject]@{runId=$id;bootBeforeUtc='2026-10-18T01:00:00Z';ready=[pscustomobject]@{mode='poweroff-proof';readyAtUtc='2026-10-18T02:00:00Z'}}
$started=[pscustomobject]@{mode='poweroff-proof';runId=$id;processId=42;startedAtUtc='2026-10-18T02:03:20Z'}
$boot=[datetime]'2026-10-18T02:03:00Z';$creation=[datetime]'2026-10-18T02:03:10Z'
# The normal reboot implementation rejects a true abrupt restart with no graceful marker.
Assert-RebootTransition $prepared $boot $null $started 42 $creation
Write-Output 'PASS abrupt restart accepted without graceful marker'
$checks=1
function Reject([scriptblock]$Action,[string]$Code){$seen=$null;try{& $Action}catch{$seen=$_.Exception.Message};if($seen-cne$Code){throw ('expected_'+$Code+'_got_'+$seen)};$script:checks++}
$closed=[pscustomobject]@{runId=$id;reason='graceful_stop';closedAtUtc='2026-10-18T02:02:00Z'}
Reject {Assert-RebootTransition $prepared $boot $closed $started 42 $creation} 'poweroff_graceful_shutdown_detected'
Reject {Assert-RebootTransition $prepared ([datetime]'2026-10-18T01:00:00Z') $null $started 42 $creation} 'reboot_new_boot_not_proved'
Reject {Assert-RebootTransition $prepared $boot $null $started 43 $creation} 'reboot_automatic_runtime_not_proved'
Reject {Assert-RebootTransition $prepared $boot $null $started 42 ([datetime]'2026-10-18T01:03:10Z')} 'reboot_automatic_runtime_not_proved'
$prepared.ready.mode='reboot-proof'
Reject {Assert-RebootTransition $prepared $boot $null $started 42 $creation} 'poweroff_proof_mode_mismatch'
$prepared.ready.mode='poweroff-proof';$started.mode='reboot-proof'
Reject {Assert-RebootTransition $prepared $boot $null $started 42 $creation} 'poweroff_proof_mode_mismatch'
$started.mode='poweroff-proof';$started.runId='00000000000000000000000000000000'
Reject {Assert-RebootTransition $prepared $boot $null $started 42 $creation} 'reboot_automatic_runtime_not_proved'
Write-Output ('PASS '+$checks+' abrupt boot-transition acceptance/rejection checks; no service control.')

$now=[datetimeoffset]'2026-10-18T04:00:00Z'
Reject {Assert-PowerOffRollback $false '2026-10-18T03:55:00Z' $now} 'poweroff_fresh_host_rollback_confirmation_required'
Reject {Assert-PowerOffRollback $true '' $now} 'poweroff_fresh_host_rollback_confirmation_required'
Reject {Assert-PowerOffRollback $true '2026-10-18T03:55:00' $now} 'poweroff_fresh_host_rollback_confirmation_required'
Reject {Assert-PowerOffRollback $true '2026-10-18T03:00:00Z' $now} 'poweroff_host_rollback_confirmation_expired'
Reject {Assert-PowerOffRollback $true '2026-10-18T04:05:00Z' $now} 'poweroff_host_rollback_confirmation_expired'
$proof=Assert-PowerOffRollback $true '2026-10-18T03:55:00Z' $now
if($proof.snapshotUuid-cne'3b3a6a0e-d861-4d25-9997-b39c407058a5'){throw 'wrong_snapshot_bound'};$checks++
Write-Output ('PASS '+$checks+' total poweroff transition and rollback guards. No service control.')

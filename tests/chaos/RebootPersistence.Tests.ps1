$ErrorActionPreference='Stop'
Import-Module (Join-Path $PSScriptRoot '..\..\scripts\chaos\RebootPersistence.psm1') -Force
$checks=0
function Reject([scriptblock]$Action,[string]$Code){$seen=$null;try{& $Action}catch{$seen=$_.Exception.Message};if($seen-cne$Code){throw ('expected_'+$Code+'_got_'+$seen)};$script:checks++}
$id='a7e26ca9929b48b5b36d1b2556c41bda'
$service=[pscustomobject]@{Name='HostAgentDevScm_'+$id;StartName='NT SERVICE\HostAgentDevScm_'+$id;StartMode='Auto';PathName='"C:\ProgramData\fixture\app.exe" isolated-scm-service '+$id}
Assert-RebootServiceIdentity $service $id $service.PathName; $checks++
$bad=$service|ConvertTo-Json|ConvertFrom-Json;$bad.Name='HostAgent';Reject {Assert-RebootServiceIdentity $bad $id $service.PathName} 'reboot_service_identity_mismatch'
$bad=$service|ConvertTo-Json|ConvertFrom-Json;$bad.StartName='LocalSystem';Reject {Assert-RebootServiceIdentity $bad $id $service.PathName} 'reboot_service_identity_mismatch'
$bad=$service|ConvertTo-Json|ConvertFrom-Json;$bad.StartMode='Manual';Reject {Assert-RebootServiceIdentity $bad $id $service.PathName} 'reboot_service_identity_mismatch'
Reject {Assert-RebootServiceIdentity $service ('0'*32) $service.PathName} 'reboot_guid_required'
$before=[pscustomobject]@{Name='HostAgent';State='Running';StartMode='Auto';StartName='NT SERVICE\HostAgent';PathName='pinned';ImageSha256='expected';ProcessId=10;AssemblyHashes=[pscustomobject]@{'HostAgent.Core.dll'='old'}}
$after=$before|ConvertTo-Json|ConvertFrom-Json;$after.ProcessId=42
Assert-RebootProtectedContinuity $before $after;$checks++ # PID changes across a real boot are expected.
$after.AssemblyHashes.'HostAgent.Core.dll'='changed';Reject {Assert-RebootProtectedContinuity $before $after} 'reboot_protected_assembly_changed'
$after=$before|ConvertTo-Json|ConvertFrom-Json;$after.StartName='LocalSystem';Reject {Assert-RebootProtectedContinuity $before $after} 'reboot_protected_configuration_changed'
$prepared=[pscustomobject]@{runId=$id;bootBeforeUtc='2026-10-18T01:00:00Z';ready=[pscustomobject]@{readyAtUtc='2026-10-18T02:00:00Z'}}
$closed=[pscustomobject]@{runId=$id;reason='graceful_stop';closedAtUtc='2026-10-18T02:02:00Z'}
$started=[pscustomobject]@{runId=$id;processId=42;startedAtUtc='2026-10-18T02:03:20Z'}
$boot=[datetime]'2026-10-18T02:03:00Z';$creation=[datetime]'2026-10-18T02:03:10Z'
Assert-RebootTransition $prepared $boot $closed $started 42 $creation;$checks++
Reject {Assert-RebootTransition $prepared ([datetime]'2026-10-18T01:00:00Z') $closed $started 42 $creation} 'reboot_new_boot_not_proved'
$closed.reason='lease_expired';Reject {Assert-RebootTransition $prepared $boot $closed $started 42 $creation} 'reboot_graceful_rollback_not_proved';$closed.reason='graceful_stop'
Reject {Assert-RebootTransition $prepared $boot $closed $started 43 $creation} 'reboot_automatic_runtime_not_proved'
Reject {Assert-RebootTransition $prepared $boot $closed $started 42 ([datetime]'2026-10-18T01:03:10Z')} 'reboot_automatic_runtime_not_proved'
$closed.closedAtUtc='2026-10-18T02:04:00Z';Reject {Assert-RebootTransition $prepared $boot $closed $started 42 $creation} 'reboot_shutdown_time_invalid'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$r8=[IO.Path]::GetFullPath((Join-Path $repo '..\HostAgent-DEV-SCM-candidates\scm-release-candidate-20261018-soak-r8'))
Assert-RebootManifest $r8 'AEEA44CBEA5F46C0216A11AC0BF1A9E59B3C82EC050B826DAD38F505D716BA61';$checks++
Reject {Assert-RebootManifest $r8 ('0'*64)} 'reboot_candidate_manifest_mismatch'
Write-Output ('PASS: '+$checks+' reboot identity, protected continuity, real r8 manifest and boot acceptance checks. No service control.')

$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$runner=Join-Path $repo 'scripts\chaos\run-manual-poweroff-persistence.ps1'
$ast=[Management.Automation.Language.Parser]::ParseFile($runner,[ref]$null,[ref]$null)
$node=$ast.Find({param($n)$n-is[Management.Automation.Language.FunctionDefinitionAst]-and$n.Name-ceq'Read-RebootMarker'},$false)
. ([scriptblock]::Create($node.Extent.Text))
$temp=Join-Path $env:TEMP ('HostAgent-poweroff-ready-'+[guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($temp);$taskDataDirectory=$temp
function Assert-RebootPlainPath{}
function Assert-RebootSafety{}
$script:polls=0
function Get-RebootService{
 $script:polls++
 if($polls-eq2){[IO.File]::WriteAllText((Join-Path $taskDataDirectory 'reboot-after-start.json'),'{"mode":"poweroff-proof"}')}
 [pscustomobject]@{State='Running'}
}
try{
 $marker=Read-RebootMarker 'reboot-after-start.json'
 if($marker.mode-cne'poweroff-proof'-or$polls-lt2){throw 'running_service_marker_readiness_not_waited'}
 Write-Output 'PASS running service waited for durable after-start marker'
 function Get-RebootService{[pscustomobject]@{State='Stopped'}}
 $seen=$null;try{Read-RebootMarker 'reboot-after-start.json'}catch{$seen=$_.Exception.Message}
 if($seen-cne'reboot_automatic_start_not_running'){throw 'stopped_service_marker_accepted'}
 function Get-RebootService{[pscustomobject]@{State='Running'}}
 [IO.File]::WriteAllText((Join-Path $taskDataDirectory 'reboot-proof-closed.json'),'test')
 $seen=$null;try{Read-RebootMarker 'reboot-after-start.json'}catch{$seen=$_.Exception.Message}
 if($seen-cne'poweroff_graceful_shutdown_detected'){throw 'graceful_marker_was_accepted'}
 [IO.File]::Delete((Join-Path $taskDataDirectory 'reboot-proof-closed.json'))
 function Assert-RebootSafety{throw 'hard_free_memory_floor_512_mib'}
 $seen=$null;try{Read-RebootMarker 'reboot-after-start.json'}catch{$seen=$_.Exception.Message}
 if($seen-cne'hard_free_memory_floor_512_mib'){throw 'readiness_ignored_safety_abort'}
 Write-Output 'PASS 4 marker readiness / stopped / graceful / safety checks; no SCM operations.'
}finally{[IO.Directory]::Delete($temp,$true)}
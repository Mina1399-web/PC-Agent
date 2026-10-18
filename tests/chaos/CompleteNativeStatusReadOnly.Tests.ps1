$ErrorActionPreference='Stop'
$repo=Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
if($env:COMPUTERNAME-ne'AGENTPC' -or (Get-CimInstance Win32_ComputerSystem).Model-ne'VirtualBox' -or
 (Get-CimInstance Win32_ComputerSystemProduct).UUID-ne'46543236-8C5E-49DA-953C-DB17F9B1E402'){throw 'approved_dev_vm_required'}
Add-Type -Path (Join-Path $repo 'scripts/chaos/CompleteProtectedServiceGuard.cs')
$before=Get-CimInstance Win32_Service -Filter "Name='HostAgent'" -ErrorAction Stop
for($i=0;$i-lt100;$i++) {
 $status=[CompleteProtectedServiceGuard]::Read()
 if($status.State-ne'Running' -or $status.ProcessId-ne$before.ProcessId){throw 'native_status_does_not_match_protected_baseline'}
 Start-Sleep -Milliseconds 20
}
$after=Get-CimInstance Win32_Service -Filter "Name='HostAgent'" -ErrorAction Stop
foreach($property in @('State','ProcessId','PathName','StartName','StartMode')){
 if($before.$property-cne$after.$property){throw 'protected_identity_changed'}
}
[pscustomobject]@{scenario='native_protected_status_readonly';observations=100;state=$status.State;pid=$status.ProcessId;serviceOperations=0}|ConvertTo-Json -Compress

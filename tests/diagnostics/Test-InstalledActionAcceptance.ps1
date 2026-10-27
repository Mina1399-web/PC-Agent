$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
Import-Module (Join-Path $PSScriptRoot '../../scripts/diagnostics/InstalledActionAcceptance.psm1') -Force
$id='aabbccddeeff00112233445566778899'
$plan=New-DevActionPlan $id
if($plan.ServiceName -cne ('0HostAgentDevAcceptance_'+$id) -or $plan.Root -cne ('C:\ProgramData\HostAgent-DEV-Acceptance-'+$id) -or $plan.Alias -cne ('devaccept-'+$id)){throw 'guid_scope_not_exact'}
foreach($bad in @('',('0'*32),$id.ToUpperInvariant(),'../HostAgent')){
    try{New-DevActionPlan $bad|Out-Null;throw 'bad_scope_accepted'}catch{if($_.Exception.Message -cne 'canonical_nonempty_run_id_required'){throw}}
}
$reply=Read-DevCliReply ('Proposal: 11111111-2222-3333-4444-555555555555'+"`n"+'{"version":1,"ok":true,"code":"confirmation_required","data":{"requestId":"11111111-2222-3333-4444-555555555555"}}')
if($reply.code -cne 'confirmation_required'){throw 'proposal_json_not_parsed'}
foreach($bad in @('{"version":1,"ok":true,"code":"ready"}'+"`n"+'{"version":1,"ok":true,"code":"ready"}','configuration_cancelled','{"version":1,"ok":"true","code":"ready"}')){
    try{Read-DevCliReply $bad|Out-Null;throw 'bad_receipt_accepted'}catch{if($_.Exception.Message -cne 'cli_receipt_invalid'){throw}}
}
$now=[datetimeoffset]'2026-10-27T12:00:00Z'
$detail=[pscustomobject]@{incidentId='0123456789abcdef0123456789abcdef';state='Active';confidence=88;diagnosisId='diagnosis';observedAtUtc=$now.AddSeconds(-5);rootCause=[pscustomobject]@{code='service_stopped';subject=$plan.ServiceName};evidence=@([pscustomobject]@{source='service.state_change';observedAtUtc=$now.AddSeconds(-5)})}
if((Select-DevActionIncident @($detail) $plan $now).incidentId -cne $detail.incidentId){throw 'owned_fresh_incident_missing'}
foreach($change in @('subject','confidence','resolved','stale','future','event_only')){
    $copy=$detail|ConvertTo-Json -Depth 5|ConvertFrom-Json
    switch($change){
        subject{$copy.rootCause.subject='HostAgent'}
        confidence{$copy.confidence=78}
        resolved{$copy.state='Resolved'}
        stale{$copy.observedAtUtc=$now.AddMinutes(-4)}
        future{$copy.observedAtUtc=$now.AddSeconds(1)}
        event_only{$copy.evidence[0].source='eventlog.service-crash'}
    }
    if($null -ne (Select-DevActionIncident @($copy) $plan $now)){throw ('ineligible_incident_accepted_'+$change)}
}
try{Select-DevActionIncident @($detail,$detail) $plan $now|Out-Null;throw 'ambiguous_incidents_accepted'}catch{if($_.Exception.Message -cne 'ambiguous_owned_incident'){throw}}
$service=[pscustomobject]@{Name=$plan.ServiceName;PathName=('"'+$plan.Image+'" run '+$id);StartName=('NT SERVICE\'+$plan.ServiceName);StartMode='Auto';State='Stopped';ProcessId=0}
Assert-DevTargetIdentity $plan $service 'ABC' 'ABC'
$forged=$plan|ConvertTo-Json|ConvertFrom-Json;$forged.Image='C:\Program Files\HostAgent\Service\HostAgent.Service.exe'
try{Assert-DevTargetIdentity $forged $service 'ABC' 'ABC';throw 'forged_target_accepted'}catch{if($_.Exception.Message -cne 'owned_target_identity_mismatch'){throw}}
try{Assert-DevTargetIdentity $plan $service 'ABC' 'CHANGED';throw 'changed_target_image_accepted'}catch{if($_.Exception.Message -cne 'owned_target_identity_mismatch'){throw}}
$manual=$service|ConvertTo-Json|ConvertFrom-Json;$manual.StartMode='Manual'
try{Assert-DevTargetIdentity $plan $manual 'ABC' 'ABC';throw 'manual_target_allowed_for_action'}catch{if($_.Exception.Message -cne 'owned_target_identity_mismatch'){throw}}
Assert-DevTargetIdentity $plan $manual 'ABC' 'ABC' -Retiring
$create=@(Get-DevScmArguments $plan 'create')
if($create[1] -cne $plan.ServiceName -or $create -notcontains 'disabled' -or $create -notcontains ('NT SERVICE\'+$plan.ServiceName)){throw 'partial_setup_must_not_create_autostart_target'}
foreach($operation in @('start','stop','disable')){if(@(Get-DevScmArguments $plan $operation)[1] -cne $plan.ServiceName){throw 'scm_operation_wrong_target'}}
if(@(Get-DevScmArguments $plan 'arm') -notcontains 'auto'){throw 'owned_auto_mode_not_explicitly_armed'}
$controller=Join-Path $PSScriptRoot '../../scripts/diagnostics/Complete-InstalledDevAcceptance.ps1'
$tokens=$null;$parseErrors=$null;$ast=[Management.Automation.Language.Parser]::ParseFile($controller,[ref]$tokens,[ref]$parseErrors)
if($parseErrors.Count){throw 'controller_parse_failed'}
$outer=@($ast.EndBlock.Statements|Where-Object{$_ -is [Management.Automation.Language.TryStatementAst]})
if($outer.Count -ne 1 -or -not $outer[0].Finally){throw 'controller_cleanup_missing'}
$cleanupBlock=[scriptblock]::Create($outer[0].Finally.Extent.Text.Trim().Substring(1,$outer[0].Finally.Extent.Text.Trim().Length-2))
foreach($name in @('Read-HelperConfiguration','Set-OwnedAcl')){
    $definition=@($ast.EndBlock.Statements|Where-Object{$_ -is [Management.Automation.Language.FunctionDefinitionAst] -and $_.Name -ceq $name})[0]
    . ([scriptblock]::Create($definition.Extent.Text))
}
& {
    $sid='S-1-5-80-11-22-33-44-55';$script:capturedAcl=$null
    function Assert-PlainPath {param($Path)}
    function Set-Acl {param($LiteralPath,$AclObject);$script:capturedAcl=$AclObject}
    foreach($kind in @('root','app','file','data')){
        Set-OwnedAcl 'C:\owned-test-fixture' $sid -File:($kind -eq 'file') -Writable:($kind -eq 'data')
        if(-not $script:capturedAcl.AreAccessRulesProtected -or $script:capturedAcl.GetOwner([Security.Principal.SecurityIdentifier]).Value -cne 'S-1-5-32-544'){throw 'owned_acl_owner_or_inheritance_wrong'}
        $rules=@($script:capturedAcl.GetAccessRules($true,$true,[Security.Principal.SecurityIdentifier])|Where-Object{$_.IdentityReference.Value -ceq $sid})
        if($rules.Count -ne 1){throw 'owned_service_acl_count_wrong'}
        $forbidden=[Security.AccessControl.FileSystemRights]::Write -bor [Security.AccessControl.FileSystemRights]::Delete -bor [Security.AccessControl.FileSystemRights]::DeleteSubdirectoriesAndFiles -bor [Security.AccessControl.FileSystemRights]::ChangePermissions -bor [Security.AccessControl.FileSystemRights]::TakeOwnership
        if($kind -ne 'data' -and ($rules[0].FileSystemRights -band $forbidden)){throw 'owned_app_acl_allows_write_or_delete'}
        if($kind -eq 'data' -and ($rules[0].FileSystemRights -band [Security.AccessControl.FileSystemRights]::FullControl) -ne [Security.AccessControl.FileSystemRights]::FullControl){throw 'owned_data_not_writable'}
    }
}
foreach($case in @('quiescence_failed','quiescent','changed_alias')){
    & {
        param($Scenario,$ActualFinally,$Plan)
        $plan=$Plan;$operatorExecuteRequested=$true;$approvedVm=$true;$created=$true;$targetReady=$true;$creationRequested=$true;$aliasAttempted=$true;$aliasRemoved=$false
        $initialFacts=[pscustomobject]@{AllowlistFingerprint='original'};$release=$null
        $summary=@{status='NOTRUN';failureCode=$null};$cleanupErrors=[Collections.Generic.List[string]]::new();$script:fixtureRemovals=0
        function Invoke-OwnedScm {param([string]$Operation,[switch]$Recovery);if($Scenario -eq 'quiescence_failed'){throw 'simulated_stop_failure'}}
        function Get-OwnedTarget {param([switch]$Retiring);[pscustomobject]@{State='Stopped';StartMode='Disabled'}}
        function Wait-OwnedState {param([string]$State,[switch]$Recovery);Get-OwnedTarget}
        function Wait-OwnedView {param([string]$State,[switch]$Retiring);[pscustomobject]@{state=$State}}
        function Read-ProtectedModeJson {
            param($Path,$AllowedFields,$TrustedSids,$Kind)
            @{services=@(@{id=$plan.Alias;serviceName=$(if($Scenario -eq 'changed_alias'){'HostAgent'}else{$plan.ServiceName});enabledOperations=@('StartService')})}
        }
        function Invoke-InteractiveCli {param([string[]]$Arguments);if($Arguments[0] -cne 'remove-service' -or $Arguments[1] -cne $plan.Alias){throw 'cleanup_wrong_cli'};$script:fixtureRemovals++}
        function Get-InstalledModeFacts {param($Manifest);[pscustomobject]@{AllowlistFingerprint='original'}}
        function Assert-ModeFacts {param($Facts)}
        & $ActualFinally
        $expected=if($Scenario -eq 'quiescent'){1}else{0}
        if($script:fixtureRemovals -ne $expected){throw ('cleanup_alias_removed_before_quiescence_'+$Scenario)}
    } $case $cleanupBlock $plan
}
'PASS installed_action_scope_and_receipts; native_operations=0'

[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ManifestPath,
    [Parameter(Mandatory)][string]$ExpectedManifestSha256,
    [switch]$Execute
)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$operatorExecuteRequested=[bool]$Execute
$stage='preflight';$approvedVm=$false;$created=$false;$aliasAttempted=$false;$aliasRemoved=$false
$creationRequested=$false;$targetReady=$false
$actionSucceeded=$false;$request=$null;$incident=$null;$initialFacts=$null;$plan=$null;$runRoot=$null
$summary=[ordered]@{scenario='installed_DEV_exact_approval_and_database_acceptance';status='NOTRUN';failureCode=$null;failureStage=$null;runId=$null;actionAcceptance='NOTRUN';journalIntegrity='NOTRUN';databaseIntegrity='NOTRUN';cleanup='NOTRUN';services=@();resultPath=$null;manualApprovals='normal_CLI_APPLY_and_exact_request_GUID_only'}
$summary['scmOperations']=[Collections.Generic.List[object]]::new()
$summary['lowMemoryWarningObserved']=$false;$summary['minimumObservedFreeMemoryMiB']=$null
$summary['resourceFloors']=[pscustomobject]@{startAboveMiB=768;warningMiB=768;emergencyMiB=512;diskGiB=10}
$cleanupErrors=[Collections.Generic.List[string]]::new()
$cli='C:\Program Files\HostAgent\Cli\HostAgent.Cli.exe'

function Assert-Resources([switch]$Initial){
    $available=[double](Get-CimInstance Win32_OperatingSystem -OperationTimeoutSec 5).FreePhysicalMemory/1024
    if($null -eq $summary.minimumObservedFreeMemoryMiB -or $available -lt $summary.minimumObservedFreeMemoryMiB){$summary.minimumObservedFreeMemoryMiB=[math]::Round($available,1)}
    if($available -lt 768){$summary.lowMemoryWarningObserved=$true}
    if($available -le 512){throw 'hard_memory_floor_512_mib'}
    if($Initial -and $available -le 768){throw 'initial_memory_must_exceed_768_mib'}
    if((Get-PSDrive C).Free -lt 10GB){throw 'disk_floor_10_gib'}
}
function Assert-PlainPath([string]$Path){
    $cursor=[IO.Path]::GetFullPath($Path)
    while($cursor){
        $item=Get-Item -LiteralPath $cursor -Force -ErrorAction Stop
        if($item.Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'reparse_path_rejected'}
        $parent=Split-Path -Path $cursor -Parent
        if(-not $parent -or $parent -ceq $cursor){break};$cursor=$parent
    }
}
function Invoke-ReadProcess([string]$File,[string[]]$Arguments,[int]$Seconds=45,[switch]$Recovery){
    $info=[Diagnostics.ProcessStartInfo]::new($File);$info.UseShellExecute=$false;$info.CreateNoWindow=$true
    $info.RedirectStandardOutput=$true;$info.RedirectStandardError=$true
    foreach($argument in $Arguments){$info.ArgumentList.Add($argument)}
    $child=[Diagnostics.Process]::new();$child.StartInfo=$info;$started=$false
    try{
        if(-not $child.Start()){throw 'read_child_start_failed'};$started=$true
        $stdout=$child.StandardOutput.ReadToEndAsync();$stderr=$child.StandardError.ReadToEndAsync();$timer=[Diagnostics.Stopwatch]::StartNew()
        while(-not $child.WaitForExit(250)){
            if(-not $Recovery){Assert-Resources}
            if($timer.Elapsed.TotalSeconds -ge $Seconds){throw 'read_child_deadline'}
        }
        if(-not $stdout.Wait(2000) -or -not $stderr.Wait(2000) -or $stdout.Result.Length -gt 2MB -or $stderr.Result.Length -gt 65536){throw 'read_child_output_limit'}
        [pscustomobject]@{ExitCode=$child.ExitCode;Output=$stdout.Result;ErrorPresent=($stderr.Result.Length -gt 0)}
    }finally{
        # These are read-only probes or exact scoped sc.exe clients, never MSI/configuration/approval children.
        if($started -and -not $child.HasExited){$child.Kill($true);[void]$child.WaitForExit(5000)}
        $child.Dispose()
    }
}
function Read-Cli([string[]]$Arguments){
    $result=Invoke-ReadProcess $cli $Arguments
    if($result.ExitCode -ne 0 -or $result.ErrorPresent){throw 'cli_read_or_proposal_failed'}
    $reply=Read-DevCliReply $result.Output
    if(-not $reply.ok){throw 'cli_read_or_proposal_rejected'}
    $jsonLine=@($result.Output -split '\r?\n'|Where-Object{$_.TrimStart().StartsWith('{')})[0]
    $document=[Text.Json.JsonDocument]::Parse($jsonLine)
    try{$reply|Add-Member -NotePropertyName CanonicalDataJson -NotePropertyValue $document.RootElement.GetProperty('data').GetRawText()}finally{$document.Dispose()}
    $reply
}
function Invoke-InteractiveCli([string[]]$Arguments){
    Assert-Resources
    $LASTEXITCODE=0
    & $cli @Arguments
    if($LASTEXITCODE -ne 0){throw 'interactive_cli_cancelled_or_failed'}
}
function Read-HelperConfiguration {
    Read-ProtectedModeJson -Path 'C:\ProgramData\HostAgent.Helper\helper.json' -AllowedFields @('readOnly','shadow','killSwitch','executionTimeoutSeconds','services','processes','approverSids') -TrustedSids @('S-1-5-18','S-1-5-32-544') -Kind Helper
}
function Get-OwnedTarget([switch]$Retiring) {
    $target=Get-CimInstance Win32_Service -Filter ("Name='"+$plan.ServiceName+"'") -OperationTimeoutSec 5
    Assert-PlainPath $plan.Image
    Assert-DevTargetIdentity $plan $target $targetHash (Get-FileHash -LiteralPath $plan.Image).Hash -Retiring:$Retiring
    $reg=Get-ItemProperty -LiteralPath ('Registry::HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\'+$plan.ServiceName)
    if([int]$reg.Type -ne 16 -or (Test-Path -LiteralPath ('Registry::HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\'+$plan.ServiceName+'\TriggerInfo'))){throw 'owned_target_type_or_trigger_mismatch'}
    if([int]$target.ProcessId -gt 0){
        $process=Get-CimInstance Win32_Process -Filter ('ProcessId='+[int]$target.ProcessId) -OperationTimeoutSec 5
        $protected=@(Get-CimInstance Win32_Service -Filter "Name='HostAgent' OR Name='HostAgent.Helper'" -OperationTimeoutSec 5)
        if(-not $process -or [int]$target.ProcessId -le 4 -or [int]$target.ProcessId -eq $PID -or [int]$target.ProcessId -in @($protected.ProcessId) -or
            $process.ExecutablePath -ine $plan.Image -or $process.CommandLine -cne ('"'+$plan.Image+'" run '+$plan.RunId) -or [int]$process.SessionId -ne 0){throw 'owned_target_process_identity_mismatch'}
    }
    $target
}
function Invoke-OwnedScm([ValidateSet('create','arm','retire','start','stop','disable')][string]$Operation,[switch]$Recovery){
    if($Operation -eq 'create'){
        if(Get-CimInstance Win32_Service -Filter ("Name='"+$plan.ServiceName+"'") -OperationTimeoutSec 5){throw 'target_registration_already_exists'}
        $script:creationRequested=$true
    }else{Get-OwnedTarget -Retiring:$Recovery|Out-Null}
    $result=Invoke-ReadProcess 'C:\Windows\System32\sc.exe' @(Get-DevScmArguments $plan $Operation) 30 -Recovery:$Recovery
    $summary.scmOperations.Add([pscustomobject]@{operation=$Operation;serviceName=$plan.ServiceName;exitCode=$result.ExitCode})
    if($result.ExitCode -ne 0){throw ('owned_scm_'+$Operation+'_failed')}
}
function Wait-OwnedState([string]$State,[switch]$Recovery){
    $timer=[Diagnostics.Stopwatch]::StartNew()
    do{
        if(-not $Recovery){Assert-Resources}
        $current=Get-OwnedTarget -Retiring:$Recovery
        if($current.State -ceq $State){return $current}
        if($timer.Elapsed.TotalSeconds -ge 35){throw 'owned_service_state_deadline'}
        Start-Sleep -Milliseconds 250
    }while($true)
}
function Set-OwnedAcl([string]$Path,[string]$ServiceSid,[switch]$Writable,[switch]$File){
    Assert-PlainPath $Path
    $security=if($File){[Security.AccessControl.FileSecurity]::new()}else{[Security.AccessControl.DirectorySecurity]::new()}
    $security.SetAccessRuleProtection($true,$false);$security.SetOwner([Security.Principal.SecurityIdentifier]::new('S-1-5-32-544'))
    foreach($entry in @(@('S-1-5-18','FullControl'),@('S-1-5-32-544','FullControl'),@($ServiceSid,$(if($Writable){'FullControl'}else{'ReadAndExecute'})))){
        if(-not $entry[0]){continue}
        $sid=[Security.Principal.SecurityIdentifier]::new($entry[0])
        $rule=if($File){[Security.AccessControl.FileSystemAccessRule]::new($sid,$entry[1],'Allow')}else{[Security.AccessControl.FileSystemAccessRule]::new($sid,$entry[1],'ContainerInherit,ObjectInherit','None','Allow')}
        $security.AddAccessRule($rule)
    }
    Set-Acl -LiteralPath $Path -AclObject $security
}
function Assert-ModeFacts($Facts){
    if(-not $Facts.ApprovedVm -or -not $Facts.Elevated -or -not $Facts.InstalledValid -or -not $Facts.ServicesValid -or -not $Facts.ConfigurationValid -or
        $Facts.Mode -cne 'Confirmation' -or $Facts.ReadOnly -or $Facts.HelperReadOnly -or $Facts.HelperShadow -or
        $Facts.ServiceKillSwitch -or $Facts.HelperKillSwitch -or $Facts.DisableMarkerPresent -or -not $Facts.ApproverPresent -or
        $Facts.Pushover -or $Facts.Heartbeat -or $Facts.NetworkProbes){throw 'installed_confirmation_preflight_failed'}
}
function Wait-OwnedView([string]$State,[switch]$Retiring){
    $timer=[Diagnostics.Stopwatch]::StartNew()
    do{
        Assert-Resources;Get-OwnedTarget -Retiring:$Retiring|Out-Null
        $result=Invoke-ReadProcess $viewProbe @('installed-acceptance-view',$plan.RunId) 30
        if($result.ExitCode -eq 0){
            $view=$result.Output|ConvertFrom-Json
            if($view.valid -eq $true -and $view.name -ceq $plan.ServiceName -and $view.state -ceq $State){return $view}
        }
        if($timer.Elapsed.TotalSeconds -ge 300){throw 'owned_collector_view_deadline'}
        Start-Sleep -Seconds 2
    }while($true)
}

try{
    $who=[Security.Principal.WindowsIdentity]::GetCurrent()
    if($env:COMPUTERNAME -ne 'AGENTPC' -or $who.Name -ine 'AgentPC\AgentPCdevtest' -or (Get-CimInstance Win32_ComputerSystem -OperationTimeoutSec 5).Model -ne 'VirtualBox' -or
        (Get-CimInstance Win32_ComputerSystemProduct -OperationTimeoutSec 5).UUID -ine '46543236-8C5E-49DA-953C-DB17F9B1E402' -or (Get-Service VBoxService).Status -ne 'Running'){throw 'approved_dev_vm_required'}
    $approvedVm=$true;Assert-Resources -Initial
    if($PSVersionTable.PSVersion.Major -lt 7 -or [IntPtr]::Size -ne 8){throw 'powershell7_x64_required'}
    Assert-PlainPath $ManifestPath
    if($ExpectedManifestSha256 -cnotmatch '^[A-F0-9]{64}$' -or (Get-FileHash -LiteralPath $ManifestPath).Hash -cne $ExpectedManifestSha256){throw 'handoff_manifest_hash_mismatch'}
    $manifestFile=Get-Item -LiteralPath $ManifestPath
    if($manifestFile.Length -gt 2MB){throw 'handoff_manifest_limit'}
    $handoff=Get-Content -LiteralPath $ManifestPath -Raw|ConvertFrom-Json
    if($handoff.format -ne 1 -or @($handoff.files).Count -lt 5 -or @($handoff.files).Count -gt 2000){throw 'handoff_manifest_invalid'}
    $handoffRoot=Split-Path -Parent ([IO.Path]::GetFullPath($ManifestPath));$seen=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach($entry in $handoff.files){
        Assert-Resources
        if($entry.path -cnotmatch '^[A-Za-z0-9_.-]+(/[A-Za-z0-9_.-]+)*$' -or @($entry.path.Split('/')|Where-Object{$_ -in '.','..'}).Count -or -not $seen.Add($entry.path)){throw 'handoff_entry_invalid'}
        $path=Join-Path $handoffRoot $entry.path;Assert-PlainPath $path
        if((Get-Item -LiteralPath $path).Length -ne $entry.length -or (Get-FileHash -LiteralPath $path).Hash -cne $entry.sha256){throw 'handoff_entry_hash_mismatch'}
    }
    $loader=Join-Path $handoffRoot 'Read-InstalledDevConfirmationPlan.ps1'
    $databaseTool=Join-Path $handoffRoot 'Read-InstalledDatabaseSnapshot.ps1'
    $viewProbe=Join-Path $handoffRoot 'probe\HostAgent.PipeProbe.exe'
    $targetSource=Join-Path $handoffRoot 'target\HostAgent.DevActionTarget.exe'
    foreach($required in @('Read-InstalledDevConfirmationPlan.ps1','Read-InstalledDatabaseSnapshot.ps1','InstalledActionAcceptance.psm1','probe/HostAgent.PipeProbe.exe','target/HostAgent.DevActionTarget.exe')){if(-not $seen.Contains($required)){throw 'required_handoff_entry_missing'}}
    Import-Module (Join-Path $handoffRoot 'InstalledActionAcceptance.psm1') -Force
    # Preserve the original switch; the shared loader binds its own Execute parameter when dot-sourced.
    . $loader -PackagePath $handoff.product.path -ExpectedManifestSha256 $handoff.product.manifestSha256 -ExpectedSourceCommit $handoff.product.sourceCommit -ExpectedVersion $handoff.product.version
    $release=Read-ModeManifest -LiteralPath (Join-Path $handoff.product.path 'manifest.psd1') -ExpectedManifestSha256 $handoff.product.manifestSha256 -ExpectedSourceCommit $handoff.product.sourceCommit -ExpectedVersion $handoff.product.version
    $expectedCritical=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach($entry in $release.Files|Where-Object{$_.Path -match '^(Service|Helper|Cli|Desktop)/.+\.(exe|dll|deps\.json|runtimeconfig\.json)$'}){
        Assert-Resources;$path=Join-Path 'C:\Program Files\HostAgent' $entry.Path;Assert-PlainPath $path
        if((Get-Item -LiteralPath $path).Length -ne $entry.Length -or (Get-FileHash -LiteralPath $path).Hash -cne $entry.Sha256){throw 'installed_critical_payload_mismatch'}
        [void]$expectedCritical.Add($entry.Path.Replace('/','\'))
    }
    if($expectedCritical.Count -lt 8){throw 'installed_critical_inventory_missing'}
    $queue=[Collections.Generic.Stack[string]]::new();foreach($folder in @('Service','Helper','Cli','Desktop')){$queue.Push((Join-Path 'C:\Program Files\HostAgent' $folder))}
    $count=0
    while($queue.Count){
        $directory=$queue.Pop();Assert-PlainPath $directory
        foreach($file in Get-ChildItem -LiteralPath $directory -Force){
            if(++$count -gt 10000 -or $file.Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'installed_tree_limit_or_reparse'}
            if($file.PSIsContainer){$queue.Push($file.FullName)}elseif($file.Name -match '\.(exe|dll|deps\.json|runtimeconfig\.json)$'){
                $relative=$file.FullName.Substring('C:\Program Files\HostAgent\'.Length)
                if(-not $expectedCritical.Contains($relative)){throw 'unexpected_installed_critical_file'}
            }
        }
    }
    $publicServices=@(Get-CimInstance Win32_Service -Filter "Name='HostAgent' OR Name='HostAgent.Helper'" -OperationTimeoutSec 5)
    if($publicServices.Count -ne 2){throw 'product_services_missing'}
    foreach($service in $publicServices){
        $expectedAccount=if($service.Name -eq 'HostAgent'){'NT SERVICE\HostAgent'}else{'LocalSystem'}
        $expectedPath=if($service.Name -eq 'HostAgent'){'"C:\Program Files\HostAgent\Service\HostAgent.Service.exe"'}else{'"C:\Program Files\HostAgent\Helper\HostAgent.Helper.exe"'}
        if($service.StartMode -cne 'Auto' -or $service.StartName -ine $expectedAccount -or $service.PathName -cne $expectedPath -or $service.State -cne 'Running'){throw 'product_service_public_preflight_failed'}
    }
    $summary['installedCriticalFilesVerified']=$expectedCritical.Count
    $plan=New-DevActionPlan ([guid]::NewGuid().ToString('N'));$summary.runId=$plan.RunId
    $targetHash=(Get-FileHash -LiteralPath $targetSource).Hash
    $summary['plannedTarget']=$plan.ServiceName;$summary['plannedRoot']=$plan.Root;$summary['packageVerified']=$true
    if(-not $operatorExecuteRequested){$summary.status='READ_ONLY_PLAN_READY_ADMIN_RUN_PENDING'}else{
        if(-not ([Security.Principal.WindowsPrincipal]::new($who)).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator) -or [Console]::IsInputRedirected){throw 'elevated_interactive_operator_required'}
        $initialFacts=Get-InstalledModeFacts $release;Assert-ModeFacts $initialFacts
        $status=Read-Cli @('status','--verbose')
        if($status.data.mode -cne 'Confirmation' -or $status.data.ReadOnly -ne $false -or -not $status.data.ConfigurationAvailable){throw 'runtime_confirmation_not_loaded'}
        $summary['initialDegraded']=[bool]$status.data.degraded
        $monitorSid=([Security.Principal.NTAccount]::new('NT SERVICE','HostAgent')).Translate([Security.Principal.SecurityIdentifier]).Value
        $monitorConfig=Read-ProtectedModeJson -Path 'C:\ProgramData\HostAgent\service.json' -AllowedFields @('mode','readOnly','killSwitch','collectionIntervalSeconds','heartbeatIntervalSeconds','requestTimeoutSeconds','enablePushover','enableHeartbeat','enableNetworkProbes') -TrustedSids @('S-1-5-18','S-1-5-32-544',$monitorSid) -Kind Service
        $interval=[int]$monitorConfig.collectionIntervalSeconds
        if($interval -lt 5 -or $interval -gt 120){throw 'collection_interval_not_compatible_with_bounded_acceptance'}
        $summary['collectionIntervalSeconds']=$interval
        $initialHelper=Read-HelperConfiguration
        if(@($initialHelper.services|Where-Object{$_.id -ceq $plan.Alias}).Count){throw 'test_alias_already_exists'}
        $stage='database_read_only_plan_before_any_fault'
        $LASTEXITCODE=0
        $dbPlanLines=@(& $databaseTool -PackagePath $handoff.product.path -ExpectedManifestSha256 $handoff.product.manifestSha256 -ExpectedSourceCommit $handoff.product.sourceCommit -ExpectedVersion $handoff.product.version)
        $dbPlanExit=$LASTEXITCODE
        if($dbPlanLines.Count -ne 1){throw 'database_preflight_receipt_invalid'}
        $dbPlan=$dbPlanLines[0]|ConvertFrom-Json
        $summary['databasePreflight']=[pscustomobject]@{Outcome=$dbPlan.Outcome;FailureCode=$dbPlan.FailureCode}
        if($dbPlanExit -ne 0 -or $dbPlan.Outcome -cne 'plan_ready'){throw 'database_read_only_preflight_blocked'}
        if(Test-Path -LiteralPath $plan.Root){throw 'fresh_owned_target_required'}
        $runRoot=Join-Path $env:LOCALAPPDATA ('HostAgent-DEV-evidence\final-installed-acceptance-'+$plan.RunId)
        if(Test-Path -LiteralPath $runRoot){throw 'fresh_evidence_required'}
        [void][IO.Directory]::CreateDirectory($runRoot);$plan|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $runRoot 'target-plan.json') -Encoding utf8
        $stage='create_owned_target'
        [void][IO.Directory]::CreateDirectory($plan.Root);Set-OwnedAcl $plan.Root $null
        Invoke-OwnedScm 'create';$created=$true
        $sid=([Security.Principal.NTAccount]::new('NT SERVICE',$plan.ServiceName)).Translate([Security.Principal.SecurityIdentifier]).Value
        Set-OwnedAcl $plan.Root $sid
        [void][IO.Directory]::CreateDirectory($plan.App);[void][IO.Directory]::CreateDirectory($plan.Data)
        Set-OwnedAcl $plan.App $sid;Set-OwnedAcl $plan.Data $sid -Writable
        $inputFile=[IO.File]::OpenRead($targetSource)
        try{$outputFile=[IO.File]::Open($plan.Image,'CreateNew','Write','None');try{$inputFile.CopyTo($outputFile);$outputFile.Flush($true)}finally{$outputFile.Dispose()}}finally{$inputFile.Dispose()}
        Set-OwnedAcl $plan.Image $sid -File
        Get-OwnedTarget|Out-Null
        $validation=Invoke-ReadProcess $plan.Image @('validate',$plan.RunId)
        if($validation.ExitCode -ne 0 -or ($validation.Output|ConvertFrom-Json).valid -ne $true){throw 'owned_target_preflight_failed'}
        $targetReady=$true
        $stage='register_one_test_alias'
        Write-Host 'Potvrd prosim APPLY pro registraci pouze zobrazene testovaci sluzby. Produktove sluzby se standardne restartuji.'
        $aliasAttempted=$true
        Invoke-InteractiveCli @('allow-service',$plan.Alias,$plan.ServiceName,'StartService')
        Assert-ModeFacts (Get-InstalledModeFacts $release)
        $registeredHelper=Read-HelperConfiguration
        $registered=@($registeredHelper.services|Where-Object{$_.id -ceq $plan.Alias})
        if($registered.Count -ne 1 -or $registered[0].serviceName -cne $plan.ServiceName -or @($registered[0].enabledOperations).Count -ne 1 -or $registered[0].enabledOperations[0] -cne 'StartService'){throw 'test_alias_registration_not_exact'}
        $stage='observe_running_then_stop'
        # First start occurs AFTER configuration restart. A prior persisted snapshot cannot contain this GUID as Running.
        Invoke-OwnedScm 'arm';Invoke-OwnedScm 'start';$first=Wait-OwnedState 'Running';$summary['firstTargetPid']=[int]$first.ProcessId
        $running=Wait-OwnedView 'Running';$summary['runningObservedAtUtc']=$running.generatedAtUtc
        Invoke-OwnedScm 'stop';Wait-OwnedState 'Stopped'|Out-Null
        $stopped=Wait-OwnedView 'Stopped';$summary['stoppedObservedAtUtc']=$stopped.generatedAtUtc
        $stage='wait_real_eligible_incident';$timer=[Diagnostics.Stopwatch]::StartNew()
        do{
            Assert-Resources;$details=@();$heads=Read-Cli @('incidents')
            foreach($head in @($heads.data)){
                if([int]$head.Confidence -ne 88 -or $head.state -notin @('Detected','Active','Acknowledged','Failed')){continue}
                $reply=Read-Cli @('incident',[string]$head.IncidentId);$details+= $reply.data
            }
            $incident=Select-DevActionIncident $details $plan ([datetimeoffset]::UtcNow)
            if($incident){break}
            if($timer.Elapsed.TotalSeconds -ge 180){throw 'real_actionable_incident_not_observed'}
            Start-Sleep -Seconds 2
        }while($true)
        $summary['incidentId']=$incident.incidentId;$summary['confidence']=[int]$incident.confidence
        $stage='propose_and_manual_exact_approval'
        Get-OwnedTarget|Out-Null
        $proposal=Read-Cli @('propose',$incident.incidentId,'StartService',$plan.Alias)
        $request=$proposal.data
        if($proposal.code -cne 'confirmation_required' -or $request.operation -cne 'StartService' -or $request.parameters.serviceId -cne $plan.Alias -or
            $request.incidentId -cne $incident.incidentId -or $request.diagnosisId -cne $incident.diagnosisId -or [int]$request.confidence -ne 88 -or
            [string]$request.requestId -cnotmatch '^[0-9a-f]{8}(-[0-9a-f]{4}){3}-[0-9a-f]{12}$'){throw 'owned_proposal_binding_mismatch'}
        $summary['requestGuid']=$request.requestId
        # The server's data is the exact HelperProtocol.SerializeRequest JSON with approvalId=null.
        # Hash those bytes, retaining original property order/escaping, rather than reserializing PowerShell objects.
        if($null -ne $request.approvalId){throw 'proposal_already_contains_approval'}
        $requestDigest=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($proposal.CanonicalDataJson)))
        $summary['requestDigest']=$requestDigest
        Write-Host 'Ted CLI ukaze presnou akci a pozada o opsani jejiho GUID. Schval pouze StartService pro tuto testovaci sluzbu.'
        Invoke-InteractiveCli @('approve-execute',$request.requestId)
        $second=Wait-OwnedState 'Running'
        if([int]$second.ProcessId -le 4 -or [int]$second.ProcessId -eq [int]$first.ProcessId){throw 'helper_start_not_observed'}
        $afterIncident=Read-Cli @('incident',$incident.incidentId)
        if($afterIncident.data.state -cne 'Resolved'){throw 'remediation_lifecycle_not_resolved'}
        Wait-OwnedView 'Running'|Out-Null
        $actionSucceeded=$true;$summary.actionAcceptance='PASS_runtime_pending_journal_linkage';$summary['secondTargetPid']=[int]$second.ProcessId
    }
}catch{
    $summary.status='FAIL_OR_INCOMPLETE';$summary.failureStage=$stage
    $summary.failureCode=if($_.Exception.Message -cmatch '^[A-Za-z0-9_]+$'){$_.Exception.Message}else{'acceptance_step_failed'}
}finally{
    $targetQuiescent=$false
    if($operatorExecuteRequested -and $approvedVm -and $plan){
        if($created -and $targetReady){
            try{
                # Retire as demand-start before the cleanup stop so the collector does not diagnose the test's retirement as a new Auto-service outage.
                Invoke-OwnedScm 'retire' -Recovery
                $current=Get-OwnedTarget -Retiring
                if($current.State -ne 'Stopped'){Invoke-OwnedScm 'stop' -Recovery}
                Wait-OwnedState 'Stopped' -Recovery|Out-Null
                try{Wait-OwnedView 'Stopped' -Retiring|Out-Null}catch{$cleanupErrors.Add('retirement_collector_observation_unconfirmed')}
                Invoke-OwnedScm 'disable' -Recovery
                $current=Wait-OwnedState 'Stopped' -Recovery
                if($current.StartMode -ne 'Disabled'){throw 'owned_service_not_disabled'}
                $targetQuiescent=$true
            }catch{$cleanupErrors.Add('test_service_quiescence_unconfirmed')}
        }elseif($creationRequested){
            try{
                $partial=Get-CimInstance Win32_Service -Filter ("Name='"+$plan.ServiceName+"'") -OperationTimeoutSec 5
                if($partial){
                    if($partial.Name -cne $plan.ServiceName -or $partial.PathName -cne ('"'+$plan.Image+'" run '+$plan.RunId) -or
                        $partial.StartName -ine ('NT SERVICE\'+$plan.ServiceName) -or $partial.StartMode -cne 'Disabled' -or $partial.State -cne 'Stopped' -or [int]$partial.ProcessId -ne 0){throw 'partial_target_not_quiescent'}
                }
                $targetQuiescent=$true
            }catch{$cleanupErrors.Add('partial_target_quiescence_unconfirmed')}
        }
        if($aliasAttempted -and -not $targetQuiescent){$cleanupErrors.Add('test_alias_preserved_until_target_quiescent')}
        if($aliasAttempted -and $targetQuiescent){
            try{
                $cfg=Read-HelperConfiguration
                $registered=@($cfg.services|Where-Object{$_.id -ceq $plan.Alias})
                if($registered.Count){
                    if($registered.Count -ne 1 -or $registered[0].serviceName -cne $plan.ServiceName -or @($registered[0].enabledOperations).Count -ne 1 -or $registered[0].enabledOperations[0] -cne 'StartService'){throw 'test_alias_binding_changed'}
                    Write-Host 'Uklid: potvrd APPLY pro odebrani pouze tohoto testovaciho aliasu. Dukazy a testovaci balicek zustanou zachovane.'
                    Invoke-InteractiveCli @('remove-service',$plan.Alias)
                }
                $finalFacts=Get-InstalledModeFacts $release;Assert-ModeFacts $finalFacts
                if($finalFacts.AllowlistFingerprint -cne $initialFacts.AllowlistFingerprint){throw 'original_allowlist_not_restored'}
                $aliasRemoved=$true
            }catch{$cleanupErrors.Add('test_alias_cleanup_unconfirmed')}
        }
        if($cleanupErrors.Count){$summary.status='FAIL_OR_INCOMPLETE';if(-not $summary.failureCode){$summary.failureCode='cleanup_unconfirmed'}}
        $summary.cleanup=if($created -and $aliasRemoved -and -not $cleanupErrors.Count){'PASS_target_stopped_disabled_alias_removed_all_evidence_preserved'}elseif(-not $created){'NOT_CREATED'}else{'FAIL_OR_INCOMPLETE'}
        $summary['cleanupFailures']=@($cleanupErrors.ToArray())
    }
}

# Source DBs are never opened by SQL. The reviewed snapshot tool gracefully stops/restores only the two fixed product services.
if($operatorExecuteRequested -and $actionSucceeded -and $summary.cleanup -ceq 'PASS_target_stopped_disabled_alias_removed_all_evidence_preserved' -and -not $summary.failureCode){
    try{
        $stage='offline_copy_database_and_linked_journal_check'
        $LASTEXITCODE=0
        $databaseReceipt=@(& $databaseTool -PackagePath $handoff.product.path -ExpectedManifestSha256 $handoff.product.manifestSha256 -ExpectedSourceCommit $handoff.product.sourceCommit -ExpectedVersion $handoff.product.version -ExpectedRequestGuid $request.requestId -ExpectedIncidentId $incident.incidentId -ExpectedServiceName $plan.ServiceName -ExpectedServiceId $plan.Alias -ExpectedOperation StartService -ExpectedRequestDigest $requestDigest -Execute)
        $databaseExit=$LASTEXITCODE
        if($databaseReceipt.Count -ne 1){throw 'database_snapshot_receipt_invalid'}
        $db=$databaseReceipt[0]|ConvertFrom-Json
        $summary['databaseReceipt']=$db
        if($databaseExit -ne 0){throw 'database_snapshot_failed_or_incomplete'}
        # The snapshot receipt's exact acceptance fields are validated again after integration review.
        if($db.ResultSchemaVersion -ne 1 -or $db.Outcome -cne 'passed' -or $db.JournalLinkage -cne 'PASS' -or $db.ModePreserved -ne $true -or
            @($db.Databases).Count -ne 3 -or @($db.Databases|Where-Object{$_.Status -cne 'PASS' -or $_.QuickCheck -cne 'ok'}).Count){throw 'database_or_journal_linkage_not_proved'}
        $summary.actionAcceptance='PASS';$summary.databaseIntegrity='PASS';$summary.journalIntegrity='PASS'
        $finalStatus=Read-Cli @('status','--verbose')
        if($finalStatus.data.mode -cne 'Confirmation' -or $finalStatus.data.ReadOnly -ne $false -or -not $finalStatus.data.ConfigurationAvailable -or $finalStatus.data.EnablePushover -or $finalStatus.data.EnableHeartbeat){throw 'final_runtime_mode_readback_failed'}
        $summary['finalStatus']=[pscustomobject]@{mode=$finalStatus.data.mode;readOnly=$finalStatus.data.ReadOnly;degraded=$finalStatus.data.degraded}
        $endingMemory=[double](Get-CimInstance Win32_OperatingSystem -OperationTimeoutSec 5).FreePhysicalMemory/1024
        $summary['endingFreeMemoryMiB']=[math]::Round($endingMemory,1)
        if($endingMemory -le 768){throw 'post_run_memory_not_recovered_above_768_mib'}
        $summary.status='INSTALLED_DEV_ACTION_AND_INTEGRITY_PASS'
    }catch{
        $summary.status='FAIL_OR_INCOMPLETE';$summary.failureStage=$stage
        $summary.failureCode=if($_.Exception.Message -cmatch '^[A-Za-z0-9_]+$'){$_.Exception.Message}else{'database_acceptance_failed'}
    }
}
if($approvedVm){try{$summary.services=@(Get-CimInstance Win32_Service -Filter "Name='HostAgent' OR Name='HostAgent.Helper'" -OperationTimeoutSec 5|Select-Object Name,State,ProcessId)}catch{$summary.status='FAIL_OR_INCOMPLETE';$summary.failureCode='final_product_service_readback_unavailable'}}
if($runRoot){$summary.resultPath=Join-Path $runRoot 'result.json';$summary|ConvertTo-Json -Depth 10|Set-Content -LiteralPath $summary.resultPath -Encoding utf8}
$summary|ConvertTo-Json -Depth 10
if($summary.status -eq 'FAIL_OR_INCOMPLETE'){exit 1}

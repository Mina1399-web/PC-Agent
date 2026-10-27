Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
function New-DevActionPlan([string]$RunId) {
    $id=[guid]::Empty
    if(-not [guid]::TryParseExact($RunId,'N',[ref]$id) -or $id -eq [guid]::Empty -or $RunId -cne $id.ToString('N')){throw 'canonical_nonempty_run_id_required'}
    $root='C:\ProgramData\HostAgent-DEV-Acceptance-'+$RunId
    [pscustomobject]@{RunId=$RunId;ServiceName=('0HostAgentDevAcceptance_'+$RunId);Alias=('devaccept-'+$RunId);Root=$root;App=(Join-Path $root 'app');Data=(Join-Path $root 'data');Image=(Join-Path $root 'app\HostAgent.DevActionTarget.exe')}
}
function Read-DevCliReply([string]$Text) {
    try{
        if($Text.Length -gt 2MB){throw 'limit'}
        $lines=@($Text -split '\r?\n'|Where-Object{$_.TrimStart().StartsWith('{')})
        if($lines.Count -ne 1){throw 'count'}
        $reply=$lines[0]|ConvertFrom-Json -ErrorAction Stop
        if($reply.version -ne 1 -or $reply.ok -isnot [bool] -or $reply.code -isnot [string]){throw 'schema'}
        return $reply
    }catch{throw 'cli_receipt_invalid'}
}
function Select-DevActionIncident($Details,$Plan,[datetimeoffset]$Now) {
    $eligible=@(foreach($detail in @($Details)){
        try{
            $at=[datetimeoffset]$detail.observedAtUtc
            if($detail.incidentId -cnotmatch '^[0-9a-f]{32}$' -or $detail.state -notin @('Detected','Active','Acknowledged','Failed') -or
                [int]$detail.confidence -ne 88 -or $detail.rootCause.code -cne 'service_stopped' -or
                $detail.rootCause.subject -cne $Plan.ServiceName -or $at -gt $Now -or $Now-$at -gt [timespan]::FromMinutes(3) -or
                -not @($detail.evidence|Where-Object{$_.source -ceq 'service.state_change'}).Count){continue}
            $detail
        }catch{continue}
    })
    if($eligible.Count -gt 1){throw 'ambiguous_owned_incident'}
    if($eligible.Count -eq 1){return $eligible[0]}
    return $null
}
function Assert-DevPlan($Plan){
    $expected=New-DevActionPlan $Plan.RunId
    foreach($field in @('ServiceName','Alias','Root','App','Data','Image')){if($Plan.$field -cne $expected.$field){throw 'owned_target_identity_mismatch'}}
}
function Assert-DevTargetIdentity($Plan,$Service,[string]$ExpectedHash,[string]$ActualHash,[switch]$Retiring){
    Assert-DevPlan $Plan
    $allowedModes=if($Retiring){@('Auto','Manual','Disabled')}else{@('Auto','Disabled')}
    if($null -eq $Service -or $Service.Name -cne $Plan.ServiceName -or $Service.PathName -cne ('"'+$Plan.Image+'" run '+$Plan.RunId) -or
        $Service.StartName -ine ('NT SERVICE\'+$Plan.ServiceName) -or $Service.StartMode -notin $allowedModes -or
        $ExpectedHash -cne $ActualHash){throw 'owned_target_identity_mismatch'}
}
function Get-DevScmArguments($Plan,[ValidateSet('create','arm','retire','start','stop','disable')][string]$Operation){
    Assert-DevPlan $Plan
    switch($Operation){
        create{@('create',$Plan.ServiceName,'binPath=',('"'+$Plan.Image+'" run '+$Plan.RunId),'start=','disabled','obj=',('NT SERVICE\'+$Plan.ServiceName),'type=','own','DisplayName=',('HostAgent DEV acceptance '+$Plan.RunId))}
        arm{@('config',$Plan.ServiceName,'start=','auto')}
        retire{@('config',$Plan.ServiceName,'start=','demand')}
        start{@('start',$Plan.ServiceName)}
        stop{@('stop',$Plan.ServiceName)}
        disable{@('config',$Plan.ServiceName,'start=','disabled')}
    }
}
Export-ModuleMember -Function New-DevActionPlan,Read-DevCliReply,Select-DevActionIncident,Assert-DevTargetIdentity,Get-DevScmArguments

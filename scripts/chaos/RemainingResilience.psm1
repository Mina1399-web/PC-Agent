function Invoke-RemainingResiliencePlan {
    param([object[]]$Plan,[string]$RunId,[scriptblock]$Guard,[scriptblock]$ConfirmQuiescent,[scriptblock]$Save)
    $rows=@($Plan | ForEach-Object{[pscustomobject]@{id=$_.id;runId=$RunId;status='NOT RUN';reason='not_reached';scope=$_['scope'];limits=$_['limits'];logDirectory=$_['logDirectory'];manualStep=$_['manualStep'];startedUtc=$null;finishedUtc=$null;details=$null}})
    & $Save $rows | Out-Null
    $unsafe=$false
    for($i=0;$i -lt $Plan.Count;$i++){
        $phase=$Plan[$i];$row=$rows[$i]
        if($unsafe){$row.reason='safety_abort_or_cleanup_unconfirmed';continue}
        if($phase.kind -eq 'Manual'){$row.reason=$phase.reason;& $Save $rows | Out-Null;continue}
        # A failed precondition is never a scenario failure that can be ignored.
        try{& $Guard | Out-Null}catch{$unsafe=$true;$row.reason='safety_precondition_failed';& $Save $rows | Out-Null;continue}
        $row.startedUtc=[DateTimeOffset]::UtcNow.ToString('O');$row.reason='started_unverified'
        Write-Host ('[{0}/{1}] {2}: RUN' -f ($i+1),$Plan.Count,$row.id)
        & $Save $rows | Out-Null
        try{
            $row.details=& $phase.run
            if(-not $row.details -or $row.details.ok -ne $true){throw 'scenario_success_not_proved'}
            $row.status='PASS';$row.reason='verified_within_declared_scope'
        }catch{
            $row.status='FAIL'
            $row.reason=if($_.Exception.Message -match '^[a-z][a-z0-9_]{0,95}$'){$_.Exception.Message}else{'scenario_exception'}
        }
        # Positive postconditions are mandatory even after a failed assertion.
        try{
            $quiescence=& $ConfirmQuiescent $phase
            if($quiescence -and $quiescence.scenarioFailed -eq $true){$row.status='FAIL';$row.reason='scenario_required_additional_cleanup'}
            & $Guard | Out-Null
        }
        catch{$unsafe=$true;$row.status='FAIL';$row.reason='safety_abort_or_cleanup_unconfirmed'}
        $row.finishedUtc=[DateTimeOffset]::UtcNow.ToString('O')
        Write-Host ('{0}: {1} ({2})' -f $row.id,$row.status,$row.reason)
        & $Save $rows | Out-Null
    }
    & $Save $rows | Out-Null
    return $rows
}
function Read-RemainingTrx {
    param([string]$Path,[int]$Minimum)
    if((Get-Item -LiteralPath $Path).Length -gt 4MB){throw 'trx_over_4_mib'}
    $settings=[Xml.XmlReaderSettings]::new();$settings.DtdProcessing=[Xml.DtdProcessing]::Prohibit;$settings.XmlResolver=$null;$settings.MaxCharactersInDocument=4MB
    $reader=[Xml.XmlReader]::Create($Path,$settings)
    try{$doc=[Xml.XmlDocument]::new();$doc.XmlResolver=$null;$doc.Load($reader)}finally{$reader.Dispose()}
    $c=$doc.SelectSingleNode('/*[local-name()="TestRun"]/*[local-name()="ResultSummary"]/*[local-name()="Counters"]')
    if(-not $c){throw 'test_result_counters_missing'}
    $total=[int]$c.total;$executed=[int]$c.executed;$passed=[int]$c.passed;$failed=[int]$c.failed
    return @{ok=($total -ge $Minimum -and $executed -eq $total -and $passed -eq $total -and $failed -eq 0);total=$total;passed=$passed;failed=$failed;executed=$executed;sha256=(Get-FileHash -LiteralPath $Path).Hash;path=$Path}
}
function Test-RemainingAutoScmResult {
    param($Result,[string]$RunId,[string]$Manifest)
    if(-not $Result -or $Result.runId -cne $RunId -or $Result.serviceName -cne ('HostAgentDevScm_'+$RunId) -or
       $Result.scenario -ne 'isolated-scm-automatic-crash-recovery' -or $Result.candidateManifestSha256 -ne $Manifest -or
       $Result.outcome -ne 'PASS' -or $Result.protectedServiceUnchanged -ne $true -or $Result.recoveryDisabled -ne $true -or
       $Result.controllerFallbackStopUsed -ne $false -or $Result.crash.recovery -ne 'scm_failure_action_only' -or
       $Result.firstProcessId -ne $Result.crash.crashedProcessId -or $Result.firstProcessId -le 0 -or
       $Result.crash.recoveredProcessId -le 0 -or $Result.crash.recoveredProcessId -eq $Result.firstProcessId){return $false}
    $ops=@($Result.scmOperations)
    if($ops.Count -ne 2 -or $ops[0].operation -ne 'start' -or $ops[1].operation -ne 'stop' -or @($ops | Where-Object exitCode -ne 0).Count){return $false}
    $checks=$Result.crash.databaseChecks
    if($checks.PSObject.Properties.Name -contains 'value'){$checks=$checks.value}
    return (@($checks).Count -eq 2 -and @($checks | ForEach-Object {$_.database} | Select-Object -Unique).Count -eq 2 -and
       @($checks | Where-Object {$_.database -notin @('host-agent.db','notifications.db') -or $_.quickCheck -ne 'ok' -or $_.committedProofPreserved -ne $true -or $_.uncommittedProofAbsent -ne $true -or $_.acknowledgedSnapshotsPreserved -ne $true}).Count -eq 0)
}
Export-ModuleMember -Function Invoke-RemainingResiliencePlan,Read-RemainingTrx,Test-RemainingAutoScmResult

$ErrorActionPreference='Stop'
$repo=Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
Import-Module (Join-Path $repo 'scripts\chaos\CompleteHelperTargets.psm1') -Force
# Defect: per-target cleanup erased the underlying environment/auth failure.
# A deliberately different operator SID rejects before any CIM, file or native
# service operation, whether this test is run elevated or not.
$targets=@(Get-CompleteHelperTargetPlan @{Path='C:\HostAgent-DEV-nonexistent-fixture';ManifestSha256=('A'*64)} `
    @(1..3|ForEach-Object{[guid]::NewGuid().ToString('N')}) 'S-1-5-21-111-222-333-1001')
$errorResult=$null
try{Confirm-CompleteHelperTargets $targets {}|Out-Null}catch{$errorResult=$_}
if(-not$errorResult -or -not$errorResult.Exception.Data.Contains('TargetResults')){throw 'helper_rejection_evidence_missing'}
$results=@($errorResult.Exception.Data['TargetResults'])
if($results.Count-ne3){throw 'helper_not_all_target_rejections_retained'}
foreach($row in $results){
    if($row.Quiescent-ne$false -or -not$row.Preserved -or -not$row.failure -or
       $row.failure.stage-ne'helper_cleanup' -or $row.failure.source-ne'CompleteHelperTargets.psm1' -or
       $row.failure.line-le0){throw 'helper_cleanup_safe_failure_stage_missing'}
}
if(($results|ConvertTo-Json -Depth 8) -match 'approved_elevated_vm_operator_required'){throw 'raw_helper_error_message_saved'}
'complete_helper_cleanup_diagnostics_passed:3_rejected_targets_no_operations'

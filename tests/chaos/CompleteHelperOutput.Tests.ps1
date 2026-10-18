$ErrorActionPreference='Stop'
$repo=Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$modulePath=Join-Path $repo 'scripts/chaos/CompleteHelperTargets.psm1'
$tokens=$null;$errors=$null
$ast=[Management.Automation.Language.Parser]::ParseFile($modulePath,[ref]$tokens,[ref]$errors)
if(@($errors).Count){throw 'helper_module_does_not_parse'}
$function=$ast.Find({param($node)$node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'New-CompleteHelperTargets'},$true)
if(-not$function){throw 'helper_target_factory_missing'}
$calls=@($function.FindAll({param($node)
    $node -is [Management.Automation.Language.CommandAst] -and $node.GetCommandName() -eq 'Assert-CompleteHelperPlainPath'
},$true))
if($calls.Count -lt 2){throw 'expected_all_files_and_roots_to_be_validated'}
foreach($call in $calls){
    $pipeline=$call.Parent
    if($pipeline -isnot [Management.Automation.Language.PipelineAst]){$pipeline=$pipeline.Parent}
    $commands=@($pipeline.PipelineElements|Where-Object {$_ -is [Management.Automation.Language.CommandAst]}|ForEach-Object {$_.GetCommandName()})
    if($commands -notcontains 'Out-Null'){
        throw ('helper_validation_output_leaks_into_target_collection:'+ $call.Extent.StartLineNumber)
    }
}
$aclCalls=@($function.FindAll({param($node)
    $node -is [Management.Automation.Language.CommandAst] -and $node.GetCommandName() -in @('Set-Acl','Set-CompleteHelperDirectoryAcl')
},$true))
if($aclCalls.Count -lt 2){throw 'expected_package_acl_operations_to_be_checked'}
foreach($call in $aclCalls){
    $pipeline=$call.Parent
    if($pipeline -isnot [Management.Automation.Language.PipelineAst]){$pipeline=$pipeline.Parent}
    $commands=@($pipeline.PipelineElements|Where-Object {$_ -is [Management.Automation.Language.CommandAst]}|ForEach-Object {$_.GetCommandName()})
    if($commands -notcontains 'Out-Null'){
        throw ('helper_acl_output_leaks_into_target_collection:'+ $call.Extent.StartLineNumber)
    }
}
[pscustomobject]@{suite='complete-helper-output';passed=($calls.Count+$aclCalls.Count);failed=0;serviceOperations=0;filesystemMutations=0}|ConvertTo-Json -Compress

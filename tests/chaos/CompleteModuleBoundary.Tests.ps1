$ErrorActionPreference='Stop'
$repo=Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$runner=Join-Path $repo 'scripts/chaos/run-complete-dev-resilience.ps1'
$tokens=$null;$parseErrors=$null
$runnerAst=[Management.Automation.Language.Parser]::ParseFile($runner,[ref]$tokens,[ref]$parseErrors)
if(@($parseErrors).Count){throw 'runner_parse_failed'}
$calls=@($runnerAst.FindAll({param($node)$node -is [Management.Automation.Language.CommandAst]},$true)|ForEach-Object {$_.GetCommandName()}|Where-Object {$_}|Select-Object -Unique)
$missing=[Collections.Generic.List[string]]::new()
$checked=[Collections.Generic.List[string]]::new()
foreach($name in @('IsolatedScmSafety.psm1','CompleteDiagnostics.psm1','CompleteCampaignPlan.psm1','CompleteResilience.psm1','CompleteHelperTargets.psm1','CompletePriorHelperCleanup.psm1')){
    $path=Join-Path $repo ('scripts/chaos/'+$name)
    $moduleAst=[Management.Automation.Language.Parser]::ParseFile($path,[ref]$tokens,[ref]$parseErrors)
    if(@($parseErrors).Count){throw ('module_parse_failed:'+ $name)}
    $module=Import-Module $path -Force -PassThru -WarningAction SilentlyContinue
    $definitions=@($moduleAst.FindAll({param($node)$node -is [Management.Automation.Language.FunctionDefinitionAst]},$true)|ForEach-Object {$_.Name})
    foreach($call in $calls){
        if($definitions -contains $call){
            $checked.Add($call)
            if(-not$module.ExportedCommands.ContainsKey($call)){$missing.Add($call)}
        }
    }
}
if($missing.Count){throw ('runner_module_commands_not_exported:'+ (($missing|Sort-Object -Unique)-join ','))}
[pscustomobject]@{suite='complete-module-boundary';passed=$checked.Count;failed=0;serviceOperations=0;checks=$checked.ToArray()}|ConvertTo-Json -Depth 4

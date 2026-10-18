$ErrorActionPreference='Stop'
$repo=Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$tokens=$null;$errors=$null
$ast=[Management.Automation.Language.Parser]::ParseFile((Join-Path $repo 'scripts/chaos/run-complete-dev-resilience.ps1'),[ref]$tokens,[ref]$errors)
if(@($errors).Count){throw 'runner_does_not_parse'}
if(@($ast.ParamBlock.Parameters|Where-Object {$_.Name.VariablePath.UserPath-eq'FocusHelperFailure'}).Count-ne1){throw 'helper_only_focus_not_supported'}
$filter=$ast.EndBlock.Statements|Where-Object {$_.Extent.Text.StartsWith('if($FocusHelperFailure){$plan=')}
if(@($filter).Count-ne1){throw 'helper_only_selection_missing'}
foreach($enabled in @($false,$true)){
 $FocusHelperFailure=$enabled
 $plan=@('S1-automatic-scm-recovery','S2-collector-failure-recovery','S2-real-helper-remediation','S3-real-WPF-private-IPC'|ForEach-Object {@{id=$_}})
 . ([scriptblock]::Create($filter.Extent.Text))
 if($enabled-and($plan.Count-ne1-or$plan[0].id-ne'S2-real-helper-remediation')){throw 'helper_only_filter_selected_other_phase'}
 if(-not$enabled-and$plan.Count-ne4){throw 'default_phase_plan_changed'}
}
'complete_helper_only_focus_passed:2;service_operations=0'

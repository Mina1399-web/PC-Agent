$ErrorActionPreference='Stop'
$repo=Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
Import-Module (Join-Path $repo 'scripts/chaos/CompleteHelperTargets.psm1') -Force
$ids=@('a91dfc8957ae429e9b0e9c5cadf9bb92','d1defc8957ae429e9b0e9c5cadf9bb92','e91dfc8957ae429e9b0e9c5cadf9bb92')
$sid='S-1-5-21-111-222-333-1001'
$candidate=[pscustomobject]@{Path='C:\candidate-fixture';ManifestSha256=('A'*64)}
$passed=[Collections.Generic.List[string]]::new()
function Expect-True([string]$Name,[bool]$Condition){if(-not$Condition){throw ('assertion_failed_'+$Name)};$passed.Add($Name)}
function Expect-Rejection([string]$Name,[scriptblock]$Action){
    $rejected=$false;try{& $Action|Out-Null}catch{$rejected=$true}
    if(-not$rejected){throw ('assertion_failed_'+$Name)};$passed.Add($Name)
}
Expect-True 'runner_manifest_assertion_is_exported' ([bool](Get-Command Assert-CompleteHelperManifest -ErrorAction SilentlyContinue))
$plan=@(Get-CompleteHelperTargetPlan -Candidate $candidate -RunIds $ids -OperatorSid $sid)
Expect-True 'exact_guid_plan' ($plan.Count-eq3-and$plan[0].Name-ceq'HostAgentDevScm_a91dfc8957ae429e9b0e9c5cadf9bb92')
Expect-True 'exact_fresh_guid_paths' ($plan[0].Root-ceq'C:\ProgramData\HostAgent-DEV-SCM-a91dfc8957ae429e9b0e9c5cadf9bb92'-and
    $plan[0].Image-ceq'C:\ProgramData\HostAgent-DEV-SCM-a91dfc8957ae429e9b0e9c5cadf9bb92\app\HostAgent.PipeProbe.exe')
Expect-True 'fixed_initial_states_and_b_only_delay' ($plan[0].InitialState-eq'Stopped'-and$plan[1].InitialState-eq'Running'-and$plan[2].InitialState-eq'Running'-and
    -not$plan[0].SlowStop-and$plan[1].SlowStop-and-not$plan[2].SlowStop)
$create=@(Get-CompleteHelperNativeArguments $plan[0] create)
Expect-True 'native_create_has_twelve_distinct_arguments' ($create.Count-eq12-and$create[0]-ceq'create'-and$create[1]-ceq$plan[0].Name-and$create[2]-ceq'binPath='-and
    $create[3]-ceq('"C:\ProgramData\HostAgent-DEV-SCM-a91dfc8957ae429e9b0e9c5cadf9bb92\app\HostAgent.PipeProbe.exe" isolated-complete-target a91dfc8957ae429e9b0e9c5cadf9bb92 '+$sid)-and
    $create[4]-ceq'start='-and$create[5]-ceq'demand'-and$create[6]-ceq'obj='-and$create[7]-ceq'NT SERVICE\HostAgentDevScm_a91dfc8957ae429e9b0e9c5cadf9bb92'-and$create[9]-ceq'own')
$disable=@(Get-CompleteHelperNativeArguments $plan[1] disable)
Expect-True 'only_guid_service_can_be_disabled' ($disable.Count-eq4-and$disable[0]-ceq'config'-and$disable[1]-ceq$plan[1].Name-and$disable[2]-ceq'start='-and$disable[3]-ceq'disabled')
foreach($verb in @('start','stop')){
    $arguments=@(Get-CompleteHelperNativeArguments $plan[2] $verb)
    Expect-True ('closed_'+$verb+'_arguments') ($arguments.Count-eq2-and$arguments[0]-ceq$verb-and$arguments[1]-ceq$plan[2].Name)
}
Expect-Rejection 'uppercase_guid_rejected' {Get-CompleteHelperTargetPlan $candidate @($ids[0].ToUpperInvariant(),$ids[1],$ids[2]) $sid}
Expect-Rejection 'empty_guid_rejected' {Get-CompleteHelperTargetPlan $candidate @(('0'*32),$ids[1],$ids[2]) $sid}
Expect-Rejection 'duplicate_guid_rejected' {Get-CompleteHelperTargetPlan $candidate @($ids[0],$ids[0],$ids[2]) $sid}
Expect-Rejection 'protected_service_name_not_a_guid' {Get-CompleteHelperTargetPlan $candidate @('HostAgent',$ids[1],$ids[2]) $sid}
Expect-Rejection 'system_identity_not_operator' {Get-CompleteHelperTargetPlan $candidate $ids 'S-1-5-18'}
Expect-Rejection 'unc_candidate_rejected' {Get-CompleteHelperTargetPlan ([pscustomobject]@{Path='\\server\candidate';ManifestSha256=('A'*64)}) $ids $sid}
Expect-Rejection 'noncanonical_candidate_rejected' {Get-CompleteHelperTargetPlan ([pscustomobject]@{Path='C:\candidate-fixture\..\other';ManifestSha256=('A'*64)}) $ids $sid}
Expect-Rejection 'unbound_manifest_rejected' {Get-CompleteHelperTargetPlan ([pscustomobject]@{Path='C:\candidate-fixture';ManifestSha256='invalid'}) $ids $sid}
$forged=$plan[0].PSObject.Copy();$forged.Name='HostAgent'
Expect-Rejection 'protected_name_forgery_rejected_before_native_launch' {Get-CompleteHelperNativeArguments $forged stop}
$forged=$plan[0].PSObject.Copy();$forged.App='C:\Program Files\HostAgent\Service'
Expect-Rejection 'outside_package_path_forgery_rejected' {Get-CompleteHelperNativeArguments $forged disable}
Expect-Rejection 'unsupported_delete_operation_rejected' {Get-CompleteHelperNativeArguments $plan[0] delete}
$special=@(Get-CompleteHelperTargetPlan $candidate @('11111111111111111111111111111111',$ids[1],$ids[2]) $sid)
Assert-CompleteHelperTargetMetadata $special[0]
$passed.Add('all_nonempty_canonical_guids_supported')
$tokens=$null;$errors=$null
$ast=[Management.Automation.Language.Parser]::ParseFile((Join-Path $repo 'scripts/chaos/CompleteHelperTargets.psm1'),[ref]$tokens,[ref]$errors)
Expect-True 'module_parses_without_errors' (@($errors).Count-eq0)
$forbidden=@($ast.FindAll({param($node)$node-is[Management.Automation.Language.CommandAst]-and
    $node.GetCommandName()-in@('Remove-Item','Remove-Service','Stop-Service','Restart-Service','Set-Service','Invoke-Expression','Set-ExecutionPolicy')},$true))
Expect-True 'no_broad_service_delete_or_policy_cmdlet_in_module_ast' ($forbidden.Count-eq0)
[pscustomobject]@{scenario='complete_helper_target_readonly_preparation';passed=$passed.Count;failed=0
    nativeServiceOperationsRun=$false;aclOperationsRun=$false;checks=$passed.ToArray()}|ConvertTo-Json -Depth 4

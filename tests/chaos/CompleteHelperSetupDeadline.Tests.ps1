$ErrorActionPreference='Stop'
$repo=Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
Import-Module (Join-Path $repo 'scripts/chaos/CompleteDiagnostics.psm1') -Force
$tokens=$null;$errors=$null
$ast=[Management.Automation.Language.Parser]::ParseFile((Join-Path $repo 'scripts/chaos/run-complete-dev-resilience.ps1'),[ref]$tokens,[ref]$errors)
$wrapper=$ast.Find({param($node)$node-is[Management.Automation.Language.FunctionDefinitionAst]-and$node.Name-eq'Invoke-CompleteHelper'},$true)
. ([scriptblock]::Create($wrapper.Extent.Text))
$script:completeTargets=@()
$script:campaignCandidate='C:\candidate-fixture';$script:campaignCandidateHash='A'*64
$logRoot='C:\unused-test-logs';$helperIds=@();$identity=[Security.Principal.WindowsIdentity]::GetCurrent()
function Assert-CompleteFast {}
function New-CompleteHelperTargets {
 param($Candidate,$RunIds,$OperatorSid,$Guard)
 $script:completeHelperSetupClock=[pscustomobject]@{Elapsed=[TimeSpan]::FromSeconds(181)}
 & $Guard
 throw 'unbounded_setup_accepted'
}
$caught=$null;try{Invoke-CompleteHelper}catch{$caught=$_}
if(-not$caught-or$caught.Exception.Message-ne'helper_setup_deadline'){throw 'helper_setup_deadline_not_enforced'}
'complete_helper_setup_deadline_passed:1;service_operations=0;filesystem_mutations=0'

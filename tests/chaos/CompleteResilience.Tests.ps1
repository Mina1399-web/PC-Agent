$ErrorActionPreference='Stop'
Import-Module (Join-Path $PSScriptRoot '..\..\scripts\chaos\CompleteResilience.psm1') -Force
foreach($case in @(@(512,'hard_abort'),@(511.9,'hard_abort'),@(767.9,'soft_abort'),@(768,'continue'),@(1024,'continue'),@([double]::NaN,'hard_abort'))){
 if((Get-CompleteMemoryDecision $case[0]) -ne $case[1]){throw ('memory_boundary_regression_'+$case[0])}
}
'complete_memory_boundaries_passed'

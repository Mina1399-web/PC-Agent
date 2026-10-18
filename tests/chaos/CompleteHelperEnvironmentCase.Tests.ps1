$ErrorActionPreference='Stop'
$repo=Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$tokens=$null;$parseErrors=$null
$moduleAst=[Management.Automation.Language.Parser]::ParseFile((Join-Path $repo 'scripts/chaos/CompleteHelperTargets.psm1'),[ref]$tokens,[ref]$parseErrors)
if(@($parseErrors).Count){throw 'helper_module_does_not_parse'}
$environmentFunction=$moduleAst.Find({param($node)$node-is[Management.Automation.Language.FunctionDefinitionAst]-and$node.Name-eq'Assert-CompleteHelperEnvironment'},$true)
if(-not$environmentFunction){throw 'actual_environment_guard_missing'}
$guardIf=$environmentFunction.Body.Find({param($node)$node-is[Management.Automation.Language.IfStatementAst]-and$node.Clauses[0].Item1.Extent.Text.Contains('$env:WINDIR')},$true)
if(-not$guardIf){throw 'actual_environment_condition_missing'}
$script:helperCaseGuardCondition=$guardIf.Clauses[0].Item1.Extent.Text
# Replace only the external administrator-token query in this copied predicate.
# No production function is invoked or changed and no privileged path executes.
$administratorBoundary='-not[Security.Principal.WindowsPrincipal]::new($identity).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)'
if(-not$script:helperCaseGuardCondition.Contains($administratorBoundary)){throw 'administrator_boundary_changed_test_needs_review'}
$script:helperCaseGuardCondition=$script:helperCaseGuardCondition.Replace($administratorBoundary,'-not$true')

function Test-ActualHelperPathRejection([string]$WindowsRoot,[string]$DataRoot){
    $previousWindows=$env:WINDIR;$previousData=$env:ProgramData
    $identity=[Security.Principal.WindowsIdentity]::GetCurrent()
    try{
        $OperatorSid=$identity.User.Value
        $env:WINDIR=$WindowsRoot;$env:ProgramData=$DataRoot
        $predicate=[scriptblock]::Create($script:helperCaseGuardCondition)
        return [bool](& $predicate)
    }finally{
        $identity.Dispose();$env:WINDIR=$previousWindows;$env:ProgramData=$previousData
    }
}

Describe 'Actual helper environment predicate, read-only; no administrator operation' {
    It 'accepts standard uppercase Windows and ProgramData path spelling' {
        (Test-ActualHelperPathRejection 'C:\WINDOWS' 'C:\PROGRAMDATA') | Should Be $false
    }
    It 'accepts standard lowercase Windows and ProgramData path spelling' {
        (Test-ActualHelperPathRejection 'c:\windows' 'c:\programdata') | Should Be $false
    }
    It 'accepts the existing mixed-case spelling with actual VM account and SID' {
        (Test-ActualHelperPathRejection 'C:\Windows' 'C:\ProgramData') | Should Be $false
    }
    It 'rejects a different Windows drive' {
        (Test-ActualHelperPathRejection 'D:\Windows' 'C:\ProgramData') | Should Be $true
    }
    It 'rejects a different ProgramData drive' {
        (Test-ActualHelperPathRejection 'C:\Windows' 'D:\ProgramData') | Should Be $true
    }
    It 'rejects UNC system and data roots' {
        (Test-ActualHelperPathRejection '\\server\Windows' 'C:\ProgramData') | Should Be $true
        (Test-ActualHelperPathRejection 'C:\Windows' '\\server\ProgramData') | Should Be $true
    }
    It 'rejects parent traversal even if it normalizes to an allowed directory' {
        (Test-ActualHelperPathRejection 'C:\Windows\..\Windows' 'C:\ProgramData') | Should Be $true
        (Test-ActualHelperPathRejection 'C:\Windows' 'C:\ProgramData\..\ProgramData') | Should Be $true
    }
    It 'rejects other roots on the same drive' {
        (Test-ActualHelperPathRejection 'C:\Windows2' 'C:\ProgramData') | Should Be $true
        (Test-ActualHelperPathRejection 'C:\Windows' 'C:\OtherProgramData') | Should Be $true
    }
}

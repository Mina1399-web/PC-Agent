$ErrorActionPreference='Stop'
$repo=Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
# Reproduce the documented native contract at the OpenService boundary. Never
# create/configure/start a real service; Set and IsDisabled remain real code.
$source=Get-Content (Join-Path $repo 'scripts/chaos/IsolatedScmRecovery.cs') -Raw
$source=$source.Replace('public static class IsolatedScmRecovery','public static class RecoveryNativeContract')
$source=[regex]::Replace($source,'\[DllImport\("advapi32.dll",CharSet=CharSet.Unicode,SetLastError=true\)\] static extern IntPtr OpenSCManager\([^;]+;', 'static IntPtr OpenSCManager(string machine,string database,uint access) { return new IntPtr(1); }')
$source=[regex]::Replace($source,'\[DllImport\("advapi32.dll",CharSet=CharSet.Unicode,SetLastError=true\)\] static extern IntPtr OpenService\([^;]+;', 'public static uint ObservedAccess; public static string ObservedName; static IntPtr OpenService(IntPtr manager,string name,uint access) { ObservedAccess=access; ObservedName=name; throw new InvalidOperationException("native_boundary_observed"); }')
Add-Type -TypeDefinition $source
$run='9aa86a18379245f2940c3224ae6c4967'
foreach($case in @(@{mode='restart';access=19},@{mode='disable';access=3},@{mode='read';access=1})) {
    try {
        if($case.mode -eq 'read'){[RecoveryNativeContract]::IsDisabled($run)|Out-Null}
        else{[RecoveryNativeContract]::Set($run,($case.mode -eq 'restart'))}
        throw 'test_did_not_reach_native_boundary'
    }catch{if($_.Exception.ToString() -notmatch 'native_boundary_observed'){throw}}
    if([RecoveryNativeContract]::ObservedName -cne ('HostAgentDevScm_'+$run)){throw 'protected_service_scope_regression'}
    if([RecoveryNativeContract]::ObservedAccess -ne $case.access){throw ('recovery_access_contract_failed:'+ $case.mode+':expected_'+$case.access+':actual_'+[RecoveryNativeContract]::ObservedAccess)}
}
'scm_recovery_native_contract_passed:3;service_operations=0'

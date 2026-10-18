$ErrorActionPreference='Stop'
$repo=Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$source=Get-Content -LiteralPath (Join-Path $repo 'scripts/chaos/CompleteProtectedServiceGuard.cs') -Raw
# Exercise the production status/error/handle-lifetime code. Only the native
# SCM boundaries are replaced; no real service is opened or controlled.
$stubState=@'
    public static string FailAt, LastName;
    public static uint ManagerAccess, ServiceAccess, NativeState=4, NativePid=1234;
    public static int CloseCount;
    public static void Reset() { FailAt=null;LastName=null;ManagerAccess=0;ServiceAccess=0;NativeState=4;NativePid=1234;CloseCount=0; }
'@
$source=$source.Replace('public static class CompleteProtectedServiceGuard {',('public static class CompleteProtectedServiceGuard {'+[Environment]::NewLine+$stubState))
$nativeStubs=@{
 OpenSCManager='static IntPtr OpenSCManager(string machine,string database,uint access) { ManagerAccess=access;return FailAt=="open_scm"?IntPtr.Zero:new IntPtr(11); }'
 OpenService='static IntPtr OpenService(IntPtr manager,string name,uint access) { LastName=name;ServiceAccess=access;return FailAt=="open_service"?IntPtr.Zero:new IntPtr(12); }'
 QueryServiceStatusEx='static bool QueryServiceStatusEx(IntPtr service,int level,out Status status,int size,out int needed) { status=new Status();status.State=NativeState;status.ProcessId=NativePid;needed=0;return FailAt!="query_status"; }'
 CloseServiceHandle='static bool CloseServiceHandle(IntPtr handle) { CloseCount++;return true; }'
}
foreach($name in $nativeStubs.Keys){
 $pattern='\[DllImport\([^\]]+\)\]\s*static extern [^;]+\b'+$name+'\([^;]+;'
 $matches=[regex]::Matches($source,$pattern)
 if($matches.Count-ne1){throw ('native_boundary_not_found:'+ $name)}
 $source=[regex]::Replace($source,$pattern,[string]$nativeStubs[$name])
}
Add-Type -TypeDefinition $source
function Assert-Native([bool]$Condition,[string]$Code){if(-not$Condition){throw ('protected_helper_native_assertion_failed:'+ $Code)}}
# Defect: helper monitoring used CIM rather than the fixed read-only native
# status path, leaving the focused campaign vulnerable to provider failures.
Assert-Native ($null-ne[CompleteProtectedServiceGuard].GetMethod('ReadHelper')) 'fixed_helper_read_missing'
Assert-Native (@([CompleteProtectedServiceGuard].GetMethods()|Where-Object {$_.DeclaringType-eq[CompleteProtectedServiceGuard]-and$_.IsPublic-and$_.GetParameters().Count-gt0-and$_.Name-ne'Reset'}).Count-eq0) 'arbitrary_name_api_exposed'
$cases=0
foreach($method in @('Read','ReadHelper')){
 $expectedName=if($method-eq'Read'){'HostAgent'}else{'HostAgent.Helper'}
 $prefix=if($method-eq'Read'){'protected_service_'}else{'protected_helper_'}
 [CompleteProtectedServiceGuard]::Reset()
 $snapshot=[CompleteProtectedServiceGuard].GetMethod($method).Invoke($null,@())
 Assert-Native ($snapshot.State-eq'Running'-and$snapshot.ProcessId-eq1234) 'running_status_or_pid_changed'
 Assert-Native ([CompleteProtectedServiceGuard]::LastName-ceq$expectedName) 'wrong_fixed_service'
 Assert-Native ([CompleteProtectedServiceGuard]::ManagerAccess-eq1-and[CompleteProtectedServiceGuard]::ServiceAccess-eq4) 'excessive_native_access'
 Assert-Native ([CompleteProtectedServiceGuard]::CloseCount-eq2) 'success_handle_leak'
 $cases++
 [CompleteProtectedServiceGuard]::Reset();[CompleteProtectedServiceGuard]::NativeState=1;[CompleteProtectedServiceGuard]::NativePid=0
 $snapshot=[CompleteProtectedServiceGuard].GetMethod($method).Invoke($null,@())
 Assert-Native ($snapshot.State-eq'NotRunning'-and$snapshot.ProcessId-eq0) 'stopped_status_hidden'
 $cases++
 foreach($stage in @('open_scm','open_service','query_status')){
  [CompleteProtectedServiceGuard]::Reset();[CompleteProtectedServiceGuard]::FailAt=$stage
  $failure=$null
  try{[void][CompleteProtectedServiceGuard].GetMethod($method).Invoke($null,@())}catch{
   $failure=$_.Exception
   while($failure.InnerException){$failure=$failure.InnerException}
  }
  Assert-Native ($failure-is[ComponentModel.Win32Exception]) 'native_failure_not_fail_closed'
  Assert-Native ($failure.Data['IsolatedScmNativeStage']-ceq($prefix+$stage)) 'native_failure_stage_missing'
  $closes=if($stage-eq'open_scm'){0}elseif($stage-eq'open_service'){1}else{2}
  Assert-Native ([CompleteProtectedServiceGuard]::CloseCount-eq$closes) 'failure_handle_leak'
  $cases++
 }
}
'complete_protected_helper_native_passed:'+ $cases+';service_operations=0;native_boundaries=stubbed'

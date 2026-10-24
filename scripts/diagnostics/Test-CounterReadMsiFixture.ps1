<# Operator-only GUID MSI fixture. Default preflight is read-only. Execute
requires a separately reviewed JSON manifest SHA256; no self-observed hash is
accepted as authorization. Never starts a service or changes SID558/HostAgent.
MSI cancellation is cooperative; unconfirmed quiescence stops the matrix and
preserves all state. Only the owned client may be killed at the hard deadline.
#>
[CmdletBinding()]
param([switch]$Execute,[string]$ManifestPath,[string]$ExpectedManifestSha256)
$ErrorActionPreference='Stop';Set-StrictMode -Version Latest
$script:FixtureMonitor=$null
$script:FixtureInitialFreeMiB=$null
$script:FixtureCurrentPhase=$null
$script:FixtureStage='preflight'
$locks=@();$scope=$null;$before=$null;$steps=@();$failure=$null;$quiescent=$true;$createdGroup=$false;$createdService=$false;$createdRoot=$false;$build=$null;$seeded=$false;$msiAttempted=$false
$failureStage=$null;$failureExceptionType=$null;$failureHResult=$null
function Get-CounterFixtureFailureDetails([System.Management.Automation.ErrorRecord]$ErrorRecord,[string]$Stage,[string[]]$Known) {
    # Exception text/stacks may expose user data, commands, and paths. Keep the
    # receipt actionable with only a fixed code, explicit stage, CLR type, and
    # HRESULT; never persist the raw exception message or script stack.
    $exception=$ErrorRecord.Exception.GetBaseException()
    $message=$ErrorRecord.Exception.Message
    if($message -in $Known){$code=$message}else{
        $code=switch($exception.GetType().FullName){
            'System.UnauthorizedAccessException' {'fixture_access_denied'}
            'System.Security.SecurityException' {'fixture_access_denied'}
            'System.IO.IOException' {'fixture_io_failure'}
            'System.Management.Automation.CommandNotFoundException' {'fixture_command_unavailable'}
            'System.Management.Automation.MethodInvocationException' {'fixture_invocation_failure'}
            'System.Runtime.InteropServices.COMException' {'fixture_com_failure'}
            default {'fixture_unclassified_exception'}
        }
    }
    [pscustomobject]@{failureCode=$code;failureStage=$Stage;exceptionType=$exception.GetType().FullName;hResult=[int]$exception.HResult}
}
function Get-ProtectedFixtureIdentity($Manifest) {
    $services=@(Get-CimInstance Win32_Service -Filter "Name='HostAgent' OR Name='HostAgent.Helper'"|Sort-Object Name)
    if($services.Count-ne2){throw 'protected_identity_mismatch'}
    foreach($service in $services){$helper=$service.Name-ceq'HostAgent.Helper';$account=if($helper){'LocalSystem'}else{'NT SERVICE\HostAgent'};$image=if($helper){'"C:\Program Files\HostAgent\Helper\HostAgent.Helper.exe"'}else{'"C:\Program Files\HostAgent\Service\HostAgent.Service.exe"'}
        if($service.State-ne'Running'-or$service.StartMode-ne'Auto'-or$service.ProcessId-le0-or$service.StartName-ine$account-or$service.PathName-cne$image){throw 'protected_identity_mismatch'}}
    $relative=@('Service/HostAgent.Service.exe','Service/HostAgent.Service.dll','Service/HostAgent.Core.dll','Service/HostAgent.Infrastructure.dll','Helper/HostAgent.Helper.exe','Helper/HostAgent.Helper.dll')
    $hashes=@(foreach($name in $relative){$path=Join-Path 'C:\Program Files\HostAgent' $name;Assert-CounterFixturePlainPath $path;$hash=(Get-FileHash -LiteralPath $path).Hash;$length=(Get-Item -LiteralPath $path).Length
        if($Manifest){$entry=@($Manifest.InstalledFiles|Where-Object {$_.RelativePath-ceq$name});if($entry.Count-ne1-or$entry[0].Sha256-cne$hash-or$entry[0].Length-ne$length){throw 'protected_installed_pin_mismatch'}}
        [pscustomobject]@{relative=$name;sha256=$hash;length=$length}})
    if($Manifest-and@($Manifest.InstalledFiles).Count-ne6){throw 'protected_installed_pin_mismatch'}
    $hostSid='S-1-5-80-2080860389-2647237053-3465382480-4241646294-1393942697'
    $counterMembership=if($Execute){@((Get-LocalGroupMember -SID 'S-1-5-32-558')|Where-Object {$_.SID.Value-eq$hostSid}).Count-eq1}else{$null}
    [pscustomobject]@{services=@($services|Select-Object Name,State,StartMode,StartName,PathName,ProcessId);hashes=$hashes;hostAgentCounterMembership=$counterMembership}
}
function New-FixtureProtectedDirectory([string]$Path) {
    Assert-CounterFixturePlainPath $Path
    if(Test-Path -LiteralPath $Path){throw 'fixture_output_exists_preserve'}
    $acl=[Security.AccessControl.DirectorySecurity]::new();$owner=[Security.Principal.SecurityIdentifier]::new('S-1-5-32-544');$acl.SetOwner($owner);$acl.SetAccessRuleProtection($true,$false)
    foreach($sid in @('S-1-5-18','S-1-5-32-544')){$acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new([Security.Principal.SecurityIdentifier]::new($sid),'FullControl','ContainerInherit,ObjectInherit','None','Allow'))}
    [IO.FileSystemAclExtensions]::CreateDirectory($acl,$Path)|Out-Null
}
function Assert-FixtureStillSafe {
    if($script:FixtureMonitor.FailureCode){throw 'fixture_resource_abort'}
    $current=Get-ProtectedFixtureIdentity $manifest
    if(($current|ConvertTo-Json -Depth 6 -Compress)-cne($before|ConvertTo-Json -Depth 6 -Compress)){throw 'protected_identity_changed'}
    if((Get-LocalGroup -Name $scope.Name).SID.Value-cne$build.GroupSid){throw 'fixture_group_identity_changed'}
    $svc=Get-CimInstance Win32_Service -Filter ("Name='"+$scope.Name+"'")
    if($svc -and ($svc.State-ne'Stopped'-or$svc.StartMode-ne'Manual'-or$svc.StartName-ine('NT SERVICE\'+$scope.Name)-or$svc.PathName-cne('"'+(Join-Path $scope.ProgramFiles 'Inert.exe')+'"'))){throw 'fixture_service_identity_or_state_changed'}
}
function Ensure-FixtureRegistration {
    $script:FixtureStage='private_service_registration'
    $existing=Get-CimInstance Win32_Service -Filter ("Name='"+$scope.Name+"'")
    if(-not$existing){
        Invoke-CounterFixtureTool (Join-Path $env:WINDIR 'System32\sc.exe') @('create',$scope.Name,'binPath=',('"'+(Join-Path $scope.ProgramFiles 'Inert.exe')+'"'),'type=','own','start=','demand','obj=',('NT SERVICE\'+$scope.Name)) (Join-Path $scope.Root ('service-create-'+[Guid]::NewGuid().ToString('N')))
        $script:createdService=$true
    }
    Assert-FixtureStillSafe
}
function Invoke-FixtureClient([string[]]$Arguments,[string]$Name,[switch]$Msi) {
    if($Msi){$script:FixtureStage='msi_phase/'+$script:FixtureCurrentPhase}elseif(-not$script:FixtureStage){$script:FixtureStage='fixture_client_operation'}
    Assert-FixtureStillSafe
    if((Get-FileHash -LiteralPath $build.Controller).Hash-cne$build.ControllerSha256-or(Get-FileHash -LiteralPath $build.Msi).Hash-cne$build.MsiSha256){throw 'fixture_artifact_pin_changed'}
    $info=[Diagnostics.ProcessStartInfo]::new();$info.FileName=$build.Controller;$info.UseShellExecute=$false;$info.CreateNoWindow=$true;$info.RedirectStandardOutput=$true;$info.RedirectStandardError=$true
    foreach($argument in $Arguments){$info.ArgumentList.Add($argument)}
    if($Msi){$script:quiescent=$false;$script:msiAttempted=$true}
    $child=[Diagnostics.Process]::Start($info);$stdout=$child.StandardOutput.ReadToEndAsync();$stderr=$child.StandardError.ReadToEndAsync();$watch=[Diagnostics.Stopwatch]::StartNew()
    try{
        while(-not$child.WaitForExit(100)){
            if($script:FixtureMonitor.FailureCode){[IO.File]::WriteAllText((Join-Path $scope.Root 'cancel.flag'),'cancel')}
            if($watch.Elapsed.TotalSeconds-ge120){
                # Kill ONLY this ordinary elevated client. Never Kill(true),
                # msiexec, a service, or the Windows Installer server.
                $child.Kill();$null=$child.WaitForExit(5000);throw 'fixture_client_hard_deadline_quiescence_unconfirmed'
            }
        }
        $text=$stdout.GetAwaiter().GetResult();$errorText=$stderr.GetAwaiter().GetResult()
        if($text.Length-gt65536-or$errorText.Length-gt65536){throw 'fixture_client_output_limit'}
        [IO.File]::WriteAllText((Join-Path $scope.Root ($Name+'.stdout.json')),$text)
        [IO.File]::WriteAllText((Join-Path $scope.Root ($Name+'.stderr.txt')),$errorText)
        $receipt=$text|ConvertFrom-Json
        if($Msi-and$receipt.PSObject.Properties['quiescent']){$script:quiescent=$receipt.quiescent-eq$true}
        if($child.ExitCode-ne0-or$errorText.Length-ne0){throw 'fixture_client_failed_preserve'}
        if($Msi-and(-not$script:quiescent-or$receipt.failure)){throw 'fixture_msi_not_quiescent_or_resource_abort'}
        $receipt
    }finally{$child.Dispose()}
}
function Assert-FixtureProof($Proof,[bool]$Member,[bool]$Owned,[string]$Journal) {
    if($Proof.member-isnot[bool]-or$Proof.owned-isnot[bool]-or$Proof.member-ne$Member-or$Proof.owned-ne$Owned-or$Proof.journal-cne$Journal){throw 'fixture_proof_mismatch'}
    if($Journal-eq'committed'-and($Proof.journalAfterMember-ne$Member-or$Proof.journalAfterOwned-ne$Owned)){throw 'fixture_proof_mismatch'}
}
function Assert-FixtureSeedRemoval($Before,$After) {
    # A manual fixture seed is outside the MSI transaction. Its removal must
    # preserve, rather than rewrite or reinterpret, the completed journal.
    Assert-FixtureProof $Before $true $false 'committed'
    if($After.member-isnot[bool]-or$After.owned-isnot[bool]-or$After.member-or$After.owned-or$After.journal-cne'committed'){throw 'fixture_seed_removal_mismatch'}
    foreach($field in @('journalBeforeMember','journalBeforeOwned','journalAfterMember','journalAfterOwned')){
        if($After.$field-isnot[bool]-or$Before.$field-isnot[bool]-or$After.$field-ne$Before.$field){throw 'fixture_seed_journal_changed'}
    }
}
function Assert-FixtureFaultReceipt($Receipt,[int]$Expected,[string]$FailPhase) {
    foreach($field in @('intentionalDeferredFaults','intentionalCommitFaults')){
        $property=$Receipt.PSObject.Properties[$field]
        if(-not$property-or($property.Value-isnot[int]-and$property.Value-isnot[long])){throw 'fixture_fault_not_proved'}
    }
    if($Receipt.msiExitCode-ne$Expected){throw 'fixture_fault_not_proved'}
    $deferred=0;$commit=0
    if($Expected-eq1603){
        if($FailPhase-ceq'commit_grant'){$commit=1}
        elseif($FailPhase-ceq'deferred_grant'-or$FailPhase-ceq'deferred_release'){$deferred=1}
        else{throw 'fixture_fault_not_proved'}
    }elseif($Expected-ne0-or$FailPhase-cne'none'){throw 'fixture_fault_not_proved'}
    if($Receipt.intentionalDeferredFaults-ne$deferred-or$Receipt.intentionalCommitFaults-ne$commit){throw 'fixture_fault_not_proved'}
}
function Invoke-FixturePhase([string]$Name,[string]$Operation,[string]$FailPhase,[int]$Expected,[bool]$Member,[bool]$Owned,[string]$Journal) {
    $script:FixtureCurrentPhase=$Name
    $script:FixtureStage='msi_phase'
    $receipt=Invoke-FixtureClient @($Operation,$FailPhase) $Name -Msi
    if($receipt.msiExitCode-ne$Expected){throw 'fixture_unexpected_msi_exit'}
    Assert-FixtureFaultReceipt $receipt $Expected $FailPhase
    Assert-FixtureProof $receipt.proof $Member $Owned $Journal
    Assert-FixtureStillSafe
    $script:steps += [pscustomobject]@{phase=$Name;status='PASS';msiExitCode=$receipt.msiExitCode;quiescence=$receipt.quiescence;proof=$receipt.proof;uiMessageCount=$receipt.uiMessageCount;intentionalDeferredFaults=$receipt.intentionalDeferredFaults;intentionalCommitFaults=$receipt.intentionalCommitFaults;lowMemoryWarningObserved=$receipt.lowMemoryWarningObserved}
    $script:FixtureCurrentPhase=$null
}
try {
    # Non-admin Execute refusal precedes every read/write/compilation.
    $script:FixtureStage='authorization'
    if($Execute){$principal=[Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent());if(-not$principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)){throw 'elevated_operator_required'}}
    $prepare=Join-Path $PSScriptRoot 'Prepare-CounterReadMsiFixture.ps1'
    $manifest=$null
    if($Execute){
        $script:FixtureStage='manifest_validation'
        if($ExpectedManifestSha256-cnotmatch'^[A-F0-9]{64}$'-or-not$ManifestPath){throw 'reviewed_manifest_pin_required'}
        $manifestLock=[IO.File]::Open($ManifestPath,'Open','Read','Read');$locks+=$manifestLock
        if($manifestLock.Length-gt4MB-or(Get-FileHash -LiteralPath $ManifestPath).Hash-cne$ExpectedManifestSha256){throw 'reviewed_manifest_mismatch'}
        $manifest=Get-Content -LiteralPath $ManifestPath -Raw|ConvertFrom-Json
        if($manifest.Format-ne1-or@($manifest.Files).Count-gt4096){throw 'reviewed_manifest_invalid'}
        # Lock and hash the preparation code BEFORE dot-sourcing it.
        $entry=@($manifest.Files|Where-Object {$_.Path-ceq$prepare});if($entry.Count-ne1){throw 'reviewed_input_pin_mismatch'}
        $stream=[IO.File]::Open($prepare,'Open','Read','Read');$locks+=$stream
        if((Get-FileHash -LiteralPath $prepare).Hash-cne$entry[0].Sha256){throw 'reviewed_input_pin_mismatch'}
    }
    . $prepare
    $script:FixtureStage='vm_resource_and_protected_identity_preflight'
    Assert-CounterFixtureVm;Assert-CounterFixtureResources
    $before=Get-ProtectedFixtureIdentity $manifest
    if(-not$Execute){[pscustomobject]@{status='READ_ONLY_PREFLIGHT';changesPerformed=$false;nativeMatrixRun=$false;protectedIdentity=$before;next='Prepare and separately review/pin an external manifest before Execute.'}|ConvertTo-Json -Depth 6;return}
    $script:FixtureStage='scope_validation'
    $scope=Get-CounterFixtureScope $manifest.RunId ''
    $null=Read-CounterFixtureManifest $ManifestPath $ExpectedManifestSha256 $scope.Id
    foreach($file in $manifest.Files){Assert-CounterFixturePlainPath $file.Path;$stream=[IO.File]::Open($file.Path,'Open','Read','Read');$locks+=$stream;if((Get-FileHash -LiteralPath $file.Path).Hash-cne$file.Sha256-or$stream.Length-ne$file.Length){throw 'reviewed_input_pin_mismatch'}}
    $native=[IO.File]::ReadAllText((Join-Path ([IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))) 'tools\installer\WindowsCounterReadState.cs'))
    foreach($marker in @(Get-CounterFixtureMarkers)){if([regex]::Matches($native,[regex]::Escape($marker)).Count-ne1){throw 'fixture_native_target_marker_mismatch'}}
    if((Test-Path -LiteralPath $scope.Root)-or(Test-Path -LiteralPath $scope.ProgramFiles)-or(Test-Path -LiteralPath ('Registry::HKEY_LOCAL_MACHINE\'+$scope.Registry))-or
        (Get-LocalGroup -Name $scope.Name -ErrorAction SilentlyContinue)-or(Get-CimInstance Win32_Service -Filter ("Name='"+$scope.Name+"'"))){throw 'fixture_scope_already_exists_preserve'}
    Assert-CounterFixturePlainPath $scope.ProgramFiles
    # Independent 250ms watchdog is active throughout fixture setup/build/matrix.
    $script:FixtureStage='watchdog_start'
    Add-Type -TypeDefinition @'
using System; using System.Runtime.InteropServices; using System.Threading;
public sealed class CounterReadFixtureParentMonitor:IDisposable {
 string failure; int warning; long minimumBytes=long.MaxValue;
 readonly ManualResetEvent done=new ManualResetEvent(false);readonly Thread thread;
 public string FailureCode {get{return Volatile.Read(ref failure);}}
 public bool LowMemoryWarningObserved {get{return Volatile.Read(ref warning)!=0;}}
 public double? MinimumFreeMiB {get{var value=Interlocked.Read(ref minimumBytes);return value==long.MaxValue?(double?)null:value/1048576d;}}
 public CounterReadFixtureParentMonitor(){thread=new Thread(Run);thread.IsBackground=true;thread.Start();}
 void Run(){try{do{
  var m=new M();m.Length=(uint)Marshal.SizeOf(typeof(M));string code=null;
  if(!GlobalMemoryStatusEx(ref m))code="monitor_failed";
  else {
   var value=(long)m.Free;long previous;
   do{previous=Interlocked.Read(ref minimumBytes);if(value>=previous)break;}while(Interlocked.CompareExchange(ref minimumBytes,value,previous)!=previous);
   if(m.Free<=512UL*1048576)code="hard_memory_floor_512";
   else if(m.Free<768UL*1048576)Interlocked.Exchange(ref warning,1);
  }
  ulong a,t,f;if(code==null&&(!GetDiskFreeSpaceEx("C:\\",out a,out t,out f)||a<15UL*1024*1024*1024))code="disk_floor_15_gib";
  if(code!=null)Interlocked.CompareExchange(ref failure,code,null);
 }while(!done.WaitOne(250));}catch{Interlocked.CompareExchange(ref failure,"monitor_failed",null);}}
 public void Dispose(){done.Set();if(!thread.Join(3000))throw new InvalidOperationException("monitor_shutdown_unconfirmed");done.Dispose();}
 [StructLayout(LayoutKind.Sequential)]struct M{public uint Length,Load;public ulong Total,Free,TP,FP,TV,FV,E;}
 [DllImport("kernel32.dll")]static extern bool GlobalMemoryStatusEx(ref M m);
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]static extern bool GetDiskFreeSpaceEx(string p,out ulong a,out ulong t,out ulong f);
}
'@
    $script:FixtureMonitor=[CounterReadFixtureParentMonitor]::new()
    $script:FixtureStage='fixture_root_create'
    New-FixtureProtectedDirectory $scope.Root
    $createdRoot=$true
    $script:FixtureStage='local_group_create'
    $group=New-LocalGroup -Name $scope.Name -Description 'HostAgent counter-read fixture'
    $createdGroup=$true;$groupSid=$group.SID.Value
    # showsid derives a virtual service SID without registration or start.
    $script:FixtureStage='service_sid_derivation'
    $sidInfo=[Diagnostics.ProcessStartInfo]::new();$sidInfo.FileName=Join-Path $env:WINDIR 'System32\sc.exe';$sidInfo.UseShellExecute=$false;$sidInfo.CreateNoWindow=$true;$sidInfo.RedirectStandardOutput=$true;$sidInfo.RedirectStandardError=$true;$sidInfo.ArgumentList.Add('showsid');$sidInfo.ArgumentList.Add($scope.Name)
    $sidProcess=[Diagnostics.Process]::Start($sidInfo)
    $sidOutput=$sidProcess.StandardOutput.ReadToEndAsync();$sidErrors=$sidProcess.StandardError.ReadToEndAsync()
    try{
        if(-not$sidProcess.WaitForExit(10000)){
            $sidProcess.Kill();$null=$sidProcess.WaitForExit(2000)
            throw 'fixture_sid_derivation_failed'
        }
        if(-not$sidOutput.Wait(2000)-or-not$sidErrors.Wait(2000)-or$sidProcess.ExitCode-ne0){throw 'fixture_sid_derivation_failed'}
        $sidText=$sidOutput.GetAwaiter().GetResult();$sidError=$sidErrors.GetAwaiter().GetResult()
        if($sidText.Length-gt65536-or$sidError.Length-ne0){throw 'fixture_sid_derivation_failed'}
    }finally{$sidProcess.Dispose()}
    $matches=[regex]::Matches($sidText,'S-1-5-80-(\d+-){4}\d+');if($matches.Count-ne1){throw 'fixture_sid_derivation_failed'};$memberSid=$matches[0].Value
    $script:FixtureStage='fixture_build'
    $build=Build-CounterFixture $scope $memberSid $groupSid $manifest
    $script:FixtureStage='artifact_pin_validation'
    foreach($artifact in $build.Artifacts){$stream=[IO.File]::Open($artifact.Path,'Open','Read','Read');$locks+=$stream;if((Get-FileHash -LiteralPath $artifact.Path).Hash-cne$artifact.Sha256){throw 'fixture_artifact_pin_changed'}}
    # All subsequent controller calls use the pinned fixed-target executable.
    $script:FixtureStage='private_service_registration'
    Ensure-FixtureRegistration
    $script:FixtureStage='initial_proof'
    $proof=Invoke-FixtureClient @('--proof') 'initial-proof';Assert-FixtureProof $proof $false $false 'absent'
    Invoke-FixturePhase 'missing-grant-rollback' 'install' 'deferred_grant' 1603 $false $false 'absent'
    Ensure-FixtureRegistration
    $script:FixtureStage='seed_add'
    $proof=Invoke-FixtureClient @('--seed-add') 'preexisting-seed';$seeded=$true;Assert-FixtureProof $proof $true $false 'absent'
    Invoke-FixturePhase 'preexisting-grant-rollback' 'install' 'deferred_grant' 1603 $true $false 'absent'
    Ensure-FixtureRegistration
    Invoke-FixturePhase 'preexisting-install' 'install' 'none' 0 $true $false 'committed'
    Invoke-FixturePhase 'preexisting-uninstall-preserve' 'uninstall' 'none' 0 $true $false 'committed'
    Ensure-FixtureRegistration
    $script:FixtureStage='seed_remove_preproof'
    $seedProof=Invoke-FixtureClient @('--proof') 'before-remove-private-seed'
    $script:FixtureStage='seed_remove'
    $proof=Invoke-FixtureClient @('--seed-remove') 'remove-private-seed';$seeded=$false;Assert-FixtureSeedRemoval $seedProof $proof
    Ensure-FixtureRegistration
    Invoke-FixturePhase 'late-commit-grant-rollback' 'install' 'commit_grant' 1603 $false $false 'absent'
    Ensure-FixtureRegistration
    Invoke-FixturePhase 'owned-install' 'install' 'none' 0 $true $true 'committed'
    Invoke-FixturePhase 'owned-uninstall-rollback' 'uninstall' 'deferred_release' 1603 $true $true 'absent'
    Invoke-FixturePhase 'owned-uninstall-remove' 'uninstall' 'none' 0 $false $false 'committed'
    Assert-FixtureStillSafe
}catch{
    $known=@('elevated_operator_required','reviewed_manifest_pin_required','reviewed_manifest_mismatch','reviewed_manifest_invalid','reviewed_input_pin_mismatch',
        'approved_dev_vm_required','initial_ram_not_above_768_mib','hard_memory_floor_512','initial_disk_below_15_gib','protected_identity_mismatch','protected_installed_pin_mismatch','protected_identity_changed',
        'fixture_scope_already_exists_preserve','fixture_reparse_path_rejected','fixture_native_target_marker_mismatch','fixture_resource_abort','fixture_sid_derivation_failed',
        'fixture_client_hard_deadline_quiescence_unconfirmed','fixture_client_output_limit','fixture_client_failed_preserve','fixture_msi_not_quiescent_or_resource_abort','fixture_unexpected_msi_exit','fixture_proof_mismatch','fixture_fault_not_proved',
        'fixture_build_resource_or_deadline','fixture_build_tool_failed','fixture_service_identity_or_state_changed','fixture_group_identity_changed')
    $details=Get-CounterFixtureFailureDetails $_ $script:FixtureStage $known
    $failure=$details.failureCode;$failureStage=$details.failureStage;$failureExceptionType=$details.exceptionType;$failureHResult=$details.hResult
}finally{
    if($Execute){
        # Never start cleanup after any failure or unconfirmed MSI. Preserve the
        # group/service/journal for exact manual inspection instead of guessing.
        if(-not$failure-and$quiescent-and$createdGroup-and$build){
            try{
                $script:FixtureStage='cleanup'
                Assert-FixtureStillSafe
                if(@(Get-LocalGroupMember -Group $scope.Name).Count-ne0){throw 'fixture_group_not_empty'}
                $svc=Get-CimInstance Win32_Service -Filter ("Name='"+$scope.Name+"'")
                if($svc){Invoke-CounterFixtureTool (Join-Path $env:WINDIR 'System32\sc.exe') @('delete',$scope.Name) (Join-Path $scope.Root 'service-delete')}
                if((Get-LocalGroup -Name $scope.Name).SID.Value-cne$build.GroupSid){throw 'fixture_group_identity_changed'}
                if(@(Get-LocalGroupMember -Group $scope.Name).Count-ne0){throw 'fixture_group_not_empty'}
                Remove-LocalGroup -Name $scope.Name
            }catch{$cleanupDetails=Get-CounterFixtureFailureDetails $_ 'cleanup' @();$failure='fixture_cleanup_unconfirmed_preserve';$failureStage=$cleanupDetails.failureStage;$failureExceptionType=$cleanupDetails.exceptionType;$failureHResult=$cleanupDetails.hResult}
        }
        $endingFreeMiB=$null
        if($script:FixtureMonitor){
            try{
                $recovery=[Diagnostics.Stopwatch]::StartNew()
                do{$endingFreeMiB=(Get-CimInstance Win32_OperatingSystem -OperationTimeoutSec 5).FreePhysicalMemory/1024;if($endingFreeMiB-gt768){break};Start-Sleep -Milliseconds 250}while($recovery.Elapsed.TotalSeconds-lt30)
                if($endingFreeMiB-le768-and-not$failure){$failure='fixture_post_run_memory_recovery_unconfirmed';$failureStage='post_run_memory_recovery';$failureExceptionType=$null;$failureHResult=$null}
            }catch{if(-not$failure){$failure='fixture_post_run_memory_read_failed';$failureStage='post_run_memory_recovery';$failureExceptionType=$_.Exception.GetBaseException().GetType().FullName;$failureHResult=[int]$_.Exception.GetBaseException().HResult}}
            try{$script:FixtureStage='watchdog_shutdown';$script:FixtureMonitor.Dispose();if($script:FixtureMonitor.FailureCode-and-not$failure){$failure='fixture_resource_abort';$failureStage='watchdog_shutdown';$failureExceptionType=$null;$failureHResult=$null}}catch{$failure='fixture_monitor_shutdown_unconfirmed';$failureStage='watchdog_shutdown';$failureExceptionType=$_.Exception.GetBaseException().GetType().FullName;$failureHResult=[int]$_.Exception.GetBaseException().HResult}
        }
        $protectedUnchanged=$false
        if($before){try{$script:FixtureStage='protected_identity_final_check';$after=Get-ProtectedFixtureIdentity $manifest;$protectedUnchanged=($before|ConvertTo-Json -Depth 6 -Compress)-ceq($after|ConvertTo-Json -Depth 6 -Compress);if(-not$protectedUnchanged){$failure='protected_identity_changed';$failureStage=$script:FixtureStage;$failureExceptionType=$null;$failureHResult=$null}}catch{$failure='protected_identity_verification_failed';$failureStage='protected_identity_final_check';$failureExceptionType=$_.Exception.GetBaseException().GetType().FullName;$failureHResult=[int]$_.Exception.GetBaseException().HResult}}
        $result=[ordered]@{scenario='isolated_guid_counter_read_native_msi_matrix';status=if($failure){if(-not$scope){'NOTRUN'}else{'FAIL'}}else{'PASS_FIXTURE_ONLY'};
            failure=$failure;reason=$failure;failureStage=$failureStage;failureExceptionType=$failureExceptionType;failureHResult=$failureHResult;
            failedPhase=if($failure){$script:FixtureCurrentPhase}else{$null};quiescenceConfirmed=if($msiAttempted){$quiescent}else{$null};protectedIdentityUnchanged=$protectedUnchanged;phases=$steps;
            changesPerformed=($createdRoot-or$createdGroup-or$createdService);nativeMatrixRun=$msiAttempted;
            failureBeforeFirstMsi=([bool]$failure-and-not$msiAttempted);
            memory=[pscustomobject]@{initialFreeMiB=$script:FixtureInitialFreeMiB;endingFreeMiB=$endingFreeMiB;minimumFreeMiB=if($script:FixtureMonitor){$script:FixtureMonitor.MinimumFreeMiB}else{$null};lowMemoryWarningObserved=if($script:FixtureMonitor){$script:FixtureMonitor.LowMemoryWarningObserved}else{$false};startRequiresAboveMiB=768;runtimeWarningMiB=768;emergencyAbortMiB=512;postRunRecovered=($null-ne$endingFreeMiB-and$endingFreeMiB-gt768)};
            nativeAcceptance=if(-not$msiAttempted){'NOTRUN'}elseif($failure){'INCOMPLETE'}else{'PASS_FIXTURE_ONLY'};
            nativeFixtureIsInstalledProductAcceptance=$false;sourceAndProductPackageModified=$false;rebootPerformed=$false;
            evidenceDirectory=if($scope){$scope.Root}else{$null};preservedForManualReview=([bool]$failure-and[bool]$scope);expectedReviewedManifestSha256=$ExpectedManifestSha256}
        if($createdRoot-and$scope-and(Test-Path -LiteralPath $scope.Root)){try{$script:FixtureStage='receipt_write';$result|ConvertTo-Json -Depth 7|Set-Content -LiteralPath (Join-Path $scope.Root 'result.json')}catch{$failure='fixture_receipt_write_failed';$failureStage='receipt_write';$failureExceptionType=$_.Exception.GetBaseException().GetType().FullName;$failureHResult=[int]$_.Exception.GetBaseException().HResult;$result.failure=$failure;$result.reason=$failure;$result.status='FAIL';$result.failureStage=$failureStage;$result.failureExceptionType=$failureExceptionType;$result.failureHResult=$failureHResult;$result.failureBeforeFirstMsi=([bool]$failure-and-not$msiAttempted);$result.nativeAcceptance='INCOMPLETE';$result.preservedForManualReview=$true}}
        $result|ConvertTo-Json -Depth 7
    }
    foreach($lock in $locks){$lock.Dispose()}
}
if($failure){exit 1}

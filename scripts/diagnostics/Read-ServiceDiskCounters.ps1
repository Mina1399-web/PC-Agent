<# Operator-run, read-only diagnostic. No service control, configuration, group,
privilege, ACL or database changes. A copied token is used only for the fixed
three PDH counters on one Python thread, and always reverted before output.
This compares effective token access; it is not a complete SCM runtime proof. #>
[CmdletBinding()]
param()
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$identity=[Security.Principal.WindowsIdentity]::GetCurrent()
if(-not ([Security.Principal.WindowsPrincipal]::new($identity)).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)){throw 'elevated_operator_required'}
if($PSVersionTable.PSVersion.Major -lt 7){throw 'powershell7_required'}
if([IntPtr]::Size -ne 8 -or $identity.Name -ne 'AgentPC\AgentPCdevtest' -or $env:COMPUTERNAME -ne 'AGENTPC' -or
 (Get-CimInstance Win32_ComputerSystem).Model -ne 'VirtualBox' -or
 (Get-CimInstance Win32_ComputerSystemProduct).UUID -ne '46543236-8C5E-49DA-953C-DB17F9B1E402' -or
 (Get-Service VBoxService).Status -ne 'Running'){throw 'approved_dev_vm_required'}
if((Get-CimInstance Win32_OperatingSystem).FreePhysicalMemory/1024 -lt 1024){throw 'initial_ram_below_1024_mib'}
if((Get-PSDrive C).Free -lt 20GB){throw 'initial_disk_below_20_gib'}
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$candidate=Join-Path (Split-Path $repo) 'HostAgent-DEV-releases\HostAgent-1.2.5-dev-view-summary-r1'
$manifest=Join-Path $candidate 'manifest.psd1'
if((Get-FileHash -LiteralPath $manifest -Algorithm SHA256).Hash -cne '6D302315BE3AE5762D06C34B6D54530730BA35A5406E6F7FC52F5E5B32F184C2'){throw 'installed_manifest_identity_mismatch'}
if((Get-Item -LiteralPath $manifest).Length -gt 1MB){throw 'manifest_size_guard'}
# Parse only fixed entries from this exact hash-pinned generated manifest. Do not
# evaluate the 1200-entry PSD1 (PowerShell data-file evaluation has a key limit).
$manifestText=Get-Content -LiteralPath $manifest -Raw
if(-not $manifestText.Contains("Version='1.2.5';SourceCommit='e410360ddc31f86c752587ff315d6244c7944eb7'")){throw 'installed_source_identity_mismatch'}
$critical=@('Service/HostAgent.Service.exe','Service/HostAgent.Service.dll','Service/HostAgent.Core.dll','Service/HostAgent.Infrastructure.dll','Helper/HostAgent.Helper.exe','Helper/HostAgent.Helper.dll')
function Assert-PlainPath([string]$Path){
 $cursor=[IO.Path]::GetFullPath($Path)
 while($cursor){
  if(Test-Path -LiteralPath $cursor){if((Get-Item -LiteralPath $cursor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'reparse_path_rejected'}}
  $cursor=[IO.Path]::GetDirectoryName($cursor)
 }
}
function Open-PinnedInput([string]$Path,[string]$ExpectedHash){
 Assert-PlainPath $Path
 $handle=[IO.File]::Open($Path,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read)
 try{
  if((Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash -cne $ExpectedHash){throw 'pinned_input_mismatch'}
  return $handle
 }catch{$handle.Dispose();throw}
}
function Read-InstalledIdentity {
 $services=@(Get-CimInstance Win32_Service|Where-Object Name -In @('HostAgent','HostAgent.Helper')|Sort-Object Name)
 if($services.Count -ne 2 -or @($services|Where-Object {$_.State -ne 'Running' -or $_.StartMode -ne 'Auto' -or $_.ProcessId -le 0}).Count){throw 'installed_service_state_mismatch'}
 foreach($service in $services){
  $expected=if($service.Name -ceq 'HostAgent'){@('NT SERVICE\HostAgent','"C:\Program Files\HostAgent\Service\HostAgent.Service.exe"')}else{@('LocalSystem','"C:\Program Files\HostAgent\Helper\HostAgent.Helper.exe"')}
  if($service.StartName -ine $expected[0] -or $service.PathName -cne $expected[1]){throw 'installed_service_identity_mismatch'}
 }
 $hashes=[ordered]@{}
 foreach($relative in $critical){
  $pattern="@\{Path='"+[regex]::Escape($relative)+"';Sha256='([A-F0-9]{64})';Length=([0-9]+)\}"
  $entry=[regex]::Matches($manifestText,$pattern)
  if($entry.Count -ne 1){throw 'critical_entry_missing'}
  $path=Join-Path 'C:\Program Files\HostAgent' $relative
  Assert-PlainPath $path
  $expectedHash=$entry[0].Groups[1].Value
  $expectedLength=[long]$entry[0].Groups[2].Value
  if((Get-Item -LiteralPath $path).Length -ne $expectedLength -or (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -cne $expectedHash){throw 'installed_critical_hash_mismatch'}
  $hashes[$relative]=$expectedHash
 }
 [pscustomobject]@{services=@($services|Select-Object Name,State,StartMode,StartName,PathName,ProcessId);hashes=$hashes}
}
$before=Read-InstalledIdentity
$python=Join-Path $env:USERPROFILE '.cache\codex-runtimes\codex-primary-runtime\dependencies\python\python.exe'
$probe=Join-Path $PSScriptRoot 'Read-ServiceDiskCounters.py'
Assert-PlainPath $python; Assert-PlainPath $probe
if((Get-FileHash -LiteralPath $python -Algorithm SHA256).Hash -cne '7C4EE3011932A75B30DB176F35FB91B28F0B299E608EAF643FB1BF36ED5ACE99'){throw 'python_identity_mismatch'}
$out=Join-Path $env:LOCALAPPDATA ('HostAgent-DEV-evidence\service-token-pdh-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $out|Out-Null
$monitor=$null; $steps=@();$failure=$null
$locks=@();$after=$null
function Complete-ProbeMonitor($Monitor){
 if(-not $Monitor){return 'resource_monitor_unavailable'}
 try{$Monitor.Dispose()}catch{return 'resource_monitor_shutdown_unconfirmed'}
 # Dispose joins the thread before reading its final, latched observation.
 return $Monitor.FailureCode
}
function Invoke-FixedProbe([string[]]$Arguments,[string]$LogDirectory){
 $job=[ScmPressureJob]::new();$child=$null;$stdout=$null;$stderr=$null
 try{
  if($monitor.FailureCode){throw $monitor.FailureCode}
  $info=[Diagnostics.ProcessStartInfo]::new();$info.FileName=$python
  $info.UseShellExecute=$false;$info.CreateNoWindow=$true
  $info.RedirectStandardOutput=$true;$info.RedirectStandardError=$true
  foreach($argument in $Arguments){$info.ArgumentList.Add($argument)}
  $timer=[Diagnostics.Stopwatch]::StartNew()
  $child=[Diagnostics.Process]::Start($info)
  $job.Assign($child)
  $stdout=$child.StandardOutput.ReadToEndAsync();$stderr=$child.StandardError.ReadToEndAsync()
  while(-not $child.HasExited){
   if($monitor.FailureCode){throw $monitor.FailureCode}
   if($timer.Elapsed.TotalSeconds -ge 20){throw 'native_probe_deadline'}
   Start-Sleep -Milliseconds 100
  }
  $child.WaitForExit()
  if($monitor.FailureCode){throw $monitor.FailureCode}
  return [pscustomobject]@{exitCode=$child.ExitCode;stdoutPath=(Join-Path $LogDirectory 'stdout.txt')}
 }finally{
  # No services need cleanup: terminate only this probe job immediately on abort.
  $job.Dispose()
  if($child){
   if(-not $child.WaitForExit(2000)){$child.Kill();if(-not $child.WaitForExit(2000)){throw 'own_probe_termination_unconfirmed'}}
   if($stdout -and $stderr){
    $outputText=$stdout.GetAwaiter().GetResult();$errorText=$stderr.GetAwaiter().GetResult()
    [IO.File]::WriteAllText((Join-Path $LogDirectory 'stdout.txt'),$outputText.Substring(0,[math]::Min($outputText.Length,65536)))
    [IO.File]::WriteAllText((Join-Path $LogDirectory 'stderr.txt'),$errorText.Substring(0,[math]::Min($errorText.Length,65536)))
   }
   $child.Dispose()
  }
 }
}
try{
 $inputs=@(
  @($python,'7C4EE3011932A75B30DB176F35FB91B28F0B299E608EAF643FB1BF36ED5ACE99'),
  @($probe,'B1B76EE15F62367BCAEAC51A86A95607B3800F8012240023C8BA4C56408C4AB5'),
  @((Join-Path $repo 'scripts\chaos\CompleteCampaignMonitor.cs'),'5477B18B434B7E151F42AE3B4C1EC9620E47B722001728A99344FEC7AFA48CD4'),
  @((Join-Path $repo 'scripts\chaos\BoundedScmPressure.cs'),'177CA8300A55584494592B2F4E9F374B1D6C707858E7F4ED2CF9735AB2AAEE2F')
 )
 foreach($inputFile in $inputs){$locks+=Open-PinnedInput $inputFile[0] $inputFile[1]}
 Add-Type -LiteralPath (Join-Path $repo 'scripts\chaos\CompleteCampaignMonitor.cs')
 $monitor=[CompleteCampaignMonitor]::new()
 Add-Type -LiteralPath (Join-Path $repo 'scripts\chaos\BoundedScmPressure.cs')
 $sid=([Security.Principal.NTAccount]::new('NT SERVICE','HostAgent')).Translate([Security.Principal.SecurityIdentifier]).Value
 $service=@($before.services|Where-Object Name -CEQ 'HostAgent')[0]
 foreach($mode in @('service','interactive')){
  $logs=Join-Path $out $mode;New-Item -ItemType Directory -Path $logs|Out-Null
  $arguments=@('-I','-B',$probe,$mode)
  if($mode -ceq 'service'){$arguments+=@([string]$service.ProcessId,$sid)}
  $run=Invoke-FixedProbe $arguments $logs
  $data=Get-Content -LiteralPath $run.stdoutPath -Raw|ConvertFrom-Json
  $steps+=[ordered]@{context=$mode;exitCode=$run.exitCode;result=$data}
  if($run.exitCode -ne 0 -or $data.completed -ne $true){throw ('native_probe_not_completed_'+$mode)}
 }
 if($monitor.FailureCode){throw $monitor.FailureCode}
}catch{$failure=$_.Exception.Message}finally{
 foreach($lock in $locks){$lock.Dispose()}
 try{$after=Read-InstalledIdentity;if(($before|ConvertTo-Json -Depth 5 -Compress) -cne ($after|ConvertTo-Json -Depth 5 -Compress)){throw 'installed_identity_changed'}}catch{if(-not $failure){$failure=$_.Exception.Message}}
 $monitorFailure=Complete-ProbeMonitor $monitor
 if($monitorFailure -and -not $failure){$failure=$monitorFailure}
 $result=[ordered]@{scenario='installed_fixed_pdh_effective_token_comparison';status=if($failure){'INCOMPLETE'}else{'COMPLETE_DIAGNOSTIC_NOT_FULL_HEALTH'};failure=$failure;steps=$steps;monitorFailure=$monitorFailure;servicesAndHashesUnchanged=($null -ne $after -and ($before|ConvertTo-Json -Depth 5 -Compress) -ceq ($after|ConvertTo-Json -Depth 5 -Compress));privilegesChanged=$false;serviceControlPerformed=$false;databaseOpened=$false;evidenceDirectory=$out}
 $result|ConvertTo-Json -Depth 8|Set-Content -LiteralPath (Join-Path $out 'result.json')
 $result|ConvertTo-Json -Depth 8
}
if($failure){throw $failure}

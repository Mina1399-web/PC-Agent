$ErrorActionPreference='Stop'
$source=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$script=Join-Path $source 'tools/Build-SubmissionPreview.ps1'
$fixture=Join-Path ([IO.Path]::GetTempPath()) ('HostAgent-submission-guard-'+[guid]::NewGuid().ToString('N'))
$null=New-Item -ItemType Directory -Path $fixture
$sentinel=Join-Path $fixture 'preserve.txt';[IO.File]::WriteAllText($sentinel,'preserve existing data')
$before=(Get-FileHash -LiteralPath $sentinel).Hash
$checks=0
foreach($output in @((Join-Path $fixture 'new'),$fixture,(Join-Path $source 'must-not-be-created'))){
 $info=[Diagnostics.ProcessStartInfo]::new((Join-Path $PSHOME 'pwsh.exe'))
 $info.UseShellExecute=$false;$info.CreateNoWindow=$true;$info.RedirectStandardOutput=$true;$info.RedirectStandardError=$true
 foreach($argument in @('-NoProfile','-NonInteractive','-File',$script,'-PackagePath',$output,'-WixPath','not-used','-DtfRoot','not-used','-WixSourceArchive','not-used')){$info.ArgumentList.Add($argument)}
 $info.Environment['GITHUB_ACTIONS']='false'
 $child=[Diagnostics.Process]::Start($info);$stdout=$child.StandardOutput.ReadToEndAsync();$stderr=$child.StandardError.ReadToEndAsync()
 if(-not $child.WaitForExit(20000)){$child.Kill($true);throw 'submission_guard_test_timeout'}
 $out=$stdout.GetAwaiter().GetResult();$err=$stderr.GetAwaiter().GetResult()
 if($child.ExitCode -eq 0 -or $err -notmatch 'submission_hosted_runner_required' -or $out.Trim()){throw ('submission_guard_assertion:unapproved_environment_refused_before_output:'+ $err.Trim())}
 $checks++
}
if((Get-FileHash -LiteralPath $sentinel).Hash -cne $before -or (Test-Path -LiteralPath (Join-Path $fixture 'new')) -or (Test-Path -LiteralPath (Join-Path $source 'must-not-be-created'))){throw 'submission_guard_assertion:output_or_existing_data_changed'}
[pscustomobject]@{status='PASS';checks=$checks+1;scope='unapproved_host_rejected_without_writes_or_tool_execution'} | ConvertTo-Json -Compress

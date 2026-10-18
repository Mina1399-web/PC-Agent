$ErrorActionPreference='Stop'
$repo=Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$tokens=$null;$errors=$null
$ast=[Management.Automation.Language.Parser]::ParseFile((Join-Path $repo 'scripts/chaos/run-complete-dev-resilience.ps1'),[ref]$tokens,[ref]$errors)
$exitStatement=@($ast.EndBlock.Statements|Where-Object {$_.Extent.Text.StartsWith('if(@($rows|Where-Object status -eq FAIL).Count')})
if($exitStatement.Count-ne1){throw 'final_exit_guard_not_found'}
$checked=0
foreach($case in @(@{focus=$true;preserved=$false;failed=$false;latched=$false;exit=1},@{focus=$true;preserved=$true;failed=$false;latched=$false;exit=0},@{focus=$false;preserved=$false;failed=$false;latched=$false;exit=0},@{focus=$true;preserved=$true;failed=$true;latched=$false;exit=1},@{focus=$true;preserved=$true;failed=$false;latched=$true;exit=1},@{focus=$false;s3=$true;preserved=$false;failed=$false;latched=$false;exit=1},@{focus=$false;s3=$true;preserved=$true;failed=$false;latched=$false;exit=0})){
 $source='$FocusS2Failures='+ '$'+ $case.focus.ToString().ToLowerInvariant()+ ';$script:previousHelperArtifactsUnchanged=$'+ $case.preserved.ToString().ToLowerInvariant()+ ';$script:completeSafetyLatched=$'+ $case.latched.ToString().ToLowerInvariant()+ ';$rows=@(@{status="'+ $(if($case.failed){'FAIL'}else{'PASS'})+ '"});'+ $exitStatement[0].Extent.Text
 $isS3=($case.ContainsKey('s3')-and$case.s3)
 $source='$FocusS3=$'+ $isS3.ToString().ToLowerInvariant()+ ';'+ $source
 $source|& 'C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe' -NoProfile -NonInteractive -Command -
 if($LASTEXITCODE-ne$case.exit){throw 'focused_exit_does_not_enforce_preservation_and_safety'}
 $checked++
}
[pscustomobject]@{suite='complete-focused-exit';passed=$checked;failed=0;serviceOperations=0}|ConvertTo-Json -Compress

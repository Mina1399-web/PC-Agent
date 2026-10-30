$ErrorActionPreference='Stop'
Import-Module (Join-Path $PSScriptRoot '../../scripts/chaos/DevSnapshotReceipt.psm1') -Force
$file=Join-Path ([IO.Path]::GetTempPath()) ('snapshot-receipt-'+[guid]::NewGuid()+'.json')
$now=[DateTimeOffset]::UtcNow
$r=@{format=1;vmUuid='0bb543d1-7bb0-43ed-9c94-596d63ef3310';snapshotUuid='3b3a6a0e-d861-4d25-9997-b39c407058a5';snapshotName='clean-win-base';verifiedAtUtc=$now.ToString('o');scope='snapshot-exists-only'}
try {
 $checks=0
 foreach($kind in @('valid','stale','future','wrong_vm','empty_snapshot','missing')){
  $test=$r.Clone()
  switch($kind){stale{$test.verifiedAtUtc=$now.AddHours(-2).ToString('o')} future{$test.verifiedAtUtc=$now.AddMinutes(1).ToString('o')} wrong_vm{$test.vmUuid=[guid]::NewGuid().ToString()} empty_snapshot{$test.snapshotUuid=[guid]::Empty.ToString()}}
  [IO.File]::WriteAllText($file,($test|ConvertTo-Json))
  $accepted=$false
  try{$null=Read-DevSnapshotReceipt -Path $(if($kind -eq 'missing'){''}else{$file}) -Now $now;$accepted=$true}catch{}
  if($accepted -ne ($kind -eq 'valid')){throw "snapshot_receipt_case_failed:$kind"}
  $checks++
 }
 "snapshot_receipt_passed:$checks;power_operations=0"
} finally {if(Test-Path -LiteralPath $file){Remove-Item -LiteralPath $file}}

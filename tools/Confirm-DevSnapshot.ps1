param([Parameter(Mandatory)][guid]$SnapshotUuid,
      [string]$VBoxManagePath='C:\Program Files\Oracle\VirtualBox\VBoxManage.exe')
$ErrorActionPreference='Stop'
$vm='0bb543d1-7bb0-43ed-9c94-596d63ef3310'
$lines=@(& $VBoxManagePath snapshot $vm list --machinereadable)
if($LASTEXITCODE -ne 0){throw 'snapshot_inventory_failed'}
$values=@{}
foreach($line in $lines){if($line -match '^(Snapshot(?:Name|UUID)(?:-[0-9-]+)?)="(.*)"$'){$values[$matches[1]]=$matches[2]}}
$key=@($values.Keys | Where-Object {$_ -like 'SnapshotUUID*' -and $values[$_] -ieq $SnapshotUuid.ToString()})
if($key.Count -ne 1){throw 'snapshot_not_found_for_approved_vm'}
$name=$values[$key[0].Replace('SnapshotUUID','SnapshotName')]
if(-not $name){throw 'snapshot_name_missing'}
[ordered]@{format=1;vmUuid=$vm;snapshotUuid=$SnapshotUuid.ToString();snapshotName=$name;
 verifiedAtUtc=[DateTimeOffset]::UtcNow.ToString('o');scope='snapshot-exists-only'} | ConvertTo-Json

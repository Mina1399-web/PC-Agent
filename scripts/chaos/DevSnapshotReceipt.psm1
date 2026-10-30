function Read-DevSnapshotReceipt {
    param([string]$Path,[DateTimeOffset]$Now=[DateTimeOffset]::UtcNow)
    if(-not $Path -or -not(Test-Path -LiteralPath $Path -PathType Leaf)){throw 'snapshot_receipt_required'}
    if((Get-Item -LiteralPath $Path).Length -gt 8192){throw 'snapshot_receipt_invalid'}
    $r=Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json -ErrorAction Stop
    $at=[DateTimeOffset]::MinValue; $guid=[guid]::Empty
    # Recent PowerShell versions deserialize ISO timestamps as DateTime.
    # Keep its Kind/offset instead of round-tripping through localized text.
    $dateValid=if($r.verifiedAtUtc -is [DateTime]){$at=[DateTimeOffset]$r.verifiedAtUtc;$true}
        else{[DateTimeOffset]::TryParse([string]$r.verifiedAtUtc,[Globalization.CultureInfo]::InvariantCulture,[Globalization.DateTimeStyles]::RoundtripKind,[ref]$at)}
    if($r.format -ne 1 -or $r.vmUuid -ine '0bb543d1-7bb0-43ed-9c94-596d63ef3310' -or
       $r.scope -cne 'snapshot-exists-only' -or -not [guid]::TryParse([string]$r.snapshotUuid,[ref]$guid) -or $guid -eq [guid]::Empty -or
       -not $r.snapshotName -or -not $dateValid -or
       $at -gt $Now -or ($Now-$at).TotalMinutes -gt 60){throw 'snapshot_receipt_invalid_or_stale'}
    return $r
}
Export-ModuleMember -Function Read-DevSnapshotReceipt

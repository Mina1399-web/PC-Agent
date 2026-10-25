#requires -Version 7.0
Set-StrictMode -Version Latest
function Assert-SubmissionHost([string]$SourcePath) {
    if($env:GITHUB_ACTIONS -cne 'true' -or $env:RUNNER_ENVIRONMENT -cne 'github-hosted' -or $env:RUNNER_OS -cne 'Windows' -or
       $env:GITHUB_REPOSITORY -cne 'Mina1399-web/PC-Agent' -or $env:GITHUB_EVENT_NAME -cnotin @('push','workflow_dispatch')){throw 'submission_hosted_runner_required'}
    if($env:GITHUB_SHA -cnotmatch '^[a-f0-9]{40}$' -or $env:GITHUB_RUN_ID -notmatch '^[0-9]{1,20}$'){throw 'submission_origin_invalid'}
    $commit=(& git -C $SourcePath rev-parse HEAD).Trim()
    if($LASTEXITCODE -ne 0 -or $commit -cne $env:GITHUB_SHA -or (& git -C $SourcePath status --porcelain)){throw 'submission_clean_source_required'}
    return $commit
}
function Assert-SubmissionPlainPath([string]$LiteralPath) {
    $item=Get-Item -LiteralPath $LiteralPath -Force -ErrorAction Stop
    while($null -ne $item){
        if($item.Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'submission_reparse_point'}
        if($item -is [IO.FileInfo]){$item=$item.Directory}else{$item=$item.Parent}
    }
}
function Assert-NewSubmissionPath([string]$LiteralPath,[string]$SourcePath) {
    $output=[IO.Path]::GetFullPath($LiteralPath).TrimEnd('\','/')
    $source=[IO.Path]::GetFullPath($SourcePath).TrimEnd('\','/')
    if($output -eq $source -or $output.StartsWith($source+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'submission_output_inside_source'}
    if((Test-Path -LiteralPath $output) -or (Test-Path -LiteralPath ($output+'.zip'))){throw 'submission_output_exists'}
    Assert-SubmissionPlainPath ([IO.Path]::GetDirectoryName($output))
    return $output
}
Export-ModuleMember -Function Assert-SubmissionHost,Assert-SubmissionPlainPath,Assert-NewSubmissionPath

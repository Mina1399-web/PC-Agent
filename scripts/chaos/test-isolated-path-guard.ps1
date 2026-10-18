$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'Assert-IsolatedCandidatePath.ps1')
$taskFixture=Join-Path $env:TEMP ('HostAgent-path-guard-'+[guid]::NewGuid().ToString('N'))
$taskEvidence=Join-Path $taskFixture 'evidence'
$taskOutside=Join-Path $taskFixture 'outside'
$taskLink=Join-Path $taskEvidence 'candidate-link'
$taskPlain=Join-Path $taskEvidence 'candidate-plain'
try{
    New-Item -ItemType Directory -Path $taskEvidence,$taskOutside,$taskPlain | Out-Null
    New-Item -ItemType Junction -Path $taskLink -Target $taskOutside | Out-Null
    $taskAccepted=Assert-IsolatedCandidatePath $taskPlain $taskEvidence
    if($taskAccepted -ne [IO.Path]::GetFullPath($taskPlain)){throw 'plain_candidate_rejected'}
    $taskRejected=$false
    try{Assert-IsolatedCandidatePath $taskLink $taskEvidence | Out-Null}
    catch{if($_.Exception.Message -eq 'candidate_reparse_point'){$taskRejected=$true}else{throw}}
    if(-not $taskRejected){throw 'junction_candidate_accepted'}
    'PASS: plain directory accepted; junction rejected'
}
finally{
    # Explicit non-recursive removal of our own empty fixture only.
    if(Test-Path -LiteralPath $taskLink){Remove-Item -LiteralPath $taskLink}
    foreach($taskPath in @($taskPlain,$taskEvidence,$taskOutside,$taskFixture)){
        if(Test-Path -LiteralPath $taskPath){Remove-Item -LiteralPath $taskPath}
    }
}

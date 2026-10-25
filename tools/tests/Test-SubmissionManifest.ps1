#requires -Version 7.0
param([string]$ModulePath = (Join-Path $PSScriptRoot '../signing/SubmissionManifest.psm1'))
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Import-Module $ModulePath -Force
$root = Join-Path ([IO.Path]::GetTempPath()) ('HostAgent-submission-manifest-test-' + [guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $root
$cases = [Collections.Generic.List[object]]::new()
$commit = 'a' * 40
$version = '1.2.8'

function Assert-ManifestTest([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw ('assertion_failed:' + $Message) }
}
function New-ValidManifest {
    return [ordered]@{
        format = 1; channel = 'unsigned-submission-preview'; signed = $false
        version = $version; sourceCommit = $commit; repository = 'Mina1399-web/PC-Agent'; runId = '40217705947'
        files = @([ordered]@{ path = 'Service/a.dll'; length = 17; sha256 = ('b' * 64) })
    }
}
function Write-ManifestFixture([object]$Data, [switch]$Raw) {
    $path = Join-Path $root ([guid]::NewGuid().ToString('N') + '.json')
    $json = if ($Raw) { [string]$Data } else { $Data | ConvertTo-Json -Depth 12 -Compress }
    [IO.File]::WriteAllText($path, $json, [Text.UTF8Encoding]::new($false))
    return @{ LiteralPath = $path; ExpectedSourceCommit = $commit; ExpectedVersion = $version; ExpectedManifestSha256 = (Get-FileHash -LiteralPath $path).Hash }
}
function Assert-ManifestRejected([scriptblock]$Action, [string]$ExpectedCode) {
    $code = $null
    try { $null = & $Action } catch { $code = $_.Exception.Message }
    Assert-ManifestTest ($code -ceq $ExpectedCode) ('expected ' + $ExpectedCode + '; observed ' + $code)
}
function Invoke-ManifestTest([string]$Name, [scriptblock]$Action) {
    try {
        $null = & $Action
        $cases.Add(@{ name = $Name; status = 'PASS' })
        Write-Host ('PASS ' + $Name)
    } catch {
        $cases.Add(@{ name = $Name; status = 'FAIL' })
        Write-Host ('FAIL ' + $Name + ': ' + $_.Exception.Message)
    }
}

try {
    # An absent field validation, wrong conversion, or unwanted property projection changes this public result.
    Invoke-ManifestTest 'valid_manifest_returns_only_reviewed_data' {
        $arguments = Write-ManifestFixture (New-ValidManifest)
        $data = Read-SubmissionManifest @arguments
        Assert-ManifestTest ($data.Format -eq 1 -and $data.Version -ceq '1.2.8' -and $data.SourceCommit -ceq $commit -and $data.RunId -ceq '40217705947') 'root projection'
        Assert-ManifestTest (@($data.Files).Count -eq 1 -and $data.Files[0].Path -ceq 'Service/a.dll' -and $data.Files[0].Length -eq 17 -and $data.Files[0].Sha256 -ieq ('b' * 64)) 'file projection'
        Assert-ManifestTest (($data.PSObject.Properties.Name | Sort-Object) -join ',' -ceq 'Files,Format,RunId,SourceCommit,Version') 'exact output root properties'
    }

    # Parsers that silently keep the last duplicate property would let reviewed values be reinterpreted.
    Invoke-ManifestTest 'duplicate_root_property_case_rejected' {
        $raw = (New-ValidManifest | ConvertTo-Json -Depth 12 -Compress).Replace('"format":1', '"format":1,"FORMAT":1')
        $arguments = Write-ManifestFixture $raw -Raw
        Assert-ManifestRejected { Read-SubmissionManifest @arguments } 'submission_manifest_duplicate_property'
    }
    Invoke-ManifestTest 'duplicate_file_property_case_rejected' {
        $raw = (New-ValidManifest | ConvertTo-Json -Depth 12 -Compress).Replace('"path":"Service/a.dll"', '"path":"Service/a.dll","PATH":"Service/b.dll"')
        $arguments = Write-ManifestFixture $raw -Raw
        Assert-ManifestRejected { Read-SubmissionManifest @arguments } 'submission_manifest_duplicate_property'
    }
    Invoke-ManifestTest 'duplicate_payload_path_case_rejected' {
        $manifest = New-ValidManifest
        $manifest.files += @{ path = 'SERVICE/A.DLL'; length = 0; sha256 = ('c' * 64) }
        $arguments = Write-ManifestFixture $manifest
        Assert-ManifestRejected { Read-SubmissionManifest @arguments } 'submission_manifest_duplicate_path'
    }

    Invoke-ManifestTest 'unknown_root_field_rejected' {
        $manifest = New-ValidManifest; $manifest.extra = $true
        $arguments = Write-ManifestFixture $manifest
        Assert-ManifestRejected { Read-SubmissionManifest @arguments } 'submission_manifest_root_invalid'
    }
    Invoke-ManifestTest 'unknown_file_field_rejected' {
        $manifest = New-ValidManifest; $manifest.files[0].permissions = 'write'
        $arguments = Write-ManifestFixture $manifest
        Assert-ManifestRejected { Read-SubmissionManifest @arguments } 'submission_manifest_file_invalid'
    }

    # All paths remain data-only relative ASCII names, never an escape or a self-hashing manifest.
    foreach ($badPath in @('../a.dll', 'a/../b.dll', './a.dll', '/a.dll', 'C:/a.dll', 'a//b.dll', 'manifest.json', 'MANIFEST.JSON', 'a/./b.dll', 'a\b.dll', 'ž.dll')) {
        Invoke-ManifestTest ('invalid_path_' + ($cases.Count + 1)) {
            $manifest = New-ValidManifest; $manifest.files[0].path = $badPath
            $arguments = Write-ManifestFixture $manifest
            Assert-ManifestRejected { Read-SubmissionManifest @arguments } 'submission_manifest_path_invalid'
        }
    }

    Invoke-ManifestTest 'anchor_checked_before_json_parsing' {
        $arguments = Write-ManifestFixture 'not-json' -Raw
        $arguments.ExpectedManifestSha256 = '0' * 64
        Assert-ManifestRejected { Read-SubmissionManifest @arguments } 'submission_manifest_hash_mismatch'
    }
    Invoke-ManifestTest 'anchored_invalid_json_rejected' {
        $arguments = Write-ManifestFixture 'not-json' -Raw
        Assert-ManifestRejected { Read-SubmissionManifest @arguments } 'submission_manifest_json_invalid'
    }

    foreach ($mutation in @(
        @{ field = 'sourceCommit'; value = ('c' * 40); code = 'submission_manifest_source_mismatch' },
        @{ field = 'version'; value = '1.2.9'; code = 'submission_manifest_version_mismatch' },
        @{ field = 'channel'; value = 'production'; code = 'submission_manifest_channel_invalid' },
        @{ field = 'signed'; value = $true; code = 'submission_manifest_signed_invalid' },
        @{ field = 'signed'; value = 'false'; code = 'submission_manifest_signed_invalid' },
        @{ field = 'repository'; value = 'other/PC-Agent'; code = 'submission_manifest_repository_mismatch' },
        @{ field = 'runId'; value = 123; code = 'submission_manifest_run_id_invalid' },
        @{ field = 'runId'; value = '123x'; code = 'submission_manifest_run_id_invalid' },
        @{ field = 'format'; value = 2; code = 'submission_manifest_root_invalid' }
    )) {
        Invoke-ManifestTest ('invalid_root_' + $mutation.field + '_' + ($cases.Count + 1)) {
            $manifest = New-ValidManifest; $manifest[$mutation.field] = $mutation.value
            $arguments = Write-ManifestFixture $manifest
            Assert-ManifestRejected { Read-SubmissionManifest @arguments } $mutation.code
        }
    }

    foreach ($badLength in @(-1, 0.5, '17', 1073741825)) {
        Invoke-ManifestTest ('invalid_length_' + ($cases.Count + 1)) {
            $manifest = New-ValidManifest; $manifest.files[0].length = $badLength
            $arguments = Write-ManifestFixture $manifest
            Assert-ManifestRejected { Read-SubmissionManifest @arguments } 'submission_manifest_length_invalid'
        }
    }
    Invoke-ManifestTest 'total_payload_over_2_gib_rejected' {
        $manifest = New-ValidManifest
        $manifest.files = @(@{ path = 'a'; length = 1GB; sha256 = ('b' * 64) }, @{ path = 'b'; length = 1GB; sha256 = ('c' * 64) }, @{ path = 'c'; length = 1; sha256 = ('d' * 64) })
        $arguments = Write-ManifestFixture $manifest
        Assert-ManifestRejected { Read-SubmissionManifest @arguments } 'submission_manifest_total_length_limit'
    }
    Invoke-ManifestTest 'exact_2_gib_and_zero_lengths_allowed' {
        $manifest = New-ValidManifest
        $manifest.files = @(@{ path = 'a'; length = 1GB; sha256 = ('b' * 64) }, @{ path = 'b'; length = 1GB; sha256 = ('c' * 64) }, @{ path = 'c'; length = 0; sha256 = ('d' * 64) })
        $arguments = Write-ManifestFixture $manifest
        $data = Read-SubmissionManifest @arguments
        Assert-ManifestTest (@($data.Files).Count -eq 3 -and $data.Files[2].Length -eq 0) 'inclusive limits'
    }
    Invoke-ManifestTest 'bad_file_hash_rejected' {
        $manifest = New-ValidManifest; $manifest.files[0].sha256 = ('z' * 64)
        $arguments = Write-ManifestFixture $manifest
        Assert-ManifestRejected { Read-SubmissionManifest @arguments } 'submission_manifest_file_hash_invalid'
    }
    Invoke-ManifestTest 'empty_files_rejected' {
        $manifest = New-ValidManifest; $manifest.files = @()
        $arguments = Write-ManifestFixture $manifest
        Assert-ManifestRejected { Read-SubmissionManifest @arguments } 'submission_manifest_file_count_invalid'
    }
    Invoke-ManifestTest 'over_10000_file_rows_rejected' {
        $manifest = New-ValidManifest
        $manifest.files = @(for ($i = 0; $i -lt 10001; $i++) { @{ path = ('f' + $i); length = 0; sha256 = ('b' * 64) } })
        $arguments = Write-ManifestFixture $manifest
        Assert-ManifestRejected { Read-SubmissionManifest @arguments } 'submission_manifest_file_count_invalid'
    }
    Invoke-ManifestTest 'over_4_mib_manifest_rejected' {
        $arguments = Write-ManifestFixture (' ' * (4MB + 1)) -Raw
        Assert-ManifestRejected { Read-SubmissionManifest @arguments } 'submission_manifest_size_limit'
    }
    Invoke-ManifestTest 'manifest_never_executes_embedded_commands' {
        $marker = Join-Path $root 'must-not-exist.txt'
        $manifest = New-ValidManifest
        $manifest.execute = ('Set-Content -LiteralPath "' + $marker + '" -Value executed')
        $arguments = Write-ManifestFixture $manifest
        Assert-ManifestRejected { Read-SubmissionManifest @arguments } 'submission_manifest_root_invalid'
        Assert-ManifestTest (-not (Test-Path -LiteralPath $marker)) 'no command execution'
    }

    $failures = @($cases | Where-Object status -CEQ 'FAIL')
    [pscustomobject]@{ status = $(if ($failures.Count) { 'FAIL' } else { 'PASS' }); tests = $cases.Count; failed = $failures.Count; scope = 'data_only_manifest_contract_no_payload_execution' } | ConvertTo-Json -Compress
    if ($failures.Count) { throw 'submission_manifest_regression_failed' }
} finally {
    $resolved = [IO.Path]::GetFullPath($root)
    $temp = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    if (-not $resolved.StartsWith($temp, [StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($resolved) -notmatch '^HostAgent-submission-manifest-test-[a-f0-9]{32}$') { throw 'manifest_test_cleanup_scope_mismatch' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}

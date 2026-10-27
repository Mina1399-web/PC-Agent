param([string]$ScriptPath = (Join-Path $PSScriptRoot '..\..\scripts\diagnostics\Read-InstalledDevConfirmationPlan.ps1'))
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$script:assertionCount = 0

function Assert-True([bool]$Value, [string]$Message) {
    if (-not $Value) { throw "assertion_failed:$Message" }
    $script:assertionCount++
}
function Assert-Code([scriptblock]$Action, [string]$Expected) {
    try { & $Action; throw "expected_failure_not_observed:$Expected" }
    catch {
        if ($_.Exception.Message -ne $Expected) { throw "wrong_failure_code:expected=$Expected;actual=$($_.Exception.Message)" }
        $script:assertionCount++
    }
}

$temp = Join-Path ([IO.Path]::GetTempPath()) ('hostagent-mode-test-' + [guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $temp
try {
    $payload = Join-Path $temp 'HostAgent.msi'
    [IO.File]::WriteAllText($payload, 'fixture only; never execute')
    $payloadHash = (Get-FileHash -LiteralPath $payload -Algorithm SHA256).Hash
    $payloadLength = (Get-Item -LiteralPath $payload).Length
    $sourceCommit = 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa'
    $version = '1.2.9'
    $manifestPath = Join-Path $temp 'manifest.psd1'
    $manifest = "@{Format=1;Version='$version';SourceCommit='$sourceCommit';Files=@(@{Path='HostAgent.msi';Sha256='$payloadHash';Length=$payloadLength})}"
    [IO.File]::WriteAllText($manifestPath, $manifest, [Text.Encoding]::UTF8)
    $manifestHash = (Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash

    if (-not (Test-Path -LiteralPath $ScriptPath -PathType Leaf)) {
        throw 'mode_handoff_library_missing'
    }
    . $ScriptPath -PackagePath $temp -ExpectedManifestSha256 $manifestHash -ExpectedSourceCommit $sourceCommit -ExpectedVersion $version

    # Behavior: an altered manifest anchor must fail closed before any package identity is trusted.
    Assert-Code { Read-ModeManifest -LiteralPath $manifestPath -ExpectedManifestSha256 ('0' * 64) -ExpectedSourceCommit $sourceCommit -ExpectedVersion $version } 'manifest_hash_mismatch'
    $parsed = Read-ModeManifest -LiteralPath $manifestPath -ExpectedManifestSha256 $manifestHash -ExpectedSourceCommit $sourceCommit -ExpectedVersion $version
    Assert-True ($parsed.Version -eq $version -and $parsed.Files.Count -eq 1) 'valid_manifest_identity'

    # Behavior: a data manifest must not evaluate PowerShell expressions.
    $sentinel = Join-Path $temp 'manifest-evaluated.txt'
    $unsafeManifest = "@{Format=1;Version='$version';SourceCommit=(Set-Content -LiteralPath '$sentinel' -Value 'executed');Files=@()}"
    [IO.File]::WriteAllText($manifestPath, $unsafeManifest, [Text.Encoding]::UTF8)
    $unsafeHash = (Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash
    Assert-Code { Read-ModeManifest -LiteralPath $manifestPath -ExpectedManifestSha256 $unsafeHash -ExpectedSourceCommit $sourceCommit -ExpectedVersion $version } 'release_manifest_invalid'
    Assert-True (-not (Test-Path -LiteralPath $sentinel)) 'manifest_expression_not_executed'
    Assert-True ((Get-DisableRemediationMarkerPath) -ceq 'C:\ProgramData\HostAgent.Helper\disable-remediation') 'marker_path_boundary'
    $markerEnabled=[pscustomobject]@{ApprovedVm=$true;Elevated=$true;PackageValid=$true;InstalledValid=$true;ServicesValid=$true;ConfigurationValid=$true;ConfigurationAvailable=$true;RuntimeReadbackValid=$true;FreeMemoryMiB=900;FreeDiskGiB=12;Mode='Shadow';ReadOnly=$true;HelperReadOnly=$true;HelperShadow=$true;Pushover=$false;Heartbeat=$false;NetworkProbes=$false;ServiceRegistrations=0;ProcessRegistrations=0;ApproverCapacityAvailable=$true;ApproverPresent=$false;DisableMarkerPresent=$true}
    Assert-True ((Get-ModeHandoffDecision -Facts $markerEnabled).Blockers -contains 'disable_remediation_marker_present') 'marker_presence_blocks_manual_transition'
    $duplicateJson=[System.Text.Json.JsonDocument]::Parse('{"mode":"Shadow","mode":"Confirmation"}')
    try { Assert-Code { Assert-NoDuplicateJsonPropertyNames $duplicateJson.RootElement } 'configuration_duplicate_key' } finally { $duplicateJson.Dispose() }

    # Exercise the actual JSON and ACL reader on a private fixture, including Int64 values produced by PowerShell 7.
    $helperJsonPath=Join-Path $temp 'helper-config.json'
    $validHelperJson='{"readOnly":true,"shadow":true,"killSwitch":false,"executionTimeoutSeconds":10,"services":[],"processes":[],"approverSids":[]}'
    $utf8NoBom=[Text.UTF8Encoding]::new($false)
    [IO.File]::WriteAllText($helperJsonPath,$validHelperJson,$utf8NoBom)
    $currentSid=[Security.Principal.WindowsIdentity]::GetCurrent().User.Value
    $trustedFixtureSids=@($currentSid,'S-1-5-18','S-1-5-32-544')
    $fixtureAcl=Get-Acl -LiteralPath $helperJsonPath
    $fixtureAcl.SetAccessRuleProtection($true,$false)
    foreach($oldRule in @($fixtureAcl.Access)){[void]$fixtureAcl.RemoveAccessRuleAll($oldRule)}
    foreach($sidText in $trustedFixtureSids){$sid=[Security.Principal.SecurityIdentifier]::new($sidText);$rule=[Security.AccessControl.FileSystemAccessRule]::new($sid,[Security.AccessControl.FileSystemRights]::FullControl,[Security.AccessControl.AccessControlType]::Allow);$fixtureAcl.AddAccessRule($rule)}
    Set-Acl -LiteralPath $helperJsonPath -AclObject $fixtureAcl
    $allowedFields=@('readOnly','shadow','killSwitch','executionTimeoutSeconds','services','processes','approverSids')
    $parsedHelper=Read-ProtectedModeJson -Path $helperJsonPath -AllowedFields $allowedFields -TrustedSids $trustedFixtureSids -Kind Helper
    Assert-True ($parsedHelper.executionTimeoutSeconds -in 10,60) 'real_json_reader_accepts_ps7_int64'
    $serviceJsonPath=Join-Path $temp 'service-config.json'
    $serviceJson='{"mode":"Shadow","readOnly":true,"killSwitch":false,"collectionIntervalSeconds":60,"heartbeatIntervalSeconds":60,"requestTimeoutSeconds":10,"enablePushover":false,"enableHeartbeat":false,"enableNetworkProbes":false}'
    [IO.File]::WriteAllText($serviceJsonPath,$serviceJson,$utf8NoBom)
    $serviceAcl=Get-Acl -LiteralPath $serviceJsonPath; $serviceAcl.SetAccessRuleProtection($true,$false)
    foreach($oldRule in @($serviceAcl.Access)){[void]$serviceAcl.RemoveAccessRuleAll($oldRule)}
    foreach($sidText in $trustedFixtureSids){$sid=[Security.Principal.SecurityIdentifier]::new($sidText);$rule=[Security.AccessControl.FileSystemAccessRule]::new($sid,[Security.AccessControl.FileSystemRights]::FullControl,[Security.AccessControl.AccessControlType]::Allow);$serviceAcl.AddAccessRule($rule)}
    Set-Acl -LiteralPath $serviceJsonPath -AclObject $serviceAcl
    $parsedService=Read-ProtectedModeJson -Path $serviceJsonPath -AllowedFields @('mode','readOnly','killSwitch','collectionIntervalSeconds','heartbeatIntervalSeconds','requestTimeoutSeconds','enablePushover','enableHeartbeat','enableNetworkProbes') -TrustedSids $trustedFixtureSids -Kind Service
    Assert-True ($parsedService.collectionIntervalSeconds -eq 60 -and $parsedService.heartbeatIntervalSeconds -eq 60) 'real_json_reader_accepts_service_interval_int64'
    [IO.File]::WriteAllText($helperJsonPath,$validHelperJson.Replace('10,"services"','10.5,"services"'),$utf8NoBom)
    Assert-Code { Read-ProtectedModeJson -Path $helperJsonPath -AllowedFields $allowedFields -TrustedSids $trustedFixtureSids -Kind Helper } 'configuration_invalid'
    [IO.File]::WriteAllText($helperJsonPath,$validHelperJson.Replace('10,"services"','"10","services"'),$utf8NoBom)
    Assert-Code { Read-ProtectedModeJson -Path $helperJsonPath -AllowedFields $allowedFields -TrustedSids $trustedFixtureSids -Kind Helper } 'configuration_invalid'

    # Validate HostAgentDataDirectory ACL semantics in memory. Helper's service SID is SYSTEM, so only 2 unique rules exist.
    $memoryDirectoryAcl=[Security.AccessControl.DirectorySecurity]::new()
    $memoryDirectoryAcl.SetAccessRuleProtection($true,$false)
    $systemSid=[Security.Principal.SecurityIdentifier]::new('S-1-5-18')
    $adminsSid=[Security.Principal.SecurityIdentifier]::new('S-1-5-32-544')
    $memoryDirectoryAcl.SetOwner($systemSid)
    $inherit=[Security.AccessControl.InheritanceFlags]::ContainerInherit -bor [Security.AccessControl.InheritanceFlags]::ObjectInherit
    foreach($sid in @($systemSid,$adminsSid)){$memoryDirectoryAcl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($sid,[Security.AccessControl.FileSystemRights]::FullControl,$inherit,[Security.AccessControl.PropagationFlags]::None,[Security.AccessControl.AccessControlType]::Allow))}
    Assert-True (Test-ModeDirectorySecurity -Security $memoryDirectoryAcl -ServiceSid 'S-1-5-18') 'memory_directory_acl_deduplicates_system'

    # A successful ACL assertion must not emit booleans into Get-InstalledModeFacts' single-object result.
    $directoryFixture=Join-Path $temp 'protected-directory'
    [void][IO.Directory]::CreateDirectory($directoryFixture)
    $directoryAcl=[Security.AccessControl.DirectorySecurity]::new()
    $directoryAcl.SetOwner([Security.Principal.SecurityIdentifier]::new($currentSid))
    $directoryAcl.SetAccessRuleProtection($true,$false)
    foreach($sidText in $trustedFixtureSids){
        $directoryAcl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new(
            [Security.Principal.SecurityIdentifier]::new($sidText),[Security.AccessControl.FileSystemRights]::FullControl,
            $inherit,[Security.AccessControl.PropagationFlags]::None,[Security.AccessControl.AccessControlType]::Allow))
    }
    Set-Acl -LiteralPath $directoryFixture -AclObject $directoryAcl
    $validationOutput=@(Test-ModeDataDirectoryAcl -Path $directoryFixture -ServiceSid $currentSid)
    Assert-True ($validationOutput.Count -eq 0) 'acl_assertion_does_not_pollute_facts_pipeline'

    $facts = [pscustomobject]@{
        ApprovedVm = $true; Elevated = $true; PackageValid = $true; InstalledValid = $true; ServicesValid = $true
        ConfigurationValid = $true; ConfigurationAvailable = $true; RuntimeReadbackValid = $true; FreeMemoryMiB = 900.0; FreeDiskGiB = 12.0
        Mode = 'Shadow'; ReadOnly = $true; ServiceKillSwitch = $false; Pushover = $false; Heartbeat = $false; NetworkProbes = $false
        HelperReadOnly = $true; HelperShadow = $true; HelperKillSwitch = $false; ServiceRegistrations = 0; ProcessRegistrations = 0
        ApproverPresent = $false; ApproverCapacityAvailable = $true; DisableMarkerPresent = $false; JournalExists = $false
        AllowlistFingerprint = 'fixture-fingerprint'
    }

    # Behavior: default plan must never invoke the configuration callback.
    $planned = Get-ModeReadOnlyPlan -Facts $facts
    Assert-True ($planned.Outcome -eq 'planned' -and -not $planned.ConfigurationPerformed) 'default_read_only_plan'
    $manualHandoff = Get-ModeManualHandoff -Core $planned -Execute:$true
    Assert-True ($manualHandoff.Outcome -eq 'manual_step_required' -and $manualHandoff.FailureCode -eq 'automation_disabled_for_recovery_safety' -and -not $manualHandoff.ConfigurationPerformed -and $manualHandoff.ManualNextStep.Count -eq 2) 'execute_is_manual_and_not_accepted'

    # Behavior: an existing allowlist blocks activation without disclosing target identities or invoking CLI.
    $registered = $facts.PSObject.Copy(); $registered.ServiceRegistrations = 1
    $blocked = Get-ModeReadOnlyPlan -Facts $registered
    Assert-True ($blocked.Outcome -eq 'blocked' -and $blocked.Blockers -contains 'review_existing_allowlist_before_enable') 'allowlist_review_gate'
    $registered | Add-Member -NotePropertyName SecretTargetIdentity -NotePropertyValue 'SENSITIVE-TEST-TARGET'
    Assert-True ((ConvertTo-Json -InputObject $blocked -Compress) -notmatch 'SENSITIVE-TEST-TARGET') 'blocked_result_does_not_disclose_target'

    # Behavior: resource thresholds are exact; 768 MiB is not a valid start, while 769 MiB is.
    $lowMemory = $facts.PSObject.Copy(); $lowMemory.FreeMemoryMiB = 768.0
    $low = Get-ModeHandoffDecision -Facts $lowMemory
    Assert-True ($low.Blockers -contains 'initial_free_memory_not_above_768_mib') 'memory_start_boundary'
    $hardMemory = $facts.PSObject.Copy(); $hardMemory.FreeMemoryMiB = 512.0
    $hard = Get-ModeHandoffDecision -Facts $hardMemory
    Assert-True ($hard.Blockers -contains 'hard_free_memory_floor_512_mib') 'memory_hard_boundary'
    $lowDisk = $facts.PSObject.Copy(); $lowDisk.FreeDiskGiB = 9.99
    Assert-True ((Get-ModeHandoffDecision -Facts $lowDisk).Blockers -contains 'disk_free_below_10_gib') 'disk_boundary'

    # Behavior: already-confirmation state is verified without a second restart/configuration call.
    $confirmed = $facts.PSObject.Copy(); $confirmed.Mode = 'Confirmation'; $confirmed.ReadOnly = $false
    $confirmed.HelperReadOnly = $false; $confirmed.HelperShadow = $false; $confirmed.ApproverPresent = $true
    $already = Get-ModeReadOnlyPlan -Facts $confirmed
    Assert-True ($already.Outcome -eq 'mode_only_accepted' -and -not $already.ConfigurationPerformed) 'already_confirmed_readback_acceptance'
    $unapproved = $confirmed.PSObject.Copy(); $unapproved.ApproverPresent = $false
    Assert-True ((Get-ModeHandoffDecision -Facts $unapproved).Blockers -contains 'current_operator_not_approved') 'already_confirmation_requires_approver'
    $noRuntimeReadback = $confirmed.PSObject.Copy(); $noRuntimeReadback.RuntimeReadbackValid = $false
    Assert-True ((Get-ModeHandoffDecision -Facts $noRuntimeReadback).Blockers -contains 'runtime_status_readback_required') 'already_confirmation_requires_runtime_readback'

    # Behavior: the caller's approved allowlist and kill-switch states must remain identical after CLI.
    $after = $facts.PSObject.Copy(); $after.HelperKillSwitch = $true
    Assert-True (-not (Test-ModePreservedState -Before $facts -After $after)) 'kill_switch_state_comparison'
    $after = $facts.PSObject.Copy(); $after.AllowlistFingerprint = 'changed-without-count-change'
    Assert-True (-not (Test-ModePreservedState -Before $facts -After $after)) 'allowlist_fingerprint_tamper_comparison'

    "PASS installed_dev_confirmation_behavioral_checks=$script:assertionCount"
} finally {
    if (Test-Path -LiteralPath $temp) {
        $resolvedTemp=[IO.Path]::GetFullPath((Resolve-Path -LiteralPath $temp).Path).TrimEnd('\','/')
        $resolvedTempRoot=[IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\','/')
        $leaf=[IO.Path]::GetFileName($resolvedTemp)
        if (-not $resolvedTemp.StartsWith($resolvedTempRoot+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase) -or $leaf -notmatch '^hostagent-mode-test-[0-9a-f]{32}$') { throw 'fixture_cleanup_boundary_rejected' }
        Assert-NoReparsePath $resolvedTemp
        Remove-Item -LiteralPath $resolvedTemp -Recurse -Force
    }
}

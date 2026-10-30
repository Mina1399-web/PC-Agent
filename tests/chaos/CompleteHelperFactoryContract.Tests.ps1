$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# Regression: asynchronous file-copy completion must never become a factory
# result. Windows PowerShell 5.1 exposes VoidTaskResult from GetResult().
# Also exercise a noisy copy boundary: the factory must return only its three
# owned target records even if a mutation command emits incidental output.
# Native service, SID translation and ACL application are the only OS doubles.
# Tree enumeration, async copy, manifests, hashes, canaries, in-memory security
# descriptors, target validation and cleanup orchestration remain real.
$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$modulePath = Join-Path $repo 'scripts\chaos\CompleteHelperTargets.psm1'
$safetyPath = Join-Path $repo 'scripts\chaos\IsolatedScmSafety.psm1'
$computer = Get-CimInstance Win32_ComputerSystem
$machine = Get-CimInstance Win32_ComputerSystemProduct
if ($env:COMPUTERNAME -cne 'AGENTPC' -or $computer.Model -cne 'VirtualBox' -or
    $machine.UUID -ine '0bb543d1-7bb0-43ed-9c94-596d63ef3310' -or
    (Get-Service VBoxService).Status -ne 'Running') { throw 'approved_dev_vm_required' }

function Read-TaskFunctions {
    param([string]$Path)
    $tokens = $null; $parseErrors = $null
    $ast = [Management.Automation.Language.Parser]::ParseFile($Path, [ref]$tokens, [ref]$parseErrors)
    if (@($parseErrors).Count) { throw 'test_dependency_parse_failed' }
    return @($ast.FindAll({ param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] }, $false))
}

$definitions = @(Read-TaskFunctions $modulePath)
$factory = @($definitions | Where-Object Name -eq 'New-CompleteHelperTargets')[0].Extent.Text
$sidBoundary = "([Security.Principal.NTAccount]::new('NT SERVICE',`$target.Name)).Translate([Security.Principal.SecurityIdentifier]).Value"
if (($factory.Split(@($sidBoundary), [StringSplitOptions]::None)).Count -ne 2) {
    throw 'exact_sid_translation_boundary_not_found'
}
$factory = $factory.Replace($sidBoundary, '(Get-TaskServiceSid $target.Name)')
$originalPlan = @($definitions | Where-Object Name -eq 'Get-CompleteHelperTargetPlan')[0].Extent.Text.Replace(
    'function Get-CompleteHelperTargetPlan', 'function Get-OriginalCompleteHelperTargetPlan')
$productionFunctions = @($definitions | Where-Object Name -ne 'New-CompleteHelperTargets' | ForEach-Object { $_.Extent.Text }) -join "`r`n"
$safetyFunctions = @(Read-TaskFunctions $safetyPath | Where-Object Name -in @(
    'Get-IsolatedScmAclPolicy', 'Test-IsolatedScmPackageRootAcl') | ForEach-Object { $_.Extent.Text }) -join "`r`n"
$operatorSid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
$fixtureRoot = Join-Path $env:LOCALAPPDATA ('HostAgent-DEV-evidence\helper-factory-contract-' + [guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($fixtureRoot)
$candidatePath = Join-Path $fixtureRoot 'candidate'
[void][IO.Directory]::CreateDirectory($candidatePath)
$entries = [Collections.Generic.List[object]]::new()
$names = @('HostAgent.PipeProbe.exe', 'HostAgent.PipeProbe.dll') + @(1..73 | ForEach-Object { 'payload\file-' + $_ + '.fixture' })
foreach ($relative in $names) {
    $file = Join-Path $candidatePath $relative
    [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($file))
    [IO.File]::WriteAllBytes($file, [Text.Encoding]::UTF8.GetBytes('inert test fixture ' + $relative))
    $entries.Add([pscustomobject]@{ path = $relative; sha256 = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash })
}
$manifestPath = Join-Path $candidatePath 'candidate-manifest.json'
[IO.File]::WriteAllText($manifestPath, ([pscustomobject]@{ format = 1; files = $entries.ToArray() } | ConvertTo-Json -Depth 5))
$candidate = [pscustomobject]@{ Path = $candidatePath; ManifestSha256 = (Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash }

$boundaryStubs = @'
function Assert-TaskOwnedPath {
    param([string]$Path)
    $full = [IO.Path]::GetFullPath($Path)
    if (-not $full.StartsWith($script:TaskRoot.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) {
        throw 'test_attempted_path_outside_owned_fixture'
    }
}
function Get-CompleteHelperTargetPlan {
    param($Candidate, [string[]]$RunIds, [string]$OperatorSid)
    $result = @(Get-OriginalCompleteHelperTargetPlan $Candidate $RunIds $OperatorSid)
    foreach ($target in $result) {
        $target.Root = Join-Path $script:TaskRoot ('HostAgent-DEV-SCM-' + $target.RunId)
        $target.App = Join-Path $target.Root 'app'; $target.Data = Join-Path $target.Root 'data'
        $target.Image = Join-Path $target.App 'HostAgent.PipeProbe.exe'
        $target.BinaryPath = '"' + $target.Image + '" isolated-complete-target ' + $target.RunId + ' ' + $OperatorSid
        Assert-TaskOwnedPath $target.Root
    }
    if (($RunIds -join ',') -ceq ($script:TaskRunIds -join ',')) { $script:TaskFactoryTargets = $result }
    return $result
}
function Assert-CompleteHelperEnvironment {
    param([string]$OperatorSid)
    if ($OperatorSid -cne $script:TaskOperatorSid) { throw 'test_operator_identity_mismatch' }
}
function Get-TaskServiceSid {
    param([string]$Name)
    if ($Name -cnotmatch '\AHostAgentDevScm_[0-9a-f]{32}\z') { throw 'test_sid_scope_rejected' }
    return 'S-1-5-80-1-2-3-4-5'
}
function Set-Acl {
    param([string]$LiteralPath, [Security.AccessControl.FileSystemSecurity]$AclObject, $ErrorAction)
    Assert-TaskOwnedPath $LiteralPath
    if (-not $AclObject.AreAccessRulesProtected -or
        $AclObject.GetOwner([Security.Principal.SecurityIdentifier]).Value -cne 'S-1-5-32-544') {
        throw 'in_memory_acl_contract_failed'
    }
    $script:TaskAcls[$LiteralPath] = $AclObject
}
function Get-Acl {
    param([string]$LiteralPath)
    Assert-TaskOwnedPath $LiteralPath
    if (-not $script:TaskAcls.ContainsKey($LiteralPath)) { throw 'test_acl_not_applied' }
    return $script:TaskAcls[$LiteralPath]
}
function Get-CimInstance {
    param([string]$ClassName, [string]$Filter, $OperationTimeoutSec, $ErrorAction)
    if ($ClassName -ceq 'Win32_Service') {
        if ($Filter -ceq "Name='HostAgent'") { return [pscustomobject]@{ ProcessId = 900001 } }
        if ($Filter -cnotmatch "\AName='(HostAgentDevScm_[0-9a-f]{32})'\z") { throw 'test_service_query_outside_scope' }
        return $script:TaskServices[$Matches[1]]
    }
    if ($ClassName -ceq 'Win32_Process' -and $Filter -cmatch '\AProcessId=([0-9]+)\z') {
        $processId = [int]$Matches[1]
        foreach ($registration in $script:TaskServices.Values) {
            if ($registration.ProcessId -eq $processId) {
                return [pscustomobject]@{ ExecutablePath = $registration.Image; CommandLine = $registration.PathName; SessionId = 0 }
            }
        }
        throw 'test_process_query_outside_scope'
    }
    throw 'test_unexpected_cim_operation'
}
function Get-ItemProperty {
    param([string]$LiteralPath, $ErrorAction)
    $prefix = 'Registry::HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\'
    if (-not $LiteralPath.StartsWith($prefix, [StringComparison]::Ordinal) -or
        -not $script:TaskServices.ContainsKey($LiteralPath.Substring($prefix.Length))) { throw 'test_registry_query_outside_scope' }
    return [pscustomobject]@{ Type = 16 }
}
function Invoke-CompleteHelperNative {
    param($Target, [string]$Operation, [scriptblock]$Guard)
    Assert-CompleteHelperTargetMetadata $Target
    Assert-TaskOwnedPath $Target.Root
    & $Guard | Out-Null
    $script:TaskNativeOperations.Add($Target.Name + ':' + $Operation)
    if ($Operation -eq 'create') {
        if ($script:TaskServices.ContainsKey($Target.Name)) { throw 'test_duplicate_native_create' }
        $script:TaskServices[$Target.Name] = [pscustomobject]@{
            Name = $Target.Name; PathName = $Target.BinaryPath; StartName = 'NT SERVICE\' + $Target.Name
            State = 'Stopped'; StartMode = 'Manual'; ProcessId = 0; Image = $Target.Image
        }
    } else {
        $registration = Assert-CompleteHelperRegistration $Target
        switch ($Operation) {
            start { $registration.State = 'Running'; $registration.ProcessId = 700000 + $script:TaskServices.Count }
            stop { $registration.State = 'Stopped'; $registration.ProcessId = 0 }
            disable { $registration.StartMode = 'Disabled' }
            default { throw 'test_native_operation_outside_scope' }
        }
    }
    $Target.NativeOperations.Add([pscustomobject]@{ Operation = $Operation; ExitCode = 0 })
}
function Invoke-TaskFactoryContract {
    $guard = { $script:TaskGuardCalls++ }
    $owned = $null
    $output = @(New-CompleteHelperTargets $script:TaskCandidate $script:TaskRunIds $script:TaskOperatorSid $guard -TargetState ([ref]$owned))
    $errors = [Collections.Generic.List[string]]::new()
    if ($output.Count -ne 3) { $errors.Add('factory_output_count_expected_3_actual_' + $output.Count) }
    if (@($owned).Count -ne 3) { $errors.Add('factory_owned_state_not_preserved') }
    if ($output.Count -eq 3) { Assert-CompleteHelperFactoryResult -ReturnedTargets $output -OwnedTargets $owned -RunIds $script:TaskRunIds }
    $records = @($output | Where-Object { $_ -is [pscustomobject] -and $_.PSObject.Properties['RunId'] })
    if ($records.Count -ne 3) { $errors.Add('factory_target_record_count_mismatch') }
    foreach ($index in 0..2) {
        $target = $script:TaskFactoryTargets[$index]
        if ($records.Count -eq 3 -and -not [object]::ReferenceEquals($target, $records[$index])) { $errors.Add('factory_record_identity_changed') }
        if (-not $target.CopyCompleted -or -not $target.Created -or -not $target.RootCreated) { $errors.Add('factory_incomplete_target') }
        $files = @(Get-CompleteHelperTreeFiles $target.App $guard)
        if ($files.Count -ne 78) { $errors.Add('copied_inventory_expected_78_actual_' + $files.Count) }
        Assert-CompleteHelperManifest $target.App $target.ManifestSha256 $guard -WithCanaries | Out-Null
    }
    # When the result is polluted, still exercise real cleanup using the exact
    # factory-owned records; this operates exclusively on the in-memory SCM.
    $cleanupTargets = if ($output.Count -eq 3) { $output } else { $script:TaskFactoryTargets }
    $cleanup = @(Confirm-CompleteHelperTargets $cleanupTargets $guard)
    if ($cleanup.Count -ne 3) { $errors.Add('cleanup_output_count_mismatch') }
    foreach ($item in $cleanup) {
        if ($item.State -cne 'Stopped' -or $item.StartMode -cne 'Disabled' -or $item.ProcessId -ne 0 -or
            -not $item.Quiescent -or -not $item.PackageVerified -or -not $item.Preserved) { $errors.Add('cleanup_quiescence_not_proved') }
    }
    if ($script:TaskGuardCalls -lt 228 -or $script:TaskAcls.Count -lt 3 * 78) { $errors.Add('real_guard_or_acl_construction_not_exercised') }
    return [pscustomobject]@{ mode = $script:TaskMode; outputCount = $output.Count; targetRecords = $records.Count
        cleanupRecords = $cleanup.Count; copiedFiles = 228; guardCalls = $script:TaskGuardCalls
        realServiceOperations = 0; realAclMutations = 0; errors = $errors.ToArray() }
}
Export-ModuleMember -Function Invoke-TaskFactoryContract
'@

$results = [Collections.Generic.List[object]]::new()
foreach ($mode in @('real_async_copy', 'noisy_copy_boundary')) {
    $modeRoot = Join-Path $fixtureRoot $mode
    [void][IO.Directory]::CreateDirectory($modeRoot)
    $ids = @((1..3 | ForEach-Object { [guid]::NewGuid().ToString('N') }))
    $prefix = @'
param($TaskRootValue, $TaskCandidateValue, $TaskIdsValue, $TaskOperatorValue, $TaskModeValue)
Set-StrictMode -Version Latest
$script:TaskRoot = $TaskRootValue
$script:TaskCandidate = $TaskCandidateValue
$script:TaskRunIds = $TaskIdsValue
$script:TaskOperatorSid = $TaskOperatorValue
$script:TaskMode = $TaskModeValue
$script:TaskServices = @{}
$script:TaskAcls = @{}
$script:TaskNativeOperations = [Collections.Generic.List[string]]::new()
$script:TaskFactoryTargets = @()
$script:TaskGuardCalls = 0
'@
    $noisyOverride = ''
    if ($mode -ceq 'noisy_copy_boundary') {
        $copy = @($definitions | Where-Object Name -eq 'Copy-CompleteHelperFile')[0].Extent.Text.Replace(
            'function Copy-CompleteHelperFile', 'function Copy-ActualCompleteHelperFile')
        $noisyOverride = $copy + @'

function Copy-CompleteHelperFile {
    param([string]$Source, [string]$Destination, [scriptblock]$Guard)
    Copy-ActualCompleteHelperFile $Source $Destination $Guard
    'incidental-copy-command-output'
}
'@
    }
    $moduleText = $prefix + "`r`n" + $productionFunctions + "`r`n" + $safetyFunctions + "`r`n" + $originalPlan + "`r`n" +
        $factory + "`r`n" + $noisyOverride + "`r`n" + $boundaryStubs
    $testModule = New-Module -Name ('HelperFactoryContract_' + [guid]::NewGuid().ToString('N')) -ScriptBlock ([scriptblock]::Create($moduleText)) `
        -ArgumentList $modeRoot, $candidate, $ids, $operatorSid, $mode
    try { $results.Add((& $testModule { Invoke-TaskFactoryContract })) }
    finally { Remove-Module $testModule -ErrorAction SilentlyContinue }
}
$results.ToArray() | ConvertTo-Json -Depth 5
[pscustomobject]@{ suite = 'complete-helper-factory-contract'; fixtureDirectory = $fixtureRoot
    passed = @($results | Where-Object { $_.errors.Count -eq 0 }).Count
    failed = @($results | Where-Object { $_.errors.Count -gt 0 }).Count } | ConvertTo-Json -Compress
if (@($results | Where-Object { $_.errors.Count -gt 0 }).Count) { throw 'helper_factory_contract_failed' }

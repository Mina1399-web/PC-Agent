Set-StrictMode -Version Latest

function Get-IsolatedScmAclPolicy {
    [CmdletBinding()]
    param([ValidateSet('PackageRoot','App','Data')][string]$Scope)

    switch ($Scope) {
        'PackageRoot' { return [pscustomobject]@{ ServiceRights = 'None'; AdminRights = 'FullControl'; SystemRights = 'FullControl' } }
        'App'         { return [pscustomobject]@{ ServiceRights = 'ReadAndExecute'; AdminRights = 'FullControl'; SystemRights = 'FullControl' } }
        'Data'        { return [pscustomobject]@{ ServiceRights = 'FullControl'; AdminRights = 'FullControl'; SystemRights = 'FullControl' } }
    }
}

function Get-IsolatedScmCreateArguments {
    param([string]$ServiceName, [string]$BinaryPath, [string]$DisplayName)
    @('create', $ServiceName, 'binPath=', $BinaryPath, 'start=', 'demand',
        'obj=', "NT SERVICE\$ServiceName", 'type=', 'own', 'DisplayName=', $DisplayName)
}

function ConvertTo-IsolatedScmNativeCommandLine {
    param([AllowEmptyCollection()][string[]]$Arguments)
    # Windows native argv quoting, independent of PowerShell's legacy binder.
    $encoded = foreach ($argument in $Arguments) {
        $builder = [Text.StringBuilder]::new()
        [void]$builder.Append('"')
        $slashes = 0
        foreach ($character in ([string]$argument).ToCharArray()) {
            if ($character -eq '\') { $slashes++; continue }
            if ($character -eq '"') {
                [void]$builder.Append(('\' * (2 * $slashes + 1)))
            }
            elseif ($slashes -gt 0) { [void]$builder.Append(('\' * $slashes)) }
            [void]$builder.Append($character)
            $slashes = 0
        }
        if ($slashes -gt 0) { [void]$builder.Append(('\' * (2 * $slashes))) }
        [void]$builder.Append('"')
        $builder.ToString()
    }
    [string]::Join(' ', [string[]]$encoded)
}

function Test-IsolatedScmPackageRootAcl {
    param([Security.AccessControl.DirectorySecurity]$Security)
    if (-not $Security.AreAccessRulesProtected) { return $false }
    $owner = $Security.GetOwner([Security.Principal.SecurityIdentifier])
    if (-not $owner -or $owner.Value -ne 'S-1-5-32-544') { return $false }
    $rules = @($Security.GetAccessRules($true, $true, [Security.Principal.SecurityIdentifier]))
    if ($rules.Count -ne 2) { return $false }
    $expected = @('S-1-5-32-544', 'S-1-5-18')
    $inheritance = [Security.AccessControl.InheritanceFlags]::ContainerInherit -bor [Security.AccessControl.InheritanceFlags]::ObjectInherit
    foreach ($rule in $rules) {
        if ($rule.IsInherited -or $rule.AccessControlType -ne 'Allow' -or
            $expected -notcontains $rule.IdentityReference.Value -or
            $rule.FileSystemRights -ne [Security.AccessControl.FileSystemRights]::FullControl -or
            $rule.InheritanceFlags -ne $inheritance -or $rule.PropagationFlags -ne 'None') { return $false }
    }
    foreach ($sid in $expected) { if ($rules.IdentityReference.Value -notcontains $sid) { return $false } }
    return $true
}

function Remove-IsolatedScmPackageAfterWatchdog {
    param([scriptblock]$StopWatchdog, [scriptblock]$RemovePackage)
    & $StopWatchdog
    return [bool](& $RemovePackage)
}

function Test-IsolatedWatchdogStopEvidence {
    param($Marker, [string]$RunId, [int]$WatchdogExit, [int]$PressureExit, [string]$ServiceState, [bool]$HardAbort)
    $parsedId = [guid]::Empty
    if (-not [guid]::TryParseExact($RunId, 'N', [ref]$parsedId) -or $parsedId -eq [guid]::Empty -or -not $Marker) { return $false }
    try {
        return ($WatchdogExit -eq 73 -and $PressureExit -eq 0 -and -not $HardAbort -and
            $ServiceState -eq 'Stopped' -and $Marker.runId -ceq $RunId -and
            $Marker.serviceName -ceq ('HostAgentDevScm_' + $RunId) -and
            $Marker.floorMiB -eq 768 -and $Marker.stopConfirmed -eq $true -and
            $Marker.action -ceq 'stop_isolated_test_service' -and
            [double]$Marker.availableMemoryMiB -gt 512 -and [double]$Marker.availableMemoryMiB -lt 768)
    } catch { return $false }
}

Export-ModuleMember -Function Get-IsolatedScmAclPolicy, Get-IsolatedScmCreateArguments, ConvertTo-IsolatedScmNativeCommandLine, Test-IsolatedScmPackageRootAcl, Remove-IsolatedScmPackageAfterWatchdog, Test-IsolatedWatchdogStopEvidence

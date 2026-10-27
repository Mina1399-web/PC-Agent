param(
    [Parameter(Mandatory)][string]$PackagePath,
    [Parameter(Mandatory)][string]$ExpectedManifestSha256,
    [Parameter(Mandatory)][string]$ExpectedSourceCommit,
    [Parameter(Mandatory)][string]$ExpectedVersion,
    [switch]$Execute
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$script:ModeDisableRemediationMarkerPath = 'C:\ProgramData\HostAgent.Helper\disable-remediation'

function Get-DisableRemediationMarkerPath { $script:ModeDisableRemediationMarkerPath }
function Assert-NoReparsePath {
    param([Parameter(Mandatory)][string]$Path)
    $cursor=[IO.Path]::GetFullPath($Path)
    while (-not [string]::IsNullOrWhiteSpace($cursor)) {
        $item=Get-Item -LiteralPath $cursor -Force -ErrorAction Stop
        if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'reparse_path_rejected' }
        $parent=Split-Path -Path $cursor -Parent
        if ([string]::IsNullOrWhiteSpace($parent) -or $parent -ceq $cursor) { break }
        $cursor=$parent
    }
}
function Assert-NoDuplicateJsonPropertyNames {
    param([System.Text.Json.JsonElement]$Element)
    if ($Element.ValueKind -eq [System.Text.Json.JsonValueKind]::Object) {
        $names=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        foreach($property in $Element.EnumerateObject()) { if (-not $names.Add($property.Name)) { throw 'configuration_duplicate_key' }; Assert-NoDuplicateJsonPropertyNames $property.Value }
    } elseif ($Element.ValueKind -eq [System.Text.Json.JsonValueKind]::Array) { foreach($child in $Element.EnumerateArray()) { Assert-NoDuplicateJsonPropertyNames $child } }
}

function Get-ModeExpression($node) {
    if ($node -is [Management.Automation.Language.PipelineAst]) { $pipeline = $node }
    elseif ($node -is [Management.Automation.Language.StatementBlockAst] -or $node -is [Management.Automation.Language.NamedBlockAst]) {
        if ($node.Statements.Count -ne 1 -or $node.Traps) { throw 'release_manifest_invalid' }; $pipeline = $node.Statements[0]
    } else { throw 'release_manifest_invalid' }
    if ($pipeline -isnot [Management.Automation.Language.PipelineAst] -or $pipeline.PipelineElements.Count -ne 1 -or $pipeline.Background) { throw 'release_manifest_invalid' }
    $element = $pipeline.PipelineElements[0]
    if ($element -isnot [Management.Automation.Language.CommandExpressionAst] -or $element.Redirections.Count) { throw 'release_manifest_invalid' }
    $element.Expression
}
function Get-ModeMap($node, [string[]]$keys) {
    if ($node -isnot [Management.Automation.Language.HashtableAst] -or $node.KeyValuePairs.Count -ne $keys.Count) { throw 'release_manifest_invalid' }
    $map = @{}
    foreach ($pair in $node.KeyValuePairs) {
        if ($pair.Item1 -isnot [Management.Automation.Language.StringConstantExpressionAst]) { throw 'release_manifest_invalid' }
        $key = $pair.Item1.Value
        if ($key -cnotin $keys -or $map.ContainsKey($key)) { throw 'release_manifest_invalid' }
        $map[$key] = Get-ModeExpression $pair.Item2
    }
    $map
}
function Get-ModeString($node) {
    if ($node -isnot [Management.Automation.Language.StringConstantExpressionAst]) { throw 'release_manifest_invalid' }; $node.Value
}
function Read-ModeManifest {
    param([Parameter(Mandatory)][string]$LiteralPath,[Parameter(Mandatory)][string]$ExpectedManifestSha256,[Parameter(Mandatory)][string]$ExpectedSourceCommit,[Parameter(Mandatory)][string]$ExpectedVersion)
    if ($ExpectedManifestSha256 -cnotmatch '^[A-Fa-f0-9]{64}$' -or $ExpectedSourceCommit -cnotmatch '^[a-f0-9]{40}$' -or $ExpectedVersion -cnotmatch '^\d+\.\d+\.\d+$') { throw 'manifest_anchor_invalid' }
    try {
        $rootPath=[IO.Path]::GetFullPath((Split-Path -Parent $LiteralPath)); Assert-NoReparsePath $rootPath
        $item = Get-Item -LiteralPath $LiteralPath -ErrorAction Stop
        if ($item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'release_manifest_invalid' }
        if ((Get-FileHash -LiteralPath $LiteralPath -Algorithm SHA256).Hash -cne $ExpectedManifestSha256) { throw 'manifest_hash_mismatch' }
        $stream = [IO.File]::Open($LiteralPath,'Open','Read','Read')
        try { if ($stream.Length -gt 4MB) { throw 'release_manifest_limit' }; $reader=[IO.StreamReader]::new($stream,[Text.Encoding]::UTF8,$true,4096,$true); try { $text=$reader.ReadToEnd() } finally { $reader.Dispose() } } finally { $stream.Dispose() }
        $tokens=$null; $errors=$null; $ast=[Management.Automation.Language.Parser]::ParseInput($text,[ref]$tokens,[ref]$errors)
        if ($errors -or $ast.ParamBlock -or $ast.DynamicParamBlock -or -not $ast.EndBlock.Unnamed -or $ast.BeginBlock -or $ast.ProcessBlock -or $ast.CleanBlock -or $ast.UsingStatements.Count) { throw 'release_manifest_invalid' }
        $root=Get-ModeMap (Get-ModeExpression $ast.EndBlock) @('Format','Version','SourceCommit','Files')
        if ($root.Format -isnot [Management.Automation.Language.ConstantExpressionAst] -or $root.Format.Value -isnot [int] -or $root.Format.Value -ne 1 -or $root.Files -isnot [Management.Automation.Language.ArrayExpressionAst]) { throw 'release_manifest_invalid' }
        $version=Get-ModeString $root.Version; $commit=Get-ModeString $root.SourceCommit
        if ($version -cnotmatch '^\d+\.\d+\.\d+$' -or $commit -cnotmatch '^[a-f0-9]{40}$') { throw 'release_manifest_invalid' }
        $items=@(); if ($root.Files.SubExpression.Statements.Count) { $expr=Get-ModeExpression $root.Files.SubExpression; $items=@(if($expr -is [Management.Automation.Language.ArrayLiteralAst]){$expr.Elements}else{$expr}) }
        if ($items.Count -lt 1 -or $items.Count -gt 10000) { throw 'release_manifest_invalid' }
        $seen=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
        $files=@(foreach($entryNode in $items) {
            $entry=Get-ModeMap $entryNode @('Path','Sha256','Length'); $path=Get-ModeString $entry.Path; $hash=Get-ModeString $entry.Sha256
            if ($path -cnotmatch '^[A-Za-z0-9_.-]+(/[A-Za-z0-9_.-]+)*$' -or ($path.Split('/') | Where-Object {$_ -in '.','..'}) -or $path -eq 'manifest.psd1' -or -not $seen.Add($path) -or $hash -cnotmatch '^[A-Fa-f0-9]{64}$' -or $entry.Length -isnot [Management.Automation.Language.ConstantExpressionAst]) { throw 'release_manifest_invalid' }
            $length=$entry.Length.Value; if (($length -isnot [int] -and $length -isnot [long]) -or $length -lt 0) { throw 'release_manifest_invalid' }
            [pscustomobject]@{Path=$path;Sha256=$hash;Length=[long]$length}
        })
        if ($commit -cne $ExpectedSourceCommit -or $version -cne $ExpectedVersion) { throw 'manifest_identity_mismatch' }
        foreach ($entry in $files) {
            $full=[IO.Path]::GetFullPath((Join-Path $rootPath ($entry.Path.Replace('/',[IO.Path]::DirectorySeparatorChar))))
            if (-not $full.StartsWith($rootPath+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) { throw 'release_path_invalid' }
            Assert-NoReparsePath $full
            $file=Get-Item -LiteralPath $full -ErrorAction Stop
            if ($file.PSIsContainer -or ($file.Attributes -band [IO.FileAttributes]::ReparsePoint) -or $file.Length -ne $entry.Length -or (Get-FileHash -LiteralPath $full -Algorithm SHA256).Hash -cne $entry.Sha256) { throw 'release_integrity_failed' }
        }
        [pscustomobject]@{Format=1;Version=$version;SourceCommit=$commit;Files=$files;Root=$rootPath}
    } catch { if ($_.Exception.Message -match '^(manifest_hash_mismatch|manifest_identity_mismatch|release_manifest_limit|release_manifest_invalid|release_reparse_point|release_path_invalid|release_integrity_failed)$') { throw }; throw 'release_manifest_invalid' }
}

function Get-ModeHandoffDecision {
    param([Parameter(Mandatory)]$Facts)
    $blockers=[Collections.Generic.List[string]]::new()
    if (-not $Facts.ApprovedVm) { $blockers.Add('approved_dev_vm_required') }
    if (-not $Facts.Elevated) { $blockers.Add('administrator_required') }
    foreach ($name in 'PackageValid','InstalledValid','ServicesValid','ConfigurationValid') { if (-not $Facts.$name) { $blockers.Add(($name.ToLowerInvariant()+'_required')) } }
    if ($Facts.FreeMemoryMiB -le 512) { $blockers.Add('hard_free_memory_floor_512_mib') }
    elseif ($Facts.FreeMemoryMiB -le 768) { $blockers.Add('initial_free_memory_not_above_768_mib') }
    if ($Facts.FreeDiskGiB -lt 10) { $blockers.Add('disk_free_below_10_gib') }
    if ($Facts.Pushover -or $Facts.Heartbeat -or $Facts.NetworkProbes) { $blockers.Add('outbound_configuration_not_off') }
    if ($Facts.DisableMarkerPresent) { $blockers.Add('disable_remediation_marker_present') }
    if (-not $Facts.ConfigurationAvailable) { $blockers.Add('configuration_unavailable') }
    $already=($Facts.Mode -ceq 'Confirmation' -and -not $Facts.ReadOnly -and -not $Facts.HelperReadOnly -and -not $Facts.HelperShadow)
    $baseline=($Facts.Mode -ceq 'Shadow' -and $Facts.ReadOnly -and $Facts.HelperReadOnly -and $Facts.HelperShadow)
    if (-not $already -and -not $baseline) { $blockers.Add('mode_state_inconsistent') }
    if (-not $already -and ($Facts.ServiceRegistrations -gt 0 -or $Facts.ProcessRegistrations -gt 0)) { $blockers.Add('review_existing_allowlist_before_enable') }
    if (-not $already -and -not $Facts.ApproverCapacityAvailable) { $blockers.Add('approver_capacity_unavailable') }
    if ($already -and -not $Facts.ApproverPresent) { $blockers.Add('current_operator_not_approved') }
    if ($already -and -not $Facts.RuntimeReadbackValid) { $blockers.Add('runtime_status_readback_required') }
    [pscustomobject]@{Allowed=($blockers.Count -eq 0);Blockers=@($blockers);AlreadyConfirmed=$already}
}

function Get-ModeReadOnlyPlan {
    param([Parameter(Mandatory)]$Facts)
    $decision=Get-ModeHandoffDecision $Facts
    if (-not $decision.Allowed) { return [pscustomobject]@{Outcome='blocked';Blockers=$decision.Blockers;ConfigurationPerformed=$false;ActionAcceptance='NOTRUN';JournalIntegrity='NOTRUN'} }
    if ($decision.AlreadyConfirmed) { return [pscustomobject]@{Outcome='mode_only_accepted';Blockers=@();ConfigurationPerformed=$false;ActionAcceptance='NOTRUN';JournalIntegrity='NOTRUN'} }
    [pscustomobject]@{Outcome='planned';Blockers=@();ConfigurationPerformed=$false;ActionAcceptance='NOTRUN';JournalIntegrity='NOTRUN'}
}

function Test-ModePreservedState {
    param([Parameter(Mandatory)]$Before,[Parameter(Mandatory)]$After)
    ($After.ServiceKillSwitch -eq $Before.ServiceKillSwitch -and $After.HelperKillSwitch -eq $Before.HelperKillSwitch -and
     $After.Pushover -eq $Before.Pushover -and $After.Heartbeat -eq $Before.Heartbeat -and $After.NetworkProbes -eq $Before.NetworkProbes -and
     $After.ServiceRegistrations -eq $Before.ServiceRegistrations -and $After.ProcessRegistrations -eq $Before.ProcessRegistrations -and
     $After.AllowlistFingerprint -eq $Before.AllowlistFingerprint -and $After.DisableMarkerPresent -eq $Before.DisableMarkerPresent -and
     $After.JournalExists -eq $Before.JournalExists)
}

function Get-ModeManualHandoff {
    param([Parameter(Mandatory)]$Core,[bool]$Execute)
    $outcome=$Core.Outcome; $failure=$null; $blockers=@($Core.Blockers); $next=@()
    if ($Core.Outcome -eq 'planned') {
        $next=@("& 'C:\Program Files\HostAgent\Cli\HostAgent.Cli.exe' configure Confirmation off off",'Confirm the CLI APPLY prompt yourself. Then run the installed CLI status --verbose and inspect a fresh protected configuration readback.')
        if ($Execute) { $outcome='manual_step_required'; $failure='automation_disabled_for_recovery_safety'; $blockers=@('manual_interactive_step_required') }
    }
    [pscustomobject]@{Outcome=$outcome;FailureCode=$failure;Blockers=$blockers;ManualNextStep=$next;ConfigurationPerformed=[bool]$Core.ConfigurationPerformed;ModeOnlyAcceptance='NOTRUN';ActionAcceptance='NOTRUN';JournalIntegrity='NOTRUN'}
}

function Get-InstalledModeFacts {
    param($Manifest)
    try {
        $computer=Get-CimInstance Win32_ComputerSystem; $product=Get-CimInstance Win32_ComputerSystemProduct
        $approved=($env:COMPUTERNAME -eq 'AgentPC' -and $computer.Model -eq 'VirtualBox' -and $product.UUID -eq '46543236-8C5E-49DA-953C-DB17F9B1E402' -and (Get-Service VBoxService).Status -eq 'Running' -and [Security.Principal.WindowsIdentity]::GetCurrent().Name -eq 'AgentPC\AgentPCdevtest')
        $elevated=([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
        $installedValid=$true; $critical=@($Manifest.Files | Where-Object {$_.Path -match '^(Service|Helper|Cli|Desktop)/.+\.(exe|dll)$'})
        if (-not ($critical.Path -contains 'Service/HostAgent.Service.exe') -or -not ($critical.Path -contains 'Helper/HostAgent.Helper.exe') -or -not ($critical.Path -contains 'Cli/HostAgent.Cli.exe')) { $installedValid=$false }
        foreach($entry in $critical) { $path=Join-Path 'C:\Program Files\HostAgent' ($entry.Path.Replace('/',[IO.Path]::DirectorySeparatorChar)); try { Assert-NoReparsePath $path; $installedFile=Get-Item -LiteralPath $path -ErrorAction Stop; if ($installedFile.PSIsContainer -or $installedFile.Length -ne $entry.Length -or (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -cne $entry.Sha256) {$installedValid=$false} } catch {$installedValid=$false} }
        $services=@(Get-CimInstance Win32_Service -Filter "Name='HostAgent' OR Name='HostAgent.Helper'"); $servicesValid=($services.Count -eq 2)
        foreach($s in $services) { $expected=if($s.Name -eq 'HostAgent'){'NT SERVICE\HostAgent'}else{'LocalSystem'}; $image=if($s.Name -eq 'HostAgent'){ '"C:\Program Files\HostAgent\Service\HostAgent.Service.exe"' }else{'"C:\Program Files\HostAgent\Helper\HostAgent.Helper.exe"'}; if($s.StartName -ne $expected -or $s.PathName -cne $image -or $s.StartMode -ne 'Auto' -or $s.State -ne 'Running'){$servicesValid=$false} }
        $servicePath='C:\ProgramData\HostAgent\service.json'; $helperPath='C:\ProgramData\HostAgent.Helper\helper.json'
        $serviceSid=([Security.Principal.NTAccount]::new('NT SERVICE\HostAgent')).Translate([Security.Principal.SecurityIdentifier]).Value
        Test-ModeDataDirectoryAcl -Path 'C:\ProgramData\HostAgent' -ServiceSid $serviceSid
        Test-ModeDataDirectoryAcl -Path 'C:\ProgramData\HostAgent.Helper' -ServiceSid 'S-1-5-18'
        $service=Read-ProtectedModeJson -Path $servicePath -AllowedFields @('mode','readOnly','killSwitch','collectionIntervalSeconds','heartbeatIntervalSeconds','requestTimeoutSeconds','enablePushover','enableHeartbeat','enableNetworkProbes') -TrustedSids @('S-1-5-18','S-1-5-32-544',$serviceSid) -Kind Service
        $helper=Read-ProtectedModeJson -Path $helperPath -AllowedFields @('readOnly','shadow','killSwitch','executionTimeoutSeconds','services','processes','approverSids') -TrustedSids @('S-1-5-18','S-1-5-32-544') -Kind Helper
        $networkProbes=if ($service.Contains('enableNetworkProbes')) {[bool]$service.enableNetworkProbes} else {$false}
        $validConfig=($service.mode -in 'Shadow','Confirmation' -and $service.readOnly -is [bool] -and $service.killSwitch -is [bool] -and $service.enablePushover -is [bool] -and $service.enableHeartbeat -is [bool] -and (-not $service.Contains('enableNetworkProbes') -or $service.enableNetworkProbes -is [bool]) -and $helper.readOnly -is [bool] -and $helper.shadow -is [bool] -and $helper.killSwitch -is [bool] -and $helper.services -is [array] -and $helper.processes -is [array] -and $helper.approverSids -is [array])
        $serviceRegs=@($helper.services).Count; $processRegs=@($helper.processes).Count
        $approverSid=[Security.Principal.WindowsIdentity]::GetCurrent().User.Value; $approvers=@($helper.approverSids); $marker=Test-Path -LiteralPath (Get-DisableRemediationMarkerPath) -PathType Leaf
        $mem=[double](Get-CimInstance Win32_OperatingSystem).FreePhysicalMemory / 1024.0; $disk=[double](Get-PSDrive C).Free/1GB
        $allowlistJson=ConvertTo-Json -InputObject @($helper.services,$helper.processes) -Compress -Depth 10
        $allowlistFingerprint=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($allowlistJson)))
        [pscustomobject]@{ApprovedVm=$approved;Elevated=$elevated;PackageValid=$true;InstalledValid=$installedValid;ServicesValid=$servicesValid;ConfigurationValid=$validConfig;ConfigurationAvailable=$validConfig;RuntimeReadbackValid=$false;FreeMemoryMiB=$mem;FreeDiskGiB=$disk;Mode=[string]$service.mode;ReadOnly=[bool]$service.readOnly;ServiceKillSwitch=[bool]$service.killSwitch;Pushover=[bool]$service.enablePushover;Heartbeat=[bool]$service.enableHeartbeat;NetworkProbes=$networkProbes;HelperReadOnly=[bool]$helper.readOnly;HelperShadow=[bool]$helper.shadow;HelperKillSwitch=[bool]$helper.killSwitch;ServiceRegistrations=$serviceRegs;ProcessRegistrations=$processRegs;AllowlistFingerprint=$allowlistFingerprint;ApproverPresent=($approvers -contains $approverSid);ApproverCapacityAvailable=($approvers.Count -lt 16 -or $approvers -contains $approverSid);DisableMarkerPresent=$marker;JournalExists=(Test-Path -LiteralPath 'C:\ProgramData\HostAgent.Helper\helper-journal.db' -PathType Leaf);ApproverSid=$approverSid;Manifest=$Manifest}
    } catch { throw 'installed_preflight_unavailable' }
}

function Read-ProtectedModeJson {
    param([string]$Path,[string[]]$AllowedFields,[string[]]$TrustedSids,[ValidateSet('Service','Helper')][string]$Kind)
    Assert-NoReparsePath $Path
    $file=Get-Item -LiteralPath $Path -ErrorAction Stop
    if ($file.PSIsContainer -or ($file.Attributes -band [IO.FileAttributes]::ReparsePoint) -or $file.Length -gt 16384) { throw 'configuration_invalid' }
    $trusted=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal); foreach($sid in $TrustedSids) {$null=$trusted.Add($sid)}
    if ($Kind -eq 'Service') { try {$serviceSid=([Security.Principal.NTAccount]::new('NT SERVICE\HostAgent')).Translate([Security.Principal.SecurityIdentifier]).Value; $null=$trusted.Add($serviceSid)} catch {throw 'configuration_acl_invalid'} }
    $acl=Get-Acl -LiteralPath $Path
    if (-not $trusted.Contains($acl.GetOwner([Security.Principal.SecurityIdentifier]).Value)) { throw 'configuration_acl_invalid' }
    foreach($rule in $acl.GetAccessRules($true,$true,[Security.Principal.SecurityIdentifier])) { if ($rule.AccessControlType -eq 'Allow' -and -not $trusted.Contains($rule.IdentityReference.Value)) {throw 'configuration_acl_invalid'} }
    $bytes=[IO.File]::ReadAllBytes($Path); if($bytes.Length -lt 1 -or $bytes.Length -gt 16384){throw 'configuration_invalid'}; $json=[Text.Encoding]::UTF8.GetString($bytes)
    $jsonOptions=[System.Text.Json.JsonDocumentOptions]::new(); $jsonOptions.MaxDepth=8
    $document=[System.Text.Json.JsonDocument]::Parse($json,$jsonOptions); try {Assert-NoDuplicateJsonPropertyNames $document.RootElement; if($document.RootElement.ValueKind -ne 'Object'){throw 'configuration_invalid'}
        $keys=@($document.RootElement.EnumerateObject() | ForEach-Object Name); foreach($required in $AllowedFields | Where-Object {$_ -ne 'enableNetworkProbes'}) {if($required -notin $keys){throw 'configuration_invalid'}}
        foreach($key in $keys){if($key -cnotin $AllowedFields){throw 'configuration_invalid'}}
        if($Kind -eq 'Helper') {
            foreach($prop in 'services','processes','approverSids') {if($document.RootElement.GetProperty($prop).ValueKind -ne 'Array'){throw 'configuration_invalid'}}
            $serviceIds=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal); $serviceNames=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
            foreach($entry in $document.RootElement.GetProperty('services').EnumerateArray()){
                if($entry.ValueKind -ne 'Object' -or (@($entry.EnumerateObject()|% Name|? {$_ -notin @('id','serviceName','enabledOperations')}).Count -gt 0) -or @($entry.EnumerateObject()).Count -ne 3 -or $entry.GetProperty('id').ValueKind -ne 'String' -or $entry.GetProperty('serviceName').ValueKind -ne 'String' -or $entry.GetProperty('enabledOperations').ValueKind -ne 'Array'){throw 'configuration_invalid'}
                foreach($field in 'id','serviceName'){ $v=$entry.GetProperty($field).GetString(); if($v -cnotmatch '^[A-Za-z0-9][A-Za-z0-9_.-]{0,127}$' -or $v.Contains('..')){throw 'configuration_invalid'} }
                if(-not $serviceIds.Add($entry.GetProperty('id').GetString()) -or -not $serviceNames.Add($entry.GetProperty('serviceName').GetString())){throw 'configuration_invalid'}
                foreach($op in $entry.GetProperty('enabledOperations').EnumerateArray()){if($op.ValueKind -ne 'String' -or $op.GetString() -cnotin @('StartService','StopService','RestartService')){throw 'configuration_invalid'}}
            }
            $processIds=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal); $processPaths=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
            foreach($entry in $document.RootElement.GetProperty('processes').EnumerateArray()){
                if($entry.ValueKind -ne 'Object' -or (@($entry.EnumerateObject()|% Name|? {$_ -notin @('id','executablePath','sha256','enabled')}).Count -gt 0) -or @($entry.EnumerateObject()).Count -ne 4 -or $entry.GetProperty('id').ValueKind -ne 'String' -or $entry.GetProperty('executablePath').ValueKind -ne 'String' -or $entry.GetProperty('sha256').ValueKind -ne 'String' -or $entry.GetProperty('enabled').ValueKind -notin @('True','False')){throw 'configuration_invalid'}
                $id=$entry.GetProperty('id').GetString(); $exe=$entry.GetProperty('executablePath').GetString(); $hash=$entry.GetProperty('sha256').GetString()
                if($id -cnotmatch '^[A-Za-z0-9][A-Za-z0-9_.-]{0,127}$' -or $id.Contains('..') -or -not [IO.Path]::IsPathFullyQualified($exe) -or $exe.StartsWith('\\') -or ($exe.Length -gt 2 -and $exe.Substring(2).Contains(':')) -or $hash -cnotmatch '^[A-Fa-f0-9]{64}$'){throw 'configuration_invalid'}
                if(-not $processIds.Add($id) -or -not $processPaths.Add([IO.Path]::GetFullPath($exe))){throw 'configuration_invalid'}
            }
        }
    } finally {$document.Dispose()}
    $obj=ConvertFrom-Json -InputObject $json -AsHashtable -Depth 8
    if ($obj -isnot [System.Collections.IDictionary] -or @($obj.Keys).Count -gt $AllowedFields.Count) { throw 'configuration_invalid' }
    foreach($key in $obj.Keys) { if ($key -cnotin $AllowedFields) {throw 'configuration_invalid'} }
    foreach($name in 'readOnly','killSwitch','enablePushover','enableHeartbeat','shadow') {if($obj.Contains($name) -and $obj[$name] -isnot [bool]){throw 'configuration_invalid'}}
    foreach($name in 'collectionIntervalSeconds','heartbeatIntervalSeconds','requestTimeoutSeconds','executionTimeoutSeconds') {if($obj.Contains($name) -and $obj[$name] -isnot [int] -and $obj[$name] -isnot [long]){throw 'configuration_invalid'}}
    if($Kind -eq 'Service' -and ($obj.mode -isnot [string] -or $obj.mode -cnotin 'Shadow','Confirmation' -or $obj.collectionIntervalSeconds -lt 5 -or $obj.collectionIntervalSeconds -gt 3600 -or $obj.heartbeatIntervalSeconds -lt 30 -or $obj.heartbeatIntervalSeconds -gt 3600 -or $obj.requestTimeoutSeconds -lt 1 -or $obj.requestTimeoutSeconds -gt 30)){throw 'configuration_invalid'}
    if($Kind -eq 'Helper' -and ($obj.executionTimeoutSeconds -lt 1 -or $obj.executionTimeoutSeconds -gt 30 -or @($obj.services).Count -gt 64 -or @($obj.processes).Count -gt 64 -or @($obj.approverSids).Count -gt 16)){throw 'configuration_invalid'}
    if($Kind -eq 'Service' -and $obj.Contains('enableNetworkProbes') -and $obj.enableNetworkProbes -isnot [bool]){throw 'configuration_invalid'}
    if($Kind -eq 'Helper'){foreach($sidText in $obj.approverSids){if($sidText -isnot [string]){throw 'configuration_invalid'}; try{[void][Security.Principal.SecurityIdentifier]::new($sidText)}catch{throw 'configuration_invalid'}}}
    $obj
}

function Test-ModeDirectorySecurity {
    param([Parameter(Mandatory)][Security.AccessControl.DirectorySecurity]$Security,[Parameter(Mandatory)][string]$ServiceSid)
    $expected=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach($sid in @($ServiceSid,'S-1-5-18','S-1-5-32-544')){$null=$expected.Add($sid)}
    if (-not $Security.AreAccessRulesProtected -or -not $expected.Contains($Security.GetOwner([Security.Principal.SecurityIdentifier]).Value)) { throw 'configuration_acl_invalid' }
    $rules=@($Security.GetAccessRules($true,$true,[Security.Principal.SecurityIdentifier]))
    foreach($rule in $rules) {
        $inherit=[Security.AccessControl.InheritanceFlags]::ContainerInherit -bor [Security.AccessControl.InheritanceFlags]::ObjectInherit
        if ($rule.IsInherited -or $rule.AccessControlType -ne 'Allow' -or -not $expected.Contains($rule.IdentityReference.Value) -or
            ($rule.FileSystemRights -band [Security.AccessControl.FileSystemRights]::FullControl) -ne [Security.AccessControl.FileSystemRights]::FullControl -or
            ($rule.InheritanceFlags -band $inherit) -ne $inherit -or $rule.PropagationFlags -ne [Security.AccessControl.PropagationFlags]::None) { throw 'configuration_acl_invalid' }
    }
    foreach($sid in $expected) { if (-not ($rules | Where-Object {$_.IdentityReference.Value -eq $sid})) { throw 'configuration_acl_invalid' } }
    $true
}

function Test-ModeDataDirectoryAcl {
    param([Parameter(Mandatory)][string]$Path,[Parameter(Mandatory)][string]$ServiceSid)
    Assert-NoReparsePath $Path
    $acl=Get-Acl -LiteralPath $Path
    [void](Test-ModeDirectorySecurity -Security $acl -ServiceSid $ServiceSid)
}

if ($MyInvocation.InvocationName -ne '.') {
    $result=[ordered]@{Outcome='blocked';FailureCode=$null;ModeOnlyAcceptance='NOTRUN';ActionAcceptance='NOTRUN';JournalIntegrity='NOTRUN';ConfigurationPerformed=$false;Blockers=@();ManualNextStep=@()}
    try {
        $initialFree=[double](Get-CimInstance Win32_OperatingSystem).FreePhysicalMemory/1024.0
        if($initialFree -le 512){throw 'hard_free_memory_floor_512_mib'}; if($initialFree -le 768){throw 'initial_free_memory_not_above_768_mib'}
        if([double](Get-PSDrive C).Free/1GB -lt 10){throw 'disk_free_below_10_gib'}
        $manifest=Read-ModeManifest -LiteralPath (Join-Path $PackagePath 'manifest.psd1') -ExpectedManifestSha256 $ExpectedManifestSha256 -ExpectedSourceCommit $ExpectedSourceCommit -ExpectedVersion $ExpectedVersion
        $facts=Get-InstalledModeFacts $manifest
        $core=Get-ModeReadOnlyPlan -Facts $facts
        $handoff=Get-ModeManualHandoff -Core $core -Execute:$Execute
        foreach($key in $handoff.PSObject.Properties.Name){$result[$key]=$handoff.$key}
    } catch { if (-not $result.FailureCode) { $result.FailureCode=if($_.Exception.Message -match '^[a-z0-9_]+$'){$_.Exception.Message}else{'preflight_failed'} }; $result.Outcome='blocked'; $result.Blockers=@($result.FailureCode) }
    $result | ConvertTo-Json -Depth 4
    if ($result.Outcome -notin @('planned','mode_only_accepted')) { exit 2 }
}

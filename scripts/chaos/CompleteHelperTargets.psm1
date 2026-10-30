Set-StrictMode -Version Latest
Import-Module (Join-Path $PSScriptRoot 'IsolatedScmSafety.psm1')
Import-Module (Join-Path $PSScriptRoot 'CompleteDiagnostics.psm1')

function Get-CompleteHelperTargetPlan {
    param($Candidate,[string[]]$RunIds,[string]$OperatorSid)
    if(-not $Candidate -or @($RunIds).Count-ne3 -or @($RunIds|Select-Object -Unique).Count-ne3){throw 'three_distinct_target_ids_required'}
    foreach($id in $RunIds){
        $value=[guid]::Empty
        if(-not[guid]::TryParseExact($id,'N',[ref]$value)-or$value-eq[guid]::Empty-or$id-cne$value.ToString('N')){throw 'canonical_nonempty_guid_required'}
    }
    $sid=[Security.Principal.SecurityIdentifier]::new($OperatorSid)
    if(-not$sid.IsAccountSid()-or$sid.Value-cne$OperatorSid){throw 'operator_account_sid_required'}
    $source=[IO.Path]::GetFullPath([string]$Candidate.Path)
    if($source-cne[string]$Candidate.Path-or-not[IO.Path]::IsPathRooted($source)-or$source.StartsWith('\\')-or$source.Substring(2).Contains(':')-or
        [string]$Candidate.ManifestSha256-cnotmatch'\A[0-9A-F]{64}\z'){throw 'canonical_candidate_identity_required'}
    for($index=0;$index-lt3;$index++){
        $id=$RunIds[$index];$name='HostAgentDevScm_'+$id;$root='C:\ProgramData\HostAgent-DEV-SCM-'+$id
        $image=Join-Path $root 'app\HostAgent.PipeProbe.exe'
        [pscustomobject]@{RunId=$id;Name=$name;Root=$root;App=(Join-Path $root 'app');Data=(Join-Path $root 'data');Image=$image
            BinaryPath=('"'+$image+'" isolated-complete-target '+$id+' '+$OperatorSid);OperatorSid=$OperatorSid
            CandidatePath=$source;ManifestSha256=[string]$Candidate.ManifestSha256;ServiceSid=$null
            RootCreated=$false;CreationAttempted=$false;Created=$false;CopyCompleted=$false;StartRequested=$false
            InitialState=$(if($index-eq0){'Stopped'}else{'Running'});SlowStop=($index-eq1);NativeOperations=[Collections.Generic.List[object]]::new()}
    }
}

function Assert-CompleteHelperTargetMetadata {
    param($Target)
    $other=@('11111111111111111111111111111111','22222222222222222222222222222222','33333333333333333333333333333333'|Where-Object{$_-cne$Target.RunId}|Select-Object -First 2)
    $expected=@(Get-CompleteHelperTargetPlan -Candidate ([pscustomobject]@{Path=$Target.CandidatePath;ManifestSha256=$Target.ManifestSha256}) `
        -RunIds @($Target.RunId,$other[0],$other[1]) -OperatorSid $Target.OperatorSid)[0]
    foreach($property in @('RunId','Name','Root','App','Data','Image','BinaryPath','OperatorSid','CandidatePath','ManifestSha256')){
        if([string]$Target.$property-cne[string]$expected.$property){throw 'target_metadata_scope_mismatch'}
    }
}

function Get-CompleteHelperNativeArguments {
    param($Target,[ValidateSet('create','start','stop','disable')][string]$Operation)
    Assert-CompleteHelperTargetMetadata $Target
    switch($Operation){
        create {Get-IsolatedScmCreateArguments $Target.Name $Target.BinaryPath ('HostAgent DEV helper target '+$Target.RunId)}
        start {@('start',$Target.Name)}
        stop {@('stop',$Target.Name)}
        disable {@('config',$Target.Name,'start=','disabled')}
    }
}

function Assert-CompleteHelperEnvironment {
    param([string]$OperatorSid)
    $identity=[Security.Principal.WindowsIdentity]::GetCurrent()
    try {
        if($env:COMPUTERNAME-cne'AGENTPC'-or$env:ProgramData-ine'C:\ProgramData'-or$env:WINDIR-ine'C:\Windows'-or
            $identity.Name-ine'AgentPC\vmdev'-or$identity.User.Value-cne$OperatorSid-or
            -not[Security.Principal.WindowsPrincipal]::new($identity).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)){
            throw 'approved_elevated_vm_operator_required'
        }
    }finally{$identity.Dispose()}
    if((Get-CimInstance Win32_ComputerSystem -OperationTimeoutSec 5 -ErrorAction Stop).Model-cne'VirtualBox'-or
        (Get-CimInstance Win32_ComputerSystemProduct -OperationTimeoutSec 5 -ErrorAction Stop).UUID-ine'0bb543d1-7bb0-43ed-9c94-596d63ef3310'-or
        (Get-Service VBoxService -ErrorAction Stop).Status-ne'Running'){throw 'approved_vm_identity_required'}
}

function Assert-CompleteHelperPlainPath {
    param([string]$Path)
    for($current=[IO.DirectoryInfo]::new([IO.Path]::GetFullPath($Path));$null-ne$current;$current=$current.Parent){
        if((Test-Path -LiteralPath $current.FullName)-and((Get-Item -LiteralPath $current.FullName -Force).Attributes-band[IO.FileAttributes]::ReparsePoint)){
            throw 'reparse_path_rejected'
        }
    }
}

function Get-CompleteHelperTreeFiles {
    param([string]$Path,[scriptblock]$Guard)
    Assert-CompleteHelperPlainPath $Path
    if(-not(Test-Path -LiteralPath $Path -PathType Container)){throw 'required_directory_missing'}
    $pending=[Collections.Generic.Stack[string]]::new();$pending.Push($Path);$count=0
    while($pending.Count-gt0){
        foreach($item in Get-ChildItem -LiteralPath $pending.Pop() -Force -ErrorAction Stop){
            & $Guard|Out-Null
            if(++$count-gt10000-or($item.Attributes-band[IO.FileAttributes]::ReparsePoint)){throw 'package_tree_limit_or_reparse'}
            if($item.PSIsContainer){$pending.Push($item.FullName)}else{$item}
        }
    }
}

function Assert-CompleteHelperManifest {
    param([string]$Path,[string]$ManifestSha256,[scriptblock]$Guard,[switch]$WithCanaries)
    $all=@(Get-CompleteHelperTreeFiles $Path $Guard)
    $manifestPath=Join-Path $Path 'candidate-manifest.json'
    if((Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256 -ErrorAction Stop).Hash-cne$ManifestSha256){throw 'candidate_manifest_hash_mismatch'}
    $manifest=Get-Content -LiteralPath $manifestPath -Raw -ErrorAction Stop|ConvertFrom-Json -ErrorAction Stop
    if($manifest.format-ne1-or@($manifest.files).Count-lt50-or@($manifest.files).Count-gt5000){throw 'candidate_manifest_invalid'}
    $entries=@{};$prefix=[IO.Path]::GetFullPath($Path).TrimEnd('\')+'\'
    foreach($entry in $manifest.files){
        & $Guard|Out-Null
        $relative=[string]$entry.path
        if([string]::IsNullOrWhiteSpace($relative)-or[IO.Path]::IsPathRooted($relative)-or$relative.Contains('..')-or$relative.Contains(':')-or
            $entries.ContainsKey($relative.Replace('/','\'))-or[string]$entry.sha256-cnotmatch'\A[0-9A-Fa-f]{64}\z'){throw 'manifest_entry_invalid'}
        $full=[IO.Path]::GetFullPath((Join-Path $Path $relative))
        if(-not$full.StartsWith($prefix,[StringComparison]::OrdinalIgnoreCase)-or-not(Test-Path -LiteralPath $full -PathType Leaf)){throw 'manifest_entry_scope_or_missing'}
        Assert-CompleteHelperPlainPath $full
        if((Get-FileHash -LiteralPath $full -Algorithm SHA256).Hash-ine[string]$entry.sha256){throw 'candidate_file_hash_mismatch'}
        $entries[$relative.Replace('/','\')]=$true
    }
    $allowed=@{'candidate-manifest.json'=$true}
    if($WithCanaries){$allowed['.scm-acl-write-canary']=$true;$allowed['.scm-acl-delete-canary']=$true}
    foreach($file in $all){
        $relative=$file.FullName.Substring($prefix.Length)
        if(-not$entries.ContainsKey($relative)-and-not$allowed.ContainsKey($relative)){throw 'candidate_unmanifested_file'}
    }
    if($all.Count-ne($entries.Count+$allowed.Count)){throw 'candidate_inventory_mismatch'}
    foreach($name in @('HostAgent.PipeProbe.exe','HostAgent.PipeProbe.dll')){if(-not$entries.ContainsKey($name)){throw 'test_host_not_manifested'}}
    if($WithCanaries){
        $expected=[Text.Encoding]::UTF8.GetBytes('HostAgent isolated SCM ACL canary v1')
        foreach($name in @('.scm-acl-write-canary','.scm-acl-delete-canary')){
            $actual=[IO.File]::ReadAllBytes((Join-Path $Path $name))
            if([Convert]::ToBase64String($actual)-cne[Convert]::ToBase64String($expected)){throw 'acl_canary_changed'}
        }
    }
    return $manifest
}

function Set-CompleteHelperDirectoryAcl {
    param([string]$Path,[string]$ServiceSid,[ValidateSet('PackageRoot','App','Data')][string]$Scope)
    Assert-CompleteHelperPlainPath $Path
    $policy=Get-IsolatedScmAclPolicy $Scope
    $security=[Security.AccessControl.DirectorySecurity]::new();$security.SetAccessRuleProtection($true,$false)
    $admin=[Security.Principal.SecurityIdentifier]::new('S-1-5-32-544');$system=[Security.Principal.SecurityIdentifier]::new('S-1-5-18')
    $security.SetOwner($admin)
    $inherit=[Security.AccessControl.InheritanceFlags]'ContainerInherit,ObjectInherit'
    foreach($sid in @($admin,$system)){
        $security.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($sid,[Security.AccessControl.FileSystemRights]::FullControl,
            $inherit,[Security.AccessControl.PropagationFlags]::None,[Security.AccessControl.AccessControlType]::Allow))
    }
    if($policy.ServiceRights-ne'None'){
        $security.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new([Security.Principal.SecurityIdentifier]::new($ServiceSid),
            [Security.AccessControl.FileSystemRights]$policy.ServiceRights,$inherit,[Security.AccessControl.PropagationFlags]::None,[Security.AccessControl.AccessControlType]::Allow))
    }
    Set-Acl -LiteralPath $Path -AclObject $security -ErrorAction Stop
}

function Assert-CompleteHelperRegistration {
    param($Target,[switch]$AllowAbsent)
    Assert-CompleteHelperTargetMetadata $Target
    $service=Get-CimInstance Win32_Service -Filter ("Name='"+$Target.Name+"'") -OperationTimeoutSec 5 -ErrorAction Stop
    if(-not$service){if($AllowAbsent){return $null};throw 'isolated_target_registration_missing'}
    $registry=Get-ItemProperty -LiteralPath ('Registry::HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\'+$Target.Name) -ErrorAction Stop
    if($service.Name-cne$Target.Name-or$service.PathName-cne$Target.BinaryPath-or$service.StartName-cne('NT SERVICE\'+$Target.Name)-or
        $service.StartMode-notin@('Manual','Disabled')-or[int]$registry.Type-ne16){throw 'isolated_target_configuration_mismatch'}
    if([int]$service.ProcessId-gt0){
        $protected=Get-CimInstance Win32_Service -Filter "Name='HostAgent'" -OperationTimeoutSec 5 -ErrorAction Stop
        if([int]$service.ProcessId-le4-or[int]$service.ProcessId-eq$PID-or[int]$service.ProcessId-eq[int]$protected.ProcessId){throw 'isolated_target_pid_protected'}
        $process=Get-CimInstance Win32_Process -Filter ('ProcessId='+[int]$service.ProcessId) -OperationTimeoutSec 5 -ErrorAction Stop
        if(-not$process){
            # The bounded fixture may exit between the SCM and process reads.
            # Accept only a fresh, fully identified Stopped/PID0 registration.
            $stopped=Get-CimInstance Win32_Service -Filter ("Name='"+$Target.Name+"'") -OperationTimeoutSec 5 -ErrorAction Stop
            $stoppedRegistry=Get-ItemProperty -LiteralPath ('Registry::HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\'+$Target.Name) -ErrorAction Stop
            if(-not$stopped-or$stopped.State-ne'Stopped'-or[int]$stopped.ProcessId-ne0-or
                $stopped.Name-cne$Target.Name-or$stopped.PathName-cne$Target.BinaryPath-or$stopped.StartName-cne('NT SERVICE\'+$Target.Name)-or
                $stopped.StartMode-notin@('Manual','Disabled')-or[int]$stoppedRegistry.Type-ne16){throw 'isolated_target_process_missing'}
            return $stopped
        }
        if($process.ExecutablePath-ine$Target.Image){throw 'isolated_target_process_image_mismatch'}
        if($process.CommandLine-cne$Target.BinaryPath){throw 'isolated_target_process_command_mismatch'}
        if([int]$process.SessionId-ne0){throw 'isolated_target_process_session_mismatch'}
    }
    return $service
}

function Invoke-CompleteHelperNative {
    param($Target,[ValidateSet('create','start','stop','disable')][string]$Operation,[scriptblock]$Guard)
    & $Guard|Out-Null
    $arguments=@(Get-CompleteHelperNativeArguments $Target $Operation)
    if($Operation-eq'create'){
        if(Get-CimInstance Win32_Service -Filter ("Name='"+$Target.Name+"'") -OperationTimeoutSec 5 -ErrorAction Stop){throw 'target_registration_already_exists'}
    }else{
        $service=Assert-CompleteHelperRegistration $Target
        if($Operation-eq'start'-or[int]$service.ProcessId-gt0){Assert-CompleteHelperManifest $Target.App $Target.ManifestSha256 $Guard -WithCanaries|Out-Null}
    }
    $info=[Diagnostics.ProcessStartInfo]::new();$info.FileName='C:\Windows\System32\sc.exe'
    $info.Arguments=ConvertTo-IsolatedScmNativeCommandLine $arguments
    $info.UseShellExecute=$false;$info.CreateNoWindow=$true;$info.RedirectStandardOutput=$true;$info.RedirectStandardError=$true
    $child=[Diagnostics.Process]::Start($info);$stdout=$child.StandardOutput.ReadToEndAsync();$stderr=$child.StandardError.ReadToEndAsync()
    $clock=[Diagnostics.Stopwatch]::StartNew()
    try{
        while(-not$child.HasExited){
            & $Guard|Out-Null
            if($clock.Elapsed.TotalSeconds-ge15){throw 'isolated_native_control_deadline'}
            Start-Sleep -Milliseconds 250;$child.Refresh()
        }
        $child.WaitForExit();[void]$stdout.GetAwaiter().GetResult();[void]$stderr.GetAwaiter().GetResult()
        $Target.NativeOperations.Add([pscustomobject]@{Operation=$Operation;ExitCode=$child.ExitCode;ElapsedMilliseconds=$clock.ElapsedMilliseconds})
        if($child.ExitCode-ne0){throw ('isolated_native_'+$Operation+'_failed_'+$child.ExitCode)}
    }finally{
        if(-not$child.HasExited){$child.Kill();[void]$child.WaitForExit(5000)}
        $child.Dispose()
    }
}

function Wait-CompleteHelperTargetState {
    param($Target,[ValidateSet('Running','Stopped')][string]$State,[scriptblock]$Guard)
    $clock=[Diagnostics.Stopwatch]::StartNew()
    while($clock.Elapsed.TotalSeconds-lt15){
        & $Guard|Out-Null
        $current=Assert-CompleteHelperRegistration $Target
        if($current.State-eq$State-and(($State-eq'Running'-and[int]$current.ProcessId-gt0)-or($State-eq'Stopped'-and[int]$current.ProcessId-eq0))){return $current}
        Start-Sleep -Milliseconds 250
    }
    throw 'isolated_target_state_deadline'
}

function Copy-CompleteHelperFile {
    param([string]$Source,[string]$Destination,[scriptblock]$Guard)
    Assert-CompleteHelperPlainPath $Source;Assert-CompleteHelperPlainPath $Destination
    $inputFile=$null;$outputFile=$null;$task=$null
    $cancel=[Threading.CancellationTokenSource]::new()
    try{
        $inputFile=[IO.FileStream]::new($Source,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read,81920,[IO.FileOptions]::Asynchronous)
        $outputFile=[IO.FileStream]::new($Destination,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None,81920,[IO.FileOptions]::Asynchronous)
        $task=$inputFile.CopyToAsync($outputFile,81920,$cancel.Token)
        $clock=[Diagnostics.Stopwatch]::StartNew()
        while(-not$task.IsCompleted){
            & $Guard|Out-Null
            if($clock.Elapsed.TotalSeconds-ge15){throw 'candidate_copy_file_deadline'}
            [void]$task.Wait(250)
        }
        [void]$task.GetAwaiter().GetResult()
        & $Guard|Out-Null
    }finally{
        $cancel.Cancel()
        if($task-and-not$task.IsCompleted){try{[void]$task.Wait(5000)}catch{}}
        if($outputFile){$outputFile.Dispose()};if($inputFile){$inputFile.Dispose()};$cancel.Dispose()
    }
}

function New-CompleteHelperTargets {
    param($Candidate,[string[]]$RunIds,[string]$OperatorSid,[scriptblock]$Guard,[ref]$TargetState)
    $targets=@(Get-CompleteHelperTargetPlan $Candidate $RunIds $OperatorSid)
    if($TargetState){$TargetState.Value=$targets}
    try{
        Assert-CompleteHelperEnvironment $OperatorSid
        & $Guard|Out-Null
        Assert-CompleteHelperManifest $Candidate.Path $Candidate.ManifestSha256 $Guard|Out-Null
        foreach($target in $targets){
            Assert-CompleteHelperPlainPath $target.Root|Out-Null
            if((Test-Path -LiteralPath $target.Root)-or(Get-CimInstance Win32_Service -Filter ("Name='"+$target.Name+"'") -OperationTimeoutSec 5 -ErrorAction Stop)){
                throw 'fresh_guid_target_required'
            }
        }
        foreach($target in $targets){
            & $Guard|Out-Null
            New-Item -ItemType Directory -Path $target.Root -ErrorAction Stop|Out-Null;$target.RootCreated=$true
            Set-CompleteHelperDirectoryAcl $target.Root $null 'PackageRoot'|Out-Null
            if(-not(Test-IsolatedScmPackageRootAcl (Get-Acl -LiteralPath $target.Root))){throw 'root_acl_guard_failed'}
            $target.CreationAttempted=$true
            Invoke-CompleteHelperNative $target 'create' $Guard;$target.Created=$true
            $target.ServiceSid=([Security.Principal.NTAccount]::new('NT SERVICE',$target.Name)).Translate([Security.Principal.SecurityIdentifier]).Value
            foreach($pair in @(@($target.App,'App'),@($target.Data,'Data'))){
                New-Item -ItemType Directory -Path $pair[0] -ErrorAction Stop|Out-Null
                Set-CompleteHelperDirectoryAcl $pair[0] $target.ServiceSid $pair[1]|Out-Null
            }
            # Copy each inventoried file with an asynchronous bounded wait and
            # the same 250ms guard. CreateNew never replaces an existing file.
            $sourcePrefix=$Candidate.Path.TrimEnd('\')+'\'
            foreach($item in @(Get-CompleteHelperTreeFiles $Candidate.Path $Guard)){
                & $Guard|Out-Null
                $destination=Join-Path $target.App $item.FullName.Substring($sourcePrefix.Length)
                Assert-CompleteHelperPlainPath $destination|Out-Null
                [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($destination))
                Copy-CompleteHelperFile $item.FullName $destination $Guard|Out-Null
            }
            foreach($name in @('.scm-acl-write-canary','.scm-acl-delete-canary')){
                [IO.File]::WriteAllBytes((Join-Path $target.App $name),[Text.Encoding]::UTF8.GetBytes('HostAgent isolated SCM ACL canary v1'))
            }
            # Freeze every app entry as explicit Admin/SYSTEM Full plus exact
            # service SID ReadAndExecute before any target can start.
            $queue=[Collections.Generic.Stack[string]]::new();$queue.Push($target.App)
            while($queue.Count-gt0){
                $directory=$queue.Pop();Set-CompleteHelperDirectoryAcl $directory $target.ServiceSid 'App'|Out-Null
                foreach($item in Get-ChildItem -LiteralPath $directory -Force){
                    & $Guard|Out-Null
                    if($item.Attributes-band[IO.FileAttributes]::ReparsePoint){throw 'app_reparse_rejected'}
                    if($item.PSIsContainer){$queue.Push($item.FullName)}else{
                        $acl=[Security.AccessControl.FileSecurity]::new();$acl.SetAccessRuleProtection($true,$false)
                        $acl.SetOwner([Security.Principal.SecurityIdentifier]::new('S-1-5-32-544'))
                        foreach($entry in @(@('S-1-5-32-544','FullControl'),@('S-1-5-18','FullControl'),@($target.ServiceSid,'ReadAndExecute'))){
                            $acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new([Security.Principal.SecurityIdentifier]::new($entry[0]),
                                [Security.AccessControl.FileSystemRights]$entry[1],[Security.AccessControl.AccessControlType]::Allow))
                        }
                        Set-Acl -LiteralPath $item.FullName -AclObject $acl -ErrorAction Stop|Out-Null
                    }
                }
            }
            Assert-CompleteHelperManifest $target.App $target.ManifestSha256 $Guard -WithCanaries|Out-Null
            $target.CopyCompleted=$true
            if($target.SlowStop){[IO.File]::WriteAllBytes((Join-Path $target.Data 'stop-delay.flag'),[byte[]]@())}
            Assert-CompleteHelperRegistration $target|Out-Null
            if($target.InitialState-eq'Running'){
                $target.StartRequested=$true;Invoke-CompleteHelperNative $target 'start' $Guard
                Wait-CompleteHelperTargetState $target 'Running' $Guard|Out-Null
            }else{Wait-CompleteHelperTargetState $target 'Stopped' $Guard|Out-Null}
        }
        return $targets
    }catch{
        $_.Exception.Data['Targets']=$targets
        throw
    }
}

function Assert-CompleteHelperFactoryResult {
    param([object[]]$ReturnedTargets,[object[]]$OwnedTargets,[string[]]$RunIds)
    if(@($ReturnedTargets).Count-ne3-or@($OwnedTargets).Count-ne3-or@($RunIds).Count-ne3){throw 'helper_target_output_contract'}
    for($index=0;$index-lt3;$index++){
        $returned=$ReturnedTargets[$index];$owned=$OwnedTargets[$index]
        if(-not$returned-or-not$owned-or-not[object]::ReferenceEquals($returned.PSObject.BaseObject,$owned.PSObject.BaseObject)){
            throw 'helper_target_output_contract'
        }
        Assert-CompleteHelperTargetMetadata $returned
        if($returned.RunId-cne$RunIds[$index]-or-not$returned.Created-or-not$returned.CopyCompleted){throw 'helper_target_output_contract'}
    }
}

function Confirm-CompleteHelperTargets {
    param([object[]]$Targets,[scriptblock]$Guard)
    if(@($Targets).Count-ne3){throw 'three_planned_targets_required_for_cleanup'}
    $results=[Collections.Generic.List[object]]::new()
    $failed=$false;$resourceAbort=$false
    foreach($target in $Targets){
      try{
        Assert-CompleteHelperTargetMetadata $target
        Assert-CompleteHelperEnvironment $target.OperatorSid
        if(-not$target.CreationAttempted-or-not$target.RootCreated){
            # A failed freshness check must never authorize touching an older
            # registration merely because its random name matched our plan.
            $unowned=Get-CimInstance Win32_Service -Filter ("Name='"+$target.Name+"'") -OperationTimeoutSec 5 -ErrorAction Stop
            if($unowned){throw 'unowned_target_registration_must_not_be_controlled'}
            $results.Add([pscustomobject]@{RunId=$target.RunId;Name=$target.Name;State='NotCreated';ProcessId=0;StartMode='NotCreated'
                Quiescent=$true;PackageVerified=$false;PartialSetup=$true;Root=$target.Root;Data=$target.Data;GuardFailed=$false;Preserved=$true;NativeOperations=@()})
            continue
        }
        # A latched resource/protected-state abort must not prevent the light
        # stop/disable of these independently validated test registrations.
        # Its occurrence is retained for the parent to prohibit continuation.
        $guardErrors=[Collections.Generic.List[string]]::new()
        $cleanupGuard={try{& $Guard|Out-Null}catch{$guardErrors.Add('parent_safety_guard_failed')}}.GetNewClosure()
        & $cleanupGuard|Out-Null
        $current=Assert-CompleteHelperRegistration $target -AllowAbsent
        if($current){
            Invoke-CompleteHelperNative $target 'disable' $cleanupGuard
            $current=Assert-CompleteHelperRegistration $target
            if($current.State-ne'Stopped'){
                if($current.State-ne'Stop Pending'){Invoke-CompleteHelperNative $target 'stop' $cleanupGuard}
                Wait-CompleteHelperTargetState $target 'Stopped' $cleanupGuard|Out-Null
            }
            $current=Assert-CompleteHelperRegistration $target
            if($current.State-ne'Stopped'-or[int]$current.ProcessId-ne0-or$current.StartMode-ne'Disabled'){throw 'isolated_target_quiescence_unconfirmed'}
        }elseif($target.StartRequested){throw 'started_target_registration_missing'}
        $packageVerified=$false
        if($target.CopyCompleted){
            Assert-CompleteHelperManifest $target.App $target.ManifestSha256 $cleanupGuard -WithCanaries|Out-Null
            $packageVerified=$true
        }
        $abortCodes=@()
        if(Test-Path -LiteralPath $target.Data -PathType Container){
            Assert-CompleteHelperPlainPath $target.Data
            $markers=@(Get-ChildItem -LiteralPath $target.Data -Filter 'resource-abort-*.json' -File -Force -ErrorAction Stop)
            if($markers.Count-gt0){$resourceAbort=$true}
            if($markers.Count-gt16){throw 'target_resource_abort_metadata_invalid'}
            foreach($marker in $markers){
                Assert-CompleteHelperPlainPath $marker.FullName
                if($marker.Length-le0-or$marker.Length-gt1024){throw 'target_resource_abort_metadata_invalid'}
                $record=Get-Content -LiteralPath $marker.FullName -Raw -ErrorAction Stop|ConvertFrom-Json -ErrorAction Stop
                if($record.runId-cne$target.RunId-or[int]$record.processId-le4-or[string]$record.code-notin@(
                    'memory_reading_unavailable','hard_free_memory_floor_512_mib','soft_free_memory_floor_768_mib',
                    'disk_free_below_20_gib','resource_guard_failed')){throw 'target_resource_abort_metadata_invalid'}
                $abortCodes+=[string]$record.code
            }
        }
        if($guardErrors.Count-gt0){$failed=$true}
        $results.Add([pscustomobject]@{RunId=$target.RunId;Name=$target.Name;State=$(if($current){$current.State}else{'NotCreated'})
            ProcessId=0;StartMode=$(if($current){$current.StartMode}else{'NotCreated'});Quiescent=$true
            PackageVerified=$packageVerified;PartialSetup=(-not$target.CopyCompleted);Root=$target.Root;Data=$target.Data
            GuardFailed=($guardErrors.Count-gt0);ResourceAbort=($abortCodes.Count-gt0);ResourceFailureCodes=$abortCodes
            Preserved=$true;NativeOperations=@($target.NativeOperations.ToArray())})
      }catch{
        $failed=$true
        $results.Add([pscustomobject]@{RunId=$target.RunId;Name=$target.Name;Quiescent=$false;Preserved=$true;FailureCode='target_cleanup_unconfirmed';failure=(Get-CompleteFailureDiagnostic $_ 'helper_cleanup')})
      }
    }
    if($resourceAbort-or$failed){
        $reason=if($resourceAbort){'target_resource_abort'}else{'target_cleanup_unconfirmed'}
        $error=[InvalidOperationException]::new($reason)
        $error.Data['TargetResults']=$results.ToArray();$error.Data['Targets']=$Targets
        throw $error
    }
    return $results.ToArray()
}

Export-ModuleMember -Function Get-CompleteHelperTargetPlan,Get-CompleteHelperNativeArguments,Assert-CompleteHelperTargetMetadata,Assert-CompleteHelperManifest,Assert-CompleteHelperPlainPath,Assert-CompleteHelperRegistration,Get-CompleteHelperTreeFiles,New-CompleteHelperTargets,Assert-CompleteHelperFactoryResult,Confirm-CompleteHelperTargets

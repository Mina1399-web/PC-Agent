[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$PackagePath,
    [Parameter(Mandatory)][string]$ExpectedManifestSha256,
    [Parameter(Mandatory)][string]$ExpectedSourceCommit,
    [Parameter(Mandatory)][string]$ExpectedVersion,
    [string]$ExpectedRequestGuid,
    [string]$ExpectedIncidentId,
    [string]$ExpectedServiceName,
    [string]$ExpectedServiceId,
    [ValidateSet('StartService')][string]$ExpectedOperation,
    [string]$ExpectedRequestDigest,
    [switch]$Execute
)

$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$script:SnapshotSharedPlanPath=Join-Path $PSScriptRoot 'Read-InstalledDevConfirmationPlan.ps1'
$script:SnapshotSharedPlanSha256='4BB381E09F7782AF0F3BE6024008D0AC0E1DFB8F41C0A58257BCE0BBF82D6097'
$script:SnapshotPythonPath=Join-Path $env:USERPROFILE '.cache\codex-runtimes\codex-primary-runtime\dependencies\python\python.exe'
$script:SnapshotPythonSha256='73DBF1F40507275D6FA636B49EB80B4753E313F649F52591ABBA86B31F1A1BCE'
$script:SnapshotMemoryHardMiB=512.0
$script:SnapshotDiskFloorBytes=10GB
$script:SnapshotDiskHeadroomBytes=256MB
$script:SnapshotDatabaseCapBytes=1GB
$script:SnapshotServiceStopTimeoutSeconds=30
$script:SnapshotServiceStartTimeoutSeconds=45
$script:SnapshotProbeTimeoutSeconds=120
$script:SnapshotLowMemoryWarning=$false

function New-SnapshotDatabaseState {
    param([Parameter(Mandatory)][ValidateSet('host-agent','notifications','helper-journal')][string]$Name,[Parameter(Mandatory)][bool]$MainExists)
    if(-not $MainExists){
        if($Name -eq 'helper-journal'){return [pscustomobject]@{Name=$Name;Status='NOTRUN';FailureCode='lazy_database_absent';MainExists=$false;SourceBytes=0L;CopyVerified=$false;QuickCheck='NOTRUN';SchemaValid=$false;JournalLinkage='NOTRUN';RequestGuidCounts=$null}}
        return [pscustomobject]@{Name=$Name;Status='FAIL';FailureCode='required_database_missing';MainExists=$false;SourceBytes=0L;CopyVerified=$false;QuickCheck='NOTRUN';SchemaValid=$false;JournalLinkage='NOTRUN';RequestGuidCounts=$null}
    }
    [pscustomobject]@{Name=$Name;Status='NOTRUN';FailureCode=$null;MainExists=$true;SourceBytes=0L;CopyVerified=$false;QuickCheck='NOTRUN';SchemaValid=$false;JournalLinkage='NOTRUN';RequestGuidCounts=$null}
}

function Get-SnapshotDatabaseSchemaContract {
    param([Parameter(Mandatory)][ValidateSet('host-agent','notifications','helper-journal')][string]$Name)
    switch($Name){
        'host-agent'{return [pscustomobject]@{VersionTable='schema_migrations';MinimumVersion=3;Tables=[ordered]@{
            schema_migrations=@('version','applied_at_utc');telemetry_snapshots=@('snapshot_id','collector_id','payload_schema_version','sampled_at_utc','payload_json')
            baseline_samples=@('sample_id','metric_name','scope','sampled_at_utc','value');hourly_metric_aggregates=@('metric_name','scope','bucket_start_utc','sample_count','sum_value','minimum','maximum')
            daily_metric_aggregates=@('metric_name','scope','bucket_start_utc','sample_count','sum_value','minimum','maximum');incidents=@('incident_id','correlation_key','state','created_at_utc','updated_at_utc','payload_json')
            incident_snapshots=@('snapshot_id','incident_id','captured_at_utc','payload_json');diagnostic_evidence=@('evidence_id','incident_id','evidence_type','recorded_at_utc','payload_json')
            audit_entries=@('audit_id','recorded_at_utc','initiator','incident_id','diagnosis','confidence','evidence_references_json','requested_action','policy_decision','approval_status','execution_started_at_utc','execution_ended_at_utc','result','error')
            remediation_attempts=@('attempt_id','incident_id','operation_id','status','requested_at_utc','payload_json');service_health=@('health_id','component_id','status','observed_at_utc','payload_json')
            collector_checkpoints=@('collector_id','checkpoint_key','checkpoint_value','updated_at_utc');baseline_origins=@('metric_name','scope','started_at_utc')};
            Indexes=@('ix_telemetry_collector_time','ix_baseline_metric_scope_time','ix_incidents_state_update','ix_incident_snapshots_incident_time','ix_diagnostic_evidence_incident_time','ix_remediation_incident_time','ix_service_health_component_time');
            Triggers=@('audit_entries_no_update','audit_entries_no_delete','baseline_origin_on_sample')}}
        'notifications'{return [pscustomobject]@{VersionTable='notification_schema';MinimumVersion=1;Tables=[ordered]@{
            notification_schema=@('version');notifications=@('id','payload','state','code','receipt','attempts','created','next');notification_events=@('sequence','id','state','code','at')};
            Indexes=@('notification_due');Triggers=@('notification_events_no_update','notification_events_no_delete')}}
        'helper-journal'{return [pscustomobject]@{VersionTable='helper_journal_schema';MinimumVersion=1;Tables=[ordered]@{
            helper_journal_schema=@('version');helper_audit=@('sequence','audit_id','request_id','payload_json');helper_approvals=@('approval_id','request_id','payload_json')
            helper_claims=@('request_id','approval_id','incident_id','operation','target_key','started_at_utc','request_digest')};
            Indexes=@('ix_helper_audit_request','ix_helper_claims_target_time','ix_helper_claims_incident_operation');
            Triggers=@('helper_audit_no_UPDATE','helper_audit_no_DELETE','helper_approvals_no_UPDATE','helper_approvals_no_DELETE','helper_claims_no_UPDATE','helper_claims_no_DELETE')}}
    }
}

function Assert-SnapshotNoReparsePath {
    param([Parameter(Mandatory)][string]$Path,[switch]$AllowMissingLeaf)
    $full=[IO.Path]::GetFullPath($Path);$cursor=$full
    while(-not [string]::IsNullOrWhiteSpace($cursor)){
        if(Test-Path -LiteralPath $cursor){if((Get-Item -LiteralPath $cursor -Force -ErrorAction Stop).Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'reparse_path_rejected'}}
        elseif(-not($AllowMissingLeaf -and $cursor -ceq $full)){throw 'snapshot_path_missing'}
        $parent=[IO.Path]::GetDirectoryName($cursor);if([string]::IsNullOrWhiteSpace($parent)-or $parent -ceq $cursor){break};$cursor=$parent
    }
}

function Get-SnapshotDatabaseDefinitions {
    @([pscustomobject]@{Name='host-agent';Path='C:\ProgramData\HostAgent\host-agent.db';Service='HostAgent';Required=$true},
      [pscustomobject]@{Name='notifications';Path='C:\ProgramData\HostAgent\notifications.db';Service='HostAgent';Required=$true},
      [pscustomobject]@{Name='helper-journal';Path='C:\ProgramData\HostAgent.Helper\helper-journal.db';Service='HostAgent.Helper';Required=$false})
}

function Get-SnapshotDatabaseFiles {
    param([Parameter(Mandatory)]$Definition)
    $files=[Collections.Generic.List[object]]::new()
    foreach($suffix in @('','-wal','-shm','-journal')){
        $path=$Definition.Path+$suffix;if(-not [IO.File]::Exists($path)){continue};Assert-SnapshotNoReparsePath $path
        $file=Get-Item -LiteralPath $path -Force -ErrorAction Stop;if($file.PSIsContainer){throw 'database_file_not_regular'}
        $files.Add([pscustomobject]@{Source=$path;Name=[IO.Path]::GetFileName($path);Length=[long]$file.Length})
    }
    $main=[bool]($files|Where-Object Source -CEQ $Definition.Path)
    if(-not$main-and$files.Count -gt 0){throw 'orphan_database_sidecar'}
    if($Definition.Required -and -not $main){throw 'required_database_missing'}
    [long]$total=0;if($files.Count){$total=[long](($files|Measure-Object Length -Sum).Sum)}
    [pscustomobject]@{Name=$Definition.Name;MainExists=$main;Files=@($files);TotalBytes=$total}
}

function Get-SnapshotResourceState {[pscustomobject]@{FreeMemoryMiB=[double](Get-CimInstance Win32_OperatingSystem).FreePhysicalMemory/1024.0;FreeDiskBytes=[long](Get-PSDrive C).Free}}
function Assert-SnapshotRuntimeResources {
    param([long]$RequiredCopyBytes=0)
    $state=Get-SnapshotResourceState
    if($state.FreeMemoryMiB -le $script:SnapshotMemoryHardMiB){throw 'hard_memory_floor_512_mib'}
    if($state.FreeMemoryMiB -le 768.0){$script:SnapshotLowMemoryWarning=$true}
    if($state.FreeDiskBytes -lt ($script:SnapshotDiskFloorBytes+$RequiredCopyBytes+$script:SnapshotDiskHeadroomBytes)){throw 'disk_floor_or_copy_headroom'}
    $state
}

function Copy-SnapshotFileBounded {
    param([Parameter(Mandatory)][string]$Source,[Parameter(Mandatory)][string]$Destination,[long]$RequiredCopyBytes=0)
    $input=$null;$output=$null;$hash=$null
    try{
        Assert-SnapshotNoReparsePath $Source
        $input=[IO.File]::Open($Source,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read)
        $output=[IO.File]::Open($Destination,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
        $hash=[Security.Cryptography.IncrementalHash]::CreateHash([Security.Cryptography.HashAlgorithmName]::SHA256);$buffer=[byte[]]::new(1MB);[long]$count=0
        while(($read=$input.Read($buffer,0,$buffer.Length)) -gt 0){Assert-SnapshotRuntimeResources -RequiredCopyBytes $RequiredCopyBytes|Out-Null;$output.Write($buffer,0,$read);$hash.AppendData($buffer,0,$read);$count+=$read}
        $output.Flush($true);$sourceHash=[Convert]::ToHexString($hash.GetHashAndReset());$output.Dispose();$output=$null;$input.Dispose();$input=$null
        $destFile=Get-Item -LiteralPath $Destination -Force -ErrorAction Stop
        if($count -ne $destFile.Length -or $count -ne (Get-Item -LiteralPath $Source).Length -or $sourceHash -cne (Get-FileHash -LiteralPath $Destination -Algorithm SHA256).Hash){throw 'snapshot_copy_verification_failed'}
        [pscustomobject]@{Length=$count;Sha256=$sourceHash}
    }finally{if($output){$output.Dispose()};if($input){$input.Dispose()};if($hash){$hash.Dispose()}}
}

function New-SnapshotDatabaseCopies {
    param([Parameter(Mandatory)]$Inventory,[Parameter(Mandatory)][string]$DestinationRoot,[long]$AllDatabasesBytes=0)
    if($Inventory.TotalBytes -gt 1GB -or $AllDatabasesBytes -gt 1GB){throw 'database_copy_size_cap'}
    Assert-SnapshotNoReparsePath -Path $DestinationRoot -AllowMissingLeaf;[void][IO.Directory]::CreateDirectory($DestinationRoot)
    $root=Join-Path $DestinationRoot $Inventory.Name;[void][IO.Directory]::CreateDirectory($root);Assert-SnapshotNoReparsePath $root
    $copies=[Collections.Generic.List[object]]::new()
    foreach($item in $Inventory.Files){$dest=Join-Path $root $item.Name;$copied=Copy-SnapshotFileBounded -Source $item.Source -Destination $dest -RequiredCopyBytes $AllDatabasesBytes;$copies.Add([pscustomobject]@{Path=$dest;Length=$copied.Length;Sha256=$copied.Sha256})}
    $mainFile=@($Inventory.Files|Where-Object {$_.Name -notmatch '(-wal|-shm|-journal)$'})[0]
    $mainPath=Join-Path $root $mainFile.Name
    [pscustomobject]@{Name=$Inventory.Name;Root=$root;MainPath=$mainPath;Files=@($copies);TotalBytes=[long](($copies|Measure-Object Length -Sum).Sum);CopyVerified=$true}
}

function Get-SnapshotPythonProbeSource {@'
import json,pathlib,sqlite3,sys,time
def probe(item):
 name=item["name"];conn=sqlite3.connect(pathlib.Path(item["path"]).as_uri()+"?mode=ro",uri=True,timeout=2)
 result={"name":name,"quickCheck":"NOTRUN","schemaValid":False,"journalLinkage":"NOTRUN","failureCode":None,"requestGuidCounts":None}
 try:
  conn.execute("PRAGMA query_only=ON");conn.execute("PRAGMA temp_store=MEMORY");deadline=time.monotonic()+120;conn.set_progress_handler(lambda:1 if time.monotonic()>deadline else 0,10000)
  rows=conn.execute("PRAGMA quick_check(1)").fetchmany(2)
  if len(rows)!=1 or rows[0][0]!="ok":raise RuntimeError("quick_check_failed")
  result["quickCheck"]="ok";expected=item["schemaContract"];tables=expected["tables"]
  actual={row[0] for row in conn.execute("SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%'")}
  if actual!=set(tables):raise RuntimeError("schema_table_set_mismatch")
  for table,columns in tables.items():
   found={row[1] for row in conn.execute('PRAGMA table_info("'+table+'")')}
   if found!=set(columns):raise RuntimeError("schema_column_set_mismatch")
  for kind,names in (("index",expected["indexes"]),("trigger",expected["triggers"])):
   actualObjects={row[0] for row in conn.execute("SELECT name FROM sqlite_master WHERE type=? AND name NOT LIKE 'sqlite_%'",(kind,))}
   if actualObjects!=set(names):raise RuntimeError("schema_object_set_mismatch")
  version=conn.execute('SELECT MAX(version) FROM "'+expected["versionTable"]+'"').fetchone()[0]
  if version!=expected["version"]:raise RuntimeError("schema_version_mismatch")
  result["schemaValid"]=True
  if name=="helper-journal" and item.get("linkage"):
   link=item["linkage"];req=link["requestGuid"];incident=link["incidentId"];service=link["serviceName"];serviceId=link["serviceId"];digest=link["requestDigest"]
   approvalRows=conn.execute("SELECT approval_id FROM helper_approvals WHERE request_id=? AND json_extract(payload_json,'$.IncidentId')=? AND json_extract(payload_json,'$.Operation')=0 AND json_extract(payload_json,'$.RequestDigest')=? AND json_extract(payload_json,'$.Target.ServiceName')=?",(req,incident,digest,service)).fetchmany(2)
   approval=len(approvalRows);approvalId=approvalRows[0][0] if approval==1 else None
   claim=conn.execute("SELECT COUNT(*) FROM helper_claims WHERE request_id=? AND approval_id=? AND incident_id=? AND operation='StartService' AND target_key=? AND request_digest=?",(req,approvalId,incident,'service:'+serviceId,digest)).fetchone()[0] if approvalId else 0
   common="request_id=? AND json_extract(payload_json,'$.IncidentId')=? AND json_extract(payload_json,'$.Operation')=0 AND json_extract(payload_json,'$.RequestDigest')=?"
   serviceBound="json_extract(json_extract(payload_json,'$.ParametersJson'),'$.serviceId')=?"
   approvalAuditRows=conn.execute("SELECT sequence FROM helper_audit WHERE "+common+" AND json_extract(payload_json,'$.Event')=2 AND json_extract(payload_json,'$.ApprovalId')=? AND json_extract(payload_json,'$.Code')='approved'",(req,incident,digest,approvalId)).fetchmany(2) if approvalId else []
   audit={event:conn.execute("SELECT COUNT(*) FROM helper_audit WHERE "+common+" AND json_extract(payload_json,'$.Event')=? AND "+serviceBound,(req,incident,digest,event,serviceId)).fetchone()[0] for event in (0,3,4)}
   success=conn.execute("SELECT COUNT(*) FROM helper_audit WHERE "+common+" AND json_extract(payload_json,'$.Event')=4 AND "+serviceBound+" AND json_extract(payload_json,'$.Code')='succeeded'",(req,incident,digest,serviceId)).fetchone()[0]
   sequenceRows={event:conn.execute("SELECT sequence FROM helper_audit WHERE "+common+" AND json_extract(payload_json,'$.Event')=? AND "+serviceBound,(req,incident,digest,event,serviceId)).fetchmany(2) for event in (0,3,4)}
   sequenceValid=(len(approvalAuditRows)==1 and all(len(sequenceRows[event])==1 for event in (0,3,4)) and approvalAuditRows[0][0]<sequenceRows[0][0][0]<sequenceRows[3][0][0]<sequenceRows[4][0][0])
   allEnd=conn.execute("SELECT COUNT(*) FROM helper_audit WHERE request_id=? AND json_extract(payload_json,'$.Event')=4",(req,)).fetchone()[0]
   counts={"approval":approval,"claim":claim,"approvalAudit":len(approvalAuditRows),"proposed":audit[0],"started":audit[3],"ended":audit[4],"succeeded":success,"endedAnyCode":allEnd,"sequenceValid":bool(sequenceValid)}
   result["requestGuidCounts"]=counts;result["journalLinkage"]="PASS" if approval==claim==len(approvalAuditRows)==audit[0]==audit[3]==audit[4]==success==allEnd==1 and sequenceValid else "FAIL"
  return result
 finally:conn.close()
inputs=json.loads(SNAPSHOT_INPUT_JSON);out=[]
for item in inputs["databases"]:
 try:out.append(probe(item))
 except Exception as exc:out.append({"name":item.get("name"),"quickCheck":"NOTRUN","schemaValid":False,"journalLinkage":"FAIL" if item.get("linkage") else "NOTRUN","failureCode":str(exc).split(':',1)[0][:80],"requestGuidCounts":None})
print(json.dumps({"databases":out},separators=(",",":")))
'@}

function Invoke-SnapshotSqliteProbe {
    param([Parameter(Mandatory)][object[]]$Copies,[string]$RequestGuid,[object]$Linkage)
    Assert-SnapshotNoReparsePath $script:SnapshotPythonPath
    if(-not(Test-Path -LiteralPath $script:SnapshotPythonPath -PathType Leaf)-or(Get-FileHash -LiteralPath $script:SnapshotPythonPath -Algorithm SHA256).Hash -cne $script:SnapshotPythonSha256){throw 'pinned_python_unavailable'}
    $items=@(foreach($copy in $Copies){$name=[string]$copy.Name;$contract=Get-SnapshotDatabaseSchemaContract -Name $name;$item=[ordered]@{name=$name;path=$copy.MainPath;schemaContract=[ordered]@{versionTable=$contract.VersionTable;version=[int]$contract.MinimumVersion;tables=$contract.Tables;indexes=@($contract.Indexes);triggers=@($contract.Triggers)}};if($name -eq 'helper-journal' -and $Linkage){$item.linkage=@{requestGuid=$Linkage.RequestGuid;incidentId=$Linkage.IncidentId;serviceName=$Linkage.ServiceName;serviceId=$Linkage.ServiceId;operation=$Linkage.Operation;requestDigest=$Linkage.RequestDigest}};$item})
    $json=ConvertTo-Json -InputObject ([pscustomobject]@{databases=$items}) -Compress -Depth 8
    $inputBase64=[Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($json))
    $pythonSource="SNAPSHOT_INPUT_JSON=__import__('base64').b64decode('$inputBase64')`n"+(Get-SnapshotPythonProbeSource)
    $info=[Diagnostics.ProcessStartInfo]::new();$info.FileName=$script:SnapshotPythonPath;$info.UseShellExecute=$false;$info.CreateNoWindow=$true;$info.RedirectStandardInput=$true;$info.RedirectStandardOutput=$true;$info.RedirectStandardError=$true
    foreach($arg in @('-I','-B','-')){$info.ArgumentList.Add($arg)}
    $process=[Diagnostics.Process]::new();$process.StartInfo=$info
    try{
        if(-not $process.Start()){throw 'sqlite_probe_start_failed'}
        $stdoutTask=$process.StandardOutput.ReadToEndAsync();$stderrTask=$process.StandardError.ReadToEndAsync();$writeTask=$process.StandardInput.WriteAsync($pythonSource);if(-not $writeTask.Wait(10000)){try{$process.Kill($true)}catch{};throw 'sqlite_probe_stdin_write_timeout'};$process.StandardInput.Close();$watch=[Diagnostics.Stopwatch]::StartNew()
        while(-not $process.WaitForExit(250)){Assert-SnapshotRuntimeResources|Out-Null;if($watch.Elapsed.TotalSeconds -ge 120){try{$process.Kill($true)}catch{};throw 'sqlite_probe_timeout'}}
        $stdout=$stdoutTask.GetAwaiter().GetResult();$stderr=$stderrTask.GetAwaiter().GetResult()
        if($stdout.Length -gt 65536 -or $stderr.Length -gt 8192){throw 'sqlite_probe_output_cap'}
        if($process.ExitCode -ne 0){throw 'sqlite_probe_failed'}
        $parsed=ConvertFrom-Json -InputObject $stdout -Depth 10
        if(@($parsed.databases).Count -ne $Copies.Count){throw 'sqlite_result_identity_mismatch'}
        @($parsed.databases)
    }finally{if(-not $process.HasExited){try{$process.Kill($true)}catch{}};$process.Dispose()}
}

function Get-SnapshotSharedManifest {
    param([string]$Root,[string]$ManifestHash,[string]$Commit,[string]$Version)
    if((Get-FileHash -LiteralPath $script:SnapshotSharedPlanPath -Algorithm SHA256).Hash -cne $script:SnapshotSharedPlanSha256){throw 'shared_confirmation_plan_hash_mismatch'}
    . $script:SnapshotSharedPlanPath -PackagePath $Root -ExpectedManifestSha256 $ManifestHash -ExpectedSourceCommit $Commit -ExpectedVersion $Version -Execute:$false
    Read-ModeManifest -LiteralPath (Join-Path $Root 'manifest.psd1') -ExpectedManifestSha256 $ManifestHash -ExpectedSourceCommit $Commit -ExpectedVersion $Version
}
function Get-SnapshotModeState {
    param([string]$Root,[string]$ManifestHash,[string]$Commit,[string]$Version)
    if((Get-FileHash -LiteralPath $script:SnapshotSharedPlanPath -Algorithm SHA256).Hash -cne $script:SnapshotSharedPlanSha256){throw 'shared_confirmation_plan_hash_mismatch'}
    . $script:SnapshotSharedPlanPath -PackagePath $Root -ExpectedManifestSha256 $ManifestHash -ExpectedSourceCommit $Commit -ExpectedVersion $Version -Execute:$false
    $serviceSid=([Security.Principal.NTAccount]::new('NT SERVICE\HostAgent')).Translate([Security.Principal.SecurityIdentifier]).Value
    $service=Read-ProtectedModeJson -Path 'C:\ProgramData\HostAgent\service.json' -AllowedFields @('mode','readOnly','killSwitch','collectionIntervalSeconds','heartbeatIntervalSeconds','requestTimeoutSeconds','enablePushover','enableHeartbeat','enableNetworkProbes') -TrustedSids @('S-1-5-18','S-1-5-32-544',$serviceSid) -Kind Service
    $helper=Read-ProtectedModeJson -Path 'C:\ProgramData\HostAgent.Helper\helper.json' -AllowedFields @('readOnly','shadow','killSwitch','executionTimeoutSeconds','services','processes','approverSids') -TrustedSids @('S-1-5-18','S-1-5-32-544') -Kind Helper
    [pscustomobject]@{Mode=$service.mode;ReadOnly=$service.readOnly;Pushover=$service.enablePushover;Heartbeat=$service.enableHeartbeat;NetworkProbes=if($service.Contains('enableNetworkProbes')){$service.enableNetworkProbes}else{$false};HelperReadOnly=$helper.readOnly;HelperShadow=$helper.shadow;ServiceConfigurationSha256=(Get-FileHash -LiteralPath 'C:\ProgramData\HostAgent\service.json' -Algorithm SHA256).Hash;HelperConfigurationSha256=(Get-FileHash -LiteralPath 'C:\ProgramData\HostAgent.Helper\helper.json' -Algorithm SHA256).Hash}
}

function Get-SnapshotServiceDefinitions {
    @([pscustomobject]@{Name='HostAgent';StartName='NT SERVICE\HostAgent';PathName='"C:\Program Files\HostAgent\Service\HostAgent.Service.exe"';Executable='C:\Program Files\HostAgent\Service\HostAgent.Service.exe';Library='C:\Program Files\HostAgent\Service\HostAgent.Service.dll';ExeRelative='Service/HostAgent.Service.exe';DllRelative='Service/HostAgent.Service.dll';InitialState=$null;OriginallyRunning=$false;StopRequested=$false},
      [pscustomobject]@{Name='HostAgent.Helper';StartName='LocalSystem';PathName='"C:\Program Files\HostAgent\Helper\HostAgent.Helper.exe"';Executable='C:\Program Files\HostAgent\Helper\HostAgent.Helper.exe';Library='C:\Program Files\HostAgent\Helper\HostAgent.Helper.dll';ExeRelative='Helper/HostAgent.Helper.exe';DllRelative='Helper/HostAgent.Helper.dll';InitialState=$null;OriginallyRunning=$false;StopRequested=$false})
}
function Assert-SnapshotInstalledIdentity {
    param([Parameter(Mandatory)]$Manifest,[Parameter(Mandatory)][object[]]$Services,[switch]$CaptureInitialState,[switch]$RequireInitialState,[switch]$AllowPending)
    $actual=@(Get-CimInstance Win32_Service -Filter "Name='HostAgent' OR Name='HostAgent.Helper'");if($actual.Count -ne 2){throw 'service_missing'}
    foreach($expected in $Services){
        $found=@($actual|Where-Object Name -CEQ $expected.Name)
        $allowedStates=if($AllowPending){@('Running','Stopped','Start Pending','Stop Pending')}else{@('Running','Stopped')}
        if($found.Count -ne 1-or$found[0].StartMode -cne 'Auto'-or$found[0].StartName -cne $expected.StartName-or$found[0].PathName -cne $expected.PathName-or$found[0].State -notin $allowedStates){throw 'service_identity_guard_failed'}
        if($CaptureInitialState){if($null -ne $expected.InitialState){throw 'service_initial_state_already_captured'};$expected.InitialState=$found[0].State;$expected.OriginallyRunning=($found[0].State -ceq 'Running')}
        if($RequireInitialState -and $found[0].State -cne $expected.InitialState){throw 'service_state_changed_before_stop'}
        foreach($relative in @($expected.ExeRelative,$expected.DllRelative)){
            $entry=@($Manifest.Files|Where-Object Path -CEQ $relative);if($entry.Count -ne 1){throw 'manifest_service_file_missing'}
            $path=if($relative -ceq $expected.ExeRelative){$expected.Executable}else{$expected.Library}
            Assert-SnapshotNoReparsePath $path;$file=Get-Item -LiteralPath $path -Force -ErrorAction Stop
            if($file.PSIsContainer-or$file.Length -ne $entry[0].Length-or(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -cne $entry[0].Sha256){throw 'installed_binary_identity_mismatch'}
        }
    }
}
function Get-SnapshotServiceState([string]$Name){$found=@(Get-CimInstance Win32_Service -Filter "Name='$Name'");if($found.Count -ne 1){throw 'service_missing'};$found[0]}
function Wait-SnapshotServiceState {
    param([string]$Name,[string]$State,[int]$TimeoutSeconds,[switch]$ForRecovery,$Manifest,[object[]]$Services)
    $watch=[Diagnostics.Stopwatch]::StartNew();while($watch.Elapsed.TotalSeconds -lt $TimeoutSeconds){if($ForRecovery){Assert-SnapshotInstalledIdentity -Manifest $Manifest -Services $Services -AllowPending}else{Assert-SnapshotRuntimeResources|Out-Null};$current=Get-SnapshotServiceState $Name;if($current.State -ceq $State){return $current};Start-Sleep -Milliseconds 250};throw 'service_state_timeout'
}
function Stop-SnapshotServices([object[]]$Services){foreach($service in $Services){$current=Get-SnapshotServiceState $service.Name;if($current.State -cne $service.InitialState){throw 'service_state_changed_before_stop'};if($service.OriginallyRunning){$service.StopRequested=$true;Stop-Service -Name $service.Name -ErrorAction Stop;[void](Wait-SnapshotServiceState $service.Name 'Stopped' 30)}}}
function Restore-SnapshotServices {
    param([object[]]$Services,$Manifest)
    $failures=[Collections.Generic.List[string]]::new()
    $restoreOrder=@($Services);[array]::Reverse($restoreOrder)
    foreach($service in $restoreOrder){
        if(-not$service.OriginallyRunning-or-not$service.StopRequested){continue}
        try{
            Assert-SnapshotInstalledIdentity -Manifest $Manifest -Services $Services -AllowPending
            $actual=Get-SnapshotServiceState $service.Name
            if($actual.State -eq 'Start Pending'){[void](Wait-SnapshotServiceState -Name $service.Name -State 'Running' -TimeoutSeconds $script:SnapshotServiceStartTimeoutSeconds -ForRecovery -Manifest $Manifest -Services $Services);$actual=Get-SnapshotServiceState $service.Name}
            if($actual.State -eq 'Stop Pending'){[void](Wait-SnapshotServiceState -Name $service.Name -State 'Stopped' -TimeoutSeconds $script:SnapshotServiceStartTimeoutSeconds -ForRecovery -Manifest $Manifest -Services $Services);$actual=Get-SnapshotServiceState $service.Name}
            if($actual.State -eq 'Running'){continue}
            if($actual.State -ne 'Stopped'){throw 'recovery_service_state_ambiguous'}
            Assert-SnapshotInstalledIdentity -Manifest $Manifest -Services $Services -AllowPending
            Start-Service -Name $service.Name -ErrorAction Stop
            [void](Wait-SnapshotServiceState -Name $service.Name -State 'Running' -TimeoutSeconds $script:SnapshotServiceStartTimeoutSeconds -ForRecovery -Manifest $Manifest -Services $Services)
            Assert-SnapshotInstalledIdentity -Manifest $Manifest -Services $Services -AllowPending
        }catch{$code=if($_.Exception.Message -match '^[a-z0-9_]+$'){$_.Exception.Message}else{'restore_action_failed'};$failures.Add($service.Name+':'+$code)}
    }
    try{Assert-SnapshotInstalledIdentity -Manifest $Manifest -Services $Services}catch{$code=if($_.Exception.Message -match '^[a-z0-9_]+$'){$_.Exception.Message}else{'final_identity_unverified'};$failures.Add('final_identity:'+ $code)}
    foreach($service in $Services){
        try{$final=Get-SnapshotServiceState $service.Name;$expectedFinal=if($service.OriginallyRunning){'Running'}else{$service.InitialState};if($final.State -cne $expectedFinal){$failures.Add($service.Name+':final_state_not_restored')}}catch{$failures.Add($service.Name+':final_state_unavailable')}
    }
    if($failures.Count){throw ('service_restore_failed:' + ($failures -join ','))}
}

function Get-SnapshotRequestLinkage {
    param([string]$RequestGuid,[string]$IncidentId,[string]$ServiceName,[string]$ServiceId,[string]$Operation,[string]$RequestDigest)
    $present=@($RequestGuid,$IncidentId,$ServiceName,$ServiceId,$Operation,$RequestDigest|Where-Object {-not [string]::IsNullOrWhiteSpace($_)}).Count
    if($present -eq 0){return $null};if($present -ne 6){throw 'request_linkage_arguments_incomplete'}
    $parsed=[guid]::Empty;if(-not[guid]::TryParseExact($RequestGuid,'D',[ref]$parsed)-or$parsed -eq [guid]::Empty){throw 'request_guid_invalid'}
    if($IncidentId -cnotmatch '^[0-9a-f]{32}$'){throw 'request_incident_id_invalid'}
    if($ServiceName -cnotmatch '^0HostAgentDevAcceptance_[0-9a-f]{32}$'){throw 'request_service_name_invalid'}
    if($ServiceId -cnotmatch '^devaccept-[0-9a-f]{32}$' -or $ServiceId.Substring(10) -cne $ServiceName.Substring('0HostAgentDevAcceptance_'.Length)){throw 'request_service_id_invalid'}
    if($Operation -cne 'StartService'){throw 'request_operation_invalid'}
    if($RequestDigest -cnotmatch '^[A-F0-9]{64}$'){throw 'request_digest_invalid'}
    [pscustomobject]@{RequestGuid=$parsed.ToString('D');IncidentId=$IncidentId;ServiceName=$ServiceName;ServiceId=$ServiceId;Operation=$Operation;RequestDigest=$RequestDigest}
}

function Resolve-SnapshotIntegrityOutcome {
    param([Parameter(Mandatory)][object[]]$Databases,[bool]$LinkageRequested,[string]$JournalLinkage='NOTRUN')
    if(@($Databases|Where-Object Status -eq 'FAIL').Count){return [pscustomobject]@{Outcome='blocked';FailureCode='database_integrity_failed'}}
    if(@($Databases|Where-Object Status -ne 'PASS').Count){return [pscustomobject]@{Outcome='integrity_incomplete';FailureCode=$null}}
    if($LinkageRequested -and $JournalLinkage -cne 'PASS'){return [pscustomobject]@{Outcome='integrity_incomplete';FailureCode='journal_linkage_failed'}}
    [pscustomobject]@{Outcome='passed';FailureCode=$null}
}

function Invoke-InstalledDatabaseSnapshot {
    param([bool]$DoExecute,[object]$Linkage)
    $out=[ordered]@{ResultSchemaVersion=1;Outcome='blocked';FailureCode=$null;Mode='NOTRUN';ModePreserved='NOTRUN';PackageValidated=$false;InstalledBinariesValidated=$false;ServicesBefore=@();ServicesAfter=@();Databases=@();JournalLinkage='NOTRUN';EvidenceRoot=$null;ConfigurationPerformed=$false;DatabaseSourceOpenedBySqlite=$false;LowMemoryWarningObserved=$false;ResourceFloors=[pscustomobject]@{StartAboveMiB=768;WarningMiB=768;EmergencyAtOrBelowMiB=512;DiskFloorGiB=10;DatabaseCapGiB=1};NextStep=''}
    $services=Get-SnapshotServiceDefinitions;$manifest=$null;$modeBefore=$null;$evidenceRoot=$null
    try{
        $identity=[Security.Principal.WindowsIdentity]::GetCurrent();$principal=[Security.Principal.WindowsPrincipal]::new($identity)
        if([IntPtr]::Size -ne 8-or$PSVersionTable.PSVersion.Major -lt 7){throw 'powershell7_x64_required'}
        if($env:COMPUTERNAME -cne 'AGENTPC' -or $identity.Name -cne 'AgentPC\AgentPCdevtest' -or (Get-CimInstance Win32_ComputerSystem).Model -cne 'VirtualBox' -or (Get-CimInstance Win32_ComputerSystemProduct).UUID -cne '46543236-8C5E-49DA-953C-DB17F9B1E402' -or (Get-Service VBoxService).Status -ne 'Running'){throw 'approved_dev_vm_required'}
        if($DoExecute){if(-not$principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)){throw 'administrator_required'};$resource=Get-SnapshotResourceState;if($resource.FreeMemoryMiB -le 512){throw 'hard_memory_floor_512_mib'};if($resource.FreeMemoryMiB -le 768){throw 'initial_free_memory_not_above_768_mib'};if($resource.FreeDiskBytes -lt 10GB){throw 'disk_free_below_10_gib'}}
        $manifest=Get-SnapshotSharedManifest $PackagePath $ExpectedManifestSha256 $ExpectedSourceCommit $ExpectedVersion;$out.PackageValidated=$true
        Assert-SnapshotInstalledIdentity -Manifest $manifest -Services $services -CaptureInitialState;$out.InstalledBinariesValidated=$true
        $modeBefore=Get-SnapshotModeState $PackagePath $ExpectedManifestSha256 $ExpectedSourceCommit $ExpectedVersion
        if($modeBefore.Mode -cne 'Confirmation'-or$modeBefore.ReadOnly -ne $false-or$modeBefore.HelperReadOnly -ne $false-or$modeBefore.HelperShadow -ne $false){throw 'confirmation_mode_required'}
        if($modeBefore.Pushover-or$modeBefore.Heartbeat-or$modeBefore.NetworkProbes){throw 'outbound_configuration_not_off'};$out.Mode='Confirmation'
        $out.ServicesBefore=@($services|ForEach-Object {[pscustomobject]@{Name=$_.Name;State=$_.InitialState;OriginallyRunning=$_.OriginallyRunning;StopRequested=$false}})
        $definitions=Get-SnapshotDatabaseDefinitions;$before=@(foreach($d in $definitions){Get-SnapshotDatabaseFiles $d});foreach($db in $before){if(-not$db.MainExists-and$db.Name -ne 'helper-journal'){throw 'required_database_missing'}}
        if([long](($before|Measure-Object TotalBytes -Sum).Sum) -gt 1GB){throw 'database_copy_size_cap'}
        if(-not$DoExecute){$out.Databases=@(foreach($db in $before){New-SnapshotDatabaseState $db.Name $db.MainExists});$out.Outcome='plan_ready';$out.NextStep='Review this plan. Add -Execute only on the approved DEV VM to create preserved database copies.'}
        else {
        $parent=Join-Path $env:LOCALAPPDATA 'HostAgent-DEV-evidence';if(-not(Test-Path -LiteralPath $parent)){[void][IO.Directory]::CreateDirectory($parent)};Assert-SnapshotNoReparsePath $parent
        $evidenceRoot=Join-Path $parent ('installed-db-snapshot-'+[guid]::NewGuid().ToString('N'));[void][IO.Directory]::CreateDirectory($evidenceRoot);$out.EvidenceRoot=$evidenceRoot
        Assert-SnapshotInstalledIdentity -Manifest $manifest -Services $services -RequireInitialState
        Stop-SnapshotServices -Services $services
        $after=@(foreach($d in $definitions){Get-SnapshotDatabaseFiles $d});foreach($db in $after){if(-not$db.MainExists-and$db.Name -ne 'helper-journal'){throw 'required_database_missing'}}
        $total=[long](($after|Measure-Object TotalBytes -Sum).Sum);if($total -gt 1GB){throw 'database_copy_size_cap'};Assert-SnapshotRuntimeResources -RequiredCopyBytes $total|Out-Null
        $states=[Collections.Generic.List[object]]::new();$copies=[Collections.Generic.List[object]]::new()
        foreach($inventory in $after){$state=New-SnapshotDatabaseState $inventory.Name $inventory.MainExists;$states.Add($state);if(-not$inventory.MainExists){continue};$copy=New-SnapshotDatabaseCopies $inventory $evidenceRoot $total;$state.SourceBytes=$copy.TotalBytes;$state.CopyVerified=$true;$copies.Add($copy)}
        $probe=@(Invoke-SnapshotSqliteProbe -Copies @($copies) -RequestGuid $(if($Linkage){$Linkage.RequestGuid}else{$null}) -Linkage $(if($Linkage){$Linkage}else{$null}))
        foreach($check in $probe){$state=@($states|Where-Object Name -CEQ $check.name)[0];$state.QuickCheck=$check.quickCheck;$state.SchemaValid=[bool]$check.schemaValid;$state.RequestGuidCounts=$check.requestGuidCounts;$state.JournalLinkage=$check.journalLinkage;if($check.quickCheck -eq 'ok'-and$check.schemaValid-and($check.journalLinkage -ne 'FAIL')){$state.Status='PASS'}else{$state.Status='FAIL';$state.FailureCode=if($check.failureCode){$check.failureCode}else{'database_integrity_failed'}}}
        $out.Databases=@($states);if($Linkage){$journal=@($states|Where-Object Name -CEQ 'helper-journal');$out.JournalLinkage=if($journal.Count -eq 1){$journal[0].JournalLinkage}else{'FAIL'}}
        $integrity=Resolve-SnapshotIntegrityOutcome -Databases @($states) -LinkageRequested ([bool]$Linkage) -JournalLinkage $out.JournalLinkage
        $out.Outcome=$integrity.Outcome;$out.FailureCode=$integrity.FailureCode
        if($out.Outcome -eq 'integrity_incomplete'){$out.NextStep=if($out.JournalLinkage -eq 'FAIL'){'Request-linked approval, claim, and successful audit chain did not validate.'}else{'Helper journal is absent or not verified; preserve this evidence and do not claim full integrity.'}}
        elseif($out.Outcome -eq 'passed'){$out.NextStep='Review the preserved snapshot and service restoration record before combining it with installed acceptance.'}
        else{throw 'database_integrity_failed'}
        }
    }catch{$out.FailureCode=if($_.Exception.Message -match '^[a-z0-9_:-]+$'){$_.Exception.Message}else{'snapshot_preflight_or_execution_failed'};$out.Outcome='blocked';$out.NextStep='Preserve evidence and resolve the blocker before any acceptance claim.'}
    finally{
        $restoreFailure=$null
        if($DoExecute-and$manifest){try{Restore-SnapshotServices -Services $services -Manifest $manifest}catch{$restoreFailure=$_.Exception.Message}}
        if($modeBefore){try{$modeAfter=Get-SnapshotModeState $PackagePath $ExpectedManifestSha256 $ExpectedSourceCommit $ExpectedVersion;$out.ModePreserved=($modeAfter.Mode -ceq $modeBefore.Mode-and$modeAfter.ServiceConfigurationSha256 -ceq $modeBefore.ServiceConfigurationSha256-and$modeAfter.HelperConfigurationSha256 -ceq $modeBefore.HelperConfigurationSha256)}catch{$out.ModePreserved=$false}}
        $out.ServicesAfter=@(foreach($service in $services){try{$current=Get-SnapshotServiceState $service.Name;[pscustomobject]@{Name=$service.Name;State=$current.State;OriginallyRunning=$service.OriginallyRunning;StopRequested=$service.StopRequested}}catch{[pscustomobject]@{Name=$service.Name;State='UNAVAILABLE';OriginallyRunning=$service.OriginallyRunning;StopRequested=$service.StopRequested}}})
        $out.LowMemoryWarningObserved=$script:SnapshotLowMemoryWarning
        if($restoreFailure){$out.Outcome='blocked';$out.FailureCode='service_restore_failed';$out.RestoreFailureCode=$restoreFailure;$out.NextStep='Service restoration failed. Preserve evidence and inspect both fixed service states.'}
        if($out.ModePreserved -eq $false){$out.Outcome='blocked';$out.FailureCode='configuration_mode_changed';$out.NextStep='Mode/configuration changed; preserve evidence and investigate before further action.'}
        if($evidenceRoot){$out.EvidenceRoot=$evidenceRoot}
    }
    [pscustomobject]$out
}

if($MyInvocation.InvocationName -ne '.'){
    $result=$null
    try{
        $link=Get-SnapshotRequestLinkage -RequestGuid $ExpectedRequestGuid -IncidentId $ExpectedIncidentId -ServiceName $ExpectedServiceName -ServiceId $ExpectedServiceId -Operation $ExpectedOperation -RequestDigest $ExpectedRequestDigest
        $result=Invoke-InstalledDatabaseSnapshot -DoExecute:$Execute.IsPresent -Linkage $link
    }catch{$code=if($_.Exception.Message -match '^[a-z0-9_:-]+$'){$_.Exception.Message}else{'operator_snapshot_failed'};$result=[pscustomobject]@{Outcome='blocked';FailureCode=$code;Mode='NOTRUN';ModePreserved='NOTRUN';PackageValidated=$false;InstalledBinariesValidated=$false;ServicesBefore=@();ServicesAfter=@();Databases=@();JournalLinkage='NOTRUN';EvidenceRoot=$null;ConfigurationPerformed=$false;DatabaseSourceOpenedBySqlite=$false;NextStep='Correct the blocker and rerun the read-only plan.'}}
    $json=ConvertTo-Json -InputObject $result -Depth 8 -Compress;if($json.Length -gt 65536){$json='{"Outcome":"blocked","FailureCode":"result_output_cap","DatabaseSourceOpenedBySqlite":false}'};$json
    if($result.Outcome -in @('blocked','integrity_incomplete')){exit 2}
}

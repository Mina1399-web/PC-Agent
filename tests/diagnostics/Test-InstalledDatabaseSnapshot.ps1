$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$script:assertionCount = 0

function Assert-True([bool]$Value, [string]$Message) {
    if (-not $Value) { throw "assertion_failed:$Message" }
    $script:assertionCount++
}
function Assert-Code([scriptblock]$Action,[string]$Expected) {
    try { & $Action; throw "expected_failure_not_observed:$Expected" }
    catch { if($_.Exception.Message -ne $Expected){throw "wrong_failure_code:expected=$Expected;actual=$($_.Exception.Message)"};$script:assertionCount++ }
}

function Invoke-FixturePython([string]$Code,[string[]]$Arguments=@()) {
    $python = Join-Path $env:USERPROFILE '.cache\codex-runtimes\codex-primary-runtime\dependencies\python\python.exe'
    $info = [Diagnostics.ProcessStartInfo]::new()
    $info.FileName = $python
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    $info.RedirectStandardInput = $true
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    foreach ($arg in @('-I','-B','-')+$Arguments) { $info.ArgumentList.Add($arg) }
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $info
    try {
        if (-not $process.Start()) { throw 'fixture_sqlite_start_failed' }
        $stdoutTask = $process.StandardOutput.ReadToEndAsync()
        $stderrTask = $process.StandardError.ReadToEndAsync()
        $writeTask = $process.StandardInput.WriteAsync($Code)
        if (-not $writeTask.Wait(10000)) { try { $process.Kill($true) } catch {}; throw 'fixture_sqlite_stdin_write_timeout' }
        $process.StandardInput.Close()
        if (-not $process.WaitForExit(30000)) { try { $process.Kill($true) } catch {}; throw 'fixture_sqlite_timeout' }
        $stdout = $stdoutTask.GetAwaiter().GetResult()
        $stderr = $stderrTask.GetAwaiter().GetResult()
        if ($process.ExitCode -ne 0 -or $stderr.Length -gt 4096) { throw 'fixture_sqlite_creation_failed' }
        return $stdout.Trim()
    } finally { $process.Dispose() }
}

$scriptPath = Join-Path $PSScriptRoot '..\..\scripts\diagnostics\Read-InstalledDatabaseSnapshot.ps1'
if (-not (Test-Path -LiteralPath $scriptPath -PathType Leaf)) {
    throw 'operator_script_missing'
}

$fixtureRoot = Join-Path ([IO.Path]::GetTempPath()) ('hostagent-db-snapshot-test-' + [guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($fixtureRoot)
try {
    . $scriptPath -PackagePath $fixtureRoot -ExpectedManifestSha256 ('0' * 64) `
        -ExpectedSourceCommit ('a' * 40) -ExpectedVersion '1.2.9'
    $script:actualWaitSnapshotServiceState=(Get-Command Wait-SnapshotServiceState).ScriptBlock

    # Defect caught: treating the helper's intentionally lazy, absent journal as a failed database check.
    try { $missingJournal = New-SnapshotDatabaseState -Name 'helper-journal' -MainExists $false }
    catch { throw 'missing_contract:lazy_journal_state' }
    Assert-True ($missingJournal.Status -eq 'NOTRUN' -and $missingJournal.FailureCode -eq 'lazy_database_absent') 'lazy_journal_is_explicit_notrun'
    Assert-True (-not $missingJournal.PSObject.Properties.Name.Contains('CreateDatabase')) 'journal_state_does_not_request_creation'
    $orphanJournal = Join-Path $fixtureRoot 'orphan-helper.db-wal'
    [IO.File]::WriteAllBytes($orphanJournal,[byte[]](1,2,3))
    Assert-Code { Get-SnapshotDatabaseFiles ([pscustomobject]@{Name='helper-journal';Path=(Join-Path $fixtureRoot 'orphan-helper.db');Required=$false}) } 'orphan_database_sidecar'
    Assert-Code { Get-SnapshotDatabaseFiles ([pscustomobject]@{Name='host-agent';Path=(Join-Path $fixtureRoot 'missing-host.db');Required=$true}) } 'required_database_missing'

    # Defect caught: a schema contract that omits required tables/columns can report a wrong-version DB as valid.
    $notificationSchema = Get-SnapshotDatabaseSchemaContract -Name 'notifications'
    Assert-True ($notificationSchema.Tables.notifications -contains 'payload') 'notification_payload_column_required'
    Assert-True ($notificationSchema.Tables.notification_events -contains 'sequence') 'notification_event_sequence_required'
    $helperSchema = Get-SnapshotDatabaseSchemaContract -Name 'helper-journal'
    Assert-True ($helperSchema.Tables.helper_claims -contains 'request_digest') 'helper_claim_digest_required'
    Assert-True ($helperSchema.Tables.helper_approvals -contains 'approval_id') 'helper_approval_id_required'
    $hostSchema = Get-SnapshotDatabaseSchemaContract -Name 'host-agent'
    Assert-True ($hostSchema.Tables.baseline_origins -contains 'started_at_utc' -and $hostSchema.Triggers -contains 'baseline_origin_on_sample') 'hostagent_v3_migration_schema_required'
    Assert-True ($hostSchema.MinimumVersion -eq 3 -and $notificationSchema.MinimumVersion -eq 1 -and $helperSchema.MinimumVersion -eq 1 -and $hostSchema.Indexes.Count -gt 0 -and $helperSchema.Triggers.Count -gt 0) 'schema_contract_carries_exact_versions_and_objects'

    # Defect caught: runtime pressure must warn at 768 MiB but defer hard abort until 512 MiB.
    $script:fixtureMemoryMiB = 768.0
    function Get-SnapshotResourceState { [pscustomobject]@{FreeMemoryMiB=$script:fixtureMemoryMiB;FreeDiskBytes=12GB} }
    $script:SnapshotLowMemoryWarning = $false
    [void](Assert-SnapshotRuntimeResources)
    Assert-True $script:SnapshotLowMemoryWarning 'runtime_memory_warning_at_768_mib'
    $script:fixtureMemoryMiB = 512.0
    Assert-Code { Assert-SnapshotRuntimeResources } 'hard_memory_floor_512_mib'
    $script:fixtureMemoryMiB = 900.0
    $validLink = Get-SnapshotRequestLinkage -RequestGuid '8f2a113e-bc4d-4be0-87a9-7284c9aa22dd' -IncidentId 'd7bc28d7b1dd474e8fd597d657c72783' -ServiceName '0HostAgentDevAcceptance_0123456789abcdef0123456789abcdef' -ServiceId 'devaccept-0123456789abcdef0123456789abcdef' -Operation 'StartService' -RequestDigest ('A' * 64)
    Assert-True ($validLink.Operation -eq 'StartService' -and $validLink.ServiceName.StartsWith('0HostAgentDevAcceptance_') -and $validLink.ServiceId -ceq 'devaccept-0123456789abcdef0123456789abcdef') 'request_linkage_accepts_only_owned_service_target'
    Assert-Code { Get-SnapshotRequestLinkage -RequestGuid '8f2a113e-bc4d-4be0-87a9-7284c9aa22dd' -IncidentId 'd7bc28d7b1dd474e8fd597d657c72783' -ServiceName '0HostAgentDevAcceptance_0123456789abcdef0123456789abcdef' -ServiceId 'devaccept-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa' -Operation 'StartService' -RequestDigest ('A' * 64) } 'request_service_id_invalid'
    Assert-Code { Get-SnapshotRequestLinkage -RequestGuid '8f2a113e-bc4d-4be0-87a9-7284c9aa22dd' } 'request_linkage_arguments_incomplete'
    $allDatabasePass = @(
        [pscustomobject]@{Name='host-agent';Status='PASS'},
        [pscustomobject]@{Name='notifications';Status='PASS'},
        [pscustomobject]@{Name='helper-journal';Status='PASS'}
    )
    $acceptedReceipt = Resolve-SnapshotIntegrityOutcome -Databases $allDatabasePass -LinkageRequested $true -JournalLinkage 'PASS'
    Assert-True ($acceptedReceipt.Outcome -ceq 'passed' -and $acceptedReceipt.FailureCode -eq $null) 'receipt_exact_pass_word_requires_all_db_and_linkage'
    $incompleteReceipt = Resolve-SnapshotIntegrityOutcome -Databases @($allDatabasePass[0],$allDatabasePass[1],[pscustomobject]@{Name='helper-journal';Status='NOTRUN'}) -LinkageRequested $false
    Assert-True ($incompleteReceipt.Outcome -eq 'integrity_incomplete') 'receipt_never_passes_lazy_notrun_database'

    # Defect caught: dropping or failing to copy SQLite sidecars can silently omit committed state.
    $sideSource = Join-Path $fixtureRoot 'sidecars'
    [void][IO.Directory]::CreateDirectory($sideSource)
    $sideMain = Join-Path $sideSource 'fixture.db'
    [IO.File]::WriteAllBytes($sideMain,[byte[]](1,2,3,4))
    [IO.File]::WriteAllBytes(($sideMain+'-wal'),[byte[]](5,6,7,8))
    $sideInventory = Get-SnapshotDatabaseFiles ([pscustomobject]@{Name='notifications';Path=$sideMain;Required=$true})
    $sideCopy = New-SnapshotDatabaseCopies -Inventory $sideInventory -DestinationRoot (Join-Path $fixtureRoot 'sidecar-copies') -AllDatabasesBytes $sideInventory.TotalBytes
    Assert-True ($sideCopy.CopyVerified -and $sideCopy.Files.Count -eq 2) 'main_and_wal_sidecar_copied'
    Assert-True ((Get-FileHash -LiteralPath ($sideMain+'-wal') -Algorithm SHA256).Hash -eq (Get-FileHash -LiteralPath (Join-Path $sideCopy.Root 'fixture.db-wal') -Algorithm SHA256).Hash) 'wal_copy_hash_matches'

    # Defect caught: opening source files with SQLite or skipping exact schema/linkage checks.
    $notificationDb = Join-Path $fixtureRoot 'notifications.db'
    $helperDb = Join-Path $fixtureRoot 'helper-journal.db'
    $request = '8f2a113e-bc4d-4be0-87a9-7284c9aa22dd'
    $incident = 'd7bc28d7b1dd474e8fd597d657c72783'
    $serviceName = '0HostAgentDevAcceptance_0123456789abcdef0123456789abcdef'
    $serviceId = 'devaccept-0123456789abcdef0123456789abcdef'
    $approvalId = '10b9cd2f-c210-4f5f-a133-26e8a018a7d0'
    $digest = 'A' * 64
    $createSqlite = @"
import json,sqlite3,sys
n,h,req,incident,service,serviceId,digest,approvalId=sys.argv[1:]
c=sqlite3.connect(n)
c.executescript('CREATE TABLE notification_schema(version INTEGER PRIMARY KEY CHECK(version=1)); INSERT INTO notification_schema VALUES(1); CREATE TABLE notifications(id TEXT PRIMARY KEY,payload TEXT NOT NULL,state INTEGER NOT NULL,code TEXT NOT NULL,receipt TEXT,attempts INTEGER NOT NULL,created INTEGER NOT NULL,next INTEGER NOT NULL); CREATE INDEX notification_due ON notifications(state,next); CREATE TABLE notification_events(sequence INTEGER PRIMARY KEY,id TEXT NOT NULL,state INTEGER NOT NULL,code TEXT NOT NULL,at INTEGER NOT NULL); CREATE TRIGGER notification_events_no_update BEFORE UPDATE ON notification_events BEGIN SELECT RAISE(ABORT,"immutable"); END; CREATE TRIGGER notification_events_no_delete BEFORE DELETE ON notification_events BEGIN SELECT RAISE(ABORT,"immutable"); END;')
c.commit();c.close()
c=sqlite3.connect(h)
c.executescript('CREATE TABLE helper_journal_schema(version INTEGER PRIMARY KEY CHECK(version=1)); INSERT INTO helper_journal_schema VALUES(1); CREATE TABLE helper_audit(sequence INTEGER PRIMARY KEY AUTOINCREMENT,audit_id TEXT UNIQUE NOT NULL,request_id TEXT NOT NULL,payload_json TEXT NOT NULL CHECK(json_valid(payload_json))); CREATE INDEX ix_helper_audit_request ON helper_audit(request_id,sequence); CREATE TABLE helper_approvals(approval_id TEXT PRIMARY KEY NOT NULL,request_id TEXT UNIQUE NOT NULL,payload_json TEXT NOT NULL CHECK(json_valid(payload_json))); CREATE TABLE helper_claims(request_id TEXT PRIMARY KEY NOT NULL,approval_id TEXT UNIQUE NOT NULL REFERENCES helper_approvals(approval_id),incident_id TEXT NOT NULL,operation TEXT NOT NULL,target_key TEXT NOT NULL,started_at_utc TEXT NOT NULL,request_digest TEXT NOT NULL); CREATE INDEX ix_helper_claims_target_time ON helper_claims(target_key,started_at_utc); CREATE INDEX ix_helper_claims_incident_operation ON helper_claims(incident_id,operation);')
for table in ('helper_audit','helper_approvals','helper_claims'):
 for action in ('UPDATE','DELETE'):
  c.execute("CREATE TRIGGER "+table+"_no_"+action+" BEFORE "+action+" ON "+table+" BEGIN SELECT RAISE(ABORT,'immutable'); END")
approval={'IncidentId':incident,'Operation':0,'RequestDigest':digest,'Target':{'ServiceName':service}}
c.execute('INSERT INTO helper_approvals VALUES(?,?,?)',(approvalId,req,json.dumps(approval)))
c.execute('INSERT INTO helper_claims VALUES(?,?,?,?,?,?,?)',(req,approvalId,incident,'StartService','service:'+serviceId,'2026-10-27T10:00:00Z',digest))
for event,code in ((2,'approved'),(0,'proposed'),(3,'started'),(4,'succeeded')):
 payload={'Event':event,'IncidentId':incident,'Operation':0,'RequestDigest':digest,'Code':code}
 if event==2: payload['ApprovalId']=approvalId
 else: payload['ParametersJson']=json.dumps({'serviceId':serviceId})
 c.execute('INSERT INTO helper_audit(audit_id,request_id,payload_json) VALUES(?,?,?)',(str(event),req,json.dumps(payload)))
c.commit();c.close()
"@
    [void](Invoke-FixturePython -Code $createSqlite -Arguments @($notificationDb,$helperDb,$request,$incident,$serviceName,$serviceId,$digest,$approvalId))
    $notificationHashBefore = (Get-FileHash -LiteralPath $notificationDb -Algorithm SHA256).Hash
    $helperHashBefore = (Get-FileHash -LiteralPath $helperDb -Algorithm SHA256).Hash
    $notificationInventory = Get-SnapshotDatabaseFiles ([pscustomobject]@{Name='notifications';Path=$notificationDb;Required=$true})
    $helperInventory = Get-SnapshotDatabaseFiles ([pscustomobject]@{Name='helper-journal';Path=$helperDb;Required=$false})
    $allFixtureBytes = $notificationInventory.TotalBytes + $helperInventory.TotalBytes
    $notificationCopy = New-SnapshotDatabaseCopies -Inventory $notificationInventory -DestinationRoot (Join-Path $fixtureRoot 'sqlite-copies') -AllDatabasesBytes $allFixtureBytes
    $helperCopy = New-SnapshotDatabaseCopies -Inventory $helperInventory -DestinationRoot (Join-Path $fixtureRoot 'sqlite-copies') -AllDatabasesBytes $allFixtureBytes
    $originalSchemaContract = (Get-Command Get-SnapshotDatabaseSchemaContract).ScriptBlock
    $script:actualGetSnapshotDatabaseSchemaContract = $originalSchemaContract
    try {
        Set-Item Function:\Get-SnapshotDatabaseSchemaContract -Value {
            param([Parameter(Mandatory)][ValidateSet('host-agent','notifications','helper-journal')][string]$Name)
            $contract = & $script:actualGetSnapshotDatabaseSchemaContract -Name $Name
            if ($Name -eq 'notifications') { $contract.Tables['notifications'] = @($contract.Tables['notifications']) + 'codex_required_test_column' }
            $contract
        }
        $mismatchProbe = @(Invoke-SnapshotSqliteProbe -Copies @($notificationCopy))
        Assert-True (-not $mismatchProbe[0].schemaValid -and $mismatchProbe[0].failureCode -eq 'schema_column_set_mismatch') 'python_probe_rejects_column_required_by_passed_contract'
    } finally { Set-Item Function:\Get-SnapshotDatabaseSchemaContract -Value $originalSchemaContract }
    $link = [pscustomobject]@{RequestGuid=$request;IncidentId=$incident;ServiceName=$serviceName;ServiceId=$serviceId;Operation='StartService';RequestDigest=$digest}
    $probed = @(Invoke-SnapshotSqliteProbe -Copies @($notificationCopy,$helperCopy) -Linkage $link)
    $notificationResult = @($probed | Where-Object name -CEQ 'notifications')[0]
    $helperResult = @($probed | Where-Object name -CEQ 'helper-journal')[0]
    Assert-True ($notificationResult.quickCheck -eq 'ok' -and $notificationResult.schemaValid) 'readonly_notification_quickcheck_and_exact_schema'
    Assert-True ($helperResult.quickCheck -eq 'ok' -and $helperResult.schemaValid -and $helperResult.journalLinkage -eq 'PASS') "readonly_helper_quickcheck_and_request_linkage:$($helperResult.failureCode):$($helperResult.quickCheck):$($helperResult.schemaValid):$($helperResult.journalLinkage)"
    Assert-True ($helperResult.requestGuidCounts.approval -eq 1 -and $helperResult.requestGuidCounts.claim -eq 1 -and $helperResult.requestGuidCounts.proposed -eq 1 -and $helperResult.requestGuidCounts.started -eq 1 -and $helperResult.requestGuidCounts.ended -eq 1 -and $helperResult.requestGuidCounts.succeeded -eq 1) 'linked_journal_returns_counts_only'
    Assert-True ((Get-FileHash -LiteralPath $notificationDb -Algorithm SHA256).Hash -eq $notificationHashBefore -and (Get-FileHash -LiteralPath $helperDb -Algorithm SHA256).Hash -eq $helperHashBefore) 'source_database_hashes_unchanged'
    $helperJson = ConvertTo-Json -InputObject $helperResult -Compress -Depth 8
    Assert-True ($helperJson -notmatch [regex]::Escape($request) -and $helperJson -notmatch [regex]::Escape($incident) -and $helperJson -notmatch [regex]::Escape($serviceName) -and $helperJson -notmatch [regex]::Escape($serviceId) -and $helperJson -notmatch [regex]::Escape($digest)) 'journal_output_contains_counts_not_raw_identifiers'
    $wrongLink = $link.PSObject.Copy()
    $wrongLink.RequestDigest = 'B' * 64
    $negativeProbe = @(Invoke-SnapshotSqliteProbe -Copies @($notificationCopy,$helperCopy) -Linkage $wrongLink)
    $negativeHelper = @($negativeProbe | Where-Object name -CEQ 'helper-journal')[0]
    Assert-True ($negativeHelper.journalLinkage -eq 'FAIL' -and $negativeHelper.requestGuidCounts.approval -eq 0) 'mismatched_digest_cannot_pass_linkage'

    # Defect caught: a failed helper start must not skip the monitor restart in finally restoration.
    $script:restoreStates = @{HostAgent='Stopped';'HostAgent.Helper'='Stopped'}
    $script:restoreAttempts = [Collections.Generic.List[string]]::new()
    $restoreAppRoot = Join-Path $fixtureRoot 'restore-installed'
    [void][IO.Directory]::CreateDirectory((Join-Path $restoreAppRoot 'Service'))
    [void][IO.Directory]::CreateDirectory((Join-Path $restoreAppRoot 'Helper'))
    $monitorExe = Join-Path $restoreAppRoot 'Service/HostAgent.Service.exe'
    $monitorDll = Join-Path $restoreAppRoot 'Service/HostAgent.Service.dll'
    $helperExe = Join-Path $restoreAppRoot 'Helper/HostAgent.Helper.exe'
    $helperDll = Join-Path $restoreAppRoot 'Helper/HostAgent.Helper.dll'
    [IO.File]::WriteAllText($monitorExe,'fixture monitor executable')
    [IO.File]::WriteAllText($monitorDll,'fixture monitor library')
    [IO.File]::WriteAllText($helperExe,'fixture helper executable')
    [IO.File]::WriteAllText($helperDll,'fixture helper library')
    $script:identityFixtureMap = @{
        'C:\Program Files\HostAgent\Service\HostAgent.Service.exe'=$monitorExe
        'C:\Program Files\HostAgent\Service\HostAgent.Service.dll'=$monitorDll
        'C:\Program Files\HostAgent\Helper\HostAgent.Helper.exe'=$helperExe
        'C:\Program Files\HostAgent\Helper\HostAgent.Helper.dll'=$helperDll
    }
    function Get-Item {
        param([string]$LiteralPath,[switch]$Force,[string]$ErrorAction)
        if($script:identityFixtureMap.ContainsKey($LiteralPath)){return Microsoft.PowerShell.Management\Get-Item -LiteralPath $script:identityFixtureMap[$LiteralPath] -Force -ErrorAction Stop}
        Microsoft.PowerShell.Management\Get-Item @PSBoundParameters
    }
    function Get-FileHash {
        param([string]$LiteralPath,[string]$Algorithm,[string]$ErrorAction)
        if($script:identityFixtureMap.ContainsKey($LiteralPath)){$LiteralPath=$script:identityFixtureMap[$LiteralPath]}
        Microsoft.PowerShell.Utility\Get-FileHash -LiteralPath $LiteralPath -Algorithm $Algorithm -ErrorAction Stop
    }
    function Get-CimInstance {
        param([string]$ClassName,[string]$Filter)
        if($ClassName -eq 'Win32_Service'){
            @(
                [pscustomobject]@{Name='HostAgent';StartMode='Auto';StartName='NT SERVICE\HostAgent';PathName='"C:\Program Files\HostAgent\Service\HostAgent.Service.exe"';State=$script:restoreStates.HostAgent},
                [pscustomobject]@{Name='HostAgent.Helper';StartMode='Auto';StartName='LocalSystem';PathName='"C:\Program Files\HostAgent\Helper\HostAgent.Helper.exe"';State=$script:restoreStates['HostAgent.Helper']}
            )
        }
    }
    function Get-SnapshotServiceState([string]$Name) {
        [pscustomobject]@{Name=$Name;State=$script:restoreStates[$Name]}
    }
    function Start-Service {
        param([string]$Name)
        $script:restoreAttempts.Add('start:'+ $Name)
        if($Name -eq 'HostAgent.Helper'){$script:restoreStates[$Name]='Start Pending';return}
        $script:restoreStates[$Name]='Running'
    }
    function Wait-SnapshotServiceState {
        param([string]$Name,[string]$State,[int]$TimeoutSeconds,[switch]$ForRecovery,$Manifest,[object[]]$Services)
        if($Name -eq 'HostAgent.Helper' -and $State -in @('Running','Stopped')){throw 'service_state_timeout'}
        if($script:restoreStates[$Name] -ne $State){throw 'synthetic_state_timeout'}
        [pscustomobject]@{Name=$Name;State=$State}
    }
    $servicesToRestore = @(
        [pscustomobject]@{Name='HostAgent';StartName='NT SERVICE\HostAgent';PathName='"C:\Program Files\HostAgent\Service\HostAgent.Service.exe"';Executable='C:\Program Files\HostAgent\Service\HostAgent.Service.exe';Library='C:\Program Files\HostAgent\Service\HostAgent.Service.dll';OriginallyRunning=$true;StopRequested=$true;InitialState='Running';ExeRelative='Service/HostAgent.Service.exe';DllRelative='Service/HostAgent.Service.dll'},
        [pscustomobject]@{Name='HostAgent.Helper';StartName='LocalSystem';PathName='"C:\Program Files\HostAgent\Helper\HostAgent.Helper.exe"';Executable='C:\Program Files\HostAgent\Helper\HostAgent.Helper.exe';Library='C:\Program Files\HostAgent\Helper\HostAgent.Helper.dll';OriginallyRunning=$true;StopRequested=$true;InitialState='Running';ExeRelative='Helper/HostAgent.Helper.exe';DllRelative='Helper/HostAgent.Helper.dll'}
    )
    $restoreManifest = [pscustomobject]@{Files=@(
        [pscustomobject]@{Path='Service/HostAgent.Service.exe';Length=(Get-Item $monitorExe).Length;Sha256=(Get-FileHash $monitorExe -Algorithm SHA256).Hash},
        [pscustomobject]@{Path='Service/HostAgent.Service.dll';Length=(Get-Item $monitorDll).Length;Sha256=(Get-FileHash $monitorDll -Algorithm SHA256).Hash},
        [pscustomobject]@{Path='Helper/HostAgent.Helper.exe';Length=(Get-Item $helperExe).Length;Sha256=(Get-FileHash $helperExe -Algorithm SHA256).Hash},
        [pscustomobject]@{Path='Helper/HostAgent.Helper.dll';Length=(Get-Item $helperDll).Length;Sha256=(Get-FileHash $helperDll -Algorithm SHA256).Hash}
    )}
    $restoreFailureMessage=$null
    try { Restore-SnapshotServices -Services $servicesToRestore -Manifest $restoreManifest; throw 'expected_service_restore_failure_not_observed' }
    catch { if($_.Exception.Message -notlike 'service_restore_failed*'){throw};$restoreFailureMessage=$_.Exception.Message }
    $helperStartIndex = $script:restoreAttempts.IndexOf('start:HostAgent.Helper')
    $monitorStartIndex = $script:restoreAttempts.IndexOf('start:HostAgent')
    Assert-True ($helperStartIndex -ge 0 -and $monitorStartIndex -gt $helperStartIndex -and $script:restoreStates.HostAgent -eq 'Running') 'restore_attempts_both_helper_then_monitor_even_when_helper_pending'
    Assert-True ($restoreFailureMessage -match 'HostAgent.Helper:service_state_timeout' -and $restoreFailureMessage -match 'HostAgent:final_state_not_restored|final_identity:service_identity_guard_failed') 'restore_failure_aggregates_pending_and_final_state_errors'
    Assert-True ($servicesToRestore[0].InitialState -eq 'Running' -and $servicesToRestore[1].InitialState -eq 'Running') 'recovery_keeps_initial_states_immutable'

    # Defect caught: a pending service wait must use a bounded timeout while validating identity during recovery polls.
    $script:restoreStates['HostAgent.Helper']='Start Pending'
    $script:SnapshotServiceStartTimeoutSeconds=0.02
    $waitWatch=[Diagnostics.Stopwatch]::StartNew()
    try { & $script:actualWaitSnapshotServiceState -Name 'HostAgent.Helper' -State 'Running' -TimeoutSeconds 0.02 -ForRecovery -Manifest $restoreManifest -Services $servicesToRestore; throw 'expected_pending_service_timeout_not_observed' }
    catch { if($_.Exception.Message -ne 'service_state_timeout'){throw} }
    Assert-True ($waitWatch.Elapsed.TotalSeconds -lt 3) 'pending_service_wait_is_bounded'
    $script:restoreStates['HostAgent.Helper']='Stop Pending'
    $stopWaitWatch=[Diagnostics.Stopwatch]::StartNew()
    try { & $script:actualWaitSnapshotServiceState -Name 'HostAgent.Helper' -State 'Stopped' -TimeoutSeconds 0.02 -ForRecovery -Manifest $restoreManifest -Services $servicesToRestore; throw 'expected_stop_pending_timeout_not_observed' }
    catch { if($_.Exception.Message -ne 'service_state_timeout'){throw} }
    Assert-True ($stopWaitWatch.Elapsed.TotalSeconds -lt 3) 'stop_pending_service_wait_is_bounded'
    $script:SnapshotServiceStartTimeoutSeconds=45

    # Defect caught: a Stop Pending helper must not block the independent monitor restoration attempt.
    $script:restoreStates['HostAgent.Helper']='Stop Pending';$script:restoreStates.HostAgent='Stopped';$script:restoreAttempts.Clear()
    try { Restore-SnapshotServices -Services $servicesToRestore -Manifest $restoreManifest; throw 'expected_stop_pending_restore_failure_not_observed' }
    catch { if($_.Exception.Message -notlike 'service_restore_failed*'){throw} }
    Assert-True ($script:restoreAttempts -notcontains 'start:HostAgent.Helper' -and $script:restoreAttempts -contains 'start:HostAgent' -and $script:restoreStates.HostAgent -eq 'Running') 'stop_pending_helper_does_not_block_monitor_restore'

    # Defect caught: already-running services still require exact installed EXE/DLL identity verification.
    $script:restoreStates['HostAgent.Helper']='Running';$script:restoreStates.HostAgent='Running';$script:restoreAttempts.Clear()
    [IO.File]::WriteAllText($helperDll,'changed fixture helper library')
    try { Restore-SnapshotServices -Services $servicesToRestore -Manifest $restoreManifest; throw 'expected_running_binary_identity_failure_not_observed' }
    catch { if($_.Exception.Message -notlike 'service_restore_failed*'){throw} }
    Assert-True ($script:restoreAttempts.Count -eq 0 -and $servicesToRestore[0].InitialState -eq 'Running' -and $servicesToRestore[1].InitialState -eq 'Running') 'running_identity_mismatch_fails_without_mutating_initial_state'

    # Defect caught: the real default-plan flow rejects valid VM metadata due to a malformed identity guard expression.
    $script:restoreStates = @{HostAgent='Running';'HostAgent.Helper'='Running'}
    $planHostDb = Join-Path $fixtureRoot 'plan-host-agent.db'
    $planNotificationsDb = Join-Path $fixtureRoot 'plan-notifications.db'
    [IO.File]::WriteAllBytes($planHostDb,[byte[]](1,2,3))
    [IO.File]::WriteAllBytes($planNotificationsDb,[byte[]](4,5,6))
    function Get-CimInstance {
        param([string]$ClassName,[string]$Filter)
        switch($ClassName){
            'Win32_ComputerSystem' { [pscustomobject]@{Model='VirtualBox'} }
            'Win32_ComputerSystemProduct' { [pscustomobject]@{UUID='46543236-8C5E-49DA-953C-DB17F9B1E402'} }
            'Win32_Service' { @() }
        }
    }
    function Get-Service { param([string]$Name) [pscustomobject]@{Name=$Name;Status='Running'} }
    function Get-SnapshotSharedManifest { param($Root,$ManifestHash,$Commit,$Version) [pscustomobject]@{Version=$Version;SourceCommit=$Commit;Files=@()} }
    function Assert-SnapshotInstalledIdentity {
        param($Manifest,[object[]]$Services,[switch]$CaptureInitialState)
        foreach($service in $Services){$service.InitialState='Running';$service.OriginallyRunning=$true;$script:restoreStates[$service.Name]='Running'}
    }
    function Get-SnapshotModeState {
        param($Root,$ManifestHash,$Commit,$Version)
        [pscustomobject]@{Mode='Confirmation';ReadOnly=$false;Pushover=$false;Heartbeat=$false;NetworkProbes=$false;HelperReadOnly=$false;HelperShadow=$false;ServiceConfigurationSha256='a';HelperConfigurationSha256='b'}
    }
    function Get-SnapshotDatabaseDefinitions {
        @([pscustomobject]@{Name='host-agent';Path=$planHostDb;Service='HostAgent';Required=$true},
          [pscustomobject]@{Name='notifications';Path=$planNotificationsDb;Service='HostAgent';Required=$true},
          [pscustomobject]@{Name='helper-journal';Path=(Join-Path $fixtureRoot 'missing-helper-journal.db');Service='HostAgent.Helper';Required=$false})
    }
    $wholePlan = Invoke-InstalledDatabaseSnapshot -DoExecute:$false -Linkage $null
    Assert-True ($wholePlan.Outcome -eq 'plan_ready' -and $wholePlan.ResultSchemaVersion -eq 1 -and $wholePlan.Mode -eq 'Confirmation' -and $wholePlan.ModePreserved -eq $true) "whole_default_plan_entrypoint_accepts_mocked_approved_vm:$($wholePlan.Outcome):$($wholePlan.FailureCode):$($wholePlan.ResultSchemaVersion):$($wholePlan.ModePreserved)"
    Assert-True ($wholePlan.Databases.Count -eq 3 -and @($wholePlan.Databases|Where-Object Name -eq 'helper-journal'|Where-Object {$_.Status -eq 'NOTRUN' -and $_.FailureCode -eq 'lazy_database_absent'}).Count -eq 1) 'whole_plan_keeps_lazy_helper_notrun'
    Assert-True (-not $wholePlan.ConfigurationPerformed -and -not $wholePlan.DatabaseSourceOpenedBySqlite) 'whole_default_plan_has_no_configuration_or_sqlite_source_access'

    "PASS installed_database_snapshot_contract_checks=$script:assertionCount"
} finally {
    if (Test-Path -LiteralPath $fixtureRoot) {
        Remove-Item -LiteralPath $fixtureRoot -Recurse -Force
    }
}

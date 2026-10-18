$ErrorActionPreference='Stop'
$repo=Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$source=Get-Content (Join-Path $repo 'tests\HostAgent.PipeProbe\IsolatedRuntimeProbe.cs') -Raw
if($source -notmatch 'RecoveryProofIsComplete\s*\('){throw 'collector_recovery_evidence_predicate_missing'}
if($source -notmatch 'RecoveryProofIsComplete\(faultJob!\.InjectedFailures,\s*faultJob\.Recoveries,\s*views,\s*samples\.Length\)'){
    throw 'collector_probe_not_using_recovery_evidence_predicate'
}
if($source -notmatch 'persistedSamples\s*>=\s*recoveries'){
    throw 'collector_gate_does_not_require_persisted_recovery_samples'
}
[pscustomobject]@{suite='complete-collector-evidence';passed=1;failed=0;liveCollectorOperations=0}|ConvertTo-Json -Compress

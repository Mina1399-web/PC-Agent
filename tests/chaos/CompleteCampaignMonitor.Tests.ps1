$ErrorActionPreference='Stop'
Add-Type -Path (Join-Path $PSScriptRoot '..\..\scripts\chaos\CompleteCampaignMonitor.cs')
foreach($case in @(@(512,'hard_free_memory_floor_512_mib'),@(511.9,'hard_free_memory_floor_512_mib'),@(767.9,'soft_free_memory_floor_768_mib'),@(768,$null),@([double]::NaN,'hard_free_memory_floor_512_mib'),@([double]::PositiveInfinity,'hard_free_memory_floor_512_mib'))){
 if([CompleteCampaignMonitor]::Evaluate($case[0]) -cne $case[1]){throw ('continuous_monitor_boundary_'+$case[0])}
}
'continuous_monitor_boundaries_passed'
$monitor=[CompleteCampaignMonitor]::new()
try {
 $monitor.Observe(511.9);$first=$monitor.FailureCode;$monitor.Observe(4096)
 if(-not $first -or $monitor.FailureCode -cne $first){throw 'transient_resource_breach_not_latched'}
}finally{$monitor.Dispose()}
'continuous_monitor_sticky_abort_passed'

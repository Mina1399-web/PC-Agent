param([switch]$WrongRuntimeHash)
$ErrorActionPreference='Stop'
Import-Module Microsoft.PowerShell.Utility
# Simulate only the absent build-SDK filesystem boundary. All VM, candidate,
# runtime, protected-service and resource checks execute against real state.
function Get-FileHash {
 param([string]$LiteralPath,[string]$Algorithm='SHA256')
 if($LiteralPath -like 'C:\Users\AgentPCdevtest\AppData\Local\HostAgent-dev-sdk\*'){
  throw [IO.FileNotFoundException]::new('simulated_build_sdk_unavailable')
 }
 if($WrongRuntimeHash -and $LiteralPath -ceq 'C:\Program Files\dotnet\dotnet.exe'){
  return [pscustomobject]@{Hash=('0'*64)}
 }
 Microsoft.PowerShell.Utility\Get-FileHash -LiteralPath $LiteralPath -Algorithm $Algorithm -ErrorAction Stop
}
& (Join-Path $PSScriptRoot '..\..\scripts\chaos\run-complete-dev-resilience.ps1')

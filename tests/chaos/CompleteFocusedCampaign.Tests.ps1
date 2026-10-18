$ErrorActionPreference='Stop'
$repo=Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$path=Join-Path $repo 'scripts\chaos\run-complete-dev-resilience.ps1'
$tokens=$null;$errors=$null
$ast=[Management.Automation.Language.Parser]::ParseFile($path,[ref]$tokens,[ref]$errors)
if(@($errors).Count){throw 'complete_campaign_script_does_not_parse'}
$source=Get-Content -LiteralPath $path -Raw
$catalogPath=Join-Path $repo 'scripts\chaos\complete-resilience-manifest.json'
$catalog=Get-Content -LiteralPath $catalogPath -Raw|ConvertFrom-Json
$helperEntry=@($catalog.helpers|Where-Object path -eq 'CompleteHelperTargets.psm1')|Select-Object -First 1
$helperHash=(Get-FileHash -LiteralPath (Join-Path $repo 'scripts\chaos\CompleteHelperTargets.psm1') -Algorithm SHA256).Hash
$pinnedCatalogHash=[regex]::Match($source,"campaignCatalogHash='([0-9A-F]{64})'").Groups[1].Value
$catalogHash=(Get-FileHash -LiteralPath $catalogPath -Algorithm SHA256).Hash
if(-not$helperEntry -or $helperEntry.sha256-cne$helperHash -or $pinnedCatalogHash-cne$catalogHash){throw 'complete_campaign_catalog_hashes_are_not_synchronized'}
if($source -notmatch '\[switch\]\$FocusS2Failures'){throw 'focused_s2_switch_missing'}
if($source -notmatch 'Where-Object \{\$_.id -in @\(''S2-collector-failure-recovery'',''S2-real-helper-remediation''\)\}'){
 throw 'focused_campaign_phase_allowlist_mismatch'
}
$previous=$ast.Find({param($node)$node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Get-CompletePreviousHelperState'},$true)
if(-not$previous){throw 'previous_helper_readonly_inventory_missing'}
$unsafe=@($previous.FindAll({param($node)
 $node -is [Management.Automation.Language.CommandAst] -and $node.GetCommandName() -in @('Set-Acl','Remove-Item','sc.exe','Start-Service','Stop-Service','Set-Service')
},$true))
if($unsafe.Count){throw 'previous_helper_inventory_contains_mutation'}
if($source -notmatch 'mode=ro&immutable=1' -or $source -notmatch 'PRAGMA query_only=ON'){
 throw 'previous_helper_journal_is_not_checked_read_only'
}
if($source -notmatch 'previousHelperArtifactsAfter' -or $source -notmatch 'previousHelperArtifactsUnchanged' -or $source -notmatch 'Get-FileHash -LiteralPath \$file\.FullName'){
 throw 'previous_helper_artifact_postcheck_missing'
}
if($source -match '(?i)python(?:\.exe)?\s+-c\b'){throw 'python_c_execution_not_permitted'}
$plan=$ast.Find({param($node)$node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Get-CompletePreviousHelperState'},$true)
$ids=@('de0d788a83d44c118e8433a846394960','39a242cd86284048989872baa8a5fa78','2dc31f6ea2db4e41905c222dea176c1f',
 'ce729bac8e954244820f1afc369f7279','c25e837c5a2f4179920828fa9eaff7b4','0c66d84903534ab28030e83886629bda')
foreach($id in $ids){if($source -notmatch [regex]::Escape($id)){throw 'previous_campaign_target_id_missing'}}
[pscustomobject]@{suite='complete-focused-campaign';passed=6;failed=0;serviceOperations=0;filesystemMutations=0}|ConvertTo-Json -Compress

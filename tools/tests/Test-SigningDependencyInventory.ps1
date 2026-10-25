param([string]$GeneratorPath = (Join-Path $PSScriptRoot '../New-SigningDependencyInventory.ps1'))
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('HostAgent-signing-inventory-test-' + [guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $testRoot
$testResults = [Collections.Generic.List[object]]::new()
$externalOutputs = [Collections.Generic.List[string]]::new()

function Assert-Test([bool]$Condition, [string]$Name) {
    if (-not $Condition) { throw ('assertion_failed:' + $Name) }
}
function Write-TestFile([string]$Path, [string]$Content) {
    $null = New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($Path)) -Force
    [IO.File]::WriteAllText($Path, $Content, [Text.UTF8Encoding]::new($false))
}
function New-TestSource([string]$Name, [switch]$MissingProductLicense, [switch]$TraversalKey) {
    $root = Join-Path $testRoot $Name
    $cache = Join-Path $root 'cache'
    $productLicense = if ($MissingProductLicense) { '' } else { '<license type="expression">MIT</license>' }
    Write-TestFile (Join-Path $cache 'example.product/1.0.0/example.product.nuspec') ("<package><metadata><id>Example.Product</id><version>1.0.0</version>$productLicense<repository type=`"git`" url=`"https://github.com/example/product`" commit=`"1111111111111111111111111111111111111111`" /></metadata></package>")
    Write-TestFile (Join-Path $cache 'example.tests/2.0.0/example.tests.nuspec') '<package><metadata><id>Example.Tests</id><version>2.0.0</version><licenseUrl>https://example.org/test-license</licenseUrl></metadata></package>'
    Write-TestFile (Join-Path $cache 'microsoft.netcore.app.runtime.win-x64/10.0.12/microsoft.netcore.app.runtime.win-x64.nuspec') '<package><metadata><id>Microsoft.NETCore.App.Runtime.win-x64</id><version>10.0.12</version><license type="expression">MIT</license></metadata></package>'
    Write-TestFile (Join-Path $cache 'example.product/1.0.0/init.ps1') 'throw "package_code_must_not_execute"'
    foreach ($kind in @('src', 'tests')) {
        $project = if ($kind -eq 'src') { 'Product' } else { 'Product.Tests' }
        $dir = Join-Path $root "$kind/$project"
        Write-TestFile (Join-Path $dir ($project + '.csproj')) '<Project Sdk="Microsoft.NET.Sdk" />'
        $libraries = [ordered]@{ 'Example.Product/1.0.0' = @{ type = 'package' } }
        if ($kind -eq 'tests') { $libraries['Example.Tests/2.0.0'] = @{ type = 'package' } }
        if ($TraversalKey) { $libraries['../private/1.0.0'] = @{ type = 'package' } }
        $assets = @{ version = 3; libraries = $libraries; packageFolders = @{ ($cache + [IO.Path]::DirectorySeparatorChar) = @{} } }
        Write-TestFile (Join-Path $dir 'obj/project.assets.json') ($assets | ConvertTo-Json -Depth 8)
    }
    $payload = Join-Path $root 'payload'
    $deps = @{ libraries = @{ 'Example.Product/1.0.0' = @{ type = 'package' }; 'runtimepack.Microsoft.NETCore.App.Runtime.win-x64/10.0.12' = @{ type = 'runtimepack' } } }
    Write-TestFile (Join-Path $payload 'Service/HostAgent.Service.deps.json') ($deps | ConvertTo-Json -Depth 8)
    return [pscustomobject]@{ Source = $root; Package = $payload; Output = (Join-Path $root 'inventory.json'); Cache = $cache }
}
function Assert-Rejected([scriptblock]$Action, [string]$Expected, [string]$Name) {
    $caught = $null
    try { $null = & $Action } catch { $caught = $_.Exception.Message }
    Assert-Test ($caught -ceq $Expected) ($Name + ': expected ' + $Expected + ', observed ' + $caught)
}
function Invoke-TestCase([string]$Name, [scriptblock]$Action) {
    try {
        $null = & $Action
        $testResults.Add([pscustomobject]@{ name = $Name; status = 'PASS'; failure = $null })
        Write-Host ('PASS ' + $Name)
    } catch {
        $testResults.Add([pscustomobject]@{ name = $Name; status = 'FAIL'; failure = $_.Exception.Message })
        Write-Host ('FAIL ' + $Name + ': ' + $_.Exception.Message)
    }
}

try {
    Invoke-TestCase 'valid_product_test_and_published_metadata_without_private_paths' {
    # A generator that merges product/test scope incorrectly or leaks local restore paths fails here.
    $valid = New-TestSource 'valid'
    try {
        $result = & $GeneratorPath -SourcePath $valid.Source -SourceCommit ('a' * 40) -PackagePath $valid.Package -OutputPath $valid.Output
    } catch { throw ('assertion_failed:valid_inventory_generation:' + $_.Exception.Message) }
    Assert-Test (Test-Path -LiteralPath $valid.Output -PathType Leaf) 'valid_output_exists'
    $raw = Get-Content -LiteralPath $valid.Output -Raw
    $inventory = $raw | ConvertFrom-Json
    Assert-Test ($inventory.sourceCommit -ceq ('a' * 40)) 'source_commit'
    Assert-Test ($inventory.metadataOnly -eq $true) 'metadata_only'
    Assert-Test ($inventory.projectCount -eq 2 -and $inventory.packageCount -eq 2) 'project_and_package_counts'
    $product = @($inventory.packages | Where-Object id -CEQ 'Example.Product')[0]
    $test = @($inventory.packages | Where-Object id -CEQ 'Example.Tests')[0]
    Assert-Test ($product.scope -ceq 'product' -and $product.declaredLicense -ceq 'MIT') 'product_scope_license'
    Assert-Test (@($product.projects).Count -eq 2 -and @($product.projects) -contains 'src/Product/Product.csproj' -and @($product.projects) -contains 'tests/Product.Tests/Product.Tests.csproj') 'relative_project_scope'
    Assert-Test ($test.scope -ceq 'test-only' -and $test.licenseEvidence -ceq 'legacy_url_only') 'test_only_legacy_license'
    Assert-Test ($product.nuspecSha256 -ceq (Get-FileHash -LiteralPath (Join-Path $valid.Cache 'example.product/1.0.0/example.product.nuspec')).Hash) 'nuspec_hash'
    Assert-Test (@($inventory.publishedDependencies).Count -eq 2) 'published_dependencies'
    Assert-Test (@($inventory.runtimePacks).Count -eq 1 -and $inventory.runtimePacks[0].version -ceq '10.0.12' -and $inventory.runtimePacks[0].id -ceq 'Microsoft.NETCore.App.Runtime.win-x64' -and $inventory.runtimePacks[0].declaredLicense -ceq 'MIT') 'runtime_pack_metadata'
    Assert-Test ($raw -notmatch '(?i)[a-z]:\\|\\\\|/Users/|/home/' -and (-not $env:USERNAME -or -not $raw.Contains($env:USERNAME)) -and -not $raw.Contains($valid.Source)) 'no_private_absolute_paths'
    Assert-Test ($result.status -ceq 'written' -and $result.sha256 -ceq (Get-FileHash -LiteralPath $valid.Output).Hash) 'result_digest'
    }

    Invoke-TestCase 'missing_product_license_rejected_without_partial_output' {
    # Shipping a product package without current license metadata must fail before creating an inventory.
    $missing = New-TestSource 'missing-license' -MissingProductLicense
    Assert-Rejected { & $GeneratorPath -SourcePath $missing.Source -SourceCommit ('a' * 40) -OutputPath $missing.Output } 'signing_inventory_product_license_missing' 'missing_product_license'
    Assert-Test (-not (Test-Path -LiteralPath $missing.Output)) 'no_partial_missing_license_output'
    }

    Invoke-TestCase 'traversal_key_rejected_without_partial_output' {
    # Traversal in an untrusted assets key must not choose a nuspec outside its package folder.
    $traversal = New-TestSource 'traversal' -TraversalKey
    Assert-Rejected { & $GeneratorPath -SourcePath $traversal.Source -SourceCommit ('a' * 40) -OutputPath $traversal.Output } 'signing_inventory_package_key_invalid' 'traversal_key'
    Assert-Test (-not (Test-Path -LiteralPath $traversal.Output)) 'no_partial_traversal_output'
    }

    Invoke-TestCase 'existing_evidence_preserved' {
    # Existing reviewed evidence must never be overwritten.
    $existing = New-TestSource 'existing'
    Write-TestFile $existing.Output 'existing-reviewed-evidence'
    Assert-Rejected { & $GeneratorPath -SourcePath $existing.Source -SourceCommit ('a' * 40) -OutputPath $existing.Output } 'signing_inventory_output_exists' 'existing_output'
    Assert-Test ((Get-Content -LiteralPath $existing.Output -Raw) -ceq 'existing-reviewed-evidence') 'existing_output_preserved'
    }

    Invoke-TestCase 'private_repository_metadata_rejected' {
    # Package repository metadata cannot smuggle a private local path into public evidence.
    $private = New-TestSource 'private-repository'
    Write-TestFile (Join-Path $private.Cache 'example.product/1.0.0/example.product.nuspec') '<package><metadata><id>Example.Product</id><version>1.0.0</version><license type="expression">MIT</license><repository type="git" url="file:///C:/private" /></metadata></package>'
    Assert-Rejected { & $GeneratorPath -SourcePath $private.Source -SourceCommit ('a' * 40) -OutputPath $private.Output } 'signing_inventory_repository_invalid' 'private_repository'
    }

    Invoke-TestCase 'unmapped_published_dependency_rejected' {
    # Published dependencies without a matching product restore entry must not be silently accepted.
    $unmapped = New-TestSource 'unmapped'
    Write-TestFile (Join-Path $unmapped.Package 'Service/HostAgent.Service.deps.json') '{"libraries":{"Unmapped.Package/9.0.0":{"type":"package"}}}'
    Assert-Rejected { & $GeneratorPath -SourcePath $unmapped.Source -SourceCommit ('a' * 40) -PackagePath $unmapped.Package -OutputPath $unmapped.Output } 'signing_inventory_published_package_unmapped' 'unmapped_published_package'
    }

    Invoke-TestCase 'default_output_outside_source' {
        # An implicit source-tree output could accidentally publish local metadata or overwrite evidence.
        $default = New-TestSource 'default-output'
        $result = & $GeneratorPath -SourcePath $default.Source -SourceCommit ('a' * 40)
        Assert-Test ($result.outputFileName -match '^HostAgent-signing-dependencies-[a-f0-9]{32}\.json$') 'default_file_name'
        $path = Join-Path ([IO.Path]::GetTempPath()) $result.outputFileName
        $externalOutputs.Add($path)
        Assert-Test ((Test-Path -LiteralPath $path -PathType Leaf) -and -not $path.StartsWith($default.Source, [StringComparison]::OrdinalIgnoreCase)) 'default_outside_source'
    }

    Invoke-TestCase 'external_xml_entity_rejected' {
        # Resolving nuspec external entities would read arbitrary local files during public evidence generation.
        $entity = New-TestSource 'external-entity'
        $secret = Join-Path $entity.Source 'private-license.txt'
        Write-TestFile $secret 'MIT'
        $uri = ([uri]$secret).AbsoluteUri
        Write-TestFile (Join-Path $entity.Cache 'example.product/1.0.0/example.product.nuspec') ('<!DOCTYPE package [<!ENTITY local SYSTEM "' + $uri + '">]><package><metadata><id>Example.Product</id><version>1.0.0</version><license type="expression">&local;</license></metadata></package>')
        Assert-Rejected { & $GeneratorPath -SourcePath $entity.Source -SourceCommit ('a' * 40) -OutputPath $entity.Output } 'signing_inventory_nuspec_invalid' 'external_entity'
    }

    $failed = @($testResults | Where-Object status -CEQ 'FAIL')
    [pscustomobject]@{ status = $(if ($failed.Count) { 'FAIL' } else { 'PASS' }); tests = $testResults.Count; failed = $failed.Count; scope = 'synthetic_metadata_only_no_package_code_execution' } | ConvertTo-Json -Compress
    if ($failed.Count) { throw 'signing_inventory_regression_failed' }
} finally {
    foreach ($path in $externalOutputs) {
        if ([IO.Path]::GetFileName($path) -match '^HostAgent-signing-dependencies-[a-f0-9]{32}\.json$' -and [IO.Path]::GetDirectoryName($path).TrimEnd('\', '/') -ceq ([IO.Path]::GetTempPath()).TrimEnd('\', '/')) {
            Remove-Item -LiteralPath $path -Force
        } else { throw 'test_external_cleanup_scope_mismatch' }
    }
    $resolvedRoot = [IO.Path]::GetFullPath($testRoot)
    $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    if ($resolvedRoot.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase) -and [IO.Path]::GetFileName($resolvedRoot).StartsWith('HostAgent-signing-inventory-test-', [StringComparison]::Ordinal)) {
        Remove-Item -LiteralPath $resolvedRoot -Recurse -Force
    } else { throw 'test_cleanup_scope_mismatch' }
}

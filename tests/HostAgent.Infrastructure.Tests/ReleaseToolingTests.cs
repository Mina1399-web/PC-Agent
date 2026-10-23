using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Xunit;
using Xunit.Abstractions;
namespace HostAgent.Infrastructure.Tests;
[Collection("ReleaseTooling")]
public sealed partial class ReleaseToolingTests(ITestOutputHelper output)
{
    [Fact]
    public async Task Full_desktop_manifest_is_validated_without_executing_manifest_code()
    {
        using var fixture=new PackageFixture(1200);
        var result=await Run("Test-Release.ps1",fixture.Path,"-DevVm");
        Assert.True(result.Exit==0,result.Error);
        using var json=JsonDocument.Parse(result.Out);
        Assert.Equal(1209,json.RootElement.GetProperty("files").GetInt32());
    }
    [Theory]
    [InlineData("expression")]
    [InlineData("duplicate_root")]
    [InlineData("duplicate_path")]
    [InlineData("excess_count")]
    [InlineData("excess_bytes")]
    [InlineData("extra_block")]
    public async Task Manifest_reader_rejects_expressions_duplicates_and_resource_excess(string mutation)
    {
        using var fixture=new PackageFixture();
        var manifest=System.IO.Path.Combine(fixture.Path,"manifest.psd1");
        var marker=System.IO.Path.Combine(fixture.Path,"must-not-exist.txt").Replace("'","''");
        var text=File.ReadAllText(manifest);
        var entry="@{Path='extra.txt';Sha256='"+new string('A',64)+"';Length=1}";
        text=mutation switch
        {
            "expression"=>text.Replace("Format=1;","Format=$([IO.File]::WriteAllText('"+marker+"','executed');1);"),
            "duplicate_root"=>text.Replace("Format=1;","Format=1;Format=1;"),
            "duplicate_path"=>text.Replace("Files=@(","Files=@("+entry+","+entry+","),
            "excess_count"=>text.Replace("Files=@(","Files=@("+string.Join(",",Enumerable.Repeat(entry,10001))+","),
            "excess_bytes"=>new string(' ',4*1024*1024+1),
            "extra_block"=>"dynamicparam { throw 'must_not_execute' } end { "+text+" }",
            _=>throw new InvalidOperationException()
        };
        File.WriteAllText(manifest,text,new UTF8Encoding(false));
        var result=await Run("Test-Release.ps1",fixture.Path,"-DevVm");
        Assert.NotEqual(0,result.Exit);
        Assert.Contains(mutation.StartsWith("excess",StringComparison.Ordinal)?"release_manifest_limit":"release_manifest_invalid",result.Error);
        Assert.False(File.Exists(marker));
    }
    [Fact]
    public async Task Desktop_release_cannot_pass_integrity_gate_with_missing_desktop_payload()
    {
        using var fixture=new PackageFixture();
        var manifest=System.IO.Path.Combine(fixture.Path,"manifest.psd1");
        var text=File.ReadAllText(manifest).Replace("Version='1.0.0'","Version='1.1.0'");
        text=System.Text.RegularExpressions.Regex.Replace(text,",@\\{Path='Desktop/HostAgent.Desktop.exe';[^}]+\\}","");
        File.WriteAllText(manifest,text);File.Delete(System.IO.Path.Combine(fixture.Path,"Desktop","HostAgent.Desktop.exe"));
        var result=await Run("Test-Release.ps1",fixture.Path,"-DevVm");
        Assert.NotEqual(0,result.Exit);Assert.Contains("release_missing_component",result.Error);
    }
    // Defect: a hosted review builder must not allow the DEV/production machine to masquerade as its build host.
    [Fact]
    public async Task Hosted_review_builder_refuses_an_unapproved_environment_without_writing_output()
    {
        using var fixture=new PackageFixture();
        var root=System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory,"../../../../../"));
        var script=System.IO.Path.Combine(root,"tools","Build-Review.ps1");
        Assert.True(File.Exists(script),"The separately guarded hosted review builder is missing.");
        var output=System.IO.Path.Combine(fixture.Path,"review-output");
        var info=new ProcessStartInfo("pwsh.exe"){UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true};
        foreach(var arg in new[]{"-NoProfile","-NonInteractive","-File",script,"-PackagePath",output})info.ArgumentList.Add(arg);
        info.Environment["GITHUB_ACTIONS"]="false";
        using var process=Process.Start(info)!;
        var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
        Assert.NotEqual(0,process.ExitCode);
        Assert.Contains("review_hosted_runner_required",await stderr);
        Assert.Empty(await stdout);
        Assert.False(Directory.Exists(output));
    }
    // Defect: first-party binaries identify different products, preventing a single enforced signing policy.
    [Fact]
    public void First_party_binaries_share_the_product_name_and_version()
    {
        var core = FileVersionInfo.GetVersionInfo(typeof(HostAgent.Core.Contracts.ITelemetryCollector<>).Assembly.Location);
        var infrastructure = FileVersionInfo.GetVersionInfo(typeof(HostAgent.Infrastructure.Collectors.WindowsNetworkCollector).Assembly.Location);
        Assert.Equal("Host Agent V1", core.ProductName);
        Assert.Equal("Host Agent V1", infrastructure.ProductName);
        Assert.Equal(core.ProductVersion, infrastructure.ProductVersion);
        Assert.Equal("1.0.0",core.ProductVersion);
    }
    // Defect: the owned managed MSI action has no enforced product/version metadata.
    [Fact]
    public async Task Managed_installer_action_identifies_this_product_and_exact_release_version()
    {
        using var fixture=new PackageFixture();
        var root=System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory,"../../../../../../"));
        var build=await Run("Build-Installer.ps1",fixture.Path,"-WixPath",System.IO.Path.Combine(root,"HostAgent-DEV-tools","wix","wix.exe"),"-DevVm","-Version","1.0.3");
        Assert.True(build.Exit==0,build.Error+build.Out);
        using var json=JsonDocument.Parse(build.Out);
        var version=FileVersionInfo.GetVersionInfo(System.IO.Path.Combine(json.RootElement.GetProperty("intermediate").GetString()!,"Preflight.dll"));
        Assert.Equal("Host Agent V1",version.ProductName);
        Assert.Equal("1.0.3",version.ProductVersion);
        Assert.Equal("1.0.3.0",version.FileVersion);
    }
    [Fact]
    public async Task Release_builder_refuses_unsigned_production_and_overwriting_existing_output()
    {
        using var fixture=new PackageFixture();
        var production=await Run("Build-Release.ps1",fixture.Path,"-WixPath","not-used");
        Assert.NotEqual(0,production.Exit);Assert.Contains("publisher_signature_required",production.Error);
        var existing=await Run("Build-Release.ps1",fixture.Path,"-WixPath","not-used","-DevVm");
        Assert.NotEqual(0,existing.Exit);Assert.Contains("release_output_exists",existing.Error);
    }
    // Defect: distribution has no inspectable MSI with pinned identities, service stop/remove and SYSTEM-only bootstrap.
    [Fact]
    public async Task Built_msi_has_only_product_services_and_transactional_installation_actions()
    {
        using var fixture=new PackageFixture();
        var root=System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory,"../../../../../../"));
        var wix=System.IO.Path.Combine(root,"HostAgent-DEV-tools","wix","wix.exe");
        var build=await Run("Build-Installer.ps1",fixture.Path,"-WixPath",wix,"-DevVm");
        Assert.True(build.Exit==0,build.Error+build.Out);Assert.True(File.Exists(System.IO.Path.Combine(fixture.Path,"HostAgent.msi")));
        var inspect=await Run("Test-Installer.ps1",fixture.Path,"-DevVm");
        Assert.True(inspect.Exit==0,inspect.Error+inspect.Out);using var json=JsonDocument.Parse(inspect.Out);
        Assert.Equal("NT SERVICE\\HostAgent",json.RootElement.GetProperty("monitorAccount").GetString());
        Assert.Equal("LocalSystem",json.RootElement.GetProperty("helperAccount").GetString());
        Assert.Equal(2,json.RootElement.GetProperty("serviceCount").GetInt32());
        Assert.True(json.RootElement.GetProperty("bootstrapIsSystemDeferred").GetBoolean());
        Assert.True(json.RootElement.GetProperty("precopyValidationIsSystemDeferred").GetBoolean());
        Assert.True(json.RootElement.GetProperty("uninstallPreservesData").GetBoolean());
        Assert.Equal(2,json.RootElement.GetProperty("recoveryServiceCount").GetInt32());
        Assert.Equal(9,json.RootElement.GetProperty("fileCount").GetInt32());
        var installedPaths=json.RootElement.GetProperty("installedPaths").EnumerateArray().Select(x=>x.GetString()).ToArray();
        Assert.Contains("LICENSE",installedPaths);
        Assert.Contains("Desktop/HostAgent.Desktop.exe",installedPaths);
        Assert.Equal(1,json.RootElement.GetProperty("desktopShortcutCount").GetInt32());
        Assert.Contains("docs/operations/privacy.md",installedPaths);
        Assert.Contains("docs/legal/dotnet-runtime-10.0.12-LICENSE.txt",installedPaths);
        Assert.Contains("docs/legal/dotnet-runtime-10.0.12-THIRD-PARTY-NOTICES.txt",installedPaths);
        Assert.True(json.RootElement.GetProperty("existingServicesGuarded").GetBoolean());
        Assert.True(json.RootElement.GetProperty("devVmGuard").GetBoolean());
        Assert.True(json.RootElement.GetProperty("devVmIdentityMatches").GetBoolean(),
            "The actual MSI condition engine must accept the verified VM's Windows ComputerName casing.");
    }
    // Defect: the validator reports valid=true after the MSI's service-collision guard is removed.
    [Fact]
    public async Task Installer_validator_refuses_an_actual_msi_without_the_existing_service_guard()
    {
        using var fixture=new PackageFixture();
        await BuildInstaller(fixture);
        await MutateInstaller(fixture,"existing_guard");
        var inspect=await Run("Test-Installer.ps1",fixture.Path);
        Assert.NotEqual(0,inspect.Exit);
        Assert.Contains("release_installer_existing_services_guard",inspect.Error);
        Assert.Empty(inspect.Out);
    }
    // Defect: DEV validation reports valid=true when its guard is absent or rejects the actual VM.
    [Theory]
    [InlineData("dev_guard","release_installer_dev_vm_guard")]
    [InlineData("dev_identity","release_installer_dev_vm_identity")]
    public async Task Dev_installer_validator_enforces_the_actual_msi_identity_condition(string mutation,string expectedError)
    {
        using var fixture=new PackageFixture();
        await BuildInstaller(fixture);
        await MutateInstaller(fixture,mutation);
        var inspect=await Run("Test-Installer.ps1",fixture.Path,"-DevVm");
        Assert.NotEqual(0,inspect.Exit);
        Assert.Contains(expectedError,inspect.Error);
        Assert.Empty(inspect.Out);
    }
    // Production structure validation must not acquire a DEV-machine restriction.
    [Fact]
    public async Task Non_dev_installer_validation_does_not_require_a_dev_identity_guard()
    {
        using var fixture=new PackageFixture();
        await BuildInstaller(fixture);
        await MutateInstaller(fixture,"dev_guard");
        var inspect=await Run("Test-Installer.ps1",fixture.Path);
        Assert.True(inspect.Exit==0,inspect.Error+inspect.Out);
        using var json=JsonDocument.Parse(inspect.Out);
        Assert.True(json.RootElement.GetProperty("valid").GetBoolean());
        Assert.True(json.RootElement.GetProperty("existingServicesGuarded").GetBoolean());
        Assert.False(json.RootElement.GetProperty("devVmGuard").GetBoolean());
        Assert.False(json.RootElement.GetProperty("devVmIdentityMatches").GetBoolean());
    }
    // Defect: removing the installed product before InstallInitialize leaves it outside upgrade rollback.
    [Fact]
    public async Task Built_msi_removes_the_previous_product_inside_the_upgrade_transaction_before_new_components()
    {
        using var fixture=new PackageFixture();
        await BuildInstaller(fixture);
        using var sequence=await ReadInstallerSequence(fixture);
        var initialize=sequence.RootElement.GetProperty("InstallInitialize").GetInt32();
        var remove=sequence.RootElement.GetProperty("RemoveExistingProducts").GetInt32();
        var components=sequence.RootElement.GetProperty("ProcessComponents").GetInt32();
        var files=sequence.RootElement.GetProperty("InstallFiles").GetInt32();
        var finalize=sequence.RootElement.GetProperty("InstallFinalize").GetInt32();
        output.WriteLine($"Actual MSI sequence: InstallInitialize={initialize}; RemoveExistingProducts={remove}; ProcessComponents={components}; InstallFiles={files}; InstallFinalize={finalize}.");
        Assert.True(initialize<remove,$"InstallInitialize={initialize} must precede RemoveExistingProducts={remove}.");
        Assert.True(remove<components,$"RemoveExistingProducts={remove} must precede ProcessComponents={components}.");
        Assert.True(remove<files,$"RemoveExistingProducts={remove} must precede InstallFiles={files}.");
        Assert.True(remove<finalize,$"RemoveExistingProducts={remove} must precede InstallFinalize={finalize}.");
    }
    // Defect: the validator accepts missing, late or nontransactional removal of the previous product.
    [Theory]
    [InlineData("missing_remove")]
    [InlineData("remove_before_initialize")]
    [InlineData("remove_after_components")]
    [InlineData("remove_after_finalize")]
    public async Task Installer_validator_refuses_unsafe_upgrade_transaction_order(string mutation)
    {
        using var fixture=new PackageFixture();
        await BuildInstaller(fixture);
        await MutateInstaller(fixture,mutation);
        var inspect=await Run("Test-Installer.ps1",fixture.Path,"-DevVm");
        Assert.NotEqual(0,inspect.Exit);
        Assert.Contains("release_installer_upgrade_transaction",inspect.Error);
        Assert.Empty(inspect.Out);
    }
    // Defect: Sequence=0 disables InstallInitialize but otherwise satisfies the relative-order checks.
    [Fact]
    public async Task Installer_validator_refuses_a_disabled_install_initialize_action()
    {
        using var fixture=new PackageFixture();
        await BuildInstaller(fixture);
        await MutateInstaller(fixture,"initialize_disabled");
        var inspect=await Run("Test-Installer.ps1",fixture.Path,"-DevVm");
        Assert.NotEqual(0,inspect.Exit);
        Assert.Contains("release_installer_upgrade_transaction",inspect.Error);
        Assert.Empty(inspect.Out);
    }
    // Defect: verified release package cannot be validated without silently disabling the production signature gate.
    [Fact]
    public async Task Dev_package_integrity_passes_and_production_unsigned_package_is_refused()
    {
        using var fixture=new PackageFixture();
        var dev=await Run("Test-Release.ps1",fixture.Path,"-DevVm");
        Assert.Equal(0,dev.Exit);using var result=JsonDocument.Parse(dev.Out);
        Assert.True(result.RootElement.GetProperty("valid").GetBoolean());Assert.True(result.RootElement.GetProperty("devOnly").GetBoolean());
        var production=await Run("Test-Release.ps1",fixture.Path);
        Assert.NotEqual(0,production.Exit);Assert.Contains("publisher_signature_required",production.Error);
    }
    [Theory]
    [InlineData("tamper")]
    [InlineData("extra")]
    [InlineData("escape")]
    public async Task Tampered_extra_and_escaping_artifacts_are_refused(string change)
    {
        using var fixture=new PackageFixture();
        if(change=="tamper")File.AppendAllText(System.IO.Path.Combine(fixture.Path,"Service","HostAgent.Service.exe"),"tampered");
        if(change=="extra")File.WriteAllText(System.IO.Path.Combine(fixture.Path,"unexpected.dll"),"untrusted");
        if(change=="escape")File.WriteAllText(System.IO.Path.Combine(fixture.Path,"manifest.psd1"),"@{Format=1;Version='1.0.0';SourceCommit='2932cb44749283af54cc2f2f52472405eec29c6d';Files=@(@{Path='../escape';Sha256='"+new string('A',64)+"';Length=1})}");
        var result=await Run("Test-Release.ps1",fixture.Path,"-DevVm");
        Assert.NotEqual(0,result.Exit);Assert.Contains("release_",result.Error);
    }
    // Defect: the installed virtual account cannot read counters after a clean install,
    // and a failed install has no rollback for the required narrowly scoped membership.
    [Fact]
    public async Task Built_msi_provisions_counter_read_inside_rollback_guarded_transaction()
    {
        using var fixture=new PackageFixture();
        await BuildInstaller(fixture);
        using var sequence=await ReadInstallerSequence(fixture);
        Assert.True(sequence.RootElement.TryGetProperty("GrantCounterRead",out var grant),
            "MSI has no transactional counter-read grant action.");
        Assert.True(sequence.RootElement.GetProperty("InstallServices").GetInt32()<grant.GetInt32());
        Assert.True(grant.GetInt32()<sequence.RootElement.GetProperty("StartServices").GetInt32());
        Assert.True(sequence.RootElement.GetProperty("RollbackCounterReadGrant").GetInt32()<grant.GetInt32());
        Assert.True(sequence.RootElement.GetProperty("StopServices").GetInt32()<sequence.RootElement.GetProperty("ReleaseCounterRead").GetInt32());
        Assert.True(sequence.RootElement.GetProperty("ReleaseCounterRead").GetInt32()<sequence.RootElement.GetProperty("DeleteServices").GetInt32());
        var inspect=await Run("Test-Installer.ps1",fixture.Path,"-DevVm");
        Assert.Equal(0,inspect.Exit);
        using var result=JsonDocument.Parse(inspect.Out);
        Assert.True(result.RootElement.GetProperty("counterReadTransactionGuarded").GetBoolean());
    }
    // Defect: the validator accepts a package that can grant counters without SYSTEM/rollback,
    // or removes installer-owned membership during the old-product removal of an upgrade.
    [Theory]
    [InlineData("counter_grant_type")]
    [InlineData("counter_missing_rollback")]
    [InlineData("counter_release_upgrade")]
    [InlineData("counter_rollback_disabled")]
    public async Task Installer_validator_refuses_unsafe_counter_permission_actions(string mutation)
    {
        using var fixture=new PackageFixture(); await BuildInstaller(fixture);
        await MutateInstaller(fixture,mutation);
        var inspect=await Run("Test-Installer.ps1",fixture.Path,"-DevVm");
        Assert.NotEqual(0,inspect.Exit); Assert.Contains("release_installer_counter_read",inspect.Error);
        Assert.Empty(inspect.Out);
    }
    // Defect: installer performs changes during preflight, selects a privileged monitor, or hides missing elevation.
    [Fact]
    public async Task Installation_plan_is_read_only_and_defaults_to_shadow_and_virtual_service_account()
    {
        using var fixture=new PackageFixture();
        var plan=await Run("Install-HostAgent.ps1",fixture.Path,"-DevVm","-PlanOnly");
        Assert.Equal(0,plan.Exit);using var json=JsonDocument.Parse(plan.Out);
        Assert.Equal("Shadow",json.RootElement.GetProperty("mode").GetString());
        Assert.True(json.RootElement.GetProperty("readOnly").GetBoolean());
        Assert.Equal("NT SERVICE\\HostAgent",json.RootElement.GetProperty("monitorAccount").GetString());
        Assert.Equal("LocalSystem",json.RootElement.GetProperty("helperAccount").GetString());
        Assert.False(json.RootElement.GetProperty("changesPerformed").GetBoolean());
        var denied=await Run("Install-HostAgent.ps1",fixture.Path,"-DevVm");
        Assert.NotEqual(0,denied.Exit);Assert.Contains("administrator_required",denied.Error);
    }
    internal static async Task<(int Exit,string Out,string Error)> Run(string script,string package,params string[] args)
    {
        var root=System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory,"../../../../../"));
        var shell="pwsh.exe";
        var info=new ProcessStartInfo(shell){UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true};
        foreach(var arg in new[]{"-NoProfile","-NonInteractive","-File",System.IO.Path.Combine(root,"tools",script),"-PackagePath",package}.Concat(args))info.ArgumentList.Add(arg);
        using var process=Process.Start(info)!;var output=process.StandardOutput.ReadToEndAsync();var errors=process.StandardError.ReadToEndAsync();
        try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(90)); }
        catch (TimeoutException) { process.Kill(entireProcessTree:true);await process.WaitForExitAsync();throw; }
        return(process.ExitCode,await output,await errors);
    }
    private static async Task BuildInstaller(PackageFixture fixture)
    {
        var root=System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory,"../../../../../../"));
        var build=await Run("Build-Installer.ps1",fixture.Path,"-WixPath",System.IO.Path.Combine(root,"HostAgent-DEV-tools","wix","wix.exe"),"-DevVm");
        Assert.True(build.Exit==0,build.Error+build.Out);
    }
    private static async Task MutateInstaller(PackageFixture fixture,string mutation)
    {
        // Edit only the newly built disposable MSI database; never open an install session.
        var script=System.IO.Path.Combine(fixture.Path,"mutate-msi.ps1");
        File.WriteAllText(script,"""
            param([Parameter(Mandatory)][string]$PackagePath,[Parameter(Mandatory)][string]$Mutation)
            $ErrorActionPreference='Stop'
            $installer=New-Object -ComObject WindowsInstaller.Installer
            $db=$installer.OpenDatabase((Join-Path $PackagePath 'HostAgent.msi'),1)
            if($Mutation -like 'counter_*') {
                $sql=switch($Mutation) {
                    'counter_grant_type' {'UPDATE `CustomAction` SET `Type`=1025 WHERE `Action`=''GrantCounterRead'''}
                    'counter_missing_rollback' {'DELETE FROM `InstallExecuteSequence` WHERE `Action`=''RollbackCounterReadGrant'''}
                    'counter_release_upgrade' {'UPDATE `InstallExecuteSequence` SET `Condition`=''REMOVE~="ALL"'' WHERE `Action`=''ReleaseCounterRead'''}
                    'counter_rollback_disabled' {'DELETE FROM `LaunchCondition` WHERE `Condition`=''NOT DISABLEROLLBACK AND NOT RollbackDisabled'''}
                }
                $verify=$db.OpenView('SELECT `Action` FROM `CustomAction` WHERE `Action`=''GrantCounterRead''');$null=$verify.Execute()
                try {if($null -eq $verify.Fetch()){throw 'fixture_counter_action_missing'}} finally {$null=$verify.Close()}
                $write=$db.OpenView($sql);try {$null=$write.Execute()}finally{$null=$write.Close()}
                $null=$db.Commit();return
            }
            if ($Mutation -in @('missing_remove','remove_before_initialize','remove_after_components','remove_after_finalize','initialize_disabled')) {
                $sequence=@{}
                $view=$db.OpenView('SELECT `Action`,`Sequence` FROM `InstallExecuteSequence`')
                $null=$view.Execute()
                try { while ($null -ne ($row=$view.Fetch())) { $sequence[$row.StringData(1)]=[int]$row.IntegerData(2) } }
                finally { $null=$view.Close() }
                if (-not $sequence.ContainsKey('RemoveExistingProducts')) { throw 'fixture_upgrade_action_missing' }
                $sql=if ($Mutation -eq 'missing_remove') { 'DELETE FROM `InstallExecuteSequence` WHERE `Action`=''RemoveExistingProducts''' }
                elseif ($Mutation -eq 'initialize_disabled') { 'UPDATE `InstallExecuteSequence` SET `Sequence`=0 WHERE `Action`=''InstallInitialize''' }
                else {
                    $position=switch ($Mutation) {
                        'remove_before_initialize' { $sequence['InstallInitialize']-1 }
                        'remove_after_components' { $sequence['ProcessComponents']+1 }
                        'remove_after_finalize' { $sequence['InstallFinalize']+1 }
                    }
                    'UPDATE `InstallExecuteSequence` SET `Sequence`='+$position+' WHERE `Action`=''RemoveExistingProducts'''
                }
                $write=$db.OpenView($sql)
                try { $null=$write.Execute() } finally { $null=$write.Close() }
                $null=$db.Commit()
                return
            }
            $view=$db.OpenView('SELECT `Condition` FROM `LaunchCondition`')
            $null=$view.Execute()
            $condition=$null
            try {
                while ($null -ne ($row=$view.Fetch())) {
                    $value=$row.StringData(1)
                    if (($Mutation -eq 'existing_guard' -and $value -eq 'Installed OR WIX_UPGRADE_DETECTED OR (NOT EXISTING_MONITOR AND NOT EXISTING_HELPER)') -or
                        ($Mutation -in @('dev_guard','dev_identity') -and $value -like '*DEV_MACHINE_GUID*')) {
                        if ($null -ne $condition) { throw 'fixture_ambiguous_guard' }
                        $condition=$value
                    }
                }
            } finally { $null=$view.Close() }
            if ($null -eq $condition) { throw 'fixture_guard_missing' }
            $escaped=$condition.Replace("'","''")
            $sql='DELETE FROM `LaunchCondition` WHERE `Condition`='''+$escaped+''''
            $write=$db.OpenView($sql)
            try { $null=$write.Execute() } finally { $null=$write.Close() }
            if ($Mutation -eq 'dev_identity') {
                $write=$db.OpenView('INSERT INTO `LaunchCondition` (`Condition`,`Description`) VALUES (''ComputerName~="different-host" AND DEV_MACHINE_GUID="00000000-0000-0000-0000-000000000000"'',''Disposable identity mutation'')')
                try { $null=$write.Execute() } finally { $null=$write.Close() }
            }
            $null=$db.Commit()
            """,new UTF8Encoding(false));
        var result=await Run(script,fixture.Path,"-Mutation",mutation);
        Assert.True(result.Exit==0,result.Error+result.Out);
    }
    private static async Task<JsonDocument> ReadInstallerSequence(PackageFixture fixture)
    {
        var script=System.IO.Path.Combine(fixture.Path,"read-msi-sequence.ps1");
        File.WriteAllText(script,"""
            param([Parameter(Mandatory)][string]$PackagePath)
            $ErrorActionPreference='Stop'
            $installer=New-Object -ComObject WindowsInstaller.Installer
            $db=$installer.OpenDatabase((Join-Path $PackagePath 'HostAgent.msi'),0)
            $view=$db.OpenView('SELECT `Action`,`Sequence` FROM `InstallExecuteSequence`')
            $null=$view.Execute()
            $sequence=@{}
            try { while ($null -ne ($row=$view.Fetch())) { $sequence[$row.StringData(1)]=[int]$row.IntegerData(2) } }
            finally { $null=$view.Close() }
            $sequence | ConvertTo-Json -Compress
            """,new UTF8Encoding(false));
        var result=await Run(script,fixture.Path);
        Assert.True(result.Exit==0,result.Error+result.Out);
        return JsonDocument.Parse(result.Out);
    }
    internal sealed class PackageFixture:IDisposable
    {
        public string Path{get;}=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"release-"+Guid.NewGuid().ToString("N"));
        public PackageFixture(int additionalFiles=0)
        {
            Directory.CreateDirectory(Path);var entries=new List<string>();
            for(var i=0;i<additionalFiles;i++)
            {
                var relative="extra-"+i+".txt";var bytes=new byte[]{42};
                File.WriteAllBytes(System.IO.Path.Combine(Path,relative),bytes);
                entries.Add("@{Path='"+relative+"';Sha256='"+Convert.ToHexString(SHA256.HashData(bytes))+"';Length=1}");
            }
            foreach(var project in new[]{"Service","Helper","Cli","Desktop"})
            {
                var relative=project+"/HostAgent."+project+".exe";var path=System.IO.Path.Combine(Path,relative);
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
                var bytes=Encoding.UTF8.GetBytes("synthetic offline package fixture");File.WriteAllBytes(path,bytes);
                entries.Add("@{Path='"+relative+"';Sha256='"+Convert.ToHexString(SHA256.HashData(bytes))+"';Length="+bytes.Length+"}");
            }
            Directory.CreateDirectory(System.IO.Path.Combine(Path,"Cli","cs"));
            var satellite=Encoding.UTF8.GetBytes("synthetic satellite");File.WriteAllBytes(System.IO.Path.Combine(Path,"Cli","cs","Synthetic.resources.dll"),satellite);
            entries.Add("@{Path='Cli/cs/Synthetic.resources.dll';Sha256='"+Convert.ToHexString(SHA256.HashData(satellite))+"';Length="+satellite.Length+"}");
            var source=System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory,"../../../../../"));
            foreach(var relative in new[]{"LICENSE","docs/operations/privacy.md","docs/legal/dotnet-runtime-10.0.12-LICENSE.txt","docs/legal/dotnet-runtime-10.0.12-THIRD-PARTY-NOTICES.txt"})
            {
                var bytes=File.ReadAllBytes(System.IO.Path.Combine(source,relative));
                var path=System.IO.Path.Combine(Path,relative);Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);File.WriteAllBytes(path,bytes);
                entries.Add("@{Path='"+relative+"';Sha256='"+Convert.ToHexString(SHA256.HashData(bytes))+"';Length="+bytes.Length+"}");
            }
            File.WriteAllText(System.IO.Path.Combine(Path,"manifest.psd1"),"@{Format=1;Version='1.0.0';SourceCommit='2932cb44749283af54cc2f2f52472405eec29c6d';Files=@("+
                string.Join(",",entries)+")}",new UTF8Encoding(false));
        }
        public void Dispose()=>Directory.Delete(Path,true);
    }
}

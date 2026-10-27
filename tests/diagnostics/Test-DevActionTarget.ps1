param()

$ErrorActionPreference = 'Stop'
$source = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\scripts\diagnostics\DevActionTarget.cs'))
$compiler = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler -PathType Leaf)) { throw 'framework_compiler_missing' }

$localAppData = [Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)
if ([string]::IsNullOrWhiteSpace($localAppData) -or -not [IO.Path]::IsPathRooted($localAppData)) { throw 'account_local_temp_unavailable' }
$testRoot = Join-Path $localAppData ('HostAgent-DevActionTarget-Test-' + [Guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($testRoot)
$resolved = [IO.Path]::GetFullPath((Resolve-Path -LiteralPath $testRoot).Path).TrimEnd('\', '/')
$expectedParent = [IO.Path]::GetFullPath($localAppData).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
if (-not $resolved.StartsWith($expectedParent, [StringComparison]::OrdinalIgnoreCase) -or
    [IO.Path]::GetFileName($resolved) -notmatch '^HostAgent-DevActionTarget-Test-[0-9a-f]{32}$') {
    throw 'fixture_temp_boundary_rejected'
}

try {
    $app = Join-Path $testRoot 'HostAgent.DevActionTarget.exe'
    $scopeTests = Join-Path $testRoot 'ScopeTests.cs'
    $scopeTestsExe = Join-Path $testRoot 'ScopeTests.exe'
    $aclFixture = Join-Path $testRoot 'AclFixture'
    $markerData = Join-Path (Join-Path $testRoot 'HostAgent-DEV-Acceptance-0123456789abcdef0123456789abcdef') 'data'
    [void][IO.Directory]::CreateDirectory($aclFixture)
    [void][IO.Directory]::CreateDirectory($markerData)
    $scopeSource = @'
using System;
using System.IO;
using System.Reflection;
using System.Security.AccessControl;
using System.Security.Principal;

internal static class ScopeTests
{
    private static void Assert(bool condition, string code)
    {
        if (!condition) throw new InvalidOperationException(code);
    }

    private static bool Reject(string runId, string basePath)
    {
        try { DevActionTargetScope.Resolve(runId, basePath); return false; }
        catch (ArgumentException) { return true; }
    }

    private static Type FindType(Assembly assembly, string name)
    {
        Type type = assembly.GetType(name, true);
        Assert(type != null, "reflection_type_" + name);
        return type;
    }

    private static object CreatePaths(Type scopeType, string runId, string basePath)
    {
        return scopeType.GetMethod("Resolve", BindingFlags.Public | BindingFlags.Static)
            .Invoke(null, new object[] { runId, basePath });
    }

    private static void SetAcl(string path, SecurityIdentifier currentSid, SecurityIdentifier serviceSid, FileSystemRights serviceRights)
    {
        DirectorySecurity acl = new DirectorySecurity();
        acl.SetAccessRuleProtection(true, false);
        acl.SetOwner(currentSid);
        acl.AddAccessRule(new FileSystemAccessRule(currentSid, FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        acl.AddAccessRule(new FileSystemAccessRule(serviceSid, serviceRights,
            InheritanceFlags.None, PropagationFlags.None, AccessControlType.Allow));
        Directory.SetAccessControl(path, acl);
    }

    private static void TestServiceAcl(Assembly assembly, string fixturePath)
    {
        Type type = FindType(assembly, "DevActionTargetEnvironment");
        MethodInfo method = type.GetMethod("ValidateServiceReadOnlyAcl", BindingFlags.NonPublic | BindingFlags.Static);
        Assert(method != null, "acl_validator_exists");
        SecurityIdentifier currentSid = WindowsIdentity.GetCurrent().User;
        SecurityIdentifier serviceSid = new SecurityIdentifier("S-1-5-80-1111111111-2222222222-3333333333-4444444444-5555555555");
        string path = Path.Combine(fixturePath, "read-only-dir");
        Directory.CreateDirectory(path);

        SetAcl(path, currentSid, serviceSid, FileSystemRights.ReadAndExecute);
        object accepted = method.Invoke(null, new object[] { path, serviceSid, false });
        Assert(accepted == null, "read_execute_acl_accepted");

        SetAcl(path, currentSid, serviceSid, FileSystemRights.ReadAndExecute | FileSystemRights.WriteData);
        object writeDenied = method.Invoke(null, new object[] { path, serviceSid, false });
        Assert((string)writeDenied == "app_acl_service_write_allowed", "write_acl_rejected");

        SetAcl(path, currentSid, serviceSid, FileSystemRights.ReadAndExecute | FileSystemRights.Delete);
        object deleteDenied = method.Invoke(null, new object[] { path, serviceSid, false });
        Assert((string)deleteDenied == "app_acl_service_write_allowed", "delete_acl_rejected");
        Console.WriteLine("PASS service_acl_behavior");
    }

    private static void TestRepeatedLifecycleWrites(Type scopeType, string fixturePath)
    {
        string id = "0123456789abcdef0123456789abcdef";
        object paths = CreatePaths(scopeType, id, fixturePath);
        Type serviceType = FindType(scopeType.Assembly, "DevActionTargetService");
        ConstructorInfo constructor = serviceType.GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic,
            null, new Type[] { paths.GetType() }, null);
        MethodInfo write = serviceType.GetMethod("WriteMarker", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert(constructor != null && write != null, "lifecycle_writer_exists");
        object firstLifecycle = constructor.Invoke(new object[] { paths });
        object secondLifecycle = constructor.Invoke(new object[] { paths });
        write.Invoke(firstLifecycle, new object[] { "started", null });
        write.Invoke(firstLifecycle, new object[] { "stopped", null });
        write.Invoke(secondLifecycle, new object[] { "started", null });
        write.Invoke(secondLifecycle, new object[] { "stopped", null });
        string data = Path.Combine(fixturePath, "HostAgent-DEV-Acceptance-" + id, "data");
        string[] markers = Directory.GetFiles(data, "*.json", SearchOption.TopDirectoryOnly);
        Assert(markers.Length == 4, "two_lifecycle_marker_sets_preserved");
        Console.WriteLine("PASS repeated_lifecycle_writes");
    }

    private static int Main(string[] args)
    {
        Assert(args.Length == 3, "fixture_arguments");
        string id = "0123456789abcdef0123456789abcdef";
        string basePath = args[0];
        Assembly assembly = Assembly.LoadFrom(args[1]);
        DevActionTargetPaths paths = DevActionTargetScope.Resolve(id, basePath);
        string root = Path.GetFullPath(Path.Combine(basePath, "HostAgent-DEV-Acceptance-" + id));
        Assert(paths.RunId == id, "run_id_roundtrip");
        Assert(paths.ServiceName == "0HostAgentDevAcceptance_" + id, "service_name_scope");
        Assert(paths.Root == root, "canonical_root");
        Assert(paths.App == Path.Combine(root, "app"), "canonical_app");
        Assert(paths.Data == Path.Combine(root, "data"), "canonical_data");
        Assert(paths.Executable == Path.Combine(root, "app", "HostAgent.DevActionTarget.exe"), "canonical_executable");
        Assert(Reject("", basePath), "empty_id_rejected");
        Assert(Reject("../" + id, basePath), "path_traversal_rejected");
        Assert(Reject(id.ToUpperInvariant(), basePath), "noncanonical_case_rejected");
        Assert(Reject(id + "0", basePath), "wrong_length_rejected");
        Assert(Reject("00000000000000000000000000000000", basePath), "empty_guid_rejected");
        Console.WriteLine("PASS scope_contract");
        TestServiceAcl(assembly, args[2]);
        TestRepeatedLifecycleWrites(typeof(DevActionTargetScope), args[2]);
        return 0;
    }
}
'@
    [IO.File]::WriteAllText($scopeTests, $scopeSource, [Text.UTF8Encoding]::new($false))

    $productArgs = @('/nologo', '/target:exe', ("/out:" + $app), '/r:System.ServiceProcess.dll', '/r:System.dll', '/r:System.Core.dll', $source)
    & $compiler @productArgs
    if ($LASTEXITCODE -ne 0) { throw 'target_compile_failed' }

    $testArgs = @('/nologo', '/target:exe', ("/out:" + $scopeTestsExe), ("/r:" + $app), $scopeTests)
    & $compiler @testArgs
    if ($LASTEXITCODE -ne 0) { throw 'scope_test_compile_failed' }
    & $scopeTestsExe $testRoot $app $testRoot
    if ($LASTEXITCODE -ne 0) { throw 'scope_contract_failed' }

    $invalid = [Diagnostics.ProcessStartInfo]::new()
    $invalid.FileName = $app
    $invalid.Arguments = 'not-a-command'
    $invalid.UseShellExecute = $false
    $invalid.CreateNoWindow = $true
    $invalid.RedirectStandardOutput = $true
    $invalid.RedirectStandardError = $true
    $process = [Diagnostics.Process]::Start($invalid)
    $stdout = $process.StandardOutput.ReadToEnd()
    $stderr = $process.StandardError.ReadToEnd()
    $process.WaitForExit()
    if ($process.ExitCode -ne 2 -or $stdout -notmatch '^\{"scope":"dev_action_target","valid":false,"failureCode":"invalid_arguments"\}\r?\n?$' -or $stderr.Length -ne 0) {
        throw 'invalid_arguments_contract_failed'
    }

    $badRun = [Diagnostics.ProcessStartInfo]::new()
    $badRun.FileName = $app
    $badRun.Arguments = 'run invalid'
    $badRun.UseShellExecute = $false
    $badRun.CreateNoWindow = $true
    $badRun.RedirectStandardOutput = $true
    $badRun.RedirectStandardError = $true
    $process = [Diagnostics.Process]::Start($badRun)
    $stdout = $process.StandardOutput.ReadToEnd()
    $stderr = $process.StandardError.ReadToEnd()
    $process.WaitForExit()
    if ($process.ExitCode -ne 2 -or $stdout -notmatch '^\{"scope":"dev_action_target","valid":false,"failureCode":"invalid_arguments"\}\r?\n?$' -or $stderr.Length -ne 0) {
        throw 'malformed_run_arguments_contract_failed'
    }
    'PASS invalid_arguments_no_scm'
} finally {
    if (Test-Path -LiteralPath $testRoot) {
        $finalPath = [IO.Path]::GetFullPath((Resolve-Path -LiteralPath $testRoot).Path).TrimEnd('\', '/')
        if (-not $finalPath.StartsWith($expectedParent, [StringComparison]::OrdinalIgnoreCase) -or
            [IO.Path]::GetFileName($finalPath) -notmatch '^HostAgent-DevActionTarget-Test-[0-9a-f]{32}$') { throw 'fixture_cleanup_boundary_rejected' }
        $attributes = [IO.File]::GetAttributes($finalPath)
        if (($attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'fixture_reparse_point' }
        Remove-Item -LiteralPath $finalPath -Recurse -Force
    }
}

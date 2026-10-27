using System.Diagnostics;
using System.Security.Principal;
using System.Text.Json;
using HostAgent.Core.Models.Audit;
using HostAgent.Core.Models.Policy;
using HostAgent.Core.Models.Remediation;
using HostAgent.Infrastructure.Storage;
using Xunit;

namespace HostAgent.Infrastructure.Tests;

[Collection("ReleaseTooling")]
public sealed class InstalledDatabaseSchemaTests
{
    [Fact]
    public async Task Readonly_diagnostic_accepts_databases_created_by_all_three_actual_product_stores()
    {
        var temporary = Path.Combine(Path.GetTempPath(), "hostagent-diagnostic-schema-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            await using (var store = new SqliteHostAgentStore(Path.Combine(temporary, "host-agent.db")))
                await store.InitializeAsync(default);
            Assert.False(await new SqliteNotificationStore(Path.Combine(temporary, "notifications.db")).HasFailuresAsync(default));
            var sid = WindowsIdentity.GetCurrent().User!.Value;
            await using (var journal = new SqliteHelperJournal(Path.Combine(temporary, "helper-journal.db"), [sid]))
                await journal.AppendAsync(new AuditRecord(Guid.NewGuid(), DateTimeOffset.UtcNow, AuditEvent.Proposal,
                    Guid.NewGuid(), Guid.NewGuid().ToString("N"), "fixture", 88, [], HelperOperation.StartService,
                    new string('A', 64), sid, PolicyDecision.CONFIRM_REQUIRED, null, null, null, "proposed"), default);

            var source = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
            var target = Path.Combine(source, "scripts/diagnostics/Read-InstalledDatabaseSnapshot.ps1");
            var probe = Path.Combine(temporary, "read-private-fixtures.ps1");
            await File.WriteAllTextAsync(probe, """
                param([string]$Target,[string]$Root)
                $ErrorActionPreference='Stop'
                . $Target -PackagePath 'unused_private_fixture' -ExpectedManifestSha256 ('A'*64) -ExpectedSourceCommit ('a'*40) -ExpectedVersion '1.2.9'
                # Private fixture only: portable CI/developer Python may replace the DEV operator's pinned tool boundary.
                # The installed diagnostic entry point retains its fixed verified Python path and hash.
                if(-not (Test-Path -LiteralPath $script:SnapshotPythonPath -PathType Leaf)){
                    $script:SnapshotPythonPath=(Get-Command python.exe -CommandType Application -ErrorAction Stop).Source
                    $script:SnapshotPythonSha256=(Get-FileHash -LiteralPath $script:SnapshotPythonPath).Hash
                }
                $copies=@(foreach($name in @('host-agent','notifications','helper-journal')){
                    [pscustomobject]@{Name=$name;MainPath=(Join-Path $Root ($name+'.db'))}
                })
                $checks=@(Invoke-SnapshotSqliteProbe -Copies $copies)
                ConvertTo-Json -InputObject $checks -Compress -Depth 6
                """);
            var info = new ProcessStartInfo("pwsh.exe")
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var value in new[] { "-NoProfile", "-NonInteractive", "-File", probe, "-Target", target, "-Root", temporary })
                info.ArgumentList.Add(value);
            using var process = Process.Start(info)!;
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(90)); }
            catch (TimeoutException) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); throw; }
            var text = await stdout; var errors = await stderr;
            Assert.True(process.ExitCode == 0, errors + text);
            using var document = JsonDocument.Parse(text);
            Assert.Equal(3, document.RootElement.GetArrayLength());
            Assert.All(document.RootElement.EnumerateArray(), result =>
            {
                Assert.True(result.GetProperty("schemaValid").GetBoolean(), result.GetRawText());
                Assert.Equal("ok", result.GetProperty("quickCheck").GetString());
            });
        }
        finally { Directory.Delete(temporary, recursive: true); }
    }
}

using System.Security.Principal;
using HostAgent.Core.Policy;
using HostAgent.Infrastructure.Pipes;
using HostAgent.Infrastructure.Security;
using HostAgent.Infrastructure.Storage;
using HostAgent.Helper;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

// No command-line operations, paths or policy overrides.
using var identity = WindowsIdentity.GetCurrent();
if (!identity.IsSystem) { Console.Error.WriteLine("helper_requires_system_identity"); return 1; }
var helperSid = identity.User!;
try
{
    var directory = HostAgentDataDirectory.EnsureSecure(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "HostAgent.Helper"), helperSid);
    var configurationPath = Path.Combine(directory, "helper.json");
    var databasePath = Path.Combine(directory, "helper-journal.db");
    foreach (var path in new[] { configurationPath, databasePath, databasePath + "-wal", databasePath + "-shm", databasePath + "-journal" })
        HelperProtectedFiles.ValidateExisting(path, helperSid);
    var configuration = HelperConfiguration.LoadSafe(configurationPath);
    var serviceSid = (SecurityIdentifier)new NTAccount("NT SERVICE", "HostAgent").Translate(typeof(SecurityIdentifier));
    await using var journal = new SqliteHelperJournal(databasePath, configuration.ApproverSids);
    var allowlist = new HelperAllowlist(configuration.Services, configuration.Processes);
    var dispatcher = new HelperDispatcher(journal, allowlist, new WindowsOperationExecutor(new WindowsOperationApi()),
        new PolicyEngine(), () => new HelperSettings(configuration.ReadOnly, configuration.Shadow,
            configuration.KillSwitch || File.Exists(Path.Combine(directory, "disable-remediation")),
            configuration.ConfigurationAvailable, configuration.ExecutionTimeoutSeconds));
    var server = new NamedPipeHelperServer("HostAgent.Helper.v1", new(serviceSid, helperSid), dispatcher, TimeSpan.FromSeconds(30));
    var builder=Host.CreateApplicationBuilder();
    builder.Services.AddWindowsService(options=>options.ServiceName="HostAgent.Helper");
    builder.Services.Configure<HostOptions>(options=>options.ShutdownTimeout=TimeSpan.FromSeconds(15));
    builder.Services.AddHostedService(services=>new HelperListenerWorker(
        token=>server.ServeOnceAsync(token),
        code=>services.GetRequiredService<ILogger<HelperListenerWorker>>().LogWarning("Helper worker {Code}.",code),
        ()=>Environment.Exit(1),
        TimeSpan.FromMilliseconds(250)));
    await builder.Build().RunAsync();
    return Environment.ExitCode;
}
catch (OperationCanceledException) { return 0; }
catch { Console.Error.WriteLine("helper_startup_failed"); return 1; }

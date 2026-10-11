using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using HostAgent.Core.Contracts;
using HostAgent.Core.Orchestration;
using HostAgent.Core.Models.Telemetry;
using HostAgent.Core.Metrics;
using HostAgent.Infrastructure.Collectors;
using HostAgent.Infrastructure.Security;
using HostAgent.Infrastructure.Storage;
using HostAgent.Service;
using System.Security.Principal;
using HostAgent.Infrastructure.Notifications;
using HostAgent.Infrastructure.Heartbeat;
using HostAgent.Infrastructure.Pipes;

var builder = Host.CreateApplicationBuilder();
// Runtime/policy/secrets use only protected local files, never command-line/environment binding.
const string serviceName = "HostAgent";
builder.Services.AddWindowsService(options => options.ServiceName = serviceName);
var serviceSid = (SecurityIdentifier)new NTAccount("NT SERVICE", serviceName)
    .Translate(typeof(SecurityIdentifier));
var dataDirectory = HostAgentDataDirectory.EnsureSecure(Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
    "HostAgent"), serviceSid);
var databasePath = Path.Combine(dataDirectory, "host-agent.db");
var configuration=ServiceConfiguration.LoadSafe(Path.Combine(dataDirectory,"service.json"),serviceSid);
builder.Services.AddSingleton<ProtectedSecretStore>(_=>new ProtectedSecretStore(
    Path.Combine(dataDirectory,builder.Environment.IsDevelopment()?"secrets-development":"secrets-production"),serviceSid));
builder.Services.AddSingleton<ISecretStore>(services=>services.GetRequiredService<ProtectedSecretStore>());
builder.Services.AddSingleton<INotificationStore>(_=>new SqliteNotificationStore(Path.Combine(dataDirectory,"notifications.db")));
builder.Services.AddSingleton(_=>OutboundHttp.CreateClient());
builder.Services.AddSingleton<INotificationSender>(services=>configuration.EnablePushover?
    new PushoverNotifier(services.GetRequiredService<HttpClient>(),services.GetRequiredService<ISecretStore>(),TimeSpan.FromSeconds(configuration.RequestTimeoutSeconds)):
    new LocalNotificationSender());
builder.Services.AddSingleton<IHeartbeatSender>(services=>configuration.EnableHeartbeat?
    new HealthchecksHeartbeat(services.GetRequiredService<HttpClient>(),services.GetRequiredService<ISecretStore>(),TimeSpan.FromSeconds(configuration.RequestTimeoutSeconds)):
    new DisabledHeartbeat());
builder.Services.AddSingleton<IHelperClient>(_=>new NamedPipeHelperClient("HostAgent.Helper.v1",
    new SecurityIdentifier(WellKnownSidType.LocalSystemSid,null),TimeSpan.FromSeconds(configuration.RequestTimeoutSeconds)));

builder.Services.AddSingleton<IHostAgentStore>(_ => new SqliteHostAgentStore(databasePath));
builder.Services.AddSingleton<ICollectorCheckpointStore>(services => services.GetRequiredService<IHostAgentStore>());
builder.Services.AddSingleton<DiagnosticCycleProcessor>();
builder.Services.AddSingleton<ITelemetryCollector<SystemTelemetry>, WindowsSystemCollector>();
builder.Services.AddSingleton<ITelemetryCollector<CpuMemoryTelemetry>, WindowsCpuMemoryCollector>();
builder.Services.AddSingleton<IDiskIoCounterReader, WindowsDiskIoCounterReader>();
builder.Services.AddSingleton<ITelemetryCollector<DiskTelemetry>, WindowsDiskCollector>();
builder.Services.AddSingleton<ITelemetryCollector<ProcessServiceTelemetry>, WindowsProcessServicesCollector>();
builder.Services.AddSingleton<ITelemetryCollector<NetworkTelemetry>, WindowsNetworkCollector>();
builder.Services.AddSingleton<ITelemetryCollector<EventLogTelemetry>>(services =>
    new WindowsEventLogCollector(services.GetRequiredService<ICollectorCheckpointStore>()));

builder.Services.AddSingleton<ITelemetryMetricProjector<SystemTelemetry>, SystemTelemetryMetricProjector>();
builder.Services.AddSingleton<ITelemetryMetricProjector<CpuMemoryTelemetry>, CpuMemoryMetricProjector>();
builder.Services.AddSingleton<ITelemetryMetricProjector<DiskTelemetry>, DiskTelemetryMetricProjector>();
builder.Services.AddSingleton<ITelemetryMetricProjector<ProcessServiceTelemetry>, ProcessServiceMetricProjector>();
builder.Services.AddSingleton<ITelemetryMetricProjector<NetworkTelemetry>, NetworkTelemetryMetricProjector>();
builder.Services.AddSingleton<ITelemetryMetricProjector<EventLogTelemetry>, EventLogMetricProjector>();

builder.Services.AddSingleton<ITelemetryCollectionJob>(services => new TelemetryCollectionJob<SystemTelemetry>(
    services.GetRequiredService<ITelemetryCollector<SystemTelemetry>>(),
    services.GetRequiredService<IHostAgentStore>(),
    services.GetRequiredService<ITelemetryMetricProjector<SystemTelemetry>>()));
builder.Services.AddSingleton<ITelemetryCollectionJob>(services => new TelemetryCollectionJob<CpuMemoryTelemetry>(
    services.GetRequiredService<ITelemetryCollector<CpuMemoryTelemetry>>(),
    services.GetRequiredService<IHostAgentStore>(),
    services.GetRequiredService<ITelemetryMetricProjector<CpuMemoryTelemetry>>()));
builder.Services.AddSingleton<ITelemetryCollectionJob>(services => new TelemetryCollectionJob<DiskTelemetry>(
    services.GetRequiredService<ITelemetryCollector<DiskTelemetry>>(),
    services.GetRequiredService<IHostAgentStore>(),
    services.GetRequiredService<ITelemetryMetricProjector<DiskTelemetry>>()));
builder.Services.AddSingleton<ITelemetryCollectionJob>(services => new TelemetryCollectionJob<ProcessServiceTelemetry>(
    services.GetRequiredService<ITelemetryCollector<ProcessServiceTelemetry>>(),
    services.GetRequiredService<IHostAgentStore>(),
    services.GetRequiredService<ITelemetryMetricProjector<ProcessServiceTelemetry>>()));
builder.Services.AddSingleton<ITelemetryCollectionJob>(services => new TelemetryCollectionJob<NetworkTelemetry>(
    services.GetRequiredService<ITelemetryCollector<NetworkTelemetry>>(),
    services.GetRequiredService<IHostAgentStore>(),
    services.GetRequiredService<ITelemetryMetricProjector<NetworkTelemetry>>()));
builder.Services.AddSingleton<ITelemetryCollectionJob>(services => new TelemetryCollectionJob<EventLogTelemetry>(
    services.GetRequiredService<ITelemetryCollector<EventLogTelemetry>>(),
    services.GetRequiredService<IHostAgentStore>(),
    services.GetRequiredService<ITelemetryMetricProjector<EventLogTelemetry>>()));
ServiceRuntime.AddWorkers(builder.Services,configuration);
ServiceAdministration.Add(builder.Services,serviceSid);

await builder.Build().RunAsync();

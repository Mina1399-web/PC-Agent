using System.Security.Principal;
using System.ServiceProcess;
using HostAgent.Cli;
using HostAgent.Infrastructure.Administration;
using HostAgent.Infrastructure.Security;
using HostAgent.Infrastructure.Storage;

if(args.Length==0||args is ["--help"]){Console.WriteLine(CliApplication.Usage);return 0;}
using var identity=WindowsIdentity.GetCurrent();
if(args is ["--installer-bootstrap"])
{
    if(!identity.IsSystem){Console.Error.WriteLine("installer_requires_system");return 1;}
    try
    {
        var monitor=(SecurityIdentifier)new NTAccount("NT SERVICE","HostAgent").Translate(typeof(SecurityIdentifier));
        InstallationBootstrap.Provision(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),monitor,identity.User!);
        return 0;
    }
    catch{Console.Error.WriteLine("installer_bootstrap_failed");return 1;}
}
if(!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
{Console.Error.WriteLine("administrator_required");return 1;}
try
{
    var sid=(SecurityIdentifier)new NTAccount("NT SERVICE","HostAgent").Translate(typeof(SecurityIdentifier));
    var system=new SecurityIdentifier(WellKnownSidType.LocalSystemSid,null);
    var programData=Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
    var client=new AdminPipeClient("HostAgent.Admin.v1",sid,TimeSpan.FromSeconds(30));
    using var cancellation=new CancellationTokenSource();
    Console.CancelKeyPress+=(_,e)=>{e.Cancel=true;cancellation.Cancel();};
    if(args[0] is "configure" or "network-probes" or "allow-service" or "allow-process" or "remove-service" or "remove-process")
    {
        var workflow=new ConfigurationWorkflow(values=>
        {
            if(Console.IsInputRedirected)throw new InvalidOperationException("interactive_confirmation_required");
            Console.WriteLine("Configuration: "+System.Text.Json.JsonSerializer.Serialize(values));
            if(values is ["network-probes","on"])
                Console.WriteLine("Network probes send ICMP to the gateway and 1.1.1.1, and resolve example.com via your DNS resolver. Receivers observe your IP/timing. Read docs/operations/privacy.md; network-probes off disables these requests.");
            Console.Write("This restarts only HostAgent services. Type APPLY to continue: ");return Console.ReadLine()=="APPLY";
        },token=>ProductServices(false,token),values=>OperatorConfiguration.Apply(programData,sid,system,identity.User!.Value,values),token=>ProductServices(true,token));
        var changed=await workflow.RunAsync(args,cancellation.Token);
        Console.WriteLine(changed?"configuration_saved":"configuration_cancelled");return changed?0:1;
    }
    HostAgent.Core.Models.Remediation.ApprovedTarget? displayedTarget=null;
    Guid? displayedRequestId=null;
    async ValueTask<HostAgent.Core.Models.Remediation.OperationApproval> Approve(HostAgent.Core.Models.Remediation.HelperOperationRequest request,CancellationToken token)
    {
        var directory=HostAgentDataDirectory.EnsureSecure(Path.Combine(programData,"HostAgent.Helper"),system);
        var configurationPath=Path.Combine(directory,"helper.json");var database=Path.Combine(directory,"helper-journal.db");
        foreach(var path in new[]{configurationPath,database,database+"-wal",database+"-shm",database+"-journal"})HelperProtectedFiles.ValidateExisting(path,system);
        var config=HelperConfiguration.LoadSafe(configurationPath);
        if(!config.ConfigurationAvailable||config.ReadOnly||config.Shadow||config.KillSwitch||File.Exists(Path.Combine(directory,"disable-remediation"))||
            new HelperAllowlist(config.Services,config.Processes).Resolve(request) is null)throw new UnauthorizedAccessException("approval_disabled");
        var binding=new HelperAllowlist(config.Services,config.Processes).Resolve(request)!;
        if(displayedTarget is null||displayedRequestId!=request.RequestId)throw new InvalidDataException("approval_target_not_displayed");
        await using var journal=new SqliteHelperJournal(database,config.ApproverSids);
        return await new ApprovalIssuer(journal,identity.User!.Value,TimeProvider.System).CreateAsync(request,displayedTarget,
            new(binding.ServiceName,binding.ExecutablePath,binding.Sha256),token);
    }
    ValueTask<string?> Confirm(string requestId,CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if(Console.IsInputRedirected)throw new InvalidOperationException("interactive_confirmation_required");
        Console.Write("Approve this exact operation by typing "+requestId+": ");
        return ValueTask.FromResult(Console.ReadLine());
    }
    ValueTask<string> Describe(HostAgent.Core.Models.Remediation.HelperOperationRequest request,CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var directory=HostAgentDataDirectory.EnsureSecure(Path.Combine(programData,"HostAgent.Helper"),system);
        var path=Path.Combine(directory,"helper.json");HelperProtectedFiles.ValidateExisting(path,system);
        var config=HelperConfiguration.LoadSafe(path);
        if(!config.ConfigurationAvailable)throw new InvalidDataException("configuration_unavailable");
        var binding=new HelperAllowlist(config.Services,config.Processes).Resolve(request)??throw new InvalidDataException("target_not_registered");
        displayedTarget=new(binding.ServiceName,binding.ExecutablePath,binding.Sha256);displayedRequestId=request.RequestId;
        return ValueTask.FromResult("Resolved local target: "+System.Text.Json.JsonSerializer.Serialize(binding));
    }
    var app=new CliApplication(client.SendAsync,ReadSecret,Confirm,Approve,Console.Out,Path.Combine(programData,"HostAgent.Operator"),describe:Describe);
    return await app.RunAsync(args,cancellation.Token);
}
catch{Console.Error.WriteLine("operator_startup_failed");return 1;}

static async Task ProductServices(bool start,CancellationToken token)
{
    // Fixed product names only; no command line or configuration can select a service here.
    foreach(var name in start?new[]{"HostAgent.Helper","HostAgent"}:new[]{"HostAgent","HostAgent.Helper"})
    {
        token.ThrowIfCancellationRequested();using var service=new ServiceController(name);
        var desired=start?ServiceControllerStatus.Running:ServiceControllerStatus.Stopped;
        service.Refresh();if(service.Status==desired)continue;
        if(start)service.Start();else service.Stop();
        await Task.Run(()=>service.WaitForStatus(desired,TimeSpan.FromSeconds(30)),token);
    }
}

static async ValueTask<string?> ReadSecret(CancellationToken token)
{
    if(Console.IsInputRedirected)throw new InvalidOperationException("interactive_secret_required");
    Console.Write("Secret (masked; Esc cancels): ");var buffer=new char[4096];var count=0;
    try
    {
        while(true)
        {
            token.ThrowIfCancellationRequested();
            if(!Console.KeyAvailable){await Task.Delay(50,token);continue;}
            var key=Console.ReadKey(intercept:true);
            if(key.Key==ConsoleKey.Escape){Console.WriteLine();return null;}
            if(key.Key==ConsoleKey.Enter){Console.WriteLine();return new string(buffer,0,count);}
            if(key.Key==ConsoleKey.Backspace){if(count>0){buffer[--count]='\0';Console.Write("\b \b");}continue;}
            if(!char.IsControl(key.KeyChar)&&count<buffer.Length){buffer[count++]=key.KeyChar;Console.Write('*');}
        }
    }
    finally{Array.Clear(buffer);}
}

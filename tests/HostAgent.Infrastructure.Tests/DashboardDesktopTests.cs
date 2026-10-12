using System.Text.Json;
using HostAgent.Core.Dashboard;
using HostAgent.Infrastructure.Administration;
using HostAgent.Infrastructure.Dashboard;
using Xunit;
namespace HostAgent.Infrastructure.Tests;

public sealed class DashboardDesktopTests
{
    [Fact]
    public void Typed_operator_arguments_bind_exact_incident_alias_pid_and_start_time_without_shell_text()
    {
        var id=Guid.NewGuid().ToString("N");var view=Sample with{Mode="Confirmation",ReadOnly=false};
        Assert.Equal(new[]{"propose-and-review",id,"RestartService","registered"},DashboardCommands.ProposalArguments(view,id,HostAgent.Core.Models.Remediation.HelperOperation.RestartService,"registered"));
        Assert.Equal(new[]{"propose-and-review",id,"TerminateProcess","registered","42","100"},DashboardCommands.ProposalArguments(view,id,HostAgent.Core.Models.Remediation.HelperOperation.TerminateProcess,"registered",42,100));
        Assert.ThrowsAny<Exception>(()=>DashboardCommands.ProposalArguments(Sample,id,HostAgent.Core.Models.Remediation.HelperOperation.RestartService,"registered"));
        foreach(var alias in new[]{"bad alias","x;calc","..\\other",""})Assert.ThrowsAny<Exception>(()=>DashboardCommands.ProposalArguments(view,id,HostAgent.Core.Models.Remediation.HelperOperation.RestartService,alias));
        Assert.ThrowsAny<Exception>(()=>DashboardCommands.ProposalArguments(view,"invalid",HostAgent.Core.Models.Remediation.HelperOperation.RestartService,"registered"));
        Assert.ThrowsAny<Exception>(()=>DashboardCommands.ProposalArguments(view,id,HostAgent.Core.Models.Remediation.HelperOperation.TerminateProcess,"registered",42,null));
    }
    private static DashboardSnapshot Sample=>new(DateTimeOffset.UtcNow,"Shadow",true,false,null,null,null,[],[],[],null,[],[],[],[]);
    [Fact]
    public async Task Refresh_uses_only_read_pipe_and_clears_old_view_when_service_disappears()
    {
        var available=true;var calls=0;
        var controller=new DashboardController((bytes,_)=>
        {calls++;Assert.Equal("overview",ViewProtocol.Parse(bytes).Command);return ValueTask.FromResult(available?AdminProtocol.Reply(true,"overview",Sample):AdminProtocol.Reply(false,"view_unavailable"));});
        await controller.RefreshAsync(default);Assert.NotNull(controller.Snapshot);Assert.Equal("connected",controller.State);
        available=false;await controller.RefreshAsync(default);Assert.Null(controller.Snapshot);Assert.Equal("disconnected",controller.State);Assert.Equal(2,calls);
    }
    [Fact]
    public async Task Stale_view_is_visibly_marked_and_bad_envelope_is_not_accepted()
    {
        var controller=new DashboardController((_,_)=>ValueTask.FromResult(AdminProtocol.Reply(true,"overview",Sample with{GeneratedAtUtc=DateTimeOffset.UtcNow.AddHours(-1)})));
        await controller.RefreshAsync(default);Assert.Equal("stale",controller.State);Assert.Null(controller.Snapshot);
        controller=new((_,_)=>ValueTask.FromResult("{\"ok\":true,\"data\":{}}"u8.ToArray()));
        await controller.RefreshAsync(default);Assert.Equal("disconnected",controller.State);Assert.Null(controller.Snapshot);
    }
    [Fact]
    public void Redacted_export_contains_only_versioned_numeric_evidence_and_no_environment_details()
    {
        var detail=new ViewIncidentDetail(new(Guid.NewGuid().ToString("N"),"Active","windows_bugcheck",88,"High",DateTimeOffset.UtcNow,2),
            "stop_code_classification_only",[new("eventlog",10,DateTimeOffset.UtcNow)],false);
        using var doc=JsonDocument.Parse(DashboardController.Export(detail));
        Assert.Equal(1,doc.RootElement.GetProperty("format").GetInt32());
        Assert.Equal(88,doc.RootElement.GetProperty("incident").GetProperty("Confidence").GetInt32());
        Assert.Contains("stop_code_classification_only",doc.RootElement.GetRawText());
        Assert.DoesNotContain("MachineName",doc.RootElement.GetRawText());Assert.DoesNotContain("ExecutablePath",doc.RootElement.GetRawText());
    }
}

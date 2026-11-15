using System;
using System.Linq;
using System.Security.Principal;
using System.Windows;
using HostAgent.Infrastructure.Administration;
using HostAgent.Infrastructure.Dashboard;

namespace HostAgent.Desktop;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        var localization=DesktopLocalization.ForCurrentUser();
        string T(string source, params object?[] values) => localization.Text(source, values);
        var app=new Application();
        try
        {
            var owner=(SecurityIdentifier)new NTAccount("NT SERVICE","HostAgent").Translate(typeof(SecurityIdentifier));
            if(args.Length>0)
            {
                if(!TryParseServiceRecoveryArguments(args,out var incident,out var serviceName,out var darkTheme))
                {
                    new LocalizedMessages(localization).Show(null,T("ServiceReviewUnavailable","review_arguments_invalid"));
                    return 2;
                }
                var pipe=new AdminPipeClient("HostAgent.Admin.v1",owner,TimeSpan.FromSeconds(10));
                var authority=new WindowsGuidedServiceRecoveryAuthority();
                var workflow=new GuidedServiceRecoveryWorkflow(pipe.SendAsync,authority);
                var window=new GuidedServiceRecoveryWindow(workflow,incident.ToString("N"),serviceName,localization,darkTheme);
                app.Run(window);
                return window.ExitCode;
            }
            var client=new ViewPipeClient("HostAgent.View.v1",owner,TimeSpan.FromSeconds(10));
            return app.Run(new MainWindow(new DashboardController(client.SendAsync),localization));
        }
        catch
        {
            new LocalizedMessages(localization).Show(null,args.Length>0
                ?T("ServiceReviewUnavailable","review_startup_failed")
                :T("HostAgentIsUnavailableCheckTheServiceInstallationInThisDEVVM"));
            return args.Length>0?2:1;
        }
    }


    internal static bool TryParseServiceRecoveryArguments(string[] args, out Guid incident, out string serviceName, out bool darkTheme)
    {
        incident = Guid.Empty;
        serviceName = string.Empty;
        darkTheme = false;
        if ((args.Length != 3 && args.Length != 4) || args[0] != "--review-service-crash" ||
            !Guid.TryParseExact(args[1], "N", out incident) || incident == Guid.Empty || !SafeServiceName(args[2]) ||
            (args.Length == 4 && args[3] != "--dark-theme"))
            return false;
        serviceName = args[2];
        darkTheme = args.Length == 4;
        return true;
    }

    private static bool SafeServiceName(string value)=>value is{Length:>0 and <=128}&&
        value.All(c=>char.IsAsciiLetterOrDigit(c)||c is '.' or '-' or '_');
}

using System;
using System.Security.Principal;
using System.Windows;
using HostAgent.Infrastructure.Dashboard;

namespace HostAgent.Desktop;

public static class Program
{
    [STAThread]
    public static int Main()
    {
        var localization=DesktopLocalization.ForCurrentUser();
        string T(string source)=>localization.Text(source);
        var app=new Application();
        try
        {
            var owner=(SecurityIdentifier)new NTAccount("NT SERVICE","HostAgent").Translate(typeof(SecurityIdentifier));
            var client=new ViewPipeClient("HostAgent.View.v1",owner,TimeSpan.FromSeconds(10));
            return app.Run(new MainWindow(new DashboardController(client.SendAsync),localization));
        }
        catch
        {
            new LocalizedMessages(localization).Show(null,T("HostAgentIsUnavailableCheckTheServiceInstallationInThisDEVVM"));
            return 1;
        }
    }
}

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
        var app=new Application();
        try
        {
            var owner=(SecurityIdentifier)new NTAccount("NT SERVICE","HostAgent").Translate(typeof(SecurityIdentifier));
            var client=new ViewPipeClient("HostAgent.View.v1",owner,TimeSpan.FromSeconds(10));
            return app.Run(new MainWindow(new DashboardController(client.SendAsync)));
        }
        catch
        {
            MessageBox.Show("Host Agent není dostupný. Ověřte instalaci služby v této DEV VM.","Host Agent",MessageBoxButton.OK,MessageBoxImage.Information);
            return 1;
        }
    }
}

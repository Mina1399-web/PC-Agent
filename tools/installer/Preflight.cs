using System;
using System.IO;
using System.Security.Principal;
using HostAgent.Infrastructure.Security;
using WixToolset.Dtf.WindowsInstaller;
public static class Preflight
{
    [CustomAction]
    public static ActionResult ValidateInstallTree(Session session)
    {
        try
        {
            using (var identity = WindowsIdentity.GetCurrent())
                if (!identity.IsSystem) throw new UnauthorizedAccessException();
            var installer = (SecurityIdentifier)new NTAccount("NT SERVICE", "TrustedInstaller").Translate(typeof(SecurityIdentifier));
            InstallationTreeSecurity.Validate(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "HostAgent"),
                new[] { "S-1-5-18", "S-1-5-32-544", installer.Value });
            return ActionResult.Success;
        }
        catch
        {
            session.Log("HostAgent installation tree preflight refused the target. No payload has been copied.");
            return ActionResult.Failure;
        }
    }
}

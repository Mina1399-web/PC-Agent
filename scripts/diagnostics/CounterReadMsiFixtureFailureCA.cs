using WixToolset.Dtf.WindowsInstaller;

// This assembly is linked only into the GUID private MSI, never the product MSI.
public static class CounterReadMsiFixtureFailure
{
    [CustomAction]
    public static ActionResult ForceFailure(Session session)
    {
        // Exact fixed protocol marker; no session properties or payload data.
        // Native acceptance must observe this before accepting an expected1603.
        using(var receipt=new Record(0))
        {
            receipt.FormatString=session.GetMode(InstallRunMode.Commit)
                ? "HostAgent.GuidFixture.Fault.Commit.v1"
                : "HostAgent.GuidFixture.Fault.Deferred.v1";
            session.Message(InstallMessage.Info,receipt);
        }
        return ActionResult.Failure;
    }
}

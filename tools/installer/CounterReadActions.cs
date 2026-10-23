using System;
using System.ComponentModel;
using WixToolset.Dtf.WindowsInstaller;
namespace HostAgent.Installer
{
    public static class CounterReadActions
    {
        [CustomAction] public static ActionResult PrepareCounterRead(Session session)
        {
            try
            {
                if(!String.IsNullOrEmpty(session["DISABLEROLLBACK"]) || !String.IsNullOrEmpty(session["RollbackDisabled"]))
                    throw new InvalidOperationException("counter_read_rollback_required");
                // A repair has the same ProductCode but is a different transaction.
                // Never let its rollback select a previous successful attempt.
                var attempt=Guid.NewGuid().ToString("N");
                foreach(var mode in new[]{"Grant","Release"})
                {
                    var data=new CustomActionData(); data.Add("Transaction",(mode=="Grant"?"g":"r")+attempt);
                    foreach(var prefix in new[]{"Rollback","Commit"})session[prefix+"CounterRead"+mode]=data.ToString();
                    session[mode+"CounterRead"]=data.ToString();
                }
                return ActionResult.Success;
            }
            catch {session.Log("HostAgent counter-read preparation refused; MSI rollback is required.");return ActionResult.Failure;}
        }
        [CustomAction] public static ActionResult GrantCounterRead(Session session) {return Run(session,true,0);}
        [CustomAction] public static ActionResult ReleaseCounterRead(Session session) {return Run(session,false,0);}
        [CustomAction] public static ActionResult RollbackCounterReadGrant(Session session) {return Run(session,true,1);}
        [CustomAction] public static ActionResult RollbackCounterReadRelease(Session session) {return Run(session,false,1);}
        [CustomAction] public static ActionResult CommitCounterReadGrant(Session session) {return Run(session,true,2);}
        [CustomAction] public static ActionResult CommitCounterReadRelease(Session session) {return Run(session,false,2);}
        static ActionResult Run(Session session,bool grant,int phase)
        {
            try
            {
                var id=session.CustomActionData["Transaction"];
                CounterReadTransaction.ValidateId(id);
                if(id[0]!=(grant?'g':'r'))throw new InvalidOperationException("counter_read_transaction_invalid");
                using(var environment=new WindowsCounterReadState())
                {
                    var transaction=new CounterReadTransaction(environment,environment);
                    if(phase==0){environment.ValidateRegisteredMember();transaction.Apply(id,grant);}
                    else if(phase==1)transaction.Rollback(id);
                    else transaction.Commit(id);
                }
                return ActionResult.Success;
            }
            catch(Win32Exception error) {session.Log("HostAgent counter-read native stage failed with code {0}; inspect installer journal before retry.",error.NativeErrorCode);return ActionResult.Failure;}
            catch {session.Log("HostAgent counter-read transition/cleanup incomplete; state preserved for review.");return ActionResult.Failure;}
        }
    }
}

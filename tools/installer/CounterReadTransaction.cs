using System;
namespace HostAgent.Installer
{
    public static class CounterReadJournalCodec
    {
        public static string Encode(string id,string member,CounterReadJournal journal)
        {
            CounterReadTransaction.ValidateId(id);
            if(string.IsNullOrEmpty(member) || member.IndexOf('|')>=0 || journal==null)
                throw new InvalidOperationException("counter_read_journal_invalid");
            Validate(journal);
            var value=id+"|"+member+"|1|"+Bit(journal.BeforeMember)+"|"+Bit(journal.BeforeOwned)+"|"+Bit(journal.AfterMember)+"|"+Bit(journal.AfterOwned)+"|"+Bit(journal.Applied)+"|"+Bit(journal.Committed);
            if(value.Length>256)throw new InvalidOperationException("counter_read_journal_invalid");
            return value;
        }
        public static CounterReadJournal Decode(string value,string member,out string id)
        {
            id="";
            if(value==null || value.Length>256 || string.IsNullOrEmpty(member))
                throw new InvalidOperationException("counter_read_journal_invalid");
            var parts=value.Split('|');
            if(parts.Length!=9 || parts[1]!=member || parts[2]!="1")
                throw new InvalidOperationException("counter_read_journal_invalid");
            CounterReadTransaction.ValidateId(parts[0]);
            for(var i=3;i<9;i++)if(parts[i]!="0" && parts[i]!="1")
                throw new InvalidOperationException("counter_read_journal_invalid");
            var journal=new CounterReadJournal{BeforeMember=parts[3]=="1",BeforeOwned=parts[4]=="1",AfterMember=parts[5]=="1",AfterOwned=parts[6]=="1",Applied=parts[7]=="1",Committed=parts[8]=="1"};
            Validate(journal); id=parts[0]; return journal;
        }
        static void Validate(CounterReadJournal journal)
        {
            if((journal.Committed && !journal.Applied) || (journal.Applied && journal.AfterOwned && !journal.AfterMember))
                throw new InvalidOperationException("counter_read_journal_invalid");
        }
        static string Bit(bool value) {return value?"1":"0";}
    }
    public interface ICounterReadMembership
    {
        bool Contains();
        bool Add();
        bool Remove();
    }
    public interface ICounterReadState
    {
        bool ReadOwned();
        bool HasOtherJournal(string id);
        void WriteOwned(bool owned);
        CounterReadJournal ReadJournal(string id);
        void WriteJournal(string id, CounterReadJournal journal, bool create);
        void DeleteJournal(string id);
    }
    public sealed class CounterReadJournal
    {
        public bool BeforeMember, BeforeOwned, AfterMember, AfterOwned, Applied, Committed;
    }
    public sealed class CounterReadTransaction
    {
        readonly ICounterReadMembership membership;
        readonly ICounterReadState state;
        public CounterReadTransaction(ICounterReadMembership membership, ICounterReadState state)
        {
            if(membership==null)throw new ArgumentNullException("membership");
            if(state==null)throw new ArgumentNullException("state");
            this.membership=membership; this.state=state;
        }
        public static void ValidateId(string id)
        {
            Guid parsed;
            if(id==null || id.Length!=33 || (id[0]!='g' && id[0]!='r') || !Guid.TryParseExact(id.Substring(1),"N",out parsed))
                throw new InvalidOperationException("counter_read_transaction_invalid");
        }
        public void Apply(string id, bool grant)
        {
            ValidateId(id);
            if(state.HasOtherJournal(id))throw new InvalidOperationException("counter_read_prior_transition_pending");
            var previous=state.ReadJournal(id);
            if(previous!=null)throw new InvalidOperationException("counter_read_journal_exists");
            var journal=new CounterReadJournal{BeforeMember=membership.Contains(),BeforeOwned=state.ReadOwned()};
            journal.AfterMember=journal.BeforeMember; journal.AfterOwned=journal.BeforeOwned;
            state.WriteJournal(id,journal,true); // Durable intent precedes the SAM boundary.
            var nativeKnown=false;
            try
            {
                if(grant && !journal.BeforeMember)
                {
                    var added=membership.Add(); nativeKnown=true;
                    journal.AfterMember=true; journal.AfterOwned=added || journal.BeforeOwned;
                    // A concurrent administrator's add is not ours to remove.
                    if(!added)journal.BeforeMember=true;
                }
                else if(!grant && journal.BeforeOwned)
                {
                    if(journal.BeforeMember)
                    {
                        var removed=membership.Remove(); nativeKnown=true;
                        if(!removed)journal.BeforeMember=false;
                    }
                    journal.AfterMember=false; journal.AfterOwned=false;
                }
                nativeKnown=true;
                if(membership.Contains()!=journal.AfterMember)throw new InvalidOperationException("counter_read_transition_uncertain");
                journal.Applied=true;
                state.WriteJournal(id,journal,false);
                state.WriteOwned(journal.AfterOwned);
            }
            catch
            {
                // In-process failure can reverse a known result. Following process
                // death, only the persisted confirmed journal authorizes reversal.
                try
                {
                    if(nativeKnown)Restore(journal);
                    else if(membership.Contains()!=journal.BeforeMember || state.ReadOwned()!=journal.BeforeOwned)
                        throw new InvalidOperationException("counter_read_transition_uncertain");
                    state.DeleteJournal(id);
                }
                catch {throw new InvalidOperationException("counter_read_cleanup_uncertain");}
                throw;
            }
        }
        public void Rollback(string id)
        {
            ValidateId(id); var journal=state.ReadJournal(id); if(journal==null)return;
            if(journal.Applied)Restore(journal);
            else if(membership.Contains()!=journal.BeforeMember || state.ReadOwned()!=journal.BeforeOwned)
                throw new InvalidOperationException("counter_read_transition_uncertain");
            state.DeleteJournal(id);
        }
        public void Commit(string id)
        {
            ValidateId(id); var journal=state.ReadJournal(id);
            if(journal==null || !journal.Applied || membership.Contains()!=journal.AfterMember || state.ReadOwned()!=journal.AfterOwned)
                throw new InvalidOperationException("counter_read_transition_uncertain");
            // A later commit action can still fail and cause MSI rollback. Keep
            // the before-state; a later serialized MSI transaction may replace it.
            journal.Committed=true;
            state.WriteJournal(id,journal,false);
        }
        void Restore(CounterReadJournal journal)
        {
            if(journal.BeforeMember!=journal.AfterMember && membership.Contains()!=journal.BeforeMember)
            {
                if(journal.BeforeMember)membership.Add();else membership.Remove();
                if(membership.Contains()!=journal.BeforeMember)throw new InvalidOperationException("counter_read_transition_uncertain");
            }
            state.WriteOwned(journal.BeforeOwned);
        }
    }
}

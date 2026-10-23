using HostAgent.Installer;
using Xunit;
namespace HostAgent.Infrastructure.Tests;
public sealed class CounterReadTransactionTests
{
    const string Id="g11111111111111111111111111111111";
    const string NextId="g22222222222222222222222222222222";
    // Defect: a clean install starts the virtual service without read-only counter rights.
    [Fact]
    public void Missing_membership_is_added_owned_and_rolled_back()
    {
        var f=new Fixture(); f.Transaction.Apply(Id,true);
        Assert.True(f.Member); Assert.True(f.Owned); Assert.Equal(1,f.Adds);
        Assert.True(f.Journal!.Applied);
        f.Transaction.Rollback(Id);
        Assert.False(f.Member); Assert.False(f.Owned); Assert.Equal(1,f.Removes); Assert.Null(f.Journal);
    }
    // Defect: uninstall or rollback removes membership previously granted by an operator.
    [Theory]
    [InlineData(true)] [InlineData(false)]
    public void Preexisting_unowned_membership_is_never_removed(bool grant)
    {
        var f=new Fixture{Member=true}; f.Transaction.Apply(Id,grant); f.Transaction.Rollback(Id);
        Assert.True(f.Member); Assert.False(f.Owned); Assert.Equal(0,f.Adds); Assert.Equal(0,f.Removes);
    }
    [Fact]
    public void Owned_membership_is_released_and_uninstall_rollback_restores_it()
    {
        var f=new Fixture{Member=true,Owned=true}; f.Transaction.Apply(Id,false);
        Assert.False(f.Member); Assert.False(f.Owned);
        f.Transaction.Rollback(Id);
        Assert.True(f.Member); Assert.True(f.Owned); Assert.Equal(1,f.Adds); Assert.Equal(1,f.Removes);
    }
    [Fact]
    public void Repair_retains_existing_ownership_without_duplicate_add()
    {
        var f=new Fixture{Member=true,Owned=true}; f.Transaction.Apply(Id,true); f.Transaction.Commit(Id);
        Assert.True(f.Member); Assert.True(f.Owned); Assert.Equal(0,f.Adds); Assert.True(f.Journal!.Committed);
        f.Transaction.Apply(NextId,true); f.Transaction.Commit(NextId);
        Assert.True(f.Member); Assert.True(f.Owned); Assert.Equal(0,f.Adds);
    }
    [Fact]
    public void Replaying_a_committed_attempt_is_refused_before_mutation()
    {
        var f=new Fixture(); f.Transaction.Apply(Id,true); f.Transaction.Commit(Id);
        Assert.Throws<InvalidOperationException>(()=>f.Transaction.Apply(Id,true));
        Assert.True(f.Member); Assert.True(f.Owned); Assert.Equal(1,f.Adds);
    }
    [Fact]
    public void New_attempt_rollback_cannot_undo_an_earlier_success_when_apply_never_started()
    {
        var f=new Fixture(); f.Transaction.Apply(Id,true); f.Transaction.Commit(Id); f.OtherPending=true;
        Assert.Throws<InvalidOperationException>(()=>f.Transaction.Apply(NextId,true));
        f.Transaction.Rollback(NextId);
        Assert.True(f.Member); Assert.True(f.Owned); Assert.True(f.Journal!.Committed); Assert.Equal(0,f.Removes);
    }
    [Fact]
    public void Already_member_native_result_does_not_take_ownership()
    {
        var f=new Fixture{AddRaces=true}; f.Transaction.Apply(Id,true); f.Transaction.Rollback(Id);
        Assert.True(f.Member); Assert.False(f.Owned); Assert.Equal(0,f.Removes);
    }
    [Fact]
    public void Interrupted_ambiguous_journal_preserves_membership_and_refuses_destructive_guess()
    {
        var f=new Fixture{Member=true,Journal=new(){BeforeMember=false,BeforeOwned=false,Applied=false}};
        var ex=Assert.Throws<InvalidOperationException>(()=>f.Transaction.Rollback(Id));
        Assert.Equal("counter_read_transition_uncertain",ex.Message); Assert.True(f.Member); Assert.NotNull(f.Journal); Assert.Equal(0,f.Removes);
    }
    [Fact]
    public void Old_journal_blocks_replay_before_native_mutation()
    {
        var f=new Fixture{Journal=new()};
        Assert.Throws<InvalidOperationException>(()=>f.Transaction.Apply(Id,true)); Assert.Equal(0,f.Adds);
    }
    // Defect: a new ProductCode ignores an interrupted older transaction and
    // adopts ambiguous membership instead of requiring operator recovery.
    [Fact]
    public void Other_product_journal_blocks_new_apply_without_changing_membership()
    {
        var f=new Fixture{OtherPending=true};
        var error=Assert.Throws<InvalidOperationException>(()=>f.Transaction.Apply(Id,true));
        Assert.Equal("counter_read_prior_transition_pending",error.Message);
        Assert.False(f.Member); Assert.False(f.Owned); Assert.Equal(0,f.Adds); Assert.Null(f.Journal);
    }
    [Fact]
    public void Native_failure_without_change_preserves_original_state_and_surfaces_error()
    {
        var f=new Fixture{FailAdd=true};
        Assert.Throws<InvalidOperationException>(()=>f.Transaction.Apply(Id,true));
        Assert.False(f.Member); Assert.False(f.Owned); Assert.Null(f.Journal);
    }
    [Fact]
    public void Ownership_write_failure_rolls_back_known_native_change()
    {
        var f=new Fixture{FailOwnedOnce=true};
        Assert.Throws<InvalidOperationException>(()=>f.Transaction.Apply(Id,true));
        Assert.False(f.Member); Assert.False(f.Owned); Assert.Null(f.Journal);
    }
    [Fact]
    public void Cannot_commit_an_unconfirmed_transition()
    {
        var f=new Fixture{Journal=new()};
        Assert.Throws<InvalidOperationException>(()=>f.Transaction.Commit(Id)); Assert.NotNull(f.Journal);
    }
    // Defect: deleting the journal in this commit action makes a subsequent
    // MSI commit-action failure unable to restore the membership it granted.
    [Fact]
    public void Commit_keeps_evidence_until_later_commit_failures_can_no_longer_roll_back()
    {
        var f=new Fixture(); f.Transaction.Apply(Id,true); f.Transaction.Commit(Id);
        Assert.NotNull(f.Journal);
        f.Transaction.Rollback(Id); // Windows Installer rolls back a later failed commit action.
        Assert.False(f.Member); Assert.False(f.Owned); Assert.Null(f.Journal);
    }
    sealed class Fixture:ICounterReadMembership,ICounterReadState
    {
        public bool Member,Owned,AddRaces,FailAdd,FailOwnedOnce,OtherPending;
        public int Adds,Removes;
        readonly Dictionary<string,CounterReadJournal> journals=new();
        public CounterReadJournal? Journal {get=>journals.GetValueOrDefault(Id);set{if(value is null)journals.Remove(Id);else journals[Id]=value;}}
        public CounterReadTransaction Transaction=>new(this,this);
        public bool Contains()=>Member;
        public bool Add(){Adds++; if(FailAdd)throw new InvalidOperationException("native_add_failed"); Member=true; return !AddRaces;}
        public bool Remove(){Removes++; var before=Member; Member=false; return before;}
        public bool ReadOwned()=>Owned;
        public bool HasOtherJournal(string id)=>OtherPending || journals.Any(x=>x.Key!=id && (!x.Value.Applied || !x.Value.Committed));
        public void WriteOwned(bool owned){if(FailOwnedOnce){FailOwnedOnce=false;throw new InvalidOperationException("state_write_failed");}Owned=owned;}
        static CounterReadJournal Copy(CounterReadJournal j)=>new(){BeforeMember=j.BeforeMember,BeforeOwned=j.BeforeOwned,AfterMember=j.AfterMember,AfterOwned=j.AfterOwned,Applied=j.Applied,Committed=j.Committed};
        public CounterReadJournal ReadJournal(string id)=>journals.TryGetValue(id,out var j)?Copy(j):null!;
        public void WriteJournal(string id,CounterReadJournal journal,bool create){if(create&&journals.ContainsKey(id))throw new InvalidOperationException("journal_exists");journals[id]=Copy(journal);}
        public void DeleteJournal(string id)=>journals.Remove(id);
    }
}

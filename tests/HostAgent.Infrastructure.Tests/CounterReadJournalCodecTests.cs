using HostAgent.Installer;
using Xunit;
namespace HostAgent.Infrastructure.Tests;
public sealed class CounterReadJournalCodecTests
{
    const string Id="g11111111111111111111111111111111",Sid="S-1-5-80-123";
    // Defect: a swapped or malformed persisted rollback flag can remove an
    // operator's membership; the fixed record must preserve each exact flag.
    [Fact]
    public void Exact_record_preserves_attempt_identity_and_all_before_after_flags()
    {
        var j=new CounterReadJournal{AfterMember=true,AfterOwned=true,Applied=true,Committed=true};
        var expected=Id+"|S-1-5-80-123|1|0|0|1|1|1|1";
        Assert.Equal(expected,CounterReadJournalCodec.Encode(Id,Sid,j));
        var actual=CounterReadJournalCodec.Decode(expected,Sid,out var id);
        Assert.Equal(Id,id); Assert.False(actual.BeforeMember); Assert.False(actual.BeforeOwned);
        Assert.True(actual.AfterMember); Assert.True(actual.AfterOwned); Assert.True(actual.Applied); Assert.True(actual.Committed);
    }
    [Theory]
    [InlineData("|S-1-5-80-123|1|0|0|1|1|1|1")]
    [InlineData("g11111111111111111111111111111111|S-1-5-80-999|1|0|0|1|1|1|1")]
    [InlineData("g11111111111111111111111111111111|S-1-5-80-123|2|0|0|1|1|1|1")]
    [InlineData("g11111111111111111111111111111111|S-1-5-80-123|1|0|0|1|1|1|2")]
    [InlineData("g11111111111111111111111111111111|S-1-5-80-123|1|0|0|1|1|0|1")]
    [InlineData("g11111111111111111111111111111111|S-1-5-80-123|1|0|0|0|1|1|1")]
    [InlineData("g11111111111111111111111111111111|S-1-5-80-123|1|0|0|1|1|1|1|extra")]
    public void Invalid_or_inconsistent_rollback_evidence_is_refused(string value)
    {
        Assert.Throws<InvalidOperationException>(()=>CounterReadJournalCodec.Decode(value,Sid,out _));
    }
    [Fact]
    public void Excessive_metadata_is_refused_before_parsing()
    {
        Assert.Throws<InvalidOperationException>(()=>CounterReadJournalCodec.Decode(new string('x',257),Sid,out _));
    }
    [Fact]
    public void Pending_intent_and_preexisting_unowned_release_are_valid_distinct_states()
    {
        var pending=CounterReadJournalCodec.Decode(Id+"|S-1-5-80-123|1|1|0|1|0|0|0",Sid,out _);
        Assert.True(pending.BeforeMember); Assert.False(pending.Applied); Assert.False(pending.Committed);
        var preserved=CounterReadJournalCodec.Decode(Id+"|S-1-5-80-123|1|1|0|1|0|1|1",Sid,out _);
        Assert.True(preserved.BeforeMember); Assert.True(preserved.AfterMember); Assert.False(preserved.AfterOwned);
    }
}

using Dami.Contracts.Privacy;
using Xunit;

namespace Dami.Privacy.Tests;

public sealed class ChannelDisclosurePolicyTests
{
    private static OutboundContent Content(ContentProvenance provenance) =>
        new("chan-1", "some text", provenance, Guid.NewGuid());

    [Fact]
    public void EnsureMayLeave_Should_Let_Steve_See_His_Own_Profile_Data()
    {
        // ADR-0025. The rule that refused this answered "hi there" by citing a decision
        // record at him. Sending Steve his own memory back to Steve is not a disclosure.
        ChannelDisclosurePolicy.EnsureMayLeave(
            Content(ContentProvenance.ProfileDerived), "discord", recipientIsDataSubject: true);
    }

    [Fact]
    public void EnsureMayLeave_Should_Still_Refuse_Profile_Data_Addressed_To_Anyone_Else()
    {
        // The permission is to the person, never to the transport. A shared guild or a
        // public bot gets ADR-0024's behaviour back unchanged.
        var refused = Assert.Throws<EgressRefusedException>(
            () => ChannelDisclosurePolicy.EnsureMayLeave(
                Content(ContentProvenance.ProfileDerived), "discord", recipientIsDataSubject: false));

        Assert.Contains("discord", refused.Message, StringComparison.Ordinal);
        Assert.Contains("ADR-0025", refused.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void EnsureMayLeave_Should_Always_Allow_Operational_Content(bool subject)
    {
        ChannelDisclosurePolicy.EnsureMayLeave(
            Content(ContentProvenance.Operational), "discord", recipientIsDataSubject: subject);
    }

    [Fact]
    public void EnsureMayLeave_Should_Name_The_Trace_So_A_Refusal_Can_Be_Chased()
    {
        var content = Content(ContentProvenance.ProfileDerived);

        var refused = Assert.Throws<EgressRefusedException>(
            () => ChannelDisclosurePolicy.EnsureMayLeave(
                content, "discord", recipientIsDataSubject: false));

        Assert.Contains(content.TraceId.ToString(), refused.Message, StringComparison.Ordinal);
    }

    private static InboundMessage From(string authorId, string text = "hello") =>
        new(authorId, "chan-1", text, DateTimeOffset.UnixEpoch);

    [Fact]
    public void ShouldAnswer_Should_Accept_The_Owner()
    {
        Assert.True(ChannelDisclosurePolicy.ShouldAnswer(From("owner"), "owner", "self"));
    }

    [Fact]
    public void ShouldAnswer_Should_Refuse_Everyone_Else()
    {
        // A bot in a server is addressable by every member. Without this the runtime
        // takes instructions from strangers.
        Assert.False(ChannelDisclosurePolicy.ShouldAnswer(From("someone-else"), "owner", "self"));
    }

    [Fact]
    public void ShouldAnswer_Should_Refuse_Its_Own_Messages()
    {
        // Otherwise every answer is a new question and the loop never ends.
        Assert.False(ChannelDisclosurePolicy.ShouldAnswer(From("self"), "self", "self"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ShouldAnswer_Should_Refuse_An_Empty_Message(string text)
    {
        // An attachment with no caption arrives as an empty body; answering it would
        // spend a turn on nothing.
        Assert.False(ChannelDisclosurePolicy.ShouldAnswer(From("owner", text), "owner", "self"));
    }
}

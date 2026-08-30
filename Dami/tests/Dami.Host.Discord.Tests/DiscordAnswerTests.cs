using Dami.Contracts.Context;
using Dami.Contracts.Models;
using Dami.Contracts.Privacy;
using Dami.Core.Turns;
using Xunit;

namespace Dami.Host.Discord.Tests;

public sealed class DiscordAnswerTests
{
    private static RetrievedItem Item(string content) =>
        new("observation", Guid.NewGuid(), content, DateTimeOffset.UnixEpoch);

    private static TurnResult Result(PrivacyClass privacy, bool withMemory)
    {
        var context = new AssembledContext(
            withMemory ? [Item("he was in Chicago on Tuesday")] : [],
            [],
            42);

        return new TurnResult(
            Guid.NewGuid(),
            "an answer",
            context,
            new ModelRoute(ModelTier.Local, privacy, "because"));
    }

    [Fact]
    public void A_LocalOnly_Route_Should_Be_Profile_Derived()
    {
        // The router already made this decision under D-012. A channel that reached a
        // different conclusion would be a boundary with two answers.
        Assert.Equal(
            ContentProvenance.ProfileDerived,
            DiscordAnswer.ProvenanceOf(Result(PrivacyClass.LocalOnly, withMemory: false)));
    }

    [Fact]
    public void Retrieved_Memory_Should_Be_Profile_Derived_Even_If_The_Route_Says_Otherwise()
    {
        // Belt and braces, and deliberately the conservative reading: if memory entered
        // the prompt then the answer is shaped by it whatever the route claims.
        Assert.Equal(
            ContentProvenance.ProfileDerived,
            DiscordAnswer.ProvenanceOf(Result(PrivacyClass.Egressable, withMemory: true)));
    }

    [Fact]
    public void An_Egressable_Answer_With_No_Retrieved_Memory_Should_Be_Operational()
    {
        // Otherwise the gateway can never say anything and the feature is theatre.
        Assert.Equal(
            ContentProvenance.Operational,
            DiscordAnswer.ProvenanceOf(Result(PrivacyClass.Egressable, withMemory: false)));
    }

    [Fact]
    public void A_Refusal_Should_Itself_Be_Sendable()
    {
        // Silence would be the wrong failure: he asked a question and is owed the reason.
        var trace = Guid.NewGuid();

        var refusal = DiscordAnswer.Refusal("chan-1", trace);

        Assert.Equal(ContentProvenance.Operational, refusal.Provenance);
        Assert.Contains(trace.ToString(), refusal.Text, StringComparison.Ordinal);
        Assert.Contains("ADR-0025", refusal.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_Refusal_Should_Not_Repeat_The_Answer_It_Refused()
    {
        // The obvious bug in a refusal path is quoting what it would not send.
        var refusal = DiscordAnswer.Refusal("chan-1", Guid.NewGuid());

        Assert.DoesNotContain("an answer", refusal.Text, StringComparison.Ordinal);
    }
}

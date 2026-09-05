using Dami.Contracts.Models;
using Dami.Contracts.Privacy;
using Dami.Core.Frontier;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Dami.Core.Tests.Frontier;

/// <summary>The gate decides what leaves the host. It must fail closed, always.</summary>
public sealed class LocalDisclosureGateTests
{
    private readonly IChatClient chatClient = Substitute.For<IChatClient>();
    private readonly IDisclosureLedger ledger = Substitute.For<IDisclosureLedger>();

    [Fact]
    public async Task ClassifyAsync_Should_Pass_An_Item_The_Gate_Cleared()
    {
        this.Says("""[{"n":1,"action":"pass","text":"pgvector uses HNSW","why":"technical"}]""");

        var decided = await this.ClassifyAsync("pgvector uses HNSW");

        Assert.Equal(Disclosure.Pass, decided[0].Disclosure);
    }

    [Fact]
    public async Task ClassifyAsync_Should_Send_The_Rewritten_Text_When_Disguising()
    {
        this.Says(
            """[{"n":1,"action":"disguise","text":"A friend has severe aortic stenosis","why":"identity not needed"}]""");

        var decided = await this.ClassifyAsync("Steve has severe aortic stenosis");

        Assert.Equal("A friend has severe aortic stenosis", decided[0].Sendable);
    }

    [Fact]
    public async Task ClassifyAsync_Should_Send_Nothing_For_A_Withheld_Item()
    {
        this.Says("""[{"n":1,"action":"withhold","text":"","why":"another person's health"}]""");

        var decided = await this.ClassifyAsync("Riza was diagnosed with BPD");

        Assert.Empty(decided[0].Sendable);
    }

    [Fact]
    public async Task ClassifyAsync_Should_Withhold_An_Item_The_Gate_Forgot()
    {
        // The model classified item 1 and silently ignored item 2. Omission must not
        // mean permission — the unmentioned item has to stay home.
        this.Says("""[{"n":1,"action":"pass","text":"harmless","why":"fine"}]""");

        var decided = await this.ClassifyAsync("harmless", "Steve's home address is ...");

        Assert.Equal(Disclosure.Withhold, decided[1].Disclosure);
    }

    [Fact]
    public async Task ClassifyAsync_Should_Withhold_Everything_When_The_Output_Is_Unreadable()
    {
        this.Says("I'm not sure how to classify these, sorry.");

        var decided = await this.ClassifyAsync("something private", "something else");

        Assert.All(decided, item => Assert.Equal(Disclosure.Withhold, item.Disclosure));
    }

    [Fact]
    public async Task ClassifyAsync_Should_Withhold_A_Disguise_That_Carries_No_Rewrite()
    {
        // "Disguise" with empty text would otherwise send nothing while reporting success;
        // treat a rewrite that never arrived as a refusal.
        this.Says("""[{"n":1,"action":"disguise","text":"","why":"forgot to rewrite"}]""");

        var decided = await this.ClassifyAsync("Steve has severe aortic stenosis");

        Assert.Equal(Disclosure.Withhold, decided[0].Disclosure);
    }

    [Fact]
    public async Task ClassifyAsync_Should_Withhold_Everything_When_The_Model_Cannot_Be_Reached()
    {
        // 2026-09-05 13:24:44: the sidecar was restarted under this call; the exception
        // escaped and the whole Discord turn failed. Fail closed, not loud.
        this.chatClient.CompleteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task<string>>(_ => throw new HttpRequestException("response ended prematurely"));

        var decided = await this.ClassifyAsync("one", "two");

        Assert.All(decided, item => Assert.Equal(Disclosure.Withhold, item.Disclosure));
        Assert.All(decided, item => Assert.Equal("gate unavailable", item.Reason));
    }

    [Fact]
    public async Task ClassifyAsync_Should_Not_Call_The_Model_With_No_Context()
    {
        await this.ClassifyAsync();

        await this.chatClient.DidNotReceiveWithAnyArgs().CompleteAsync(default!, default);
    }

    [Fact]
    public async Task ClassifyAsync_Should_Feed_A_Recorded_Correction_Back_As_An_Example()
    {
        var at = new DateTimeOffset(2026, 8, 25, 20, 0, 0, TimeSpan.Zero);
        this.ledger.CorrectionsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(
            [new DisclosureDecision(
                Guid.NewGuid(), Guid.NewGuid(), "earlier question", "Steve's surgeon is Dr Harrison",
                Disclosure.Pass, "Steve's surgeon is Dr Harrison", "public",
                at, new DisclosureCorrection(Disclosure.Withhold, "names of doctors never leave", "steve", at))]);
        string? prompt = null;
        this.chatClient.CompleteAsync(Arg.Do<string>(text => prompt = text), Arg.Any<CancellationToken>())
            .Returns("""[{"n":1,"action":"withhold","text":"","why":"learned"}]""");

        var decided = await this.ClassifyAsync("a later item about a doctor");

        Assert.NotNull(prompt);
        Assert.Contains("Corrections the user has made before", prompt, StringComparison.Ordinal);
        Assert.Contains("the gate chose pass; the user says it should have been withhold because: names of doctors never leave", prompt, StringComparison.Ordinal);
        Assert.Equal(Disclosure.Withhold, Assert.Single(decided).Disclosure);
    }

    [Fact]
    public async Task The_Prompt_Should_Say_The_Name_Is_Known_And_History_Is_This_Chat()
    {
        // 2026-09-04/05: 44 of 67 items withheld, most with "Steve is a personal identifier"
        // — including the conversation history, which is how she lost the thread of a chat
        // about a picture she had just been asked for. The service already knows his name.
        string? prompt = null;
        this.chatClient.CompleteAsync(Arg.Do<string>(text => prompt = text), Arg.Any<CancellationToken>())
            .Returns("""[{"n":1,"action":"pass","text":"Earlier — Steve: where's the image?","why":"chat"}]""");

        await this.ClassifyAsync("Earlier — Steve: where's the image?");

        Assert.NotNull(prompt);
        Assert.Contains("knows his first name, Steve", prompt, StringComparison.Ordinal);
        Assert.Contains("never makes an item identifying", prompt, StringComparison.Ordinal);
        Assert.Contains("Items beginning \"Earlier —\"", prompt, StringComparison.Ordinal);
        Assert.Contains("Prefer this over withhold", prompt, StringComparison.Ordinal);
    }

    private void Says(string reply)
    {
        this.chatClient.CompleteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(reply);
    }

    private async Task<IReadOnlyList<DisclosedItem>> ClassifyAsync(params string[] context)
    {

        var gate = new LocalDisclosureGate(
            this.chatClient, this.ledger, Options.Create(new DisclosureOptions()),
            NullLogger<LocalDisclosureGate>.Instance);
        return await gate.ClassifyAsync("a question", context, CancellationToken.None);
    }
}

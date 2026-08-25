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
    public async Task ClassifyAsync_Should_Not_Call_The_Model_With_No_Context()
    {
        await this.ClassifyAsync();

        await this.chatClient.DidNotReceiveWithAnyArgs().CompleteAsync(default!, default);
    }

    private void Says(string reply)
    {
        this.chatClient.CompleteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(reply);
    }

    private async Task<IReadOnlyList<DisclosedItem>> ClassifyAsync(params string[] context)
    {
        var gate = new LocalDisclosureGate(
            this.chatClient, Options.Create(new DisclosureOptions()),
            NullLogger<LocalDisclosureGate>.Instance);
        return await gate.ClassifyAsync("a question", context, CancellationToken.None);
    }
}

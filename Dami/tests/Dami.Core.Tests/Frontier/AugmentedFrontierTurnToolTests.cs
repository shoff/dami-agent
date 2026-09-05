using Dami.Contracts.Briefs;
using Dami.Contracts.Context;
using Dami.Contracts.Events;
using Dami.Contracts.Models;
using Dami.Contracts.Privacy;
using Dami.Core.Frontier;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Dami.Core.Tests.Frontier;

/// <summary>The bundle reaches the frontier through the augmented turn (ADR-0030).</summary>
public sealed class AugmentedFrontierTurnToolTests
{
    private readonly IFrontierChat frontier = Substitute.For<IFrontierChat>();

    private AugmentedFrontierTurn Subject()
    {
        var contextBuilder = Substitute.For<IContextBuilder>();
        contextBuilder.BuildAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new AssembledContext([], [], 0));
        var identity = Substitute.For<IIdentityProvider>();
        identity.FrontierVoice.Returns("You are Dami.");
        return new AugmentedFrontierTurn(
            contextBuilder,
            Substitute.For<IContextDisclosureGate>(),
            this.frontier,
            identity,
            Substitute.For<IEgressBriefStore>(),
            Substitute.For<IDisclosureLedger>(),
            Substitute.For<IExecutionEventStore>(),
            Options.Create(new AugmentedTurnOptions { Gate = false }),
            TimeProvider.System,
            NullLogger<AugmentedFrontierTurn>.Instance);
    }

    private static async IAsyncEnumerable<string> WordsAsync(params string[] words)
    {
        foreach (var word in words)
        {
            yield return word;
        }

        await Task.CompletedTask;
    }

    [Fact]
    public async Task StreamAsync_Should_Hand_The_Toolbox_To_The_Frontier()
    {
        var toolbox = new FrontierToolbox(
            [new FrontierTool("make_portrait", "draw", System.Text.Json.JsonDocument.Parse("""{"type":"object"}""").RootElement)],
            Substitute.For<IFrontierToolHandler>());
        this.frontier.StreamAsync(
                Arg.Any<FrontierPrompt>(), Arg.Any<IReadOnlyList<FrontierImage>>(), toolbox, Arg.Any<CancellationToken>())
            .Returns(WordsAsync("hi"));

        var stream = await this.Subject().StreamAsync("hello", [], toolbox, CancellationToken.None);
        var fragments = new List<string>();
        await foreach (var fragment in stream.Tokens)
        {
            fragments.Add(fragment);
        }

        Assert.Equal(["hi"], fragments);
    }

    [Fact]
    public async Task StreamAsync_Without_Tools_Should_Offer_The_Empty_Bundle()
    {
        this.frontier.StreamAsync(
                Arg.Any<FrontierPrompt>(), Arg.Any<IReadOnlyList<FrontierImage>>(),
                Arg.Is<FrontierToolbox>(tools => tools.IsEmpty), Arg.Any<CancellationToken>())
            .Returns(WordsAsync("plain"));

        var stream = await this.Subject().StreamAsync("hello", [], CancellationToken.None);
        var fragments = new List<string>();
        await foreach (var fragment in stream.Tokens)
        {
            fragments.Add(fragment);
        }

        Assert.Equal(["plain"], fragments);
    }
}

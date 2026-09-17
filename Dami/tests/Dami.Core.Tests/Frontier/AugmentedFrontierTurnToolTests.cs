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
    private readonly IContextDisclosureGate gate = Substitute.For<IContextDisclosureGate>();
    private readonly IDisclosureLedger ledger = Substitute.For<IDisclosureLedger>();
    private readonly DisclosureMemo memo = new(TimeProvider.System);

    private AugmentedFrontierTurn Subject(bool gated = false)
    {
        var contextBuilder = Substitute.For<IContextBuilder>();
        contextBuilder.BuildAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new AssembledContext([], [], 0));
        var identity = Substitute.For<IIdentityProvider>();
        identity.FrontierVoice.Returns("You are Dami.");
        return new AugmentedFrontierTurn(
            contextBuilder,
            this.gate,
            this.frontier,
            identity,
            Substitute.For<IEgressBriefStore>(),
            this.ledger,
            this.memo,
            Substitute.For<IExecutionEventStore>(),
            Options.Create(new AugmentedTurnOptions { Gate = gated, GateMemoMinutes = 30 }),
            TimeProvider.System,
            NullLogger<AugmentedFrontierTurn>.Instance);
    }

    [Fact]
    public async Task A_Caller_Supplied_Trace_Should_Be_The_Turns_Trace()
    {
        var trace = Guid.NewGuid();
        this.frontier.StreamAsync(Arg.Any<FrontierPrompt>(), Arg.Any<IReadOnlyList<FrontierImage>>(), Arg.Any<FrontierToolbox>(), Arg.Any<CancellationToken>())
            .Returns(WordsAsync("hi"));

        var stream = await this.Subject().StreamAsync("q", [], FrontierToolbox.Empty, trace, CancellationToken.None);
        await foreach (var unused in stream.Tokens)
        {
        }

        _ = this.frontier.Received(1).StreamAsync(
            Arg.Is<FrontierPrompt>(prompt => prompt.TraceId == trace), Arg.Any<IReadOnlyList<FrontierImage>>(), Arg.Any<FrontierToolbox>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task The_Gate_Should_Judge_Only_Lines_It_Has_Not_Seen_Lately()
    {
        // Every turn re-sent the same dozen history lines to the local model. Now a line
        // judged in the last thirty minutes reuses its verdict; only new lines are asked.
        this.gate.ClassifyAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(call => call.ArgAt<IReadOnlyList<string>>(1)
                .Select(line => new DisclosedItem(line, Disclosure.Pass, line, "ok")).ToList());
        this.frontier.StreamAsync(Arg.Any<FrontierPrompt>(), Arg.Any<IReadOnlyList<FrontierImage>>(), Arg.Any<FrontierToolbox>(), Arg.Any<CancellationToken>())
            .Returns(WordsAsync("hi"));
        var subject = this.Subject(gated: true);

        await subject.StreamAsync("q", ["Earlier — Steve: hello", "Earlier — Dami: hi"], CancellationToken.None);
        await subject.StreamAsync("q2", ["Earlier — Steve: hello", "Earlier — Dami: hi", "a new memory"], CancellationToken.None);

        await this.gate.Received(1).ClassifyAsync(
            "q2", Arg.Is<IReadOnlyList<string>>(lines => lines.Count == 1 && lines[0] == "a new memory"), Arg.Any<CancellationToken>());
        await this.ledger.Received(1).RecordAsync(
            Arg.Any<Guid>(), "q2", Arg.Is<IReadOnlyList<DisclosedItem>>(items => items.Count == 1), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
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

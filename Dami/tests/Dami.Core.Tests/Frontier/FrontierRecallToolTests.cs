using Dami.Contracts.Briefs;
using Dami.Contracts.Context;
using Dami.Contracts.Privacy;
using Dami.Core.Frontier;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Dami.Core.Tests.Frontier;

/// <summary>Recall is the first pass again, not a side door (D-012).</summary>
public sealed class FrontierRecallToolTests
{
    private readonly IContextBuilder contextBuilder = Substitute.For<IContextBuilder>();
    private readonly IContextDisclosureGate gate = Substitute.For<IContextDisclosureGate>();
    private readonly IDisclosureLedger ledger = Substitute.For<IDisclosureLedger>();
    private readonly IEgressBriefStore briefs = Substitute.For<IEgressBriefStore>();

    private static RetrievedItem Item(string content) =>
        new("observation", Guid.NewGuid(), content, DateTimeOffset.UnixEpoch);

    private FrontierRecallTool Subject(bool gated = true) => new(
        this.contextBuilder, this.gate, this.ledger, this.briefs,
        Options.Create(new AugmentedTurnOptions { Gate = gated }),
        TimeProvider.System, NullLogger<FrontierRecallTool>.Instance);

    private void Memory(params string[] contents) =>
        this.contextBuilder.BuildAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new AssembledContext(contents.Select(Item).ToList(), [], 10));

    [Fact]
    public void The_Tool_Should_Take_One_Query_String()
    {
        var tool = this.Subject().Tool;

        Assert.Equal("recall", tool.Name);
        Assert.Equal("string", tool.InputSchema.GetProperty("properties").GetProperty("query").GetProperty("type").GetString());
    }

    [Fact]
    public async Task Should_Send_Only_What_The_Gate_Lets_Through()
    {
        this.Memory("squatted 225 for five on Monday", "the surgeon's name and the date");
        this.gate.ClassifyAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(call => call.ArgAt<IReadOnlyList<string>>(1).Select(line =>
                line.Contains("surgeon", StringComparison.Ordinal)
                    ? new DisclosedItem(line, Disclosure.Withhold, string.Empty, "medical")
                    : new DisclosedItem(line, Disclosure.Pass, line, "training")).ToList());

        var result = await this.Subject().RecallAsync(Guid.NewGuid(), "monday lifts", CancellationToken.None);

        Assert.True(result.Success);
        Assert.Contains("225 for five", result.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("surgeon", result.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Should_Record_The_Decision_And_The_Bytes_That_Left()
    {
        this.Memory("squatted 225 for five on Monday");
        var trace = Guid.NewGuid();
        this.gate.ClassifyAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(call => call.ArgAt<IReadOnlyList<string>>(1)
                .Select(line => new DisclosedItem(line, Disclosure.Pass, line, "ok")).ToList());

        await this.Subject().RecallAsync(trace, "monday lifts", CancellationToken.None);

        await this.ledger.Received(1).RecordAsync(
            trace, "recall: monday lifts", Arg.Any<IReadOnlyList<DisclosedItem>>(),
            Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
        await this.briefs.Received(1).CreateAsync(
            Arg.Is<EgressBrief>(brief =>
                brief.TraceId == trace
                && brief.Brief.Contains("225 for five", StringComparison.Ordinal)
                && brief.BriefSha256 == BriefExecutor.HashOf(brief.Brief)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_Say_So_When_Everything_Is_Withheld_And_Send_No_Brief()
    {
        this.Memory("the surgeon's name and the date");
        this.gate.ClassifyAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(call => call.ArgAt<IReadOnlyList<string>>(1)
                .Select(line => new DisclosedItem(line, Disclosure.Withhold, string.Empty, "medical")).ToList());

        var result = await this.Subject().RecallAsync(Guid.NewGuid(), "surgery", CancellationToken.None);

        Assert.True(result.Success);
        Assert.Contains("withheld", result.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("surgeon", result.Text, StringComparison.Ordinal);
        await this.briefs.DidNotReceive().CreateAsync(Arg.Any<EgressBrief>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_Say_So_When_Memory_Has_Nothing()
    {
        this.Memory();

        var result = await this.Subject().RecallAsync(Guid.NewGuid(), "unicorns", CancellationToken.None);

        Assert.True(result.Success);
        Assert.Contains("Nothing in memory", result.Text, StringComparison.Ordinal);
        await this.gate.DidNotReceive().ClassifyAsync(
            Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Gate_Off_Should_Send_Everything_And_Still_Record_The_Brief()
    {
        this.Memory("a", "b");

        var result = await this.Subject(gated: false).RecallAsync(Guid.NewGuid(), "ab", CancellationToken.None);

        Assert.Equal("- a\n- b", result.Text);
        await this.gate.DidNotReceive().ClassifyAsync(
            Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>());
        await this.briefs.Received(1).CreateAsync(Arg.Any<EgressBrief>(), Arg.Any<CancellationToken>());
    }
}

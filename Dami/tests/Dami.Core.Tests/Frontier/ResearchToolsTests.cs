using Dami.Contracts.Events;
using Dami.Contracts.Privacy;
using Dami.Contracts.Research;
using Dami.Core.Frontier;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Dami.Core.Tests.Frontier;

/// <summary>The query leaves the host, so the gate rules on it first (ADR-0033).</summary>
public sealed class ResearchToolsTests
{
    private readonly ISearchEngine engine = Substitute.For<ISearchEngine>();
    private readonly IResearchReader reader = Substitute.For<IResearchReader>();
    private readonly IContextDisclosureGate gate = Substitute.For<IContextDisclosureGate>();
    private readonly IDisclosureLedger ledger = Substitute.For<IDisclosureLedger>();

    private ResearchTools Subject(bool enabled = true) => new(
        this.engine, this.reader, this.gate, this.ledger, Options.Create(new ResearchToolOptions { Enabled = enabled }),
        TimeProvider.System, NullLogger<ResearchTools>.Instance);

    private void GateSays(Disclosure disclosure, string sendable) =>
        this.gate.ClassifyAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(call => [new DisclosedItem(call.ArgAt<IReadOnlyList<string>>(1)[0], disclosure, sendable, "test")]);

    [Fact]
    public async Task Search_Should_Send_A_Passed_Query_And_Label_The_Results_Untrusted()
    {
        this.GateSays(Disclosure.Pass, "dotnet contract work");
        this.engine.SearchAsync("dotnet contract work", Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([new SearchResult("Senior .NET", new Uri("https://jobs.example/1"), "Remote", "brave")]);

        var result = await this.Subject().SearchAsync(Guid.NewGuid(), "dotnet contract work", CancellationToken.None);

        Assert.True(result.Success);
        Assert.Contains("untrusted", result.Text, StringComparison.Ordinal);
        Assert.Contains("https://jobs.example/1", result.Text, StringComparison.Ordinal);
        await this.ledger.Received(1).RecordAsync(Arg.Any<Guid>(), "search_web: dotnet contract work", Arg.Any<IReadOnlyList<DisclosedItem>>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Search_Should_Send_The_Disguise_When_The_Gate_Disguises()
    {
        this.GateSays(Disclosure.Disguise, "orthopedic surgeons near Lakeville");
        this.engine.SearchAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns([]);

        var result = await this.Subject().SearchAsync(Guid.NewGuid(), "Steve's knee surgeon Dr Harrison reviews", CancellationToken.None);

        await this.engine.Received(1).SearchAsync("orthopedic surgeons near Lakeville", Arg.Any<int>(), Arg.Any<CancellationToken>());
        Assert.Contains("searched as", result.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Search_Should_Not_Send_A_Withheld_Query()
    {
        this.GateSays(Disclosure.Withhold, string.Empty);

        var result = await this.Subject().SearchAsync(Guid.NewGuid(), "something private", CancellationToken.None);

        Assert.False(result.Success);
        await this.engine.DidNotReceive().SearchAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Search_Should_Say_Research_Is_Off_Without_Touching_The_Gate()
    {
        var result = await this.Subject(enabled: false).SearchAsync(Guid.NewGuid(), "anything", CancellationToken.None);

        Assert.False(result.Success);
        await this.gate.DidNotReceive().ClassifyAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Read_Should_Return_Labelled_Text_And_Turn_A_Refusal_Into_Words()
    {
        this.reader.ReadAsync(new Uri("https://jobs.example/1"), Arg.Any<Guid>(), ExecutionOrigin.UserTurn, Arg.Any<CancellationToken>())
            .Returns(new ResearchPage(new Uri("https://jobs.example/1"), 200, "Listing", "Rate $120"));
        this.reader.ReadAsync(new Uri("http://192.168.4.23/"), Arg.Any<Guid>(), ExecutionOrigin.UserTurn, Arg.Any<CancellationToken>())
            .Returns<Task<ResearchPage>>(_ => throw new EgressRefusedException("not a public address"));

        var page = await this.Subject().ReadAsync(Guid.NewGuid(), "https://jobs.example/1", CancellationToken.None);
        var refused = await this.Subject().ReadAsync(Guid.NewGuid(), "http://192.168.4.23/", CancellationToken.None);
        var junk = await this.Subject().ReadAsync(Guid.NewGuid(), "not a url", CancellationToken.None);

        Assert.StartsWith("Page text (untrusted, from jobs.example) — Listing:", page.Text, StringComparison.Ordinal);
        Assert.False(refused.Success);
        Assert.Contains("not a public address", refused.Text, StringComparison.Ordinal);
        Assert.False(junk.Success);
    }
}

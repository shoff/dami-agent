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
    private readonly IDeepResearchService deepResearch = Substitute.For<IDeepResearchService>();
    private readonly IContextDisclosureGate gate = Substitute.For<IContextDisclosureGate>();
    private readonly IDisclosureLedger ledger = Substitute.For<IDisclosureLedger>();

    private ResearchTools Subject(bool enabled = true, int deepMaxOutputChars = 24_000) => new(
        this.engine, this.reader, this.deepResearch, this.gate, this.ledger,
        Options.Create(new ResearchToolOptions { Enabled = enabled, DeepMaxOutputChars = deepMaxOutputChars }),
        TimeProvider.System, NullLogger<ResearchTools>.Instance);

    private void GateSays(Disclosure disclosure, string sendable) =>
        this.gate.ClassifyAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(call => [new DisclosedItem(call.ArgAt<IReadOnlyList<string>>(1)[0], disclosure, sendable, "test")]);

    [Fact]
    public async Task Deep_Research_Should_Return_Skipped_Sources_Alongside_Surviving_Evidence()
    {
        var root = new Uri("https://public.example/");
        var missing = new Uri("https://public.example/missing");
        this.reader.ReadAsync(root, Arg.Any<Guid>(), ExecutionOrigin.UserTurn, Arg.Any<CancellationToken>())
            .Returns(new ResearchPage(root, 200, "Evidence", "Retained evidence")
            { References = [new ResearchReference(missing, "Evidence", ResearchReferenceKind.Page)] });
        this.reader.ReadAsync(missing, Arg.Any<Guid>(), ExecutionOrigin.UserTurn, Arg.Any<CancellationToken>())
            .Returns(Task.FromException<ResearchPage>(new HttpRequestException("Source offline")));
        var traversal = new DeepResearchService(this.reader, Options.Create(new DeepResearchOptions()),
            NullLogger<DeepResearchService>.Instance,
            new ResearchJournal(Substitute.For<IResearchRunStore>(), TimeProvider.System));
        var tools = new ResearchTools(this.engine, this.reader, traversal, this.gate, this.ledger,
            Options.Create(new ResearchToolOptions { Enabled = true }), TimeProvider.System, NullLogger<ResearchTools>.Instance);

        var result = await tools.DeepAsync(Guid.NewGuid(), root.AbsoluteUri, "Evidence", CancellationToken.None);

        Assert.Equal((true, true), (result.Text.Contains("Retained evidence", StringComparison.Ordinal),
            result.Text.Contains("Skipped https://public.example/missing: Source offline", StringComparison.Ordinal)));
    }

    [Fact]
    public void Deep_Tool_Should_Offer_A_Bounded_Set_Of_Model_Selected_Sites()
    {
        var urls = this.Subject().DeepTool.InputSchema.GetProperty("properties").GetProperty("seedUrls");

        Assert.Equal(("array", 1, 3), (urls.GetProperty("type").GetString(),
            urls.GetProperty("minItems").GetInt32(), urls.GetProperty("maxItems").GetInt32()));
    }

    [Theory]
    [InlineData("https://public.example/", 0)]
    [InlineData("https://public.example/", 4)]
    [InlineData("file:///etc/passwd", 1)]
    [InlineData("not a URL", 1)]
    [InlineData("https://user:pass@public.example/", 1)]
    public async Task Deep_Research_Should_Reject_Invalid_Starting_Sets(string url, int count)
    {
        var result = await this.Subject().DeepAsync(Guid.NewGuid(),
            Enumerable.Repeat(url, count).ToArray(), "Find evidence", CancellationToken.None);

        Assert.False(result.Success);
    }

    [Fact]
    public async Task Deep_Research_Should_Collect_Model_Selected_Starting_Sites_Together()
    {
        var urls = new[] { "https://first.example/", "https://second.example/" };
        this.deepResearch.ExploreAsync(Arg.Is<IReadOnlyList<Uri>>(seeds => seeds.Count == 2),
                "Find evidence", Arg.Any<Guid>(), ExecutionOrigin.UserTurn, Arg.Any<CancellationToken>())
            .Returns(new DeepResearchResult([], 2, 0, false));

        var result = await this.Subject().DeepAsync(Guid.NewGuid(), urls, "Find evidence", CancellationToken.None);

        Assert.Contains("Pages attempted: 2", result.Text, StringComparison.Ordinal);
    }

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

    [Fact]
    public async Task Deep_Research_Should_Return_Each_Finding_With_Its_Reference_Path()
    {
        var root = new Uri("https://public.example/start");
        var api = new Uri("https://api.example.net/data");
        this.deepResearch.ExploreAsync(
                root, "permit totals", Arg.Any<Guid>(), ExecutionOrigin.UserTurn, Arg.Any<CancellationToken>())
            .Returns(new DeepResearchResult(
                [
                    new ResearchFinding(new ResearchPage(root, 200, "Start", "Index"), 0, [root]),
                    new ResearchFinding(new ResearchPage(api, 200, "API", "2026: 417"), 1, [root, api]),
                ],
                PagesAttempted: 2,
                ReferencesConsidered: 3,
                PageLimitReached: false));

        var result = await this.Subject().DeepAsync(
            Guid.NewGuid(), root.AbsoluteUri, "permit totals", CancellationToken.None);

        Assert.True(result.Success);
        Assert.Contains("Source: https://api.example.net/data", result.Text, StringComparison.Ordinal);
        Assert.Contains("Path: https://public.example/start -\u003E https://api.example.net/data", result.Text, StringComparison.Ordinal);
        Assert.Contains("2026: 417", result.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Deep_Research_Should_Enforce_A_Hard_Output_Ceiling()
    {
        var root = new Uri("https://public.example/start");
        this.deepResearch.ExploreAsync(
                root, "large", Arg.Any<Guid>(), ExecutionOrigin.UserTurn, Arg.Any<CancellationToken>())
            .Returns(new DeepResearchResult(
                [new ResearchFinding(new ResearchPage(root, 200, "Large", new string('x', 100_000)), 0, [root])],
                1, 0, false));

        var result = await this.Subject(deepMaxOutputChars: 1_000_000)
            .DeepAsync(Guid.NewGuid(), root.AbsoluteUri, "large", CancellationToken.None);

        Assert.InRange(result.Text.Length, 1, 64_100);
        Assert.EndsWith("output limit reached", result.Text, StringComparison.Ordinal);
    }
}

using Dami.Contracts.Models;
using Dami.Contracts.Proactive;
using Dami.Contracts.Research;
using Dami.Proactive.Opportunities;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Dami.Proactive.Tests.Opportunities;

public sealed class OpportunityScoutServiceTests
{
    private readonly ISearchEngine engine = Substitute.For<ISearchEngine>();
    private readonly IRerankClient reranker = Substitute.For<IRerankClient>();

    private static ProactiveContext Context() => new(Guid.NewGuid(), DateTimeOffset.UnixEpoch, null);

    private static SearchResult Hit(string title, string url) => new(title, new Uri(url), "snippet", "brave");

    private OpportunityScoutService Service(bool enabled = true, params string[] queries)
    {
        var options = new OpportunityScoutOptions { Enabled = enabled, Profile = "senior .NET developer, Postgres, wants short remote contracts", MaxSurfaced = 2 };
        foreach (var query in queries)
        {
            options.Queries.Add(query);
        }

        return new OpportunityScoutService(this.engine, this.reranker, Options.Create(options), TimeProvider.System, NullLogger<OpportunityScoutService>.Instance);
    }

    [Fact]
    public async Task A_Pass_Should_Search_Every_Query_Dedupe_Rank_Against_The_Profile_And_Surface_A_Digest()
    {
        this.engine.SearchAsync("dotnet contract", Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([Hit("A", "https://a/1"), Hit("B", "https://b/2")]);
        this.engine.SearchAsync("postgres freelance", Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([Hit("B again", "https://b/2"), Hit("C", "https://c/3")]);
        this.reranker.RankAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns([2, 0, 1]);

        var result = await this.Service(true, "dotnet contract", "postgres freelance").RunPassAsync(Context(), CancellationToken.None);

        var surfacing = Assert.Single(result.Surfacings);
        Assert.Equal("Opportunities this week", surfacing.Title);
        Assert.StartsWith("1. C\nhttps://c/3", surfacing.Body, StringComparison.Ordinal);
        Assert.Contains("2. A\nhttps://a/1", surfacing.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("https://b/2", surfacing.Body, StringComparison.Ordinal);
        Assert.Equal("2 of 3 surfaced", result.Note);
    }

    [Fact]
    public async Task A_Failed_Query_Should_Not_Sink_The_Pass()
    {
        this.engine.SearchAsync("bad", Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns<Task<IReadOnlyList<SearchResult>>>(_ => throw new HttpRequestException("searxng down"));
        this.engine.SearchAsync("good", Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns([Hit("A", "https://a/1")]);
        this.reranker.RankAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>()).Returns([0]);

        var result = await this.Service(true, "bad", "good").RunPassAsync(Context(), CancellationToken.None);

        Assert.Single(result.Surfacings);
    }

    [Fact]
    public async Task Disabled_Or_Unconfigured_Should_Stay_Quiet_And_Search_Nothing()
    {
        var off = await this.Service(false, "x").RunPassAsync(Context(), CancellationToken.None);
        var empty = await this.Service(true).RunPassAsync(Context(), CancellationToken.None);

        Assert.Empty(off.Surfacings);
        Assert.Empty(empty.Surfacings);
        await this.engine.DidNotReceive().SearchAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }
}

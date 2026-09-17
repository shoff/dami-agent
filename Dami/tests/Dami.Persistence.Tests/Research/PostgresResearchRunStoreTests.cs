using Dami.Contracts.Research;
using Dami.Persistence.Research;
using Microsoft.Extensions.Options;
using Xunit;

namespace Dami.Persistence.Tests.Research;

[Collection(DatabaseCollection.NAME)]
public sealed class PostgresResearchRunStoreTests
{
    private readonly DatabaseFixture fixture;

    public PostgresResearchRunStoreTests(DatabaseFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        this.fixture = fixture;
    }

    [Fact]
    public async Task A_Rerun_Should_Preserve_The_Earlier_Report_And_Its_Own_Source_Selection()
    {
        await this.fixture.ResetAsync();
        var seed = new Uri("https://public.example/");
        var original = new ResearchRun(Guid.NewGuid(), Guid.NewGuid(), "Original topic", seed,
            DateTimeOffset.Parse("2026-09-11T12:00:00Z"))
        { Answer = "Earlier answer", Status = ResearchRunStatus.Completed };
        var rerun = original with
        {
            RunId = Guid.NewGuid(),
            TraceId = Guid.NewGuid(),
            Question = "Revised topic",
            StartedAt = original.StartedAt.AddDays(1),
            Answer = "Revised answer",
            AutomaticSources = true,
            StartingUrls = [seed, new Uri("https://second.example/")]
        };
        await this.Store().SaveAsync(original, CancellationToken.None);
        await this.Store().SaveAsync(rerun, CancellationToken.None);
        var earlier = await this.Store().GetAsync(original.RunId, CancellationToken.None);
        var latest = await this.Store().GetAsync(rerun.RunId, CancellationToken.None);
        var history = await this.Store().ListAsync(100, CancellationToken.None);

        Assert.Equal(("Original topic", "Earlier answer", "Revised topic", true, "https://second.example/", 2),
            (earlier!.Question, earlier.Answer, latest!.Question, latest.AutomaticSources,
                latest.StartingUrls[1].AbsoluteUri, history.Count));
    }

    [Fact]
    public async Task Save_Should_Preserve_Source_Text_And_Provenance_Across_Store_Instances()
    {
        await this.fixture.ResetAsync();
        var seed = new Uri("https://public.example/start");
        var source = new Uri("https://data.example/report");
        var run = new ResearchRun(Guid.NewGuid(), Guid.NewGuid(), "Find permit data", seed, DateTimeOffset.Parse("2026-09-11T12:00:00Z"))
        {
            Findings = [new ResearchFinding(new ResearchPage(source, 200, "Report", "Original source text"), 1, [seed, source])],
        };
        await this.Store().SaveAsync(run, CancellationToken.None);
        var restored = await this.Store().GetAsync(run.RunId, CancellationToken.None);

        Assert.Equal((run.Question, "Original source text", source),
            (restored!.Question, restored.Findings[0].Page.Text, restored.Findings[0].Path[1]));
    }

    private PostgresResearchRunStore Store() => new(this.fixture.DataSource,
        Options.Create(new PostgresOptions { SchemaName = DatabaseFixture.SCHEMA }));

    [Fact]
    public async Task History_Should_Return_Newest_Summaries_And_Resolve_The_Trace()
    {
        await this.fixture.ResetAsync();
        var first = new ResearchRun(Guid.NewGuid(), Guid.NewGuid(), "First question",
            new Uri("https://public.example/"), DateTimeOffset.Parse("2026-09-11T12:00:00Z"));
        var latest = first with
        {
            RunId = Guid.NewGuid(),
            TraceId = Guid.NewGuid(),
            Question = "Latest question",
            StartedAt = first.StartedAt.AddMinutes(1),
            Findings = [new ResearchFinding(new ResearchPage(first.Seed, 200, "Data", "Stored body"), 0, [first.Seed])]
        };
        var store = this.Store();
        await store.SaveAsync(first, CancellationToken.None);
        await store.SaveAsync(latest, CancellationToken.None);
        var history = await store.ListAsync(1, CancellationToken.None);
        Assert.Equal((latest.RunId, "Latest question", 1),
            (Assert.Single(history).RunId, history[0].Question, history[0].SourceCount));
        Assert.Equal(latest.RunId, (await store.FindByTraceAsync(latest.TraceId, CancellationToken.None))!.RunId);
        Assert.Null(await store.FindByTraceAsync(Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task Save_Should_Ignore_Replayed_Revisions_And_Atomically_Record_Progress_As_Runtime_Role()
    {
        await this.fixture.ResetAsync();
        await using var runtime = DatabaseFixture.CreateRuntimeDataSource();
        var store = new PostgresResearchRunStore(runtime,
            Options.Create(new PostgresOptions { SchemaName = DatabaseFixture.SCHEMA }));
        var started = new ResearchRun(Guid.NewGuid(), Guid.NewGuid(), "Find data",
            new Uri("https://public.example/"), DateTimeOffset.Parse("2026-09-11T12:00:00Z"));
        var finished = started with { Revision = 2, Status = ResearchRunStatus.Completed, PagesAttempted = 1 };
        await store.SaveAsync(started, CancellationToken.None);
        await store.SaveAsync(finished, CancellationToken.None);
        await store.SaveAsync(started, CancellationToken.None);
        await store.SaveAsync(finished, CancellationToken.None);
        var restored = await store.GetAsync(started.RunId, CancellationToken.None);
        Assert.Equal(ResearchRunStatus.Completed, restored!.Status);
        await using var command = this.fixture.DataSource.CreateCommand(
            $"select count(*) from {DatabaseFixture.SCHEMA}.execution_events where payload_reference = @reference");
        command.Parameters.AddWithValue("reference", $"research:{started.RunId:D}");
        Assert.Equal(2L, await command.ExecuteScalarAsync());
    }
}

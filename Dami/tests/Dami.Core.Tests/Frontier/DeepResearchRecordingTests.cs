using Dami.Contracts.Events;
using Dami.Contracts.Research;
using Dami.Core.Frontier;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Dami.Core.Tests.Frontier;

public sealed class DeepResearchRecordingTests
{
    [Theory]
    [InlineData("timeout")]
    [InlineData("http")]
    public async Task A_Failed_Starting_Site_Should_Not_End_The_Other_Starting_Trees(string failure)
    {
        var failed = new Uri("https://offline.example/");
        var live = new Uri("https://live.example/");
        ResearchRun? saved = null;
        var store = Substitute.For<IResearchRunStore>();
        store.SaveAsync(Arg.Any<ResearchRun>(), Arg.Any<CancellationToken>())
            .Returns(call => { saved = call.Arg<ResearchRun>(); return Task.CompletedTask; });
        var reader = Substitute.For<IResearchReader>();
        reader.ReadAsync(failed, Arg.Any<Guid>(), ExecutionOrigin.UserTurn, Arg.Any<CancellationToken>())
            .Returns(_ => failure == "timeout" ? Task.FromException<ResearchPage>(new TimeoutException("Source timed out"))
                : Task.FromResult(new ResearchPage(failed, 500, "Server error", "Unavailable")));
        reader.ReadAsync(live, Arg.Any<Guid>(), ExecutionOrigin.UserTurn, Arg.Any<CancellationToken>())
            .Returns(new ResearchPage(live, 200, "Live source", "Retained evidence"));
        var service = new DeepResearchService(reader, Options.Create(new DeepResearchOptions()),
            NullLogger<DeepResearchService>.Instance, new ResearchJournal(store, TimeProvider.System));

        var result = await service.ExploreAsync(new[] { failed, live }, "Evidence", Guid.NewGuid(), ExecutionOrigin.UserTurn, CancellationToken.None);

        Assert.Equal((2, live, 1, ResearchRunStatus.Completed),
            (result.PagesAttempted, result.Findings.Single().Page.Url, saved!.Issues.Count, saved.Status));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Explore_Should_Retain_How_Starting_Sites_Were_Chosen(bool automatic)
    {
        ResearchRun? saved = null;
        var store = Substitute.For<IResearchRunStore>();
        store.SaveAsync(Arg.Any<ResearchRun>(), Arg.Any<CancellationToken>())
            .Returns(call => { saved = call.Arg<ResearchRun>(); return Task.CompletedTask; });
        var seed = new Uri("https://public.example/");
        var service = new DeepResearchService(Reader(seed, "success"), Options.Create(new DeepResearchOptions()),
            NullLogger<DeepResearchService>.Instance, new ResearchJournal(store, TimeProvider.System));

        if (automatic)
        {
            await service.ExploreAsync(new[] { seed }, "Evidence", Guid.NewGuid(), ExecutionOrigin.UserTurn, CancellationToken.None);
        }
        else
        {
            await service.ExploreAsync(seed, "Evidence", Guid.NewGuid(), ExecutionOrigin.UserTurn, CancellationToken.None);
        }

        Assert.Equal(automatic, saved!.AutomaticSources);
    }

    [Fact]
    public async Task Explore_Should_Keep_Multiple_Starting_Sites_In_One_Archive()
    {
        var first = new Uri("https://first.example/");
        var second = new Uri("https://second.example/");
        var saved = new List<ResearchRun>();
        var store = Substitute.For<IResearchRunStore>();
        store.SaveAsync(Arg.Any<ResearchRun>(), Arg.Any<CancellationToken>())
            .Returns(call => { saved.Add(call.Arg<ResearchRun>()); return Task.CompletedTask; });
        var reader = Substitute.For<IResearchReader>();
        reader.ReadAsync(Arg.Any<Uri>(), Arg.Any<Guid>(), Arg.Any<ExecutionOrigin>(), Arg.Any<CancellationToken>())
            .Returns(call => new ResearchPage(call.Arg<Uri>(), 200, "Source", "Evidence"));
        var service = new DeepResearchService(reader, Options.Create(new DeepResearchOptions()),
            NullLogger<DeepResearchService>.Instance, new ResearchJournal(store, TimeProvider.System));

        var result = await service.ExploreAsync(
            new[] { first, second }, "Find data", Guid.NewGuid(), ExecutionOrigin.UserTurn, CancellationToken.None);

        Assert.Equal(new[] { first, second, first, second },
            result.Findings.Select(finding => finding.Page.Url).Concat(saved[^1].StartingUrls));
    }

    [Theory]
    [InlineData("success", ResearchRunStatus.Completed, 1, 0)]
    [InlineData("skip", ResearchRunStatus.Completed, 0, 1)]
    [InlineData("failure", ResearchRunStatus.Failed, 0, 0)]
    [InlineData("cancel", ResearchRunStatus.Cancelled, 0, 0)]
    public async Task Explore_Should_Save_Progress_And_The_Observed_Outcome(
        string outcome, ResearchRunStatus status, int sources, int issues)
    {
        var saved = new List<ResearchRun>();
        var store = Substitute.For<IResearchRunStore>();
        store.SaveAsync(Arg.Any<ResearchRun>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            Assert.False(call.ArgAt<CancellationToken>(1).IsCancellationRequested);
            saved.Add(call.Arg<ResearchRun>());
            return Task.CompletedTask;
        });
        var seed = new Uri("https://public.example/data");
        var service = new DeepResearchService(Reader(seed, outcome), Options.Create(new DeepResearchOptions()),
            NullLogger<DeepResearchService>.Instance, new ResearchJournal(store, TimeProvider.System));
        var error = await Record.ExceptionAsync(() => service.ExploreAsync(
            seed, "Find data", Guid.NewGuid(), ExecutionOrigin.UserTurn, CancellationToken.None));
        Assert.Equal(outcome is "failure" or "cancel", error is not null);
        Assert.Equal(ResearchRunStatus.Running, saved[0].Status);
        Assert.Contains(saved, run => run.CurrentUrl == seed && run.PagesAttempted == 1);
        Assert.Equal((status, sources, issues, 1),
            (saved[^1].Status, saved[^1].Findings.Count, saved[^1].Issues.Count, saved[^1].PagesAttempted));
        Assert.Equal(Enumerable.Range(1, saved.Count), saved.Select(run => run.Revision));
        Assert.All(saved, run => Assert.Equal(saved[0].RunId, run.RunId));
    }

    private static IResearchReader Reader(Uri seed, string outcome)
    {
        var reader = Substitute.For<IResearchReader>();
        reader.ReadAsync(seed, Arg.Any<Guid>(), Arg.Any<ExecutionOrigin>(), Arg.Any<CancellationToken>())
            .Returns(_ => outcome switch
            {
                "skip" => Task.FromException<ResearchPage>(new HttpRequestException("Source unavailable")),
                "failure" => Task.FromException<ResearchPage>(new InvalidOperationException("Reader failed")),
                "cancel" => Task.FromException<ResearchPage>(new OperationCanceledException()),
                _ => Task.FromResult(new ResearchPage(seed, 200, "Data", "Retained source text")),
            });
        return reader;
    }
}

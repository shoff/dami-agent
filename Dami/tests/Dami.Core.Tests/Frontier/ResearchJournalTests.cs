using Dami.Contracts.Research;
using Dami.Core.Frontier;
using NSubstitute;
using Xunit;

namespace Dami.Core.Tests.Frontier;

public sealed class ResearchJournalTests
{
    [Theory]
    [InlineData(false, ResearchAnswerStatus.Complete)]
    [InlineData(true, ResearchAnswerStatus.Partial)]
    public async Task Capture_Should_Keep_The_Streamed_Answer_With_An_Honest_Completion_State(
        bool interrupt, ResearchAnswerStatus status)
    {
        var run = new ResearchRun(Guid.NewGuid(), Guid.NewGuid(), "Find data",
            new Uri("https://public.example/"), DateTimeOffset.Parse("2026-09-11T12:00:00Z"))
            { Status = ResearchRunStatus.Completed };
        var store = Substitute.For<IResearchRunStore>();
        store.FindByTraceAsync(run.TraceId, Arg.Any<CancellationToken>()).Returns(run);
        var journal = new ResearchJournal(store, TimeProvider.System);
        var fragments = new List<string>();
        var error = await Record.ExceptionAsync(async () =>
        {
            await foreach (var fragment in journal.CaptureAsync(run.TraceId, StreamAsync(interrupt), CancellationToken.None))
            {
                fragments.Add(fragment);
            }
        });
        Assert.Equal(interrupt, error is not null);
        Assert.Equal("Evidence from the source.", string.Concat(fragments));
        await store.Received(1).SaveAsync(Arg.Is<ResearchRun>(saved => saved.Answer == string.Concat(fragments)
            && saved.AnswerStatus == status && saved.Revision == 2), Arg.Any<CancellationToken>());
    }

    private static async IAsyncEnumerable<string> StreamAsync(bool interrupt)
    {
        yield return "Evidence from ";
        yield return "the source.";
        await Task.CompletedTask;
        if (interrupt)
        {
            throw new OperationCanceledException();
        }
    }

    [Fact]
    public async Task Capture_Should_Bound_The_Archive_Without_Truncating_The_Chat_Stream()
    {
        var run = new ResearchRun(Guid.NewGuid(), Guid.NewGuid(), "Question", new Uri("https://public.example/"),
            DateTimeOffset.Parse("2026-09-11T12:00:00Z"));
        var store = Substitute.For<IResearchRunStore>();
        store.FindByTraceAsync(run.TraceId, Arg.Any<CancellationToken>()).Returns(run);
        var journal = new ResearchJournal(store, TimeProvider.System);
        var length = 0;
        await foreach (var fragment in journal.CaptureAsync(run.TraceId, LongStreamAsync(), CancellationToken.None))
        {
            length += fragment.Length;
        }

        Assert.Equal(80_000, length);
        await store.Received(1).SaveAsync(Arg.Is<ResearchRun>(saved => saved.Answer!.Length == 64_000
            && saved.AnswerStatus == ResearchAnswerStatus.Partial && saved.AnswerTruncated), Arg.Any<CancellationToken>());
    }

    private static async IAsyncEnumerable<string> LongStreamAsync()
    {
        yield return new string('x', 40_000);
        yield return new string('y', 40_000);
        await Task.CompletedTask;
    }

    [Theory]
    [InlineData(ResearchRunStatus.Running, ResearchRunStatus.Interrupted, 1)]
    [InlineData(ResearchRunStatus.Completed, ResearchRunStatus.Completed, 0)]
    [InlineData(ResearchRunStatus.Cancelled, ResearchRunStatus.Cancelled, 0)]
    public async Task Read_Should_Recover_Only_Unfinished_Runs_From_An_Earlier_Runtime(
        ResearchRunStatus before, ResearchRunStatus after, int writes)
    {
        var run = new ResearchRun(Guid.NewGuid(), Guid.NewGuid(), "Question", new Uri("https://public.example/"),
            DateTimeOffset.Parse("2026-09-11T12:00:00Z"))
            { Status = before, OwnerId = Guid.NewGuid(), PagesAttempted = 3 };
        var store = Substitute.For<IResearchRunStore>();
        store.GetAsync(run.RunId, Arg.Any<CancellationToken>()).Returns(run);
        var restored = await new ResearchJournal(store, TimeProvider.System).GetAsync(run.RunId, CancellationToken.None);
        Assert.Equal((after, 3), (restored!.Status, restored.PagesAttempted));
        await store.Received(writes).SaveAsync(Arg.Is<ResearchRun>(saved => saved.Status == after
            && saved.Revision == 2 && saved.Error != null), Arg.Any<CancellationToken>());
    }
}

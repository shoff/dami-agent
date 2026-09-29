using Dami.Contracts.Scheduling;
using Dami.Core.Scheduling;
using Xunit;

namespace Dami.Core.Tests.Scheduling;

/// <summary>
/// A job remembers what it said and, when asked to, says nothing when nothing changed —
/// the two fixes for the flaky, repetitive briefings users of every surveyed agent abandoned.
/// </summary>
public sealed class JobPromptTests
{
    private static readonly DateTimeOffset now = new(2026, 9, 29, 12, 0, 0, TimeSpan.Zero);

    private static ScheduledJob Job(bool onlyWhenNew = false) =>
        new(Guid.NewGuid(), "hn digest", "d", ScheduledJobKind.Prompt, "summarise the top of HN for me", [],
            "0 8 * * *", "UTC", ScheduledJobStatus.Active, now, now, null, null, null, null, onlyWhenNew);

    private static ScheduledJobRun Run(int daysAgo, string output) =>
        new(Guid.NewGuid(), Guid.NewGuid(), now.AddDays(-daysAgo), output, true);

    [Fact]
    public void A_First_Run_Should_Be_The_Request_Alone()
    {
        Assert.Equal("[scheduled job 'hn digest'] summarise the top of HN for me", JobPrompt.Compose(Job(onlyWhenNew: true), []));
    }

    [Fact]
    public void Later_Runs_Should_See_What_They_Said_And_Be_Told_Not_To_Repeat_It()
    {
        var prompt = JobPrompt.Compose(Job(), [Run(1, "Rust 2.0 released"), Run(2, "a new Postgres CVE")]);

        Assert.Contains("- 2026-09-28: Rust 2.0 released\n- 2026-09-27: a new Postgres CVE", prompt, StringComparison.Ordinal);
        Assert.Contains("do not repeat", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain(JobPrompt.NOTHING_NEW, prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void A_Change_Only_Job_Should_Be_Told_How_To_Stay_Silent()
    {
        var prompt = JobPrompt.Compose(Job(onlyWhenNew: true), [Run(1, "Rust 2.0 released")]);

        Assert.EndsWith("If nothing is new since then, reply with exactly: NOTHING NEW", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Earlier_Outputs_Should_Be_Bounded()
    {
        var prompt = JobPrompt.Compose(Job(), [Run(1, new string('x', 5000))]);

        Assert.True(prompt.Length < 1000);
    }

    [Theory]
    [InlineData("NOTHING NEW", true)]
    [InlineData("  nothing new.  ", true)]
    [InlineData("Nothing new on HN, but Rust 2.0 is out", false)]
    public void Silence_Should_Be_Recognised_Only_When_It_Is_All_That_Was_Said(string answer, bool silent)
    {
        Assert.Equal(silent, JobPrompt.IsNothingNew(answer));
    }
}

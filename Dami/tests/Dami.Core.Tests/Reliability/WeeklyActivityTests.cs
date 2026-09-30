using Dami.Contracts.Finance;
using Dami.Contracts.Memory;
using Dami.Core.Reliability;
using NSubstitute;
using Xunit;

namespace Dami.Core.Tests.Reliability;

/// <summary>What Dami did in a week, as counts: the other half of the Sunday notice (C11).</summary>
public sealed class WeeklyActivityTests
{
    private static readonly DateTimeOffset since = new(2026, 9, 27, 14, 0, 0, TimeSpan.Zero);

    private static async IAsyncEnumerable<Observation> ManyAsync(params Observation[] items)
    {
        foreach (var item in items)
        {
            yield return item;
        }

        await Task.CompletedTask;
    }

    [Fact]
    public async Task Should_Count_Conversations_Lessons_Diary_Entries_And_Receipts()
    {
        var corpus = Substitute.For<IObservationCorpus>();
        corpus.BetweenAsync(since, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(ManyAsync(
            new Observation(Guid.NewGuid(), since.AddHours(1), "chat", "a"),
            new Observation(Guid.NewGuid(), since.AddHours(2), "chat", "b"),
            new Observation(Guid.NewGuid(), since.AddHours(3), "lesson", "c"),
            new Observation(Guid.NewGuid(), since.AddHours(4), "diary", "d"),
            new Observation(Guid.NewGuid(), since.AddHours(5), "hermes-memory", "e")));
        var expenses = Substitute.For<IExpenseLedger>();
        expenses.BetweenAsync(DateOnly.FromDateTime(since.UtcDateTime), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns([new Expense(Guid.NewGuid(), new DateOnly(2026, 9, 28), "Aldi", 31.07m, "USD", "groceries", "mail", since)]);

        var line = await new WeeklyActivity(corpus, expenses, TimeProvider.System).LineAsync(since, CancellationToken.None);

        Assert.Equal("This week I answered 2 message(s), took 1 lesson(s), kept 1 diary entry(ies) and logged 1 receipt(s).", line);
    }
}

using Dami.Contracts.Anticoag;
using Dami.Contracts.Finance;
using Dami.Contracts.Models;
using Dami.Core.Life;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace Dami.Core.Tests.Life;

/// <summary>
/// "Ask your life anything": answered by the local model from Steve's own records, each answer
/// with its numbered sources, nothing leaving the host (docs/agent-landscape-2026-09.md §5, #3).
/// </summary>
public sealed class LifeAnswererTests
{
    private static readonly DateTimeOffset now = new(2026, 10, 3, 15, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("When did I last buy furnace filters?", new[] { "furnace", "filters" })]
    [InlineData("what did the vet say about Milo", new[] { "milo" })]
    public void Significant_Words_Should_Drop_The_Question_Words(string question, string[] words)
    {
        Assert.Equal(words, LifeWords.Of(question));
    }

    [Fact]
    public async Task Receipts_Should_Answer_By_Merchant_And_Money_Questions_Should_See_The_Recent_Ones()
    {
        var ledger = Substitute.For<IExpenseLedger>();
        ledger.BetweenAsync(Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(
        [
            new Expense(Guid.NewGuid(), new DateOnly(2026, 9, 12), "Menards", 42.10m, "USD", "household", "receipt-photo", now),
            new Expense(Guid.NewGuid(), new DateOnly(2026, 9, 28), "Costco", 84.12m, "USD", "groceries", "mail", now),
        ]);
        var source = new ExpenseLifeSource(ledger);

        var byName = await source.FindAsync("when was I last at menards", now, CancellationToken.None);
        var byMoney = await source.FindAsync("how much have I spent lately", now, CancellationToken.None);

        Assert.Equal(["Menards $42.10 (household)"], byName.Select(record => record.Text));
        Assert.Equal(2, byMoney.Count);
    }

    [Fact]
    public async Task Readings_Should_Come_Only_When_The_Question_Is_About_Anticoagulation()
    {
        var log = Substitute.For<IAnticoagLog>();
        log.ReadingsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns([new InrReading(Guid.NewGuid(), new DateOnly(2026, 9, 29), 2.4m, now)]);
        log.DosesAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(Array.Empty<DoseChange>());
        var source = new AnticoagLifeSource(log);

        Assert.Equal(["INR 2.4"], (await source.FindAsync("what was my last INR", now, CancellationToken.None)).Select(record => record.Text));
        Assert.Empty(await source.FindAsync("when did I last see Mom", now, CancellationToken.None));
    }

    [Fact]
    public async Task The_Local_Model_Should_Answer_From_Numbered_Records_Only()
    {
        var first = Substitute.For<ILifeSource>();
        first.FindAsync(Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns([new LifeRecord(now.AddDays(-21), "receipt", "Menards $42.10 (household)")]);
        var broken = Substitute.For<ILifeSource>();
        broken.FindAsync(Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<LifeRecord>>(_ => throw new InvalidOperationException("sidecar down"));
        var local = Substitute.For<IChatClient>();
        local.CompleteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(" On Sep 12 at Menards [1]. ");

        var answer = await new LifeAnswerer([first, broken], local, new FakeTimeProvider(now))
            .AnswerAsync("when did I last buy furnace filters?", CancellationToken.None);

        Assert.Equal("On Sep 12 at Menards [1].", answer.Answer);
        Assert.Single(answer.Sources);
        await local.Received(1).CompleteAsync(
            Arg.Is<string>(prompt => prompt.Contains("1. [2026-09-12 receipt] Menards $42.10 (household)", StringComparison.Ordinal)
                && prompt.Contains("ONLY the numbered records", StringComparison.Ordinal)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task No_Records_Should_Mean_No_Model_Call_And_No_Answer()
    {
        var local = Substitute.For<IChatClient>();

        var answer = await new LifeAnswerer([], local, new FakeTimeProvider(now)).AnswerAsync("anything", CancellationToken.None);

        Assert.Null(answer.Answer);
        await local.DidNotReceiveWithAnyArgs().CompleteAsync(default!, default);
    }
}

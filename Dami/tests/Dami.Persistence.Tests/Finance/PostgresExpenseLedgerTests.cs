using Dami.Contracts.Finance;
using Dami.Persistence.Finance;
using Microsoft.Extensions.Options;
using Xunit;

namespace Dami.Persistence.Tests.Finance;

/// <summary>The expense ledger against a live database (migration 043).</summary>
[Collection(DatabaseCollection.NAME)]
public sealed class PostgresExpenseLedgerTests
{
    private static readonly DateTimeOffset recorded = new(2026, 9, 29, 20, 0, 0, TimeSpan.Zero);

    private readonly DatabaseFixture fixture;

    public PostgresExpenseLedgerTests(DatabaseFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        this.fixture = fixture;
    }

    private static Expense Spent(string merchant, decimal total, int day, string category = "groceries") =>
        new(Guid.NewGuid(), new DateOnly(2026, 9, day), merchant, total, "USD", category, "receipt-photo", recorded);

    private PostgresExpenseLedger Ledger() =>
        new(this.fixture.DataSource, Options.Create(new PostgresOptions { SchemaName = DatabaseFixture.SCHEMA }));

    [Fact]
    public async Task A_Receipt_Should_Be_Recorded_Once_However_Often_It_Is_Photographed()
    {
        await this.fixture.ResetAsync();
        var ledger = this.Ledger();

        Assert.True(await ledger.RecordAsync(Spent("Costco", 84.12m, 29), CancellationToken.None));
        Assert.False(await ledger.RecordAsync(Spent("Costco", 84.12m, 29), CancellationToken.None));

        Assert.Single(await ledger.BetweenAsync(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), CancellationToken.None));
    }

    [Fact]
    public async Task BetweenAsync_Should_Return_The_Window_Inclusive_Newest_First_With_Exact_Amounts()
    {
        await this.fixture.ResetAsync();
        var ledger = this.Ledger();
        await ledger.RecordAsync(Spent("Aldi", 31.07m, 2), CancellationToken.None);
        await ledger.RecordAsync(Spent("Costco", 84.12m, 29), CancellationToken.None);
        await ledger.RecordAsync(Spent("Target", 12.00m, 30), CancellationToken.None);

        var spent = await ledger.BetweenAsync(new DateOnly(2026, 9, 2), new DateOnly(2026, 9, 29), CancellationToken.None);

        Assert.Equal(["Costco", "Aldi"], spent.Select(expense => expense.Merchant));
        Assert.Equal(84.12m, spent[0].Total);
        Assert.Equal(new DateOnly(2026, 9, 29), spent[0].SpentOn);
    }
}

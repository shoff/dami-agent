using Dami.Persistence.Mail;
using Microsoft.Extensions.Options;
using Xunit;

namespace Dami.Persistence.Tests.Mail;

/// <summary>The forwarded-mail ledger against a live database (migration 046).</summary>
[Collection(DatabaseCollection.NAME)]
public sealed class PostgresMailLedgerTests
{
    private static readonly DateTimeOffset at = new(2026, 9, 29, 12, 0, 0, TimeSpan.Zero);

    private readonly DatabaseFixture fixture;

    public PostgresMailLedgerTests(DatabaseFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        this.fixture = fixture;
    }

    private PostgresMailLedger Ledger() =>
        new(this.fixture.DataSource, Options.Create(new PostgresOptions { SchemaName = DatabaseFixture.SCHEMA }));

    [Fact]
    public async Task A_Message_Should_Be_Filed_Once_And_Known_Afterwards()
    {
        await this.fixture.ResetAsync();
        var ledger = this.Ledger();

        Assert.True(await ledger.RecordAsync("<a@mail>", at, "receipt", "Aldi $31.07", at, CancellationToken.None));
        Assert.False(await ledger.RecordAsync("<a@mail>", at, "receipt", "Aldi $31.07", at, CancellationToken.None));

        var filed = await ledger.FiledAsync(["<a@mail>", "<b@mail>"], CancellationToken.None);
        Assert.Equal(["<a@mail>"], filed);
    }
}

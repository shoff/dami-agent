using Dami.Contracts.Anticoag;
using Dami.Persistence.Anticoag;
using Microsoft.Extensions.Options;
using Xunit;

namespace Dami.Persistence.Tests.Anticoag;

/// <summary>INR readings and dose changes against a live database (migration 050).</summary>
[Collection(DatabaseCollection.NAME)]
public sealed class PostgresAnticoagLogTests
{
    private static readonly DateTimeOffset at = new(2026, 9, 29, 16, 0, 0, TimeSpan.FromHours(-5));

    private readonly DatabaseFixture fixture;

    public PostgresAnticoagLogTests(DatabaseFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        this.fixture = fixture;
    }

    [Fact]
    public async Task Readings_And_Doses_Should_Come_Back_Newest_First_Each_On_Its_Own()
    {
        await this.fixture.ResetAsync();
        var log = new PostgresAnticoagLog(this.fixture.DataSource, Options.Create(new PostgresOptions { SchemaName = DatabaseFixture.SCHEMA }));
        await log.RecordAsync(new InrReading(Guid.NewGuid(), new DateOnly(2026, 9, 15), 2.6m, at), CancellationToken.None);
        await log.RecordAsync(new InrReading(Guid.NewGuid(), new DateOnly(2026, 9, 29), 2.4m, at), CancellationToken.None);
        await log.RecordAsync(new DoseChange(Guid.NewGuid(), new DateOnly(2026, 9, 29), "7.5 mg Mon/Wed, 5 mg other days", at), CancellationToken.None);

        var readings = await log.ReadingsAsync(5, CancellationToken.None);
        var doses = await log.DosesAsync(5, CancellationToken.None);

        Assert.Equal([2.4m, 2.6m], readings.Select(reading => reading.Inr));
        Assert.Equal("7.5 mg Mon/Wed, 5 mg other days", Assert.Single(doses).Dose);
    }
}

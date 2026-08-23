using Dami.Contracts.Events;
using Dami.Persistence.Events;
using Microsoft.Extensions.Options;
using Xunit;

namespace Dami.Persistence.Tests.Events;

/// <summary>The meter counts egress attempts — and only egress attempts — since a cutoff.</summary>
[Collection(DatabaseCollection.NAME)]
public sealed class PostgresEgressMeterTests
{
    private static readonly DateTimeOffset at = new(2026, 8, 23, 20, 0, 0, TimeSpan.Zero);

    private readonly DatabaseFixture fixture;

    public PostgresEgressMeterTests(DatabaseFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        this.fixture = fixture;
    }

    [Fact]
    public async Task CountRequestsSinceAsync_Should_Count_Only_Egress_Requests_In_The_Window()
    {
        await this.fixture.ResetAsync();
        var (store, meter) = this.CreateStores();
        await store.AppendAsync(NewEvent(ExecutionEventType.EgressRequested, at), CancellationToken.None);
        await store.AppendAsync(NewEvent(ExecutionEventType.EgressRequested, at.AddHours(-3)), CancellationToken.None);
        await store.AppendAsync(NewEvent(ExecutionEventType.EgressCompleted, at), CancellationToken.None);

        var count = await meter.CountRequestsSinceAsync(at.AddHours(-1), CancellationToken.None);

        Assert.Equal(1, count);
    }

    private static ExecutionEvent NewEvent(ExecutionEventType type, DateTimeOffset occurredAt)
    {
        return new ExecutionEvent(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null,
            ExecutionOrigin.ScheduledService, "test", type,
            ExecutionStatus.Running, occurredAt, "an egress attempt", null, null);
    }

    private (PostgresExecutionEventStore, PostgresEgressMeter) CreateStores()
    {
        var options = Options.Create(new PostgresOptions { SchemaName = DatabaseFixture.SCHEMA });
        return (
            new PostgresExecutionEventStore(
                this.fixture.DataSource, options,
                Microsoft.Extensions.Logging.Abstractions.NullLogger<PostgresExecutionEventStore>.Instance),
            new PostgresEgressMeter(this.fixture.DataSource, options));
    }
}

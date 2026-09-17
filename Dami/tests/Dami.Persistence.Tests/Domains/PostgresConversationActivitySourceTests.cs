using Dami.Contracts.Domains;
using Dami.Contracts.Sessions;
using Dami.Persistence.Domains;
using Dami.Persistence.Sessions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Dami.Persistence.Tests.Domains;

[Collection(DatabaseCollection.NAME)]
public sealed class PostgresConversationActivitySourceTests
{
    private static readonly TimeZoneInfo chicago = TimeZoneInfo.FindSystemTimeZoneById("America/Chicago");

    // A year no other test writes into, so shared-schema rows cannot collide.
    private static readonly DateTimeOffset evening = new(2031, 5, 6, 1, 0, 0, TimeSpan.Zero);   // 20:00 on 05-05 in Chicago
    private static readonly DateTimeOffset smallHours = new(2031, 5, 5, 8, 0, 0, TimeSpan.Zero); // 03:00 on 05-05 in Chicago

    private readonly DatabaseFixture fixture;

    public PostgresConversationActivitySourceTests(DatabaseFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        this.fixture = fixture;
    }

    [Fact]
    public async Task ReadAsync_Should_Count_Turns_Per_Local_Day()
    {
        await this.SeedAsync();
        try
        {
            var series = await this.CreateSource().ReadAsync(
                new DateOnly(2031, 5, 5), new DateOnly(2031, 5, 5), chicago, CancellationToken.None);

            Assert.Equal(2.0, Find(series, "conversation-turns").Points[0].Value);
        }
        finally
        {
            await this.ClearAsync();
        }
    }

    [Fact]
    public async Task ReadAsync_Should_Count_The_Small_Hours_Separately()
    {
        await this.SeedAsync();
        try
        {
            var series = await this.CreateSource().ReadAsync(
                new DateOnly(2031, 5, 5), new DateOnly(2031, 5, 5), chicago, CancellationToken.None);

            Assert.Equal(1.0, Find(series, "late-night-turns").Points[0].Value);
        }
        finally
        {
            await this.ClearAsync();
        }
    }

    /// <summary>The schema is shared and never truncated between tests; the 2031 rows are this test's alone.</summary>
    private async Task ClearAsync()
    {
        await using var command = this.fixture.DataSource.CreateCommand(
            $"""
            delete from {DatabaseFixture.SCHEMA}.conversation_turns where requested_at >= '2031-01-01';
            delete from {DatabaseFixture.SCHEMA}.conversation_sessions where created_at >= '2031-01-01';
            """);
        await command.ExecuteNonQueryAsync(CancellationToken.None);
    }

    private static DailySeries Find(IReadOnlyList<DailySeries> series, string metric)
    {
        foreach (var item in series)
        {
            if (item.Metric == metric)
            {
                return item;
            }
        }

        throw new Xunit.Sdk.XunitException($"no series named {metric}");
    }

    private async Task SeedAsync()
    {
        await this.ClearAsync();
        var store = new PostgresSessionStore(
            this.fixture.DataSource, Options.Create(new PostgresOptions { SchemaName = DatabaseFixture.SCHEMA }));
        var session = new ConversationSession(Guid.NewGuid(), ConversationSessionState.Active, smallHours, smallHours);
        await store.CreateAsync(session, CancellationToken.None);
        await store.ReserveTurnAsync(
            new ConversationTurnRequest(session.SessionId, Guid.NewGuid(), "night", smallHours), CancellationToken.None);
        await store.ReserveTurnAsync(
            new ConversationTurnRequest(session.SessionId, Guid.NewGuid(), "evening", evening), CancellationToken.None);
    }

    private PostgresConversationActivitySource CreateSource() =>
        new(this.fixture.DataSource, Options.Create(new PostgresOptions { SchemaName = DatabaseFixture.SCHEMA }));
}

using Dami.Contracts.Calendar;
using Dami.Persistence.Calendar;
using Microsoft.Extensions.Options;
using Xunit;

namespace Dami.Persistence.Tests.Calendar;

/// <summary>The calendar mirror against a live database (migration 045).</summary>
[Collection(DatabaseCollection.NAME)]
public sealed class PostgresCalendarStoreTests
{
    private static readonly DateTimeOffset monday = new(2026, 9, 28, 5, 0, 0, TimeSpan.Zero);

    private readonly DatabaseFixture fixture;

    public PostgresCalendarStoreTests(DatabaseFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        this.fixture = fixture;
    }

    private PostgresCalendarStore Store() =>
        new(this.fixture.DataSource, Options.Create(new PostgresOptions { SchemaName = DatabaseFixture.SCHEMA }));

    private static CalendarEvent At(string summary, int day, int hour) =>
        new($"{summary}@{day}", monday.AddDays(day).AddHours(hour), monday.AddDays(day).AddHours(hour + 1), false, summary, null);

    [Fact]
    public async Task A_New_Read_Should_Replace_The_Window_So_A_Cancelled_Meeting_Is_Gone()
    {
        await this.fixture.ResetAsync();
        var store = this.Store();
        await store.ReplaceAsync(monday, monday.AddDays(14), [At("Dentist", 2, 9), At("Standup", 1, 4)], CancellationToken.None);

        await store.ReplaceAsync(monday, monday.AddDays(14), [At("Standup", 1, 4)], CancellationToken.None);

        var events = await store.BetweenAsync(monday, monday.AddDays(14), CancellationToken.None);
        Assert.Equal(["Standup"], events.Select(item => item.Summary));
    }

    [Fact]
    public async Task BetweenAsync_Should_Return_Only_The_Asked_Window_Earliest_First()
    {
        await this.fixture.ResetAsync();
        var store = this.Store();
        await store.ReplaceAsync(monday, monday.AddDays(14),
            [At("Later", 3, 9), At("Today late", 0, 20), At("Today early", 0, 8)], CancellationToken.None);

        var today = await store.BetweenAsync(monday, monday.AddDays(1), CancellationToken.None);

        Assert.Equal(["Today early", "Today late"], today.Select(item => item.Summary));
    }

    [Fact]
    public async Task Local_Offsets_Should_Be_Accepted_Everywhere()
    {
        // 2026-09-29: the diary passed Chicago midnight (-05:00) and Npgsql refused it; the
        // store takes any offset and stores the instant.
        await this.fixture.ResetAsync();
        var store = this.Store();
        var chicago = TimeSpan.FromHours(-5);
        var midnight = new DateTimeOffset(2026, 9, 29, 0, 0, 0, chicago);
        var dentist = new CalendarEvent("d@1", new DateTimeOffset(2026, 9, 29, 9, 30, 0, chicago), null, false, "Dentist", null);

        await store.ReplaceAsync(midnight, midnight.AddDays(1), [dentist], CancellationToken.None);

        var today = await store.BetweenAsync(midnight, midnight.AddDays(1), CancellationToken.None);
        Assert.Equal(dentist.StartsAt, today.Single().StartsAt);
    }

    [Fact]
    public async Task Replacing_One_Window_Should_Leave_Events_Outside_It()
    {
        await this.fixture.ResetAsync();
        var store = this.Store();
        await store.ReplaceAsync(monday, monday.AddDays(30), [At("Far", 20, 9)], CancellationToken.None);

        await store.ReplaceAsync(monday, monday.AddDays(14), [], CancellationToken.None);

        Assert.Single(await store.BetweenAsync(monday, monday.AddDays(30), CancellationToken.None));
    }
}

using Dami.Contracts.Nutrition;
using Dami.Persistence.Nutrition;
using Microsoft.Extensions.Options;
using Xunit;

namespace Dami.Persistence.Tests.Nutrition;

/// <summary>The meal log against a live database (migration 048).</summary>
[Collection(DatabaseCollection.NAME)]
public sealed class PostgresMealLogTests
{
    private static readonly DateTimeOffset noonChicago = new(2026, 9, 29, 12, 0, 0, TimeSpan.FromHours(-5));

    private readonly DatabaseFixture fixture;

    public PostgresMealLogTests(DatabaseFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        this.fixture = fixture;
    }

    [Fact]
    public async Task Meals_Should_Come_Back_For_Their_Window_In_Local_Offsets_Too()
    {
        await this.fixture.ResetAsync();
        var log = new PostgresMealLog(this.fixture.DataSource, Options.Create(new PostgresOptions { SchemaName = DatabaseFixture.SCHEMA }));
        await log.RecordAsync(new Meal(Guid.NewGuid(), noonChicago, "chicken burrito", 650, 40, noonChicago), CancellationToken.None);
        await log.RecordAsync(new Meal(Guid.NewGuid(), noonChicago.AddDays(-1), "yesterday's pasta", 800, 30, noonChicago), CancellationToken.None);

        var midnight = new DateTimeOffset(2026, 9, 29, 0, 0, 0, TimeSpan.FromHours(-5));
        var today = await log.BetweenAsync(midnight, midnight.AddDays(1), CancellationToken.None);

        var meal = Assert.Single(today);
        Assert.Equal(("chicken burrito", 650, 40), (meal.Description, meal.Calories, meal.ProteinGrams));
        Assert.Equal(noonChicago, meal.EatenAt);
    }
}

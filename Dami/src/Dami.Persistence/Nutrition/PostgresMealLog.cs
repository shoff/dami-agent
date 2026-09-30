using Dami.Contracts.Nutrition;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Dami.Persistence.Nutrition;

/// <summary>PostgreSQL store for the meal log (migration 048). Instants are stored in UTC, whatever offset arrives.</summary>
public sealed class PostgresMealLog : IMealLog
{
    private readonly NpgsqlDataSource dataSource;
    private readonly string schema;

    /// <summary>Creates the log.</summary>
    public PostgresMealLog(NpgsqlDataSource dataSource, IOptions<PostgresOptions> options)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        ArgumentNullException.ThrowIfNull(options);
        this.dataSource = dataSource;
        this.schema = options.Value.SchemaName;
    }

    /// <inheritdoc />
    public async Task RecordAsync(Meal meal, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(meal);
        await using var command = this.dataSource.CreateCommand(
            $"insert into {this.schema}.meals (meal_id, eaten_at, description, calories, protein_g, recorded_at) "
            + "values (@id, @eaten, @description, @calories, @protein, @recorded);");
        command.Parameters.AddWithValue("id", meal.MealId);
        command.Parameters.AddWithValue("eaten", meal.EatenAt.ToUniversalTime());
        command.Parameters.AddWithValue("description", meal.Description);
        command.Parameters.AddWithValue("calories", meal.Calories);
        command.Parameters.AddWithValue("protein", meal.ProteinGrams);
        command.Parameters.AddWithValue("recorded", meal.RecordedAt.ToUniversalTime());
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Meal>> BetweenAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        await using var command = this.dataSource.CreateCommand(
            $"select meal_id, eaten_at, description, calories, protein_g, recorded_at from {this.schema}.meals "
            + "where eaten_at >= @from and eaten_at < @to order by eaten_at;");
        command.Parameters.AddWithValue("from", from.ToUniversalTime());
        command.Parameters.AddWithValue("to", to.ToUniversalTime());
        var meals = new List<Meal>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            meals.Add(new Meal(
                reader.GetGuid(0),
                await reader.GetFieldValueAsync<DateTimeOffset>(1, cancellationToken).ConfigureAwait(false),
                reader.GetString(2), reader.GetInt32(3), reader.GetInt32(4),
                await reader.GetFieldValueAsync<DateTimeOffset>(5, cancellationToken).ConfigureAwait(false)));
        }

        return meals;
    }
}

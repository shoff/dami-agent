using Dami.Contracts.Anticoag;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Dami.Persistence.Anticoag;

/// <summary>PostgreSQL store for INR readings and dose changes (migration 050).</summary>
public sealed class PostgresAnticoagLog : IAnticoagLog
{
    private readonly NpgsqlDataSource dataSource;
    private readonly string schema;

    /// <summary>Creates the log.</summary>
    public PostgresAnticoagLog(NpgsqlDataSource dataSource, IOptions<PostgresOptions> options)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        ArgumentNullException.ThrowIfNull(options);
        this.dataSource = dataSource;
        this.schema = options.Value.SchemaName;
    }

    /// <inheritdoc />
    public Task RecordAsync(InrReading reading, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reading);
        return this.InsertAsync(reading.EntryId, "inr", reading.OnDay, reading.Inr, null, reading.RecordedAt, cancellationToken);
    }

    /// <inheritdoc />
    public Task RecordAsync(DoseChange dose, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dose);
        return this.InsertAsync(dose.EntryId, "dose", dose.OnDay, null, dose.Dose, dose.RecordedAt, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<InrReading>> ReadingsAsync(int limit, CancellationToken cancellationToken)
    {
        await using var command = this.dataSource.CreateCommand(
            $"select entry_id, on_day, inr, recorded_at from {this.schema}.anticoag_log where kind = 'inr' "
            + "order by on_day desc, recorded_at desc limit @limit;");
        command.Parameters.AddWithValue("limit", limit);
        var readings = new List<InrReading>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            readings.Add(new InrReading(
                reader.GetGuid(0), await reader.GetFieldValueAsync<DateOnly>(1, cancellationToken).ConfigureAwait(false),
                reader.GetDecimal(2), await reader.GetFieldValueAsync<DateTimeOffset>(3, cancellationToken).ConfigureAwait(false)));
        }

        return readings;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DoseChange>> DosesAsync(int limit, CancellationToken cancellationToken)
    {
        await using var command = this.dataSource.CreateCommand(
            $"select entry_id, on_day, dose, recorded_at from {this.schema}.anticoag_log where kind = 'dose' "
            + "order by on_day desc, recorded_at desc limit @limit;");
        command.Parameters.AddWithValue("limit", limit);
        var doses = new List<DoseChange>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            doses.Add(new DoseChange(
                reader.GetGuid(0), await reader.GetFieldValueAsync<DateOnly>(1, cancellationToken).ConfigureAwait(false),
                reader.GetString(2), await reader.GetFieldValueAsync<DateTimeOffset>(3, cancellationToken).ConfigureAwait(false)));
        }

        return doses;
    }

    private async Task InsertAsync(
        Guid id, string kind, DateOnly onDay, decimal? inr, string? dose, DateTimeOffset recordedAt, CancellationToken cancellationToken)
    {
        await using var command = this.dataSource.CreateCommand(
            $"insert into {this.schema}.anticoag_log (entry_id, kind, on_day, inr, dose, recorded_at) "
            + "values (@id, @kind, @day, @inr, @dose, @recorded);");
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("kind", kind);
        command.Parameters.AddWithValue("day", onDay);
        command.Parameters.AddWithValue("inr", (object?)inr ?? DBNull.Value);
        command.Parameters.AddWithValue("dose", (object?)dose ?? DBNull.Value);
        command.Parameters.AddWithValue("recorded", recordedAt.ToUniversalTime());
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}

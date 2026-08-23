using Dami.Contracts.Events;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Dami.Persistence.Events;

/// <summary>The egress meter, read straight off the append-only event stream.</summary>
public sealed class PostgresEgressMeter : IEgressMeter
{
    private readonly NpgsqlDataSource dataSource;
    private readonly PostgresOptions meterOptions;

    /// <summary>Creates the meter.</summary>
    public PostgresEgressMeter(NpgsqlDataSource dataSource, IOptions<PostgresOptions> meterOptions)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        ArgumentNullException.ThrowIfNull(meterOptions);

        this.dataSource = dataSource;
        this.meterOptions = meterOptions.Value;
    }

    /// <inheritdoc />
    public async Task<int> CountRequestsSinceAsync(
        DateTimeOffset since,
        CancellationToken cancellationToken)
    {
        await using var command = this.dataSource.CreateCommand(
            $"""
            select count(*) from {this.meterOptions.SchemaName}.execution_events
             where type = 'EgressRequested' and occurred_at >= @since;
            """);
        command.Parameters.AddWithValue("since", since);
        var count = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return Convert.ToInt32(count, System.Globalization.CultureInfo.InvariantCulture);
    }
}

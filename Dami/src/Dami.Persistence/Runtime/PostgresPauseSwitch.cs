using Dami.Contracts.Runtime;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Dami.Persistence.Runtime;

/// <summary>PostgreSQL store for the pause switch (migration 049): one row, or none.</summary>
public sealed class PostgresPauseSwitch : IPauseSwitch
{
    private readonly NpgsqlDataSource dataSource;
    private readonly string schema;

    /// <summary>Creates the switch.</summary>
    public PostgresPauseSwitch(NpgsqlDataSource dataSource, IOptions<PostgresOptions> options)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        ArgumentNullException.ThrowIfNull(options);
        this.dataSource = dataSource;
        this.schema = options.Value.SchemaName;
    }

    /// <inheritdoc />
    public async Task<PauseState?> CurrentAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var command = this.dataSource.CreateCommand(
            $"select paused_until, reason, set_at from {this.schema}.runtime_pause "
            + "where paused_until is null or paused_until > @now;");
        command.Parameters.AddWithValue("now", now.ToUniversalTime());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return new PauseState(
            await reader.IsDBNullAsync(0, cancellationToken).ConfigureAwait(false)
                ? null
                : await reader.GetFieldValueAsync<DateTimeOffset>(0, cancellationToken).ConfigureAwait(false),
            reader.GetString(1),
            await reader.GetFieldValueAsync<DateTimeOffset>(2, cancellationToken).ConfigureAwait(false));
    }

    /// <inheritdoc />
    public async Task PauseAsync(DateTimeOffset? until, string reason, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reason);
        await using var command = this.dataSource.CreateCommand(
            $"insert into {this.schema}.runtime_pause (singleton, paused_until, reason, set_at) values (true, @until, @reason, @now) "
            + "on conflict (singleton) do update set paused_until = excluded.paused_until, reason = excluded.reason, set_at = excluded.set_at;");
        command.Parameters.AddWithValue("until", (object?)until?.ToUniversalTime() ?? DBNull.Value);
        command.Parameters.AddWithValue("reason", reason);
        command.Parameters.AddWithValue("now", now.ToUniversalTime());
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task ResumeAsync(CancellationToken cancellationToken)
    {
        await using var command = this.dataSource.CreateCommand($"delete from {this.schema}.runtime_pause;");
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}

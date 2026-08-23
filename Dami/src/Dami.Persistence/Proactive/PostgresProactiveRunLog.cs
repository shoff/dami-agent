using Dami.Contracts.Proactive;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Dami.Persistence.Proactive;

/// <summary>The scheduler's durable memory over PostgreSQL.</summary>
public sealed class PostgresProactiveRunLog : IProactiveRunLog
{
    private readonly NpgsqlDataSource dataSource;
    private readonly PostgresOptions storeOptions;
    private readonly ILogger<PostgresProactiveRunLog> logger;

    /// <summary>Creates the run log.</summary>
    public PostgresProactiveRunLog(
        NpgsqlDataSource dataSource,
        IOptions<PostgresOptions> storeOptions,
        ILogger<PostgresProactiveRunLog> logger)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        ArgumentNullException.ThrowIfNull(storeOptions);
        ArgumentNullException.ThrowIfNull(logger);

        this.dataSource = dataSource;
        this.storeOptions = storeOptions.Value;
        this.logger = logger;
    }

    private string Table => $"{this.storeOptions.SchemaName}.proactive_runs";

    /// <inheritdoc />
    public async Task RecordAsync(
        Guid runId,
        string serviceName,
        Guid traceId,
        DateTimeOffset ranAt,
        ProactiveStatus status,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(serviceName);

        await using var command = this.dataSource.CreateCommand(
            $"insert into {this.Table} (run_id, service_name, trace_id, ran_at, status) "
            + "values (@run_id, @service_name, @trace_id, @ran_at, @status) on conflict do nothing;");
        command.Parameters.AddWithValue("run_id", runId);
        command.Parameters.AddWithValue("service_name", serviceName);
        command.Parameters.AddWithValue("trace_id", traceId);
        command.Parameters.AddWithValue("ran_at", ranAt);
        command.Parameters.AddWithValue("status", status.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<DateTimeOffset?> LastRanAtAsync(string serviceName, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(serviceName);

        await using var command = this.dataSource.CreateCommand(
            $"select max(ran_at) from {this.Table} where service_name = @service_name;");
        command.Parameters.AddWithValue("service_name", serviceName);

        var scalar = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return scalar is DateTime or DateTimeOffset
            ? (DateTimeOffset?)((scalar as DateTimeOffset?) ?? (DateTime)scalar)
            : null;
    }
}

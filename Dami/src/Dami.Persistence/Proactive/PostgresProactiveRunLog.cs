using Dami.Contracts.Proactive;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Dami.Persistence.Proactive;

/// <summary>The scheduler's durable memory over PostgreSQL.</summary>
public sealed class PostgresProactiveRunLog : IProactiveRunLog, IProactiveRunHistory
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

    private string LeaseTable => $"{this.storeOptions.SchemaName}.proactive_run_leases";

    /// <inheritdoc />
    public async Task<IProactiveRunLease?> TryAcquireLeaseAsync(
        string serviceName,
        DateTimeOffset acquiredAt,
        TimeSpan duration,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(serviceName);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(duration, TimeSpan.Zero);

        var leaseId = Guid.NewGuid();
        await using var command = this.dataSource.CreateCommand(
            $"insert into {this.LeaseTable} (service_name, lease_id, expires_at) "
            + "values (@service_name, @lease_id, @expires_at) "
            + "on conflict (service_name) do update "
            + "set lease_id = excluded.lease_id, expires_at = excluded.expires_at "
            + $"where {this.LeaseTable}.expires_at <= @acquired_at "
            + "returning lease_id;");
        command.Parameters.AddWithValue("service_name", serviceName);
        command.Parameters.AddWithValue("lease_id", leaseId);
        command.Parameters.AddWithValue("expires_at", acquiredAt + duration);
        command.Parameters.AddWithValue("acquired_at", acquiredAt);

        var acquiredLeaseId = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return acquiredLeaseId is Guid
            ? new PostgresProactiveRunLease(this.dataSource, this.LeaseTable, serviceName, leaseId)
            : null;
    }

    /// <inheritdoc />
    public async Task RecordAsync(
        Guid runId,
        string serviceName,
        Guid traceId,
        DateTimeOffset ranAt,
        ProactiveStatus status,
        ProactiveCadence cadence,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(serviceName);

        await using var command = this.dataSource.CreateCommand(
            $"insert into {this.Table} (run_id, service_name, trace_id, ran_at, status, cadence) "
            + "values (@run_id, @service_name, @trace_id, @ran_at, @status, @cadence) "
            + "on conflict do nothing;");
        command.Parameters.AddWithValue("cadence", cadence.ToString());
        command.Parameters.AddWithValue("run_id", runId);
        command.Parameters.AddWithValue("service_name", serviceName);
        command.Parameters.AddWithValue("trace_id", traceId);
        command.Parameters.AddWithValue("ran_at", ranAt);
        command.Parameters.AddWithValue("status", status.ToString());
        var written = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        if (written == 1)
        {
            return;
        }

        if (!await this.IsExactRetryAsync(
                runId, serviceName, traceId, ranAt, status, cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException(
                $"Proactive run '{runId}' is already recorded with different data.");
        }

        this.logger.LogDebug("Proactive run {RunId} was already recorded; the exact retry was discarded", runId);
    }

    private async Task<bool> IsExactRetryAsync(
        Guid runId,
        string serviceName,
        Guid traceId,
        DateTimeOffset ranAt,
        ProactiveStatus status,
        CancellationToken cancellationToken)
    {
        await using var command = this.dataSource.CreateCommand(
            "select exists (select 1 "
            + $"from {this.Table} where run_id = @run_id and service_name = @service_name "
            + "and trace_id = @trace_id and ran_at = @ran_at and status = @status);");
        command.Parameters.AddWithValue("run_id", runId);
        command.Parameters.AddWithValue("service_name", serviceName);
        command.Parameters.AddWithValue("trace_id", traceId);
        command.Parameters.AddWithValue("ran_at", ranAt);
        command.Parameters.AddWithValue("status", status.ToString());

        return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is true;
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

    /// <inheritdoc />
    /// <remarks>
    /// One query, ranked per service, rather than list-then-N-queries: the tier has eleven
    /// services and a panel that polls should not cost twelve round trips.
    /// </remarks>
    public async Task<IReadOnlyList<ProactiveServiceHistory>> ReadAsync(
        int recentPerService,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(recentPerService);

        await using var command = this.dataSource.CreateCommand(
            "with ranked as ("
            + "  select run_id, service_name, trace_id, ran_at, status, cadence,"
            + "         row_number() over (partition by service_name order by ran_at desc) as rank,"
            + "         count(*) over (partition by service_name) as runs,"
            + "         max(ran_at) over (partition by service_name) as last_ran_at"
            + $"    from {this.Table}"
            + ") select service_name, run_id, trace_id, ran_at, status, runs, last_ran_at, cadence"
            + "  from ranked where rank <= @recent"
            + "  order by last_ran_at desc, service_name, ran_at desc;");
        command.Parameters.AddWithValue("recent", recentPerService);

        return await ReadHistoriesAsync(command, cancellationToken).ConfigureAwait(false);
    }

    /// <remarks>
    /// Cadence is nullable because runs recorded before migration 035 predate the column;
    /// they read back as "unknown" rather than as a guess.
    /// </remarks>
    private static async Task<(int Runs, DateTimeOffset LastRanAt, ProactiveCadence? Cadence)> TotalsAsync(
        NpgsqlDataReader reader,
        CancellationToken cancellationToken)
    {
        var lastRanAt = await reader
            .GetFieldValueAsync<DateTimeOffset>(6, cancellationToken).ConfigureAwait(false);
        var unknown = await reader.IsDBNullAsync(7, cancellationToken).ConfigureAwait(false);
        return (
            reader.GetInt32(5),
            lastRanAt,
            unknown ? null : Enum.Parse<ProactiveCadence>(reader.GetString(7)));
    }

    private static async Task<IReadOnlyList<ProactiveServiceHistory>> ReadHistoriesAsync(
        NpgsqlCommand command,
        CancellationToken cancellationToken)
    {
        var order = new List<string>();
        var runs = new Dictionary<string, List<ProactiveRun>>(StringComparer.Ordinal);
        var totals =
            new Dictionary<string, (int Runs, DateTimeOffset LastRanAt, ProactiveCadence? Cadence)>(
                StringComparer.Ordinal);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var service = reader.GetString(0);
            if (!runs.TryGetValue(service, out var list))
            {
                list = [];
                runs[service] = list;
                order.Add(service);
                totals[service] = await TotalsAsync(reader, cancellationToken).ConfigureAwait(false);
            }

            list.Add(new ProactiveRun(
                reader.GetGuid(1), reader.GetGuid(2),
                await reader.GetFieldValueAsync<DateTimeOffset>(3, cancellationToken).ConfigureAwait(false),
                Enum.Parse<ProactiveStatus>(reader.GetString(4))));
        }

        return order
            .Select(service => new ProactiveServiceHistory(
                service, totals[service].Runs, totals[service].LastRanAt,
                runs[service][0].Status, totals[service].Cadence, runs[service]))
            .ToList();
    }
}

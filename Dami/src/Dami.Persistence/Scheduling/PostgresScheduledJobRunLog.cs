using Dami.Contracts.Scheduling;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Dami.Persistence.Scheduling;

/// <summary>PostgreSQL store for scheduled-job outputs (migration 044).</summary>
public sealed class PostgresScheduledJobRunLog : IScheduledJobRunLog
{
    private readonly NpgsqlDataSource dataSource;
    private readonly string schema;

    /// <summary>Creates the log.</summary>
    public PostgresScheduledJobRunLog(NpgsqlDataSource dataSource, IOptions<PostgresOptions> options)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        ArgumentNullException.ThrowIfNull(options);
        this.dataSource = dataSource;
        this.schema = options.Value.SchemaName;
    }

    /// <inheritdoc />
    public async Task RecordAsync(ScheduledJobRun run, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);
        await using var command = this.dataSource.CreateCommand(
            $"insert into {this.schema}.scheduled_job_runs (run_id, job_id, ran_at, output, delivered) "
            + "values (@id, @job, @at, @output, @delivered);");
        command.Parameters.AddWithValue("id", run.RunId);
        command.Parameters.AddWithValue("job", run.JobId);
        command.Parameters.AddWithValue("at", run.RanAt);
        command.Parameters.AddWithValue("output", run.Output);
        command.Parameters.AddWithValue("delivered", run.Delivered);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ScheduledJobRun>> RecentAsync(Guid jobId, int limit, CancellationToken cancellationToken)
    {
        await using var command = this.dataSource.CreateCommand(
            $"select run_id, job_id, ran_at, output, delivered from {this.schema}.scheduled_job_runs "
            + "where job_id = @job and delivered order by ran_at desc limit @limit;");
        command.Parameters.AddWithValue("job", jobId);
        command.Parameters.AddWithValue("limit", limit);
        var runs = new List<ScheduledJobRun>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            runs.Add(new ScheduledJobRun(
                reader.GetGuid(0), reader.GetGuid(1),
                await reader.GetFieldValueAsync<DateTimeOffset>(2, cancellationToken).ConfigureAwait(false),
                reader.GetString(3), reader.GetBoolean(4)));
        }

        return runs;
    }
}

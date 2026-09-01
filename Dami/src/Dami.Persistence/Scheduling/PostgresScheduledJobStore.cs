using System.Text.Json;
using Dami.Contracts.Scheduling;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Dami.Persistence.Scheduling;

/// <summary>Stores user-confirmed schedules and dashboard state in PostgreSQL.</summary>
public sealed class PostgresScheduledJobStore : IScheduledJobStore
{
    private readonly NpgsqlDataSource dataSource;
    private readonly PostgresOptions options;

    /// <summary>Creates the store.</summary>
    public PostgresScheduledJobStore(NpgsqlDataSource dataSource, IOptions<PostgresOptions> options)
    {
        this.dataSource = dataSource;
        this.options = options.Value;
    }

    private string Table => $"{this.options.SchemaName}.scheduled_jobs";

    /// <inheritdoc />
    public async Task<ScheduledJob> AddAsync(ScheduledJob job, CancellationToken cancellationToken)
    {
        await using var command = this.dataSource.CreateCommand(
            $"insert into {this.Table} (job_id,name,description,kind,payload,arguments,cron_expression,time_zone_id,status,created_at,confirmed_at,next_run_at,last_run_at,last_run_status) "
            + "values (@id,@name,@description,@kind,@payload,@arguments::jsonb,@cron,@zone,@status,@created,@confirmed,@next,@last,@last_status)");
        AddParameters(command, job);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        return job;
    }

    /// <inheritdoc />
    public async Task<ScheduledJob?> FindAsync(Guid jobId, CancellationToken cancellationToken)
    {
        await using var command = this.dataSource.CreateCommand(
            $"select job_id,name,description,kind,payload,arguments,cron_expression,time_zone_id,status,created_at,confirmed_at,next_run_at,last_run_at,last_run_status from {this.Table} where job_id=@id");
        command.Parameters.AddWithValue("id", jobId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? Read(reader) : null;
    }

    /// <inheritdoc />
    public async Task<ScheduledJob> UpdateAsync(ScheduledJob job, CancellationToken cancellationToken)
    {
        await using var command = this.dataSource.CreateCommand(
            $"update {this.Table} set name=@name,description=@description,kind=@kind,payload=@payload,arguments=@arguments::jsonb,cron_expression=@cron,time_zone_id=@zone,status=@status,created_at=@created,confirmed_at=@confirmed,next_run_at=@next,last_run_at=@last,last_run_status=@last_status where job_id=@id");
        AddParameters(command, job);
        if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
        {
            throw new KeyNotFoundException($"Scheduled job {job.JobId} does not exist.");
        }

        return job;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ScheduledJob>> ListAsync(CancellationToken cancellationToken)
    {
        await using var command = this.dataSource.CreateCommand(
            $"select job_id,name,description,kind,payload,arguments,cron_expression,time_zone_id,status,created_at,confirmed_at,next_run_at,last_run_at,last_run_status from {this.Table} order by created_at desc");
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var jobs = new List<ScheduledJob>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            jobs.Add(Read(reader));
        }

        return jobs;
    }

    private static void AddParameters(NpgsqlCommand command, ScheduledJob job)
    {
        command.Parameters.AddWithValue("id", job.JobId);
        command.Parameters.AddWithValue("name", job.Name);
        command.Parameters.AddWithValue("description", job.Description);
        command.Parameters.AddWithValue("kind", job.Kind.ToString());
        command.Parameters.AddWithValue("payload", job.Payload);
        command.Parameters.AddWithValue("arguments", JsonSerializer.Serialize(job.Arguments));
        command.Parameters.AddWithValue("cron", job.CronExpression);
        command.Parameters.AddWithValue("zone", job.TimeZoneId);
        command.Parameters.AddWithValue("status", job.Status.ToString());
        command.Parameters.AddWithValue("created", job.CreatedAt);
        command.Parameters.AddWithValue("confirmed", (object?)job.ConfirmedAt ?? DBNull.Value);
        command.Parameters.AddWithValue("next", (object?)job.NextRunAt ?? DBNull.Value);
        command.Parameters.AddWithValue("last", (object?)job.LastRunAt ?? DBNull.Value);
        command.Parameters.AddWithValue("last_status", (object?)job.LastRunStatus ?? DBNull.Value);
    }

    private static ScheduledJob Read(NpgsqlDataReader reader) => new(
        reader.GetGuid(0), reader.GetString(1), reader.GetString(2),
        Enum.Parse<ScheduledJobKind>(reader.GetString(3)), reader.GetString(4),
        JsonSerializer.Deserialize<string[]>(reader.GetString(5)) ?? [], reader.GetString(6),
        reader.GetString(7), Enum.Parse<ScheduledJobStatus>(reader.GetString(8)),
        reader.GetFieldValue<DateTimeOffset>(9),
        reader.IsDBNull(10) ? null : reader.GetFieldValue<DateTimeOffset>(10),
        reader.IsDBNull(11) ? null : reader.GetFieldValue<DateTimeOffset>(11),
        reader.IsDBNull(12) ? null : reader.GetFieldValue<DateTimeOffset>(12),
        reader.IsDBNull(13) ? null : reader.GetString(13));
}
